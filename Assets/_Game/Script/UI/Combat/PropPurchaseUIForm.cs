using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 道具购买界面。
/// 职责：
/// 1. 显示道具购买确认弹窗（金币购买）；
/// 2. 购买成功后通过 OnPropPurchased 事件通知 CombatUIForm 增加道具次数；
/// 3. 购买规则：每种道具每局可购买1次，携带道具包时禁止购买；
/// 4. 当前广告按钮仅保留旧 prefab 占位，保持显示但不参与业务。
/// </summary>
public sealed class PropPurchaseUIForm : UIFormLogic
{
    // ───────────── 道具类型枚举 ─────────────

    /// <summary>
    /// 道具类型枚举。
    /// 与 CombatUIForm 的三种道具按钮一一对应。
    /// </summary>
    public enum PropType
    {
        /// <summary>
        /// 移出道具。
        /// </summary>
        Remove = 1,

        /// <summary>
        /// 拿取道具。
        /// </summary>
        Retrieve = 2,

        /// <summary>
        /// 随机道具。
        /// </summary>
        Shuffle = 3,
    }

    // ───────────── Inspector 绑定 ─────────────

    /// <summary>
    /// 确认购买按钮（金币购买）。
    /// </summary>
    [SerializeField]
    private Button _buttonYes;

    /// <summary>
    /// 取消购买按钮。
    /// </summary>
    [SerializeField]
    private Button _buttonNo;

    /// <summary>
    /// 广告获取按钮。
    /// </summary>
    [SerializeField]
    private Button _buttonAdvertising;

    /// <summary>
    /// 半透明遮罩。
    /// 点击遮罩等同于取消。
    /// </summary>
    [SerializeField]
    private Button _buttonBJ;

    /// <summary>
    /// 提示文本。
    /// 显示当前购买的道具名称和价格。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtPrompt;

    // ───────────── 运行时状态 ─────────────

    /// <summary>
    /// 当前购买的道具类型。
    /// OnOpen 时通过 userData 传入。
    /// </summary>
    private PropType _currentPropType;

    /// <summary>
    /// 本次战斗中各道具是否已购买。
    /// 索引对应 PropType 枚举值（1=Remove, 2=Retrieve, 3=Shuffle）。
    /// </summary>
    private static readonly bool[] s_purchasedThisBattle = new bool[4];

    /// <summary>
    /// 道具购买成功事件。
    /// 参数为购买的道具类型。
    /// 由 CombatUIForm 订阅，增加对应道具的使用次数。
    /// </summary>
    public static event System.Action<PropType> OnPropPurchased;

    // ───────────── 静态方法 ─────────────

    /// <summary>
    /// 判断指定道具类型是否可以购买。
    /// 购买规则：每种道具每局可购买1次；携带道具包时禁止购买。
    /// </summary>
    /// <param name="propType">道具类型。</param>
    /// <param name="hasPropKit">本次战斗是否携带道具包。</param>
    /// <returns>true=可以购买；false=不可购买。</returns>
    public static bool CanPurchaseProp(PropType propType, bool hasPropKit)
    {
        // 携带道具包时禁止购买
        if (hasPropKit)
        {
            return false;
        }

        // 每局每种道具只能购买1次
        int index = (int)propType;
        if (index >= 0 && index < s_purchasedThisBattle.Length)
        {
            return !s_purchasedThisBattle[index];
        }

        return false;
    }

    /// <summary>
    /// 重置本次战斗的购买记录。
    /// 由 CombatUIForm.OnOpen 调用，确保每局购买状态干净。
    /// </summary>
    public static void ResetBattlePurchaseState()
    {
        for (int i = 0; i < s_purchasedThisBattle.Length; i++)
        {
            s_purchasedThisBattle[i] = false;
        }
    }

    // ───────────── 生命周期 ─────────────

    /// <summary>
    /// 初始化道具购买界面，绑定按钮事件。
    /// </summary>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);

        if (_buttonYes != null)
        {
            _buttonYes.onClick.RemoveListener(OnBtnYes);
            _buttonYes.onClick.AddListener(OnBtnYes);
        }

        if (_buttonNo != null)
        {
            _buttonNo.onClick.RemoveListener(OnBtnNo);
            _buttonNo.onClick.AddListener(OnBtnNo);
        }

        if (_buttonAdvertising != null)
        {
            _buttonAdvertising.onClick.RemoveListener(OnBtnAdvertising);
        }

