using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using UnityGameFramework.Runtime;

/// <summary>
/// 水果图鉴界面。
/// 负责展示所有水果的列表，显示水果名称、图标、解锁状态，
/// 并提供金币购买解锁功能。
/// 列表只在首次打开时构建（从 GoFruit 模板克隆），后续只刷新显示。
/// </summary>
public class ShuiGuoTJUIForm : UIFormLogic
{
    /// <summary>
    /// 单个水果列表条目的 UI 引用缓存。
    /// 仅在首次构建列表时实例化，后续只刷新显示内容，不产生任何额外 GC。
    /// </summary>
    private sealed class FruitItemEntry
    {
        /// <summary>条目根节点 GameObject。</summary>
        public GameObject Root;

        /// <summary>条目根节点上的按钮组件，用于点击水果卡片时打开详情面板。</summary>
        public Button ItemButton;

        /// <summary>水果名称文本（TxtFruitName 节点上的 TextMeshProUGUI）。</summary>
        public TextMeshProUGUI TxtName;

        /// <summary>水果图标（ImgSG 节点上的 Image）。</summary>
        public Image ImgSG;

        /// <summary>解锁按钮（Button 节点），未解锁时显示，已解锁时隐藏。</summary>
        public Button BtnUnlock;

        /// <summary>解锁按钮上的文本（Button/Text (TMP)），用于显示"解锁(金币数量)"。</summary>
        public TextMeshProUGUI TxtUnlockCost;

        /// <summary>绑定的水果数据行，包含 Code、Name、UnlockGold 等字段。</summary>
        public FruitDataRow DataRow;

        /// <summary>当前条目所属的水果图鉴界面实例，初始状态为 null，BuildList 构建条目时写入。</summary>
        public ShuiGuoTJUIForm Owner;

        /// <summary>当前条目在 _fruitEntries 列表中的稳定索引，BuildList 构建时写入，后续不会改变。</summary>
        public int Index;

        /// <summary>缓存后的点击委托，初始状态为 null，BuildList 中创建一次，后续 OnOpen/OnRecycle 复用。</summary>
        public UnityAction OnItemClicked;

        /// <summary>
        /// 条目点击事件入口。
        /// 使用缓存委托而不是 lambda 闭包，确保关闭界面后再次打开时可以精确 RemoveListener / AddListener。
        /// </summary>
        public void HandleItemClicked()
        {
            if (Owner == null)
            {
                return;
            }

            Owner.OnFruitItemClicked(Index);
        }
    }

    /// <summary>
    /// 水果详情面板的 UI 引用缓存。
    /// 该对象只保存 GoParticulars 与其子节点引用，不持有任何运行时生成资源。
    /// </summary>
    private sealed class FruitDetailView
    {
        /// <summary>详情面板根节点 GoParticulars，初始状态建议在 prefab 中隐藏，运行时点击水果条目后显示。</summary>
        public GameObject Root;

        /// <summary>详情面板中的水果大图 Image，用于显示当前水果图鉴图标。</summary>
        public Image ImgFruit;

        /// <summary>详情面板中的水果名称文本，用于显示 FruitDataRow.Name。</summary>
        public TextMeshProUGUI TxtName;

        /// <summary>详情面板中的描述文本，用于显示 FruitDataRow.Description。</summary>
        public TextMeshProUGUI TxtDescription;

        /// <summary>详情面板中的产出耗时文本，用于显示“产出一个需要X秒”。</summary>
        public TextMeshProUGUI TxtProduceSeconds;

        /// <summary>详情面板中的宠物回馈金币文本，用于显示“宠物会回馈 金币+xxx”。</summary>
        public TextMeshProUGUI TxtPetFeedbackGold;

        /// <summary>详情面板中的每分钟存钱金币文本，用于显示“每分钟存钱 金币+x”。</summary>
        public TextMeshProUGUI TxtSaveGoldPerMinute;

