using UnityGameFramework.Runtime;

/// <summary>
/// 战斗界面 — 复活部分。
/// 负责失败后的复活流程：金币复活、广告复活、复活取消后的失败结算收口。
/// </summary>
public sealed partial class CombatUIForm
{
    /// <summary>
    /// 当前已打开的 ResurgenceUIForm 序列号。
    /// 为 0 表示当前没有活动的复活确认窗实例。
    /// </summary>
    private int _resurgenceUIFormId;

    /// <summary>
    /// 尝试在失败后执行一次金币复活。
    /// 成功时关闭复活窗，清除失败态，并自动执行一次移出道具效果。
    /// </summary>
    /// <returns>true=复活成功；false=复活失败。</returns>
    internal bool TryReviveAfterFailure()
    {
        if (_hasRevivedThisBattle)
        {
            return false;
        }

        EliminateCardController controller = EliminateCardController.Instance;
        if (controller == null)
        {
            return false;
        }

        if (!TryGetResurgenceGoldCost(out int resurgenceGold))
        {
            return false;
        }

        if (GameEntry.Fruits == null || !GameEntry.Fruits.EnsureInitialized())
        {
            return false;
        }

        if (!GameEntry.Fruits.TryConsumeGold(resurgenceGold))
        {
            return false;
        }

        if (!controller.TryRecoverFromFailedStateByShiftOut())
        {
            GameEntry.Fruits.AddGold(resurgenceGold);
            return false;
        }

        controller.ApplyResurgenceComboBonus(10);
        _hasRevivedThisBattle = true;
        CloseResurgenceUIForm();
        return true;
    }

    /// <summary>
    /// 尝试在失败后执行一次广告复活（不看广告，由广告成功回调后调用）。
    /// 跳过金币扣费，直接执行移出道具效果并发放 Combo 奖励。
    /// </summary>
    /// <returns>true=复活成功；false=复活失败。</returns>
    internal bool TryReviveAfterFailureByAd()
    {
        if (_hasRevivedThisBattle)
        {
            return false;
        }

        EliminateCardController controller = EliminateCardController.Instance;
        if (controller == null)
        {
            return false;
        }

        if (!controller.TryRecoverFromFailedStateByShiftOut())
        {
            return false;
        }

        controller.ApplyResurgenceComboBonus(10);
        _hasRevivedThisBattle = true;
        CloseResurgenceUIForm();
        return true;
    }

    /// <summary>
    /// 复活取消、金币不足或复活执行失败时，收口到失败结算窗。
    /// </summary>
    internal void EnterFailureSettlementAfterResurgence()
    {
        CloseResurgenceUIForm();
        OpenVictoryFailUIForm(isVictory: false);
    }

    /// <summary>
    /// 打开复活确认窗。
    /// 若已打开则跳过，避免重复弹出。
    /// </summary>
    /// <returns>true=复活窗已存在或打开成功；false=打开失败。</returns>
    private bool OpenResurgenceUIForm()
    {
        if (GameEntry.UI == null)
        {
            return false;
        }

        if (_resurgenceUIFormId > 0 && GameEntry.UI.HasUIForm(_resurgenceUIFormId))
        {
            return true;
        }

        if (_victoryFailUIFormId > 0 && GameEntry.UI.HasUIForm(_victoryFailUIFormId))
        {
            return false;
        }

        _resurgenceUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.ResurgenceUIForm, UIFormDefine.PopupGroup);
        return _resurgenceUIFormId > 0;
    }

    /// <summary>
    /// 关闭当前记录到的 ResurgenceUIForm。
    /// </summary>
    private void CloseResurgenceUIForm()
    {
        CloseTrackedUIForm(ref _resurgenceUIFormId);
    }

    /// <summary>
    /// 获取复活价格。
    /// 委托给 GameDataTableModule 统一查询方法，避免各 UIForm 各自手写数据表读取逻辑。
    /// </summary>
    /// <param name="resurgenceGold">输出的复活价格。</param>
    /// <returns>true=读取成功；false=读取失败。</returns>
    private static bool TryGetResurgenceGoldCost(out int resurgenceGold)
    {
        if (GameEntry.DataTables == null)
        {
            resurgenceGold = 0;
            return false;
        }

        return GameEntry.DataTables.TryGetResurgenceGoldCost(out resurgenceGold);
    }
}