        if (_buttonBJ != null)
        {
            _buttonBJ.onClick.RemoveListener(OnBtnNo);
            _buttonBJ.onClick.AddListener(OnBtnNo);
        }
    }

    /// <summary>
    /// 打开时解析 userData 获取道具类型，并更新提示文本。
    /// </summary>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);

        // 解析道具类型
        if (userData is PropType propType)
        {
            _currentPropType = propType;
        }
        else
        {
            _currentPropType = PropType.Remove;
        }

        // 更新提示文本
        UpdatePromptText();
    }

    /// <summary>
    /// 销毁时移除按钮监听。
    /// </summary>
    private void OnDestroy()
    {
        if (_buttonYes != null)
        {
            _buttonYes.onClick.RemoveListener(OnBtnYes);
        }

        if (_buttonNo != null)
        {
            _buttonNo.onClick.RemoveListener(OnBtnNo);
        }

        if (_buttonAdvertising != null)
        {
            _buttonAdvertising.onClick.RemoveListener(OnBtnAdvertising);
        }

        if (_buttonBJ != null)
        {
            _buttonBJ.onClick.RemoveListener(OnBtnNo);
        }
    }

    // ───────────── 按钮回调 ─────────────

    /// <summary>
    /// 确认购买按钮回调（金币购买）。
    /// 扣费成功后才会标记购买并关闭自身。
    /// </summary>
    private void OnBtnYes()
    {
        UIInteractionSound.PlayClick();
        TrySpendGoldAndCompletePurchase();
    }

    /// <summary>
    /// 广告获取按钮回调。
    /// 当前按钮不绑定到 Button.onClick，仅保留旧接口占位。
    /// </summary>
    private void OnBtnAdvertising()
    {
        UIInteractionSound.PlayClick();
        TrySpendGoldAndCompletePurchase();
    }

    /// <summary>
    /// 取消按钮回调。
    /// 仅关闭本窗体。
    /// </summary>
    private void OnBtnNo()
    {
        UIInteractionSound.PlayClick();

        if (UIForm != null && GameEntry.UI != null)
        {
            GameEntry.UI.CloseUIForm(UIForm.SerialId);
        }
    }

    // ───────────── 内部方法 ─────────────

    /// <summary>
    /// 尝试扣除当前道具所需金币，并在成功后完成购买。
    /// </summary>
    private void TrySpendGoldAndCompletePurchase()
    {
        if (!TryGetCurrentPropGoldCost(out int goldCost))
        {
            return;
        }

        if (GameEntry.Fruits == null || !GameEntry.Fruits.EnsureInitialized())
        {
            return;
        }

        if (!GameEntry.Fruits.TryConsumeGold(goldCost))
        {
            return;
        }

        CompletePurchase();
    }

    /// <summary>
    /// 完成购买：标记已购买 → 触发事件 → 关闭自身。
    /// </summary>
    private void CompletePurchase()
    {
        // 标记本次战斗已购买该类型道具
        int index = (int)_currentPropType;
        if (index >= 0 && index < s_purchasedThisBattle.Length)
        {
            s_purchasedThisBattle[index] = true;
        }

        // 通知 CombatUIForm 增加道具次数
        OnPropPurchased?.Invoke(_currentPropType);

        // 关闭自身
        if (UIForm != null && GameEntry.UI != null)
        {
            GameEntry.UI.CloseUIForm(UIForm.SerialId);
        }
    }

    /// <summary>
    /// 根据当前道具类型更新提示文本。
    /// </summary>
    private void UpdatePromptText()
    {
        if (_txtPrompt == null)
        {
            return;
        }

        string propName = GetPropName(_currentPropType);
        if (TryGetCurrentPropGoldCost(out int goldCost))
        {
            _txtPrompt.text = $"是否花费{goldCost}金币购买{propName}？";
            return;
        }

        _txtPrompt.text = $"是否购买{propName}？";
    }

    /// <summary>
    /// 获取当前道具的价格。
    /// </summary>
    /// <param name="goldCost">输出的价格。</param>
    /// <returns>true=读取成功；false=读取失败。</returns>
    private bool TryGetCurrentPropGoldCost(out int goldCost)
    {
        return TryGetPropGoldCost(_currentPropType, out goldCost);
    }

    /// <summary>
    /// 获取指定道具的价格。
    /// </summary>
    /// <param name="propType">目标道具类型。</param>
    /// <param name="goldCost">输出的价格。</param>
    /// <returns>true=读取成功；false=读取失败。</returns>
    internal static bool TryGetPropGoldCost(PropType propType, out int goldCost)
    {
        goldCost = 0;
        if (GameEntry.DataTables == null || !GameEntry.DataTables.IsAvailable<DailyChallengeCostDataRow>())
        {
            return false;
        }

        DailyChallengeCostDataRow costDataRow = GameEntry.DataTables.GetDataRowByCode<DailyChallengeCostDataRow>(DailyChallengeCostDataRow.DefaultCode);
        if (costDataRow == null)
        {
            return false;
        }

        switch (propType)
        {
            case PropType.Remove:
                goldCost = costDataRow.RemoveGold;
                return goldCost > 0;
            case PropType.Retrieve:
                goldCost = costDataRow.RetrieveGold;
                return goldCost > 0;
            case PropType.Shuffle:
                goldCost = costDataRow.ShuffleGold;
                return goldCost > 0;
            default:
                return false;
        }
    }

    /// <summary>
    /// 获取道具名称。
    /// </summary>
    /// <param name="propType">目标道具类型。</param>
    /// <returns>用于 UI 显示的道具名称。</returns>
    private static string GetPropName(PropType propType)
    {
        switch (propType)
        {
            case PropType.Remove:
                return "移出道具";
            case PropType.Retrieve:
                return "拿取道具";
            case PropType.Shuffle:
                return "随机道具";
            default:
                return "道具";
        }
    }
}
