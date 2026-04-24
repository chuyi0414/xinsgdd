using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 建筑升级界面。
/// 负责缓存 16 个建筑条目，刷新标题、按钮文案和 10 个等级指示物。
/// </summary>
public sealed class ArchitectureUpgradeUIForm : UIFormLogic
{
    /// <summary>
    /// 当前 prefab 中固定存在 16 个建筑条目。
    /// </summary>
    private const int ExpectedEntryCount = 16;

    /// <summary>
    /// 每个建筑条目固定有 10 个等级指示物。
    /// 下标 0~9 对应 Level 1~10，不显示 Level 0。
    /// </summary>
    private const int ExpectedLevelIndicatorCount = 10;

    /// <summary>
    /// 已达成等级指示物的不透明度。
    /// 保持原始资源观感，不额外做变暗处理。
    /// </summary>
    private const float ActiveLevelIndicatorAlpha = 1f;

    /// <summary>
    /// 未购买或未升级到的等级指示物不再隐藏。
    /// 这里只降低透明度，让玩家仍然能看到完整建筑位。
    /// </summary>
    private const float InactiveLevelIndicatorAlpha = 0.35f;

    /// <summary>
    /// 关闭按钮。
    /// 对应 GoArchitectureUpgrade/BtnClose。
    /// </summary>
    [SerializeField]
    private Button _btnClose;

    /// <summary>
    /// 孵化区分页按钮。
    /// 默认打开界面时选中这个分区。
    /// </summary>
    [SerializeField]
    private Button _btnHatch;

    /// <summary>
    /// 饮食区分页按钮。
    /// 点击后只显示饮食区条目。
    /// </summary>
    [SerializeField]
    private Button _btnDiet;

    /// <summary>
    /// 农场区分页按钮。
    /// 点击后只显示农场区条目。
    /// </summary>
    [SerializeField]
    private Button _btnFruiter;

    /// <summary>
    /// 主列表滚动组件。
    /// 点击分区按钮后需要把它重置到起始位置。
    /// </summary>
    [SerializeField]
    private ScrollRect _entryScrollRect;

    /// <summary>
    /// 16 个建筑条目的内容根节点。
    /// 对应 GoArchitectureUpgrade/ArchitectureUpgrade/Scroll View/Viewport/Content。
    /// </summary>
    [SerializeField]
    private RectTransform _contentRoot;

    /// <summary>
    /// 16 个建筑条目的视图缓存。
    /// 下标顺序与 prefab 里的直接子节点顺序一致。
    /// </summary>
    private ArchitectureEntryView[] _entryViews;

    /// <summary>
    /// 当前界面缓存是否已经构建完成。
    /// </summary>
    private bool _isViewReady;

    /// <summary>
    /// 是否已经监听建筑状态变化事件。
    /// </summary>
    private bool _isListeningArchitectureStateChanged;

    /// <summary>
    /// 当前正在显示的建筑分区。
    /// 界面每次打开时都会重置到孵化区。
    /// </summary>
    private PlayerRuntimeModule.ArchitectureCategory _currentVisibleCategory = PlayerRuntimeModule.ArchitectureCategory.Hatch;

    /// <summary>
    /// 单个建筑条目的界面缓存。
    /// </summary>
    private sealed class ArchitectureEntryView
    {
        /// <summary>
        /// 建筑条目根节点。
        /// </summary>
        public RectTransform Root;

        /// <summary>
        /// 建筑条目类型。
        /// </summary>
        public PlayerRuntimeModule.ArchitectureCategory Category;

        /// <summary>
        /// 1 基索引。
        /// 例如“饮食区 2 号”这里就是 2。
        /// </summary>
        public int SlotIndex;

        /// <summary>
        /// 建筑名称文本。
        /// </summary>
        public TextMeshProUGUI TitleText;

        /// <summary>
        /// 购买/升级按钮。
        /// </summary>
        public Button ActionButton;

        /// <summary>
        /// 按钮上的文案文本。
        /// </summary>
        public TextMeshProUGUI ActionText;

        /// <summary>
        /// 11 个等级指示物。
        /// 下标 0 = Level 0（未解锁），下标 1~10 = Level 1~10。
        /// 所有物体都会保持显示，通过 Sprite 图片和透明度区分已达成与未达成。
        /// </summary>
        public GameObject[] LevelIndicators;

        /// <summary>
        /// 11 个等级指示物上的 Graphic 组件缓存。
        /// 用于低频刷新透明度，避免每次刷新再查组件。
        /// </summary>
        public Graphic[] LevelIndicatorGraphics;

