using DG.Tweening;
using UnityEngine;

/// <summary>
/// 战斗界面 — 分数滚动部分。
/// 负责分数显示、自适应步长滚动动画、Tween 生命周期管理。
/// </summary>
public sealed partial class CombatUIForm
{
    /// <summary>
    /// 分数滚动刷新时间间隔（秒）。
    /// 参考项目按 0.1 秒一个节拍推进一次分数，这里保持同样手感。
    /// </summary>
    private const float ScoreTickIntervalSeconds = 0.1f;

    /// <summary>
    /// 分数滚动的自适应步长档位。
    /// 会从大到小挑选一个不超过当前分差的最大步长，兼顾大分差追赶速度与小分差收敛精度。
    /// </summary>
    private static readonly int[] ScoreStepLadder = { 100000, 10000, 1000, 100, 10, 1 };

    /// <summary>
    /// 当前分数滚动用的延迟 Tween 句柄。
    /// 只在需要继续追分时创建；界面关闭、销毁或直接同步分数时统一 Kill。
    /// </summary>
    private Tween _scoreTickTween;

    /// <summary>
    /// 当前界面已经显示出来的分数。
    /// 作为滚动动画的起点，每次推进都会向目标值逼近。
    /// </summary>
    private int _displayedScore;

    /// <summary>
    /// 当前需要追赶到的目标分数。
    /// 由 EliminateCardController.OnScoreUpdated 驱动刷新。
    /// </summary>
    private int _targetScore;

    /// <summary>
    /// 得分变化回调：触发滚动分数刷新。
    /// </summary>
    /// <param name="score">当前累计得分。</param>
    private void OnScoreChanged(int score)
    {
        UpdateScoreTextAnimated(score);
    }

    /// <summary>
    /// 更新得分显示。
    /// 通过 ScoreDigitRenderer 用精灵图片渲染分数数字。
    /// </summary>
    /// <param name="score">当前得分。</param>
    private void UpdateScoreText(int score)
    {
        // 立即同步时必须先终止旧滚分，避免旧 Tween 在下一拍又把显示值改回去。
        KillScoreAnimation();
        _targetScore = Mathf.Max(0, score);
        _displayedScore = _targetScore;
        ApplyScoreText(_displayedScore);
    }

    /// <summary>
    /// 按参考项目手感执行滚动分数刷新。
    /// 分数上升时按固定节拍 + 自适应步长追赶；分数回退或界面不可见时直接同步。
    /// </summary>
    /// <param name="score">本次需要显示到的目标分数。</param>
    private void UpdateScoreTextAnimated(int score)
    {
        int safeScore = Mathf.Max(0, score);

        // 每次事件到来都先覆盖目标值；若滚分正在执行，后续会自动追到新目标。
        _targetScore = safeScore;

        // 1. UI 不可见时不做后台滚分；
        // 2. 分数下降/重置时直接同步，避免出现倒着滚的表现。
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy || safeScore <= _displayedScore)
        {
            UpdateScoreText(safeScore);
            return;
        }

        // 当前没有滚分任务时，立刻推进第一拍；
        // 若已有任务在跑，本次只更新目标值，等下一拍自然追上即可。
        if (_scoreTickTween == null || !_scoreTickTween.IsActive())
        {
            AdvanceScoreAnimation();
        }
    }

    /// <summary>
    /// 推进一拍分数滚动动画。
    /// 会根据当前分差选择合适步长，写入显示层后决定是否继续排下一拍。
    /// </summary>
    private void AdvanceScoreAnimation()
    {
        int diff = _targetScore - _displayedScore;
        int step = GetAdaptiveScoreStep(diff);
        if (step <= 0)
        {
            UpdateScoreText(_targetScore);
            return;
        }

        // 这里不允许越过目标值，必须用 Min 把最终显示值钳到目标分。
        _displayedScore = Mathf.Min(_targetScore, _displayedScore + step);
        ApplyScoreText(_displayedScore);

        // 只有还没追平时才继续排下一拍，避免无意义空转。
        if (_displayedScore < _targetScore)
        {
            ScheduleScoreAnimationTick();
        }
    }

    /// <summary>
    /// 安排下一拍分数滚动。
    /// 使用 DOVirtual.DelayedCall 而不是 Update/Coroutine，减少常驻轮询代码。
    /// </summary>
    private void ScheduleScoreAnimationTick()
    {
        // 防御式先 Kill，保证任意时刻最多只存在一个有效的分数滚动定时器。
        KillScoreAnimation();
        _scoreTickTween = DOVirtual.DelayedCall(ScoreTickIntervalSeconds, OnScoreAnimationTick).SetUpdate(true);
    }

    /// <summary>
    /// 分数滚动下一拍的回调入口。
    /// 延迟 Tween 到点后会进入这里继续推进追分。
    /// </summary>
    private void OnScoreAnimationTick()
    {
        // 先清空句柄，表示这一拍已经执行完；
        // 若还需要继续，会在 AdvanceScoreAnimation 内重新排下一拍。
        _scoreTickTween = null;

        // 若此时界面已经不可见，直接落到目标值并收口，避免隐藏状态残留 Tween。
        if (!isActiveAndEnabled || !gameObject.activeInHierarchy)
        {
            UpdateScoreText(_targetScore);
            return;
        }

        AdvanceScoreAnimation();
    }

    /// <summary>
    /// 根据当前分差选择一档滚分步长。
    /// 规则：返回不超过 diff 的最大档位；若 diff 非正则返回 0。
    /// </summary>
    /// <param name="diff">目标分与当前显示分之间的差值。</param>
    /// <returns>本拍应增加的分数步长。</returns>
    private static int GetAdaptiveScoreStep(int diff)
    {
        if (diff <= 0)
        {
            return 0;
        }

        // 从大到小找第一个可用档位，确保大分差时追得快，小分差时落点稳。
        for (int i = 0; i < ScoreStepLadder.Length; i++)
        {
            int step = ScoreStepLadder[i];
            if (step <= diff)
            {
                return step;
            }
        }

        return 1;
    }

    /// <summary>
    /// 将指定分数真正写入 Scores 显示层。
    /// 这里只负责 UI 呈现，不修改任何战斗层的真实记分数据。
    /// </summary>
    /// <param name="score">当前要显示的分数。</param>
    private void ApplyScoreText(int score)
    {
        if (_scoreDigitRenderer != null)
        {
            _scoreDigitRenderer.SetScore(score);
        }
    }

    /// <summary>
    /// 终止当前分数滚动 Tween。
    /// 统一供直接同步分数、界面关闭、界面销毁等收口场景调用。
    /// </summary>
    private void KillScoreAnimation()
    {
        if (_scoreTickTween != null)
        {
            _scoreTickTween.Kill(false);
            _scoreTickTween = null;
        }
    }
}
