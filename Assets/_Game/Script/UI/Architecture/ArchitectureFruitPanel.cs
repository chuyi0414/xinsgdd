using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using TMPro;
using UnityGameFramework.Runtime;

/// <summary>
/// 水果图鉴子面板（挂载在 ArchitectureMenuUIForm 预制体的 Fruits 节点上）。
/// 负责构建水果列表、刷新条目显示、展示水果详情、执行金币购买解锁。
/// 由 ArchitectureMenuUIForm 持有引用，切换顶层「水果」Tab 时调用 Activate()。
/// </summary>
public class ArchitectureFruitPanel : MonoBehaviour
{
    /// <summary>
    /// 单个水果列表条目的 UI 引用缓存。
    /// 只在首次构建列表时实例化，后续只刷新显示内容，不产生额外 GC。
    /// </summary>
    private sealed class FruitItemEntry
    {
        /// <summary>条目根节点 GameObject。</summary>
        public GameObject Root;

        /// <summary>条目根节点上的按钮组件，点击打开详情面板。</summary>
        public Button ItemButton;

        /// <summary>水果名称文本。</summary>
        public TextMeshProUGUI TxtName;

        /// <summary>水果图标 Image。</summary>
        public Image ImgSG;

        /// <summary>解锁/已解锁按钮，未解锁时显示，已解锁时隐藏。</summary>
        public Button BtnUnlock;

        /// <summary>解锁按钮上的金币或"已解锁"文本。</summary>
        public TextMeshProUGUI TxtUnlockCost;

        /// <summary>已解锁图片（节点名由预制体决定，如 ImgUnlocked），已解锁时显示，未解锁时隐藏。</summary>
        public GameObject GoUnlockedImage;

        /// <summary>绑定的水果数据行。</summary>
        public FruitDataRow DataRow;

        /// <summary>当前条目在列表中的稳定索引。</summary>
        public int Index;

        /// <summary>所属面板实例，HandleItemClicked 时回调用。</summary>
        public ArchitectureFruitPanel Owner;

        /// <summary>缓存后的点击委托，避免每次 AddListener 分配闭包。</summary>
        public UnityAction OnItemClicked;

        /// <summary>条目点击入口，转发给 Owner。</summary>
        public void HandleItemClicked()
        {
            if (Owner == null) return;
            Owner.OnFruitItemClicked(Index);
        }
    }

    // ───────────── 文本格式常量 ─────────────

    /// <summary>详情面板产出耗时格式。</summary>
    private const string DetailProduceSecondsFormat = "！产出一个需要{0}秒";

    /// <summary>详情面板宠物回馈金币格式。</summary>
    private const string DetailPetFeedbackGoldFormat = "！宠物会回馈\t金币+{0}";

    /// <summary>详情面板解锁星星条件格式。</summary>
    private const string DetailRequiredStarsFormat = "！解锁条件\t星星{0}个";

    /// <summary>详情面板解锁金币格式。</summary>
    private const string DetailUnlockGoldFormat = "需要{0}金币解锁";

    /// <summary>列表条目未解锁金币文案格式。</summary>
    private const string ListItemLockedGoldFormat = "{0}";

    /// <summary>已解锁水果图标颜色（纯白，完整显示原始 Sprite 颜色）。</summary>
    private static readonly Color UnlockedFruitIconColor = Color.white;

    /// <summary>未解锁水果图标颜色（纯白，保持灰度效果由 Sprite 本身决定）。</summary>
    private static readonly Color LockedFruitIconColor = Color.white;

    // ───────────── Inspector 字段 ─────────────

    /// <summary>Scroll View → Viewport → Content，所有水果条目的父节点。</summary>
    [SerializeField] private Transform _content;

    /// <summary>GoFruit 模板节点（Content 下的示例条目），运行时隐藏并作为克隆源。</summary>
    [SerializeField] private Transform _goFruitTemplate;

    /// <summary>详情面板根节点 GoParticulars。</summary>
    [SerializeField] private GameObject _goParticulars;

