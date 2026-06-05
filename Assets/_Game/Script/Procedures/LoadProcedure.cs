using GameFramework.Procedure;
using ProcedureOwner = GameFramework.Fsm.IFsm<GameFramework.Procedure.IProcedureManager>;
using UnityGameFramework.Runtime;

/// <summary>
/// 加载流程。
/// 仅负责启动数据表与资源预加载，并驱动加载界面按钮状态。
/// 具体的数据表读取、注册与校验逻辑统一下沉到 GameDataTableModule。
/// </summary>
public class LoadProcedure : ProcedureBase
{
    // 流程间传递“待关闭界面”序列号的键名。
    private const string PendingCloseUIFormIdDataName = "PendingCloseUIFormId";

    // 当前加载界面的序列号。
    private int _loadUIFormId;

    // 当前是否已订阅数据表状态事件。
    private bool _isListeningDataTableStateEvents;

    // 当前是否已订阅资源预加载状态事件。
    private bool _isListeningAssetPreloadEvents;

    // 当前是否已经收到加载界面的进入主流程请求。
    // 该标记只在 LoadProcedure.OnUpdate 中消费，避免 UI 按钮回调直接切流程导致 WebGL/微信 PlayerLoop 重入。
    private bool _isEnterMainRequested;

    // 进入主流程请求的延迟帧数。
    // 点击帧先把控制权还给当前 PlayerLoop，下一次流程轮询再真正 ChangeState。
    private int _enterMainDelayFrames;

    /// <summary>
    /// 进入加载流程后打开加载界面并开始读取静态表。
    /// </summary>
    protected override void OnEnter(ProcedureOwner procedureOwner)
    {
        // P0 缓存验证打点：进入加载前的微信文件缓存基线。
        // 真机首启 diskBundles 应该接近 0，二启应该明显大于 0；编辑器/非微信平台 no-op。
        WechatBundleCacheUtility.LogCacheStats("LoadProcedure.OnEnter");
        WechatBundleCacheUtility.LogWasmMemory("LoadProcedure.OnEnter");

        SubscribeDataTableStateEvents();
        SubscribeAssetPreloadEvents();
        _isEnterMainRequested = false;
        _enterMainDelayFrames = 0;
        _loadUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.LoadUIForm, UIFormDefine.BJGroup);
        GameEntry.DataTables?.BeginLoadRequiredDataTables();
        GameEntry.GameAssets?.BeginPreloadRequiredAssets();

