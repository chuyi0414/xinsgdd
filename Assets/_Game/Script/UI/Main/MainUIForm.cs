using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// Main界面
/// </summary>
public partial class MainUIForm : UIFormLogic
{
    /// <summary>
    /// 左页索引。
    /// </summary>
    private const int LeftPageIndex = -1;

    /// <summary>
    /// 中页索引。
    /// </summary>
    private const int CenterPageIndex = 0;

    /// <summary>
    /// 右页索引。
    /// </summary>
    private const int RightPageIndex = 1;

    // 左边按钮
    [SerializeField]
    private Button _btnLeft;
    // 右边按钮
    [SerializeField]
    private Button _btnRight;
    // 分页可视区域，未手动绑定时默认取 GoYiDong 的父节点。
    [SerializeField]
    private RectTransform _pageViewport;
    // 三页静态容器。
    [SerializeField]
    private RectTransform _goYiDong;
    // 背景三页静态容器，位于 Canvas_Back 下。
    private RectTransform _backgroundPageRoot;
    // 左页根节点。
    [SerializeField]
    private RectTransform _pageLeft;
    // 中页根节点。
    [SerializeField]
    private RectTransform _pageCenter;
    // 右页根节点。
    [SerializeField]
    private RectTransform _pageRight;
    // 切页动画时长。
    [SerializeField]
    private float _switchDuration = 0.2f;

    /// <summary>
    /// 当前切页补间。
    /// </summary>
    private Tweener _switchTween;

    /// <summary>
    /// 当前所在页索引。
    /// </summary>
    private int _currentPageIndex = CenterPageIndex;

    /// <summary>
    /// 上一次记录的可视区域宽度。
    /// </summary>
    private float _cachedViewportWidth = -1f;

    /// <summary>
    /// 当前是否处于切页动画中。
    /// </summary>
    private bool _isSwitching = false;

    /// <summary>
    /// 初始化主界面引用、事件和子视图。
    /// </summary>
    protected override void OnInit(object userData)
    {
        CacheReferences();
        if (_btnLeft != null)
        {
            _btnLeft.onClick.AddListener(OnBtnLeft);
        }
        else
        {
            Log.Warning("MainUIForm 找不到 BtnLeft。");
        }

        if (_btnRight != null)
        {
            _btnRight.onClick.AddListener(OnBtnRight);
        }
        else
        {
            Log.Warning("MainUIForm 找不到 BtnRight。");
        }

        base.OnInit(userData);
        RefreshPageLayout(true);
        UpdateButtonState();
        InitializeHatchView();
        InitializePetPlacementView();
        InitializeGoldView();
        InitializeProduceView();
        InitializeArchitectureView();
    }