    /// <summary>详情面板水果大图 Image。</summary>
    [SerializeField] private Image _imgParticularsFruit;

    /// <summary>详情面板水果名称文本。</summary>
    [SerializeField] private TextMeshProUGUI _txtParticularsName;

    /// <summary>详情面板水果描述文本。</summary>
    [SerializeField] private TextMeshProUGUI _txtParticularsDescription;

    /// <summary>详情面板产出耗时文本。</summary>
    [SerializeField] private TextMeshProUGUI _txtParticularsProduceSeconds;

    /// <summary>详情面板宠物回馈金币文本。</summary>
    [SerializeField] private TextMeshProUGUI _txtParticularsPetFeedbackGold;

    /// <summary>详情面板解锁星星条件文本。</summary>
    [SerializeField] private TextMeshProUGUI _txtParticularsRequiredStars;

    /// <summary>详情面板解锁金币文本。</summary>
    [SerializeField] private TextMeshProUGUI _txtParticularsUnlockGold;

    /// <summary>详情面板解锁按钮。</summary>
    [SerializeField] private Button _btnParticularsUnlock;

    /// <summary>详情面板关闭按钮。</summary>
    [SerializeField] private Button _btnParticularsClose;

    /// <summary>详情面板已解锁图片容器（已解锁时显示，未解锁时隐藏）。</summary>
    [SerializeField] private GameObject _goParticularsUnlocked;

    // ───────────── 内部状态 ─────────────

    /// <summary>所有水果条目缓存列表，预分配 20 容量（当前 18 种水果有余量）。</summary>
    private readonly List<FruitItemEntry> _fruitEntries = new List<FruitItemEntry>(20);

    /// <summary>详情面板当前展示的数据行，用于解锁成功后刷新详情。</summary>
    private FruitDataRow _currentDetailRow;

    /// <summary>标记列表是否已构建，避免重复实例化。</summary>
    private bool _isListBuilt;

    // ───────────── 对外 API ─────────────

    /// <summary>
    /// 激活水果面板：首次构建列表，每次刷新条目。
    /// 由 ArchitectureMenuUIForm.SwitchMainTab 切换到水果 Tab 时调用（显隐已由父级控制）。
    /// </summary>
    public void Activate()
    {
        CacheDetailRefs();

        if (!_isListBuilt)
        {
            BuildList();
            BindItemClickEvents();
        }

        RefreshAllItems();
        HideDetail();
    }

    /// <summary>
    /// 停用面板：清空详情状态。
    /// 由 ArchitectureMenuUIForm.SwitchMainTab 切换到其他 Tab 时调用。
    /// </summary>
    public void Deactivate()
    {
        HideDetail();
    }

    // ───────────── 详情面板引用缓存 ─────────────

    /// <summary>
    /// 缓存详情面板 UI 引用。优先使用 Inspector 序列化字段，缺失时按节点名自动查找一次。
    /// 只在首次 Activate 时执行，不在高频路径中调用。
    /// </summary>
    private void CacheDetailRefs()
    {
        if (_goParticulars == null) return;

        Transform root = _goParticulars.transform;
        _imgParticularsFruit           = _imgParticularsFruit           ?? FindChild<Image>(root, "ImgFruit");
        _txtParticularsName            = _txtParticularsName            ?? FindChild<TextMeshProUGUI>(root, "TxtName");
        _txtParticularsDescription     = _txtParticularsDescription     ?? FindChild<TextMeshProUGUI>(root, "TxtParticulars");
        _txtParticularsProduceSeconds  = _txtParticularsProduceSeconds  ?? FindChild<TextMeshProUGUI>(root, "TxtParticulars (1)");
        _txtParticularsPetFeedbackGold = _txtParticularsPetFeedbackGold ?? FindChild<TextMeshProUGUI>(root, "TxtParticulars (2)");
        _txtParticularsRequiredStars   = _txtParticularsRequiredStars   ?? FindChild<TextMeshProUGUI>(root, "TxtParticulars (3)");
        _txtParticularsUnlockGold      = _txtParticularsUnlockGold      ?? FindChild<TextMeshProUGUI>(root, "TxtParticulars (4)");

        if (_btnParticularsClose != null)
        {
            _btnParticularsClose.onClick.RemoveAllListeners();
            _btnParticularsClose.onClick.AddListener(OnBtnDetailClose);
        }

        if (_btnParticularsUnlock != null)
        {
            _btnParticularsUnlock.onClick.RemoveAllListeners();
            _btnParticularsUnlock.onClick.AddListener(OnBtnDetailUnlock);
        }

        HideDetail();
    }

