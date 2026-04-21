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
    /// 流程间传递"本次战斗是否携带道具包"标记的键名。
    /// 由 DailyChallengeUIForm 或 MainUIForm 在进入战斗前设置，
    /// CombatProcedure.OnEnter 读取后传给 CombatUIForm。
    /// </summary>
    public const string HasPropKitDataName = "HasPropKit";

    /// <summary>
    /// 当前战斗界面的序列号。
    /// 用于在 OnLeave 时精确关闭由本流程打开的 CombatUIForm。
    /// </summary>
    private int _combatUIFormId;

    /// <summary>
    /// 进入战斗流程时打开 CombatUIForm。
    /// 读取 HasPropKit 标记并传给 CombatUIForm。
    /// </summary>
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        // 读取道具包状态
        bool hasPropKit = procedureOwner.HasData(HasPropKitDataName)
            && procedureOwner.GetData<VarBoolean>(HasPropKitDataName).Value;

        // 清理标记，避免跨局残留
        if (procedureOwner.HasData(HasPropKitDataName))
        {
            procedureOwner.RemoveData(HasPropKitDataName);
        }

        _combatUIFormId = GameEntry.UI.OpenUIForm(
            UIFormDefine.CombatUIForm, UIFormDefine.MainGroup, hasPropKit);

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
