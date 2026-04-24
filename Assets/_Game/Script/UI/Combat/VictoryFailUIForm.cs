using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 胜利/失败弹窗界面。
/// 职责：
/// 1. 根据 IsVictory 显示 Victory 或 Fail 物体；
/// 2. 显示本局结算分数；
/// 3. BtnReturn 点击后关闭自身，设置 ReturningFromCombat 标记，切回 MainProcedure（恢复 DailyChallengeUIForm）；
/// 4. BtnRechallenge 点击后重新开始一局。
/// 序列化字段由 Inspector 拖入绑定。
/// </summary>
public sealed class VictoryFailUIForm : UIFormLogic
{
    /// <summary>
    /// 胜利物体。
    /// IsVictory=true 时显示，false 时隐藏。
    /// </summary>
    [SerializeField]
    private GameObject _victoryObj;

    /// <summary>
    /// 失败物体。
    /// IsVictory=false 时显示，true 时隐藏。
    /// </summary>
    [SerializeField]
    private GameObject _failObj;

    /// <summary>
    /// 返回按钮。
    /// 点击后关闭弹窗并返回 DailyChallengeUIForm。
    /// </summary>
    [SerializeField]
    private Button _btnReturn;

    /// <summary>
    /// 再来一局按钮。
    /// 点击后直接复用旧 CombatUIForm 原地重开当前每日一关。
    /// </summary>
    [SerializeField]
    private Button _btnRechallenge;

    /// <summary>
    /// 分数文本。
    /// 用于显示当前这一次结算弹窗携带的本局得分。
    /// Inspector 中拖入绑定；若为 null 则不显示得分。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtScore;

    /// <summary>
    /// 当前是否已经进入“再来一局”处理流程。
    /// 用于防止玩家连续点击 BtnRechallenge / BtnReturn 导致重复切换流程。
    /// 初始状态为 false。
    /// </summary>
    private bool _isRechallenging;

    /// <summary>
    /// 初始化：绑定返回按钮与再来一局按钮事件。
    /// </summary>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        if (_btnReturn != null)
        {
            _btnReturn.onClick.RemoveListener(OnBtnReturn);
            _btnReturn.onClick.AddListener(OnBtnReturn);
        }

