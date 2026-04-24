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

    /// <summary>
    /// 流程间传递“正在切换到战斗流程”标记的键名。
    /// DailyChallengeUIForm.OnBtnStartLevel 设置后，
    /// MainProcedure.OnLeave 检测到该标记会保留 MainUIForm 不销毁。
    /// </summary>
    public const string TransitionToCombatDataName = "TransitionToCombat";

    /// <summary>
    /// 流程间传递“正在从战斗流程返回”标记的键名。
    /// IsExitUIForm.OnBtnYes 设置后，
    /// MainProcedure.OnEnter 检测到该标记会恢复每日一关界面。
    /// </summary>
    public const string ReturningFromCombatDataName = "ReturningFromCombat";

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
    /// 若检测到 ReturningFromCombat 标记，说明从战斗流程返回，
    /// MainUIForm 仍然存活，只需恢复每日一关视图即可。
    /// </summary>
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        // 检测是否从战斗流程返回。
        bool returningFromCombat = procedureOwner.HasData(ReturningFromCombatDataName)
            && procedureOwner.GetData<VarInt32>(ReturningFromCombatDataName).Value == 1;

        if (returningFromCombat)
        {
            procedureOwner.RemoveData(ReturningFromCombatDataName);
            // MainUIForm 仍然存活，直接恢复每日一关视图。
            RestoreDailyChallengeOnMainUIForm();
        }
        else
        {
            // 正常进入：先监听打开成功事件，确保主界面真正显示后再关闭加载界面。
            SubscribeOpenSuccessEvent();
            _mainUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.MainUIForm, UIFormDefine.MainGroup);
        }

        base.OnEnter(procedureOwner);
    }

    /// <summary>
    /// 离开主流程时关闭主界面并清理遗留的加载界面。
    /// 若检测到 TransitionToCombat 标记，保留 MainUIForm 不销毁，
    /// 因为战斗期间需要保持棋盘实体与主界面状态。
    /// </summary>
    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        UnsubscribeOpenSuccessEvent();

        // 检测是否正在切换到战斗流程。
        bool transitionToCombat = procedureOwner.HasData(TransitionToCombatDataName)
            && procedureOwner.GetData<VarInt32>(TransitionToCombatDataName).Value == 1;

        if (transitionToCombat)
        {
            procedureOwner.RemoveData(TransitionToCombatDataName);
            // 保留 MainUIForm 不销毁，战斗期间棋盘实体仍需显示。
            // 仅关闭新人礼包界面，避免战斗期间弹出礼包。
            if (_newcomerPackageUIFormId > 0 && GameEntry.UI.HasUIForm(_newcomerPackageUIFormId))
            {
                GameEntry.UI.CloseUIForm(_newcomerPackageUIFormId);
            }

            _newcomerPackageUIFormId = 0;
        }
        else
        {
            // 正常离开：关闭所有界面。
            if (_newcomerPackageUIFormId > 0 && GameEntry.UI.HasUIForm(_newcomerPackageUIFormId))
            {
                GameEntry.UI.CloseUIForm(_newcomerPackageUIFormId);
            }

            _newcomerPackageUIFormId = 0;

            if (_mainUIFormId > 0 && GameEntry.UI.HasUIForm(_mainUIFormId))
            {
                GameEntry.UI.CloseUIForm(_mainUIFormId);
            }
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

        _newcomerPackageUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.NewcomerPackageUIForm, UIFormDefine.PopupGroup);
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

    /// <summary>
    /// 从战斗流程返回后，恢复 MainUIForm 上的每日一关视图。
    /// 导航到 Below 页并打开 DailyChallengeUIForm，同时恢复 BtnUp 显示。
    /// </summary>
    private void RestoreDailyChallengeOnMainUIForm()
    {
        if (_mainUIFormId <= 0 || !GameEntry.UI.HasUIForm(_mainUIFormId))
        {
            return;
        }

        UIForm mainUI = GameEntry.UI.GetUIForm(_mainUIFormId);
        MainUIForm mainUIForm = mainUI != null ? mainUI.Logic as MainUIForm : null;
        if (mainUIForm != null)
        {
            mainUIForm.RestoreDailyChallengeView();
        }
    }
}
