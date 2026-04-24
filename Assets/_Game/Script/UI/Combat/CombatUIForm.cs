using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 战斗界面 — 核心部分。
/// 负责生命周期（OnInit/OnOpen/OnClose/OnDestroy）、得分数字渲染器引用。
/// 其余职责拆分到 partial 文件：
/// - CombatUIForm.Score.cs   分数滚动动画
/// - CombatUIForm.Prop.cs    道具按钮与状态
/// - CombatUIForm.Resurgence.cs 复活流程
/// - CombatUIForm.Popup.cs   弹窗管理（退出/规则/胜负/原地重开）
/// </summary>
public sealed partial class CombatUIForm : UIFormLogic
{
    /// <summary>
    /// 得分数字精灵渲染器。
    /// 挂在 Scores 节点上，用数字图片显示分数，自适应位数居中排列。
    /// Inspector 中拖入绑定；若为 null 则不显示得分。
    /// </summary>
    [SerializeField]
    private ScoreDigitRenderer _scoreDigitRenderer;

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

        // 绑定道具按钮事件
        if (_btnShiftOut != null)
        {
            _btnShiftOut.onClick.RemoveListener(OnBtnShiftOut);
            _btnShiftOut.onClick.AddListener(OnBtnShiftOut);
        }

        if (_btnTake != null)
        {
            _btnTake.onClick.RemoveListener(OnBtnTake);
            _btnTake.onClick.AddListener(OnBtnTake);
        }

        if (_btnRandom != null)
        {
            _btnRandom.onClick.RemoveListener(OnBtnRandom);
            _btnRandom.onClick.AddListener(OnBtnRandom);
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
            controller.OnScoreUpdated += OnScoreChanged;
        }

        // 初始化得分显示
        UpdateScoreText(0);

        // 重置道具状态
        ResetPropState();

        // 解析道具包状态（由 CombatProcedure 传入）
        if (userData is bool hasPropKit)
        {
            _hasPropKit = hasPropKit;
        }

        // 订阅拿取状态变化事件
        if (controller != null)
        {
            controller.OnTakeStateChanged += OnTakeStateChanged;
        }

        // 重置拿取按钮视觉（防止重进游戏时残留拿取状态的缩放）
        OnTakeStateChanged(false);

        // 订阅购买成功事件
        PropPurchaseUIForm.OnPropPurchased += OnPropPurchased;
    }

    /// <summary>
    /// 关闭战斗界面时连带清理 IsExitUIForm / EliminateRulesUIForm / VictoryFailUIForm / ResurgenceUIForm / PropPurchaseUIForm / Punch 动画。
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
            controller.OnScoreUpdated -= OnScoreChanged;
        }

        // 取消订阅拿取状态变化事件
        if (controller != null)
        {
            controller.OnTakeStateChanged -= OnTakeStateChanged;
        }

        // 取消订阅购买成功事件
        PropPurchaseUIForm.OnPropPurchased -= OnPropPurchased;

        // 退出拿取状态
        controller?.ExitTakeState();

        CloseIsExitUIForm();
        CloseEliminateRulesUIForm();
        CloseVictoryFailUIForm();
        CloseResurgenceUIForm();
        ClosePropPurchaseUIForm();
        KillEliminateRulesPunchTween();
        KillScoreAnimation();
        base.OnClose(isShutdown, userData);
    }

    /// <summary>
    /// 销毁时移除按钮监听并清理 Punch 动画。
    /// </summary>
    private void OnDestroy()
    {
        KillEliminateRulesPunchTween();
        KillScoreAnimation();
        if (_btnExit != null)
        {
            _btnExit.onClick.RemoveListener(OnBtnExit);
        }

        if (_btnEliminateRules != null)
        {
            _btnEliminateRules.onClick.RemoveListener(OnBtnEliminateRules);
        }

        if (_btnShiftOut != null)
        {
            _btnShiftOut.onClick.RemoveListener(OnBtnShiftOut);
        }

        if (_btnTake != null)
        {
            _btnTake.onClick.RemoveListener(OnBtnTake);
        }

        if (_btnRandom != null)
        {
            _btnRandom.onClick.RemoveListener(OnBtnRandom);
        }
    }
}
