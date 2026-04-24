using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 战斗加载过渡界面。
/// 由 DailyChallengeUIForm.OnBtnStartLevel 在进入战斗前打开，
/// 覆盖流程切换期间的视觉空白，最少显示 1 秒后自动关闭。
/// 关闭时机：累计真实时间 ≥ MinimumDisplayDuration 即自动 CloseSelf，
/// 此时 CombatProcedure 已完成 CombatUIForm 的打开，玩家不会看到黑底。
/// </summary>
public sealed class LoadingUIForm : UIFormLogic
{
    /// <summary>
    /// 最少显示时长（秒）。
    /// 无论实际加载多快，加载界面至少停留此时长，避免闪烁。
    /// </summary>
    private const float MinimumDisplayDuration = 1f;

    /// <summary>
    /// 从 OnOpen 开始累计的真实时间（秒）。
    /// 使用 realElapseSeconds 而非 elapseSeconds，不受 Time.timeScale 影响。
    /// </summary>
    private float _elapsedTime;

    /// <summary>
    /// 是否已触发关闭。
    /// 防止 OnUpdate 在 CloseSelf 之后仍重复调用关闭逻辑。
    /// </summary>
    private bool _closeTriggered;

    /// <summary>
    /// 界面初始化。当前无需缓存引用或绑定事件。
    /// </summary>
    /// <param name="userData">界面打开附加参数。</param>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
    }

    /// <summary>
    /// 界面打开时重置计时状态。
    /// </summary>
    /// <param name="userData">界面打开附加参数。</param>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        _elapsedTime = 0f;
        _closeTriggered = false;
    }

    /// <summary>
    /// 每帧累计真实时间，达到最短显示时长后自动关闭。
    /// 由于 DailyChallengeUIForm.OnBtnStartLevel 中的棋盘生成
    /// 与 CombatProcedure 状态切换均为同步完成，
    /// 1 秒后 CombatUIForm 必然已经打开，关闭加载界面不会露出黑底。
    /// </summary>
    /// <param name="elapseSeconds">逻辑流逝时间（秒）。</param>
    /// <param name="realElapseSeconds">真实流逝时间（秒）。</param>
    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);

        if (_closeTriggered)
        {
            return;
        }

        _elapsedTime += realElapseSeconds;
        if (_elapsedTime >= MinimumDisplayDuration)
        {
            _closeTriggered = true;
            CloseSelf();
        }
    }

    /// <summary>
    /// 关闭自身。
    /// 通过 GF UI 模块的 SerialId 精确关闭，避免误关其他界面。
    /// </summary>
    private void CloseSelf()
    {
        if (UIForm == null || GameEntry.UI == null)
        {
            return;
        }

        GameEntry.UI.CloseUIForm(UIForm.SerialId);
    }
}