        /// <summary>
        /// 11 个等级指示物上的 Image 组件缓存。
        /// 用于从配置表读取 Sprite 并赋值，替代纯透明度方案。
        /// </summary>
        public Image[] LevelIndicatorImages;

        /// <summary>
        /// 条目内部横向滚动组件。
        /// 用于把当前等级位置滚到可视区域内。
        /// </summary>
        public ScrollRect LevelScrollRect;

        /// <summary>
        /// 条目内部横向滚动的可视区域。
        /// 用于计算最大滚动距离。
        /// </summary>
        public RectTransform LevelViewport;

        /// <summary>
        /// 条目内部横向滚动的 Content 根节点。
        /// 10 个等级位都挂在这里。
        /// </summary>
        public RectTransform LevelContent;

        /// <summary>
        /// 10 个等级指示物对应的 RectTransform 缓存。
        /// 用于根据目标等级计算偏移。
        /// </summary>
        public RectTransform[] LevelIndicatorRects;

        /// <summary>
        /// 10 个等级指示物的原始颜色缓存。
        /// 只改透明度，不改资源本身的 RGB 配色。
        /// </summary>
        public Color[] LevelIndicatorBaseColors;
    }

    /// <summary>
    /// 界面初始化。
    /// 这里只构建静态缓存和按钮监听，不在这里订阅低频业务事件。
    /// </summary>
    /// <param name="userData">用户自定义数据。</param>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        CacheReferences();
        _isViewReady = BuildEntryViews();
        RegisterButtonListeners();
        SwitchVisibleCategory(PlayerRuntimeModule.ArchitectureCategory.Hatch, true);
        RefreshAllEntries();
    }

    /// <summary>
    /// 界面打开。
    /// 打开时开始监听建筑状态变化，并立即刷新一次全部条目。
    /// </summary>
    /// <param name="userData">用户自定义数据。</param>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        EnsureArchitectureStateEventSubscription();
        SwitchVisibleCategory(PlayerRuntimeModule.ArchitectureCategory.Hatch, true);
        RefreshAllEntries();
    }

    /// <summary>
    /// 界面关闭。
    /// 关闭时释放低频业务事件监听。
    /// </summary>
    /// <param name="isShutdown">是否是关闭界面管理器时触发。</param>
    /// <param name="userData">用户自定义数据。</param>
    protected override void OnClose(bool isShutdown, object userData)
    {
        ReleaseArchitectureStateEventSubscription();
        base.OnClose(isShutdown, userData);
    }

    /// <summary>
    /// 对象销毁时清理按钮监听。
    /// </summary>
    private void OnDestroy()
    {
        ReleaseArchitectureStateEventSubscription();
        UnregisterButtonListeners();
    }

    /// <summary>
    /// 缓存界面上会用到的关键节点。
    /// 若未手动拖拽，则按当前 prefab 命名约定自动查找。
    /// </summary>
    private void CacheReferences()
    {
        if (_btnClose == null)
        {
            Transform closeButton = transform.Find("GoArchitectureUpgrade/BtnClose");
            if (closeButton != null)
            {
                _btnClose = closeButton.GetComponent<Button>();
            }
        }

        if (_btnHatch == null)
        {
            Transform hatchButton = transform.Find("GoArchitectureUpgrade/BtnHatch");
            if (hatchButton != null)
            {
                _btnHatch = hatchButton.GetComponent<Button>();
            }
        }

        if (_btnDiet == null)
        {
            Transform dietButton = transform.Find("GoArchitectureUpgrade/BtnDiet");
            if (dietButton != null)
            {
                _btnDiet = dietButton.GetComponent<Button>();
            }
        }

        if (_btnFruiter == null)
        {
            Transform fruiterButton = transform.Find("GoArchitectureUpgrade/BtnFruiter");
            if (fruiterButton != null)
            {
                _btnFruiter = fruiterButton.GetComponent<Button>();
            }
        }

        if (_contentRoot == null)
        {
            _contentRoot = transform.Find("GoArchitectureUpgrade/ArchitectureUpgrade/Scroll View/Viewport/Content") as RectTransform;
        }

        if (_entryScrollRect == null)
        {
            Transform scrollView = transform.Find("GoArchitectureUpgrade/ArchitectureUpgrade/Scroll View");
            if (scrollView != null)
            {
                _entryScrollRect = scrollView.GetComponent<ScrollRect>();
            }
        }
    }

    /// <summary>
    /// 构建 16 个建筑条目的视图缓存。
    /// 每个条目都严格按当前 prefab 的固定层级读取。
    /// </summary>
    /// <returns>缓存是否构建成功。</returns>
    private bool BuildEntryViews()
    {
        if (_contentRoot == null)
        {
            Log.Error("ArchitectureUpgradeUIForm 初始化失败，Content 根节点缺失。");
            return false;
        }

        if (_contentRoot.childCount != ExpectedEntryCount)
        {
            Log.Error(
                "ArchitectureUpgradeUIForm 初始化失败，Content 子节点数为 '{0}'，期望 '{1}'。",
                _contentRoot.childCount,
                ExpectedEntryCount);
            return false;
        }

        _entryViews = new ArchitectureEntryView[_contentRoot.childCount];
        for (int i = 0; i < _contentRoot.childCount; i++)
        {
            RectTransform entryRoot = _contentRoot.GetChild(i) as RectTransform;
            if (entryRoot == null)
            {
                Log.Error("ArchitectureUpgradeUIForm 初始化失败，条目根节点 '{0}' 无效。", i);
                return false;
            }

            if (!TryParseEntryIdentity(entryRoot.name, out PlayerRuntimeModule.ArchitectureCategory category, out int slotIndex))
            {
                Log.Error("ArchitectureUpgradeUIForm 初始化失败，条目名称 '{0}' 无效。", entryRoot.name);
                return false;
            }

            Button actionButton = entryRoot.Find("Button") != null ? entryRoot.Find("Button").GetComponent<Button>() : null;
            TextMeshProUGUI titleText = entryRoot.Find("Text (TMP)") != null
                ? entryRoot.Find("Text (TMP)").GetComponent<TextMeshProUGUI>()
                : null;
            TextMeshProUGUI actionText = actionButton != null ? actionButton.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            ScrollRect levelScrollRect = entryRoot.Find("Scroll View") != null
                ? entryRoot.Find("Scroll View").GetComponent<ScrollRect>()
                : null;
            RectTransform levelViewport = entryRoot.Find("Scroll View/Viewport") as RectTransform;
            RectTransform indicatorRoot = entryRoot.Find("Scroll View/Viewport/Content") as RectTransform;
            if (actionButton == null
                || titleText == null
                || actionText == null
                || levelScrollRect == null
                || levelViewport == null
                || indicatorRoot == null)
            {
                Log.Error("ArchitectureUpgradeUIForm 初始化失败，条目 '{0}' 缺少关键节点。", entryRoot.name);
                return false;
            }

            if (indicatorRoot.childCount != ExpectedLevelIndicatorCount)
            {
                Log.Error(
                    "ArchitectureUpgradeUIForm 初始化失败，条目 '{0}' 等级指示器数量为 '{1}'，期望 '{2}'。",
                    entryRoot.name,
                    indicatorRoot.childCount,
                    ExpectedLevelIndicatorCount);
                return false;
            }

            GameObject[] levelIndicators = new GameObject[indicatorRoot.childCount];
            Graphic[] levelIndicatorGraphics = new Graphic[indicatorRoot.childCount];
            Image[] levelIndicatorImages = new Image[indicatorRoot.childCount];
            RectTransform[] levelIndicatorRects = new RectTransform[indicatorRoot.childCount];
            Color[] levelIndicatorBaseColors = new Color[indicatorRoot.childCount];
            for (int indicatorIndex = 0; indicatorIndex < indicatorRoot.childCount; indicatorIndex++)
            {
                RectTransform levelIndicatorRect = indicatorRoot.GetChild(indicatorIndex) as RectTransform;
                GameObject levelIndicatorObject = levelIndicatorRect != null
                    ? levelIndicatorRect.gameObject
                    : indicatorRoot.GetChild(indicatorIndex).gameObject;
                levelIndicators[indicatorIndex] = levelIndicatorObject;
                levelIndicatorRects[indicatorIndex] = levelIndicatorRect;

                Graphic levelIndicatorGraphic = levelIndicatorObject.GetComponent<Graphic>();
                levelIndicatorGraphics[indicatorIndex] = levelIndicatorGraphic;
                levelIndicatorBaseColors[indicatorIndex] = levelIndicatorGraphic != null ? levelIndicatorGraphic.color : Color.white;

                // 缓存 Image 组件，用于后续 Sprite 赋值。
                Image levelIndicatorImage = levelIndicatorObject.GetComponent<Image>();
                if (levelIndicatorImage == null)
                {
                    // 如果指示物节点上没有 Image，自动添加一个，确保 Sprite 赋值链路畅通。
                    levelIndicatorImage = levelIndicatorObject.AddComponent<Image>();
                }
                levelIndicatorImages[indicatorIndex] = levelIndicatorImage;
            }

            _entryViews[i] = new ArchitectureEntryView
            {
                Root = entryRoot,
                Category = category,
                SlotIndex = slotIndex,
                TitleText = titleText,
                ActionButton = actionButton,
                ActionText = actionText,
                LevelScrollRect = levelScrollRect,
                LevelViewport = levelViewport,
                LevelContent = indicatorRoot,
                LevelIndicators = levelIndicators,
                LevelIndicatorGraphics = levelIndicatorGraphics,
                LevelIndicatorImages = levelIndicatorImages,
                LevelIndicatorRects = levelIndicatorRects,
                LevelIndicatorBaseColors = levelIndicatorBaseColors,
            };
        }

        return true;
    }

    /// <summary>
    /// 注册关闭按钮和 16 个条目的点击监听。
    /// </summary>
    private void RegisterButtonListeners()
    {
        if (_btnClose != null)
        {
            _btnClose.onClick.RemoveListener(OnCloseButtonClicked);
            _btnClose.onClick.AddListener(OnCloseButtonClicked);
        }

        if (_btnHatch != null)
        {
            _btnHatch.onClick.RemoveListener(OnHatchButtonClicked);
            _btnHatch.onClick.AddListener(OnHatchButtonClicked);
        }

        if (_btnDiet != null)
        {
            _btnDiet.onClick.RemoveListener(OnDietButtonClicked);
            _btnDiet.onClick.AddListener(OnDietButtonClicked);
        }

        if (_btnFruiter != null)
        {
            _btnFruiter.onClick.RemoveListener(OnFruiterButtonClicked);
            _btnFruiter.onClick.AddListener(OnFruiterButtonClicked);
        }

        if (_entryViews == null)
        {
            return;
        }

        for (int i = 0; i < _entryViews.Length; i++)
        {
            ArchitectureEntryView entryView = _entryViews[i];
            if (entryView == null || entryView.ActionButton == null)
            {
                continue;
            }

            int entryIndex = i;
            entryView.ActionButton.onClick.RemoveAllListeners();
            entryView.ActionButton.onClick.AddListener(() => OnEntryActionClicked(entryIndex));
        }
    }

    /// <summary>
    /// 注销关闭按钮和 16 个条目的点击监听。
    /// </summary>
    private void UnregisterButtonListeners()
    {
        if (_btnClose != null)
        {
            _btnClose.onClick.RemoveListener(OnCloseButtonClicked);
        }

        if (_btnHatch != null)
        {
            _btnHatch.onClick.RemoveListener(OnHatchButtonClicked);
        }

        if (_btnDiet != null)
        {
            _btnDiet.onClick.RemoveListener(OnDietButtonClicked);
        }

        if (_btnFruiter != null)
        {
            _btnFruiter.onClick.RemoveListener(OnFruiterButtonClicked);
        }

        if (_entryViews == null)
        {
            return;
        }

        for (int i = 0; i < _entryViews.Length; i++)
        {
            ArchitectureEntryView entryView = _entryViews[i];
            if (entryView == null || entryView.ActionButton == null)
            {
                continue;
            }

            entryView.ActionButton.onClick.RemoveAllListeners();
        }
    }

    /// <summary>
    /// 确保已经订阅建筑状态变化事件。
    /// 建筑购买、升级成功后会通过这个事件刷新 16 个条目。
    /// </summary>
    private void EnsureArchitectureStateEventSubscription()
    {
        if (_isListeningArchitectureStateChanged || GameEntry.Fruits == null)
        {
            return;
        }

        GameEntry.Fruits.ArchitectureStateChanged += OnArchitectureStateChanged;
        _isListeningArchitectureStateChanged = true;
    }

    /// <summary>
    /// 释放建筑状态变化事件订阅。
    /// </summary>
    private void ReleaseArchitectureStateEventSubscription()
    {
        if (!_isListeningArchitectureStateChanged || GameEntry.Fruits == null)
        {
            _isListeningArchitectureStateChanged = false;
            return;
        }

        GameEntry.Fruits.ArchitectureStateChanged -= OnArchitectureStateChanged;
        _isListeningArchitectureStateChanged = false;
    }

    /// <summary>
    /// 建筑状态变化回调。
    /// </summary>
    private void OnArchitectureStateChanged()
    {
        RefreshAllEntries();
    }

    /// <summary>
    /// 关闭按钮点击回调。
    /// 这里必须通过 UIForm.SerialId 精确关闭当前窗体。
    /// </summary>
    private void OnCloseButtonClicked()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        if (UIForm == null || GameEntry.UI == null)
        {
            return;
        }

        GameEntry.UI.CloseUIForm(UIForm.SerialId);
    }

    /// <summary>
    /// 孵化区分页按钮点击回调。
    /// 如果当前已经是孵化区，则重复点击不做任何事。
    /// </summary>
    private void OnHatchButtonClicked()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        SwitchVisibleCategory(PlayerRuntimeModule.ArchitectureCategory.Hatch, false);
    }

    /// <summary>
    /// 饮食区分页按钮点击回调。
    /// 如果当前已经是饮食区，则重复点击不做任何事。
    /// </summary>
    private void OnDietButtonClicked()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        SwitchVisibleCategory(PlayerRuntimeModule.ArchitectureCategory.Diet, false);
    }

    /// <summary>
    /// 农场区分页按钮点击回调。
    /// 如果当前已经是农场区，则重复点击不做任何事。
    /// </summary>
    private void OnFruiterButtonClicked()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        SwitchVisibleCategory(PlayerRuntimeModule.ArchitectureCategory.Fruiter, false);
    }

    /// <summary>
    /// 某个建筑条目的购买/升级按钮点击回调。
    /// 金币不足时底层会直接返回 false，这里不做禁用处理。
    /// </summary>
    /// <param name="entryIndex">条目在缓存数组中的下标。</param>
    private void OnEntryActionClicked(int entryIndex)
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        if (!_isViewReady || _entryViews == null || entryIndex < 0 || entryIndex >= _entryViews.Length)
        {
            return;
        }

        if (GameEntry.Fruits == null)
        {
            return;
        }

        ArchitectureEntryView entryView = _entryViews[entryIndex];
        if (entryView == null)
        {
            return;
        }

        if (GameEntry.Fruits.TryExecuteArchitectureAction(entryView.Category, entryView.SlotIndex))
        {
            RefreshAllEntries();
        }
    }

    /// <summary>
    /// 刷新 16 个建筑条目的标题、按钮文案和等级指示物。
    /// </summary>
    private void RefreshAllEntries()
    {
        if (!_isViewReady || _entryViews == null)
        {
            return;
        }

        for (int i = 0; i < _entryViews.Length; i++)
        {
            RefreshSingleEntry(_entryViews[i]);
        }

        RefreshEntryVisibility();
        ResetVisibleLevelScrollPositions();
    }

    /// <summary>
    /// 切换当前可见的建筑分区。
    /// 默认打开界面时会强制回到孵化区；重复点击当前分区时直接返回。
    /// </summary>
    /// <param name="category">目标分区。</param>
    /// <param name="force">是否强制切换。</param>
    private void SwitchVisibleCategory(PlayerRuntimeModule.ArchitectureCategory category, bool force)
    {
        if (!force && _currentVisibleCategory == category)
        {
            return;
        }

        _currentVisibleCategory = category;
        RefreshEntryVisibility();
        ResetEntryScrollToStart();
        ResetVisibleLevelScrollPositions();
    }

    /// <summary>
    /// 根据当前分区刷新 16 个建筑条目的显隐。
    /// 同分区条目全部显示，其他分区条目全部隐藏。
    /// </summary>
    private void RefreshEntryVisibility()
    {
        if (_entryViews == null)
        {
            return;
        }

        for (int i = 0; i < _entryViews.Length; i++)
        {
            ArchitectureEntryView entryView = _entryViews[i];
            if (entryView == null || entryView.Root == null)
            {
                continue;
            }

            bool isVisible = entryView.Category == _currentVisibleCategory;
            if (entryView.Root.gameObject.activeSelf != isVisible)
            {
                entryView.Root.gameObject.SetActive(isVisible);
            }
        }
    }

    /// <summary>
    /// 把当前可见条目内部的横向 Scroll View 滚到当前等级所在的位置。
    /// 未购买时保持在最左侧。
    /// </summary>
    private void ResetVisibleLevelScrollPositions()
    {
        if (_entryViews == null || GameEntry.Fruits == null)
        {
            return;
        }

        for (int i = 0; i < _entryViews.Length; i++)
        {
            ArchitectureEntryView entryView = _entryViews[i];
            if (entryView == null || entryView.Root == null || !entryView.Root.gameObject.activeSelf)
            {
                continue;
            }

            PlayerRuntimeModule.ArchitectureEntryState entryState =
                GameEntry.Fruits.GetArchitectureEntryState(entryView.Category, entryView.SlotIndex);
            ResetSingleLevelScrollPosition(entryView, entryState);
        }
    }

    /// <summary>
    /// 把单个条目内部横向等级列表滚到目标等级位置。
    /// 若未购买，则回到最左侧。
    /// </summary>
    /// <param name="entryView">条目视图缓存。</param>
    /// <param name="entryState">条目当前状态。</param>
    private static void ResetSingleLevelScrollPosition(
        ArchitectureEntryView entryView,
        PlayerRuntimeModule.ArchitectureEntryState entryState)
    {
        if (entryView == null
            || entryView.LevelScrollRect == null
            || entryView.LevelViewport == null
            || entryView.LevelContent == null
            || entryView.LevelIndicatorRects == null
            || entryView.LevelIndicatorRects.Length == 0)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(entryView.LevelViewport);
        LayoutRebuilder.ForceRebuildLayoutImmediate(entryView.LevelContent);
        Canvas.ForceUpdateCanvases();

        // 下标 0 = Level 1，下标 9 = Level 10。
        // 未解锁时滚到最左侧（下标 0）；已解锁时滚到当前等级（下标 = level-1）。
        int targetIndex = entryState.IsUnlocked
            ? Mathf.Clamp(entryState.Level - 1, 0, entryView.LevelIndicatorRects.Length - 1)
            : 0;

        RectTransform firstRect = entryView.LevelIndicatorRects[0];
        RectTransform targetRect = entryView.LevelIndicatorRects[targetIndex];
        if (firstRect == null || targetRect == null)
        {
            return;
        }

        float hiddenWidth = Mathf.Max(0f, entryView.LevelContent.rect.width - entryView.LevelViewport.rect.width);
        float targetOffsetX = targetRect.anchoredPosition.x - firstRect.anchoredPosition.x;
        float clampedAnchoredX = Mathf.Clamp(-targetOffsetX, -hiddenWidth, 0f);

        entryView.LevelScrollRect.StopMovement();
        entryView.LevelContent.anchoredPosition = new Vector2(clampedAnchoredX, entryView.LevelContent.anchoredPosition.y);

        if (hiddenWidth <= 0f)
        {
            entryView.LevelScrollRect.horizontalNormalizedPosition = 0f;
            return;
        }

        entryView.LevelScrollRect.horizontalNormalizedPosition = Mathf.Clamp01((-clampedAnchoredX) / hiddenWidth);
    }

    /// <summary>
    /// 把主列表滚动位置重置到起始端。
    /// 当前 prefab 主列表是纵向 ScrollRect，因此这里回到顶部。
    /// </summary>
    private void ResetEntryScrollToStart()
    {
        if (_entryScrollRect == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();

        RectTransform viewport = _entryScrollRect.viewport;
        if (viewport != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(viewport);
        }

        if (_contentRoot != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRoot);
        }

        Canvas.ForceUpdateCanvases();
        _entryScrollRect.StopMovement();
        _entryScrollRect.verticalNormalizedPosition = 1f;
        _entryScrollRect.horizontalNormalizedPosition = 0f;

        if (_contentRoot != null)
        {
            _contentRoot.anchoredPosition = Vector2.zero;
        }
    }

    /// <summary>
    /// 刷新单个建筑条目。
    /// </summary>
    /// <param name="entryView">目标条目视图缓存。</param>
    private void RefreshSingleEntry(ArchitectureEntryView entryView)
    {
        if (entryView == null)
        {
            return;
        }

        PlayerRuntimeModule.ArchitectureEntryState entryState = GameEntry.Fruits != null
            ? GameEntry.Fruits.GetArchitectureEntryState(entryView.Category, entryView.SlotIndex)
            : new PlayerRuntimeModule.ArchitectureEntryState(
                entryView.Category,
                entryView.SlotIndex,
                false,
                0,
                10,
                PlayerRuntimeModule.ArchitectureActionType.None,
                0);

        if (entryView.TitleText != null)
        {
            string titlePrefix = GetCategoryTitlePrefix(entryView.Category) + entryView.SlotIndex + "号";
            entryView.TitleText.SetText(
                entryState.IsUnlocked
                    ? titlePrefix + "\t等级:" + entryState.Level
                    : titlePrefix + "\t未购买");
        }

        RefreshLevelIndicators(entryView.Category, entryView.LevelIndicators, entryView.LevelIndicatorImages, entryView.LevelIndicatorGraphics, entryView.LevelIndicatorBaseColors, entryState.IsUnlocked, entryState.Level);
        RefreshActionButton(entryView, entryState);
    }

    /// <summary>
    /// 刷新等级指示物。
    /// 每个指示物从配置表读取对应等级的 Sprite 图片赋给 Image，
    /// 同时通过透明度表示"当前已达成到哪一级"。
    /// </summary>
    /// <param name="category">建筑类别，用于从配置表查询精灵路径。</param>
    /// <param name="levelIndicators">10 个等级指示物。</param>
    /// <param name="levelIndicatorImages">10 个等级指示物上的 Image 缓存。</param>
    /// <param name="levelIndicatorGraphics">10 个等级指示物上的 Graphic 缓存。</param>
    /// <param name="levelIndicatorBaseColors">10 个等级指示物的原始颜色缓存。</param>
    /// <param name="isUnlocked">当前槽位是否已解锁。</param>
    /// <param name="level">当前等级。</param>
    private static void RefreshLevelIndicators(
        PlayerRuntimeModule.ArchitectureCategory category,
        GameObject[] levelIndicators,
        Image[] levelIndicatorImages,
        Graphic[] levelIndicatorGraphics,
        Color[] levelIndicatorBaseColors,
        bool isUnlocked,
        int level)
    {
        if (levelIndicators == null)
        {
            return;
        }

        PlayerRuntimeModule runtimeModule = GameEntry.Fruits;

        for (int i = 0; i < levelIndicators.Length; i++)
        {
            GameObject levelIndicatorObject = levelIndicators[i];
            if (levelIndicatorObject == null)
            {
                continue;
            }

            if (!levelIndicatorObject.activeSelf)
            {
                levelIndicatorObject.SetActive(true);
            }

            // 从配置表读取对应等级的精灵并赋给 Image。
            // 下标 i 对应等级 i+1（0→Level1，9→Level10）。
            int indicatorLevel = i + 1;
            if (levelIndicatorImages != null && i < levelIndicatorImages.Length && levelIndicatorImages[i] != null && runtimeModule != null)
            {
                string spritePath = runtimeModule.GetIndicatorSpritePath(category, indicatorLevel);
                if (GameEntry.GameAssets != null
                    && !string.IsNullOrEmpty(spritePath)
                    && GameEntry.GameAssets.TryGetArchitectureSprite(spritePath, out Sprite loadedSprite)
                    && loadedSprite != null)
                {
                    levelIndicatorImages[i].sprite = loadedSprite;
                }
            }

            // 透明度逻辑：
            // 未解锁时，所有指示物 alpha=0.35。
            // 已解锁时，Level 1~level alpha=1，Level (level+1)~10 alpha=0.35。
            if (levelIndicatorGraphics == null || levelIndicatorBaseColors == null || i >= levelIndicatorGraphics.Length || i >= levelIndicatorBaseColors.Length)
            {
                continue;
            }

            Graphic levelIndicatorGraphic = levelIndicatorGraphics[i];
            if (levelIndicatorGraphic == null)
            {
                continue;
            }

            Color baseColor = levelIndicatorBaseColors[i];
            if (!isUnlocked)
            {
                // 未解锁：所有指示物暗淡
                baseColor.a = InactiveLevelIndicatorAlpha;
            }
            else
            {
                // 已解锁：Level 1~level 亮起，Level (level+1)~10 暗淡
                baseColor.a = indicatorLevel <= level ? ActiveLevelIndicatorAlpha : InactiveLevelIndicatorAlpha;
            }
            levelIndicatorGraphic.color = baseColor;
        }
    }

    /// <summary>
    /// 刷新购买/升级按钮文案和交互状态。
    /// 金币不足时不禁用按钮；只有满级时才禁用。
    /// </summary>
    /// <param name="entryView">目标条目视图缓存。</param>
    /// <param name="entryState">当前条目快照。</param>
    private static void RefreshActionButton(
        ArchitectureEntryView entryView,
        PlayerRuntimeModule.ArchitectureEntryState entryState)
    {
        if (entryView == null || entryView.ActionButton == null || entryView.ActionText == null)
        {
            return;
        }

        switch (entryState.ActionType)
        {
            case PlayerRuntimeModule.ArchitectureActionType.Buy:
                entryView.ActionButton.interactable = true;
                entryView.ActionText.SetText("购买\n({0})", entryState.Cost);
                break;

            case PlayerRuntimeModule.ArchitectureActionType.Upgrade:
                entryView.ActionButton.interactable = true;
                entryView.ActionText.SetText("升级\n({0})", entryState.Cost);
                break;

            case PlayerRuntimeModule.ArchitectureActionType.Max:
                entryView.ActionButton.interactable = false;
                entryView.ActionText.SetText("满级");
                break;

            default:
                entryView.ActionButton.interactable = false;
                entryView.ActionText.SetText("未开放");
                break;
        }
    }

    /// <summary>
    /// 根据 prefab 里的固定节点名称解析建筑条目类型和索引。
    /// </summary>
    /// <param name="entryName">例如 GoDiet (3)。</param>
    /// <param name="category">返回建筑条目类型。</param>
    /// <param name="slotIndex">返回 1 基索引。</param>
    /// <returns>是否解析成功。</returns>
    private static bool TryParseEntryIdentity(
        string entryName,
        out PlayerRuntimeModule.ArchitectureCategory category,
        out int slotIndex)
    {
        category = PlayerRuntimeModule.ArchitectureCategory.Hatch;
        slotIndex = 0;
        if (string.IsNullOrEmpty(entryName))
        {
            return false;
        }

        if (entryName.StartsWith("GoHatch ", System.StringComparison.Ordinal))
        {
            category = PlayerRuntimeModule.ArchitectureCategory.Hatch;
            return TryParseSlotIndex(entryName, out slotIndex);
        }

        if (entryName.StartsWith("GoDiet ", System.StringComparison.Ordinal))
        {
            category = PlayerRuntimeModule.ArchitectureCategory.Diet;
            return TryParseSlotIndex(entryName, out slotIndex);
        }

        if (entryName.StartsWith("GoFruiter ", System.StringComparison.Ordinal))
        {
            category = PlayerRuntimeModule.ArchitectureCategory.Fruiter;
            return TryParseSlotIndex(entryName, out slotIndex);
        }

        return false;
    }

    /// <summary>
    /// 从节点名末尾的括号中解析 1 基索引。
    /// 例如 GoDiet (6) 会解析出 6。
    /// </summary>
    /// <param name="entryName">节点名。</param>
    /// <param name="slotIndex">返回的索引。</param>
    /// <returns>是否解析成功。</returns>
    private static bool TryParseSlotIndex(string entryName, out int slotIndex)
    {
        slotIndex = 0;
        if (string.IsNullOrEmpty(entryName))
        {
            return false;
        }

        int leftParenthesisIndex = entryName.LastIndexOf('(');
        int rightParenthesisIndex = entryName.LastIndexOf(')');
        if (leftParenthesisIndex < 0 || rightParenthesisIndex <= leftParenthesisIndex + 1)
        {
            return false;
        }

        string slotIndexText = entryName.Substring(leftParenthesisIndex + 1, rightParenthesisIndex - leftParenthesisIndex - 1);
        return int.TryParse(slotIndexText, out slotIndex) && slotIndex > 0;
    }

    /// <summary>
    /// 获取建筑类型对应的中文显示名。
    /// </summary>
    /// <param name="category">建筑条目类型。</param>
    /// <returns>界面显示名。</returns>
    private static string GetCategoryDisplayName(PlayerRuntimeModule.ArchitectureCategory category)
    {
        switch (category)
        {
            case PlayerRuntimeModule.ArchitectureCategory.Hatch:
                return "孵化区";

            case PlayerRuntimeModule.ArchitectureCategory.Diet:
                return "饮食区";

            case PlayerRuntimeModule.ArchitectureCategory.Fruiter:
                return "农场区";

            default:
                return "未知区";
        }
    }

    /// <summary>
    /// 获取建筑标题前缀。
    /// 标题里不带“区”字，直接显示“孵化1号”“饮食2号”“农场3号”。
    /// </summary>
    /// <param name="category">建筑条目类型。</param>
    /// <returns>标题前缀。</returns>
    private static string GetCategoryTitlePrefix(PlayerRuntimeModule.ArchitectureCategory category)
    {
        switch (category)
        {
            case PlayerRuntimeModule.ArchitectureCategory.Hatch:
                return "孵化";

            case PlayerRuntimeModule.ArchitectureCategory.Diet:
                return "饮食";

            case PlayerRuntimeModule.ArchitectureCategory.Fruiter:
                return "农场";

            default:
                return "未知";
        }
    }
}