    // ───────────── 列表构建 ─────────────

    /// <summary>
    /// 根据 FruitDataRow 数据表构建水果列表。
    /// 所有条目均从 _goFruitTemplate 克隆，克隆后激活显示。
    /// </summary>
    private void BuildList()
    {
        if (_isListBuilt || _content == null || _goFruitTemplate == null || GameEntry.DataTables == null) return;

        FruitDataRow[] allRows = GameEntry.DataTables.GetAllDataRows<FruitDataRow>();
        if (allRows == null || allRows.Length == 0) return;

        // 按 Id 升序排列，保证顺序与数据表定义一致
        Array.Sort(allRows, (a, b) => a.Id.CompareTo(b.Id));

        _goFruitTemplate.gameObject.SetActive(false);

        for (int i = 0; i < allRows.Length; i++)
        {
            Transform itemTransform = Instantiate(_goFruitTemplate, _content);
            itemTransform.gameObject.SetActive(true);

            FruitItemEntry entry = new FruitItemEntry
            {
                Root       = itemTransform.gameObject,
                ItemButton = itemTransform.GetComponent<Button>(),
                DataRow    = allRows[i],
                Index      = i,
                Owner      = this
            };
            entry.OnItemClicked = entry.HandleItemClicked;

            // 缓存水果名称文本（节点路径固定为 ImgTop/TxtFruitName）
            Transform txtNameNode = itemTransform.Find("ImgTop/TxtFruitName");
            if (txtNameNode != null) entry.TxtName = txtNameNode.GetComponent<TextMeshProUGUI>();

            // 缓存水果图标
            Transform imgSGNode = itemTransform.Find("ImgSG");
            if (imgSGNode != null) entry.ImgSG = imgSGNode.GetComponent<Image>();

            // 缓存解锁按钮及其内嵌文本（节点名固定为 "Button" 和 "Text (TMP)"）
            Transform btnNode = itemTransform.Find("Button");
            if (btnNode != null)
            {
                entry.BtnUnlock = btnNode.GetComponent<Button>();
                Transform txtUnlockNode = btnNode.Find("Text (TMP)");
                if (txtUnlockNode != null) entry.TxtUnlockCost = txtUnlockNode.GetComponent<TextMeshProUGUI>();
            }

            // 缓存已解锁图片（节点名 ImgUnlocked，预制体中默认隐藏）
            Transform unlockedNode = itemTransform.Find("ImgUnlocked");
            if (unlockedNode != null) entry.GoUnlockedImage = unlockedNode.gameObject;

            _fruitEntries.Add(entry);
        }

        _isListBuilt = true;
    }

    /// <summary>
    /// 为已构建的条目绑定点击事件。
    /// 先 Remove 再 Add，防止重复监听导致一次点击触发多次。
    /// </summary>
    private void BindItemClickEvents()
    {
        for (int i = 0; i < _fruitEntries.Count; i++)
        {
            FruitItemEntry entry = _fruitEntries[i];
            if (entry?.OnItemClicked == null) continue;

            if (entry.ItemButton != null)
            {
                entry.ItemButton.onClick.RemoveListener(entry.OnItemClicked);
                entry.ItemButton.onClick.AddListener(entry.OnItemClicked);
            }

            if (entry.BtnUnlock != null)
            {
                entry.BtnUnlock.onClick.RemoveListener(entry.OnItemClicked);
                entry.BtnUnlock.onClick.AddListener(entry.OnItemClicked);
            }
        }
    }