        /// <summary>详情面板中的解锁金币文本，用于显示“需要xxxxxxxx金币解锁”。</summary>
        public TextMeshProUGUI TxtUnlockGold;

        /// <summary>详情面板中的解锁按钮，由 Inspector 手动拖入；未解锁水果点击后执行购买解锁。</summary>
        public Button BtnUnlock;

        /// <summary>详情面板中的关闭按钮，用于只关闭 GoParticulars，不关闭整个水果图鉴界面。</summary>
        public Button BtnClose;

        /// <summary>详情面板当前正在展示的数据行，用于重复点击同一水果时执行关闭逻辑。</summary>
        public FruitDataRow CurrentDataRow;
    }

    /// <summary>
    /// 产出耗时文本格式。
    /// 使用 TMP SetText(format, value) 写入数值，避免手写字符串拼接。
    /// </summary>
    private const string DetailProduceSecondsFormat = "！产出一个需要{0}秒";

    /// <summary>
    /// 宠物回馈金币文本格式。
    /// 这里的金币数来自 FruitDataRow.CoinAmount，对应水果表 CoinAmount 列。
    /// </summary>
    private const string DetailPetFeedbackGoldFormat = "！宠物会回馈\t金币+{0}";

    /// <summary>
    /// 每分钟存钱金币文本格式。
    /// 当前仅做显示，无点击或结算效果；金币数来自 FruitDataRow.SaveGoldPerMinute。
    /// </summary>
    private const string DetailSaveGoldPerMinuteFormat = "！每分钟存钱\t金币+{0}";

    /// <summary>
    /// 解锁金币文本格式。
    /// 未解锁水果显示 FruitDataRow.UnlockGold，已解锁或默认解锁水果显示 0。
    /// </summary>
    private const string DetailUnlockGoldFormat = "需要{0}金币解锁";

    /// <summary>
    /// 已解锁水果图标颜色。
    /// 使用纯白乘色，保证 ImgSG 原始 Sprite 颜色完整显示。
    /// </summary>
    private static readonly Color UnlockedFruitIconColor = Color.white;

    /// <summary>
    /// 未解锁水果图标置灰颜色。
    /// 这是 UGUI Image 的顶点乘色，不会修改 Sprite 资源本体，只影响当前 Image 显示。
    /// </summary>
    private static readonly Color LockedFruitIconColor = new Color(0.45f, 0.45f, 0.45f, 1f);

    /// <summary>
    /// Scroll View → Viewport → Content 容器，所有水果条目的父节点。
    /// 用户在 Inspector 中拖入。
    /// </summary>
    [SerializeField]
    private Transform _content;

    /// <summary>
    /// GoFruit 模板节点（Content 下的示例条目），运行时隐藏并作为克隆源。
    /// 用户在 Inspector 中拖入。
    /// </summary>
    [SerializeField]
    private Transform _goFruitTemplate;

    /// <summary>
    /// 关闭按钮。用户在 Inspector 中拖入。
    /// </summary>
    [SerializeField]
    private Button _btnClose;

    /// <summary>
    /// 水果详情面板根节点 GoParticulars。
    /// 用户在 Inspector 中拖入；若未拖入，会在 OnInit 中按节点名自动查找一次。
    /// </summary>
    [SerializeField]
    private GameObject _goParticulars;

    /// <summary>
    /// 详情面板水果大图。
    /// 用户可在 Inspector 中拖入；若未拖入，会在 GoParticulars/ImgFruit 上自动查找。
    /// </summary>
    [SerializeField]
    private Image _imgParticularsFruit;

    /// <summary>
    /// 详情面板水果名字文本。
    /// 用户可在 Inspector 中拖入；若未拖入，会在 GoParticulars/TxtName 上自动查找。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtParticularsName;

    /// <summary>
    /// 详情面板水果描述文本。
    /// 文本内容来自 Fruit.txt 的 Description 列。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtParticularsDescription;

