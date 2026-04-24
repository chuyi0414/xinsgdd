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
    /// 主界面分页槽位。
    /// 这里不再用单一整数表示“左中右”，
    /// 因为当前页面已经扩展为“左/中/右/下”四个离散槽位。
    /// </summary>
    private enum MainPageSlot
    {
        /// <summary>
        /// 左页。
        /// </summary>
        Left = 0,

        /// <summary>
        /// 中页。
        /// </summary>
        Center = 1,

        /// <summary>
        /// 右页。
        /// </summary>
        Right = 2,

        /// <summary>
        /// 下页。
        /// </summary>
        Below = 3,
    }

    // 左边按钮
    [SerializeField]
    private Button _btnLeft;
    // 右边按钮
    [SerializeField]
    private Button _btnRight;
    // 下方页面回到中页的上翻按钮。
    // 这个按钮是全局悬浮按钮，默认隐藏，仅在 Below 页显示。
    [SerializeField]
    private Button _btnUp;
    // 每日一关按钮。点击后切到下方页面。
    [SerializeField]
    private Button _btnDailyChallenge;
    // 水果图鉴按钮。用户在 Inspector 中自行拖入 GOSGTJ 上的 Button 组件。
    [SerializeField]
    private Button _btnFruitTJ;
    // 宠物图鉴按钮。用户在 Inspector 中自行拖入 GOCWTJ 上的 Button 组件。
    [SerializeField]
    private Button _btnPetTJ;
    // 每日一关附属 UI：GoTX 根节点。进入每日一关时隐藏，返回中页动画结束后恢复。
    [SerializeField]
    private GameObject _goTX;
    // 个人设置按钮。
    // 用户在 Inspector 中自行拖入 GoTX 上的 Button 组件。
    [SerializeField]
    private Button _btnPersonalSetting;
    // 当前选中头像的展示 RawImage。
    // 用户在 Inspector 中自行拖入 GoTX 上的头像 RawImage 组件。
    [SerializeField]
    private RawImage _rawImageAvatar;
    // 当前选中头像框的展示 Image。
    // 用户在 Inspector 中自行拖入 GoTX 上的头像框 Image 组件。
    [SerializeField]
    private Image _imageAvatarFrame;
    // 每日一关附属 UI：GoJB 根节点。进入每日一关时隐藏，返回中页动画结束后恢复。
    [SerializeField]
    private GameObject _goJB;
    // 每日一关附属 UI：GOCWTJ 根节点。进入每日一关时隐藏，返回中页动画结束后恢复。
    [SerializeField]
    private GameObject _goCWTJ;
    // 当前是否处于"每日一关附属 UI 已隐藏"状态。
    // 进入每日一关时置 true，返回中页动画结束后置 false。
    private bool _isDailyChallengeAuxiliaryUiHidden;
    // 战斗期间强制隐藏 BtnUp 的标记。
    // 由 DailyChallengeUIForm.OnBtnStartLevel 设置为 true，
    // 由 RestoreDailyChallengeView 重置为 false。
    // 此标记优先级高于 _currentPageSlot == Below 的常规显示逻辑。
    private bool _forceHideBtnUp;
    // 点击 BtnUp 后，是否需要在回中页动画结束后恢复附属 UI。
    // 这个标记跨越切页动画期间，确保恢复时机在动画完成而不是点击瞬间。
    private bool _pendingRestoreDailyChallengeAuxiliaryUi;
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
    // 下页根节点。
    [SerializeField]
    private RectTransform _pageBelow;
    // 切页动画时长。
    [SerializeField]
    private float _switchDuration = 0.2f;

    /// <summary>
    /// 当前切页补间。
    /// </summary>
    private Tweener _switchTween;

    /// <summary>
    /// 当前所在页槽位。
    /// </summary>
    private MainPageSlot _currentPageSlot = MainPageSlot.Center;

    /// <summary>
    /// 上一次记录的可视区域宽度。
    /// </summary>
    private float _cachedViewportWidth = -1f;

    /// <summary>
    /// 上一次记录的可视区域高度。
    /// 下页分页依赖这个高度来计算纵向位移。
    /// </summary>
    private float _cachedViewportHeight = -1f;

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
        _isDailyChallengeAuxiliaryUiHidden = false;
        _pendingRestoreDailyChallengeAuxiliaryUi = false;
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

        if (_btnUp != null)
        {
            _btnUp.onClick.AddListener(OnBtnUp);
        }
        else
        {
            Log.Warning("MainUIForm 找不到 BtnUp。");
        }

        if (_btnDailyChallenge != null)
        {
            _btnDailyChallenge.onClick.AddListener(OnBtnDailyChallenge);
        }
        else
        {
            Log.Warning("MainUIForm 找不到 GoDailyChallenge。");
        }

        if (_btnFruitTJ != null)
        {
            _btnFruitTJ.onClick.AddListener(OnBtnFruitTJ);
        }

        if (_btnPetTJ != null)
        {
            _btnPetTJ.onClick.AddListener(OnBtnPetTJ);
        }
        else
        {
            Log.Warning("MainUIForm 缺少宠物图鉴按钮引用，请在 Inspector 中把 GOCWTJ 上的 Button 组件拖入 _btnPetTJ。");
        }

        base.OnInit(userData);
        RefreshPageLayout(true);
        UpdateButtonState();
        InitializeHatchView();
        InitializePetPlacementView();
        InitializeGoldView();
        InitializeProduceView();
        InitializeArchitectureView();
        InitializeDailyChallengeView();
        InitializeFruitTJView();
        InitializePetTJView();
        CachePersonalSettingButton();
    }

    /// <summary>
    /// 打开主界面时刷新布局与子视图状态。
    /// </summary>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        _isDailyChallengeAuxiliaryUiHidden = false;
        _pendingRestoreDailyChallengeAuxiliaryUi = false;
        RefreshPageLayout(true);
        UpdateButtonState();
        OpenHatchView();
        OpenPetPlacementView();
        OpenGoldView();
        OpenProduceView();
        OpenArchitectureView();
        OpenDailyChallengeView();
        TryFlushPendingPetRewardDrops();
        RefreshAvatarDisplay();
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
        CloseDailyChallengeView();
        CloseFruitTJView();
        ClosePetTJView();
        base.OnClose(isShutdown, userData);
    }

    /// <summary>
    /// 界面遮挡恢复（如 PersonalSettingUIForm 关闭后）。
    /// 刷新头像展示，确保与运行时选中状态同步。
    /// </summary>
    protected override void OnReveal()
    {
        base.OnReveal();
        RefreshAvatarDisplay();
    }

    /// <summary>
    /// 每帧更新子视图状态。
    /// </summary>
    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);
        UpdateHatchView();
        UpdatePetPlacementView();
        TryFlushPendingPetRewardDrops();
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

        if (_btnUp != null)
        {
            _btnUp.onClick.RemoveListener(OnBtnUp);
        }

        if (_btnDailyChallenge != null)
        {
            _btnDailyChallenge.onClick.RemoveListener(OnBtnDailyChallenge);
        }

        if (_btnFruitTJ != null)
        {
            _btnFruitTJ.onClick.RemoveListener(OnBtnFruitTJ);
        }

        if (_btnPetTJ != null)
        {
            _btnPetTJ.onClick.RemoveListener(OnBtnPetTJ);
        }

        DestroyHatchView();
        DestroyPetPlacementView();
        DestroyGoldView();
        DestroyProduceView();
        DestroyArchitectureView();
        DestroyDailyChallengeView();
        DestroyFruitTJView();
        DestroyPetTJView();
        DestroyPersonalSettingButton();
    }

    /// <summary>
    /// 右边按钮点击逻辑
    /// </summary>
    private void OnBtnRight()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();

        if (_currentPageSlot == MainPageSlot.Left)
        {
            SwitchToPage(MainPageSlot.Center);
            return;
        }

        if (_currentPageSlot == MainPageSlot.Center)
        {
            SwitchToPage(MainPageSlot.Right);
        }
    }

    /// <summary>
    /// 左边按钮点击逻辑
    /// </summary>
    private void OnBtnLeft()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();

        if (_currentPageSlot == MainPageSlot.Right)
        {
            SwitchToPage(MainPageSlot.Center);
            return;
        }

        if (_currentPageSlot == MainPageSlot.Center)
        {
            SwitchToPage(MainPageSlot.Left);
        }
    }

    /// <summary>
    /// 上翻按钮点击逻辑。
    /// 只在下页可见，用来从 BJBelow 返回 BJ。
    /// </summary>
    private void OnBtnUp()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();

        if (_currentPageSlot == MainPageSlot.Below)
        {
            CloseDailyChallengeUIForm();
            ClearDailyChallengeBoardPreview();
            _pendingRestoreDailyChallengeAuxiliaryUi = true;
            SwitchToPage(MainPageSlot.Center);
        }
    }

    /// <summary>
    /// 每日一关按钮点击逻辑。
    /// 从中页向下翻到 BJBelow。
    /// </summary>
    private void OnBtnDailyChallenge()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        if (_currentPageSlot == MainPageSlot.Center)
        {
            _pendingRestoreDailyChallengeAuxiliaryUi = false;
            SetDailyChallengeAuxiliaryUiHidden(true);
            SwitchToPage(MainPageSlot.Below);
            ScheduleDailyChallengeUIFormOpenAfterSwitch();
        }
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
    /// 缓存界面上会用到的节点引用。
    /// 除 Canvas_Back 背景容器需要运行时跨 Canvas 查找外，
    /// 其余引用全部由用户在 Inspector 中手动拖入。
    /// </summary>
    private void CacheReferences()
    {
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

        CacheHatchReferences();
        CachePetPlacementReferences();
        CacheArchitectureReferences();
    }

    /// <summary>
    /// 根据当前可视区域尺寸刷新四页的摆放位置，并让分页容器保持在当前页。
    /// </summary>
    private void RefreshPageLayout(bool force)
    {
        CacheReferences();

        if (_goYiDong == null || _pageViewport == null)
        {
            return;
        }

        float viewportWidth = _pageViewport.rect.width;
        float viewportHeight = _pageViewport.rect.height;
        if (viewportWidth <= 0f || viewportHeight <= 0f)
        {
            return;
        }

        if (!force
            && Mathf.Approximately(_cachedViewportWidth, viewportWidth)
            && Mathf.Approximately(_cachedViewportHeight, viewportHeight))
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
        _cachedViewportHeight = viewportHeight;
        SetPageOffset(_pageLeft, new Vector2(-viewportWidth, 0f));
        SetPageOffset(_pageCenter, Vector2.zero);
        SetPageOffset(_pageRight, new Vector2(viewportWidth, 0f));
        SetPageOffset(_pageBelow, new Vector2(0f, -viewportHeight));
        SyncBackgroundPageLayout(viewportWidth, viewportHeight);
        SetContainerOffset(GetPageOffset(_currentPageSlot));
        SetBackgroundContainerOffset(GetPageOffset(_currentPageSlot));
        ApplyMainCameraOffset(GetMainCameraPageOffset(_currentPageSlot));
        Canvas.ForceUpdateCanvases();

        StopSwitchTween();
        UpdateButtonState();
        MarkPetPlacementLayoutDirty();
        SyncPetPlacementMarkersToEntities();
    }

    /// <summary>
    /// 切换到指定页。
    /// </summary>
    /// <param name="targetPageSlot">目标页槽位。</param>
    private void SwitchToPage(MainPageSlot targetPageSlot)
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

        if (targetPageSlot == _currentPageSlot && !_isSwitching)
        {
            return;
        }

        _currentPageSlot = targetPageSlot;
        Vector2 targetOffset = GetPageOffset(_currentPageSlot);
        Vector2 targetMainCameraOffset = GetMainCameraPageOffset(_currentPageSlot);

        StopSwitchTween(true);
        if (_switchDuration <= 0f)
        {
            SetContainerOffset(targetOffset);
            SetBackgroundContainerOffset(targetOffset);
            ApplyMainCameraOffset(targetMainCameraOffset);
            TryCompletePendingDailyChallengeAuxiliaryUiRestore();
            UpdateButtonState();
            HandleDailyChallengePageArrived();
            return;
        }

        Vector2 startContainerOffset = _goYiDong.anchoredPosition;
        Vector2 startBackgroundOffset = _backgroundPageRoot != null ? _backgroundPageRoot.anchoredPosition : startContainerOffset;
        Vector2 startMainCameraOffset = CurrentMainCameraOffset;
        _isSwitching = true;
        UpdateButtonState();

        _switchTween = DOTween.To(
                () => 0f,
                progress =>
                {
                    Vector2 containerOffset = Vector2.LerpUnclamped(startContainerOffset, targetOffset, progress);
                    Vector2 backgroundOffset = Vector2.LerpUnclamped(startBackgroundOffset, targetOffset, progress);
                    Vector2 mainCameraOffset = Vector2.LerpUnclamped(startMainCameraOffset, targetMainCameraOffset, progress);
                    SetContainerOffset(containerOffset);
                    SetBackgroundContainerOffset(backgroundOffset);
                    ApplyMainCameraOffset(mainCameraOffset);
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
        SetContainerOffset(GetPageOffset(_currentPageSlot));
        SetBackgroundContainerOffset(GetPageOffset(_currentPageSlot));
        ApplyMainCameraOffset(GetMainCameraPageOffset(_currentPageSlot));
        _isSwitching = false;
        _switchTween = null;
        TryCompletePendingDailyChallengeAuxiliaryUiRestore();
        UpdateButtonState();
        HandleDailyChallengePageArrived();
    }

    /// <summary>
    /// 停止当前切页补间。
    /// </summary>
    private void StopSwitchTween(bool preserveDailyChallengeAuxiliaryUiRestoreState = false)
    {
        if (_switchTween != null)
        {
            _switchTween.Kill();
            _switchTween = null;
        }

        _isSwitching = false;
        ResetDailyChallengeTransitionState();

        if (!preserveDailyChallengeAuxiliaryUiRestoreState)
        {
            TryCompletePendingDailyChallengeAuxiliaryUiRestore();
        }
    }

    /// <summary>
    /// 设置每日一关附属 UI 的隐藏状态，并立即刷新显隐。
    /// </summary>
    /// <param name="isHidden">true 隐藏附属 UI，false 恢复显示。</param>
    private void SetDailyChallengeAuxiliaryUiHidden(bool isHidden)
    {
        _isDailyChallengeAuxiliaryUiHidden = isHidden;
        UpdateDailyChallengeAuxiliaryUiVisibility();
    }

    /// <summary>
    /// 尝试完成待执行的附属 UI 恢复。
    /// 只有在 BtnUp 点击后、且回中页动画已抵达 Center 时才真正恢复。
    /// </summary>
    private void TryCompletePendingDailyChallengeAuxiliaryUiRestore()
    {
        if (!_pendingRestoreDailyChallengeAuxiliaryUi || _currentPageSlot != MainPageSlot.Center)
        {
            return;
        }

        _pendingRestoreDailyChallengeAuxiliaryUi = false;
        SetDailyChallengeAuxiliaryUiHidden(false);
    }

    /// <summary>
    /// 根据当前隐藏状态，统一刷新每日一关附属 UI 的显隐。
    /// 控制的节点：BtnLeft、BtnRight、GoTX、GOSGTJ、GOCWTJ、GoJB。
    /// </summary>
    private void UpdateDailyChallengeAuxiliaryUiVisibility()
    {
        bool isVisible = !_isDailyChallengeAuxiliaryUiHidden;
        SetNodeActive(_btnLeft != null ? _btnLeft.gameObject : null, isVisible);
        SetNodeActive(_btnRight != null ? _btnRight.gameObject : null, isVisible);
        SetNodeActive(_goTX, isVisible);
        SetNodeActive(_btnFruitTJ != null ? _btnFruitTJ.gameObject : null, isVisible);
        SetNodeActive(_goCWTJ, isVisible);
        SetNodeActive(_goJB, isVisible);
    }

    /// <summary>
    /// 安全设置节点激活状态，跳过空引用和重复赋值。
    /// </summary>
    /// <param name="node">目标节点，允许为 null。</param>
    /// <param name="isActive">是否激活。</param>
    private static void SetNodeActive(GameObject node, bool isActive)
    {
        if (node == null || node.activeSelf == isActive)
        {
            return;
        }

        node.SetActive(isActive);
    }

    /// <summary>
    /// 设置单页相对分页原点的二维偏移。
    /// </summary>
    /// <param name="page">页面节点。</param>
    /// <param name="offset">页面在分页容器中的目标偏移。</param>
    private static void SetPageOffset(RectTransform page, Vector2 offset)
    {
        if (page == null)
        {
            return;
        }

        page.anchoredPosition = offset;
    }

    /// <summary>
    /// 设置分页容器的二维偏移。
    /// </summary>
    /// <param name="offset">容器目标偏移。</param>
    private void SetContainerOffset(Vector2 offset)
    {
        if (_goYiDong == null)
        {
            return;
        }

        _goYiDong.anchoredPosition = offset;
    }

    /// <summary>
    /// 设置 Canvas_Back 背景分页容器的二维偏移，使背景页与前景页同步滑动。
    /// </summary>
    private void SetBackgroundContainerOffset(Vector2 offset)
    {
        if (_backgroundPageRoot == null)
        {
            return;
        }

        _backgroundPageRoot.anchoredPosition = offset;
    }

    /// <summary>
    /// 按当前可视区域尺寸同步 Canvas_Back 背景分页节点的摆放位置。
    /// </summary>
    /// <param name="viewportWidth">当前可视区域宽度。</param>
    /// <param name="viewportHeight">当前可视区域高度。</param>
    private void SyncBackgroundPageLayout(float viewportWidth, float viewportHeight)
    {
        if (_backgroundPageRoot == null)
        {
            return;
        }

        RectTransform backgroundPageLeft = _backgroundPageRoot.Find("BJLeft") as RectTransform;
        RectTransform backgroundPageCenter = _backgroundPageRoot.Find("BJ") as RectTransform;
        RectTransform backgroundPageRight = _backgroundPageRoot.Find("BJRight") as RectTransform;
        RectTransform backgroundPageBelow = _backgroundPageRoot.Find("BJBelow") as RectTransform;

        SetPageOffset(backgroundPageLeft, new Vector2(-viewportWidth, 0f));
        SetPageOffset(backgroundPageCenter, Vector2.zero);
        SetPageOffset(backgroundPageRight, new Vector2(viewportWidth, 0f));
        SetPageOffset(backgroundPageBelow, new Vector2(0f, -viewportHeight));
    }

    /// <summary>
    /// 获取指定页在容器上的目标偏移。
    /// </summary>
    /// <param name="pageSlot">目标页槽位。</param>
    /// <returns>目标二维偏移。</returns>
    private Vector2 GetPageOffset(MainPageSlot pageSlot)
    {
        switch (pageSlot)
        {
            case MainPageSlot.Left:
                return new Vector2(_cachedViewportWidth, 0f);

            case MainPageSlot.Right:
                return new Vector2(-_cachedViewportWidth, 0f);

            case MainPageSlot.Below:
                return new Vector2(0f, _cachedViewportHeight);

            default:
                return Vector2.zero;
        }
    }

    /// <summary>
    /// 四页始终保持激活，仅通过移动分页容器控制显示区域。
    /// </summary>
    private void UpdatePageVisibility()
    {
        CacheReferences();
        SetPageVisible(_pageLeft, true);
        SetPageVisible(_pageCenter, true);
        SetPageVisible(_pageRight, true);
        SetPageVisible(_pageBelow, true);
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
        UpdateDailyChallengeAuxiliaryUiVisibility();
        UpdatePetRewardUiVisibility();

        if (_btnLeft != null)
        {
            _btnLeft.interactable = !_isSwitching
                && (_currentPageSlot == MainPageSlot.Center || _currentPageSlot == MainPageSlot.Right);
        }

        if (_btnRight != null)
        {
            _btnRight.interactable = !_isSwitching
                && (_currentPageSlot == MainPageSlot.Center || _currentPageSlot == MainPageSlot.Left);
        }

        if (_btnUp != null)
        {
            // _forceHideBtnUp 为 true 时强制隐藏 BtnUp，忽略当前页槽位。
            bool shouldShowBtnUp = !_forceHideBtnUp && _currentPageSlot == MainPageSlot.Below;
            if (_btnUp.gameObject.activeSelf != shouldShowBtnUp)
            {
                _btnUp.gameObject.SetActive(shouldShowBtnUp);
            }

            _btnUp.interactable = !_isSwitching && shouldShowBtnUp;
        }

        if (_btnDailyChallenge != null)
        {
            _btnDailyChallenge.interactable = !_isSwitching && _currentPageSlot == MainPageSlot.Center;
        }
    }

    /// <summary>
    /// 设置 BtnUp 的强制隐藏状态。
    /// 由 DailyChallengeUIForm.OnBtnStartLevel 调用，战斗期间隐藏上翻按钮。
    /// </summary>
    /// <param name="visible">true 显示 BtnUp（恢复常规逻辑），false 强制隐藏。</param>
    public void SetBtnUpVisible(bool visible)
    {
        _forceHideBtnUp = !visible;
        if (_btnUp != null && _forceHideBtnUp)
        {
            _btnUp.gameObject.SetActive(false);
        }
        else
        {
            UpdateButtonState();
        }
    }

    /// <summary>
    /// 从战斗流程返回后恢复每日一关视图。
    /// 由 MainProcedure.OnEnter 在检测到 ReturningFromCombat 标记时调用。
    /// 导航到 Below 页并打开 DailyChallengeUIForm，同时恢复 BtnUp 显示。
    /// </summary>
    public void RestoreDailyChallengeView()
    {
        _forceHideBtnUp = false;
        _pendingRestoreDailyChallengeAuxiliaryUi = false;
        // 清理战斗前生成的消除卡片与 EliminateTheAreaEntity，避免残留在场景中。
        ClearDailyChallengeBoardPreview();
        SetDailyChallengeAuxiliaryUiHidden(true);
        SwitchToPage(MainPageSlot.Below);
        ScheduleDailyChallengeUIFormOpenAfterSwitch();
    }

    /// <summary>
    /// 判断当前是否允许直接展示宠物奖励掉落物。
    /// 只有主界面已经稳定停在中页时，金币/产出物的投影结果才和宠物页一致。
    /// </summary>
    /// <returns>true 表示可立即展示；false 表示应先缓存。</returns>
    private bool CanPresentPetRewardDropsNow()
    {
        return _currentPageSlot == MainPageSlot.Center && !_isSwitching;
    }

    /// <summary>
    /// 根据分页状态刷新宠物奖励 UI 的显隐。
    /// 规则非常严格：只有停在 Center 且切页动画完全结束后才显示。
    /// </summary>
    private void UpdatePetRewardUiVisibility()
    {
        bool isVisible = CanPresentPetRewardDropsNow();
        UpdateGoldRewardUiVisibility(isVisible);
        UpdateProduceRewardUiVisibility(isVisible);
    }

    /// <summary>
    /// 尝试刷新所有待展示的宠物奖励掉落请求。
    /// 这里统一作为金币与产出物两条链路的公共入口。
    /// </summary>
    private void TryFlushPendingPetRewardDrops()
    {
        if (!CanPresentPetRewardDropsNow())
        {
            return;
        }

        FlushPendingGoldDropRequests();
        FlushPendingProduceDropRequests();
    }

    /// <summary>
    /// 基于当前宠物实体位置，构建一条奖励掉落的起点/终点世界坐标。
    /// 这里必须在奖励事件发生当下就把世界坐标固化下来，
    /// 否则等玩家返回宠物页时，宠物可能已经移动，掉落位置就会失真。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    /// <param name="offsetMinX">终点随机偏移的 X 最小值。</param>
    /// <param name="offsetMaxX">终点随机偏移的 X 最大值。</param>
    /// <param name="offsetMinY">终点随机偏移的 Y 最小值。</param>
    /// <param name="offsetMaxY">终点随机偏移的 Y 最大值。</param>
    /// <param name="startWorldPos">输出：奖励起点世界坐标。</param>
    /// <param name="endWorldPos">输出：奖励终点世界坐标。</param>
    /// <returns>成功构建返回 true；否则返回 false。</returns>
    private static bool TryBuildPetRewardWorldPositions(
        int petInstanceId,
        float offsetMinX,
        float offsetMaxX,
        float offsetMinY,
        float offsetMaxY,
        out Vector3 startWorldPos,
        out Vector3 endWorldPos)
    {
        startWorldPos = Vector3.zero;
        endWorldPos = Vector3.zero;

        if (petInstanceId <= 0 || GameEntry.PlayfieldEntities == null)
        {
            return false;
        }

        if (!GameEntry.PlayfieldEntities.TryGetPetEntityLogic(petInstanceId, out PetEntityLogic petEntityLogic)
            || petEntityLogic == null
            || petEntityLogic.CachedTransform == null)
        {
            return false;
        }

        startWorldPos = petEntityLogic.CachedTransform.position;
        float offsetX = UnityEngine.Random.Range(offsetMinX, offsetMaxX);
        float offsetY = UnityEngine.Random.Range(offsetMinY, offsetMaxY);
        endWorldPos = startWorldPos + new Vector3(offsetX, offsetY, 0f);
        return true;
    }

    /// <summary>
    /// 缓存个人设置入口按钮。
    /// 引用由用户在 Inspector 中手动拖入，这里只负责注册监听。
    /// </summary>
    private void CachePersonalSettingButton()
    {
        if (_btnPersonalSetting == null)
        {
            Log.Warning("MainUIForm 缺少个人设置按钮引用，请在 Inspector 中把 GoTX 上的 Button 组件拖入 _btnPersonalSetting。");
            return;
        }

        _btnPersonalSetting.onClick.RemoveListener(OnBtnPersonalSettingClicked);
        _btnPersonalSetting.onClick.AddListener(OnBtnPersonalSettingClicked);
    }

    /// <summary>
    /// 清理个人设置入口按钮的点击监听。
    /// </summary>
    private void DestroyPersonalSettingButton()
    {
        if (_btnPersonalSetting == null)
        {
            return;
        }

        _btnPersonalSetting.onClick.RemoveListener(OnBtnPersonalSettingClicked);
        _btnPersonalSetting = null;
    }

    /// <summary>
    /// GoTX 点击回调。
    /// 打开个人设置界面 PersonalSettingUIForm。
    /// </summary>
    private void OnBtnPersonalSettingClicked()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();

        if (GameEntry.UI == null)
        {
            Log.Warning("MainUIForm 无法打开个人设置界面，UIComponent 缺失。");
            return;
        }

        GameEntry.UI.OpenUIForm(UIFormDefine.PersonalSettingUIForm, UIFormDefine.PopupGroup);
    }

    /// <summary>
    /// 刷新当前选中头像的展示图片。
    /// 从运行时数据读取选中 Code，再从预加载缓存取 Sprite 赋给 RawImage。
    /// </summary>
    private void RefreshAvatarDisplay()
    {
        if (GameEntry.Fruits == null || GameEntry.GameAssets == null)
        {
            return;
        }

        // ── 头像 ──
        if (_rawImageAvatar != null)
        {
            string selectedCode = GameEntry.Fruits.SelectedHeadPortraitCode;

            if (string.IsNullOrEmpty(selectedCode) && GameEntry.DataTables != null && GameEntry.DataTables.IsAvailable<HeadPortraitDataRow>())
            {
                HeadPortraitDataRow[] rows = GameEntry.DataTables.GetAllDataRows<HeadPortraitDataRow>();
                for (int i = 0; i < rows.Length; i++)
                {
                    if (rows[i] != null && rows[i].IsDefaultUnlocked)
                    {
                        GameEntry.Fruits.TrySetSelectedHeadPortrait(rows[i].Code);
                        selectedCode = rows[i].Code;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(selectedCode))
            {
                _rawImageAvatar.texture = null;
            }
            else
            {
                HeadPortraitDataRow row = GameEntry.DataTables?.GetDataRowByCode<HeadPortraitDataRow>(selectedCode);
                if (row != null && GameEntry.GameAssets.TryGetHeadPortraitSprite(row.IconPath, out Sprite sprite) && sprite != null)
                {
                    _rawImageAvatar.texture = sprite.texture;
                }
                else
                {
                    _rawImageAvatar.texture = null;
                }
            }
        }

        // ── 头像框 ──
        if (_imageAvatarFrame != null)
        {
            string selectedFrameCode = GameEntry.Fruits.SelectedHeadPortraitFrameCode;

            if (string.IsNullOrEmpty(selectedFrameCode) && GameEntry.DataTables != null && GameEntry.DataTables.IsAvailable<HeadPortraitFrameDataRow>())
            {
                HeadPortraitFrameDataRow[] rows = GameEntry.DataTables.GetAllDataRows<HeadPortraitFrameDataRow>();
                for (int i = 0; i < rows.Length; i++)
                {
                    if (rows[i] != null && rows[i].IsDefaultUnlocked)
                    {
                        GameEntry.Fruits.TrySetSelectedHeadPortraitFrame(rows[i].Code);
                        selectedFrameCode = rows[i].Code;
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(selectedFrameCode))
            {
                _imageAvatarFrame.sprite = null;
            }
            else
            {
                HeadPortraitFrameDataRow row = GameEntry.DataTables?.GetDataRowByCode<HeadPortraitFrameDataRow>(selectedFrameCode);
                if (row != null && GameEntry.GameAssets.TryGetHeadPortraitFrameSprite(row.IconPath, out Sprite sprite) && sprite != null)
                {
                    _imageAvatarFrame.sprite = sprite;
                }
                else
                {
                    _imageAvatarFrame.sprite = null;
                }
            }
        }
    }
}