    // ───────────── 列表刷新 ─────────────

    /// <summary>刷新所有条目显示。</summary>
    private void RefreshAllItems()
    {
        for (int i = 0; i < _fruitEntries.Count; i++) RefreshItem(_fruitEntries[i]);
    }

    /// <summary>
    /// 刷新单个条目：名称、图标、按钮文案。
    /// 已解锁水果按钮显示"已解锁"，未解锁显示所需金币数。
    /// </summary>
    private static void RefreshItem(FruitItemEntry entry)
    {
        if (entry?.DataRow == null) return;

        bool isUnlocked = entry.DataRow.IsUnlocked
            || (GameEntry.Fruits != null && GameEntry.Fruits.IsFruitUnlocked(entry.DataRow.Code));

        if (entry.TxtName != null)
            entry.TxtName.text = entry.DataRow.Name;

        if (entry.ImgSG != null)
        {
            if (GameEntry.GameAssets != null
                && GameEntry.GameAssets.TryGetFruitSprite(entry.DataRow.Code, out Sprite sprite))
            {
                entry.ImgSG.sprite = sprite;
            }
            entry.ImgSG.color = isUnlocked ? UnlockedFruitIconColor : LockedFruitIconColor;
        }

        // 未解锁：显示按钮（含金币文案）；已解锁：隐藏按钮，显示已解锁图片
        if (entry.BtnUnlock != null)
        {
            entry.BtnUnlock.gameObject.SetActive(!isUnlocked);
            if (!isUnlocked && entry.TxtUnlockCost != null)
            {
                entry.TxtUnlockCost.SetText(string.Format(ListItemLockedGoldFormat, entry.DataRow.UnlockGold));
            }
        }

        if (entry.GoUnlockedImage != null)
        {
            entry.GoUnlockedImage.SetActive(isUnlocked);
        }
    }

    // ───────────── 详情面板 ─────────────

    /// <summary>显示并刷新水果详情面板。</summary>
    private void ShowDetail(FruitDataRow row)
    {
        if (row == null || _goParticulars == null) return;

        _currentDetailRow = row;
        if (!_goParticulars.activeSelf) _goParticulars.SetActive(true);

        bool isUnlocked = row.IsUnlocked
            || (GameEntry.Fruits != null && GameEntry.Fruits.IsFruitUnlocked(row.Code));

        if (_imgParticularsFruit != null)
        {
            if (GameEntry.GameAssets != null
                && GameEntry.GameAssets.TryGetFruitSprite(row.Code, out Sprite sprite))
            {
                _imgParticularsFruit.sprite = sprite;
            }
            _imgParticularsFruit.color = isUnlocked ? UnlockedFruitIconColor : LockedFruitIconColor;
        }

        if (_txtParticularsName != null)
            _txtParticularsName.text = row.Name;

        if (_txtParticularsDescription != null)
            _txtParticularsDescription.text = row.Description;

        if (_txtParticularsProduceSeconds != null)
            _txtParticularsProduceSeconds.SetText(DetailProduceSecondsFormat, row.ProduceSeconds);

        if (_txtParticularsPetFeedbackGold != null)
            _txtParticularsPetFeedbackGold.SetText(DetailPetFeedbackGoldFormat, row.CoinAmount);

        if (_txtParticularsRequiredStars != null)
        {
            bool show = row.RequiredStars > 0;
            if (_txtParticularsRequiredStars.gameObject.activeSelf != show)
                _txtParticularsRequiredStars.gameObject.SetActive(show);
            if (show) _txtParticularsRequiredStars.SetText(DetailRequiredStarsFormat, row.RequiredStars);
        }

        if (_txtParticularsUnlockGold != null)
        {
            _txtParticularsUnlockGold.gameObject.SetActive(!isUnlocked);
            if (!isUnlocked) _txtParticularsUnlockGold.SetText(DetailUnlockGoldFormat, row.UnlockGold);
        }

        if (_btnParticularsUnlock != null)
        {
            _btnParticularsUnlock.gameObject.SetActive(!isUnlocked);

            // 未解锁时根据玩家资源是否充足给按钮变暗（按钮始终可点击，点击后弹 Toast 提示）
            if (!isUnlocked)
            {
                int currentStars = GameEntry.Fruits != null ? GameEntry.Fruits.CurrentStars : 0;
                int currentGold  = GameEntry.Fruits != null ? GameEntry.Fruits.CurrentGold  : 0;
                bool canAfford   = currentStars >= row.RequiredStars && currentGold >= row.UnlockGold;

                Image btnImage = _btnParticularsUnlock.GetComponent<Image>();
                if (btnImage != null)
                {
                    btnImage.color = canAfford ? Color.white : new Color(0.4f, 0.4f, 0.4f, 1f);
                }
            }
        }

        if (_goParticularsUnlocked != null)
            _goParticularsUnlocked.SetActive(isUnlocked);
    }

