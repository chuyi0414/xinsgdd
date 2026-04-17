using System;
using System.Collections.Generic;
using Spine;
using Spine.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 宠物图鉴界面。
/// 负责展示宠物品质分页、宠物列表、详情面板以及当前解锁状态。
/// 顶层预制体引用全部改为 Inspector 手动拖入，不再依赖全局路径查找。
/// </summary>
public sealed class PetTJUIForm : UIFormLogic
{
    /// <summary>
    /// 未解锁宠物的显示颜色。
    /// 这里直接压成纯黑色，用于表达“图鉴未点亮”。
    /// </summary>
    private static readonly Color32 LockedPetColor = new Color32(0, 0, 0, 255);

    /// <summary>
    /// 已解锁宠物的显示颜色。
    /// 使用纯白色，让角色以原始贴图颜色输出。
    /// </summary>
    private static readonly Color32 UnlockedPetColor = new Color32(255, 255, 255, 255);

    /// <summary>
    /// 品质分页的固定顺序。
    /// 数组索引会和品质按钮缓存、点击回调索引严格对应。
    /// </summary>
    private static readonly QualityType[] QualityOrders = { QualityType.Normal, QualityType.Rare, QualityType.Epic, QualityType.Legendary, QualityType.Mythic };

    /// <summary>
    /// 单个宠物列表条目的运行时缓存。
    /// </summary>
    private sealed class PetItemEntry
    {
        /// <summary>
        /// 条目根节点。
        /// 用于控制显隐以及作为按钮挂载对象。
        /// </summary>
        public GameObject Root;

        /// <summary>
        /// 条目按钮。
        /// 点击后会打开或关闭对应宠物的详情面板。
        /// </summary>
        public Button Button;

        /// <summary>
        /// 条目名称文本。
        /// 用于显示当前宠物名称。
        /// </summary>
        public TextMeshProUGUI TxtName;

        /// <summary>
        /// 宠物 Spine 图像挂点。
        /// 运行时创建的 SkeletonGraphic 会挂到这里。
        /// </summary>
        public Transform PetRoot;

        /// <summary>
        /// 条目复用的 SkeletonGraphic。
        /// 首次创建后会一直复用，避免重复生成 UI 角色。
        /// </summary>
        public SkeletonGraphic PetGraphic;

        /// <summary>
        /// 当前条目绑定的数据行。
        /// 点击条目时直接使用这份缓存打开详情。
        /// </summary>
        public PetDataRow DataRow;
    }

    /// <summary>
    /// 单个品质页签的运行时缓存。
    /// </summary>
    private sealed class QualityTabView
    {
        /// <summary>
        /// 当前页签对应的宠物品质。
        /// </summary>
        public QualityType Quality;

        /// <summary>
        /// 页签按钮本体。
        /// 用户在 Inspector 中手动拖入。
        /// </summary>
        public Button Button;

        /// <summary>
        /// 页签背景图。
        /// 用于在选中态和未选中态之间切换 sprite。
        /// </summary>
        public Image Background;

        /// <summary>
        /// 页签文字组件。
        /// 用于在选中态和未选中态之间切换颜色。
        /// </summary>
        public TextMeshProUGUI Text;
    }

    /// <summary>
    /// 详情面板的运行时缓存。
    /// 顶层组件由 Inspector 手拖，Spine 图像仍在运行时创建并复用。
    /// </summary>
    private sealed class PetDetailView
    {
        /// <summary>
        /// 详情面板根节点。
        /// </summary>
        public GameObject Root;

        /// <summary>
        /// 详情面板关闭按钮。
        /// </summary>
        public Button CloseButton;

        /// <summary>
        /// 宠物名称文本。
        /// </summary>
        public TextMeshProUGUI TxtName;

        /// <summary>
        /// 宠物品质文本。
        /// </summary>
        public TextMeshProUGUI TxtQuality;

        /// <summary>
        /// 宠物属性文本。
        /// </summary>
        public TextMeshProUGUI TxtProperty;

        /// <summary>
        /// 宠物介绍文本。
        /// </summary>
        public TextMeshProUGUI TxtIntroduce;

        /// <summary>
        /// 详情宠物 Spine 图像挂点。
        /// </summary>
        public Transform PetRoot;

        /// <summary>
        /// 详情面板复用的 SkeletonGraphic。
        /// </summary>
        public SkeletonGraphic PetGraphic;

        /// <summary>
        /// 当前详情面板正在展示的数据行。
        /// 再次点击同一只宠物时，用它判断是否反向关闭详情。
        /// </summary>
        public PetDataRow CurrentDataRow;
    }

