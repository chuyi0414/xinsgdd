using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// MainUIForm 产出物视图分部类。
/// 负责产出物掉落按钮的创建、点击收取与对象池回收。
/// </summary>
public partial class MainUIForm
{
    /// <summary>
    /// 待展示的产出物掉落请求。
    /// 当主界面不在宠物页时，先缓存世界坐标，等回到中页再统一回放。
    /// </summary>
    private struct PendingProduceDropRequest
    {
        /// <summary>
        /// 产出物出生动画起点世界坐标。
        /// </summary>
        public Vector3 StartWorldPos;

        /// <summary>
        /// 产出物出生动画终点世界坐标。
        /// </summary>
        public Vector3 EndWorldPos;

        /// <summary>
        /// 本次产出物 Code。
        /// </summary>
        public string ProduceCode;
    }

    /// <summary>
    /// 产出物掉落容器根节点名称。
    /// 若预制体未提前放置该节点，则运行时在 BJ 页面根节点下动态创建。
    /// </summary>
    private const string ProduceDropRootName = "ProduceDropRoot";

    /// <summary>
    /// 产出物掉落动画世界坐标偏移范围：X 最小值。
    /// </summary>
    private const float ProduceOffsetMinX = -1.5f;

    /// <summary>
    /// 产出物掉落动画世界坐标偏移范围：X 最大值。
    /// </summary>
    private const float ProduceOffsetMaxX = 0.5f;

    /// <summary>
    /// 产出物掉落动画世界坐标偏移范围：Y 最小值。
    /// </summary>
    private const float ProduceOffsetMinY = -1f;

    /// <summary>
    /// 产出物掉落动画世界坐标偏移范围：Y 最大值。
    /// </summary>
    private const float ProduceOffsetMaxY = -0.5f;

    /// <summary>
    /// 产出物 UI 容器。
    /// </summary>
    private RectTransform _produceDropRoot;

    /// <summary>
    /// 产出物奖励层的 CanvasGroup。
    /// 用来在翻页前后统一隐藏/显示整层产出物，而不是回收现有按钮。
    /// </summary>
    private CanvasGroup _produceDropCanvasGroup;

    /// <summary>
    /// 当前场上存活的产出物按钮列表。
    /// </summary>
    private readonly List<OutputProduceItem> _activeProduceItems = new List<OutputProduceItem>();

    /// <summary>
    /// 复用的产出物按钮池。
    /// </summary>
    private readonly Stack<OutputProduceItem> _produceItemPool = new Stack<OutputProduceItem>();

    /// <summary>
    /// 产出物容器是否由运行时动态创建。
    /// </summary>
    private bool _isRuntimeCreatedProduceDropRoot;

    /// <summary>
    /// 待展示的产出物掉落请求列表。
    /// </summary>
    private readonly List<PendingProduceDropRequest> _pendingProduceDropRequests = new List<PendingProduceDropRequest>(8);

    /// <summary>
    /// 是否已经订阅产出物相关事件。
    /// </summary>
    private bool _isProduceEventSubscribed;

    /// <summary>
    /// 是否已经输出过缺失产出物预制体日志。
    /// </summary>
    private bool _hasLoggedMissingOutputProducePrefab;

    // ──────────────────────────────────────────────────────────
    //  生命周期（由 MainUIForm.cs 调用）
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 产出物视图初始化。
    /// </summary>
    private void InitializeProduceView()
    {
        EnsureProduceDropRoot();
        EnsureProduceEventSubscription();
    }

    /// <summary>
    /// 产出物视图打开。
    /// </summary>
    private void OpenProduceView()
    {
        EnsureProduceDropRoot();
        UpdateProduceRewardUiVisibility(CanPresentPetRewardDropsNow());
    }

    /// <summary>
    /// 产出物视图关闭。
    /// </summary>
    private void CloseProduceView()
    {
        ReleaseAllActiveProduceItems();
        ClearPendingProduceDropRequests();
    }

    /// <summary>
    /// 产出物视图销毁。
    /// </summary>
    private void DestroyProduceView()
    {
        ReleaseProduceEventSubscription();
        DestroyAllProduceItems();
        ClearPendingProduceDropRequests();
        _produceDropCanvasGroup = null;
        _produceDropRoot = null;
    }

