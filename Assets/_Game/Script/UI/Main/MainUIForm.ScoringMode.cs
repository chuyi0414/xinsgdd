using UnityGameFramework.Runtime;

public partial class MainUIForm
{
    /// <summary>
    /// 每日一关本地预览使用的默认关卡资源路径。
    /// 当前先固定到 bbl1，后续接云端后再由外部下发实际关卡路径。
    /// </summary>
    private const string DailyChallengeDefaultLevelAssetPath = "Configs/Levels/bbl1";

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
    /// 消除卡片控制器。
    /// 生成出来的消除卡片实体应该由 MainUIForm 持有，
    /// 不能挂在 DailyChallengeUIForm 自己身上，否则点击开始后关窗会把棋盘一起清掉。
    /// </summary>
    private EliminateCardController _eliminateCardController;

    /// <summary>
    /// 初始化每日一关相关的运行时状态。
    /// </summary>
    private void InitializeDailyChallengeView()
    {
        _dailyChallengeUIFormId = 0;
        ResetDailyChallengeTransitionState();
        _eliminateCardController = new EliminateCardController();
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
        ClearDailyChallengeBoardPreview();
    }

    /// <summary>
    /// 主界面销毁时清理每日一关相关的缓存状态。
    /// </summary>
    private void DestroyDailyChallengeView()
    {
        ResetDailyChallengeTransitionState();
        ClearDailyChallengeBoardPreview();
        _dailyChallengeUIFormId = 0;
        _eliminateCardController = null;
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
    /// 供 DailyChallengeUIForm 调用的“开始关卡预览”入口。
    /// 这里由 MainUIForm 接管生成逻辑，确保关窗后棋盘实体仍然保留在 Below 页。
    /// </summary>
    /// <param name="levelAssetPath">要加载的关卡资源路径；为空时回退到默认测试关卡。</param>
    /// <returns>是否成功开始生成棋盘。</returns>
    public bool TryStartDailyChallengePreviewFromUIForm(string levelAssetPath)
    {
        if (_currentPageSlot != MainPageSlot.Below)
        {
            Log.Warning("MainUIForm can not start daily challenge preview because current page is not Below.");
            return false;
        }

        if (_eliminateCardController == null)
        {
            _eliminateCardController = new EliminateCardController();
        }

        string targetLevelAssetPath = string.IsNullOrWhiteSpace(levelAssetPath)
            ? DailyChallengeDefaultLevelAssetPath
            : levelAssetPath.Trim();
        EliminateCardPreviewResult result = _eliminateCardController.RebuildPreview(targetLevelAssetPath);
        if (!result.IsSuccess)
        {
            Log.Warning("MainUIForm daily challenge preview failed: {0}", result.ErrorMessage);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 清理每日一关当前预览棋盘。
    /// 返回中页、关闭主界面或重建棋盘前都走同一个收口入口。
    /// </summary>
    private void ClearDailyChallengeBoardPreview()
    {
        _eliminateCardController?.Dispose();
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
