using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 退出确认界面。
/// 职责：
/// 1. BtnYes —— 确认退出战斗，设置 ReturningFromCombat 标记后切换回 MainProcedure；
/// 2. BtnNo  —— 取消退出，仅关闭本窗体，战斗继续。
/// </summary>
public sealed class IsExitUIForm : UIFormLogic
{
    /// <summary>
    /// 确认退出按钮。
    /// 点击后结束战斗并返回主流程。
    /// </summary>
    [SerializeField]
    private Button _btnYes;

    /// <summary>
    /// 取消退出按钮。
    /// 点击后仅关闭本窗体，战斗继续。
    /// </summary>
    [SerializeField]
    private Button _btnNo;

    /// <summary>
    /// 初始化退出确认界面，绑定按钮事件。
    /// </summary>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        if (_btnYes != null)
        {
            _btnYes.onClick.RemoveListener(OnBtnYes);
            _btnYes.onClick.AddListener(OnBtnYes);
        }

        if (_btnNo != null)
        {
            _btnNo.onClick.RemoveListener(OnBtnNo);
            _btnNo.onClick.AddListener(OnBtnNo);
        }
    }

    /// <summary>
    /// 销毁时移除按钮监听。
    /// </summary>
    private void OnDestroy()
    {
        if (_btnYes != null)
        {
            _btnYes.onClick.RemoveListener(OnBtnYes);
        }

        if (_btnNo != null)
        {
            _btnNo.onClick.RemoveListener(OnBtnNo);
        }
    }

    /// <summary>
    /// 确认退出按钮点击回调。
    /// 1. 若当前处于消除失败状态（满格单牌过多），则弹出 VictoryFailUIForm(Fail)；
    /// 2. 否则设置 ReturningFromCombat 标记后切换回 MainProcedure。
    /// MainProcedure.OnEnter 检测到该标记后会恢复每日一关界面。
    /// </summary>
    private void OnBtnYes()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        // 先关闭自身，避免流程切换时残留。
        if (UIForm != null && GameEntry.UI != null)
        {
            GameEntry.UI.CloseUIForm(UIForm.SerialId);
        }

        // 每日一关消除失败判定：若已失败，弹出 VictoryFailUIForm(Fail)
        var controller = EliminateCardController.Instance;
        if (controller != null && controller.HasFailedState())
        {
            int failScore = controller.GetCurrentScore();
            var openData = new VictoryFailUIData(false, failScore);
            GameEntry.UI?.OpenUIForm(UIFormDefine.VictoryFailUIForm, UIFormDefine.MainGroup, openData);
            return;
        }

        // 设置 ReturningFromCombat 标记，通知 MainProcedure 恢复每日一关界面。
        GameFramework.Procedure.ProcedureBase currentProcedure = GameEntry.Procedure.CurrentProcedure;
        currentProcedure.procedureOwner.SetData<VarInt32>(MainProcedure.ReturningFromCombatDataName, 1);
        currentProcedure.ChangeState<MainProcedure>(currentProcedure.procedureOwner);
    }

    /// <summary>
    /// 取消退出按钮点击回调。
    /// 仅关闭本窗体，战斗继续。
    /// </summary>
    private void OnBtnNo()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        if (UIForm != null && GameEntry.UI != null)
        {
            GameEntry.UI.CloseUIForm(UIForm.SerialId);
        }
    }
}
