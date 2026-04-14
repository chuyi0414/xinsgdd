using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// 宠物期望食物气泡项。
/// 只负责缓存组件引用和更新显示内容，不自行做任何逐帧逻辑。
/// </summary>
public sealed class PetFoodBubbleItem : MonoBehaviour
{
    /// <summary>
    /// 自身 RectTransform 缓存。
    /// </summary>
    private RectTransform _cachedRectTransform;

    /// <summary>
    /// 根节点按钮组件缓存。
    /// 当前仅作为展示容器，不参与交互。
    /// </summary>
    private Button _button;

    /// <summary>
    /// 根节点 Image 组件缓存。
    /// 用于关闭射线检测，避免遮挡主界面操作。
    /// </summary>
    private Image _rootImage;

    /// <summary>
    /// 期望水果图标组件缓存。
    /// </summary>
    private Image _fruitImage;

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
    /// 当前是否已经绑定到有效的水果图标。
    /// 用于让外层在资源缺失时直接隐藏气泡，而不是显示一个空框。
    /// </summary>
    public bool HasFruitSprite { get; private set; }

    /// <summary>
    /// 当前绑定的宠物实例 Id。
    /// 点击时会把这个 Id 回传给外层处理。
    /// </summary>
    private int _petInstanceId;

    /// <summary>
    /// 当前点击回调。
    /// 由外层统一转发到全局点餐生产组件。
    /// </summary>
    private Action<int> _onClick;

    /// <summary>
    /// 初始化时缓存组件并应用只读展示配置。
    /// </summary>
    private void Awake()
    {
        CacheReferences();
        ApplyInteractionState(false);

        if (_button != null)
        {
            _button.onClick.RemoveListener(OnButtonClicked);
            _button.onClick.AddListener(OnButtonClicked);
        }
    }

    /// <summary>
    /// 销毁时移除按钮监听，避免重复挂接。
    /// </summary>
    private void OnDestroy()
    {
        if (_button != null)
        {
            _button.onClick.RemoveListener(OnButtonClicked);
        }
    }

    /// <summary>
    /// 绑定当前气泡应该显示的水果图标与点击行为。
    /// </summary>
    /// <param name="petInstanceId">当前绑定的宠物实例 Id。</param>
    /// <param name="fruitSprite">水果图标资源。</param>
    /// <param name="onClick">点击回调。</param>
    public void Bind(int petInstanceId, Sprite fruitSprite, Action<int> onClick)
    {
        CacheReferences();
        _petInstanceId = petInstanceId;
        _onClick = onClick;

        if (_fruitImage == null)
        {
            HasFruitSprite = false;
            ApplyInteractionState(false);
            return;
        }

        _fruitImage.sprite = fruitSprite;
        _fruitImage.enabled = fruitSprite != null;
        _fruitImage.preserveAspect = true;
        HasFruitSprite = fruitSprite != null;
        ApplyInteractionState(HasFruitSprite && _onClick != null && _petInstanceId > 0);
    }

    /// <summary>
    /// 统一控制气泡项显隐。
    /// </summary>
    /// <param name="isVisible">是否显示。</param>
    public void SetVisible(bool isVisible)
    {
        if (gameObject.activeSelf == isVisible)
        {
            return;
        }

        gameObject.SetActive(isVisible);
    }

    /// <summary>
    /// 缓存当前气泡项所需的所有组件引用。
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

        if (_fruitImage == null)
        {
            Transform fruitTransform = transform.Find("ImgFruit");
            if (fruitTransform != null)
            {
                _fruitImage = fruitTransform.GetComponent<Image>();
            }
        }
    }

    /// <summary>
    /// 应用当前气泡项的交互状态。
    /// </summary>
    /// <param name="isInteractable">当前是否允许点击。</param>
    private void ApplyInteractionState(bool isInteractable)
    {
        if (_button != null)
        {
            _button.interactable = isInteractable;
        }

        if (_rootImage != null)
        {
            _rootImage.raycastTarget = isInteractable;
        }

        if (_fruitImage != null)
        {
            _fruitImage.raycastTarget = false;
        }
    }

    /// <summary>
    /// 根按钮点击回调。
    /// </summary>
    private void OnButtonClicked()
    {
        if (_onClick == null || _petInstanceId <= 0)
        {
            return;
        }

        _onClick.Invoke(_petInstanceId);
    }
}
