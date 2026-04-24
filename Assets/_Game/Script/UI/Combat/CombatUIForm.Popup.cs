using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 战斗界面 — 弹窗管理部分。
/// 负责退出确认、消除规则、胜负结算弹窗的打开/关闭/动画，
/// 以及原地重开战斗逻辑。
/// </summary>
public sealed partial class CombatUIForm
{
    /// <summary>
    /// 退出按钮。
    /// 点击后弹出 IsExitUIForm 供玩家确认是否退出战斗。
    /// </summary>
    [SerializeField]
    private Button _btnExit;

    /// <summary>
    /// 消除规则按钮。
    /// 点击后弹出 EliminateRulesUIForm 显示消除规则说明。
    /// </summary>
    [SerializeField]
    private Button _btnEliminateRules;

    /// <summary>
    /// 当前已打开的 IsExitUIForm 序列号。
    /// 为 0 表示当前没有活动的退出确认窗实例。
    /// </summary>
    private int _isExitUIFormId;

    /// <summary>
    /// 当前已打开的 EliminateRulesUIForm 序列号。
    /// 为 0 表示当前没有活动的消除规则窗实例。
    /// </summary>
    private int _eliminateRulesUIFormId;

    /// <summary>
    /// 当前已打开的 VictoryFailUIForm 序列号。
    /// 为 0 表示当前没有活动的胜利/失败弹窗实例。
    /// </summary>
    private int _victoryFailUIFormId;

    /// <summary>
    /// 规则按钮 Punch 回弹动画 Tween 句柄。
    /// EliminateRulesUIForm 关闭动画完成后触发，强调规则按钮位置。
    /// </summary>
    private Tween _btnEliminateRulesPunchTween;

    /// <summary>
    /// 规则按钮 Punch 回弹动画时长（秒）。
    /// </summary>
    private const float BtnEliminateRulesPunchDurationSeconds = 0.45f;

    /// <summary>
    /// 规则按钮 Punch 回弹幅度。
    /// DOPunchScale 的 punch 参数，表示缩放偏移量。
    /// </summary>
    private static readonly Vector3 BtnEliminateRulesPunchScale = new Vector3(0.3f, 0.3f, 0f);

    /// <summary>
    /// 获取规则按钮的 Transform 引用。
    /// 供 EliminateRulesUIForm 在 OnOpen 中查找并设置关闭动画位移目标。
    /// </summary>
    public Transform BtnEliminateRulesTransform => _btnEliminateRules != null
        ? _btnEliminateRules.transform
        : null;

    // ───────────── 消除胜负事件处理 ─────────────

    /// <summary>
    /// 消除胜利回调：弹出 VictoryFailUIForm(Victory)。
    /// </summary>
    private void OnEliminateVictory()
    {
        OpenVictoryFailUIForm(isVictory: true);
    }

    /// <summary>
    /// 消除失败回调。
    /// 本局未复活过时优先弹出 ResurgenceUIForm；
    /// 本局已复活过时，后续再次失败直接进入 VictoryFailUIForm(Fail)。
    /// </summary>
    private void OnEliminateFail()
    {
        if (_hasRevivedThisBattle || !OpenResurgenceUIForm())
        {
            OpenVictoryFailUIForm(isVictory: false);
        }
    }

    // ───────────── 胜负结算弹窗 ─────────────

