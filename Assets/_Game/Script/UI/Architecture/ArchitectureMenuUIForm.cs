using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 建筑升级菜单界面（双层 Tab 版）。
/// 顶层 Tab：设施 / 水果 / 植物，使用 Select / NoSelect 槽位互换模式。
/// 设施子 Tab：孵化区 / 果园区 / 植物区，简单高亮/正常色切换。
/// 条目按钮位于 GridLayout 容器中；选中条目后在 ScrollView 中按模板克隆等级指示物。
/// 后续将替换 ArchitectureUpgradeUIForm。
/// </summary>
public sealed class ArchitectureMenuUIForm : UIFormLogic
{
    #region 常量

    /// <summary>Tab 按钮选中态染色（纯白）。</summary>
    private static readonly Color SelectedTabColor = Color.white;

    /// <summary>Tab 按钮未选中态染色（中灰）。</summary>
    private static readonly Color UnselectedTabColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    #endregion

    #region 枚举

    /// <summary>顶层 Tab 模式。</summary>
    private enum MainTabMode
    {
        Facility = 0,  // 设施（默认）
        Fruit = 1,     // 水果
        Plant = 2,     // 植物
    }

    /// <summary>设施子 Tab 模式。</summary>
    private enum SubTabMode
    {
        Hatch = 0,    // 孵化区（默认）
        Orchard = 1,  // 果园区
        Plant = 2,    // 植物区（空逻辑）
    }

    #endregion

    #region 内部类 — 条目按钮缓存

    /// <summary>GridLayout 容器中单个条目按钮的运行时缓存。</summary>
    private sealed class EntryButtonInfo
    {
        /// <summary>按钮根节点。</summary>
        public GameObject Root;

        /// <summary>按钮组件。</summary>
        public Button Button;

        /// <summary>建筑类别。</summary>
        public PlayerRuntimeModule.ArchitectureCategory Category;

        /// <summary>1 基槽位索引。</summary>
        public int SlotIndex;

        /// <summary>按钮上的文本（可选，用于刷新名称）。</summary>
        public TextMeshProUGUI LabelText;
    }

    #endregion

    #region SerializeField — 顶层 Tab

    [SerializeField] private RectTransform _goSelectParent;
    [SerializeField] private RectTransform _goNoSelectParent;
    [SerializeField] private Button _btnFacility;   // 设施（prefab 中名为 BtnHatch）
    [SerializeField] private Button _btnFruit;       // 水果（prefab 中名为 BtnDiet）
    [SerializeField] private Button _btnPlant;       // 植物（prefab 中名为 BtnFruiter）

    #endregion

    #region SerializeField — 设施子 Tab

    [SerializeField] private Button _btnSubHatch;    // 孵化区（Facility/Button）
    [SerializeField] private Button _btnSubOrchard;  // 果园区（Facility/Button (1)）
    [SerializeField] private Button _btnSubPlant;    // 植物区（Facility/Button (2)）

    #endregion

    #region SerializeField — 内容面板

    [SerializeField] private GameObject _goFacilityPanel; // GoArchitectureUpgrade/Facility
    [SerializeField] private GameObject _goFruitPanel;     // GoArchitectureUpgrade/Fruits
    [SerializeField] private GameObject _goPlantPanel;     // GoArchitectureUpgrade/Plant

    #endregion

    #region SerializeField — 内容

    /// <summary>条目按钮容器（Facility 下的 GridLayout GameObject）。</summary>
    [SerializeField] private RectTransform _entryButtonContainer;

    /// <summary>等级指示物模板（ScrollView/Content 下第一个子物体，克隆用）。</summary>
    [SerializeField] private RectTransform _levelTemplate;

    /// <summary>克隆模板的父节点（ScrollView/Viewport/Content）。</summary>
    [SerializeField] private RectTransform _contentRoot;

    /// <summary>关闭按钮。</summary>
    [SerializeField] private Button _btnClose;

    /// <summary>设施详情面板（预制体内的子物体，挂载 ArchitectureDetailPanel 脚本）。</summary>
    [SerializeField] private ArchitectureDetailPanel _architectureDetailPanel;

    /// <summary>水果图鉴面板（挂载在 GoArchitectureUpgrade/Fruits 节点上，挂载 ArchitectureFruitPanel 脚本）。</summary>
    [SerializeField] private ArchitectureFruitPanel _fruitPanel;

    #endregion

    #region 私有状态

    /// <summary>GridLayout 中所有条目按钮的缓存。</summary>
    private readonly List<EntryButtonInfo> _entryButtons = new List<EntryButtonInfo>();

    /// <summary>当前克隆出的等级指示物列表。</summary>
    private readonly List<GameObject> _clonedLevelItems = new List<GameObject>();