    /// <summary>
    /// 详情面板产出耗时文本，对应现有 TxtParticulars (1) 节点。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtParticularsProduceSeconds;

    /// <summary>
    /// 详情面板宠物回馈金币文本，对应现有 TxtParticulars (2) 节点。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtParticularsPetFeedbackGold;

    /// <summary>
    /// 详情面板每分钟存钱金币文本，对应现有 TxtParticulars (3) 节点。
    /// 暂时没有功能效果，只负责展示“每分钟存钱 金币+x”。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtParticularsSaveGoldPerMinute;

    /// <summary>
    /// 详情面板解锁金币文本，对应现有 TxtParticulars (4) 节点。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtParticularsUnlockGold;

    /// <summary>
    /// 详情面板解锁按钮。
    /// 按需求预留给 Inspector 手动拖入；未解锁水果点击后调用现有 TryPurchaseFruit 购买接口。
    /// </summary>
    [SerializeField]
    private Button _btnParticularsUnlock;

    /// <summary>
    /// 详情面板关闭按钮。
    /// 点击后仅隐藏 GoParticulars，不影响水果图鉴主窗体和列表滚动状态。
    /// </summary>
    [SerializeField]
    private Button _btnParticularsClose;

    /// <summary>
    /// 所有水果条目的运行时缓存列表。
    /// 预分配 20 个容量，覆盖当前 18 种水果并留有余量。
    /// </summary>
    private readonly List<FruitItemEntry> _fruitEntries = new List<FruitItemEntry>(20);

    /// <summary>
    /// 详情面板缓存对象。
    /// OnInit 只填充引用，点击水果条目时复用这些引用刷新显示。
    /// </summary>
    private readonly FruitDetailView _detailView = new FruitDetailView();

    /// <summary>
    /// 标记列表是否已构建，避免重复实例化。
    /// </summary>
    private bool _isListBuilt;

    /// <summary>
    /// 初始化时缓存所有 UI 引用并绑定关闭按钮事件。
    /// </summary>
    /// <param name="userData">用户自定义数据。</param>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);

