using GameFramework.Event;
using GameFramework.Fsm;
using GameFramework.Procedure;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// Main流程
/// </summary>
public class MainProcedure : ProcedureBase
{
    // 流程间传递“待关闭界面”序列号的键名。
    private const string PendingCloseUIFormIdDataName = "PendingCloseUIFormId";

    private int _mainUIFormId = 0;
    // 记录当前是否已经订阅打开成功事件，避免重复反订阅报错。
    private bool _isListeningOpenSuccessEvent = false;

    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        // 先监听打开成功事件，确保主界面真正显示后再关闭加载界面。
        SubscribeOpenSuccessEvent();
        _mainUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.MainUIForm, UIFormDefine.MainGroup);

        base.OnEnter(procedureOwner);
    }

    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        UnsubscribeOpenSuccessEvent();

        if (_mainUIFormId > 0 && GameEntry.UI.HasUIForm(_mainUIFormId))
        {
            GameEntry.UI.CloseUIForm(_mainUIFormId);
        }

        ClosePendingLoadUIForm();

        base.OnLeave(procedureOwner, isShutdown);
    }

    /// <summary>
    /// 主界面打开成功后，关闭上一个流程遗留的加载界面。
    /// </summary>
    private void OnOpenUIFormSuccess(object sender, GameEventArgs e)
    {
        OpenUIFormSuccessEventArgs ne = (OpenUIFormSuccessEventArgs)e;
        if (ne.UIForm == null || ne.UIForm.SerialId != _mainUIFormId)
        {
            return;
        }

        UnsubscribeOpenSuccessEvent();
        ClosePendingLoadUIForm();
    }

    /// <summary>
    /// 关闭上一个流程暂存下来的加载界面。
    /// </summary>
    private void ClosePendingLoadUIForm()
    {
        if (procedureOwner == null || !procedureOwner.HasData(PendingCloseUIFormIdDataName))
        {
            return;
        }

        int pendingCloseUIFormId = procedureOwner.GetData<VarInt32>(PendingCloseUIFormIdDataName);
        procedureOwner.RemoveData(PendingCloseUIFormIdDataName);

        if (pendingCloseUIFormId > 0 && GameEntry.UI.HasUIForm(pendingCloseUIFormId))
        {
            GameEntry.UI.CloseUIForm(pendingCloseUIFormId);
        }
    }

    /// <summary>
    /// 订阅主界面打开成功事件。
    /// </summary>
    private void SubscribeOpenSuccessEvent()
    {
        if (_isListeningOpenSuccessEvent)
        {
            return;
        }

        GameEntry.Event.Subscribe(OpenUIFormSuccessEventArgs.EventId, OnOpenUIFormSuccess);
        _isListeningOpenSuccessEvent = true;
    }

    /// <summary>
    /// 取消订阅主界面打开成功事件。
    /// </summary>
    private void UnsubscribeOpenSuccessEvent()
    {
        if (!_isListeningOpenSuccessEvent)
        {
            return;
        }

        GameEntry.Event.Unsubscribe(OpenUIFormSuccessEventArgs.EventId, OnOpenUIFormSuccess);
        _isListeningOpenSuccessEvent = false;
    }
}
