using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;
using TMPro;

/// <summary>
/// MainUIForm 金币视图分部类。
/// 负责金币总额显示、金币掉落 UI 的创建/动画/回收。
/// </summary>
public partial class MainUIForm
{
    /// <summary>
    /// 金币掉落容器根节点名称。
    /// 若预制体未提前放置该节点，则运行时按此名称在 BJ 页面根节点下动态创建。
    /// </summary>
    private const string GoldCoinRootName = "GoldCoinRoot";

    /// <summary>
    /// 金币掉落动画世界坐标偏移范围：X 最小值。
    /// </summary>
    private const float CoinOffsetMinX = -1.5f;

    /// <summary>
    /// 金币掉落动画世界坐标偏移范围：X 最大值。
    /// </summary>
    private const float CoinOffsetMaxX = 0.5f;

    /// <summary>
    /// 金币掉落动画世界坐标偏移范围：Y 最小值。
    /// </summary>
    private const float CoinOffsetMinY = -1f;

    /// <summary>
    /// 金币掉落动画世界坐标偏移范围：Y 最大值。
    /// </summary>
    private const float CoinOffsetMaxY = -0.5f;

    /// <summary>
    /// 当前金币总额文本（对应 GoJB/TxtJB）。
    /// 用户在 Inspector 自行拖拽赋值。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _goldText;

    /// <summary>
    /// 金币 UI 容器。
    /// 屏幕空间金币统一挂在这里。
    /// </summary>
    private RectTransform _goldCoinRoot;

    /// <summary>
    /// 当前场上存活的金币 UI 列表。
    /// </summary>
    private readonly List<GoldCoinItem> _activeGoldCoins = new List<GoldCoinItem>();

    /// <summary>
    /// 复用的金币 UI 池。
    /// </summary>
    private readonly Stack<GoldCoinItem> _goldCoinPool = new Stack<GoldCoinItem>();

    /// <summary>
    /// 当前场上存活的金币点击提示 Toast 列表。
    /// </summary>
    private readonly List<GoldCoinToastItem> _activeGoldCoinToasts = new List<GoldCoinToastItem>();

    /// <summary>
    /// 复用的金币点击提示 Toast 池。
    /// </summary>
    private readonly Stack<GoldCoinToastItem> _goldCoinToastPool = new Stack<GoldCoinToastItem>();

    /// <summary>
    /// 金币容器是否由运行时动态创建。
    /// </summary>
    private bool _isRuntimeCreatedGoldCoinRoot;

    /// <summary>
    /// 是否已经输出过缺失金币预制体日志。
    /// </summary>
    private bool _hasLoggedMissingGoldCoinPrefab;

    /// <summary>
    /// 是否已经输出过缺失金币点击提示 Toast 预制体日志。
    /// </summary>
    private bool _hasLoggedMissingGoldCoinToastPrefab;

    /// <summary>
    /// 是否已经订阅金币相关事件。
    /// </summary>
    private bool _isGoldEventSubscribed;

    // ──────────────────────────────────────────────────────────
    //  生命周期（由 MainUIForm.cs 调用）
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 金币视图初始化：订阅事件、创建容器、刷新文本。
    /// </summary>
    private void InitializeGoldView()
    {
        EnsureGoldCoinRoot();
        EnsureGoldEventSubscription();
        RefreshGoldText();
    }

    /// <summary>
    /// 金币视图打开：刷新文本。
    /// </summary>
    private void OpenGoldView()
    {
        RefreshGoldText();
    }

    /// <summary>
    /// 金币视图关闭：回收全部场上金币 UI。
    /// </summary>
    private void CloseGoldView()
    {
        ReleaseAllActiveGoldCoinToasts();
        ReleaseAllActiveGoldCoins();
    }

    /// <summary>
    /// 金币视图销毁：取消订阅、销毁全部金币 UI。
    /// </summary>
    private void DestroyGoldView()
    {
        ReleaseGoldEventSubscription();
        DestroyAllGoldCoinToasts();
        DestroyAllGoldCoins();
        _goldCoinRoot = null;
    }

