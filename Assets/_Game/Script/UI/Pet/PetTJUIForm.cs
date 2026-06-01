using System;
using System.Collections.Generic;
using Spine;
using Spine.Unity;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 宠物图鉴 / 产出物图鉴双模式界面。
/// 进入界面默认显示全部宠物（按品质升序、再按 Id 升序）；顶部 GoSelect / GoNoSelect 两个槽位互换两枚 tab 按钮以切换模式。
/// 顶层节点全部走 Inspector 拖入；不再依赖 transform.Find 全路径查找，避免 prefab 结构调整后悄悄走兜底逻辑。
/// </summary>
public sealed class PetTJUIForm : UIFormLogic
{
    /// <summary>
    /// 未解锁宠物 Spine 染色：纯黑色剪影，告知玩家"图鉴未点亮"。
    /// </summary>
    private static readonly Color32 LockedPetColor = new Color32(0, 0, 0, 255);

    /// <summary>
    /// 已解锁宠物 Spine 染色：纯白色，让贴图按原色输出。
    /// </summary>
    private static readonly Color32 UnlockedPetColor = new Color32(255, 255, 255, 255);

    /// <summary>
    /// 未解锁产出物图标染色：纯黑色剪影，与宠物 Spine 同口径。
    /// 注意：作用在 UnityEngine.UI.Image.color 上（不是 Color32），UI 系统按线性 alpha 混合，黑色剪影最干净。
    /// </summary>
    private static readonly Color LockedProduceColor = new Color(0f, 0f, 0f, 1f);

    /// <summary>
    /// 已解锁产出物图标染色：纯白色，让 sprite 按原色输出。
    /// </summary>
    private static readonly Color UnlockedProduceColor = Color.white;

    /// <summary>
    /// 未选中态 Tab 按钮的染色（中灰）。
    /// 给玩家"这个按钮当前不在选中位、可以点击切换"的视觉提示。
    /// </summary>
    private static readonly Color UnselectedTabColor = new Color(0.5f, 0.5f, 0.5f, 1f);

    /// <summary>
    /// 选中态 Tab 按钮的染色（纯白），表示当前位于选中槽位。
    /// </summary>
    private static readonly Color SelectedTabColor = Color.white;

    /// <summary>
    /// 详情面板出现条件文本格式（宠物详情）。
    /// 参数：PetDataRow.RequiredStars。
    /// </summary>
    private const string PetDetailOccurrenceFormat = "拥有{0}星星";

    /// <summary>
    /// 产出物详情：金币价值文本格式。
    /// </summary>
    private const string ProduceDetailValueFormat = "价值：{0} 金币";

    /// <summary>
    /// 产出物详情：获得星星文本格式（0 时整段隐藏，不进入此格式化）。
    /// </summary>
    private const string ProduceDetailStarsFormat = "获得 {0} 星星";

    /// <summary>
    /// 图鉴的两种模式：宠物图鉴 / 产出物图鉴。
    /// </summary>
    private enum CatalogMode
    {
        /// <summary>宠物图鉴模式（默认）。</summary>
        Pet = 0,

        /// <summary>产出物图鉴模式。</summary>
        Produce = 1,
    }

    /// <summary>
    /// 单个宠物列表条目的运行时缓存。
    /// </summary>
    private sealed class PetItemEntry
    {
        /// <summary>条目根节点。</summary>
        public GameObject Root;

        /// <summary>条目按钮；未解锁宠物条目把 interactable 关掉来阻止打开详情。</summary>
        public Button Button;

        /// <summary>条目名称文本。</summary>
        public TextMeshProUGUI TxtName;

        /// <summary>宠物 Spine 图像挂点。</summary>
        public Transform PetRoot;

        /// <summary>条目复用的 SkeletonGraphic（首次创建后一直复用）。</summary>
        public SkeletonGraphic PetGraphic;

        /// <summary>当前条目绑定的数据行。</summary>
        public PetDataRow DataRow;
    }

    /// <summary>
    /// 单个产出物列表条目的运行时缓存。
    /// </summary>
    private sealed class ProduceItemEntry
    {
        /// <summary>条目根节点。</summary>
        public GameObject Root;

        /// <summary>条目按钮；未解锁产出物条目通过 interactable=false 阻止打开详情。</summary>
        public Button Button;

        /// <summary>条目名称文本。</summary>
        public TextMeshProUGUI TxtName;

        /// <summary>条目图标 Image（直接显示 IconPath sprite，未解锁时染黑色剪影）。</summary>
        public Image Icon;

        /// <summary>条目预制体里 Image 自带的默认 sprite，作为缓存未命中时的回退图。</summary>
        public Sprite DefaultIconSprite;

        /// <summary>当前条目绑定的数据行。</summary>
        public PetProduceDataRow DataRow;
    }

    /// <summary>
    /// 宠物详情面板的运行时缓存。
    /// 顶层组件由 Inspector 手拖；详情区 Spine 在运行时创建并复用。
    /// </summary>
    private sealed class PetDetailView
    {
        public GameObject Root;
        public Button CloseButton;
        public TextMeshProUGUI TxtName;
        public TextMeshProUGUI TxtQuality;
        public TextMeshProUGUI TxtProperty;
        public TextMeshProUGUI TxtIntroduce;
        public TextMeshProUGUI TxtOccurrenceConditions;
        public Transform PetRoot;
        public SkeletonGraphic PetGraphic;
        public PetDataRow CurrentDataRow;
    }

    /// <summary>
    /// 产出物详情面板的运行时缓存。
    /// </summary>
    private sealed class ProduceDetailView
    {
        public GameObject Root;
        public Button CloseButton;
        public TextMeshProUGUI TxtName;
        public TextMeshProUGUI TxtQuality;
        public TextMeshProUGUI TxtProperty;
        public TextMeshProUGUI TxtIntroduce;
        public TextMeshProUGUI TxtOccurrenceConditions;
        public Image Icon;
        public Sprite DefaultIconSprite;
        public bool HasCachedDefaultIcon;
        public PetProduceDataRow CurrentDataRow;
    }