    /// <summary>
    /// 所有已创建的列表条目缓存。
    /// 列表首次构建后会一直复用这一批条目对象。
    /// </summary>
    private readonly List<PetItemEntry> _entries = new List<PetItemEntry>(16);

    /// <summary>
    /// 当前筛选品质下可见的数据行缓存。
    /// 切换品质时先重建这个列表，再把前 N 个条目绑定到这些数据行。
    /// </summary>
    private readonly List<PetDataRow> _visibleRows = new List<PetDataRow>(16);

    /// <summary>
    /// 五个品质页签的运行时缓存数组。
    /// 数组索引严格对应 QualityOrders 的顺序。
    /// </summary>
    private readonly QualityTabView[] _qualityTabs = new QualityTabView[5];

    /// <summary>
    /// 列表 Content 容器。
    /// 用户需要在 Inspector 中把 Scroll View/Viewport/Content 拖进来。
    /// </summary>
    [SerializeField]
    private RectTransform _content;

    /// <summary>
    /// 列表条目模板。
    /// 用户需要在 Inspector 中把 Content 下的 GoPet 模板拖进来。
    /// </summary>
    [SerializeField]
    private Transform _goPetTemplate;

    /// <summary>
    /// 背景关闭点击区域。
    /// 用户需要在 Inspector 中把 BJ 节点拖进来。
    /// </summary>
    [SerializeField]
    private GameObject _goBackgroundCloseTarget;

    /// <summary>
    /// 普通品质页签按钮。
    /// 用户需要在 Inspector 中手动拖入 Quality/Button。
    /// </summary>
    [SerializeField]
    private Button _btnQualityNormal;

    /// <summary>
    /// 稀有品质页签按钮。
    /// 用户需要在 Inspector 中手动拖入 Quality/Button (1)。
    /// </summary>
    [SerializeField]
    private Button _btnQualityRare;

    /// <summary>
    /// 史诗品质页签按钮。
    /// 用户需要在 Inspector 中手动拖入 Quality/Button (2)。
    /// </summary>
    [SerializeField]
    private Button _btnQualityEpic;

    /// <summary>
    /// 传说品质页签按钮。
    /// 用户需要在 Inspector 中手动拖入 Quality/Button (3)。
    /// </summary>
    [SerializeField]
    private Button _btnQualityLegendary;

    /// <summary>
    /// 神话品质页签按钮。
    /// 用户需要在 Inspector 中手动拖入 Quality/Button (4)。
    /// </summary>
    [SerializeField]
    private Button _btnQualityMythic;

    /// <summary>
    /// 详情面板根节点。
    /// 用户需要在 Inspector 中把 GoPetDetailed 拖进来。
    /// </summary>
    [SerializeField]
    private GameObject _goDetailRoot;

    /// <summary>
    /// 详情面板关闭按钮。
    /// 用户需要在 Inspector 中把 GoPetDetailed 上的 Button 组件拖进来。
    /// </summary>
    [SerializeField]
    private Button _btnDetailClose;

    /// <summary>
    /// 详情面板宠物名称文本。
    /// 用户需要在 Inspector 中把 PetDetailed/TxtName 拖进来。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtDetailName;

    /// <summary>
    /// 详情面板宠物品质文本。
    /// 用户需要在 Inspector 中把 PetDetailed/TxtQuality 拖进来。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtDetailQuality;

    /// <summary>
    /// 详情面板宠物属性文本。
    /// 用户需要在 Inspector 中把 PetDetailed/TxtProperty 拖进来。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtDetailProperty;

    /// <summary>
    /// 详情面板宠物介绍文本。
    /// 用户需要在 Inspector 中把 PetDetailed/TxtIntroduce 拖进来。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtDetailIntroduce;

    /// <summary>
    /// 详情面板宠物 Spine 挂点。
    /// 用户需要在 Inspector 中把 PetDetailed/Pet 拖进来。
    /// </summary>
    [SerializeField]
    private Transform _trDetailPetRoot;

    /// <summary>
    /// 背景关闭按钮缓存。
    /// 来自 _goBackgroundCloseTarget 上的 Button 组件，没有就运行时补一个。
    /// </summary>
    private Button _btnBackgroundClose;

    /// <summary>
    /// 详情面板缓存。
    /// 用于统一保存详情区 GameObject、文字组件以及详情 Spine 图像。
    /// </summary>
    private readonly PetDetailView _detailView = new PetDetailView();

    /// <summary>
    /// 所有宠物数据行缓存。
    /// 列表首次构建时从数据表取出并排序，后续只读不重复取表。
    /// </summary>
    private PetDataRow[] _allRows = Array.Empty<PetDataRow>();

