using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 产出物 UI 按钮项。
/// 挂在 OutputBtn.prefab 上，负责播放出生动画，
/// 并在点击时通知外层将产出物写入运行时库存。
/// </summary>
public sealed class OutputProduceItem : MonoBehaviour
{
    /// <summary>
    /// 出生动画时长（秒）。
    /// </summary>
    private const float SpawnAnimationDuration = 0.35f;

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
    /// 本次绑定的产出物 Code。
    /// </summary>
    private string _produceCode;

    /// <summary>
    /// 点击收取时触发的回调。
    /// </summary>
    private Action<OutputProduceItem> _onCollected;

    /// <summary>
    /// 当前正在执行的出生动画 Tween。
    /// </summary>
    private Tweener _spawnTween;

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
    /// 当前绑定的产出物 Code。
    /// </summary>
    public string ProduceCode => _produceCode;

    /// <summary>
    /// 初始化时缓存组件并注册点击事件。
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
    /// 失活时终止出生动画，避免回收到对象池后残留补间。
    /// </summary>
    private void OnDisable()
    {
        KillSpawnTween();
    }

    /// <summary>
    /// 销毁时清理按钮监听与补间。
    /// </summary>
    private void OnDestroy()
    {
        KillSpawnTween();

        if (_button != null)
        {
            _button.onClick.RemoveListener(OnButtonClicked);
        }
    }

    /// <summary>
    /// 绑定产出物 Code 与收取回调。
    /// </summary>
    /// <param name="produceCode">本次产出物 Code。</param>
    /// <param name="onCollected">点击收取回调。</param>
    public void Bind(string produceCode, Action<OutputProduceItem> onCollected)
    {
        CacheReferences();
        _produceCode = produceCode;
        _onCollected = onCollected;
        SetInteractable(true);
    }

    /// <summary>
    /// 播放出生动画：从起点移动到终点。
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
        SetInteractable(true);

        _spawnTween = _cachedRectTransform
            .DOAnchorPos(endLocalPos, SpawnAnimationDuration)
            .SetEase(Ease.OutBack)
            .SetUpdate(true)
            .OnComplete(OnSpawnAnimationComplete);
    }

    /// <summary>
    /// 出生动画自然完成。
    /// </summary>
    private void OnSpawnAnimationComplete()
    {
        _spawnTween = null;
    }

    /// <summary>
    /// 按钮点击回调。
    /// 点击后立即通知外层写入运行时库存。
    /// </summary>
    private void OnButtonClicked()
    {
        if (string.IsNullOrWhiteSpace(_produceCode) || _onCollected == null)
        {
            return;
        }

        SetInteractable(false);
        KillSpawnTween();
        _onCollected.Invoke(this);
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
}
