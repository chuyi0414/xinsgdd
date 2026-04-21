using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 购买蛋界面。
/// 负责展示商店蛋列表、执行购买和关闭窗体。
/// </summary>
public sealed class PurchaseEggsUIForm : UIFormLogic
{
    /// <summary>
    /// 当前界面固定展示的商店蛋数量。
    /// </summary>
    private const int ShopEggDisplayCount = 5;

    /// <summary>
    /// 关闭按钮。
    /// </summary>
    [SerializeField]
    private Button _btnClose;

    /// <summary>
    /// 商店蛋条目根节点。
    /// </summary>
    [SerializeField]
    private RectTransform _eggsRoot;

    /// <summary>
    /// 商店条目视图缓存。
    /// </summary>
    private readonly EggShopEntryView[] _entryViews = new EggShopEntryView[ShopEggDisplayCount];

    /// <summary>
    /// 当前绑定到界面的商店蛋数据。
    /// </summary>
    private readonly EggDataRow[] _shopEggRows = new EggDataRow[ShopEggDisplayCount];

    /// <summary>
    /// 当前界面是否已完成节点缓存。
    /// </summary>
    private bool _isReady;

    /// <summary>
    /// 单个商店蛋条目的界面缓存。
    /// </summary>
    private sealed class EggShopEntryView
    {
        /// <summary>
        /// 条目根节点。
        /// </summary>
        public RectTransform Root;

        /// <summary>
        /// 蛋图标。
        /// </summary>
        public Image Icon;

        /// <summary>
        /// 详情文本。
        /// </summary>
        public TextMeshProUGUI DetailText;

        /// <summary>
        /// 购买按钮。
        /// </summary>
        public Button PurchaseButton;

        /// <summary>
        /// 购买按钮文本。
        /// </summary>
        public TextMeshProUGUI PurchaseText;
    }

    /// <summary>
    /// 界面初始化。
    /// </summary>
    /// <param name="userData">用户自定义数据。</param>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        CacheReferences();
        _isReady = BuildEntryViewCache();
        if (!_isReady)
        {
            return;
        }