    // ──────────────────────────────────────────────────────────
    //  事件订阅
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 确保已订阅产出物相关事件。
    /// </summary>
    private void EnsureProduceEventSubscription()
    {
        if (_isProduceEventSubscribed)
        {
            return;
        }

        if (GameEntry.PetDiningOrders != null)
        {
            GameEntry.PetDiningOrders.ProduceDropRequested += OnProduceDropRequested;
        }

        _isProduceEventSubscribed = true;
    }

    /// <summary>
    /// 释放产出物相关事件订阅。
    /// </summary>
    private void ReleaseProduceEventSubscription()
    {
        if (!_isProduceEventSubscribed)
        {
            return;
        }

        if (GameEntry.PetDiningOrders != null)
        {
            GameEntry.PetDiningOrders.ProduceDropRequested -= OnProduceDropRequested;
        }

        _isProduceEventSubscribed = false;
    }

    // ──────────────────────────────────────────────────────────
    //  事件回调
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 产出物掉落事件回调。
    /// 在宠物实体附近生成一个可点击的产出物按钮。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    /// <param name="produceCode">产出物 Code。</param>
    private void OnProduceDropRequested(int petInstanceId, string produceCode)
    {
        if (string.IsNullOrWhiteSpace(produceCode))
        {
            return;
        }

        if (!TryBuildPetRewardWorldPositions(
                petInstanceId,
                ProduceOffsetMinX,
                ProduceOffsetMaxX,
                ProduceOffsetMinY,
                ProduceOffsetMaxY,
                out Vector3 startWorldPos,
                out Vector3 endWorldPos))
        {
            return;
        }

        if (!CanPresentPetRewardDropsNow() || !TryPresentProduceDrop(startWorldPos, endWorldPos, produceCode))
        {
            EnqueuePendingProduceDropRequest(startWorldPos, endWorldPos, produceCode);
        }
    }

    /// <summary>
    /// 产出物按钮点击收取回调。
    /// 点击后立即写入运行时库存并回收按钮。
    /// </summary>
    /// <param name="produceItem">被点击的产出物按钮。</param>
    private void OnOutputProduceItemCollected(OutputProduceItem produceItem)
    {
        if (produceItem == null)
        {
            return;
        }

        if (GameEntry.Fruits != null && !string.IsNullOrWhiteSpace(produceItem.ProduceCode))
        {
            GameEntry.Fruits.AddProduce(produceItem.ProduceCode);
        }

        ReleaseOutputProduceItem(produceItem);
    }

    // ──────────────────────────────────────────────────────────
    //  UI 辅助
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 获取产出物 UI 投影所需的 UI 相机。
    /// </summary>
    /// <returns>可用于屏幕点转换的 UI 相机。</returns>
    private Camera GetProduceUICamera()
    {
        if (_rootCanvas != null && _rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            return null;
        }

        return GetMarkerUICamera();
    }