    private MainTabMode _currentMainTab = MainTabMode.Facility;
    private SubTabMode _currentSubTab = SubTabMode.Hatch;

    /// <summary>当前选中条目的类别。</summary>
    private PlayerRuntimeModule.ArchitectureCategory _selectedCategory;

    /// <summary>当前选中条目的槽位索引（1 基）。</summary>
    private int _selectedSlotIndex;

    /// <summary>是否有条目被选中（用于判断是否需要刷新等级列表）。</summary>
    private bool _hasSelectedEntry;

    /// <summary>当前选中条目在 _entryButtons 中的下标，-1 表示无选中。</summary>
    private int _selectedEntryIndex = -1;

    private bool _isViewReady;

    #endregion

    #region 生命周期

    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        CacheReferences();
        BuildEntryButtons();
        RegisterButtonListeners();

        // 默认：设施 → 孵化区（SwitchMainTab 内部会调 SwitchSubTab）
        SwitchMainTab(MainTabMode.Facility, true);
        _isViewReady = true;
    }

    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);

        // 每次打开都重置到默认状态（SwitchMainTab 内部会调 SwitchSubTab）
        ClearCurrentSelectedGameObject();
        ResetEntrySelectionState();
        SwitchMainTab(MainTabMode.Facility, true);
    }

    protected override void OnClose(bool isShutdown, object userData)
    {
        base.OnClose(isShutdown, userData);
        ClearCurrentSelectedGameObject();
        ResetEntrySelectionState();
    }

    private void OnDestroy()
    {
        UnregisterButtonListeners();
    }

    #endregion

    #region 引用缓存

    /// <summary>
    /// 按 prefab 实际命名自动查找所有引用。
    /// 若已在 Inspector 拖入则跳过。
    /// </summary>
    private void CacheReferences()
    {
        // --- 顶层 Tab 槽位 ---
        if (_goSelectParent == null)
        {
            _goSelectParent = transform.Find("Select") as RectTransform;
        }

        if (_goNoSelectParent == null)
        {
            _goNoSelectParent = transform.Find("NoSelect") as RectTransform;
        }

        // --- 顶层按钮（位于 Select 或 NoSelect 下）---
        if (_btnFacility == null)
        {
            Transform t = transform.Find("NoSelect/BtnHatch")
                ?? transform.Find("Select/BtnHatch");
            if (t != null) _btnFacility = t.GetComponent<Button>();
        }

        if (_btnFruit == null)
        {
            Transform t = transform.Find("NoSelect/BtnDiet")
                ?? transform.Find("Select/BtnDiet");
            if (t != null) _btnFruit = t.GetComponent<Button>();
        }

        if (_btnPlant == null)
        {
            Transform t = transform.Find("NoSelect/BtnFruiter")
                ?? transform.Find("Select/BtnFruiter");
            if (t != null) _btnPlant = t.GetComponent<Button>();
        }

        // --- 子 Tab 按钮（位于 GoArchitectureUpgrade/Facility 下）---
        if (_btnSubHatch == null)
        {
            Transform t = transform.Find("GoArchitectureUpgrade/Facility/Button");
            if (t != null) _btnSubHatch = t.GetComponent<Button>();
        }

        if (_btnSubOrchard == null)
        {
            Transform t = transform.Find("GoArchitectureUpgrade/Facility/Button (1)");
            if (t != null) _btnSubOrchard = t.GetComponent<Button>();
        }

        if (_btnSubPlant == null)
        {
            Transform t = transform.Find("GoArchitectureUpgrade/Facility/Button (2)");
            if (t != null) _btnSubPlant = t.GetComponent<Button>();
        }

        // --- 内容面板 ---
        if (_goFacilityPanel == null)
        {
            Transform t = transform.Find("GoArchitectureUpgrade/Facility");
            if (t != null) _goFacilityPanel = t.gameObject;
        }

        if (_goFruitPanel == null)
        {
            Transform t = transform.Find("GoArchitectureUpgrade/Fruits");
            if (t != null) _goFruitPanel = t.gameObject;
        }

        if (_goPlantPanel == null)
        {
            Transform t = transform.Find("GoArchitectureUpgrade/Plant");
            if (t != null) _goPlantPanel = t.gameObject;
        }

        // --- 条目按钮容器（Facility 下的 GridLayout GameObject）---
        if (_entryButtonContainer == null && _goFacilityPanel != null)
        {
            _entryButtonContainer = _goFacilityPanel.transform.Find("GameObject") as RectTransform;
        }

        // --- 克隆父节点（Facility → GameObject (1) → Scroll View → Viewport → Content）---
        if (_contentRoot == null && _goFacilityPanel != null)
        {
            _contentRoot = _goFacilityPanel.transform.Find("GameObject (1)/Scroll View/Viewport/Content") as RectTransform;
        }

        // --- 等级模板（Content 下的第一个子物体）---
        if (_levelTemplate == null && _contentRoot != null && _contentRoot.childCount > 0)
        {
            _levelTemplate = _contentRoot.GetChild(0) as RectTransform;
        }

        // --- 关闭按钮 ---
        if (_btnClose == null)
        {
            Transform t = transform.Find("BtnClose");
            if (t != null) _btnClose = t.GetComponent<Button>();
        }
    }

    #endregion

    #region 条目按钮构建

    /// <summary>
    /// 遍历 GridLayout 容器中的子节点，按名称解析类别与槽位索引并缓存。
    /// 名称约定：BtnHatch、BtnHatch (1)、BtnDiet、BtnSavingPot 等。
    /// </summary>
    private void BuildEntryButtons()
    {
        _entryButtons.Clear();

        if (_entryButtonContainer == null)
        {
            Log.Warning("ArchitectureMenuUIForm：_entryButtonContainer 为空，无法构建条目按钮缓存。");
            return;
        }

        for (int i = 0; i < _entryButtonContainer.childCount; i++)
        {
            Transform child = _entryButtonContainer.GetChild(i);
            if (child == null)
            {
                continue;
            }

            if (!TryParseEntryButtonName(child.name, out PlayerRuntimeModule.ArchitectureCategory category, out int slotIndex))
            {
                // 不是条目按钮（可能是其他 UI 元素），跳过
                continue;
            }

            Button button = child.GetComponent<Button>();
            if (button == null)
            {
                continue;
            }

            // 尝试查找按钮上的文本
            TextMeshProUGUI label = child.GetComponentInChildren<TextMeshProUGUI>(true);

            EntryButtonInfo info = new EntryButtonInfo
            {
                Root = child.gameObject,
                Button = button,
                Category = category,
                SlotIndex = slotIndex,
                LabelText = label,
            };

            _entryButtons.Add(info);
        }

    }

    /// <summary>
    /// 根据条目按钮名称解析建筑类别和槽位索引。
    /// 例如 "BtnHatch" → (Hatch, 1)，"BtnHatch (1)" → (Hatch, 2)，"BtnSavingPot" → (SavingPot, 1)。
    /// </summary>
    private static bool TryParseEntryButtonName(
        string name,
        out PlayerRuntimeModule.ArchitectureCategory category,
        out int slotIndex)
    {
        category = PlayerRuntimeModule.ArchitectureCategory.Hatch;
        slotIndex = 1;

        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        // 去掉 " (N)" 后缀
        string baseName = name;
        int parenIndex = name.IndexOf(" (", StringComparison.Ordinal);
        if (parenIndex > 0)
        {
            baseName = name.Substring(0, parenIndex);
            string suffix = name.Substring(parenIndex + 2).TrimEnd(')');
            if (int.TryParse(suffix, out int parsedIndex))
            {
                slotIndex = parsedIndex + 1; // "BtnHatch (0)" → slot 1, "BtnHatch (1)" → slot 2
            }
        }

        if (baseName == "BtnHatch")
        {
            category = PlayerRuntimeModule.ArchitectureCategory.Hatch;
            return true;
        }

        if (baseName == "BtnDiet")
        {
            category = PlayerRuntimeModule.ArchitectureCategory.Diet;
            return true;
        }

        if (baseName == "BtnFruiter")
        {
            category = PlayerRuntimeModule.ArchitectureCategory.Fruiter;
            return true;
        }

        if (baseName == "BtnSavingPot")
        {
            category = PlayerRuntimeModule.ArchitectureCategory.SavingPot;
            return true;
        }

        return false;
    }

    #endregion

    #region 按钮注册 / 注销

    private void RegisterButtonListeners()
    {
        // --- 关闭 ---
        if (_btnClose != null)
        {
            _btnClose.onClick.RemoveListener(OnCloseButtonClicked);
            _btnClose.onClick.AddListener(OnCloseButtonClicked);
        }

        // --- 顶层 Tab ---
        if (_btnFacility != null)
        {
            _btnFacility.onClick.RemoveListener(OnFacilityTabClicked);
            _btnFacility.onClick.AddListener(OnFacilityTabClicked);
        }

        if (_btnFruit != null)
        {
            _btnFruit.onClick.RemoveListener(OnFruitTabClicked);
            _btnFruit.onClick.AddListener(OnFruitTabClicked);
        }

        if (_btnPlant != null)
        {
            _btnPlant.onClick.RemoveListener(OnPlantTabClicked);
            _btnPlant.onClick.AddListener(OnPlantTabClicked);
        }

        // --- 子 Tab ---
        if (_btnSubHatch != null)
        {
            _btnSubHatch.onClick.RemoveListener(OnSubHatchClicked);
            _btnSubHatch.onClick.AddListener(OnSubHatchClicked);
        }

        if (_btnSubOrchard != null)
        {
            _btnSubOrchard.onClick.RemoveListener(OnSubOrchardClicked);
            _btnSubOrchard.onClick.AddListener(OnSubOrchardClicked);
        }

        if (_btnSubPlant != null)
        {
            _btnSubPlant.onClick.RemoveListener(OnSubPlantClicked);
            _btnSubPlant.onClick.AddListener(OnSubPlantClicked);
        }

        // --- 条目按钮 ---
        for (int i = 0; i < _entryButtons.Count; i++)
        {
            EntryButtonInfo info = _entryButtons[i];
            if (info == null || info.Button == null)
            {
                continue;
            }

            int capturedIndex = i;
            info.Button.onClick.RemoveAllListeners();
            info.Button.onClick.AddListener(() => OnEntryButtonClicked(capturedIndex));
        }
    }

    private void UnregisterButtonListeners()
    {
        if (_btnClose != null) _btnClose.onClick.RemoveListener(OnCloseButtonClicked);
        if (_btnFacility != null) _btnFacility.onClick.RemoveListener(OnFacilityTabClicked);
        if (_btnFruit != null) _btnFruit.onClick.RemoveListener(OnFruitTabClicked);
        if (_btnPlant != null) _btnPlant.onClick.RemoveListener(OnPlantTabClicked);
        if (_btnSubHatch != null) _btnSubHatch.onClick.RemoveListener(OnSubHatchClicked);
        if (_btnSubOrchard != null) _btnSubOrchard.onClick.RemoveListener(OnSubOrchardClicked);
        if (_btnSubPlant != null) _btnSubPlant.onClick.RemoveListener(OnSubPlantClicked);

        for (int i = 0; i < _entryButtons.Count; i++)
        {
            EntryButtonInfo info = _entryButtons[i];
            if (info != null && info.Button != null)
            {
                info.Button.onClick.RemoveAllListeners();
            }
        }
    }

    #endregion

    #region 顶层 Tab 切换

    /// <summary>
    /// 切换顶层 Tab。选中按钮移到 Select 槽位（白、不可点），其余移到 NoSelect 槽位（灰、可点）。
    /// </summary>
    private void SwitchMainTab(MainTabMode mode, bool force)
    {
        if (!force && _currentMainTab == mode)
        {
            return;
        }

        _currentMainTab = mode;

        // 切换顶层 Tab 时隐藏详情面板，避免残留旧数据
        if (_architectureDetailPanel != null)
        {
            _architectureDetailPanel.Hide();
        }

        // 1. 按钮槽位互换
        ReparentTabButton(_btnFacility, mode == MainTabMode.Facility ? _goSelectParent : _goNoSelectParent, mode == MainTabMode.Facility);
        ReparentTabButton(_btnFruit, mode == MainTabMode.Fruit ? _goSelectParent : _goNoSelectParent, mode == MainTabMode.Fruit);
        ReparentTabButton(_btnPlant, mode == MainTabMode.Plant ? _goSelectParent : _goNoSelectParent, mode == MainTabMode.Plant);

        // 2. 内容面板显隐
        if (_goFacilityPanel != null) _goFacilityPanel.SetActive(mode == MainTabMode.Facility);
        if (_goFruitPanel != null) _goFruitPanel.SetActive(mode == MainTabMode.Fruit);
        if (_goPlantPanel != null) _goPlantPanel.SetActive(mode == MainTabMode.Plant);

        // 2.5 水果面板激活/停用（首次 Activate 会构建列表，后续只刷新）
        if (mode == MainTabMode.Fruit)
        {
            _fruitPanel?.Activate();
        }
        else
        {
            _fruitPanel?.Deactivate();
        }

        // 3. 切换到设施时重置子 Tab；其他模式清等级列表和选中态
        if (mode == MainTabMode.Facility)
        {
            SwitchSubTab(SubTabMode.Hatch, true);
        }
        else
        {
            SetEntryButtonSelected(_selectedEntryIndex, false);
            _selectedEntryIndex = -1;
            _hasSelectedEntry = false;
            ClearClonedLevelItems();
        }

        // 4. 刷新条目按钮显隐
        RefreshEntryButtonVisibility();
    }

    private void OnFacilityTabClicked()
    {
        UIInteractionSound.PlayClick();
        SwitchMainTab(MainTabMode.Facility, false);
    }

    private void OnFruitTabClicked()
    {
        UIInteractionSound.PlayClick();
        SwitchMainTab(MainTabMode.Fruit, false);
    }

    private void OnPlantTabClicked()
    {
        UIInteractionSound.PlayClick();
        SwitchMainTab(MainTabMode.Plant, false);
    }

    /// <summary>参考 PetTJUIForm.ReparentTabButton。</summary>
    private static void ReparentTabButton(Button button, RectTransform parent, bool isSelected)
    {
        if (button == null || parent == null)
        {
            return;
        }

        RectTransform buttonRect = button.transform as RectTransform;
        if (buttonRect == null)
        {
            return;
        }

        if (buttonRect.parent != parent)
        {
            buttonRect.SetParent(parent, false);
        }

        buttonRect.SetAsLastSibling();
        button.interactable = !isSelected;

        Image graphic = button.targetGraphic as Image;
        if (graphic == null)
        {
            graphic = button.GetComponent<Image>();
        }

        if (graphic != null)
        {
            Color target = isSelected ? SelectedTabColor : UnselectedTabColor;
            if (graphic.color != target)
            {
                graphic.color = target;
            }
        }
    }

    #endregion

    #region 设施子 Tab 切换

    private void SwitchSubTab(SubTabMode mode, bool force)
    {
        if (!force && _currentSubTab == mode)
        {
            return;
        }

        _currentSubTab = mode;

        // 刷新子 Tab 高亮
        RefreshSubTabHighlights();

        // 刷新条目按钮显隐
        RefreshEntryButtonVisibility();

        // 切换子 Tab 时清空之前的等级列表和选中状态
        SetEntryButtonSelected(_selectedEntryIndex, false);
        _hasSelectedEntry = false;
        _selectedEntryIndex = -1;
        ClearClonedLevelItems();

        // 自动选中第一个可见条目按钮
        AutoSelectFirstVisibleEntry();
    }

    /// <summary>子 Tab 按钮高亮：选中 = 白色不可点，未选中 = 灰色可点。</summary>
    private void RefreshSubTabHighlights()
    {
        ApplySubTabHighlight(_btnSubHatch, _currentSubTab == SubTabMode.Hatch);
        ApplySubTabHighlight(_btnSubOrchard, _currentSubTab == SubTabMode.Orchard);
        ApplySubTabHighlight(_btnSubPlant, _currentSubTab == SubTabMode.Plant);
    }

    private static void ApplySubTabHighlight(Button button, bool isSelected)
    {
        if (button == null) return;

        button.interactable = !isSelected;

        Image graphic = button.targetGraphic as Image;
        if (graphic == null) graphic = button.GetComponent<Image>();
        if (graphic != null)
        {
            Color target = isSelected ? SelectedTabColor : UnselectedTabColor;
            if (graphic.color != target) graphic.color = target;
        }
    }

    private void OnSubHatchClicked()
    {
        UIInteractionSound.PlayClick();
        SwitchSubTab(SubTabMode.Hatch, false);
    }

    private void OnSubOrchardClicked()
    {
        UIInteractionSound.PlayClick();
        SwitchSubTab(SubTabMode.Orchard, false);
    }

    private void OnSubPlantClicked()
    {
        UIInteractionSound.PlayClick();
        SwitchSubTab(SubTabMode.Plant, false);
    }

    #endregion

    #region 条目按钮显隐

    /// <summary>
    /// 根据当前顶层 Tab 和子 Tab 刷新 GridLayout 中条目按钮的显隐。
    ///
    /// 设施模式：
    ///   孵化区 → BtnHatch* + BtnDiet* + BtnSavingPot
    ///   果园区 → BtnFruiter*
    ///   植物区 → 全部隐藏
    ///
    /// 水果/植物模式 → 全部隐藏。
    /// </summary>
    private void RefreshEntryButtonVisibility()
    {
        bool isFacilityMode = _currentMainTab == MainTabMode.Facility;

        for (int i = 0; i < _entryButtons.Count; i++)
        {
            EntryButtonInfo info = _entryButtons[i];
            if (info == null || info.Root == null)
            {
                continue;
            }

            bool visible = false;

            if (isFacilityMode)
            {
                switch (_currentSubTab)
                {
                    case SubTabMode.Hatch:
                        visible = info.Category == PlayerRuntimeModule.ArchitectureCategory.Hatch
                            || info.Category == PlayerRuntimeModule.ArchitectureCategory.Diet
                            || info.Category == PlayerRuntimeModule.ArchitectureCategory.SavingPot;
                        break;

                    case SubTabMode.Orchard:
                        visible = info.Category == PlayerRuntimeModule.ArchitectureCategory.Fruiter;
                        break;

                    case SubTabMode.Plant:
                        visible = false;
                        break;
                }
            }

            if (info.Root.activeSelf != visible)
            {
                info.Root.SetActive(visible);
            }
        }
    }

    #endregion

    #region 条目按钮点击 → 克隆等级模板

    /// <summary>遍历条目按钮，自动选中第一个可见的（不播音效）。</summary>
    private void AutoSelectFirstVisibleEntry()
    {
        for (int i = 0; i < _entryButtons.Count; i++)
        {
            EntryButtonInfo info = _entryButtons[i];
            if (info == null || info.Root == null || !info.Root.activeSelf)
            {
                continue;
            }

            SelectEntryButton(i, false);
            return;
        }

        // 没有可见条目（如植物区），清空状态
        SetEntryButtonSelected(_selectedEntryIndex, false);
        _selectedEntryIndex = -1;
        _hasSelectedEntry = false;
        ClearClonedLevelItems();
    }

    /// <summary>选中指定条目按钮，切换视觉并重建等级列表。</summary>
    private void SelectEntryButton(int entryIndex, bool playSound)
    {
        if (entryIndex < 0 || entryIndex >= _entryButtons.Count)
        {
            return;
        }

        EntryButtonInfo info = _entryButtons[entryIndex];
        if (info == null)
        {
            return;
        }

        // 重复选中同一个不做任何事
        if (_selectedEntryIndex == entryIndex)
        {
            return;
        }

        if (playSound)
        {
            UIInteractionSound.PlayClick();
        }

        SetEntryButtonSelected(_selectedEntryIndex, false);

        _selectedCategory = info.Category;
        _selectedSlotIndex = info.SlotIndex;
        _selectedEntryIndex = entryIndex;
        _hasSelectedEntry = true;

        SetEntryButtonSelected(entryIndex, true);
        RebuildLevelItems();
    }

    /// <summary>切换单个条目按钮的选中/未选中视觉。[0]=未选中图, [1]=选中图。</summary>
    private void SetEntryButtonSelected(int entryIndex, bool selected)
    {
        if (entryIndex < 0 || entryIndex >= _entryButtons.Count)
        {
            return;
        }

        EntryButtonInfo info = _entryButtons[entryIndex];
        if (info == null || info.Root == null)
        {
            return;
        }

        Transform root = info.Root.transform;
        if (root.childCount >= 2)
        {
            Transform unselectedImg = root.GetChild(0); // 未选中
            Transform selectedImg = root.GetChild(1);   // 选中
            if (unselectedImg != null) unselectedImg.gameObject.SetActive(!selected);
            if (selectedImg != null) selectedImg.gameObject.SetActive(selected);
        }
    }

    /// <summary>
    /// 清空所有建筑条目按钮的选中视觉和运行时选中记录。
    /// 关闭再打开界面时必须全量清理，避免旧条目的选中子节点残留，和新默认条目同时显示为选中。
    /// </summary>
    private void ResetEntrySelectionState()
    {
        for (int i = 0; i < _entryButtons.Count; i++)
        {
            SetEntryButtonSelected(i, false);
        }

        _selectedEntryIndex = -1;
        _hasSelectedEntry = false;
    }

    /// <summary>
    /// 清空 Unity EventSystem 当前选中对象。
    /// Button 点击后会成为 EventSystem.currentSelectedGameObject；若关闭按钮残留为 Selected 状态，
    /// 下次打开界面时它可能继续显示选中视觉，因此关闭和打开阶段都主动清掉。
    /// </summary>
    private static void ClearCurrentSelectedGameObject()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null || eventSystem.currentSelectedGameObject == null)
        {
            return;
        }

        eventSystem.SetSelectedGameObject(null);
    }

    /// <summary>
    /// 条目按钮点击回调（播音效）。
    /// </summary>
    private void OnEntryButtonClicked(int entryIndex)
    {
        SelectEntryButton(entryIndex, true);
    }

    /// <summary>
    /// 清空所有克隆的等级指示物，按当前选中建筑的等级数重新克隆模板。
    /// </summary>
    private void RebuildLevelItems()
    {
        ClearClonedLevelItems();

        if (!_hasSelectedEntry || _levelTemplate == null || _contentRoot == null)
        {
            return;
        }

        // 隐藏模板本体
        _levelTemplate.gameObject.SetActive(false);

        // 强制刷新 Canvas，确保刚 SetActive 的面板布局就绪
        Canvas.ForceUpdateCanvases();

        // 获取建筑状态
        int currentLevel = 0;
        int maxLevel = 10;
        bool isUnlocked = false;

        if (GameEntry.Fruits != null)
        {
            PlayerRuntimeModule.ArchitectureEntryState state =
                GameEntry.Fruits.GetArchitectureEntryState(_selectedCategory, _selectedSlotIndex);
            currentLevel = state.Level;
            maxLevel = state.MaxLevel;
            isUnlocked = state.IsUnlocked;
        }

        // 为每个等级克隆一个模板实例
        for (int level = 1; level <= maxLevel; level++)
        {
            RectTransform clone = Instantiate(_levelTemplate, _contentRoot);
            clone.gameObject.SetActive(true);
            clone.name = string.Format("Level_{0}", level);
            _clonedLevelItems.Add(clone.gameObject);

            // 为克隆出的等级条目注册点击回调（搜索整棵子树，兜底 AddComponent）
            int capturedLevel = level;
            Button levelItemBtn = clone.GetComponentInChildren<Button>(true);
            if (levelItemBtn == null)
            {
                levelItemBtn = clone.gameObject.AddComponent<Button>();
            }
            levelItemBtn.interactable = true; // 强制可点击（模板可能默认 Interactable=false）
            levelItemBtn.onClick.RemoveAllListeners();
            levelItemBtn.onClick.AddListener(() => OnLevelItemClicked(capturedLevel));

            // 子物体索引约定：[0] Image, [1] 金币花费, [2] 已解锁标记
            bool isLevelUnlocked = isUnlocked && level <= currentLevel;

            Transform child0 = clone.GetChild(0); // Image — 建筑图标
            Transform child1 = clone.childCount > 1 ? clone.GetChild(1) : null; // 金币花费
            Transform child2 = clone.childCount > 2 ? clone.GetChild(2) : null; // 已解锁标记

            // 设置图标（从配置表读取 Sprite）
            if (child0 != null)
            {
                Image iconImage = child0.GetComponent<Image>();
                if (iconImage != null && GameEntry.Fruits != null)
                {
                    string spritePath = GameEntry.Fruits.GetIndicatorSpritePath(_selectedCategory, level);
                    if (GameEntry.GameAssets != null
                        && !string.IsNullOrEmpty(spritePath)
                        && GameEntry.GameAssets.TryGetArchitectureSprite(spritePath, out Sprite loadedSprite)
                        && loadedSprite != null)
                    {
                        iconImage.sprite = loadedSprite;
                    }
                }
            }

            // 已解锁 → 隐藏金币花费，显示已解锁标记
            // 未解锁 → 显示金币花费（从配置表查该等级对应的解锁/升级价格）
            if (child1 != null)
            {
                child1.gameObject.SetActive(!isLevelUnlocked);

                if (!isLevelUnlocked)
                {
                    int levelCost = GetLevelCost(_selectedCategory, _selectedSlotIndex, level);
                    TextMeshProUGUI costText = child1.GetComponentInChildren<TextMeshProUGUI>(true);
                    if (costText != null)
                    {
                        costText.SetText("{0}", levelCost);
                    }
                }
            }

            if (child2 != null)
            {
                child2.gameObject.SetActive(isLevelUnlocked);
            }
        }

        // 强制刷新布局：Content + Viewport + ScrollRect 逐层重建
        if (_contentRoot != null)
        {
            // Content 本身
            LayoutRebuilder.ForceRebuildLayoutImmediate(_contentRoot);

            // Viewport（Content 的父节点）
            RectTransform viewport = _contentRoot.parent as RectTransform;
            if (viewport != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(viewport);
            }

            // ScrollRect（触发滚动区域更新）
            ScrollRect scrollRect = _contentRoot.GetComponentInParent<ScrollRect>();
            if (scrollRect != null)
            {
                Canvas.ForceUpdateCanvases();
                scrollRect.horizontalNormalizedPosition = 0f;
            }
        }
    }

    /// <summary>清空所有克隆的等级指示物。</summary>
    private void ClearClonedLevelItems()
    {
        // 清空克隆条目时同步隐藏详情面板，防止残留旧数据
        if (_architectureDetailPanel != null)
        {
            _architectureDetailPanel.Hide();
        }

        for (int i = _clonedLevelItems.Count - 1; i >= 0; i--)
        {
            GameObject item = _clonedLevelItems[i];
            if (item != null)
            {
                Destroy(item);
            }
        }

        _clonedLevelItems.Clear();

        // 恢复模板显隐（模板本身保持隐藏，等待下次克隆时再隐藏；这里确保模板可见性正确）
        if (_levelTemplate != null)
        {
            _levelTemplate.gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// 查询指定建筑的某一等级解锁/升级所需金币。
    /// Level 1 查 ArchitectureSlotDataRow.UnlockGold（存钱罐解锁固定 0）；
    /// Level 2+ 查 ArchitectureUpgradeDataRow.UpgradeGold（存钱罐查 SavingPotDataRow.UpgradeGold）。
    /// </summary>
    private static int GetLevelCost(PlayerRuntimeModule.ArchitectureCategory category, int slotIndex, int level)
    {
        if (level <= 0)
        {
            return 0;
        }

        bool isSavingPot = category == PlayerRuntimeModule.ArchitectureCategory.SavingPot;

        if (level == 1)
        {
            // 存钱罐初始解锁，无购买价格
            if (isSavingPot)
            {
                return 0;
            }

            // 普通建筑解锁花费：查 ArchitectureSlotDataRow
            ArchitectureSlotDataRow[] slotRows = GameEntry.DataTables != null
                ? GameEntry.DataTables.GetAllDataRows<ArchitectureSlotDataRow>()
                : null;
            if (slotRows != null)
            {
                for (int i = 0; i < slotRows.Length; i++)
                {
                    ArchitectureSlotDataRow row = slotRows[i];
                    if (row != null && row.Category == category && row.SlotIndex == slotIndex)
                    {
                        return row.UnlockGold;
                    }
                }
            }

            return 0;
        }

        // 升级花费
        if (isSavingPot)
        {
            // 存钱罐升级：查 SavingPotDataRow（CurrentLevel 即目标等级）
            SavingPotDataRow[] savingPotRows = GameEntry.DataTables != null
                ? GameEntry.DataTables.GetAllDataRows<SavingPotDataRow>()
                : null;
            if (savingPotRows != null)
            {
                for (int i = 0; i < savingPotRows.Length; i++)
                {
                    SavingPotDataRow row = savingPotRows[i];
                    if (row != null && row.CurrentLevel == level)
                    {
                        return row.UpgradeGold;
                    }
                }
            }

            return 0;
        }

        // 普通建筑升级：查 ArchitectureUpgradeDataRow（CurrentLevel = level - 1）
        ArchitectureUpgradeDataRow[] upgradeRows = GameEntry.DataTables != null
            ? GameEntry.DataTables.GetAllDataRows<ArchitectureUpgradeDataRow>()
            : null;
        if (upgradeRows != null)
        {
            int fromLevel = level - 1;
            for (int i = 0; i < upgradeRows.Length; i++)
            {
                ArchitectureUpgradeDataRow row = upgradeRows[i];
                if (row != null && row.Category == category && row.SlotIndex == slotIndex && row.CurrentLevel == fromLevel)
                {
                    return row.UpgradeGold;
                }
            }
        }

        return 0;
    }

    #endregion

    #region 关闭

    private void OnCloseButtonClicked()
    {
        UIInteractionSound.PlayClick();
        ClearCurrentSelectedGameObject();

        // 关闭菜单时同步隐藏详情面板
        if (_architectureDetailPanel != null)
        {
            _architectureDetailPanel.Hide();
        }

        if (UIForm == null || GameEntry.UI == null)
        {
            return;
        }

        GameEntry.UI.CloseUIForm(UIForm.SerialId);
    }

    #endregion

    #region 详情面板

    /// <summary>
    /// 等级条目点击回调：打开设施详情面板，展示对应等级的详细信息。
    /// </summary>
    /// <param name="level">被点击的等级（1 基）。</param>
    private void OnLevelItemClicked(int level)
    {
        UIInteractionSound.PlayClick();

        if (_architectureDetailPanel == null) return;

        string facilityName;
        switch (_selectedCategory)
        {
            case PlayerRuntimeModule.ArchitectureCategory.Hatch:    facilityName = "孵化器"; break;
            case PlayerRuntimeModule.ArchitectureCategory.Diet:     facilityName = "餐桌";   break;
            case PlayerRuntimeModule.ArchitectureCategory.Fruiter:  facilityName = "果树";   break;
            case PlayerRuntimeModule.ArchitectureCategory.SavingPot: facilityName = "存钱罐"; break;
            default: facilityName = ""; break;
        }

        _architectureDetailPanel.Show(_selectedCategory, _selectedSlotIndex, level, facilityName);

        // 注入解锁回调，详情面板点击解锁按钮时由这里统一处理购买逻辑
        _architectureDetailPanel.OnUnlockRequested = OnDetailUnlockRequested;
    }

    /// <summary>
    /// 详情面板解锁按钮回调：执行购买/升级并刷新主界面。
    /// 调用 PlayerRuntimeModule.TryExecuteArchitectureAction，
    /// 成功后刷新等级列表；失败时通过 ToastUtility 提示原因。
    /// </summary>
    /// <param name="category">建筑类别。</param>
    /// <param name="slotIndex">1 基槽位索引。</param>
    private void OnDetailUnlockRequested(
        PlayerRuntimeModule.ArchitectureCategory category, int slotIndex)
    {
        if (GameEntry.Fruits == null) return;

        if (GameEntry.Fruits.TryExecuteArchitectureAction(category, slotIndex,
                out PlayerRuntimeModule.ArchitectureActionFailureReason reason))
        {
            // 解锁/升级成功：刷新等级列表（ClearClonedLevelItems 内部已同步隐藏详情面板）
            RebuildLevelItems();
            return;
        }

        // 失败提示
        switch (reason)
        {
            case PlayerRuntimeModule.ArchitectureActionFailureReason.NotEnoughStars:
                ToastUtility.Show("星星不足");
                break;
            case PlayerRuntimeModule.ArchitectureActionFailureReason.NotEnoughGold:
                ToastUtility.Show("金币不足");
                break;
            default:
                ToastUtility.Show("当前无法操作");
                break;
        }
    }

    #endregion
}
