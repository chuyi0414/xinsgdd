using GameFramework.Event;
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
    /// 当前正在等待打开的失败结算窗数据。
    /// 用途：作为本次 VictoryFailUIForm 打开请求的唯一匹配锚点，避免误处理其他模块打开的同名界面。
    /// 初始状态：null，表示当前没有等待中的复活失败结算流程。
    /// </summary>
    private VictoryFailUIData _pendingResurgenceFailureSettlementData;

    /// <summary>
    /// 当前正在等待打开的 VictoryFailUIForm 序列号。
    /// 用途：记录 OpenUIForm 返回的 SerialId，兼容同步打开和异步加载两种路径。
    /// 初始状态：0，表示当前没有待跟踪的结果窗实例。
    /// </summary>
    private int _pendingResurgenceFailureSettlementFormId;

    /// <summary>
    /// 当前是否已经订阅 VictoryFailUIForm 打开成功/失败事件。
    /// 用途：避免重复订阅导致同一次打开回调被处理多次。
    /// 初始状态：false，表示未订阅。
    /// </summary>
    private bool _isListeningResurgenceFailureSettlementOpenEvent;

    /// <summary>
    /// 尝试在失败后执行一次金币复活。
    /// 成功时关闭复活窗，清除失败态，并自动执行一次移出道具效果。
    /// </summary>
    /// <returns>true=复活成功；false=复活失败。</returns>
    internal bool TryReviveAfterFailure()
    {
        if (_pendingResurgenceFailureSettlementData != null)
        {
            return false;
        }

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
        ToastUtility.Show("复活成功，连击×10");
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
        if (_pendingResurgenceFailureSettlementData != null)
        {
            return false;
        }

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
        ToastUtility.Show("复活成功，连击×10");
        CloseResurgenceUIForm();
        return true;
    }

    /// <summary>
    /// 复活取消、金币不足或复活执行失败时，安全收口到失败结算窗。
    /// 核心原则：先确保 VictoryFailUIForm 真正打开，再关闭 ResurgenceUIForm，避免首次加载结果窗时底层 UI 闪现。
    /// </summary>
    internal void EnterFailureSettlementAfterResurgence()
    {
        if (GameEntry.UI == null)
        {
            CloseResurgenceUIForm();
            return;
        }

        if (_pendingResurgenceFailureSettlementData != null)
        {
            return;
        }

        int failScore = 0;
        EliminateCardController controller = EliminateCardController.Instance;
        if (controller != null)
        {
            failScore = controller.GetCurrentScore();
        }

        _pendingResurgenceFailureSettlementData = new VictoryFailUIData(false, failScore);
        SubscribeResurgenceFailureSettlementOpenEvents();

        _pendingResurgenceFailureSettlementFormId = OpenVictoryFailUIForm(false, _pendingResurgenceFailureSettlementData);
        if (_pendingResurgenceFailureSettlementData == null)
        {
            return;
        }

        if (_pendingResurgenceFailureSettlementFormId <= 0)
        {
            ClearPendingResurgenceFailureSettlementState();
            return;
        }

        if (GameEntry.UI.HasUIForm(_pendingResurgenceFailureSettlementFormId))
        {
            ClearPendingResurgenceFailureSettlementState();
            CloseResurgenceUIForm();
        }
    }

    /// <summary>
    /// VictoryFailUIForm 打开成功事件回调。
    /// 只处理当前复活取消流程发起的那一次打开请求。
    /// </summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">GF 事件参数。</param>
    private void OnOpenResurgenceFailureSettlementSuccess(object sender, GameEventArgs e)
    {
        OpenUIFormSuccessEventArgs ne = (OpenUIFormSuccessEventArgs)e;
        if (_pendingResurgenceFailureSettlementData == null
            || ne.UIForm == null
            || !ReferenceEquals(ne.UserData, _pendingResurgenceFailureSettlementData))
        {
            return;
        }

        _pendingResurgenceFailureSettlementFormId = ne.UIForm.SerialId;
        ClearPendingResurgenceFailureSettlementState();
        CloseResurgenceUIForm();
    }

    /// <summary>
    /// VictoryFailUIForm 打开失败事件回调。
    /// 打开失败时保留 ResurgenceUIForm，避免界面掉到底层战斗 UI。
    /// </summary>
    /// <param name="sender">事件发送者。</param>
    /// <param name="e">GF 事件参数。</param>
    private void OnOpenResurgenceFailureSettlementFailure(object sender, GameEventArgs e)
    {
        OpenUIFormFailureEventArgs ne = (OpenUIFormFailureEventArgs)e;
        if (_pendingResurgenceFailureSettlementData == null
            || !ReferenceEquals(ne.UserData, _pendingResurgenceFailureSettlementData))
        {
            return;
        }

        ClearPendingResurgenceFailureSettlementState();
    }

    /// <summary>
    /// 订阅失败结算窗打开成功/失败事件。
    /// </summary>
    private void SubscribeResurgenceFailureSettlementOpenEvents()
    {
        if (_isListeningResurgenceFailureSettlementOpenEvent || GameEntry.Event == null)
        {
            return;
        }

        GameEntry.Event.Subscribe(OpenUIFormSuccessEventArgs.EventId, OnOpenResurgenceFailureSettlementSuccess);
        GameEntry.Event.Subscribe(OpenUIFormFailureEventArgs.EventId, OnOpenResurgenceFailureSettlementFailure);
        _isListeningResurgenceFailureSettlementOpenEvent = true;
    }

    /// <summary>
    /// 取消订阅失败结算窗打开成功/失败事件。
    /// </summary>
    private void UnsubscribeResurgenceFailureSettlementOpenEvents()
    {
        if (!_isListeningResurgenceFailureSettlementOpenEvent || GameEntry.Event == null)
        {
            return;
        }

        GameEntry.Event.Unsubscribe(OpenUIFormSuccessEventArgs.EventId, OnOpenResurgenceFailureSettlementSuccess);
        GameEntry.Event.Unsubscribe(OpenUIFormFailureEventArgs.EventId, OnOpenResurgenceFailureSettlementFailure);
        _isListeningResurgenceFailureSettlementOpenEvent = false;
    }

    /// <summary>
    /// 清理复活取消后等待失败结算窗打开的临时状态。
    /// </summary>
    private void ClearPendingResurgenceFailureSettlementState()
    {
        UnsubscribeResurgenceFailureSettlementOpenEvents();
        _pendingResurgenceFailureSettlementData = null;
        _pendingResurgenceFailureSettlementFormId = 0;
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