        base.OnEnter(procedureOwner);
    }

    /// <summary>
    /// 加载流程轮询。
    /// UI 点击只负责提交请求，真正切到 MainProcedure 必须在这里执行，
    /// 这样 ChangeState/OpenUIForm 不会嵌套在 Unity UI 点击回调或协程恢复栈内。
    /// </summary>
    /// <param name="procedureOwner">当前流程状态机。</param>
    /// <param name="elapseSeconds">逻辑流逝时间。</param>
    /// <param name="realElapseSeconds">真实流逝时间。</param>
    protected override void OnUpdate(ProcedureOwner procedureOwner, float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

        if (!_isEnterMainRequested)
        {
            return;
        }

        if (_enterMainDelayFrames > 0)
        {
            _enterMainDelayFrames--;
            return;
        }

        if (!CanEnterMain())
        {
            RefreshLoadButtonState(false);
            return;
        }

        _isEnterMainRequested = false;
        ChangeState<MainProcedure>(procedureOwner);
    }

    /// <summary>
    /// 离开流程时移除事件监听，并决定是否延迟关闭加载界面。
    /// </summary>
    protected override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
    {
        // P0 缓存验证打点：加载完成后的微信文件缓存增量。
        // 首启 diskBundles 会从 0 增长到本次实际下载数；二启与 OnEnter 数值持平 = 全部命中缓存。
        WechatBundleCacheUtility.LogCacheStats("LoadProcedure.OnLeave");
        WechatBundleCacheUtility.LogWasmMemory("LoadProcedure.OnLeave");

        UnsubscribeDataTableStateEvents();
        UnsubscribeAssetPreloadEvents();
        _isEnterMainRequested = false;
        _enterMainDelayFrames = 0;

        if (isShutdown)
        {
            // 整个流程关闭时直接回收当前界面。
            CloseLoadUIForm();
        }
        else
        {
            // 保持加载界面直到下一个界面真正打开，避免切流程时露出黑底。
            procedureOwner.SetData<VarInt32>(PendingCloseUIFormIdDataName, _loadUIFormId);
            _loadUIFormId = 0;
        }

        base.OnLeave(procedureOwner, isShutdown);
    }

    /// <summary>
    /// 由 LoadUIForm 按钮点击提交“进入主流程”请求。
    /// 该方法不会立刻 ChangeState，只记录请求并禁用按钮，真正切流程交给 OnUpdate。
    /// </summary>
    /// <returns>成功受理请求返回 true；加载条件不足返回 false。</returns>
    public bool RequestEnterMainFromLoadUI()
    {
        if (_isEnterMainRequested)
        {
            return true;
        }

        if (!CanEnterMain())
        {
            RefreshLoadButtonState(false);
            return false;
        }

        _isEnterMainRequested = true;
        _enterMainDelayFrames = 1;
        RefreshLoadButtonState(false);
        return true;
    }

    /// <summary>
    /// 关闭当前加载界面。
    /// </summary>
    private void CloseLoadUIForm()
    {
        if (_loadUIFormId <= 0 || !GameEntry.UI.HasUIForm(_loadUIFormId))
        {
            _loadUIFormId = 0;
            return;
        }

        GameEntry.UI.CloseUIForm(_loadUIFormId);
        _loadUIFormId = 0;
    }

    /// <summary>
    /// 订阅数据表状态变化事件。
    /// </summary>
    private void SubscribeDataTableStateEvents()
    {
        if (_isListeningDataTableStateEvents || GameEntry.DataTables == null)
        {
            return;
        }

        GameEntry.DataTables.LoadStateChanged += OnDataTableLoadStateChanged;
        _isListeningDataTableStateEvents = true;
    }

    /// <summary>
    /// 取消订阅数据表状态变化事件。
    /// </summary>
    private void UnsubscribeDataTableStateEvents()
    {
        if (!_isListeningDataTableStateEvents || GameEntry.DataTables == null)
        {
            return;
        }

        GameEntry.DataTables.LoadStateChanged -= OnDataTableLoadStateChanged;
        _isListeningDataTableStateEvents = false;
    }

    /// <summary>
    /// 订阅资源预加载状态事件。
    /// </summary>
    private void SubscribeAssetPreloadEvents()
    {
        if (_isListeningAssetPreloadEvents || GameEntry.GameAssets == null)
        {
            return;
        }

        GameEntry.GameAssets.PreloadStateChanged += OnAssetPreloadStateChanged;
        _isListeningAssetPreloadEvents = true;
    }

    /// <summary>
    /// 取消订阅资源预加载状态事件。
    /// </summary>
    private void UnsubscribeAssetPreloadEvents()
    {
        if (!_isListeningAssetPreloadEvents || GameEntry.GameAssets == null)
        {
            return;
        }

        GameEntry.GameAssets.PreloadStateChanged -= OnAssetPreloadStateChanged;
        _isListeningAssetPreloadEvents = false;
    }

    /// <summary>
    /// 数据表状态变化回调。
    /// 新表注册完成后，尝试补齐依赖该表的资源预加载并刷新按钮状态。
    /// </summary>
    private void OnDataTableLoadStateChanged()
    {
        GameEntry.GameAssets?.BeginPreloadRequiredAssets();
    }

    /// <summary>
    /// 当前是否已经满足进入主界面的所有条件。
    /// </summary>
    public static bool CanEnterMain()
    {
        bool isBaseReady = GameEntry.DataTables != null
            && GameEntry.DataTables.IsReady
            && GameEntry.GameAssets != null
            && GameEntry.GameAssets.IsReady;

        if (!isBaseReady)
        {
            return false;
        }

        if (GameEntry.CloudSave == null)
        {
            return true;
        }

        if (!GameEntry.CloudSave.HasBegunInitialize)
        {
            GameEntry.CloudSave.BeginInitialize();
        }

        return GameEntry.CloudSave.IsReady;
    }

    /// <summary>
    /// 资源预加载状态变化回调。
    /// </summary>
    private void OnAssetPreloadStateChanged()
    {
    }

    /// <summary>
    /// 刷新加载界面的按钮状态。
    /// </summary>
    private void RefreshLoadButtonState(bool isInteractable)
    {
        if (_loadUIFormId <= 0 || !GameEntry.UI.HasUIForm(_loadUIFormId))
        {
            return;
        }

        UIForm loadUIForm = GameEntry.UI.GetUIForm(_loadUIFormId);
        LoadUIForm loadUIFormLogic = loadUIForm != null ? loadUIForm.Logic as LoadUIForm : null;
        if (loadUIFormLogic == null)
        {
            return;
        }

        loadUIFormLogic.SetLoadButtonInteractable(isInteractable);
    }
}
