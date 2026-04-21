using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
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

        /// <summary>水果名称文本（TxtFruitName 节点上的 TextMeshProUGUI）。</summary>
        public TextMeshProUGUI TxtName;

        /// <summary>水果图标（ImgSG 节点上的 Image）。</summary>
        public Image ImgSG;

        /// <summary>未解锁蒙版节点 GameObject（ImgMR），未解锁时显示，已解锁时隐藏。</summary>
        public GameObject GoImgMR;

        /// <summary>解锁按钮（Button 节点），未解锁时显示，已解锁时隐藏。</summary>
        public Button BtnUnlock;

        /// <summary>解锁按钮上的文本（Button/Text (TMP)），用于显示"解锁(金币数量)"。</summary>
        public TextMeshProUGUI TxtUnlockCost;

        /// <summary>绑定的水果数据行，包含 Code、Name、UnlockGold 等字段。</summary>
        public FruitDataRow DataRow;
    }

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
    /// 所有水果条目的运行时缓存列表。
    /// 预分配 20 个容量，覆盖当前 18 种水果并留有余量。
    /// </summary>
    private readonly List<FruitItemEntry> _fruitEntries = new List<FruitItemEntry>(20);

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

        BuildList();
        RefreshAllItems();
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

        base.OnClose(isShutdown, userData);
    }

    /// <summary>
    /// 界面回收时解绑所有按钮事件，防止泄漏。
    /// </summary>
    protected override void OnRecycle()
    {
        // 解绑所有水果条目的解锁按钮事件
        for (int i = 0; i < _fruitEntries.Count; i++)
        {
            FruitItemEntry entry = _fruitEntries[i];
            if (entry != null && entry.BtnUnlock != null)
            {
                entry.BtnUnlock.onClick.RemoveAllListeners();
            }
        }

        base.OnRecycle();
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
                DataRow = allRows[i]
            };

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

            // 缓存未解锁蒙版节点（ImgMR）
            Transform imgMR = itemTransform.Find("ImgMR");
            if (imgMR != null)
            {
                entry.GoImgMR = imgMR.gameObject;
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

            // 为解锁按钮绑定点击事件
            // 用局部变量捕获当前索引，避免闭包捕获循环变量 i
            if (entry.BtnUnlock != null)
            {
                int capturedIndex = i;
                entry.BtnUnlock.onClick.AddListener(() => OnBtnUnlock(capturedIndex));
            }

            _fruitEntries.Add(entry);
        }

        _isListBuilt = true;
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
    /// - 蒙版可见性（ImgMR）：未解锁显示，已解锁隐藏
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

        // 刷新水果图标 —— 通过 Code 从预加载缓存中取 Sprite
        if (entry.ImgSG != null && GameEntry.GameAssets != null)
        {
            if (GameEntry.GameAssets.TryGetFruitSprite(entry.DataRow.Code, out Sprite sprite))
            {
                entry.ImgSG.sprite = sprite;
            }
        }

        // 未解锁蒙版：未解锁时显示遮罩，已解锁时隐藏
        if (entry.GoImgMR != null)
        {
            entry.GoImgMR.SetActive(!isUnlocked);
        }

        // 解锁按钮：未解锁时显示，已解锁时隐藏
        if (entry.BtnUnlock != null)
        {
            entry.BtnUnlock.gameObject.SetActive(!isUnlocked);
        }

        // 刷新解锁按钮文案：使用 TMP 的 SetText 格式化接口，避免字符串拼接产生 GC
        if (entry.TxtUnlockCost != null && !isUnlocked)
        {
            entry.TxtUnlockCost.SetText("解锁({0})", entry.DataRow.UnlockGold);
        }
    }

    /// <summary>
    /// 解锁按钮点击回调。
    /// 通过 PlayerRuntimeModule.TryPurchaseFruit 原子完成"扣金币 + 解锁水果"，
    /// 成功后立即刷新当前条目的显示状态。
    /// </summary>
    /// <param name="index">被点击条目在列表中的索引。</param>
    private void OnBtnUnlock(int index)
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
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