        _btnClose.onClick.RemoveListener(OnBtnCloseClicked);
        _btnClose.onClick.AddListener(OnBtnCloseClicked);
        RegisterPurchaseListeners();
    }

    /// <summary>
    /// 界面打开时刷新商店列表。
    /// </summary>
    /// <param name="userData">用户自定义数据。</param>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        RefreshShopView();
    }

    /// <summary>
    /// 对象销毁时清理按钮监听。
    /// </summary>
    private void OnDestroy()
    {
        if (_btnClose != null)
        {
            _btnClose.onClick.RemoveListener(OnBtnCloseClicked);
        }

        UnregisterPurchaseListeners();
    }

    /// <summary>
    /// 缓存界面节点引用。
    /// </summary>
    private void CacheReferences()
    {
        if (_btnClose == null)
        {
            Transform closeButton = transform.Find("Frame/Btnclose");
            if (closeButton != null)
            {
                _btnClose = closeButton.GetComponent<Button>();
            }
        }

        if (_eggsRoot == null)
        {
            _eggsRoot = transform.Find("Frame/Eggs") as RectTransform;
        }
    }

    /// <summary>
    /// 构建商店条目缓存。
    /// </summary>
    /// <returns>是否构建成功。</returns>
    private bool BuildEntryViewCache()
    {
        if (_btnClose == null || _eggsRoot == null)
        {
            Log.Error("PurchaseEggsUIForm 初始化失败，关键节点缺失。");
            return false;
        }

        if (_eggsRoot.childCount < ShopEggDisplayCount)
        {
            Log.Error("PurchaseEggsUIForm 初始化失败，Eggs 子节点数为 '{0}'，期望至少 '{1}'。", _eggsRoot.childCount, ShopEggDisplayCount);
            return false;
        }

        for (int i = 0; i < ShopEggDisplayCount; i++)
        {
            Transform entryTransform = _eggsRoot.GetChild(i);
            Transform iconTransform = entryTransform.Find("Egg");
            Transform detailTransform = entryTransform.Find("Text (TMP)");
            Transform buttonTransform = entryTransform.Find("Button");
            if (iconTransform == null || detailTransform == null || buttonTransform == null)
            {
                Log.Error("PurchaseEggsUIForm 初始化失败，条目 '{0}' 结构无效。", entryTransform.name);
                return false;
            }

            Image iconImage = iconTransform.GetComponent<Image>();
            Button purchaseButton = buttonTransform.GetComponent<Button>();
            TextMeshProUGUI detailText = detailTransform.GetComponent<TextMeshProUGUI>();
            TextMeshProUGUI purchaseText = buttonTransform.GetComponentInChildren<TextMeshProUGUI>(true);
            if (iconImage == null || purchaseButton == null || detailText == null || purchaseText == null)
            {
                Log.Error("PurchaseEggsUIForm 初始化失败，条目 '{0}' 组件缺失。", entryTransform.name);
                return false;
            }

            _entryViews[i] = new EggShopEntryView
            {
                Root = entryTransform as RectTransform,
                Icon = iconImage,
                DetailText = detailText,
                PurchaseButton = purchaseButton,
                PurchaseText = purchaseText,
            };
        }

        return true;
    }

    /// <summary>
    /// 注册购买按钮监听。
    /// </summary>
    private void RegisterPurchaseListeners()
    {
        for (int i = 0; i < _entryViews.Length; i++)
        {
            EggShopEntryView entryView = _entryViews[i];
            if (entryView == null || entryView.PurchaseButton == null)
            {
                continue;
            }

            switch (i)
            {
                case 0:
                    entryView.PurchaseButton.onClick.RemoveListener(OnPurchaseEntry0Clicked);
                    entryView.PurchaseButton.onClick.AddListener(OnPurchaseEntry0Clicked);
                    break;

                case 1:
                    entryView.PurchaseButton.onClick.RemoveListener(OnPurchaseEntry1Clicked);
                    entryView.PurchaseButton.onClick.AddListener(OnPurchaseEntry1Clicked);
                    break;

                case 2:
                    entryView.PurchaseButton.onClick.RemoveListener(OnPurchaseEntry2Clicked);
                    entryView.PurchaseButton.onClick.AddListener(OnPurchaseEntry2Clicked);
                    break;

                case 3:
                    entryView.PurchaseButton.onClick.RemoveListener(OnPurchaseEntry3Clicked);
                    entryView.PurchaseButton.onClick.AddListener(OnPurchaseEntry3Clicked);
                    break;

                case 4:
                    entryView.PurchaseButton.onClick.RemoveListener(OnPurchaseEntry4Clicked);
                    entryView.PurchaseButton.onClick.AddListener(OnPurchaseEntry4Clicked);
                    break;
            }
        }
    }

    /// <summary>
    /// 移除购买按钮监听。
    /// </summary>
    private void UnregisterPurchaseListeners()
    {
        for (int i = 0; i < _entryViews.Length; i++)
        {
            EggShopEntryView entryView = _entryViews[i];
            if (entryView == null || entryView.PurchaseButton == null)
            {
                continue;
            }

            switch (i)
            {
                case 0:
                    entryView.PurchaseButton.onClick.RemoveListener(OnPurchaseEntry0Clicked);
                    break;

                case 1:
                    entryView.PurchaseButton.onClick.RemoveListener(OnPurchaseEntry1Clicked);
                    break;

                case 2:
                    entryView.PurchaseButton.onClick.RemoveListener(OnPurchaseEntry2Clicked);
                    break;

                case 3:
                    entryView.PurchaseButton.onClick.RemoveListener(OnPurchaseEntry3Clicked);
                    break;

                case 4:
                    entryView.PurchaseButton.onClick.RemoveListener(OnPurchaseEntry4Clicked);
                    break;
            }
        }
    }

    /// <summary>
    /// 刷新商店蛋列表。
    /// </summary>
    private void RefreshShopView()
    {
        if (!_isReady)
        {
            return;
        }

        for (int i = ShopEggDisplayCount; i < _eggsRoot.childCount; i++)
        {
            Transform extraEntry = _eggsRoot.GetChild(i);
            if (extraEntry != null)
            {
                extraEntry.gameObject.SetActive(false);
            }
        }

        int shopEggCount = CollectShopEggRows();
        for (int i = 0; i < _entryViews.Length; i++)
        {
            EggShopEntryView entryView = _entryViews[i];
            if (entryView == null || entryView.Root == null)
            {
                continue;
            }

            bool hasRow = i < shopEggCount && _shopEggRows[i] != null;
            entryView.Root.gameObject.SetActive(hasRow);
            if (!hasRow)
            {
                continue;
            }

            BindEntryView(entryView, _shopEggRows[i]);
        }
    }

    /// <summary>
    /// 收集商店蛋数据。
    /// </summary>
    /// <returns>实际收集到的商店蛋数量。</returns>
    private int CollectShopEggRows()
    {
        for (int i = 0; i < _shopEggRows.Length; i++)
        {
            _shopEggRows[i] = null;
        }

        if (GameEntry.DataTables == null || !GameEntry.DataTables.IsAvailable<EggDataRow>())
        {
            return 0;
        }

        EggDataRow[] eggRows = GameEntry.DataTables.GetAllDataRows<EggDataRow>();
        int count = 0;
        for (int i = 0; i < eggRows.Length; i++)
        {
            EggDataRow eggDataRow = eggRows[i];
            if (eggDataRow == null || (eggDataRow.AcquireWays & EggDataRow.EggAcquireWay.Shop) == 0)
            {
                continue;
            }

            if (count >= _shopEggRows.Length)
            {
                break;
            }

            int insertIndex = count;
            while (insertIndex > 0 && _shopEggRows[insertIndex - 1] != null && _shopEggRows[insertIndex - 1].Id > eggDataRow.Id)
            {
                _shopEggRows[insertIndex] = _shopEggRows[insertIndex - 1];
                insertIndex--;
            }

            _shopEggRows[insertIndex] = eggDataRow;
            count++;
        }

        return count;
    }

    /// <summary>
    /// 绑定单个商店条目显示。
    /// </summary>
    /// <param name="entryView">条目视图。</param>
    /// <param name="eggDataRow">蛋表数据。</param>
    private static void BindEntryView(EggShopEntryView entryView, EggDataRow eggDataRow)
    {
        if (entryView == null || eggDataRow == null)
        {
            return;
        }

        if (entryView.Icon != null)
        {
            if (GameEntry.GameAssets != null && GameEntry.GameAssets.TryGetEggSprite(eggDataRow.IconPath, out Sprite sprite))
            {
                entryView.Icon.sprite = sprite;
            }
            else
            {
                entryView.Icon.sprite = null;
            }

            entryView.Icon.color = Color.white;
        }

        if (entryView.DetailText != null)
        {
            entryView.DetailText.text = BuildEggDetailText(eggDataRow);
        }

        if (entryView.PurchaseText != null)
        {
            entryView.PurchaseText.text = BuildPurchaseText(eggDataRow.PurchaseGold);
        }
    }

    /// <summary>
    /// 构建蛋详情文本。
    /// </summary>
    /// <param name="eggDataRow">蛋表数据。</param>
    /// <returns>详情文本。</returns>
    private static string BuildEggDetailText(EggDataRow eggDataRow)
    {
        return eggDataRow.Name
            + "\n孵化时间"
            + eggDataRow.HatchSeconds
            + "秒\n普通:"
            + eggDataRow.NormalRate
            + "%\\稀有:"
            + eggDataRow.RareRate
            + "%\\史诗:"
            + eggDataRow.EpicRate
            + "%\\传说:"
            + eggDataRow.LegendaryRate
            + "%\\神话:"
            + eggDataRow.MythicRate
            + "%";
    }

    /// <summary>
    /// 构建购买按钮文本。
    /// </summary>
    /// <param name="purchaseGold">购买所需金币。</param>
    /// <returns>购买按钮文本。</returns>
    private static string BuildPurchaseText(int purchaseGold)
    {
        return "购买\n" + purchaseGold;
    }

    /// <summary>
    /// 关闭按钮点击回调。
    /// </summary>
    private void OnBtnCloseClicked()
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
    /// 购买第 1 个条目。
    /// </summary>
    private void OnPurchaseEntry0Clicked()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        TryPurchaseEntry(0);
    }

    /// <summary>
    /// 购买第 2 个条目。
    /// </summary>
    private void OnPurchaseEntry1Clicked()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        TryPurchaseEntry(1);
    }

    /// <summary>
    /// 购买第 3 个条目。
    /// </summary>
    private void OnPurchaseEntry2Clicked()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        TryPurchaseEntry(2);
    }

    /// <summary>
    /// 购买第 4 个条目。
    /// </summary>
    private void OnPurchaseEntry3Clicked()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        TryPurchaseEntry(3);
    }

    /// <summary>
    /// 购买第 5 个条目。
    /// </summary>
    private void OnPurchaseEntry4Clicked()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        TryPurchaseEntry(4);
    }

    /// <summary>
    /// 尝试购买指定索引的商店蛋。
    /// </summary>
    /// <param name="index">条目索引。</param>
    private void TryPurchaseEntry(int index)
    {
        if (index < 0 || index >= _shopEggRows.Length)
        {
            return;
        }

        EggDataRow eggDataRow = _shopEggRows[index];
        if (eggDataRow == null)
        {
            return;
        }

        if (GameEntry.EggHatch == null)
        {
            Log.Warning("PurchaseEggsUIForm 无法购买蛋，EggHatchComponent 缺失。");
            return;
        }

        if (!GameEntry.EggHatch.TryPurchaseEgg(eggDataRow.Code, out EggHatchComponent.EggPurchaseFailure failure))
        {
            Log.Warning("PurchaseEggsUIForm 购买蛋 '{0}' 失败，原因：'{1}'。", eggDataRow.Code, failure);
            return;
        }

        RefreshShopView();
    }
}