        CacheDetailView();
        HideDetail();
    }

    /// <summary>
    /// 每次打开界面时构建列表（首次）并刷新所有条目显示。
    /// </summary>
    /// <param name="userData">用户自定义数据。</param>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);

        if (_btnClose != null)
        {
            _btnClose.onClick.AddListener(OnBtnClose);
        }

        if (_detailView.BtnUnlock != null)
        {
            _detailView.BtnUnlock.onClick.AddListener(OnBtnParticularsUnlock);
        }

        if (_detailView.BtnClose != null)
        {
            _detailView.BtnClose.onClick.AddListener(OnBtnParticularsClose);
        }

        BuildList();
        BindFruitItemClickEvents();
        RefreshAllItems();
        HideDetail();
    }

    /// <summary>
    /// 界面关闭时无需额外清理，列表缓存保留供下次复用。
    /// </summary>
    /// <param name="isShutdown">是否为关闭流程。</param>
    /// <param name="userData">用户自定义数据。</param>
    protected override void OnClose(bool isShutdown, object userData)
    {
        if (_btnClose != null)
        {
            _btnClose.onClick.RemoveListener(OnBtnClose);
        }

        if (_detailView.BtnUnlock != null)
        {
            _detailView.BtnUnlock.onClick.RemoveListener(OnBtnParticularsUnlock);
        }

        if (_detailView.BtnClose != null)
        {
            _detailView.BtnClose.onClick.RemoveListener(OnBtnParticularsClose);
        }

        base.OnClose(isShutdown, userData);
    }

    /// <summary>
    /// 界面回收时解绑本脚本动态注册的按钮事件，防止泄漏。
    /// </summary>
    protected override void OnRecycle()
    {
        // 只移除本脚本注册的水果条目点击事件。
        // 警告：这里不能使用 RemoveAllListeners。
        // 原因是 UGF 的 UIForm 可能被对象池复用：关闭 FruitTJUIForm 后再次打开时，
        // _isListBuilt 仍然为 true，BuildList 不会重新实例化条目；如果上一轮回收时把监听全部清空，
        // 下一次打开后点击条目就不会再进入 OnFruitItemClicked，自然也无法显示 GoParticulars。
        for (int i = 0; i < _fruitEntries.Count; i++)
        {
            FruitItemEntry entry = _fruitEntries[i];
            if (entry != null && entry.ItemButton != null && entry.OnItemClicked != null)
            {
                entry.ItemButton.onClick.RemoveListener(entry.OnItemClicked);
            }
        }

        _detailView.CurrentDataRow = null;

        base.OnRecycle();
    }

    /// <summary>
    /// 缓存详情面板中的全部 UI 引用。
    /// 优先使用 Inspector 序列化字段，缺失时再按当前 prefab 的固定节点名自动查找一次。
    /// </summary>
    private void CacheDetailView()
    {
        if (_goParticulars == null)
        {
            Transform particularsTransform = transform.Find("GoParticulars");
            if (particularsTransform != null)
            {
                _goParticulars = particularsTransform.gameObject;
            }
        }

        _detailView.Root = _goParticulars;
        if (_detailView.Root == null)
        {
            return;
        }

        Transform rootTransform = _detailView.Root.transform;
        _detailView.ImgFruit = _imgParticularsFruit != null
            ? _imgParticularsFruit
            : GetChildComponent<Image>(rootTransform, "ImgFruit");
        _detailView.TxtName = _txtParticularsName != null
            ? _txtParticularsName
            : GetChildComponent<TextMeshProUGUI>(rootTransform, "TxtName");
        _detailView.TxtDescription = _txtParticularsDescription != null
            ? _txtParticularsDescription
            : GetChildComponent<TextMeshProUGUI>(rootTransform, "TxtParticulars");
        _detailView.TxtProduceSeconds = _txtParticularsProduceSeconds != null
            ? _txtParticularsProduceSeconds
            : GetChildComponent<TextMeshProUGUI>(rootTransform, "TxtParticulars (1)");
        _detailView.TxtPetFeedbackGold = _txtParticularsPetFeedbackGold != null
            ? _txtParticularsPetFeedbackGold
            : GetChildComponent<TextMeshProUGUI>(rootTransform, "TxtParticulars (2)");
        _detailView.TxtSaveGoldPerMinute = _txtParticularsSaveGoldPerMinute != null
            ? _txtParticularsSaveGoldPerMinute
            : GetChildComponent<TextMeshProUGUI>(rootTransform, "TxtParticulars (3)");
        _detailView.TxtUnlockGold = _txtParticularsUnlockGold != null
            ? _txtParticularsUnlockGold
            : GetChildComponent<TextMeshProUGUI>(rootTransform, "TxtParticulars (4)");
        _detailView.BtnUnlock = _btnParticularsUnlock;
        _detailView.BtnClose = _btnParticularsClose;
    }

    /// <summary>
    /// 从指定父节点的直接子节点中查找组件。
    /// 该方法只在 OnInit 初始化阶段调用，不在 Update 等高频路径中执行。
    /// </summary>
    /// <typeparam name="T">需要获取的 Unity 组件类型。</typeparam>
    /// <param name="parent">查找起点父节点。</param>
    /// <param name="childName">直接子节点名称。</param>
    /// <returns>找到的组件；父节点、子节点或组件不存在时返回 null。</returns>
    private static T GetChildComponent<T>(Transform parent, string childName) where T : Component
    {
        if (parent == null)
        {
            return null;
        }

        Transform child = parent.Find(childName);
        return child != null ? child.GetComponent<T>() : null;
    }


    /// <summary>
    /// 首次打开时根据水果数据表构建完整列表。
    /// 第一个条目复用 prefab 中已有的 GoFruit 模板节点，
    /// 后续条目从模板克隆并挂到 Content 下。
    /// 构建完成后设置 _isListBuilt 标记，后续打开不再重复构建。
    /// </summary>
    private void BuildList()
    {
        // 防止重复构建、缺少容器或缺少模板
        if (_isListBuilt || _content == null || _goFruitTemplate == null)
        {
            return;
        }

        if (GameEntry.DataTables == null)
        {
            return;
        }

        FruitDataRow[] allRows = GameEntry.DataTables.GetAllDataRows<FruitDataRow>();
        if (allRows == null || allRows.Length == 0)
        {
            return;
        }

        // 按 Id 升序排列，保证列表顺序与数据表定义一致
        Array.Sort(allRows, CompareFruitById);

        // 模板节点仅作为克隆源，永迎隐藏，不参与实际显示
        _goFruitTemplate.gameObject.SetActive(false);

        for (int i = 0; i < allRows.Length; i++)
        {
            // 所有条目均从模板克隆，克隆后激活显示
            Transform itemTransform = Instantiate(_goFruitTemplate, _content);
            itemTransform.gameObject.SetActive(true);

            FruitItemEntry entry = new FruitItemEntry
            {
                Root = itemTransform.gameObject,
                ItemButton = itemTransform.GetComponent<Button>(),
                DataRow = allRows[i],
                Index = i
            };

            entry.OnItemClicked = entry.HandleItemClicked;
            entry.Owner = this;

            // 缓存水果名称文本组件
            Transform txtName = itemTransform.Find("ImgTop/TxtFruitName");
            if (txtName != null)
            {
                entry.TxtName = txtName.GetComponent<TextMeshProUGUI>();
            }

            // 缓存水果图标组件
            Transform imgSG = itemTransform.Find("ImgSG");
            if (imgSG != null)
            {
                entry.ImgSG = imgSG.GetComponent<Image>();
            }

            // 缓存解锁按钮及按钮上的金币文本
            Transform btnTransform = itemTransform.Find("Button");
            if (btnTransform != null)
            {
                entry.BtnUnlock = btnTransform.GetComponent<Button>();

                // 按钮内嵌的文本节点名固定为 "Text (TMP)"
                Transform txtUnlock = btnTransform.Find("Text (TMP)");
                if (txtUnlock != null)
                {
                    entry.TxtUnlockCost = txtUnlock.GetComponent<TextMeshProUGUI>();
                }
            }

            _fruitEntries.Add(entry);
        }

        _isListBuilt = true;
    }

    /// <summary>
    /// 为已经构建完成的水果条目绑定点击事件。
    /// 该方法会在每次 OnOpen 后执行：先移除本脚本旧监听，再绑定当前监听，
    /// 既能修复 UIForm 对象池复用后的监听丢失，也能避免重复 AddListener 导致一次点击触发多次。
    /// </summary>
    private void BindFruitItemClickEvents()
    {
        for (int i = 0; i < _fruitEntries.Count; i++)
        {
            FruitItemEntry entry = _fruitEntries[i];
            if (entry == null || entry.ItemButton == null || entry.OnItemClicked == null)
            {
                continue;
            }

            entry.ItemButton.onClick.RemoveListener(entry.OnItemClicked);
            entry.ItemButton.onClick.AddListener(entry.OnItemClicked);
        }
    }

    /// <summary>
    /// FruitDataRow 按 Id 升序比较器，供 Array.Sort 使用。
    /// 独立为静态方法避免每次排序分配新的委托。
    /// </summary>
    /// <param name="a">水果数据行 A。</param>
    /// <param name="b">水果数据行 B。</param>
    /// <returns>比较结果。</returns>
    private static int CompareFruitById(FruitDataRow a, FruitDataRow b)
    {
        return a.Id.CompareTo(b.Id);
    }

    /// <summary>
    /// 刷新所有条目的显示状态。
    /// </summary>
    private void RefreshAllItems()
    {
        for (int i = 0; i < _fruitEntries.Count; i++)
        {
            RefreshItem(_fruitEntries[i]);
        }
    }

    /// <summary>
    /// 刷新单个条目的显示内容。
    /// 根据水果数据和运行时解锁状态更新：
    /// - 水果名称（TxtFruitName）
    /// - 水果图标（ImgSG），通过 GameEntry.GameAssets.TryGetFruitSprite 加载
    /// - 水果图标颜色（ImgSG）：未解锁置灰，已解锁恢复白色
    /// - 解锁按钮可见性（Button）：未解锁显示，已解锁隐藏
    /// - 按钮文案：显示 "解锁(金币数量)"
    /// </summary>
    /// <param name="entry">要刷新的条目。</param>
    private static void RefreshItem(FruitItemEntry entry)
    {
        if (entry == null || entry.DataRow == null)
        {
            return;
        }

        // 判定解锁状态：数据表默认解锁（IsUnlocked=true）或运行时已购买解锁
        bool isUnlocked = entry.DataRow.IsUnlocked
            || (GameEntry.Fruits != null && GameEntry.Fruits.IsFruitUnlocked(entry.DataRow.Code));

        // 刷新水果名称
        if (entry.TxtName != null)
        {
            entry.TxtName.text = entry.DataRow.Name;
        }

        // 刷新水果图标 —— 通过 Code 从预加载缓存中取 Sprite。
        // 未解锁时不再使用 ImgMR 遮罩，而是直接给 ImgSG 设置灰色顶点乘色。
        if (entry.ImgSG != null)
        {
            if (GameEntry.GameAssets != null && GameEntry.GameAssets.TryGetFruitSprite(entry.DataRow.Code, out Sprite sprite))
            {
                entry.ImgSG.sprite = sprite;
            }

            entry.ImgSG.color = isUnlocked ? UnlockedFruitIconColor : LockedFruitIconColor;
        }

        // 列表内原解锁按钮已废弃：购买入口统一迁移到 GoParticulars 详情按钮。
        if (entry.BtnUnlock != null)
        {
            entry.BtnUnlock.gameObject.SetActive(false);
        }

        // 列表解锁文案不再显示，避免用户误以为列表按钮仍可购买。
    }

    /// <summary>
    /// 按指定水果条目执行购买解锁。
    /// 通过 PlayerRuntimeModule.TryPurchaseFruit 原子完成“扣金币 + 解锁水果”，
    /// 成功后立即刷新当前条目的显示状态。
    /// </summary>
    /// <param name="index">被点击条目在列表中的索引。</param>
    private void TryUnlockFruitByIndex(int index)
    {
        if (index < 0 || index >= _fruitEntries.Count)
        {
            return;
        }

        FruitItemEntry entry = _fruitEntries[index];
        if (entry == null || entry.DataRow == null || GameEntry.Fruits == null)
        {
            return;
        }

        // 调用原子购买接口：校验数据行 → 校验未解锁 → 校验金币 → 扣金币 → 解锁
        if (GameEntry.Fruits.TryPurchaseFruit(entry.DataRow.Code))
        {
            RefreshItem(entry);
            if (ReferenceEquals(_detailView.CurrentDataRow, entry.DataRow))
            {
                ShowDetail(entry.DataRow);
            }
        }
    }

    /// <summary>
    /// 详情面板解锁按钮点击回调。
    /// 只对 GoParticulars 当前展示的水果执行购买，避免列表按钮和详情按钮出现双入口状态不一致。
    /// </summary>
    private void OnBtnParticularsUnlock()
    {
        // 播放点击音效。
        UIInteractionSound.PlayClick();

        FruitDataRow currentDataRow = _detailView.CurrentDataRow;
        if (currentDataRow == null)
        {
            return;
        }

        for (int i = 0; i < _fruitEntries.Count; i++)
        {
            FruitItemEntry entry = _fruitEntries[i];
            if (entry != null && ReferenceEquals(entry.DataRow, currentDataRow))
            {
                TryUnlockFruitByIndex(i);
                return;
            }
        }
    }

    /// <summary>
    /// 水果条目点击回调。
    /// 点击 Content 下的水果按钮时打开 GoParticulars，并把当前水果的数据写入详情面板。
    /// </summary>
    /// <param name="index">被点击条目在缓存列表中的索引。</param>
    private void OnFruitItemClicked(int index)
    {
        // 播放统一 UI 点击音效，保持与解锁按钮、关闭按钮的反馈一致。
        UIInteractionSound.PlayClick();

        if (index < 0 || index >= _fruitEntries.Count)
        {
            return;
        }

        FruitItemEntry entry = _fruitEntries[index];
        if (entry == null || entry.DataRow == null)
        {
            return;
        }

        ShowDetail(entry.DataRow);
    }

    /// <summary>
    /// 显示并刷新水果详情面板。
    /// 该方法只在点击条目或解锁成功后调用，不属于高频路径。
    /// </summary>
    /// <param name="row">需要展示的水果数据行。</param>
    private void ShowDetail(FruitDataRow row)
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

        bool isUnlocked = row.IsUnlocked
            || (GameEntry.Fruits != null && GameEntry.Fruits.IsFruitUnlocked(row.Code));

        if (_detailView.ImgFruit != null)
        {
            if (GameEntry.GameAssets != null && GameEntry.GameAssets.TryGetFruitSprite(row.Code, out Sprite sprite))
            {
                _detailView.ImgFruit.sprite = sprite;
            }

            _detailView.ImgFruit.color = isUnlocked ? UnlockedFruitIconColor : LockedFruitIconColor;
        }

        if (_detailView.TxtName != null)
        {
            _detailView.TxtName.text = row.Name;
        }

        if (_detailView.TxtDescription != null)
        {
            _detailView.TxtDescription.text = row.Description;
        }

        if (_detailView.TxtProduceSeconds != null)
        {
            _detailView.TxtProduceSeconds.SetText(DetailProduceSecondsFormat, row.ProduceSeconds);
        }

        if (_detailView.TxtPetFeedbackGold != null)
        {
            _detailView.TxtPetFeedbackGold.SetText(DetailPetFeedbackGoldFormat, row.CoinAmount);
        }

        if (_detailView.TxtSaveGoldPerMinute != null)
        {
            _detailView.TxtSaveGoldPerMinute.SetText(DetailSaveGoldPerMinuteFormat, row.SaveGoldPerMinute);
        }

        if (_detailView.TxtUnlockGold != null)
        {
            _detailView.TxtUnlockGold.gameObject.SetActive(!isUnlocked);
            if (!isUnlocked)
            {
                _detailView.TxtUnlockGold.SetText(DetailUnlockGoldFormat, row.UnlockGold);
            }
        }

        if (_detailView.BtnUnlock != null)
        {
            _detailView.BtnUnlock.gameObject.SetActive(!isUnlocked);
        }
    }

    /// <summary>
    /// 详情面板关闭按钮点击回调。
    /// 只隐藏 GoParticulars，让玩家可以继续浏览 Content 列表。
    /// </summary>
    private void OnBtnParticularsClose()
    {
        // 播放点击音效。
        UIInteractionSound.PlayClick();

        HideDetail();
    }

    /// <summary>
    /// 隐藏水果详情面板。
    /// 界面打开时默认隐藏，避免 GoParticulars 保留 prefab 中的占位文案。
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
    /// 通过 UIForm.SerialId 告知 UI 管理器关闭本窗体。
    /// </summary>
    private void OnBtnClose()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();

        if (GameEntry.UI == null)
        {
            return;
        }

        GameEntry.UI.CloseUIForm(UIForm.SerialId);
    }
}
