using GameFramework.Fsm;
using GameFramework.Procedure;
using UnityGameFramework.Runtime;

/// <summary>
/// 战斗流程。
/// 进入时打开 CombatUIForm，离开时关闭 CombatUIForm。
/// 配合 MainProcedure 的 TransitionToCombat / ReturningFromCombat 标记，
/// 实现战斗期间保留 MainUIForm 不销毁的跨流程 UI 共存。
/// </summary>
public class CombatProcedure : ProcedureBase
{
    /// <summary>
    /// 当前战斗界面的序列号。
    /// 用于在 OnLeave 时精确关闭由本流程打开的 CombatUIForm。
    /// </summary>
    private int _combatUIFormId;

    /// <summary>
    /// 进入战斗流程时打开 CombatUIForm。
    /// </summary>
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        _combatUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.CombatUIForm, UIFormDefine.MainGroup);
        base.OnEnter(procedureOwner);
    }

    /// <summary>
    /// 离开战斗流程时关闭 CombatUIForm。
    /// CombatUIForm.OnClose 会连带清理 IsExitUIForm，此处无需额外处理。
    /// </summary>
    protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
    {
        if (_combatUIFormId > 0 && GameEntry.UI.HasUIForm(_combatUIFormId))
        {
            GameEntry.UI.CloseUIForm(_combatUIFormId);
        }

        _combatUIFormId = 0;
        base.OnLeave(procedureOwner, isShutdown);
    }
}
