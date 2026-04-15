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

    // 本次运行内是否已经展示过新人礼包。
    private static bool s_hasOpenedNewcomerPackageThisSession;

    // 当前主界面的序列号。
    private int _mainUIFormId = 0;

    // 当前新人礼包界面的序列号。
    private int _newcomerPackageUIFormId = 0;

    // 记录当前是否已经订阅打开成功事件，避免重复反订阅报错。
    private bool _isListeningOpenSuccessEvent = false;

    /// <summary>
    /// 进入主流程时打开主界面。
    /// </summary>
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        // 先监听打开成功事件，确保主界面真正显示后再关闭加载界面。
        SubscribeOpenSuccessEvent();
        _mainUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.MainUIForm, UIFormDefine.MainGroup);

        base.OnEnter(procedureOwner);
    }

    /// <summary>
    /// 离开主流程时关闭主界面并清理遗留的加载界面。
    /// </summary>
    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        UnsubscribeOpenSuccessEvent();

        if (_newcomerPackageUIFormId > 0 && GameEntry.UI.HasUIForm(_newcomerPackageUIFormId))
        {
            GameEntry.UI.CloseUIForm(_newcomerPackageUIFormId);
        }

        _newcomerPackageUIFormId = 0;

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
        TryOpenNewcomerPackageUI();
    }

    /// <summary>
    /// 尝试在本次运行首次进入主界面后打开新人礼包。
    /// </summary>
    private void TryOpenNewcomerPackageUI()
    {
        if (s_hasOpenedNewcomerPackageThisSession || GameEntry.UI == null)
        {
            return;
        }

        _newcomerPackageUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.NewcomerPackageUIForm, UIFormDefine.MainGroup);
        if (_newcomerPackageUIFormId > 0)
        {
            s_hasOpenedNewcomerPackageThisSession = true;
        }
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