    // ──────────────────────────────────────────────────────────
    //  事件订阅
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 确保已订阅金币相关事件。
    /// </summary>
    private void EnsureGoldEventSubscription()
    {
        if (_isGoldEventSubscribed)
        {
            return;
        }

        if (GameEntry.PetDiningOrders != null)
        {
            GameEntry.PetDiningOrders.CoinDropRequested += OnCoinDropRequested;
        }

        if (GameEntry.Fruits != null)
        {
            GameEntry.Fruits.GoldChanged += OnGoldChanged;
        }

        _isGoldEventSubscribed = true;
    }

    /// <summary>
    /// 释放金币相关事件订阅。
    /// </summary>
    private void ReleaseGoldEventSubscription()
    {
        if (!_isGoldEventSubscribed)
        {
            return;
        }

        if (GameEntry.PetDiningOrders != null)
        {
            GameEntry.PetDiningOrders.CoinDropRequested -= OnCoinDropRequested;
        }

        if (GameEntry.Fruits != null)
        {
            GameEntry.Fruits.GoldChanged -= OnGoldChanged;
        }

        _isGoldEventSubscribed = false;
    }

    // ──────────────────────────────────────────────────────────
    //  事件回调
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 金币掉落事件回调。
    /// 在宠物实体的世界坐标处创建金币 UI，并播放偏移动画。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    /// <param name="coinAmount">金币数量。</param>
    private void OnCoinDropRequested(int petInstanceId, int coinAmount)
    {
        if (coinAmount <= 0 || GameEntry.PlayfieldEntities == null)
        {
            return;
        }

        // 获取宠物实体的世界坐标
        if (!GameEntry.PlayfieldEntities.TryGetPetEntityLogic(petInstanceId, out PetEntityLogic petEntityLogic)
            || petEntityLogic == null)
        {
            return;
        }

        Camera worldCamera = GetPlayfieldWorldCamera();
        if (worldCamera == null)
        {
            return;
        }

        // 宠物实体世界位置
        Vector3 petWorldPos = petEntityLogic.CachedTransform.position;

        // 随机世界偏移目标点
        float offsetX = UnityEngine.Random.Range(CoinOffsetMinX, CoinOffsetMaxX);
        float offsetY = UnityEngine.Random.Range(CoinOffsetMinY, CoinOffsetMaxY);
        Vector3 targetWorldPos = petWorldPos + new Vector3(offsetX, offsetY, 0f);

        // 世界坐标 → 屏幕坐标 → UI 局部坐标
        EnsureGoldCoinRoot();
        if (_goldCoinRoot == null)
        {
            return;
        }

        Camera uiCamera = GetGoldCoinUICamera();

        Vector2 startLocalPos = WorldToGoldCoinLocalPos(petWorldPos, worldCamera, uiCamera);
        Vector2 endLocalPos = WorldToGoldCoinLocalPos(targetWorldPos, worldCamera, uiCamera);

        // 创建金币 UI 并播放动画
        GoldCoinItem coinItem = AcquireGoldCoinItem();
        if (coinItem == null)
        {
            return;
        }

        coinItem.Bind(coinAmount, OnGoldCoinFlyComplete, OnGoldCoinClicked);
        coinItem.PlaySpawnAnimation(startLocalPos, endLocalPos);
        _activeGoldCoins.Add(coinItem);
    }

    /// <summary>
    /// 金币总额变化回调：刷新金币文本。
    /// </summary>
    /// <param name="newGold">最新金币总额。</param>
    private void OnGoldChanged(int newGold)
    {
        RefreshGoldText();
    }

    /// <summary>
    /// 金币 UI 按钮点击回调（点击瞬间触发）。
    /// 计算 TxtJB 在金币容器下的局部坐标，启动飞向目标动画。
    /// </summary>
    /// <param name="coinItem">被点击的金币项。</param>
    private void OnGoldCoinClicked(GoldCoinItem coinItem)
    {
        if (coinItem == null)
        {
            return;
        }

        // 点击瞬间先在金币当前位置生成一个上飘渐隐的 Toast，
        // 然后金币本体继续沿原有逻辑飞向顶部金币栏。
        ShowGoldCoinToast(coinItem);

        // 将 TxtJB 的屏幕坐标转到金币容器下的局部坐标
        Vector2 targetLocalPos = GetGoldTextLocalPosInCoinRoot();
        coinItem.PlayFlyToTarget(targetLocalPos);
    }

