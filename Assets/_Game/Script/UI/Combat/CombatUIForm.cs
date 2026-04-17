using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 战斗界面。
/// 职责：
/// 1. 提供 BtnExit 退出按钮，点击后弹出 IsExitUIForm 确认窗；
/// 2. 提供 BtnEliminateRules 规则按钮，点击后弹出 EliminateRulesUIForm；
/// 3. OnOpen 时自动打开一次 EliminateRulesUIForm；
/// 4. 在 OnClose 时连带关闭 IsExitUIForm / EliminateRulesUIForm，防止战斗流程切换时残留弹窗。
/// </summary>
public sealed class CombatUIForm : UIFormLogic
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

    /// <summary>
    /// 初始化战斗界面，绑定按钮事件。
    /// </summary>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        if (_btnExit != null)
        {
            _btnExit.onClick.RemoveListener(OnBtnExit);
            _btnExit.onClick.AddListener(OnBtnExit);
        }

        if (_btnEliminateRules != null)
        {
            _btnEliminateRules.onClick.RemoveListener(OnBtnEliminateRules);
            _btnEliminateRules.onClick.AddListener(OnBtnEliminateRules);
        }
    }

    /// <summary>
    /// 战斗界面打开时，自动打开一次 EliminateRulesUIForm，并订阅消除胜负事件。
    /// </summary>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        OpenEliminateRulesUIForm();

        // 订阅消除控制器的胜负事件
        var controller = EliminateCardController.Instance;
        if (controller != null)
        {
            controller.OnVictory += OnEliminateVictory;
            controller.OnFail += OnEliminateFail;
        }
    }

    /// <summary>
    /// 关闭战斗界面时连带清理 IsExitUIForm / EliminateRulesUIForm / VictoryFailUIForm / Punch 动画。
    /// 防止战斗流程切换时弹窗残留在屏幕上或动画残留。
    /// </summary>
    protected override void OnClose(bool isShutdown, object userData)
    {
        // 取消订阅消除控制器的胜负事件
        var controller = EliminateCardController.Instance;
        if (controller != null)
        {
            controller.OnVictory -= OnEliminateVictory;
            controller.OnFail -= OnEliminateFail;
        }

        CloseIsExitUIForm();
        CloseEliminateRulesUIForm();
        CloseVictoryFailUIForm();
        KillEliminateRulesPunchTween();
        base.OnClose(isShutdown, userData);
    }

    /// <summary>
    /// 销毁时移除按钮监听并清理 Punch 动画。
    /// </summary>
    private void OnDestroy()
    {
        KillEliminateRulesPunchTween();
        if (_btnExit != null)
        {
            _btnExit.onClick.RemoveListener(OnBtnExit);
        }

        if (_btnEliminateRules != null)
        {
            _btnEliminateRules.onClick.RemoveListener(OnBtnEliminateRules);
        }
    }

    // ───────────── 消除胜负事件处理 ─────────────

    /// <summary>
    /// 消除胜利回调：弹出 VictoryFailUIForm(Victory)。
    /// </summary>
    private void OnEliminateVictory()
    {
        OpenVictoryFailUIForm(isVictory: true);
    }

    /// <summary>
    /// 消除失败回调：弹出 VictoryFailUIForm(Fail)。
    /// </summary>
    private void OnEliminateFail()
    {
        OpenVictoryFailUIForm(isVictory: false);
    }

    /// <summary>
    /// 打开胜利/失败弹窗。
    /// 若已打开则跳过，避免重复弹出。
    /// </summary>
    /// <param name="isVictory">true=胜利；false=失败。</param>
    private void OpenVictoryFailUIForm(bool isVictory)
    {
        if (_victoryFailUIFormId > 0 && GameEntry.UI.HasUIForm(_victoryFailUIFormId))
        {
            return;
        }

        var openData = new VictoryFailUIData(isVictory);
        _victoryFailUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.VictoryFailUIForm, UIFormDefine.MainGroup, openData);
    }

    /// <summary>
    /// 关闭当前记录到的 VictoryFailUIForm。
    /// 由 CombatUIForm.OnClose 调用，确保战斗流程切换时弹窗不会残留。
    /// </summary>
    private void CloseVictoryFailUIForm()
    {
        if (_victoryFailUIFormId <= 0)
        {
            return;
        }

        if (GameEntry.UI != null && GameEntry.UI.HasUIForm(_victoryFailUIFormId))
        {
            GameEntry.UI.CloseUIForm(_victoryFailUIFormId);
        }

        _victoryFailUIFormId = 0;
    }

    /// <summary>
    /// 退出按钮点击回调。
    /// 弹出 IsExitUIForm 供玩家确认是否退出战斗。
    /// </summary>
    private void OnBtnExit()
    {
        if (_isExitUIFormId > 0 && GameEntry.UI.HasUIForm(_isExitUIFormId))
        {
            return;
        }

        _isExitUIFormId = GameEntry.UI.OpenUIForm(UIFormDefine.IsExitUIForm, UIFormDefine.MainGroup);
    }

    /// <summary>
    /// 关闭当前记录到的 IsExitUIForm。
    /// 由 CombatUIForm.OnClose 调用，确保战斗流程切换时退出确认窗不会残留。
    /// </summary>
    private void CloseIsExitUIForm()
    {
        if (_isExitUIFormId <= 0)
        {
            return;
        }

        if (GameEntry.UI != null && GameEntry.UI.HasUIForm(_isExitUIFormId))
        {
            GameEntry.UI.CloseUIForm(_isExitUIFormId);
        }

        _isExitUIFormId = 0;
    }

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
            UIFormDefine.EliminateRulesUIForm, UIFormDefine.MainGroup);

        // EliminateRulesUIForm 在自身 OnOpen 中通过 EnsureCloseAnimationTarget
        // 主动查找 CombatUIForm 并设置关闭动画目标，无需在此处手动设置。
    }

    /// <summary>
    /// 关闭当前记录到的 EliminateRulesUIForm。
    /// 由 CombatUIForm.OnClose 调用，确保战斗流程切换时消除规则窗不会残留。
    /// </summary>
    private void CloseEliminateRulesUIForm()
    {
        if (_eliminateRulesUIFormId <= 0)
        {
            return;
        }

        if (GameEntry.UI != null && GameEntry.UI.HasUIForm(_eliminateRulesUIFormId))
        {
            GameEntry.UI.CloseUIForm(_eliminateRulesUIFormId);
        }

        _eliminateRulesUIFormId = 0;
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
}
