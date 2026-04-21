using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 金币 UI 按钮项。
/// 挂在 GoldBtn.prefab 上，负责播放出生动画、点击后飞向目标位置动画、飞完后触发收取回调。
/// 出生动画期间也可以被点击。
/// </summary>
public sealed class GoldCoinItem : MonoBehaviour
{
    /// <summary>
    /// 出生动画时长（秒）。
    /// </summary>
    private const float SpawnAnimationDuration = 0.35f;

    /// <summary>
    /// 飞向目标（TxtJB）动画时长（秒）。
    /// </summary>
    private const float FlyToTargetDuration = 0.4f;

    /// <summary>
    /// 自身 RectTransform 缓存。
    /// </summary>
    private RectTransform _cachedRectTransform;

    /// <summary>
    /// 根节点按钮组件缓存。
    /// </summary>
    private Button _button;

    /// <summary>
    /// 根节点 Image 组件缓存。
    /// </summary>
    private Image _rootImage;

    /// <summary>
    /// 本次绑定的金币数量。
    /// </summary>
    private int _coinAmount;

    /// <summary>
    /// 飞行动画完成后的收取回调（由外层设置）。
    /// 飞完后由 OnFlyToTargetComplete 调用。
    /// </summary>
    private Action<GoldCoinItem> _onClick;

    /// <summary>
    /// 按钮点击时的即时回调（通知外层启动飞向目标动画）。
    /// 与 _onClick 分离：_onClicked 在点击瞬间触发，_onClick 在飞行动画结束后触发。
    /// </summary>
    private Action<GoldCoinItem> _onClicked;

    /// <summary>
    /// 当前正在执行的出生动画 Tween。
    /// </summary>
    private Tweener _spawnTween;

    /// <summary>
    /// 当前正在执行的飞向目标动画 Tween。
    /// </summary>
    private Tweener _flyTween;

    /// <summary>
    /// 是否正在执行飞向目标动画（此阶段禁止再次点击）。
    /// </summary>
    private bool _isFlying;

    /// <summary>
    /// 对外暴露的 RectTransform 缓存。
    /// </summary>
    public RectTransform CachedRectTransform
    {
        get
        {
            CacheReferences();
            return _cachedRectTransform;
        }
    }

    /// <summary>
    /// 本次绑定的金币数量，供外部收取时读取。
    /// </summary>
    public int CoinAmount => _coinAmount;

    /// <summary>
    /// 初始化时缓存组件。
    /// </summary>
    private void Awake()
    {
        CacheReferences();

        if (_button != null)
        {
            _button.onClick.RemoveListener(OnButtonClicked);
            _button.onClick.AddListener(OnButtonClicked);
        }
    }

    /// <summary>
    /// 销毁时清理按钮监听与动画。
    /// </summary>
    private void OnDestroy()
    {
        KillSpawnTween();
        KillFlyTween();

        if (_button != null)
        {
            _button.onClick.RemoveListener(OnButtonClicked);
        }
    }

    /// <summary>
    /// 绑定金币数量、飞行完成回调以及点击即时回调。
    /// </summary>
    /// <param name="coinAmount">本次金币数量。</param>
    /// <param name="onFlyComplete">飞行动画结束后的收取回调。</param>
    /// <param name="onClicked">按钮点击瞬间回调（外层启动飞向目标动画）。</param>
    public void Bind(int coinAmount, Action<GoldCoinItem> onFlyComplete, Action<GoldCoinItem> onClicked)
    {
        CacheReferences();
        _coinAmount = coinAmount;
        _onClick = onFlyComplete;
        _onClicked = onClicked;
        _isFlying = false;
        SetInteractable(true);
    }

    /// <summary>
    /// 播放出生动画：从起点移动到终点。
    /// 动画完成后自动启用点击交互。
    /// </summary>
    /// <param name="startLocalPos">起始 UI 局部坐标。</param>
    /// <param name="endLocalPos">目标 UI 局部坐标。</param>
    public void PlaySpawnAnimation(Vector2 startLocalPos, Vector2 endLocalPos)
    {
        CacheReferences();
        KillSpawnTween();

        if (_cachedRectTransform == null)
        {
            return;
        }

        _cachedRectTransform.anchoredPosition = startLocalPos;

        // 出生动画期间也可以点击
        SetInteractable(true);

        _spawnTween = _cachedRectTransform
            .DOAnchorPos(endLocalPos, SpawnAnimationDuration)
            .SetEase(Ease.OutBack)
            .SetUpdate(true)
            .OnComplete(OnSpawnAnimationComplete);
    }

    /// <summary>
    /// 出生动画自然完成（未被点击打断）。
    /// </summary>
    private void OnSpawnAnimationComplete()
    {
        _spawnTween = null;
    }

    /// <summary>
    /// 播放飞向目标位置（TxtJB）的收取动画。
    /// 飞行期间禁止再次点击，飞完后触发 _onClick 回调。
    /// </summary>
    /// <param name="targetLocalPos">目标 UI 局部坐标（相对于自身父容器）。</param>
    public void PlayFlyToTarget(Vector2 targetLocalPos)
    {
        CacheReferences();
        KillSpawnTween();
        KillFlyTween();

        _isFlying = true;
        SetInteractable(false);

        if (_cachedRectTransform == null)
        {
            OnFlyToTargetComplete();
            return;
        }

        _flyTween = _cachedRectTransform
            .DOAnchorPos(targetLocalPos, FlyToTargetDuration)
            .SetEase(Ease.InBack)
            .SetUpdate(true)
            .OnComplete(OnFlyToTargetComplete);
    }

    /// <summary>
    /// 飞向目标动画完成后触发收取回调。
    /// </summary>
    private void OnFlyToTargetComplete()
    {
        _flyTween = null;
        _isFlying = false;

        if (_onClick != null)
        {
            _onClick.Invoke(this);
        }
    }

    /// <summary>
    /// 按钮点击回调。
    /// 点击后不立即触发收取，而是通知外层开始飞向目标动画。
    /// </summary>
    private void OnButtonClicked()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        // 飞行中禁止重复点击
        if (_isFlying)
        {
            return;
        }

        if (_onClicked == null)
        {
            return;
        }

        _onClicked.Invoke(this);
    }

    /// <summary>
    /// 缓存所需组件引用。
    /// </summary>
    private void CacheReferences()
    {
        if (_cachedRectTransform == null)
        {
            _cachedRectTransform = transform as RectTransform;
        }

        if (_button == null)
        {
            _button = GetComponent<Button>();
        }

        if (_rootImage == null)
        {
            _rootImage = GetComponent<Image>();
        }
    }

    /// <summary>
    /// 统一控制按钮与射线检测的交互状态。
    /// </summary>
    /// <param name="interactable">是否允许交互。</param>
    private void SetInteractable(bool interactable)
    {
        if (_button != null)
        {
            _button.interactable = interactable;
        }

        if (_rootImage != null)
        {
            _rootImage.raycastTarget = interactable;
        }
    }

    /// <summary>
    /// 终止当前出生动画。
    /// </summary>
    private void KillSpawnTween()
    {
        if (_spawnTween == null)
        {
            return;
        }

        _spawnTween.Kill();
        _spawnTween = null;
    }

    /// <summary>
    /// 终止当前飞向目标动画。
    /// </summary>
    private void KillFlyTween()
    {
        if (_flyTween == null)
        {
            return;
        }

        _flyTween.Kill();
        _flyTween = null;
    }
}