    /// <summary>
    /// 金币飞向目标动画完成回调。
    /// 此时才真正增加金币并回收 UI。
    /// </summary>
    /// <param name="coinItem">完成飞行的金币项。</param>
    private void OnGoldCoinFlyComplete(GoldCoinItem coinItem)
    {
        if (coinItem == null)
        {
            return;
        }

        if (GameEntry.Fruits != null)
        {
            GameEntry.Fruits.AddGold(coinItem.CoinAmount);
        }

        ReleaseGoldCoinItem(coinItem);
    }

    /// <summary>
    /// 获取 TxtJB 在金币容器下的局部坐标。
    /// 用于作为金币飞向目标动画的终点。
    /// </summary>
    /// <returns>TxtJB 在 _goldCoinRoot 下的局部坐标。</returns>
    private Vector2 GetGoldTextLocalPosInCoinRoot()
    {
        if (_goldText == null || _goldCoinRoot == null)
        {
            return Vector2.zero;
        }

        // TxtJB 的世界坐标 → 屏幕坐标 → _goldCoinRoot 下的局部坐标
        Camera uiCamera = GetGoldCoinUICamera();
        Vector3 worldPos = _goldText.rectTransform.position;
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(uiCamera, worldPos);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_goldCoinRoot, screenPos, uiCamera, out Vector2 localPos);
        return localPos;
    }

    // ──────────────────────────────────────────────────────────
    //  UI 辅助
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 刷新金币文本显示。
    /// </summary>
    private void RefreshGoldText()
    {
        if (_goldText == null)
        {
            return;
        }

        int currentGold = GameEntry.Fruits != null ? GameEntry.Fruits.CurrentGold : 0;
        _goldText.SetText(currentGold.ToString());
    }

    /// <summary>
    /// 将世界坐标转换为金币容器下的 UI 局部坐标。
    /// </summary>
    /// <param name="worldPos">世界坐标。</param>
    /// <param name="worldCamera">场地世界相机。</param>
    /// <param name="uiCamera">UI 相机；Overlay 模式下为 null。</param>
    /// <returns>金币容器下的局部坐标。</returns>
    private Vector2 WorldToGoldCoinLocalPos(Vector3 worldPos, Camera worldCamera, Camera uiCamera)
    {
        Vector3 screenPoint = worldCamera.WorldToScreenPoint(worldPos);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_goldCoinRoot, screenPoint, uiCamera, out Vector2 localPoint);
        return localPoint;
    }

    /// <summary>
    /// 获取金币 UI 投影所需的 UI 相机。
    /// Screen Space Overlay 下必须传 null。
    /// </summary>
    /// <returns>可用于屏幕点转换的 UI 相机。</returns>
    private Camera GetGoldCoinUICamera()
    {
        if (_rootCanvas != null && _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        return GetMarkerUICamera();
    }

    /// <summary>
    /// 在金币当前位置显示一次点击提示 Toast。
    /// </summary>
    /// <param name="coinItem">被点击的金币项。</param>
    private void ShowGoldCoinToast(GoldCoinItem coinItem)
    {
        if (coinItem == null || coinItem.CoinAmount <= 0)
        {
            return;
        }

        GoldCoinToastItem toastItem = AcquireGoldCoinToastItem();
        if (toastItem == null)
        {
            return;
        }

        Vector2 startLocalPos = Vector2.zero;
        RectTransform coinRectTransform = coinItem.CachedRectTransform;
        if (coinRectTransform != null)
        {
            startLocalPos = coinRectTransform.anchoredPosition;
        }

        toastItem.PlayToast(coinItem.CoinAmount, startLocalPos, OnGoldCoinToastComplete);
        _activeGoldCoinToasts.Add(toastItem);
    }

    // ──────────────────────────────────────────────────────────
    //  金币 UI / Toast 容器 & 池化
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 确保金币 UI 容器存在。
    /// </summary>
    private void EnsureGoldCoinRoot()
    {
        RectTransform overlayParent = _pageCenter != null ? _pageCenter : _pageViewport;
        if (_goldCoinRoot != null || overlayParent == null)
        {
            return;
        }

        Transform existingRoot = overlayParent.Find(GoldCoinRootName);
        if (existingRoot != null)
        {
            _goldCoinRoot = existingRoot as RectTransform;
            return;
        }

        GameObject rootObject = new GameObject(GoldCoinRootName, typeof(RectTransform));
        RectTransform rootRectTransform = rootObject.GetComponent<RectTransform>();
        rootRectTransform.SetParent(overlayParent, false);
        rootRectTransform.anchorMin = Vector2.zero;
        rootRectTransform.anchorMax = Vector2.one;
        rootRectTransform.sizeDelta = Vector2.zero;
        rootRectTransform.anchoredPosition = Vector2.zero;
        rootRectTransform.SetAsLastSibling();

        _goldCoinRoot = rootRectTransform;
        _isRuntimeCreatedGoldCoinRoot = true;
    }

    /// <summary>
    /// 从池中获取或实例化一个金币 UI 项。
    /// </summary>
    /// <returns>可用的金币项；若资源不可用则返回 null。</returns>
    private GoldCoinItem AcquireGoldCoinItem()
    {
        EnsureGoldCoinRoot();
        if (_goldCoinRoot == null)
        {
            return null;
        }

        while (_goldCoinPool.Count > 0)
        {
            GoldCoinItem pooledItem = _goldCoinPool.Pop();
            if (pooledItem == null)
            {
                continue;
            }

            RectTransform pooledRectTransform = pooledItem.CachedRectTransform;
            if (pooledRectTransform != null)
            {
                pooledRectTransform.SetParent(_goldCoinRoot, false);
            }

            pooledItem.gameObject.SetActive(true);
            return pooledItem;
        }

        if (GameEntry.GameAssets == null
            || !GameEntry.GameAssets.TryGetGoldCoinPrefab(out GameObject coinPrefab)
            || coinPrefab == null)
        {
            if (!_hasLoggedMissingGoldCoinPrefab)
            {
                _hasLoggedMissingGoldCoinPrefab = true;
                Log.Warning("MainUIForm 无法创建金币 UI，预制体缓存缺失。");
            }

            return null;
        }

        GameObject coinObject = Object.Instantiate(coinPrefab, _goldCoinRoot, false);
        GoldCoinItem coinItem = coinObject.GetComponent<GoldCoinItem>();
        if (coinItem == null)
        {
            coinItem = coinObject.AddComponent<GoldCoinItem>();
        }

        return coinItem;
    }

    /// <summary>
    /// 从池中获取或实例化一个金币点击提示 Toast 项。
    /// </summary>
    /// <returns>可用的 Toast 项；若资源不可用则返回 null。</returns>
    private GoldCoinToastItem AcquireGoldCoinToastItem()
    {
        EnsureGoldCoinRoot();
        if (_goldCoinRoot == null)
        {
            return null;
        }

        while (_goldCoinToastPool.Count > 0)
        {
            GoldCoinToastItem pooledItem = _goldCoinToastPool.Pop();
            if (pooledItem == null)
            {
                continue;
            }

            RectTransform pooledRectTransform = pooledItem.CachedRectTransform;
            if (pooledRectTransform != null)
            {
                pooledRectTransform.SetParent(_goldCoinRoot, false);
                pooledRectTransform.SetAsLastSibling();
            }

            pooledItem.gameObject.SetActive(true);
            return pooledItem;
        }

        if (GameEntry.GameAssets == null
            || !GameEntry.GameAssets.TryGetGoldCoinToastPrefab(out GameObject toastPrefab)
            || toastPrefab == null)
        {
            if (!_hasLoggedMissingGoldCoinToastPrefab)
            {
                _hasLoggedMissingGoldCoinToastPrefab = true;
                Log.Warning("MainUIForm 无法创建金币点击提示 Toast，预制体缓存缺失。");
            }

            return null;
        }

        GameObject toastObject = Object.Instantiate(toastPrefab, _goldCoinRoot, false);
        GoldCoinToastItem toastItem = toastObject.GetComponent<GoldCoinToastItem>();
        if (toastItem == null)
        {
            toastItem = toastObject.AddComponent<GoldCoinToastItem>();
        }

        RectTransform toastRectTransform = toastItem.CachedRectTransform;
        if (toastRectTransform != null)
        {
            toastRectTransform.SetAsLastSibling();
        }

        return toastItem;
    }

    /// <summary>
    /// 回收指定金币 UI 项到池中。
    /// </summary>
    /// <param name="coinItem">要回收的金币项。</param>
    private void ReleaseGoldCoinItem(GoldCoinItem coinItem)
    {
        if (coinItem == null)
        {
            return;
        }

        _activeGoldCoins.Remove(coinItem);
        coinItem.gameObject.SetActive(false);
        _goldCoinPool.Push(coinItem);
    }

    /// <summary>
    /// 金币点击提示 Toast 动画完成回调。
    /// </summary>
    /// <param name="toastItem">完成动画的 Toast 项。</param>
    private void OnGoldCoinToastComplete(GoldCoinToastItem toastItem)
    {
        ReleaseGoldCoinToastItem(toastItem);
    }

    /// <summary>
    /// 回收指定金币点击提示 Toast 到池中。
    /// </summary>
    /// <param name="toastItem">要回收的 Toast 项。</param>
    private void ReleaseGoldCoinToastItem(GoldCoinToastItem toastItem)
    {
        if (toastItem == null)
        {
            return;
        }

        _activeGoldCoinToasts.Remove(toastItem);
        toastItem.gameObject.SetActive(false);
        _goldCoinToastPool.Push(toastItem);
    }

    /// <summary>
    /// 回收全部场上激活的金币 UI。
    /// </summary>
    private void ReleaseAllActiveGoldCoins()
    {
        for (int i = _activeGoldCoins.Count - 1; i >= 0; i--)
        {
            GoldCoinItem coinItem = _activeGoldCoins[i];
            if (coinItem != null)
            {
                coinItem.gameObject.SetActive(false);
                _goldCoinPool.Push(coinItem);
            }
        }

        _activeGoldCoins.Clear();
    }

    /// <summary>
    /// 回收全部场上激活的金币点击提示 Toast。
    /// </summary>
    private void ReleaseAllActiveGoldCoinToasts()
    {
        for (int i = _activeGoldCoinToasts.Count - 1; i >= 0; i--)
        {
            GoldCoinToastItem toastItem = _activeGoldCoinToasts[i];
            if (toastItem != null)
            {
                toastItem.gameObject.SetActive(false);
                _goldCoinToastPool.Push(toastItem);
            }
        }

        _activeGoldCoinToasts.Clear();
    }

    /// <summary>
    /// 销毁全部金币 UI 项与运行时创建的容器。
    /// </summary>
    private void DestroyAllGoldCoins()
    {
        for (int i = 0; i < _activeGoldCoins.Count; i++)
        {
            if (_activeGoldCoins[i] != null)
            {
                Object.Destroy(_activeGoldCoins[i].gameObject);
            }
        }

        _activeGoldCoins.Clear();

        while (_goldCoinPool.Count > 0)
        {
            GoldCoinItem pooledItem = _goldCoinPool.Pop();
            if (pooledItem != null)
            {
                Object.Destroy(pooledItem.gameObject);
            }
        }

        if (_isRuntimeCreatedGoldCoinRoot && _goldCoinRoot != null)
        {
            Object.Destroy(_goldCoinRoot.gameObject);
        }

        _isRuntimeCreatedGoldCoinRoot = false;
    }

    /// <summary>
    /// 销毁全部金币点击提示 Toast。
    /// </summary>
    private void DestroyAllGoldCoinToasts()
    {
        for (int i = 0; i < _activeGoldCoinToasts.Count; i++)
        {
            if (_activeGoldCoinToasts[i] != null)
            {
                Object.Destroy(_activeGoldCoinToasts[i].gameObject);
            }
        }

        _activeGoldCoinToasts.Clear();

        while (_goldCoinToastPool.Count > 0)
        {
            GoldCoinToastItem pooledItem = _goldCoinToastPool.Pop();
            if (pooledItem != null)
            {
                Object.Destroy(pooledItem.gameObject);
            }
        }
    }
}