        if (_btnRechallenge != null)
        {
            _btnRechallenge.onClick.RemoveListener(OnBtnRechallenge);
            _btnRechallenge.onClick.AddListener(OnBtnRechallenge);
        }
    }

    /// <summary>
    /// 打开时根据 VictoryFailUIData.IsVictory 切换 Victory/Fail 物体显隐，并刷新本局分数文本。
    /// </summary>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);

        // 每次打开结算窗时，都先重置按钮可交互状态，避免复用实例时残留上次的锁定状态。
        _isRechallenging = false;
        SetButtonsInteractable(true);

        // 解析打开数据，默认为失败。
        // FinalScore 在当前项目里表示“本局结算分数”，
        // 失败时为当前累计得分，胜利时为胜利翻倍后的最终结算分数。
        bool isVictory = false;
        int finalScore = 0;
        if (userData is VictoryFailUIData data)
        {
            isVictory = data.IsVictory;
            finalScore = data.FinalScore;
        }

        if (_victoryObj != null)
        {
            _victoryObj.SetActive(isVictory);
        }

        if (_failObj != null)
        {
            _failObj.SetActive(!isVictory);
        }

        // 刷新本局得分文本。
        if (_txtScore != null)
        {
            _txtScore.text = finalScore.ToString();
        }
    }

    /// <summary>
    /// 关闭时重置再来一局防重入锁，防止窗体回池后残留状态。
    /// </summary>
    protected override void OnClose(bool isShutdown, object userData)
    {
        _isRechallenging = false;
        base.OnClose(isShutdown, userData);
    }

    /// <summary>
    /// 销毁时移除按钮监听。
    /// </summary>
    private void OnDestroy()
    {
        if (_btnReturn != null)
        {
            _btnReturn.onClick.RemoveListener(OnBtnReturn);
        }

        if (_btnRechallenge != null)
        {
            _btnRechallenge.onClick.RemoveListener(OnBtnRechallenge);
        }
    }

    /// <summary>
    /// 返回按钮点击回调。
    /// 关闭自身 → 设 ReturningFromCombat 标记 → 切回 MainProcedure（恢复 DailyChallengeUIForm）。
    /// </summary>
    private void OnBtnReturn()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();

        // 若当前已经进入“再来一局”流程，则忽略返回操作，避免两个按钮竞争切流程。
        if (_isRechallenging)
        {
            return;
        }
        
        // 先关闭自身，避免流程切换时残留
        if (UIForm != null && GameEntry.UI != null)
        {
            GameEntry.UI.CloseUIForm(UIForm.SerialId);
        }

        // 设置 ReturningFromCombat 标记，通知 MainProcedure 恢复每日一关界面
        GameFramework.Procedure.ProcedureBase currentProcedure = GameEntry.Procedure.CurrentProcedure;
        currentProcedure.procedureOwner.SetData<VarInt32>(MainProcedure.ReturningFromCombatDataName, 1);
        currentProcedure.ChangeState<MainProcedure>(currentProcedure.procedureOwner);
    }

    /// <summary>
    /// 再来一局按钮点击回调。
    /// 直接复用当前已打开的 CombatUIForm，原地重建每日一关棋盘，
    /// 不再重新进入 CombatProcedure，也不再新创建 CombatUIForm(Clone)。
    /// </summary>
    private void OnBtnRechallenge()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();

        // 防重入：避免玩家连续点击导致重复重开。
        if (_isRechallenging)
        {
            return;
        }

        // 只允许在当前已有 CombatUIForm 的前提下执行“再来一局”。
        CombatUIForm combatUIForm = ResolveCombatUIForm();
        if (combatUIForm == null)
        {
            Log.Warning("VictoryFailUIForm 无法执行再来一局：当前拿不到 CombatUIForm。");
            return;
        }

        // 锁住返回 / 再来一局两个按钮，避免在原地重开收口期间再次点击。
        _isRechallenging = true;
        SetButtonsInteractable(false);

        // 直接复用旧 CombatUIForm 原地重开。
        // 失败时不关闭当前结果窗，避免玩家落到一个被清空却没重开成功的坏状态。
        if (!combatUIForm.TryRestartCurrentBattle())
        {
            _isRechallenging = false;
            SetButtonsInteractable(true);
            return;
        }

        // 重开成功后关闭当前结果窗。
        // CombatUIForm 会继续沿用旧实例，因此这里只收掉顶层结果窗即可。
        if (UIForm != null && GameEntry.UI != null && GameEntry.UI.HasUIForm(UIForm.SerialId))
        {
            GameEntry.UI.CloseUIForm(UIForm.SerialId);
        }
    }

    /// <summary>
    /// 获取当前已打开的 CombatUIForm 逻辑对象。
    /// 委托给 CombatUIForm.ResolveCurrent() 统一入口。
    /// </summary>
    /// <returns>当前 CombatUIForm 逻辑对象；不存在时返回 null。</returns>
    private static CombatUIForm ResolveCombatUIForm()
    {
        return CombatUIForm.ResolveCurrent();
    }

    /// <summary>
    /// 设置返回 / 再来一局按钮的可交互状态。
    /// 用于在切流程期间临时锁定按钮，防止重复点击。
    /// </summary>
    /// <param name="interactable">true=可点击；false=不可点击。</param>
    private void SetButtonsInteractable(bool interactable)
    {
        if (_btnReturn != null)
        {
            _btnReturn.interactable = interactable;
        }

        if (_btnRechallenge != null)
        {
            _btnRechallenge.interactable = interactable;
        }
    }
}

/// <summary>
/// VictoryFailUIForm 打开数据。
/// 携带 IsVictory 标记和本局结算分数，决定显示 Victory 或 Fail 以及分数。
/// </summary>
public sealed class VictoryFailUIData
{
    /// <summary>
    /// 是否胜利。
    /// true=显示 Victory；false=显示 Fail。
    /// </summary>
    public bool IsVictory { get; }

    /// <summary>
    /// 本局结算分数。
    /// 胜利时为翻倍后的总分，失败时为当前累计得分；
    /// 当前用于 VictoryFailUIForm.TxtScore 显示。
    /// </summary>
    public int FinalScore { get; }

    /// <summary>
    /// 构造一份 VictoryFailUIForm 打开数据。
    /// </summary>
    /// <param name="isVictory">true=胜利；false=失败。</param>
    /// <param name="finalScore">本局结算分数。</param>
    public VictoryFailUIData(bool isVictory, int finalScore = 0)
    {
        IsVictory = isVictory;
        FinalScore = finalScore;
    }
}
