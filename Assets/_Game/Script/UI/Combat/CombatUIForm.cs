using DG.Tweening;
using TMPro;
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
    /// 得分数字精灵渲染器。
    /// 挂在 Scores 节点上，用数字图片显示分数，自适应位数居中排列。
    /// Inspector 中拖入绑定；若为 null 则不显示得分。
    /// </summary>
    [SerializeField]
    private ScoreDigitRenderer _scoreDigitRenderer;

    // ───────────── 道具按钮 ─────────────

    /// <summary>
    /// 移出道具按钮（BtnProp）。
    /// 点击后消耗移出道具，将等待区前3张卡片移出并回收。
    /// </summary>
    [SerializeField]
    private Button _btnShiftOut;

    /// <summary>
    /// 移出道具剩余数文本。
    /// 显示当前可用的移出道具次数。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtShiftOutCount;

    /// <summary>
    /// 拿取道具按钮（BtnProp (1)）。
    /// 点击后消耗拿取道具，进入拿取状态。
    /// </summary>
    [SerializeField]
    private Button _btnTake;

    /// <summary>
    /// 拿取道具剩余数文本。
    /// 显示当前可用的拿取道具次数。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtTakeCount;

    /// <summary>
    /// 随机道具按钮（BtnProp (2)）。
    /// 点击后消耗随机道具，打乱棋盘上所有未遮挡卡片的类型。
    /// </summary>
    [SerializeField]
    private Button _btnRandom;

    /// <summary>
    /// 随机道具剩余数文本。
    /// 显示当前可用的随机道具次数。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtRandomCount;

    // ───────────── 道具状态 ─────────────

    /// <summary>
    /// 本次战斗是否携带道具包。
    /// 道具包提供每种道具各1次免费使用；携带时禁止购买。
    /// </summary>
    private bool _hasPropKit;

    /// <summary>
    /// 移出道具是否已使用（道具包内）。
    /// </summary>
    private bool _removeToolUsed;

    /// <summary>
    /// 拿取道具是否已使用（道具包内）。
    /// </summary>
    private bool _pickToolUsed;

    /// <summary>
    /// 随机道具是否已使用（道具包内）。
    /// </summary>
    private bool _randomToolUsed;

    /// <summary>
    /// 购买的移出道具次数。
    /// </summary>
    private int _purchasedRemoveCount;

    /// <summary>
    /// 购买的拿取道具次数。
    /// </summary>
    private int _purchasedRetrieveCount;

    /// <summary>
    /// 购买的随机道具次数。
    /// </summary>
    private int _purchasedShuffleCount;

    /// <summary>
    /// 当前已打开的 PropPurchaseUIForm 序列号。
    /// 为 0 表示当前没有活动的购买窗实例。
    /// </summary>
    private int _propPurchaseUIFormId;

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
        ClosePropPurchaseUIForm();
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

    // ───────────── 得分/连击事件处理 ─────────────

    /// <summary>
    /// 得分变化回调：刷新得分文本。
    /// </summary>
    /// <param name="score">当前累计得分。</param>
    private void OnScoreChanged(int score)
    {
        UpdateScoreText(score);
    }

    /// <summary>
    /// 更新得分显示。
    /// 通过 ScoreDigitRenderer 用精灵图片渲染分数数字。
    /// </summary>
    /// <param name="score">当前得分。</param>
    private void UpdateScoreText(int score)
    {
        if (_scoreDigitRenderer != null)
        {
            _scoreDigitRenderer.SetScore(score);
        }
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

        // 获取当前得分
        int finalScore = 0;
        var controller = EliminateCardController.Instance;
        if (controller != null)
        {
            finalScore = controller.GetCurrentScore();
        }

        var openData = new VictoryFailUIData(isVictory, finalScore);
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
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
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

    // ───────────── 道具按钮回调 ─────────────

    /// <summary>
    /// 移出道具按钮点击回调。
    /// 优先级：道具包免费次数 → 购买次数 → 弹出购买UI。
    /// </summary>
    private void OnBtnShiftOut()
    {
        UIInteractionSound.PlayClick();

        var controller = EliminateCardController.Instance;
        if (controller == null)
        {
            return;
        }

        // 1. 道具包免费次数
        if (_hasPropKit && !_removeToolUsed)
        {
            if (controller.PropShiftOut())
            {
                _removeToolUsed = true;
                RefreshToolCounts();
            }
            return;
        }

        // 2. 购买次数
        if (_purchasedRemoveCount > 0)
        {
            if (controller.PropShiftOut())
            {
                _purchasedRemoveCount--;
                RefreshToolCounts();
            }
            return;
        }

        // 3. 弹出购买UI
        OpenPropPurchaseUIForm(PropPurchaseUIForm.PropType.Remove);
    }

    /// <summary>
    /// 拿取道具按钮点击回调。
    /// 优先级：道具包免费次数 → 购买次数 → 弹出购买UI。
    /// 若已在拿取状态，点击则退出拿取状态。
    /// </summary>
    private void OnBtnTake()
    {
        UIInteractionSound.PlayClick();

        var controller = EliminateCardController.Instance;
        if (controller == null)
        {
            return;
        }

        // 若已在拿取状态，点击则退出
        if (controller.IsTakeState)
        {
            controller.ExitTakeState();
            return;
        }

        // 1. 道具包免费次数
        if (_hasPropKit && !_pickToolUsed)
        {
            if (controller.PropEnterTakeState())
            {
                _pickToolUsed = true;
                RefreshToolCounts();
            }
            return;
        }

        // 2. 购买次数
        if (_purchasedRetrieveCount > 0)
        {
            if (controller.PropEnterTakeState())
            {
                _purchasedRetrieveCount--;
                RefreshToolCounts();
            }
            return;
        }

        // 3. 弹出购买UI
        OpenPropPurchaseUIForm(PropPurchaseUIForm.PropType.Retrieve);
    }

    /// <summary>
    /// 随机道具按钮点击回调。
    /// 优先级：道具包免费次数 → 购买次数 → 弹出购买UI。
    /// </summary>
    private void OnBtnRandom()
    {
        UIInteractionSound.PlayClick();

        var controller = EliminateCardController.Instance;
        if (controller == null)
        {
            return;
        }

        // 1. 道具包免费次数
        if (_hasPropKit && !_randomToolUsed)
        {
            if (controller.PropShuffle())
            {
                _randomToolUsed = true;
                RefreshToolCounts();
            }
            return;
        }

        // 2. 购买次数
        if (_purchasedShuffleCount > 0)
        {
            if (controller.PropShuffle())
            {
                _purchasedShuffleCount--;
                RefreshToolCounts();
            }
            return;
        }

        // 3. 弹出购买UI
        OpenPropPurchaseUIForm(PropPurchaseUIForm.PropType.Shuffle);
    }

    // ───────────── 道具状态管理 ─────────────

    /// <summary>
    /// 重置道具状态（每局战斗开始时调用）。
    /// </summary>
    private void ResetPropState()
    {
        _hasPropKit = false;
        _removeToolUsed = false;
        _pickToolUsed = false;
        _randomToolUsed = false;
        _purchasedRemoveCount = 0;
        _purchasedRetrieveCount = 0;
        _purchasedShuffleCount = 0;

        PropPurchaseUIForm.ResetBattlePurchaseState();
        RefreshToolCounts();
    }

    /// <summary>
    /// 刷新道具数量显示。
    /// 数量 = 道具包未使用次数 + 购买次数。
    /// </summary>
    private void RefreshToolCounts()
    {
        // 移出道具数量
        int removeCount = 0;
        if (_hasPropKit && !_removeToolUsed) removeCount++;
        if (_purchasedRemoveCount > 0) removeCount += _purchasedRemoveCount;
        if (_txtShiftOutCount != null) _txtShiftOutCount.text = removeCount.ToString();

        // 拿取道具数量
        int pickCount = 0;
        if (_hasPropKit && !_pickToolUsed) pickCount++;
        if (_purchasedRetrieveCount > 0) pickCount += _purchasedRetrieveCount;
        if (_txtTakeCount != null) _txtTakeCount.text = pickCount.ToString();

        // 随机道具数量
        int randomCount = 0;
        if (_hasPropKit && !_randomToolUsed) randomCount++;
        if (_purchasedShuffleCount > 0) randomCount += _purchasedShuffleCount;
        if (_txtRandomCount != null) _txtRandomCount.text = randomCount.ToString();
    }

    /// <summary>
    /// 购买成功回调：增加对应道具的购买次数。
    /// </summary>
    /// <param name="propType">购买的道具类型。</param>
    private void OnPropPurchased(PropPurchaseUIForm.PropType propType)
    {
        switch (propType)
        {
            case PropPurchaseUIForm.PropType.Remove:
                _purchasedRemoveCount++;
                break;
            case PropPurchaseUIForm.PropType.Retrieve:
                _purchasedRetrieveCount++;
                break;
            case PropPurchaseUIForm.PropType.Shuffle:
                _purchasedShuffleCount++;
                break;
        }

        RefreshToolCounts();
    }

    /// <summary>
    /// 拿取状态变化回调：更新拿取按钮视觉反馈。
    /// </summary>
    /// <param name="isTakeState">当前是否处于拿取状态。</param>
    private void OnTakeStateChanged(bool isTakeState)
    {
        // 拿取状态下高亮拿取按钮（缩小提示玩家可再次点击退出）
        if (_btnTake != null)
        {
            _btnTake.transform.localScale = isTakeState
                ? new Vector3(0.9f, 0.9f, 0.9f)
                : Vector3.one;
        }
    }

    // ───────────── 道具购买UI ─────────────

    /// <summary>
    /// 打开道具购买UI。
    /// 先检查是否可购买，不可购买则跳过。
    /// </summary>
    /// <param name="propType">要购买的道具类型。</param>
    private void OpenPropPurchaseUIForm(PropPurchaseUIForm.PropType propType)
    {
        if (!PropPurchaseUIForm.CanPurchaseProp(propType, _hasPropKit))
        {
            return;
        }

        if (_propPurchaseUIFormId > 0 && GameEntry.UI.HasUIForm(_propPurchaseUIFormId))
        {
            return;
        }

        _propPurchaseUIFormId = GameEntry.UI.OpenUIForm(
            UIFormDefine.PropPurchaseUIForm, UIFormDefine.MainGroup, propType);
    }

    /// <summary>
    /// 关闭当前记录到的 PropPurchaseUIForm。
    /// </summary>
    private void ClosePropPurchaseUIForm()
    {
        if (_propPurchaseUIFormId <= 0)
        {
            return;
        }

        if (GameEntry.UI != null && GameEntry.UI.HasUIForm(_propPurchaseUIFormId))
        {
            GameEntry.UI.CloseUIForm(_propPurchaseUIFormId);
        }

        _propPurchaseUIFormId = 0;
    }
}
