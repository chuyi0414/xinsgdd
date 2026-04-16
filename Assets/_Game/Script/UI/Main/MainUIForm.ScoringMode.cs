using UnityGameFramework.Runtime;

public partial class MainUIForm
{
    /// <summary>
    /// 当前已打开的每日一关窗体序列号。
    /// 为 0 表示当前没有活动中的每日一关界面实例。
    /// </summary>
    private int _dailyChallengeUIFormId;

    /// <summary>
    /// 当前是否存在“切页抵达下页后再打开每日一关窗体”的待执行请求。
    /// 这个标记只服务于一次 GoDailyChallenge 触发，不允许跨次切页残留。
    /// </summary>
    private bool _pendingOpenDailyChallengeUIForm;

    /// <summary>
    /// 初始化每日一关相关的运行时状态。
    /// </summary>
    private void InitializeDailyChallengeView()
    {
        _dailyChallengeUIFormId = 0;
        ResetDailyChallengeTransitionState();
    }

    /// <summary>
    /// 主界面打开时重置每日一关过渡态。
    /// </summary>
    private void OpenDailyChallengeView()
    {
        ResetDailyChallengeTransitionState();
    }

    /// <summary>
    /// 主界面关闭时关闭每日一关窗体，并清理过渡态。
    /// </summary>
    private void CloseDailyChallengeView()
    {
        CloseDailyChallengeUIForm();
    }

    /// <summary>
    /// 主界面销毁时清理每日一关相关的缓存状态。
    /// </summary>
    private void DestroyDailyChallengeView()
    {
        ResetDailyChallengeTransitionState();
        _dailyChallengeUIFormId = 0;
    }

    /// <summary>
    /// 在每日一关按钮点击后，根据当前切页状态安排每日一关窗体打开时机。
    /// 如果已经抵达下页则立即打开，否则等切页动画完成后再打开。
    /// </summary>
    private void ScheduleDailyChallengeUIFormOpenAfterSwitch()
    {
        if (_currentPageSlot != MainPageSlot.Below)
        {
            return;
        }

        if (_isSwitching)
        {
            _pendingOpenDailyChallengeUIForm = true;
            return;
        }

        TryOpenDailyChallengeUIForm();
    }

    /// <summary>
    /// 在切页真正抵达下页后执行每日一关窗体打开。
    /// </summary>
    private void HandleDailyChallengePageArrived()
    {
        if (!_pendingOpenDailyChallengeUIForm || _currentPageSlot != MainPageSlot.Below || _isSwitching)
        {
            return;
        }

        _pendingOpenDailyChallengeUIForm = false;
        TryOpenDailyChallengeUIForm();
    }

    /// <summary>
    /// 尝试打开每日一关窗体。
    /// 若当前已经有活动实例，则不重复打开第二份。
    /// </summary>
    private void TryOpenDailyChallengeUIForm()
    {
        if (GameEntry.UI == null)
        {
            Log.Warning("MainUIForm 无法打开每日一关界面，UIComponent 缺失。");
            return;
        }

        if (_dailyChallengeUIFormId > 0 && GameEntry.UI.HasUIForm(_dailyChallengeUIFormId))
        {
            return;
        }

        _dailyChallengeUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.DailyChallengeUIForm, UIFormDefine.MainGroup);
    }

    /// <summary>
    /// 关闭当前记录到的每日一关窗体。
    /// 这里先清理待打开标记，确保 BtnUp 触发时一定先关窗再播返回动画。
    /// </summary>
    private void CloseDailyChallengeUIForm()
    {
        ResetDailyChallengeTransitionState();
        if (_dailyChallengeUIFormId <= 0)
        {
            return;
        }

        if (GameEntry.UI != null && GameEntry.UI.HasUIForm(_dailyChallengeUIFormId))
        {
            GameEntry.UI.CloseUIForm(_dailyChallengeUIFormId);
        }

        _dailyChallengeUIFormId = 0;
    }

    /// <summary>
    /// 清理每日一关切页过程中的待打开状态。
    /// 避免切页中断、布局重排或主界面关闭后残留脏请求。
    /// </summary>
    private void ResetDailyChallengeTransitionState()
    {
        _pendingOpenDailyChallengeUIForm = false;
    }
}