    /// <summary>
    /// 打开胜利/失败弹窗。
    /// 若已打开则跳过，避免重复弹出。
    /// </summary>
    /// <param name="isVictory">true=胜利；false=失败。</param>
    /// <param name="openData">
    /// 可选的打开数据。
    /// 传入时直接使用外部构造的数据；为 null 时由本方法根据当前分数自动构造。
    /// </param>
    /// <returns>
    /// 当前 VictoryFailUIForm 的序列号。
    /// 若结果窗已存在，则返回已存在实例的序列号；否则返回本次新打开实例的序列号。
    /// </returns>
    internal int OpenVictoryFailUIForm(bool isVictory, VictoryFailUIData openData = null)
    {
        // 若结果窗已经存在，则直接返回现有实例的序列号，避免重复弹出。
        if (_victoryFailUIFormId > 0 && GameEntry.UI.HasUIForm(_victoryFailUIFormId))
        {
            return _victoryFailUIFormId;
        }

        // 若外部没有传入打开数据，则在这里按当前得分自动构造默认打开数据。
        if (openData == null)
        {
            int finalScore = 0;
            var controller = EliminateCardController.Instance;
            if (controller != null)
            {
                finalScore = controller.GetCurrentScore();
            }

            openData = new VictoryFailUIData(isVictory, finalScore);
        }

        // 发起打开结果窗，并记录最新实例的序列号，供 CombatUIForm 后续统一管理与关闭。
        _victoryFailUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.VictoryFailUIForm, UIFormDefine.PopupGroup, openData);
        return _victoryFailUIFormId;
    }

    /// <summary>
    /// 直接复用当前 CombatUIForm 原地重开一局。
    /// 只重建棋盘与战斗态 UI，不重新创建 CombatUIForm 实例，
    /// 从而避免 VictoryFailUIForm 下次再次打开时出现层级被旧/新 CombatUIForm 扰乱的问题。
    /// </summary>
    /// <returns>true=重开成功；false=重开失败，当前结果窗与战斗界面保持原状。</returns>
    internal bool TryRestartCurrentBattle()
    {
        // 当前项目的每日一关棋盘实体由 MainUIForm 持有，
        // 因此即使复用旧 CombatUIForm，也必须委托 MainUIForm 重建棋盘。
        MainUIForm mainUIForm = ResolveMainUIForm();
        if (mainUIForm == null)
        {
            Log.Warning("CombatUIForm 无法原地重开：当前拿不到 MainUIForm。");
            return false;
        }

        // 记录当前这局是否携带道具包。
        // 复用旧 CombatUIForm 时，需要保留"有无道具包"这层配置，
        // 但要把本道具局内的已用次数与购买次数全部重置为新一局状态。
        bool hasPropKit = HasPropKit;

        // 防御式退出拿取状态，避免极端情况下上一局残留按钮缩放或控制器状态。
        EliminateCardController.Instance?.ExitTakeState();

        // 先重建棋盘；MainUIForm / EliminateCardController 会负责把旧实体清掉并生成新的一局。
        // 这里不重新打开 CombatUIForm，确保层级里始终只有同一个 CombatUIForm(Clone)。
        if (!mainUIForm.TryStartDailyChallengePreviewFromUIForm(null))
        {
            Log.Warning("CombatUIForm 原地重开失败：每日一关棋盘重建返回 false。");
            return false;
        }

        // 重建成功后，把当前 CombatUIForm 的局内显示状态重置成"新开一局"的口径。
        _victoryFailUIFormId = 0;
        _resurgenceUIFormId = 0;
        UpdateScoreText(0);
        ResetPropState();
        _hasPropKit = hasPropKit;
        RefreshToolCounts();
        OnTakeStateChanged(false);
        return true;
    }

    /// <summary>
    /// 关闭当前记录到的 VictoryFailUIForm。
    /// 由 CombatUIForm.OnClose 调用，确保战斗流程切换时弹窗不会残留。
    /// </summary>
    private void CloseVictoryFailUIForm()
    {
        CloseTrackedUIForm(ref _victoryFailUIFormId);
    }

    // ───────────── 退出确认弹窗 ─────────────

    /// <summary>
    /// 退出按钮点击回调。
    /// 弹出 IsExitUIForm 供玩家确认是否退出战斗。
    /// </summary>
    private void OnBtnExit()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        if (_isExitUIFormId > 0 && GameEntry.UI.HasUIForm(_isExitUIFormId))
        {
            return;
        }

        _isExitUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.IsExitUIForm, UIFormDefine.PopupGroup);
    }

    /// <summary>
    /// 关闭当前记录到的 IsExitUIForm。
    /// 由 CombatUIForm.OnClose 调用，确保战斗流程切换时退出确认窗不会残留。
    /// </summary>
    private void CloseIsExitUIForm()
    {
        CloseTrackedUIForm(ref _isExitUIFormId);
    }

    // ───────────── 消除规则弹窗 ─────────────

    /// <summary>
    /// 消除规则按钮点击回调。
    /// 弹出 EliminateRulesUIForm 显示消除规则说明。
    /// </summary>
    private void OnBtnEliminateRules()
    {
        OpenEliminateRulesUIForm();
    }

    /// <summary>
    /// 打开 EliminateRulesUIForm。
    /// 若已打开则跳过，避免重复弹出。
    /// 打开后通过 GetUIForm 获取实例，设置关闭动画的目标坐标和回调。
    /// </summary>
    private void OpenEliminateRulesUIForm()
    {
        if (_eliminateRulesUIFormId > 0 && GameEntry.UI.HasUIForm(_eliminateRulesUIFormId))
        {
            return;
        }

        _eliminateRulesUIFormId = GameEntry.UI.OpenUIForm(
            UIFormDefine.EliminateRulesUIForm, UIFormDefine.InfoGroup);

        // EliminateRulesUIForm 在自身 OnOpen 中通过 EnsureCloseAnimationTarget
        // 主动查找 CombatUIForm 并设置关闭动画目标，无需在此处手动设置。
    }

    /// <summary>
    /// 关闭当前记录到的 EliminateRulesUIForm。
    /// 由 CombatUIForm.OnClose 调用，确保战斗流程切换时消除规则窗不会残留。
    /// </summary>
    private void CloseEliminateRulesUIForm()
    {
        CloseTrackedUIForm(ref _eliminateRulesUIFormId);
    }

    /// <summary>
    /// 播放规则按钮 Punch 回弹动画。
    /// 由 EliminateRulesUIForm 关闭动画完成后通过回调触发，
    /// 也可由外部调用。public 以便 EliminateRulesUIForm 通过 CombatUIForm 实例调用。
    /// </summary>
    public void PlayEliminateRulesButtonPunchAnimation()
    {
        if (_btnEliminateRules == null || !_btnEliminateRules.gameObject.activeInHierarchy)
        {
            return;
        }

        KillEliminateRulesPunchTween();

        Transform btnTransform = _btnEliminateRules.transform;
        btnTransform.DOKill(complete: false);
        btnTransform.localScale = Vector3.one;
        _btnEliminateRulesPunchTween = btnTransform
            .DOPunchScale(BtnEliminateRulesPunchScale, BtnEliminateRulesPunchDurationSeconds, vibrato: 6, elasticity: 0.85f)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                _btnEliminateRulesPunchTween = null;
                if (btnTransform != null)
                {
                    btnTransform.localScale = Vector3.one;
                }
            });
    }

    /// <summary>
    /// Kill 规则按钮 Punch 动画并还原按钮缩放。
    /// 在 OnClose / OnDestroy 中调用，防止动画残留。
    /// </summary>
    private void KillEliminateRulesPunchTween()
    {
        if (_btnEliminateRulesPunchTween != null)
        {
            _btnEliminateRulesPunchTween.Kill(false);
            _btnEliminateRulesPunchTween = null;
        }

        if (_btnEliminateRules != null)
        {
            _btnEliminateRules.transform.DOKill(complete: false);
            _btnEliminateRules.transform.localScale = Vector3.one;
        }
    }

    // ───────────── 工具方法 ─────────────

    /// <summary>
    /// 获取当前仍处于保活状态的 MainUIForm 逻辑对象。
    /// 原地重开每日一关时，需要通过它来重建棋盘预览。
    /// </summary>
    /// <returns>当前 MainUIForm 逻辑对象；不存在时返回 null。</returns>
    private static MainUIForm ResolveMainUIForm()
    {
        if (GameEntry.UI == null)
        {
            return null;
        }

        UIForm mainUI = GameEntry.UI.GetUIForm(UIFormDefine.MainUIForm);
        return mainUI != null ? mainUI.Logic as MainUIForm : null;
    }

    // ───────────── UIForm 解析工具 ─────────────

    /// <summary>
    /// 获取当前仍处于保活状态的 CombatUIForm 逻辑对象。
    /// 供 ResurgenceUIForm / VictoryFailUIForm / IsExitUIForm 等子窗体
    /// 统一通过此入口获取宿主，消除各子窗体重复手写 GameEntry.UI 查询逻辑。
    /// </summary>
    /// <returns>当前 CombatUIForm 逻辑对象；不存在时返回 null。</returns>
    public static CombatUIForm ResolveCurrent()
    {
        if (GameEntry.UI == null)
        {
            return null;
        }

        UIForm combatUI = GameEntry.UI.GetUIForm(UIFormDefine.CombatUIForm);
        return combatUI != null ? combatUI.Logic as CombatUIForm : null;
    }

    // ───────────── UIForm 关闭工具 ─────────────

    /// <summary>
    /// 按 SerialId 关闭一个被追踪的 UIForm，并自动将 formId 归零。
    /// 所有 CloseXxxUIForm 方法统一走此入口，消除重复的 null 检查与 HasUIForm 判定。
    /// </summary>
    /// <param name="formId">
    /// 被追踪的 UIForm SerialId 引用。
    /// 调用后会被自动置 0，表示"当前无活动实例"。
    /// </param>
    private static void CloseTrackedUIForm(ref int formId)
    {
        if (formId <= 0)
        {
            return;
        }

        if (GameEntry.UI != null && GameEntry.UI.HasUIForm(formId))
        {
            GameEntry.UI.CloseUIForm(formId);
        }

        formId = 0;
    }
}
