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

    /// <summary>
    /// 进入加载流程后打开加载界面并开始读取静态表。
    /// </summary>
    protected override void OnEnter(ProcedureOwner procedureOwner)
    {
        SubscribeDataTableStateEvents();
        SubscribeAssetPreloadEvents();
        _loadUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.LoadUIForm, UIFormDefine.MainGroup);
        RefreshLoadButtonState(CanEnterMain());
        GameEntry.DataTables?.BeginLoadRequiredDataTables();
        GameEntry.GameAssets?.BeginPreloadRequiredAssets();

        base.OnEnter(procedureOwner);
    }

    /// <summary>
    /// 离开流程时移除事件监听，并决定是否延迟关闭加载界面。
    /// </summary>
    protected override void OnLeave(ProcedureOwner procedureOwner, bool isShutdown)
    {
        UnsubscribeDataTableStateEvents();
        UnsubscribeAssetPreloadEvents();

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
        RefreshLoadButtonState(CanEnterMain());
    }

    /// <summary>
    /// 当前是否已经满足进入主界面的所有条件。
    /// </summary>
    private static bool CanEnterMain()
    {
        return GameEntry.DataTables != null
            && GameEntry.DataTables.IsReady
            && GameEntry.GameAssets != null
            && GameEntry.GameAssets.IsReady;
    }

    /// <summary>
    /// 资源预加载状态变化回调。
    /// </summary>
    private void OnAssetPreloadStateChanged()
    {
        RefreshLoadButtonState(CanEnterMain());
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
