using UnityGameFramework.Runtime;

public partial class MainUIForm
{
    /// <summary>
    /// 运行时覆盖的每日一关关卡标识码（如 "4-2"）。
    /// 为 null 时走数据表 DailyChallengeLevelDataRow 的默认值。
    /// 云端函数下发新关卡时通过 OverrideDailyChallengeLevel 设置此字段。
    /// </summary>
    private string _dailyChallengeLevelCode;

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
    /// 供 DailyChallengeUIForm 调用的"开始关卡预览"入口。
    /// 这里由 MainUIForm 接管生成逻辑，确保关窗后棋盘实体仍然保留在 Below 页。
    /// </summary>
    /// <param name="levelAssetPath">要加载的关卡资源路径；为空时使用 GetCurrentDailyChallengeLevelAssetPath 决定。</param>
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
            ? GetCurrentDailyChallengeLevelAssetPath()
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
    /// 获取当前生效的每日一关关卡标识码。
    /// 优先级：云端覆盖值 > 数据表默认值 > 硬编码兜底 "4-2"。
    /// 这是所有每日关卡路径拼装的唯一入口，外部不应再自行拼装。
    /// </summary>
    /// <returns>关卡标识码，如 "4-2"。</returns>
    public string GetCurrentDailyChallengeLevelCode()
    {
        if (!string.IsNullOrWhiteSpace(_dailyChallengeLevelCode))
        {
            return _dailyChallengeLevelCode;
        }
    
        if (GameEntry.DataTables != null
            && GameEntry.DataTables.TryGetDefaultDailyChallengeLevelCode(out string defaultCode)
            && !string.IsNullOrWhiteSpace(defaultCode))
        {
            return defaultCode;
        }
    
        // 兜底：数据表未就绪时使用项目内已有的 4-2 关卡。
        return "4-2";
    }
    
    /// <summary>
    /// 获取当前生效的每日一关完整资源路径。
    /// 路径格式为 "Configs/Levels/{关卡标识码}"，对应 Resources 下的 .txt 文件。
    /// </summary>
    /// <returns>完整资源路径。</returns>
    public string GetCurrentDailyChallengeLevelAssetPath()
    {
        return "Configs/Levels/" + GetCurrentDailyChallengeLevelCode();
    }
    
    /// <summary>
    /// 云端下发每日关卡时的运行时覆盖入口。
    /// 外部（如云函数回调、远程配置拉取）拿到关卡标识码后调用此方法，
    /// MainUIForm 内部所有每日一关逻辑将立即切换为新关卡。
    /// 若对应关卡 txt 尚未预加载，这里会自动触发补充加载。
    /// </summary>
    /// <param name="levelCode">云端下发的关卡标识码，如 "5-3"。</param>
    public void OverrideDailyChallengeLevel(string levelCode)
    {
        if (string.IsNullOrWhiteSpace(levelCode))
        {
            Log.Warning("MainUIForm.OverrideDailyChallengeLevel 收到空的关卡标识码，忽略本次覆盖。");
            return;
        }
    
        _dailyChallengeLevelCode = levelCode.Trim();
        Log.Info("MainUIForm 每日关卡已切换为: {0}", _dailyChallengeLevelCode);
    
        // 补充加载：如果新关卡文本尚未在预加载缓存中，触发一次按需加载。
        string assetPath = GetCurrentDailyChallengeLevelAssetPath();
        if (GameEntry.GameAssets != null && !GameEntry.GameAssets.HasDailyChallengeLevelText(assetPath))
        {
            GameEntry.GameAssets.LoadDailyChallengeLevelTextOnDemand(assetPath);
        }
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