    /// <summary>
    /// 当前选中的品质分页。
    /// 打开界面时默认重置为普通品质。
    /// </summary>
    private QualityType _currentQuality = QualityType.Normal;

    /// <summary>
    /// 选中态页签背景图。
    /// 首次从已绑定按钮里推断并缓存。
    /// </summary>
    private Sprite _selectedTabSprite;

    /// <summary>
    /// 未选中态页签背景图。
    /// 首次从已绑定按钮里推断并缓存。
    /// </summary>
    private Sprite _unselectedTabSprite;

    /// <summary>
    /// 是否已经缓存到选中态文字颜色。
    /// </summary>
    private bool _hasSelectedTabTextColor;

    /// <summary>
    /// 是否已经缓存到未选中态文字颜色。
    /// </summary>
    private bool _hasUnselectedTabTextColor;

    /// <summary>
    /// 列表是否已经构建完成。
    /// 首次打开构建一次，后续只刷新显示。
    /// </summary>
    private bool _isListBuilt;

    /// <summary>
    /// 按钮事件是否已经绑定。
    /// 防止界面重复打开时把同一个监听重复 Add 多次。
    /// </summary>
    private bool _eventsBound;

    /// <summary>
    /// 是否已经输出过“缺少序列化引用”的警告。
    /// 缺引用时只打一轮日志，避免同一次打开刷屏。
    /// </summary>
    private bool _hasLoggedMissingReferenceWarning;

    /// <summary>
    /// 初始化界面。
    /// 这里只做显式引用缓存和一次性事件绑定，不做业务数据刷新。
    /// </summary>
    /// <param name="userData">打开窗体时传入的自定义参数。</param>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        if (!EnsureSerializedReferencesReady())
        {
            return;
        }