    /// <summary>
    /// 单只宠物的两档产出物配置槽位。
    /// 把 PetProduce 表里同一 PetId 的初级 / 高级两行打平到详情面板的 Output1 / Output2。
    /// </summary>
    private struct PetProduceSlot
    {
        public PetProduceDataRow Primary;
        public PetProduceDataRow Advanced;
    }

    private readonly List<PetItemEntry> _petEntries = new List<PetItemEntry>(32);
    private readonly List<ProduceItemEntry> _produceEntries = new List<ProduceItemEntry>(64);
    private PetDataRow[] _allPetRows = Array.Empty<PetDataRow>();
    private PetProduceDataRow[] _allProduceRows = Array.Empty<PetProduceDataRow>();

    /// <summary>
    /// 宠物 Id → 该宠物两档产出物配置行的缓存（Pet 详情面板使用）。
    /// </summary>
    private readonly Dictionary<int, PetProduceSlot> _produceSlotsByPetId = new Dictionary<int, PetProduceSlot>(32);

    /// <summary>
    /// 宠物 Id → PetDataRow 缓存（产出物详情用于读父宠物 QualityType）。
    /// </summary>
    private readonly Dictionary<int, PetDataRow> _petRowsById = new Dictionary<int, PetDataRow>(32);

    [Header("通用引用")]
    [SerializeField] private RectTransform _content;
    [SerializeField] private GameObject _goBackgroundCloseTarget;

    [Header("宠物条目 / 详情")]
    [SerializeField] private Transform _goPetTemplate;
    [SerializeField] private GameObject _goDetailRoot;
    [SerializeField] private Button _btnDetailClose;
    [SerializeField] private TextMeshProUGUI _txtDetailName;
    [SerializeField] private TextMeshProUGUI _txtDetailQuality;
    [SerializeField] private TextMeshProUGUI _txtDetailProperty;
    [SerializeField] private TextMeshProUGUI _txtDetailIntroduce;
    [SerializeField] private TextMeshProUGUI _txtDetailOccurrenceConditions;
    [SerializeField] private Transform _trDetailPetRoot;
    [SerializeField] private Image _imgDetailOutput1;
    [SerializeField] private Image _imgDetailOutput2;

    [Header("产出物条目 / 详情")]
    [SerializeField] private Transform _goProduceTemplate;
    [SerializeField] private GameObject _goProduceDetailRoot;
    [SerializeField] private Button _btnProduceDetailClose;
    [SerializeField] private TextMeshProUGUI _txtProduceDetailName;
    [SerializeField] private TextMeshProUGUI _txtProduceDetailQuality;
    [SerializeField] private TextMeshProUGUI _txtProduceDetailProperty;
    [SerializeField] private TextMeshProUGUI _txtProduceDetailIntroduce;
    [SerializeField] private TextMeshProUGUI _txtProduceDetailOccurrenceConditions;
    [SerializeField] private Image _imgProduceDetailIcon;

    [Header("Tab 切换")]
    [SerializeField] private RectTransform _goSelectParent;
    [SerializeField] private RectTransform _goNoSelectParent;
    [SerializeField] private Button _btnPetTab;
    [SerializeField] private Button _btnProduceTab;

    private Button _btnBackgroundClose;
    private readonly PetDetailView _detailView = new PetDetailView();
    private readonly ProduceDetailView _produceDetailView = new ProduceDetailView();
    private CatalogMode _currentMode = CatalogMode.Pet;

    /// <summary>
    /// 已经由本界面发起过按需请求的 UI SkeletonData 路径集合（避免对同一资源重复发射加载请求）。
    /// </summary>
    private readonly HashSet<string> _requestedUiSkeletonDataPaths = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Output1 默认 sprite 缓存。</summary>
    private Sprite _detailOutput1DefaultSprite;

    /// <summary>Output2 默认 sprite 缓存。</summary>
    private Sprite _detailOutput2DefaultSprite;

    /// <summary>Output1 默认 sprite 是否已快照。</summary>
    private bool _hasCachedOutput1DefaultSprite;

    /// <summary>Output2 默认 sprite 是否已快照。</summary>
    private bool _hasCachedOutput2DefaultSprite;

    private bool _isListBuilt;
    private bool _eventsBound;
    private bool _hasLoggedMissingReferenceWarning;
    private bool _isListeningPetSkeletonDataStateChanged;

    /// <summary>
    /// 初始化时只做引用缓存与一次性事件绑定，不做业务数据刷新。
    /// </summary>
    /// <param name="userData">用户自定义数据。</param>
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
    /// 打开界面：首次构建列表，进入 Pet 模式，关闭两个详情面板。
    /// </summary>
    /// <param name="userData">用户自定义数据。</param>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        if (!EnsureSerializedReferencesReady())
        {
            return;
        }