    /// <summary>
    /// 打开主界面时刷新布局与子视图状态。
    /// </summary>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        RefreshPageLayout(true);
        UpdateButtonState();
        OpenHatchView();
        OpenPetPlacementView();
        OpenGoldView();
        OpenProduceView();
        OpenArchitectureView();
    }

    /// <summary>
    /// 关闭主界面时停止动画并关闭子视图。
    /// </summary>
    protected override void OnClose(bool isShutdown, object userData)
    {
        StopSwitchTween();
        CloseHatchView();
        ClosePetPlacementView();
        CloseGoldView();
        CloseProduceView();
        CloseArchitectureView();
        base.OnClose(isShutdown, userData);
    }

    /// <summary>
    /// 每帧更新子视图状态。
    /// </summary>
    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);
        UpdateHatchView();
        UpdatePetPlacementView();
    }


    /// <summary>
    /// 销毁时清理按钮监听和子视图资源。
    /// </summary>
    private void OnDestroy()
    {
        StopSwitchTween();

        if (_btnLeft != null)
        {
            _btnLeft.onClick.RemoveListener(OnBtnLeft);
        }

        if (_btnRight != null)
        {
            _btnRight.onClick.RemoveListener(OnBtnRight);
        }

        DestroyHatchView();
        DestroyPetPlacementView();
        DestroyGoldView();
        DestroyProduceView();
        DestroyArchitectureView();
    }

    /// <summary>
    /// 右边按钮点击逻辑
    /// </summary>
    private void OnBtnRight()
    {
        SwitchToPage(_currentPageIndex + 1);
    }

    /// <summary>
    /// 左边按钮点击逻辑
    /// </summary>
    private void OnBtnLeft()
    {
        SwitchToPage(_currentPageIndex - 1);
    }

    /// <summary>
    /// RectTransform 尺寸变化时，重新按当前可视宽度排版页面。
    /// </summary>
    private void OnRectTransformDimensionsChange()
    {
        if (!gameObject.activeInHierarchy)
        {
            return;
        }

        RefreshPageLayout(false);
    }

    /// <summary>
    /// 缓存界面上会用到的节点引用，未在 Inspector 绑定时按约定名称自动查找。
    /// </summary>
    private void CacheReferences()
    {
        if (_btnLeft == null)
        {
            Transform btnLeft = transform.Find("BtnLeft");
            if (btnLeft != null)
            {
                _btnLeft = btnLeft.GetComponent<Button>();
            }
        }

        if (_btnRight == null)
        {
            Transform btnRight = transform.Find("BtnRight");
            if (btnRight != null)
            {
                _btnRight = btnRight.GetComponent<Button>();
            }
        }

        if (_goYiDong == null)
        {
            _goYiDong = transform.Find("GoYiDong") as RectTransform;
        }

        if (_backgroundPageRoot == null)
        {
            Canvas rootCanvas = GetComponentInParent<Canvas>();
            Canvas topCanvas = rootCanvas != null ? rootCanvas.rootCanvas : null;
            Transform canvasBack = topCanvas != null ? topCanvas.transform.parent.Find("Canvas_Back") : null;
            if (canvasBack != null)
            {
                _backgroundPageRoot = canvasBack.Find("GameObject") as RectTransform;
                if (_backgroundPageRoot == null)
                {
                    _backgroundPageRoot = canvasBack.GetComponentInChildren<RectTransform>(true);
                    if (_backgroundPageRoot == canvasBack as RectTransform)
                    {
                        _backgroundPageRoot = _backgroundPageRoot.childCount > 0
                            ? _backgroundPageRoot.GetChild(0) as RectTransform
                            : null;
                    }
                }
            }
        }

        if (_pageCenter == null && _goYiDong != null)
        {
            _pageCenter = _goYiDong.Find("BJ") as RectTransform;
        }

        if (_pageLeft == null && _goYiDong != null)
        {
            _pageLeft = _goYiDong.Find("BJLeft") as RectTransform;
        }

        if (_pageRight == null && _goYiDong != null)
        {
            _pageRight = _goYiDong.Find("BJRight") as RectTransform;
        }

        if (_pageViewport == null)
        {
            RectTransform parentRectTransform = _goYiDong != null ? _goYiDong.parent as RectTransform : null;
            if (parentRectTransform != null && parentRectTransform != _goYiDong)
            {
                _pageViewport = parentRectTransform;
            }
            else
            {
                _pageViewport = CachedTransform as RectTransform;
            }
        }

        CacheHatchReferences();
        CachePetPlacementReferences();
        CacheArchitectureReferences();
    }

    /// <summary>
    /// 根据当前可视宽度刷新三页的摆放位置，并让分页容器保持在当前页。
    /// </summary>
    private void RefreshPageLayout(bool force)
    {
        CacheReferences();

        if (_goYiDong == null || _pageViewport == null)
        {
            return;
        }

        float viewportWidth = _pageViewport.rect.width;
        if (viewportWidth <= 0f)
        {
            return;
        }

        if (!force && Mathf.Approximately(_cachedViewportWidth, viewportWidth))
        {
            return;
        }

        // 分辨率变化后，如果当前不在中页，需要先把主相机的“中页基准位置”反推出去，
        // 避免重算分页偏移时把当前页误记成中心页。
        SyncMainCameraBaseFromCurrentOffset();

        // 分辨率变化后重新排列三页时，容器本身仍然保持在当前页对应的偏移，
        // 避免切到左/右页后改分辨率又被重置回中间。
        _goYiDong.offsetMin = Vector2.zero;
        _goYiDong.offsetMax = Vector2.zero;

        _cachedViewportWidth = viewportWidth;
        SetPageOffset(_pageLeft, -viewportWidth);
        SetPageOffset(_pageCenter, 0f);
        SetPageOffset(_pageRight, viewportWidth);
        SetContainerOffset(GetPageOffset(_currentPageIndex));
        SetBackgroundContainerOffset(GetPageOffset(_currentPageIndex));
        ApplyMainCameraOffset(GetMainCameraPageOffset(_currentPageIndex));
        Canvas.ForceUpdateCanvases();

        StopSwitchTween();
        UpdateButtonState();
        MarkPetPlacementLayoutDirty();
        SyncPetPlacementMarkersToEntities();
    }

    /// <summary>
    /// 切换到指定页。
    /// </summary>
    /// <param name="targetPageIndex">目标页索引。</param>
    private void SwitchToPage(int targetPageIndex)
    {
        CacheReferences();
        if (_goYiDong == null || _pageViewport == null)
        {
            return;
        }

        RefreshPageLayout(false);
        if (_cachedViewportWidth <= 0f)
        {
            return;
        }

        int clampedPageIndex = Mathf.Clamp(targetPageIndex, LeftPageIndex, RightPageIndex);
        if (clampedPageIndex == _currentPageIndex && !_isSwitching)
        {
            return;
        }

        _currentPageIndex = clampedPageIndex;
        float targetOffsetX = GetPageOffset(_currentPageIndex);
        float targetMainCameraOffsetX = GetMainCameraPageOffset(_currentPageIndex);

        StopSwitchTween();
        if (_switchDuration <= 0f)
        {
            SetContainerOffset(targetOffsetX);
            SetBackgroundContainerOffset(targetOffsetX);
            ApplyMainCameraOffset(targetMainCameraOffsetX);
            UpdateButtonState();
            return;
        }

        float startContainerOffsetX = _goYiDong.anchoredPosition.x;
        float startBackgroundOffsetX = _backgroundPageRoot != null ? _backgroundPageRoot.anchoredPosition.x : startContainerOffsetX;
        float startMainCameraOffsetX = CurrentMainCameraOffsetX;
        _isSwitching = true;
        UpdateButtonState();

        _switchTween = DOTween.To(
                () => 0f,
                progress =>
                {
                    float containerOffsetX = Mathf.LerpUnclamped(startContainerOffsetX, targetOffsetX, progress);
                    float backgroundOffsetX = Mathf.LerpUnclamped(startBackgroundOffsetX, targetOffsetX, progress);
                    float mainCameraOffsetX = Mathf.LerpUnclamped(startMainCameraOffsetX, targetMainCameraOffsetX, progress);
                    SetContainerOffset(containerOffsetX);
                    SetBackgroundContainerOffset(backgroundOffsetX);
                    ApplyMainCameraOffset(mainCameraOffsetX);
                },
                1f,
                _switchDuration)
            .SetEase(Ease.OutCubic)
            .SetUpdate(true)
            .OnComplete(OnSwitchTweenComplete);
    }

    /// <summary>
    /// 切页补间完成回调。
    /// </summary>
    private void OnSwitchTweenComplete()
    {
        SetContainerOffset(GetPageOffset(_currentPageIndex));
        SetBackgroundContainerOffset(GetPageOffset(_currentPageIndex));
        ApplyMainCameraOffset(GetMainCameraPageOffset(_currentPageIndex));
        _isSwitching = false;
        _switchTween = null;
        UpdateButtonState();
    }

    /// <summary>
    /// 停止当前切页补间。
    /// </summary>
    private void StopSwitchTween()
    {
        if (_switchTween != null)
        {
            _switchTween.Kill();
            _switchTween = null;
        }

        _isSwitching = false;
    }

    /// <summary>
    /// 设置单页相对分页原点的横向偏移。
    /// </summary>
    /// <param name="page">页面节点。</param>
    /// <param name="offsetX">横向偏移。</param>
    private static void SetPageOffset(RectTransform page, float offsetX)
    {
        if (page == null)
        {
            return;
        }

        Vector2 anchoredPosition = page.anchoredPosition;
        page.anchoredPosition = new Vector2(offsetX, anchoredPosition.y);
    }

    /// <summary>
    /// 设置分页容器的横向偏移。
    /// </summary>
    /// <param name="offsetX">横向偏移。</param>
    private void SetContainerOffset(float offsetX)
    {
        if (_goYiDong == null)
        {
            return;
        }

        Vector2 anchoredPosition = _goYiDong.anchoredPosition;
        _goYiDong.anchoredPosition = new Vector2(offsetX, anchoredPosition.y);
    }

    /// <summary>
    /// 设置 Canvas_Back 背景分页容器的横向偏移，使背景页与前景页同步滑动。
    /// </summary>
    private void SetBackgroundContainerOffset(float offsetX)
    {
        if (_backgroundPageRoot == null)
        {
            return;
        }

        Vector2 anchoredPosition = _backgroundPageRoot.anchoredPosition;
        _backgroundPageRoot.anchoredPosition = new Vector2(offsetX, anchoredPosition.y);
    }

    /// <summary>
    /// 获取指定页在容器上的目标偏移。
    /// </summary>
    /// <param name="pageIndex">页索引。</param>
    /// <returns>目标横向偏移。</returns>
    private float GetPageOffset(int pageIndex)
    {
        return -pageIndex * _cachedViewportWidth;
    }

    /// <summary>
    /// 三页始终保持激活，仅通过移动分页容器控制显示区域。
    /// </summary>
    private void UpdatePageVisibility()
    {
        CacheReferences();
        SetPageVisible(_pageLeft, true);
        SetPageVisible(_pageCenter, true);
        SetPageVisible(_pageRight, true);
    }

    /// <summary>
    /// 设置单页根节点是否显示。
    /// </summary>
    private static void SetPageVisible(RectTransform page, bool isVisible)
    {
        if (page == null || page.gameObject.activeSelf == isVisible)
        {
            return;
        }

        page.gameObject.SetActive(isVisible);
    }

    /// <summary>
    /// 根据当前页更新按钮是否可交互。
    /// </summary>
    private void UpdateButtonState()
    {
        UpdatePageVisibility();
        UpdateGoYouWanVisibility();

        if (_btnLeft != null)
        {
            _btnLeft.interactable = !_isSwitching && _currentPageIndex > LeftPageIndex;
        }

        if (_btnRight != null)
        {
            _btnRight.interactable = !_isSwitching && _currentPageIndex < RightPageIndex;
        }
    }
}