    /// <summary>
    /// 将世界坐标转换为产出物容器下的 UI 局部坐标。
    /// </summary>
    /// <param name="worldPos">世界坐标。</param>
    /// <param name="worldCamera">场地世界相机。</param>
    /// <param name="uiCamera">UI 相机；Overlay 模式下为 null。</param>
    /// <returns>产出物容器下的局部坐标。</returns>
    private Vector2 WorldToProduceLocalPos(Vector3 worldPos, Camera worldCamera, Camera uiCamera)
    {
        Vector3 screenPoint = worldCamera.WorldToScreenPoint(worldPos);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_produceDropRoot, screenPoint, uiCamera, out Vector2 localPoint);
        return localPoint;
    }

    /// <summary>
    /// 根据当前分页状态刷新产出物奖励层的显隐。
    /// 这里只改整层 CanvasGroup，不中断现有产出物按钮的生命周期。
    /// </summary>
    /// <param name="isVisible">true 显示；false 隐藏。</param>
    private void UpdateProduceRewardUiVisibility(bool isVisible)
    {
        EnsureProduceDropCanvasGroup();
        if (_produceDropCanvasGroup == null)
        {
            return;
        }

        float targetAlpha = isVisible ? 1f : 0f;
        if (!Mathf.Approximately(_produceDropCanvasGroup.alpha, targetAlpha))
        {
            _produceDropCanvasGroup.alpha = targetAlpha;
        }

        _produceDropCanvasGroup.interactable = isVisible;
        _produceDropCanvasGroup.blocksRaycasts = isVisible;
    }

    /// <summary>
    /// 尝试把一条产出物掉落请求真正呈现到当前 UI 上。
    /// </summary>
    /// <param name="startWorldPos">产出物起点世界坐标。</param>
    /// <param name="endWorldPos">产出物终点世界坐标。</param>
    /// <param name="produceCode">产出物 Code。</param>
    /// <returns>成功创建返回 true；否则返回 false，调用方应继续保留缓存。</returns>
    private bool TryPresentProduceDrop(Vector3 startWorldPos, Vector3 endWorldPos, string produceCode)
    {
        if (string.IsNullOrWhiteSpace(produceCode))
        {
            return true;
        }

        Camera worldCamera = GetPlayfieldWorldCamera();
        if (worldCamera == null)
        {
            return false;
        }

        EnsureProduceDropRoot();
        if (_produceDropRoot == null)
        {
            return false;
        }

        Camera uiCamera = GetProduceUICamera();
        Vector2 startLocalPos = WorldToProduceLocalPos(startWorldPos, worldCamera, uiCamera);
        Vector2 endLocalPos = WorldToProduceLocalPos(endWorldPos, worldCamera, uiCamera);

        OutputProduceItem produceItem = AcquireOutputProduceItem();
        if (produceItem == null)
        {
            return false;
        }

        produceItem.Bind(produceCode, OnOutputProduceItemCollected);
        produceItem.PlaySpawnAnimation(startLocalPos, endLocalPos);
        _activeProduceItems.Add(produceItem);
        return true;
    }

    /// <summary>
    /// 缓存一条待展示的产出物掉落请求。
    /// </summary>
    /// <param name="startWorldPos">产出物起点世界坐标。</param>
    /// <param name="endWorldPos">产出物终点世界坐标。</param>
    /// <param name="produceCode">产出物 Code。</param>
    private void EnqueuePendingProduceDropRequest(Vector3 startWorldPos, Vector3 endWorldPos, string produceCode)
    {
        if (string.IsNullOrWhiteSpace(produceCode))
        {
            return;
        }

        PendingProduceDropRequest pendingRequest = new PendingProduceDropRequest
        {
            StartWorldPos = startWorldPos,
            EndWorldPos = endWorldPos,
            ProduceCode = produceCode,
        };
        _pendingProduceDropRequests.Add(pendingRequest);
    }

    /// <summary>
    /// 回放所有待展示的产出物掉落请求。
    /// </summary>
    private void FlushPendingProduceDropRequests()
    {
        if (_pendingProduceDropRequests.Count <= 0 || !CanPresentPetRewardDropsNow())
        {
            return;
        }

        for (int i = 0; i < _pendingProduceDropRequests.Count;)
        {
            PendingProduceDropRequest pendingRequest = _pendingProduceDropRequests[i];
            if (!TryPresentProduceDrop(pendingRequest.StartWorldPos, pendingRequest.EndWorldPos, pendingRequest.ProduceCode))
            {
                break;
            }

            _pendingProduceDropRequests.RemoveAt(i);
        }
    }

    /// <summary>
    /// 清空所有待展示的产出物掉落请求。
    /// </summary>
    private void ClearPendingProduceDropRequests()
    {
        _pendingProduceDropRequests.Clear();
    }

    // ──────────────────────────────────────────────────────────
    //  产出物 UI 容器 & 池化
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 确保产出物 UI 容器存在。
    /// </summary>
    private void EnsureProduceDropRoot()
    {
        // 【避坑】父节点必须挂 _pageViewport 而非 _pageCenter！
        // _pageCenter 会随翻页移动，导致产出物 anchoredPosition 被分页偏移污染，
        // 出现"翻页后产出物位置偏移"的 Bug。
        RectTransform overlayParent = _pageViewport;
        if (_produceDropRoot != null || overlayParent == null)
        {
            return;
        }

        Transform existingRoot = overlayParent.Find(ProduceDropRootName);
        if (existingRoot != null)
        {
            _produceDropRoot = existingRoot as RectTransform;
            return;
        }

        GameObject rootObject = new GameObject(ProduceDropRootName, typeof(RectTransform));
        RectTransform rootRectTransform = rootObject.GetComponent<RectTransform>();
        rootRectTransform.SetParent(overlayParent, false);
        rootRectTransform.anchorMin = Vector2.zero;
        rootRectTransform.anchorMax = Vector2.one;
        rootRectTransform.sizeDelta = Vector2.zero;
        rootRectTransform.anchoredPosition = Vector2.zero;
        rootRectTransform.SetAsLastSibling();

        _produceDropRoot = rootRectTransform;
        _isRuntimeCreatedProduceDropRoot = true;
    }

    /// <summary>
    /// 确保产出物奖励层存在 CanvasGroup，便于翻页前后整层控制显隐。
    /// </summary>
    private void EnsureProduceDropCanvasGroup()
    {
        EnsureProduceDropRoot();
        if (_produceDropRoot == null || _produceDropCanvasGroup != null)
        {
            return;
        }

        _produceDropCanvasGroup = _produceDropRoot.GetComponent<CanvasGroup>();
        if (_produceDropCanvasGroup == null)
        {
            _produceDropCanvasGroup = _produceDropRoot.gameObject.AddComponent<CanvasGroup>();
        }
    }

    /// <summary>
    /// 从池中获取或实例化一个产出物按钮。
    /// </summary>
    /// <returns>可用的产出物按钮；若资源不可用则返回 null。</returns>
    private OutputProduceItem AcquireOutputProduceItem()
    {
        EnsureProduceDropRoot();
        if (_produceDropRoot == null)
        {
            return null;
        }

        while (_produceItemPool.Count > 0)
        {
            OutputProduceItem pooledItem = _produceItemPool.Pop();
            if (pooledItem == null)
            {
                continue;
            }

            RectTransform pooledRectTransform = pooledItem.CachedRectTransform;
            if (pooledRectTransform != null)
            {
                pooledRectTransform.SetParent(_produceDropRoot, false);
            }

            pooledItem.gameObject.SetActive(true);
            return pooledItem;
        }

        if (GameEntry.GameAssets == null
            || !GameEntry.GameAssets.TryGetOutputProducePrefab(out GameObject outputProducePrefab)
            || outputProducePrefab == null)
        {
            if (!_hasLoggedMissingOutputProducePrefab)
            {
                _hasLoggedMissingOutputProducePrefab = true;
                Log.Warning("MainUIForm 无法创建产出物按钮，预制体缓存缺失。");
            }

            return null;
        }

        GameObject produceObject = Object.Instantiate(outputProducePrefab, _produceDropRoot, false);
        OutputProduceItem produceItem = produceObject.GetComponent<OutputProduceItem>();
        if (produceItem == null)
        {
            produceItem = produceObject.AddComponent<OutputProduceItem>();
        }

        return produceItem;
    }

    /// <summary>
    /// 回收指定产出物按钮到池中。
    /// </summary>
    /// <param name="produceItem">要回收的产出物按钮。</param>
    private void ReleaseOutputProduceItem(OutputProduceItem produceItem)
    {
        if (produceItem == null)
        {
            return;
        }

        _activeProduceItems.Remove(produceItem);
        produceItem.gameObject.SetActive(false);
        _produceItemPool.Push(produceItem);
    }

    /// <summary>
    /// 回收全部场上激活的产出物按钮。
    /// </summary>
    private void ReleaseAllActiveProduceItems()
    {
        for (int i = _activeProduceItems.Count - 1; i >= 0; i--)
        {
            OutputProduceItem produceItem = _activeProduceItems[i];
            if (produceItem != null)
            {
                produceItem.gameObject.SetActive(false);
                _produceItemPool.Push(produceItem);
            }
        }

        _activeProduceItems.Clear();
    }

    /// <summary>
    /// 销毁全部产出物按钮与运行时创建的容器。
    /// </summary>
    private void DestroyAllProduceItems()
    {
        for (int i = 0; i < _activeProduceItems.Count; i++)
        {
            if (_activeProduceItems[i] != null)
            {
                Object.Destroy(_activeProduceItems[i].gameObject);
            }
        }

        _activeProduceItems.Clear();

        while (_produceItemPool.Count > 0)
        {
            OutputProduceItem pooledItem = _produceItemPool.Pop();
            if (pooledItem != null)
            {
                Object.Destroy(pooledItem.gameObject);
            }
        }

        if (_isRuntimeCreatedProduceDropRoot && _produceDropRoot != null)
        {
            Object.Destroy(_produceDropRoot.gameObject);
        }

        _isRuntimeCreatedProduceDropRoot = false;
    }
}