        BindEventsOnce();
        BuildLists();
        SwitchMode(CatalogMode.Pet, forceRefresh: true);
        HidePetDetail();
        HideProduceDetail();
    }

    /// <summary>
    /// 关闭时释放宠物 SkeletonData 加载状态监听；不销毁列表条目复用对象。
    /// </summary>
    /// <param name="isShutdown">是否为关闭流程。</param>
    /// <param name="userData">用户自定义数据。</param>
    protected override void OnClose(bool isShutdown, object userData)
    {
        ReleasePetSkeletonDataStateSubscription();
        _requestedUiSkeletonDataPaths.Clear();
        base.OnClose(isShutdown, userData);
    }

    /// <summary>对象销毁兜底：异常销毁时也不残留资源事件委托。</summary>
    private void OnDestroy()
    {
        ReleasePetSkeletonDataStateSubscription();
    }

    #region 引用就绪校验

    /// <summary>
    /// 确保所有 Inspector 手拖字段就绪并写入运行时缓存；缺引用时一次性打印警告并停流程。
    /// </summary>
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
    /// 把序列化字段映射进运行时 PetDetailView / ProduceDetailView 缓存里。
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
        _detailView.TxtOccurrenceConditions = _txtDetailOccurrenceConditions;
        _detailView.PetRoot = _trDetailPetRoot;

        _produceDetailView.Root = _goProduceDetailRoot;
        _produceDetailView.CloseButton = _btnProduceDetailClose;
        _produceDetailView.TxtName = _txtProduceDetailName;
        _produceDetailView.TxtQuality = _txtProduceDetailQuality;
        _produceDetailView.TxtProperty = _txtProduceDetailProperty;
        _produceDetailView.TxtIntroduce = _txtProduceDetailIntroduce;
        _produceDetailView.TxtOccurrenceConditions = _txtProduceDetailOccurrenceConditions;
        _produceDetailView.Icon = _imgProduceDetailIcon;
    }

    /// <summary>判断所有打开界面必需的手拖字段是否齐全。</summary>
    private bool HasAllRequiredSerializedReferences()
    {
        return _content != null
            && _goPetTemplate != null
            && _goProduceTemplate != null
            && _goBackgroundCloseTarget != null
            && _btnBackgroundClose != null
            && _goDetailRoot != null
            && _btnDetailClose != null
            && _txtDetailName != null
            && _txtDetailQuality != null
            && _txtDetailProperty != null
            && _txtDetailIntroduce != null
            && _txtDetailOccurrenceConditions != null
            && _trDetailPetRoot != null
            && _goProduceDetailRoot != null
            && _btnProduceDetailClose != null
            && _txtProduceDetailName != null
            && _txtProduceDetailQuality != null
            && _txtProduceDetailProperty != null
            && _txtProduceDetailIntroduce != null
            && _txtProduceDetailOccurrenceConditions != null
            && _imgProduceDetailIcon != null
            && _goSelectParent != null
            && _goNoSelectParent != null
            && _btnPetTab != null
            && _btnProduceTab != null;
    }

    /// <summary>缺哪个就单独提示哪个，避免一锅端式的"少了字段"。</summary>
    private void LogMissingSerializedReferenceWarnings()
    {
        if (_content == null) Log.Warning("PetTJUIForm 缺少 _content 引用，请在 Inspector 中把 Scroll View/Viewport/Content 拖入。");
        if (_goPetTemplate == null) Log.Warning("PetTJUIForm 缺少 _goPetTemplate 引用，请在 Inspector 中把 Content/GoPet 拖入。");
        if (_goProduceTemplate == null) Log.Warning("PetTJUIForm 缺少 _goProduceTemplate 引用，请在 Inspector 中把 Content/GoPetccw 拖入。");
        if (_goBackgroundCloseTarget == null) Log.Warning("PetTJUIForm 缺少 _goBackgroundCloseTarget 引用，请在 Inspector 中把 BtnClose 节点拖入。");
        if (_goDetailRoot == null) Log.Warning("PetTJUIForm 缺少 _goDetailRoot 引用（GoPetDetailed）。");
        if (_btnDetailClose == null) Log.Warning("PetTJUIForm 缺少 _btnDetailClose 引用（GoPetDetailed 上的 Button）。");
        if (_txtDetailName == null) Log.Warning("PetTJUIForm 缺少 _txtDetailName 引用（GoPetDetailed/.../TxtName）。");
        if (_txtDetailQuality == null) Log.Warning("PetTJUIForm 缺少 _txtDetailQuality 引用（GoPetDetailed/.../TxtQuality）。");
        if (_txtDetailProperty == null) Log.Warning("PetTJUIForm 缺少 _txtDetailProperty 引用（GoPetDetailed/.../TxtProperty）。");
        if (_txtDetailIntroduce == null) Log.Warning("PetTJUIForm 缺少 _txtDetailIntroduce 引用（GoPetDetailed/.../TxtIntroduce）。");
        if (_txtDetailOccurrenceConditions == null) Log.Warning("PetTJUIForm 缺少 _txtDetailOccurrenceConditions 引用（GoPetDetailed/.../TxtOccurrenceConditions）。");
        if (_trDetailPetRoot == null) Log.Warning("PetTJUIForm 缺少 _trDetailPetRoot 引用（GoPetDetailed/.../Pet）。");
        if (_goProduceDetailRoot == null) Log.Warning("PetTJUIForm 缺少 _goProduceDetailRoot 引用（GoPetDetailed (1)）。");
        if (_btnProduceDetailClose == null) Log.Warning("PetTJUIForm 缺少 _btnProduceDetailClose 引用（GoPetDetailed (1) 上的 Button）。");
        if (_txtProduceDetailName == null) Log.Warning("PetTJUIForm 缺少 _txtProduceDetailName 引用（GoPetDetailed (1)/.../TxtName）。");
        if (_txtProduceDetailQuality == null) Log.Warning("PetTJUIForm 缺少 _txtProduceDetailQuality 引用（GoPetDetailed (1)/.../TxtQuality）。");
        if (_txtProduceDetailProperty == null) Log.Warning("PetTJUIForm 缺少 _txtProduceDetailProperty 引用（GoPetDetailed (1)/.../TxtProperty）。");
        if (_txtProduceDetailIntroduce == null) Log.Warning("PetTJUIForm 缺少 _txtProduceDetailIntroduce 引用（GoPetDetailed (1)/.../TxtIntroduce）。");
        if (_txtProduceDetailOccurrenceConditions == null) Log.Warning("PetTJUIForm 缺少 _txtProduceDetailOccurrenceConditions 引用（GoPetDetailed (1)/.../TxtOccurrenceConditions）。");
        if (_imgProduceDetailIcon == null) Log.Warning("PetTJUIForm 缺少 _imgProduceDetailIcon 引用（GoPetDetailed (1)/.../Image）。");
        if (_goSelectParent == null) Log.Warning("PetTJUIForm 缺少 _goSelectParent 引用（GoSelect）。");
        if (_goNoSelectParent == null) Log.Warning("PetTJUIForm 缺少 _goNoSelectParent 引用（GoNoSelect）。");
        if (_btnPetTab == null) Log.Warning("PetTJUIForm 缺少 _btnPetTab 引用（宠物图鉴 Tab 按钮，初始放在 GoSelect 下）。");
        if (_btnProduceTab == null) Log.Warning("PetTJUIForm 缺少 _btnProduceTab 引用（产出物图鉴 Tab 按钮，初始放在 GoNoSelect 下）。");
    }

    #endregion

    #region 事件绑定

    /// <summary>
    /// 一次性绑定所有按钮事件，避免界面重复打开重复 AddListener。
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
            _detailView.CloseButton.onClick.AddListener(OnPetDetailCloseClicked);
        }

        if (_produceDetailView.CloseButton != null)
        {
            _produceDetailView.CloseButton.onClick.AddListener(OnProduceDetailCloseClicked);
        }

        if (_btnPetTab != null)
        {
            _btnPetTab.onClick.AddListener(OnPetTabClicked);
        }

        if (_btnProduceTab != null)
        {
            _btnProduceTab.onClick.AddListener(OnProduceTabClicked);
        }

        _eventsBound = true;
    }

    #endregion

    #region 列表构建

    /// <summary>
    /// 首次打开时一次性构建宠物列表 + 产出物列表 + 反查缓存。
    /// </summary>
    private void BuildLists()
    {
        if (_isListBuilt || _content == null || _goPetTemplate == null || _goProduceTemplate == null || GameEntry.DataTables == null)
        {
            return;
        }

        BuildPetList();
        BuildProduceSlotCacheAndPetIndex();
        BuildProduceList();
        _isListBuilt = true;
    }

    /// <summary>
    /// 取整张 Pet 表，按 (Quality 升序, Id 升序) 排序后克隆条目。
    /// </summary>
    private void BuildPetList()
    {
        PetDataRow[] allRows = GameEntry.DataTables.GetAllDataRows<PetDataRow>();
        if (allRows == null || allRows.Length == 0)
        {
            return;
        }

        Array.Sort(allRows, ComparePetByQualityThenId);
        _allPetRows = allRows;

        _goPetTemplate.gameObject.SetActive(false);

        for (int i = 0; i < allRows.Length; i++)
        {
            Transform itemTransform = Instantiate(_goPetTemplate, _content);
            itemTransform.gameObject.SetActive(false);
            itemTransform.SetSiblingIndex(_goPetTemplate.GetSiblingIndex() + 1 + i);

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

            _petEntries.Add(entry);
        }
    }

    /// <summary>
    /// 一次性构建：宠物 Id → 两档产出物槽位、宠物 Id → PetDataRow 反查。
    /// </summary>
    private void BuildProduceSlotCacheAndPetIndex()
    {
        _produceSlotsByPetId.Clear();
        _petRowsById.Clear();

        for (int i = 0; i < _allPetRows.Length; i++)
        {
            PetDataRow row = _allPetRows[i];
            if (row != null)
            {
                _petRowsById[row.Id] = row;
            }
        }

        PetProduceDataRow[] produceRows = GameEntry.DataTables.GetAllDataRows<PetProduceDataRow>();
        if (produceRows == null || produceRows.Length == 0)
        {
            _allProduceRows = Array.Empty<PetProduceDataRow>();
            return;
        }

        // 产出物按 (父宠物 Quality 升序, PetId 升序, Grade 升序) 排序，与列表展示顺序一致。
        Array.Sort(produceRows, CompareProduceByPetThenGrade);
        _allProduceRows = produceRows;

        for (int i = 0; i < produceRows.Length; i++)
        {
            PetProduceDataRow produceRow = produceRows[i];
            if (produceRow == null)
            {
                continue;
            }

            _produceSlotsByPetId.TryGetValue(produceRow.PetId, out PetProduceSlot slot);
            switch (produceRow.Grade)
            {
                case ProduceGradeType.Primary:
                    if (slot.Primary == null)
                    {
                        slot.Primary = produceRow;
                    }
                    break;

                case ProduceGradeType.Advanced:
                    if (slot.Advanced == null)
                    {
                        slot.Advanced = produceRow;
                    }
                    break;
            }

            _produceSlotsByPetId[produceRow.PetId] = slot;
        }
    }

    /// <summary>
    /// 用 GoPetccw 模板克隆产出物条目。
    /// </summary>
    private void BuildProduceList()
    {
        if (_allProduceRows == null || _allProduceRows.Length == 0)
        {
            return;
        }

        _goProduceTemplate.gameObject.SetActive(false);

        for (int i = 0; i < _allProduceRows.Length; i++)
        {
            Transform itemTransform = Instantiate(_goProduceTemplate, _content);
            itemTransform.gameObject.SetActive(false);
            itemTransform.SetSiblingIndex(_goProduceTemplate.GetSiblingIndex() + 1 + i);

            ProduceItemEntry entry = new ProduceItemEntry
            {
                Root = itemTransform.gameObject,
                Button = itemTransform.GetComponent<Button>()
            };

            Transform txtName = itemTransform.Find("ImgName/Text (TMP)");
            if (txtName != null)
            {
                entry.TxtName = txtName.GetComponent<TextMeshProUGUI>();
            }

            // GoPetccw 内的 Image（与 ImgName 同级）作为图标显示位。
            Transform iconTransform = itemTransform.Find("Image");
            if (iconTransform != null)
            {
                entry.Icon = iconTransform.GetComponent<Image>();
                if (entry.Icon != null)
                {
                    entry.DefaultIconSprite = entry.Icon.sprite;
                }
            }

            if (entry.Button != null)
            {
                int capturedIndex = i;
                entry.Button.onClick.AddListener(() => OnProduceItemClicked(capturedIndex));
            }

            _produceEntries.Add(entry);
        }
    }

    #endregion

    #region 模式切换

    /// <summary>
    /// 切换 Pet / Produce 模式：互换两个 Tab 按钮的父级槽位、刷新条目显隐、隐藏两个详情面板。
    /// </summary>
    /// <param name="mode">目标模式。</param>
    /// <param name="forceRefresh">是否强制刷新（OnOpen 时即便 mode 与缓存一致也要刷一次）。</param>
    private void SwitchMode(CatalogMode mode, bool forceRefresh)
    {
        if (!forceRefresh && _currentMode == mode)
        {
            return;
        }

        _currentMode = mode;

        // 1. Tab 按钮父级互换。
        if (mode == CatalogMode.Pet)
        {
            ReparentTabButton(_btnPetTab, _goSelectParent, isSelected: true);
            ReparentTabButton(_btnProduceTab, _goNoSelectParent, isSelected: false);
        }
        else
        {
            ReparentTabButton(_btnProduceTab, _goSelectParent, isSelected: true);
            ReparentTabButton(_btnPetTab, _goNoSelectParent, isSelected: false);
        }

        // 2. 列表条目显隐切换。
        bool isPetMode = mode == CatalogMode.Pet;
        for (int i = 0; i < _petEntries.Count; i++)
        {
            PetItemEntry entry = _petEntries[i];
            if (entry == null || entry.Root == null)
            {
                continue;
            }

            if (isPetMode)
            {
                if (i < _allPetRows.Length)
                {
                    BindPetEntry(entry, _allPetRows[i]);
                    if (!entry.Root.activeSelf)
                    {
                        entry.Root.SetActive(true);
                    }
                }
                else
                {
                    entry.DataRow = null;
                    if (entry.Root.activeSelf)
                    {
                        entry.Root.SetActive(false);
                    }
                }
            }
            else if (entry.Root.activeSelf)
            {
                entry.Root.SetActive(false);
            }
        }

        for (int i = 0; i < _produceEntries.Count; i++)
        {
            ProduceItemEntry entry = _produceEntries[i];
            if (entry == null || entry.Root == null)
            {
                continue;
            }

            if (!isPetMode)
            {
                if (i < _allProduceRows.Length)
                {
                    BindProduceEntry(entry, _allProduceRows[i]);
                    if (!entry.Root.activeSelf)
                    {
                        entry.Root.SetActive(true);
                    }
                }
                else
                {
                    entry.DataRow = null;
                    if (entry.Root.activeSelf)
                    {
                        entry.Root.SetActive(false);
                    }
                }
            }
            else if (entry.Root.activeSelf)
            {
                entry.Root.SetActive(false);
            }
        }

        HidePetDetail();
        HideProduceDetail();

        if (_content != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        }
    }

    /// <summary>
    /// 把 tab 按钮挪到指定父级槽位，并按选中态切换 interactable / 染色。
    /// </summary>
    /// <param name="button">要挪动的按钮。</param>
    /// <param name="parent">目标父级 RectTransform。</param>
    /// <param name="isSelected">true=选中槽位（白、不可点）；false=未选中槽位（灰、可点）。</param>
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
            // SetParent(parent, false) 即 worldPositionStays=false，
            // 自动保留按钮原本的 anchoredPosition / localRotation / localScale，
            // 让两个按钮分别保留 prefab 上配置的位置和缩放，互换槽位时不会"位置漂移"。
            buttonRect.SetParent(parent, false);
        }

        // 仅调整兄弟顺序：GoSelect / GoNoSelect 当前各只挂一枚按钮，这一行其实不会改变视觉，
        // 仅作为后续若策划再往这两个槽位塞内容时的稳定锚点；不动 RectTransform 数值。
        buttonRect.SetAsLastSibling();

        button.interactable = !isSelected;

        // 仅染色 targetGraphic（按钮本身的 Image），不影响子节点上的图标。
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

    #region 列表绑定

    /// <summary>
    /// 把数据行内容绑定到单个宠物条目。
    /// 未解锁宠物 Button.interactable=false，无法打开详情。
    /// </summary>
    private void BindPetEntry(PetItemEntry entry, PetDataRow row)
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
        if (entry.Button != null && entry.Button.interactable != isUnlocked)
        {
            entry.Button.interactable = isUnlocked;
        }

        ApplyPetGraphic(entry.PetRoot, ref entry.PetGraphic, row, isUnlocked);
    }

    /// <summary>
    /// 把数据行内容绑定到单个产出物条目。
    /// 未解锁产出物 Button.interactable=false 且 Image 染黑剪影。
    /// </summary>
    private void BindProduceEntry(ProduceItemEntry entry, PetProduceDataRow row)
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

        bool isUnlocked = GameEntry.Fruits != null && GameEntry.Fruits.IsProduceUnlocked(row.Code);
        if (entry.Button != null && entry.Button.interactable != isUnlocked)
        {
            entry.Button.interactable = isUnlocked;
        }

        if (entry.Icon != null)
        {
            // 始终用 IconPath sprite；命中失败回退预制体默认图。
            Sprite iconSprite = TryGetProduceSprite(row);
            SetSpriteIfChanged(entry.Icon, iconSprite != null ? iconSprite : entry.DefaultIconSprite);

            Color target = isUnlocked ? UnlockedProduceColor : LockedProduceColor;
            if (entry.Icon.color != target)
            {
                entry.Icon.color = target;
            }
        }
    }

    #endregion

    #region 宠物详情

    /// <summary>
    /// 宠物条目点击：再次点同一只宠物时关闭详情；锁定项 Button.interactable=false 不会到这里。
    /// </summary>
    private void OnPetItemClicked(int index)
    {
        UIInteractionSound.PlayClick();

        if (index < 0 || index >= _petEntries.Count)
        {
            return;
        }

        PetItemEntry entry = _petEntries[index];
        if (entry == null || entry.DataRow == null)
        {
            return;
        }

        if (_detailView.Root != null
            && _detailView.Root.activeSelf
            && ReferenceEquals(_detailView.CurrentDataRow, entry.DataRow))
        {
            HidePetDetail();
            return;
        }

        ShowPetDetail(entry.DataRow);
    }

    /// <summary>
    /// 打开并刷新宠物详情面板（含 Output1 / Output2 图标）。
    /// </summary>
    private void ShowPetDetail(PetDataRow row)
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

        if (_detailView.TxtName != null) _detailView.TxtName.text = row.Name;
        if (_detailView.TxtQuality != null) _detailView.TxtQuality.text = GetQualityLabel(row.Quality);
        if (_detailView.TxtProperty != null) _detailView.TxtProperty.text = GetAttributeText(row);
        if (_detailView.TxtIntroduce != null) _detailView.TxtIntroduce.text = row.Description;

        if (_detailView.TxtOccurrenceConditions != null)
        {
            bool show = row.RequiredStars > 0;
            if (_detailView.TxtOccurrenceConditions.gameObject.activeSelf != show)
            {
                _detailView.TxtOccurrenceConditions.gameObject.SetActive(show);
            }

            if (show)
            {
                _detailView.TxtOccurrenceConditions.SetText(PetDetailOccurrenceFormat, row.RequiredStars);
            }
        }

        bool isUnlocked = GameEntry.Fruits != null && GameEntry.Fruits.IsPetUnlocked(row.Code);
        ApplyPetGraphic(_detailView.PetRoot, ref _detailView.PetGraphic, row, isUnlocked);
        RefreshPetDetailOutputs(row);
    }

    /// <summary>
    /// 刷新宠物详情面板的两个 Output 图标。
    /// 设计语义：Output1 / Output2 始终显示 IconPath 配置图（命中失败回退预制体默认图）；
    /// 解锁=白色，未解锁=黑色剪影；与玩家"我看到的就是该格位长什么样"的预期对齐。
    /// </summary>
    private void RefreshPetDetailOutputs(PetDataRow row)
    {
        PetProduceSlot slot = default;
        if (row != null)
        {
            _produceSlotsByPetId.TryGetValue(row.Id, out slot);
        }

        ApplyOutputIcon(_imgDetailOutput1, slot.Primary, ref _detailOutput1DefaultSprite, ref _hasCachedOutput1DefaultSprite);
        ApplyOutputIcon(_imgDetailOutput2, slot.Advanced, ref _detailOutput2DefaultSprite, ref _hasCachedOutput2DefaultSprite);
    }

    /// <summary>
    /// 单个 Output 图标刷新：始终显示 IconPath sprite，未解锁染黑色剪影。
    /// </summary>
    /// <param name="image">目标 Image；空引用直接跳过（兼容 prefab 没拖该字段）。</param>
    /// <param name="produceRow">该槽位对应的产出物配置行；空表示无配置。</param>
    /// <param name="defaultSprite">该槽位预制体默认 sprite（按引用）。</param>
    /// <param name="hasCachedDefaultSprite">默认 sprite 是否已快照（按引用，首次会被置 true）。</param>
    private static void ApplyOutputIcon(Image image, PetProduceDataRow produceRow, ref Sprite defaultSprite, ref bool hasCachedDefaultSprite)
    {
        if (image == null)
        {
            return;
        }

        if (!hasCachedDefaultSprite)
        {
            defaultSprite = image.sprite;
            hasCachedDefaultSprite = true;
        }

        if (produceRow == null || string.IsNullOrWhiteSpace(produceRow.Code))
        {
            // 该宠物没有这一档产出物 → 直接显默认图，颜色还原白色（视为"无效图标"，不染黑）。
            SetSpriteIfChanged(image, defaultSprite);
            if (image.color != UnlockedProduceColor)
            {
                image.color = UnlockedProduceColor;
            }
            return;
        }

        // 始终显配置 sprite；缓存命中失败回退默认图。
        Sprite iconSprite = TryGetProduceSprite(produceRow);
        SetSpriteIfChanged(image, iconSprite != null ? iconSprite : defaultSprite);

        bool isUnlocked = GameEntry.Fruits != null && GameEntry.Fruits.IsProduceUnlocked(produceRow.Code);
        Color target = isUnlocked ? UnlockedProduceColor : LockedProduceColor;
        if (image.color != target)
        {
            image.color = target;
        }
    }

    /// <summary>
    /// 宠物详情关闭按钮回调。
    /// </summary>
    private void OnPetDetailCloseClicked()
    {
        UIInteractionSound.PlayClick();
        HidePetDetail();
    }

    /// <summary>关闭宠物详情面板。</summary>
    private void HidePetDetail()
    {
        _detailView.CurrentDataRow = null;
        if (_detailView.Root != null && _detailView.Root.activeSelf)
        {
            _detailView.Root.SetActive(false);
        }
    }

    #endregion

    #region 产出物详情

    /// <summary>
    /// 产出物条目点击：解锁项打开详情；未解锁不会进入（interactable=false 已拦）。
    /// </summary>
    private void OnProduceItemClicked(int index)
    {
        UIInteractionSound.PlayClick();

        if (index < 0 || index >= _produceEntries.Count)
        {
            return;
        }

        ProduceItemEntry entry = _produceEntries[index];
        if (entry == null || entry.DataRow == null)
        {
            return;
        }

        if (_produceDetailView.Root != null
            && _produceDetailView.Root.activeSelf
            && ReferenceEquals(_produceDetailView.CurrentDataRow, entry.DataRow))
        {
            HideProduceDetail();
            return;
        }

        ShowProduceDetail(entry.DataRow);
    }

    /// <summary>
    /// 打开并刷新产出物详情面板。
    /// 字段映射：Name → TxtName；父宠物 QualityType → TxtQuality；CoinValue → TxtProperty；
    /// RewardStars → TxtOccurrenceConditions（0 时整段隐藏）；Description → TxtIntroduce；IconPath → Image。
    /// </summary>
    private void ShowProduceDetail(PetProduceDataRow row)
    {
        if (row == null || _produceDetailView.Root == null)
        {
            return;
        }

        _produceDetailView.CurrentDataRow = row;
        if (!_produceDetailView.Root.activeSelf)
        {
            _produceDetailView.Root.SetActive(true);
        }

        if (_produceDetailView.TxtName != null) _produceDetailView.TxtName.text = row.Name;

        // 品质：取关联宠物 QualityType；查不到则留空，避免 KeyNotFoundException。
        if (_produceDetailView.TxtQuality != null)
        {
            string quality = string.Empty;
            if (_petRowsById.TryGetValue(row.PetId, out PetDataRow parentPet) && parentPet != null)
            {
                quality = GetQualityLabel(parentPet.Quality);
            }

            _produceDetailView.TxtQuality.text = quality;
        }

        if (_produceDetailView.TxtProperty != null)
        {
            _produceDetailView.TxtProperty.SetText(ProduceDetailValueFormat, row.CoinValue);
        }

        if (_produceDetailView.TxtIntroduce != null)
        {
            _produceDetailView.TxtIntroduce.text = row.Description;
        }

        if (_produceDetailView.TxtOccurrenceConditions != null)
        {
            bool show = row.RewardStars > 0;
            if (_produceDetailView.TxtOccurrenceConditions.gameObject.activeSelf != show)
            {
                _produceDetailView.TxtOccurrenceConditions.gameObject.SetActive(show);
            }

            if (show)
            {
                _produceDetailView.TxtOccurrenceConditions.SetText(ProduceDetailStarsFormat, row.RewardStars);
            }
        }

        if (_produceDetailView.Icon != null)
        {
            if (!_produceDetailView.HasCachedDefaultIcon)
            {
                _produceDetailView.DefaultIconSprite = _produceDetailView.Icon.sprite;
                _produceDetailView.HasCachedDefaultIcon = true;
            }

            Sprite iconSprite = TryGetProduceSprite(row);
            SetSpriteIfChanged(_produceDetailView.Icon, iconSprite != null ? iconSprite : _produceDetailView.DefaultIconSprite);

            // 详情入口本身就是"已解锁"才能点开 → 始终白色。
            if (_produceDetailView.Icon.color != UnlockedProduceColor)
            {
                _produceDetailView.Icon.color = UnlockedProduceColor;
            }
        }
    }

    /// <summary>产出物详情根 Button 点击回调（与 GoPetDetailed 对称：点击即关闭）。</summary>
    private void OnProduceDetailCloseClicked()
    {
        UIInteractionSound.PlayClick();
        HideProduceDetail();
    }

    /// <summary>关闭产出物详情面板。</summary>
    private void HideProduceDetail()
    {
        _produceDetailView.CurrentDataRow = null;
        if (_produceDetailView.Root != null && _produceDetailView.Root.activeSelf)
        {
            _produceDetailView.Root.SetActive(false);
        }
    }

    #endregion

    #region Tab 按钮回调

    /// <summary>宠物图鉴 Tab 按钮点击：切到宠物模式。</summary>
    private void OnPetTabClicked()
    {
        UIInteractionSound.PlayClick();
        SwitchMode(CatalogMode.Pet, forceRefresh: false);
    }

    /// <summary>产出物图鉴 Tab 按钮点击：切到产出物模式。</summary>
    private void OnProduceTabClicked()
    {
        UIInteractionSound.PlayClick();
        SwitchMode(CatalogMode.Produce, forceRefresh: false);
    }

    #endregion

    #region 关闭按钮

    /// <summary>右上角 BtnClose 点击：通过 UGF 关闭当前窗体。</summary>
    private void OnBtnClose()
    {
        UIInteractionSound.PlayClick();
        if (GameEntry.UI == null)
        {
            return;
        }

        GameEntry.UI.CloseUIForm(UIForm.SerialId);
    }

    #endregion

    #region 工具方法

    /// <summary>
    /// 宠物按 (QualityType 升序, Id 升序) 排序。
    /// </summary>
    private static int ComparePetByQualityThenId(PetDataRow a, PetDataRow b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a == null) return 1;
        if (b == null) return -1;

        int qcmp = ((int)a.Quality).CompareTo((int)b.Quality);
        if (qcmp != 0) return qcmp;
        return a.Id.CompareTo(b.Id);
    }

    /// <summary>
    /// 产出物按 (父宠物 Quality 升序, PetId 升序, Grade 升序) 排序。
    /// 父宠物缺失时降级为按 (PetId, Grade) 排，确保排序稳定。
    /// </summary>
    private int CompareProduceByPetThenGrade(PetProduceDataRow a, PetProduceDataRow b)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a == null) return 1;
        if (b == null) return -1;

        int qa = _petRowsById.TryGetValue(a.PetId, out PetDataRow petA) && petA != null ? (int)petA.Quality : int.MaxValue;
        int qb = _petRowsById.TryGetValue(b.PetId, out PetDataRow petB) && petB != null ? (int)petB.Quality : int.MaxValue;
        int qcmp = qa.CompareTo(qb);
        if (qcmp != 0) return qcmp;

        int pcmp = a.PetId.CompareTo(b.PetId);
        if (pcmp != 0) return pcmp;
        return ((int)a.Grade).CompareTo((int)b.Grade);
    }

    /// <summary>仅在 sprite 真正变化时才赋值，省掉 Image 无谓的 SetVerticesDirty。</summary>
    private static void SetSpriteIfChanged(Image image, Sprite sprite)
    {
        if (image.sprite != sprite)
        {
            image.sprite = sprite;
        }
    }

    /// <summary>从 GameAssetModule 取产出物 IconPath 缓存图。</summary>
    private static Sprite TryGetProduceSprite(PetProduceDataRow row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.Code) || GameEntry.GameAssets == null)
        {
            return null;
        }

        GameEntry.GameAssets.TryGetProduceSprite(row.Code, out Sprite sprite);
        return sprite;
    }

    /// <summary>把品质枚举转中文。</summary>
    private static string GetQualityLabel(QualityType quality)
    {
        switch (quality)
        {
            case QualityType.Normal: return "普通";
            case QualityType.Rare: return "稀有";
            case QualityType.Epic: return "史诗";
            case QualityType.Legendary: return "传说";
            case QualityType.Mythic: return "神话";
            default: return string.Empty;
        }
    }

    /// <summary>根据宠物属性类型拼属性展示文本（仅在打开详情时调用，不在帧循环里）。</summary>
    private static string GetAttributeText(PetDataRow row)
    {
        if (row == null)
        {
            return string.Empty;
        }

        switch (row.AttributeType)
        {
            case PetAttributeType.ScoreBase: return string.Format("基础得分 +{0}", row.AttributeValue);
            case PetAttributeType.ComboTime: return string.Format("COMBO +{0}", row.AttributeValue);
            default: return "无额外属性";
        }
    }

    #endregion

    #region SkeletonGraphic 处理

    /// <summary>
    /// 把宠物 Spine 显示到目标挂点；优先复用已存在的 SkeletonGraphic。
    /// </summary>
    private void ApplyPetGraphic(Transform host, ref SkeletonGraphic graphic, PetDataRow row, bool isUnlocked)
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
            RequestPetUiSkeletonDataIfNeeded(row);
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

        // 严禁 new Material；统一用 SkeletonDataAsset 自带的 PrimaryMaterial，否则会出现长期材质泄漏。
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

    private void RequestPetUiSkeletonDataIfNeeded(PetDataRow row)
    {
        if (row == null || string.IsNullOrWhiteSpace(row.UiSkeletonDataPath) || GameEntry.GameAssets == null)
        {
            return;
        }

        if (GameEntry.GameAssets.TryGetPetSkeletonDataAsset(row.UiSkeletonDataPath, out SkeletonDataAsset cachedSkeletonDataAsset) && cachedSkeletonDataAsset != null)
        {
            _requestedUiSkeletonDataPaths.Remove(row.UiSkeletonDataPath);
            return;
        }

        EnsurePetSkeletonDataStateSubscription();
        if (_requestedUiSkeletonDataPaths.Add(row.UiSkeletonDataPath))
        {
            GameEntry.GameAssets.RequestPetUiSkeletonDataAsset(row);
        }
    }

    private void EnsurePetSkeletonDataStateSubscription()
    {
        if (_isListeningPetSkeletonDataStateChanged || GameEntry.GameAssets == null)
        {
            return;
        }

        GameEntry.GameAssets.PetSkeletonDataStateChanged -= OnPetSkeletonDataStateChanged;
        GameEntry.GameAssets.PetSkeletonDataStateChanged += OnPetSkeletonDataStateChanged;
        _isListeningPetSkeletonDataStateChanged = true;
    }

    private void ReleasePetSkeletonDataStateSubscription()
    {
        if (!_isListeningPetSkeletonDataStateChanged || GameEntry.GameAssets == null)
        {
            _isListeningPetSkeletonDataStateChanged = false;
            return;
        }

        GameEntry.GameAssets.PetSkeletonDataStateChanged -= OnPetSkeletonDataStateChanged;
        _isListeningPetSkeletonDataStateChanged = false;
    }

    /// <summary>
    /// SkeletonData 加载状态变化回调：仅刷新被命中的可见条目和当前详情。
    /// </summary>
    private void OnPetSkeletonDataStateChanged(string skeletonDataPath)
    {
        if (string.IsNullOrWhiteSpace(skeletonDataPath))
        {
            return;
        }

        bool isLoaded = GameEntry.GameAssets != null
            && GameEntry.GameAssets.TryGetPetSkeletonDataAsset(skeletonDataPath, out SkeletonDataAsset skeletonDataAsset)
            && skeletonDataAsset != null;
        if (isLoaded)
        {
            _requestedUiSkeletonDataPaths.Remove(skeletonDataPath);
        }

        for (int i = 0; i < _petEntries.Count; i++)
        {
            PetItemEntry entry = _petEntries[i];
            if (entry == null
                || entry.DataRow == null
                || !string.Equals(entry.DataRow.UiSkeletonDataPath, skeletonDataPath, StringComparison.Ordinal))
            {
                continue;
            }

            bool isUnlocked = GameEntry.Fruits != null && GameEntry.Fruits.IsPetUnlocked(entry.DataRow.Code);
            ApplyPetGraphic(entry.PetRoot, ref entry.PetGraphic, entry.DataRow, isUnlocked);
        }

        if (_detailView.CurrentDataRow != null
            && string.Equals(_detailView.CurrentDataRow.UiSkeletonDataPath, skeletonDataPath, StringComparison.Ordinal))
        {
            bool isUnlocked = GameEntry.Fruits != null && GameEntry.Fruits.IsPetUnlocked(_detailView.CurrentDataRow.Code);
            ApplyPetGraphic(_detailView.PetRoot, ref _detailView.PetGraphic, _detailView.CurrentDataRow, isUnlocked);
        }
    }

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
    /// 把 SkeletonGraphic 的 RectTransform 锚到底部中心，与图鉴角色站位风格保持一致。
    /// </summary>
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

    private static void SetGraphicActive(SkeletonGraphic graphic, bool isActive)
    {
        if (graphic == null || graphic.gameObject.activeSelf == isActive)
        {
            return;
        }

        graphic.gameObject.SetActive(isActive);
    }

    /// <summary>
    /// 在目标对象上取 Button，没有就补一个；背景遮罩点击关闭时不需要 Transition 动画。
    /// </summary>
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

    #endregion
}