        BindEventsOnce();
    }

    /// <summary>
    /// 打开界面时刷新当前品质、页签状态、列表内容以及详情面板显隐。
    /// </summary>
    /// <param name="userData">打开窗体时传入的自定义参数。</param>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        if (!EnsureSerializedReferencesReady())
        {
            return;
        }

        BindEventsOnce();
        BuildList();
        _currentQuality = QualityType.Normal;
        RefreshTabs();
        RefreshList();
        HideDetail();
    }

    /// <summary>
    /// 确保所有 Inspector 手拖字段都已经可用，并把它们写入运行时缓存。
    /// 字段未绑定时直接停止后续流程，不再回退到 transform.Find 的旧方案。
    /// </summary>
    /// <returns>是否已具备安全打开界面所需的全部引用。</returns>
    private bool EnsureSerializedReferencesReady()
    {
        CacheReferencesFromSerializedFields();
        if (HasAllRequiredSerializedReferences())
        {
            _hasLoggedMissingReferenceWarning = false;
            return true;
        }

        if (!_hasLoggedMissingReferenceWarning)
        {
            LogMissingSerializedReferenceWarnings();
            _hasLoggedMissingReferenceWarning = true;
        }

        return false;
    }

    /// <summary>
    /// 把 Inspector 手拖字段写入运行时缓存。
    /// 这里不做全局节点路径查找，只允许在“已经手拖进来的节点内部”取局部子组件。
    /// </summary>
    private void CacheReferencesFromSerializedFields()
    {
        if (_btnBackgroundClose == null && _goBackgroundCloseTarget != null)
        {
            _btnBackgroundClose = GetOrAddButton(_goBackgroundCloseTarget);
        }

        _detailView.Root = _goDetailRoot;
        _detailView.CloseButton = _btnDetailClose;
        _detailView.TxtName = _txtDetailName;
        _detailView.TxtQuality = _txtDetailQuality;
        _detailView.TxtProperty = _txtDetailProperty;
        _detailView.TxtIntroduce = _txtDetailIntroduce;
        _detailView.PetRoot = _trDetailPetRoot;

        CacheQualityTab(0, _btnQualityNormal, QualityOrders[0]);
        CacheQualityTab(1, _btnQualityRare, QualityOrders[1]);
        CacheQualityTab(2, _btnQualityEpic, QualityOrders[2]);
        CacheQualityTab(3, _btnQualityLegendary, QualityOrders[3]);
        CacheQualityTab(4, _btnQualityMythic, QualityOrders[4]);
        CacheTabVisualState();
    }

    /// <summary>
    /// 把单个品质按钮缓存到指定页签槽位里。
    /// Button 本体由用户手拖，背景图和文本组件再从按钮节点内部做局部查找。
    /// </summary>
    /// <param name="index">品质页签数组索引。</param>
    /// <param name="button">用户手拖进来的按钮引用。</param>
    /// <param name="quality">当前按钮对应的品质枚举。</param>
    private void CacheQualityTab(int index, Button button, QualityType quality)
    {
        if (index < 0 || index >= _qualityTabs.Length)
        {
            return;
        }

        QualityTabView tab = _qualityTabs[index] ?? (_qualityTabs[index] = new QualityTabView());
        tab.Quality = quality;
        tab.Button = button;
        tab.Background = null;
        tab.Text = null;

        if (button == null)
        {
            return;
        }

        Transform background = button.transform.Find("ImgBtn");
        if (background != null)
        {
            tab.Background = background.GetComponent<Image>();
        }

        Transform text = button.transform.Find("Text (TMP)");
        if (text != null)
        {
            tab.Text = text.GetComponent<TextMeshProUGUI>();
        }
    }

    /// <summary>
    /// 判断所有打开界面必需的手拖字段是否齐全。
    /// 这里故意严格校验，防止旧 prefab 没拖字段时还悄悄依赖运行时兜底逻辑。
    /// </summary>
    /// <returns>所有必需字段都存在时返回 true。</returns>
    private bool HasAllRequiredSerializedReferences()
    {
        return _content != null
            && _goPetTemplate != null
            && _goBackgroundCloseTarget != null
            && _btnBackgroundClose != null
            && _btnQualityNormal != null
            && _btnQualityRare != null
            && _btnQualityEpic != null
            && _btnQualityLegendary != null
            && _btnQualityMythic != null
            && _goDetailRoot != null
            && _btnDetailClose != null
            && _txtDetailName != null
            && _txtDetailQuality != null
            && _txtDetailProperty != null
            && _txtDetailIntroduce != null
            && _trDetailPetRoot != null;
    }

    /// <summary>
    /// 输出缺少绑定时的中文警告。
    /// 这些日志会直接告诉使用者应该把 prefab 上哪个节点拖到哪个字段里。
    /// </summary>
    private void LogMissingSerializedReferenceWarnings()
    {
        if (_content == null)
        {
            Log.Warning("PetTJUIForm 缺少 _content 引用，请在 Inspector 中把 Scroll View/Viewport/Content 拖入。");
        }

        if (_goPetTemplate == null)
        {
            Log.Warning("PetTJUIForm 缺少 _goPetTemplate 引用，请在 Inspector 中把 Content/GoPet 模板拖入。");
        }

        if (_goBackgroundCloseTarget == null)
        {
            Log.Warning("PetTJUIForm 缺少 _goBackgroundCloseTarget 引用，请在 Inspector 中把 BJ 节点拖入。");
        }

        if (_btnQualityNormal == null)
        {
            Log.Warning("PetTJUIForm 缺少 _btnQualityNormal 引用，请在 Inspector 中把 Quality/Button 拖入。");
        }

        if (_btnQualityRare == null)
        {
            Log.Warning("PetTJUIForm 缺少 _btnQualityRare 引用，请在 Inspector 中把 Quality/Button (1) 拖入。");
        }

        if (_btnQualityEpic == null)
        {
            Log.Warning("PetTJUIForm 缺少 _btnQualityEpic 引用，请在 Inspector 中把 Quality/Button (2) 拖入。");
        }

        if (_btnQualityLegendary == null)
        {
            Log.Warning("PetTJUIForm 缺少 _btnQualityLegendary 引用，请在 Inspector 中把 Quality/Button (3) 拖入。");
        }

        if (_btnQualityMythic == null)
        {
            Log.Warning("PetTJUIForm 缺少 _btnQualityMythic 引用，请在 Inspector 中把 Quality/Button (4) 拖入。");
        }

        if (_goDetailRoot == null)
        {
            Log.Warning("PetTJUIForm 缺少 _goDetailRoot 引用，请在 Inspector 中把 GoPetDetailed 拖入。");
        }

        if (_btnDetailClose == null)
        {
            Log.Warning("PetTJUIForm 缺少 _btnDetailClose 引用，请在 Inspector 中把 GoPetDetailed 上的 Button 组件拖入。");
        }

        if (_txtDetailName == null)
        {
            Log.Warning("PetTJUIForm 缺少 _txtDetailName 引用，请在 Inspector 中把 PetDetailed/TxtName 拖入。");
        }

        if (_txtDetailQuality == null)
        {
            Log.Warning("PetTJUIForm 缺少 _txtDetailQuality 引用，请在 Inspector 中把 PetDetailed/TxtQuality 拖入。");
        }

        if (_txtDetailProperty == null)
        {
            Log.Warning("PetTJUIForm 缺少 _txtDetailProperty 引用，请在 Inspector 中把 PetDetailed/TxtProperty 拖入。");
        }

        if (_txtDetailIntroduce == null)
        {
            Log.Warning("PetTJUIForm 缺少 _txtDetailIntroduce 引用，请在 Inspector 中把 PetDetailed/TxtIntroduce 拖入。");
        }

        if (_trDetailPetRoot == null)
        {
            Log.Warning("PetTJUIForm 缺少 _trDetailPetRoot 引用，请在 Inspector 中把 PetDetailed/Pet 拖入。");
        }
    }

    /// <summary>
    /// 缓存页签视觉状态。
    /// 这里会从当前已绑定的按钮内部提取选中态/未选中态背景图，以及两套文字颜色。
    /// </summary>
    private void CacheTabVisualState()
    {
        for (int i = 0; i < _qualityTabs.Length; i++)
        {
            Sprite sprite = _qualityTabs[i] != null && _qualityTabs[i].Background != null
                ? _qualityTabs[i].Background.sprite
                : null;
            if (sprite == null)
            {
                continue;
            }

            if (_selectedTabSprite == null && sprite.name.IndexOf("004", StringComparison.Ordinal) >= 0)
            {
                _selectedTabSprite = sprite;
            }

            if (_unselectedTabSprite == null && sprite.name.IndexOf("003", StringComparison.Ordinal) >= 0)
            {
                _unselectedTabSprite = sprite;
            }
        }

        if (_selectedTabSprite == null && _qualityTabs[0] != null && _qualityTabs[0].Background != null)
        {
            _selectedTabSprite = _qualityTabs[0].Background.sprite;
        }

        if (_unselectedTabSprite == null)
        {
            for (int i = 0; i < _qualityTabs.Length; i++)
            {
                if (_qualityTabs[i] != null && _qualityTabs[i].Background != null)
                {
                    _unselectedTabSprite = _qualityTabs[i].Background.sprite;
                    if (!ReferenceEquals(_unselectedTabSprite, _selectedTabSprite))
                    {
                        break;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 绑定所有按钮事件。
    /// 该方法只会执行一次，避免界面重复打开时重复注册监听。
    /// </summary>
    private void BindEventsOnce()
    {
        if (_eventsBound)
        {
            return;
        }

        if (_btnBackgroundClose != null)
        {
            _btnBackgroundClose.onClick.AddListener(OnBtnClose);
        }

        if (_detailView.CloseButton != null)
        {
            _detailView.CloseButton.onClick.AddListener(HideDetail);
        }

        for (int i = 0; i < _qualityTabs.Length; i++)
        {
            QualityTabView tab = _qualityTabs[i];
            if (tab == null || tab.Button == null)
            {
                continue;
            }

            int capturedIndex = i;
            tab.Button.onClick.AddListener(() => OnQualityTabClicked(capturedIndex));
        }

        _eventsBound = true;
    }

    /// <summary>
    /// 首次打开界面时构建宠物列表。
    /// 列表条目全部由 GoPet 模板克隆而来，构建一次后反复复用。
    /// </summary>
    private void BuildList()
    {
        if (_isListBuilt || _content == null || _goPetTemplate == null || GameEntry.DataTables == null)
        {
            return;
        }

        PetDataRow[] allRows = GameEntry.DataTables.GetAllDataRows<PetDataRow>();
        if (allRows == null || allRows.Length == 0)
        {
            return;
        }

        Array.Sort(allRows, ComparePetById);
        _allRows = allRows;
        _goPetTemplate.gameObject.SetActive(false);

        for (int i = 0; i < allRows.Length; i++)
        {
            Transform itemTransform = Instantiate(_goPetTemplate, _content);
            itemTransform.gameObject.SetActive(false);

            PetItemEntry entry = new PetItemEntry
            {
                Root = itemTransform.gameObject,
                Button = itemTransform.GetComponent<Button>(),
                PetRoot = itemTransform.Find("Pet")
            };

            Transform txtName = itemTransform.Find("ImgName/Text (TMP)");
            if (txtName != null)
            {
                entry.TxtName = txtName.GetComponent<TextMeshProUGUI>();
            }

            if (entry.Button != null)
            {
                int capturedIndex = i;
                entry.Button.onClick.AddListener(() => OnPetItemClicked(capturedIndex));
            }

            _entries.Add(entry);
        }

        _isListBuilt = true;
    }

    /// <summary>
    /// 根据当前选中的品质刷新所有页签视觉状态。
    /// 会统一切换背景图、文字颜色以及按钮可交互状态。
    /// </summary>
    private void RefreshTabs()
    {
        for (int i = 0; i < _qualityTabs.Length; i++)
        {
            QualityTabView tab = _qualityTabs[i];
            if (tab == null)
            {
                continue;
            }

            bool isSelected = tab.Quality == _currentQuality;
            if (tab.Background != null)
            {
                if (isSelected && _selectedTabSprite != null)
                {
                    tab.Background.sprite = _selectedTabSprite;
                }
                else if (!isSelected && _unselectedTabSprite != null)
                {
                    tab.Background.sprite = _unselectedTabSprite;
                }
            }


            if (tab.Button != null)
            {
                tab.Button.interactable = !isSelected;
            }
        }
    }

    /// <summary>
    /// 根据当前品质筛选结果刷新列表。
    /// 先重建可见数据行缓存，再把前 N 个条目绑定到这些数据行，其余条目统一隐藏。
    /// </summary>
    private void RefreshList()
    {
        if (!_isListBuilt)
        {
            return;
        }

        _visibleRows.Clear();
        for (int i = 0; i < _allRows.Length; i++)
        {
            PetDataRow row = _allRows[i];
            if (row == null || row.Quality != _currentQuality)
            {
                continue;
            }

            _visibleRows.Add(row);
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            PetItemEntry entry = _entries[i];
            bool isActive = i < _visibleRows.Count;
            if (entry == null || entry.Root == null)
            {
                continue;
            }

            if (!isActive)
            {
                entry.DataRow = null;
                entry.Root.SetActive(false);
                continue;
            }

            BindEntry(entry, _visibleRows[i]);
            if (!entry.Root.activeSelf)
            {
                entry.Root.SetActive(true);
            }
        }

        if (_content != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        }
    }

    /// <summary>
    /// 宠物数据行按 Id 升序排序。
    /// 这样列表展示顺序就和数据表定义顺序保持一致。
    /// </summary>
    /// <param name="a">待比较的宠物数据行 A。</param>
    /// <param name="b">待比较的宠物数据行 B。</param>
    /// <returns>升序比较结果。</returns>
    private static int ComparePetById(PetDataRow a, PetDataRow b)
    {
        if (ReferenceEquals(a, b))
        {
            return 0;
        }

        if (a == null)
        {
            return 1;
        }

        if (b == null)
        {
            return -1;
        }

        return a.Id.CompareTo(b.Id);
    }

    /// <summary>
    /// 把数据行内容绑定到单个列表条目上。
    /// 会同步刷新名称、解锁状态以及对应的 Spine 图像。
    /// </summary>
    /// <param name="entry">目标条目缓存。</param>
    /// <param name="row">要展示的宠物数据行。</param>
    private void BindEntry(PetItemEntry entry, PetDataRow row)
    {
        if (entry == null || row == null)
        {
            return;
        }

        entry.DataRow = row;
        if (entry.TxtName != null)
        {
            entry.TxtName.text = row.Name;
        }

        bool isUnlocked = GameEntry.Fruits != null && GameEntry.Fruits.IsPetUnlocked(row.Code);
        ApplyPetGraphic(entry.PetRoot, ref entry.PetGraphic, row, isUnlocked);
    }

    /// <summary>
    /// 品质按钮点击回调。
    /// 切换当前品质后，统一刷新页签、列表和详情面板。
    /// </summary>
    /// <param name="index">被点击品质按钮的数组索引。</param>
    private void OnQualityTabClicked(int index)
    {
        if (index < 0 || index >= _qualityTabs.Length || _qualityTabs[index] == null)
        {
            return;
        }

        QualityType quality = _qualityTabs[index].Quality;
        if (_currentQuality == quality)
        {
            return;
        }

        _currentQuality = quality;
        RefreshTabs();
        RefreshList();
        HideDetail();
    }

    /// <summary>
    /// 宠物列表项点击回调。
    /// 点击已展开详情的同一只宠物时，会执行“再次点击即关闭详情”的交互。
    /// </summary>
    /// <param name="index">被点击条目的缓存索引。</param>
    private void OnPetItemClicked(int index)
    {
        if (index < 0 || index >= _entries.Count)
        {
            return;
        }

        PetItemEntry entry = _entries[index];
        if (entry == null || entry.DataRow == null)
        {
            return;
        }

        if (_detailView.Root != null
            && _detailView.Root.activeSelf
            && ReferenceEquals(_detailView.CurrentDataRow, entry.DataRow))
        {
            HideDetail();
            return;
        }

        ShowDetail(entry.DataRow);
    }

    /// <summary>
    /// 打开并刷新详情面板。
    /// 这里会更新所有文本内容，并同步显示该宠物的 Spine 图像。
    /// </summary>
    /// <param name="row">要展示详情的宠物数据行。</param>
    private void ShowDetail(PetDataRow row)
    {
        if (row == null || _detailView.Root == null)
        {
            return;
        }

        _detailView.CurrentDataRow = row;
        if (!_detailView.Root.activeSelf)
        {
            _detailView.Root.SetActive(true);
        }

        if (_detailView.TxtName != null)
        {
            _detailView.TxtName.text = row.Name;
        }

        if (_detailView.TxtQuality != null)
        {
            _detailView.TxtQuality.text = GetQualityLabel(row.Quality);
        }

        if (_detailView.TxtProperty != null)
        {
            _detailView.TxtProperty.text = GetAttributeText(row);
        }

        if (_detailView.TxtIntroduce != null)
        {
            _detailView.TxtIntroduce.text = row.Description;
        }

        bool isUnlocked = GameEntry.Fruits != null && GameEntry.Fruits.IsPetUnlocked(row.Code);
        ApplyPetGraphic(_detailView.PetRoot, ref _detailView.PetGraphic, row, isUnlocked);
    }

    /// <summary>
    /// 关闭详情面板。
    /// 这里只做显隐和当前数据行清空，不销毁详情区域复用的 Spine 图像对象。
    /// </summary>
    private void HideDetail()
    {
        _detailView.CurrentDataRow = null;
        if (_detailView.Root != null && _detailView.Root.activeSelf)
        {
            _detailView.Root.SetActive(false);
        }
    }

    /// <summary>
    /// 关闭按钮点击回调。
    /// 通知 UGF 关闭当前窗体实例。
    /// </summary>
    private void OnBtnClose()
    {
        if (GameEntry.UI == null)
        {
            return;
        }

        GameEntry.UI.CloseUIForm(UIForm.SerialId);
    }

    /// <summary>
    /// 把品质枚举转成界面显示文案。
    /// </summary>
    /// <param name="quality">宠物品质枚举。</param>
    /// <returns>对应的中文品质名称。</returns>
    private static string GetQualityLabel(QualityType quality)
    {
        switch (quality)
        {
            case QualityType.Normal:
                return "普通";

            case QualityType.Rare:
                return "稀有";

            case QualityType.Epic:
                return "史诗";

            case QualityType.Legendary:
                return "传说";

            case QualityType.Mythic:
                return "神话";

            default:
                return string.Empty;
        }
    }

    /// <summary>
    /// 根据宠物属性类型拼出属性展示文本。
    /// 这里只在打开详情时调用，不在高频帧循环里运行。
    /// </summary>
    /// <param name="row">当前详情绑定的宠物数据行。</param>
    /// <returns>宠物属性展示文本。</returns>
    private static string GetAttributeText(PetDataRow row)
    {
        if (row == null)
        {
            return string.Empty;
        }

        switch (row.AttributeType)
        {
            case PetAttributeType.ScoreBase:
                return string.Format("基础得分 +{0}", row.AttributeValue);

            case PetAttributeType.ComboTime:
                return string.Format("连击时长 +{0}", row.AttributeValue);

            default:
                return "无额外属性";
        }
    }

    /// <summary>
    /// 把指定宠物的 Spine 图像显示到目标挂点下。
    /// 这段逻辑会优先复用已有的 SkeletonGraphic，只有首次或资源变化时才重新初始化。
    /// </summary>
    /// <param name="host">Spine 图像目标挂点。</param>
    /// <param name="graphic">要复用的 SkeletonGraphic 缓存引用。</param>
    /// <param name="row">当前宠物数据行。</param>
    /// <param name="isUnlocked">当前宠物是否已解锁。</param>
    private static void ApplyPetGraphic(Transform host, ref SkeletonGraphic graphic, PetDataRow row, bool isUnlocked)
    {
        if (host == null)
        {
            return;
        }

        if (row == null || GameEntry.GameAssets == null)
        {
            SetGraphicActive(graphic, false);
            return;
        }

        if (!GameEntry.GameAssets.TryGetPetSkeletonDataAsset(row.UiSkeletonDataPath, out SkeletonDataAsset skeletonDataAsset) || skeletonDataAsset == null)
        {
            Log.Warning("PetTJUIForm can not find cached ui skeleton data by path '{0}'.", row.UiSkeletonDataPath);
            SetGraphicActive(graphic, false);
            return;
        }

        if (skeletonDataAsset.atlasAssets == null
            || skeletonDataAsset.atlasAssets.Length == 0
            || skeletonDataAsset.atlasAssets[0] == null
            || skeletonDataAsset.atlasAssets[0].PrimaryMaterial == null)
        {
            Log.Warning("PetTJUIForm can not create skeleton graphic because atlas material is invalid, path '{0}'.", row.UiSkeletonDataPath);
            SetGraphicActive(graphic, false);
            return;
        }

        // 先从缓存好的 SkeletonDataAsset 里取材质。
        // 这里绝不能自己 new Material，否则会把图鉴界面变成长期材质泄漏点。
        Material material = skeletonDataAsset.atlasAssets[0].PrimaryMaterial;
        bool createdGraphic = false;
        if (graphic == null)
        {
            graphic = SkeletonGraphic.NewSkeletonGraphicGameObject(skeletonDataAsset, host, material);
            graphic.gameObject.name = "RuntimeSkeletonGraphic";
            graphic.gameObject.layer = host.gameObject.layer;
            graphic.raycastTarget = false;
            graphic.initialSkinName = "default";
            ConfigureGraphicRect(graphic.rectTransform);
            createdGraphic = true;
        }
        else if (graphic.transform.parent != host)
        {
            // 条目或详情缓存被重绑到别的挂点时，强制把旧图像对象挪到新的宿主下面继续复用。
            graphic.transform.SetParent(host, false);
            graphic.gameObject.layer = host.gameObject.layer;
            ConfigureGraphicRect(graphic.rectTransform);
        }

        bool needsInitialize = createdGraphic || graphic.skeletonDataAsset != skeletonDataAsset || !graphic.IsValid;
        graphic.material = material;
        if (needsInitialize)
        {
            graphic.skeletonDataAsset = skeletonDataAsset;
            graphic.initialSkinName = "default";
            graphic.Initialize(true);
            graphic.MatchRectTransformWithBounds();
            ConfigureGraphicRect(graphic.rectTransform);
        }

        if (!graphic.gameObject.activeSelf)
        {
            graphic.gameObject.SetActive(true);
        }

        PlayAnimation(graphic, row.IdleAnimationName);
        graphic.color = isUnlocked ? UnlockedPetColor : LockedPetColor;
    }

    /// <summary>
    /// 让 SkeletonGraphic 播放指定待机动画。
    /// 如果当前 0 轨已经在播同名动画，则直接跳过，避免重复 SetAnimation。
    /// </summary>
    /// <param name="graphic">目标 SkeletonGraphic。</param>
    /// <param name="animationName">要播放的动画名。</param>
    private static void PlayAnimation(SkeletonGraphic graphic, string animationName)
    {
        if (graphic == null || graphic.AnimationState == null || string.IsNullOrWhiteSpace(animationName))
        {
            return;
        }

        TrackEntry currentTrack = graphic.AnimationState.GetCurrent(0);
        if (currentTrack != null
            && currentTrack.Animation != null
            && string.Equals(currentTrack.Animation.Name, animationName, StringComparison.Ordinal))
        {
            return;
        }

        graphic.AnimationState.SetAnimation(0, animationName, true);
    }

    /// <summary>
    /// 配置运行时创建的 SkeletonGraphic 的 RectTransform。
    /// 宠物图鉴的所有角色都要求底边对齐，所以锚点和 pivot 都固定在底部中心。
    /// </summary>
    /// <param name="rectTransform">要配置的 RectTransform。</param>
    private static void ConfigureGraphicRect(RectTransform rectTransform)
    {
        if (rectTransform == null)
        {
            return;
        }

        rectTransform.anchorMin = new Vector2(0.5f, 0f);
        rectTransform.anchorMax = new Vector2(0.5f, 0f);
        rectTransform.pivot = new Vector2(0.5f, 0f);
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.localRotation = Quaternion.identity;
        rectTransform.localScale = Vector3.one;
    }

    /// <summary>
    /// 安全切换 SkeletonGraphic 的激活状态。
    /// 跳过空引用和重复 SetActive，避免无意义状态切换。
    /// </summary>
    /// <param name="graphic">目标 SkeletonGraphic。</param>
    /// <param name="isActive">目标显隐状态。</param>
    private static void SetGraphicActive(SkeletonGraphic graphic, bool isActive)
    {
        if (graphic == null || graphic.gameObject.activeSelf == isActive)
        {
            return;
        }

        graphic.gameObject.SetActive(isActive);
    }

    /// <summary>
    /// 在目标对象上取 Button，没有就补一个。
    /// 背景遮罩只负责点击关闭，因此这里统一关闭过渡动画，避免遮罩闪烁。
    /// </summary>
    /// <param name="gameObject">目标节点。</param>
    /// <returns>可用的 Button 组件。</returns>
    private static Button GetOrAddButton(GameObject gameObject)
    {
        if (gameObject == null)
        {
            return null;
        }

        Button button = gameObject.GetComponent<Button>();
        if (button != null)
        {
            return button;
        }

        button = gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = gameObject.GetComponent<Graphic>();
        return button;
    }
}