    /// <summary>隐藏水果详情面板。</summary>
    private void HideDetail()
    {
        _currentDetailRow = null;
        if (_goParticulars != null && _goParticulars.activeSelf) _goParticulars.SetActive(false);
    }

    // ───────────── 购买解锁 ─────────────

    /// <summary>
    /// 按指定索引执行水果购买解锁。
    /// 成功后刷新该条目，若详情面板正在展示同一水果则同步刷新详情。
    /// </summary>
    private void TryUnlockByIndex(int index)
    {
        if (index < 0 || index >= _fruitEntries.Count) return;

        FruitItemEntry entry = _fruitEntries[index];
        if (entry?.DataRow == null || GameEntry.Fruits == null) return;

        if (GameEntry.Fruits.TryPurchaseFruit(entry.DataRow.Code,
                out PlayerRuntimeModule.FruitPurchaseFailureReason reason))
        {
            ToastUtility.Show("解锁成功");
            RefreshItem(entry);
            if (ReferenceEquals(_currentDetailRow, entry.DataRow)) ShowDetail(entry.DataRow);
        }
        else
        {
            switch (reason)
            {
                case PlayerRuntimeModule.FruitPurchaseFailureReason.NotEnoughStars:
                    ToastUtility.Show("星星不足");
                    break;
                case PlayerRuntimeModule.FruitPurchaseFailureReason.NotEnoughGold:
                    ToastUtility.Show("金币不足");
                    break;
                default:
                    ToastUtility.Show("解锁失败");
                    break;
            }
        }
    }

    // ───────────── 事件回调 ─────────────

    /// <summary>水果条目点击回调：打开详情面板。</summary>
    private void OnFruitItemClicked(int index)
    {
        UIInteractionSound.PlayClick();
        if (index < 0 || index >= _fruitEntries.Count) return;
        FruitItemEntry entry = _fruitEntries[index];
        if (entry?.DataRow == null) return;
        ShowDetail(entry.DataRow);
    }

    /// <summary>详情面板解锁按钮回调：找到对应条目执行购买。</summary>
    private void OnBtnDetailUnlock()
    {
        UIInteractionSound.PlayClick();
        if (_currentDetailRow == null) return;

        for (int i = 0; i < _fruitEntries.Count; i++)
        {
            FruitItemEntry entry = _fruitEntries[i];
            if (entry != null && ReferenceEquals(entry.DataRow, _currentDetailRow))
            {
                TryUnlockByIndex(i);
                return;
            }
        }
    }

    /// <summary>详情面板关闭按钮回调：只隐藏详情，不影响列表。</summary>
    private void OnBtnDetailClose()
    {
        UIInteractionSound.PlayClick();
        HideDetail();
    }

    // ───────────── 工具方法 ─────────────

    /// <summary>从父节点的直接子节点中查找组件（只在初始化阶段使用，不在高频路径执行）。</summary>
    private static T FindChild<T>(Transform parent, string childName) where T : Component
    {
        if (parent == null) return null;
        Transform child = parent.Find(childName);
        return child != null ? child.GetComponent<T>() : null;
    }
}
