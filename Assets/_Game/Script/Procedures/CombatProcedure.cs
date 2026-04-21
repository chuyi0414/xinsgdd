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
    /// 流程间传递“本次是否为再来一局重开”标记的键名。
    /// 由 VictoryFailUIForm.BtnRechallenge 设置；
    /// CombatProcedure.OnEnter 读取后先重建每日一关棋盘，再打开新一局 CombatUIForm。
    /// </summary>
    public const string RechallengeCombatDataName = "RechallengeCombat";

    /// <summary>
    /// 当前战斗界面的序列号。
    /// 用于在 OnLeave 时精确关闭由本流程打开的 CombatUIForm。
    /// </summary>
    private int _combatUIFormId;

    /// <summary>
    /// 进入战斗流程时打开 CombatUIForm。
    /// 读取 HasPropKit 标记并传给 CombatUIForm；
    /// 若检测到 RechallengeCombat 标记，则先重建每日一关棋盘，再进入新一局战斗。
    /// </summary>
    protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
    {
        // 读取道具包状态
        bool hasPropKit = procedureOwner.HasData(HasPropKitDataName)
            && procedureOwner.GetData<VarBoolean>(HasPropKitDataName).Value;

        // 读取“再来一局”标记。
        // 若为 true，则说明这是从 VictoryFailUIForm 触发的原地重开，
        // 本次进入战斗流程前必须先重建每日一关棋盘。
        bool isRechallenge = procedureOwner.HasData(RechallengeCombatDataName)
            && procedureOwner.GetData<VarBoolean>(RechallengeCombatDataName).Value;

        // 清理标记，避免跨局残留
        if (procedureOwner.HasData(HasPropKitDataName))
        {
            procedureOwner.RemoveData(HasPropKitDataName);
        }

        if (procedureOwner.HasData(RechallengeCombatDataName))
        {
            procedureOwner.RemoveData(RechallengeCombatDataName);
        }

        // 若本次为“再来一局”，则先重建棋盘预览。
        // ⚠️ 避坑：不能在 VictoryFailUIForm 按钮回调里直接先重建，
        // 因为 RebuildPreview 失败时会先清旧棋盘，可能把当前战斗打坏。
        // 放到 CombatProcedure.OnEnter 里做，失败时可以直接安全回退到 MainProcedure。
        if (isRechallenge && !TryPrepareRechallengePreview(procedureOwner))
        {
            return;
        }

        // 打开新一局战斗界面，并把道具包状态传给 CombatUIForm。
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

    /// <summary>
    /// 为“再来一局”准备新的每日一关棋盘预览。
    /// 若准备失败，则回退到 MainProcedure 并恢复每日一关界面，避免停留在无效战斗态。
    /// </summary>
    /// <param name="procedureOwner">当前流程持有者。</param>
    /// <returns>true=预览准备成功，可以继续打开 CombatUIForm；false=已执行回退，不应继续打开战斗界面。</returns>
    private bool TryPrepareRechallengePreview(IFsm<IProcedureManager> procedureOwner)
    {
        // 取到仍然保活中的 MainUIForm。
        // 当前项目的每日一关棋盘实体由 MainUIForm 持有，因此重开必须委托它重建棋盘。
        UIForm mainUI = GameEntry.UI != null ? GameEntry.UI.GetUIForm(UIFormDefine.MainUIForm) : null;
        MainUIForm mainUIForm = mainUI != null ? mainUI.Logic as MainUIForm : null;
        if (mainUIForm == null)
        {
            Log.Warning("CombatProcedure 再来一局失败：当前拿不到 MainUIForm，无法重建每日一关棋盘。");
            ReturnToMainProcedureAfterRechallengeFailure(procedureOwner);
            return false;
        }

        // 传 null 让 MainUIForm 回退到现有每日一关默认关卡路径，
        // 与 DailyChallengeUIForm.OnBtnStartLevel 当前的关卡入口口径保持一致。
        if (!mainUIForm.TryStartDailyChallengePreviewFromUIForm(null))
        {
            Log.Warning("CombatProcedure 再来一局失败：每日一关棋盘重建返回 false，已回退到 MainProcedure。");
            ReturnToMainProcedureAfterRechallengeFailure(procedureOwner);
            return false;
        }

        return true;
    }

    /// <summary>
    /// 当“再来一局”准备失败时，安全回退到 MainProcedure。
    /// 通过 ReturningFromCombat 标记让 MainProcedure 恢复每日一关界面，避免用户停留在坏掉的战斗流程里。
    /// </summary>
    /// <param name="procedureOwner">当前流程持有者。</param>
    private void ReturnToMainProcedureAfterRechallengeFailure(IFsm<IProcedureManager> procedureOwner)
    {
        // 标记为“从战斗返回”，让 MainProcedure.OnEnter 直接恢复每日一关视图。
        procedureOwner.SetData<VarInt32>(MainProcedure.ReturningFromCombatDataName, 1);

        // 直接回退到主流程。
        // CombatProcedure.OnLeave 会关闭当前旧战斗界面，MainProcedure.OnEnter 会负责恢复每日一关 UI。
        ChangeState<MainProcedure>(procedureOwner);
    }
}
