using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 胜利/失败弹窗界面。
/// 职责：
/// 1. 根据 IsVictory 显示 Victory 或 Fail 物体；
/// 2. BtnReturn 点击后关闭自身，设置 ReturningFromCombat 标记，切回 MainProcedure（恢复 DailyChallengeUIForm）。
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
    /// 最终得分文本。
    /// 胜利时显示翻倍后的总分，失败时显示当前得分。
    /// Inspector 中拖入绑定；若为 null 则不显示得分。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtFinalScore;

    /// <summary>
    /// 初始化：绑定返回按钮事件。
    /// </summary>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        if (_btnReturn != null)
        {
            _btnReturn.onClick.RemoveListener(OnBtnReturn);
            _btnReturn.onClick.AddListener(OnBtnReturn);
        }
    }

    /// <summary>
    /// 打开时根据 VictoryFailUIData.IsVictory 切换 Victory/Fail 物体显隐。
    /// </summary>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);

        // 解析打开数据，默认为失败
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

        // 显示最终得分
        if (_txtFinalScore != null)
        {
            _txtFinalScore.text = finalScore.ToString();
        }
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
    }

    /// <summary>
    /// 返回按钮点击回调。
    /// 关闭自身 → 设 ReturningFromCombat 标记 → 切回 MainProcedure（恢复 DailyChallengeUIForm）。
    /// </summary>
    private void OnBtnReturn()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
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
}

/// <summary>
/// VictoryFailUIForm 打开数据。
/// 携带 IsVictory 标记和最终得分，决定显示 Victory 还是 Fail 以及分数。
/// </summary>
public sealed class VictoryFailUIData
{
    /// <summary>
    /// 是否胜利。
    /// true=显示 Victory；false=显示 Fail。
    /// </summary>
    public bool IsVictory { get; }

    /// <summary>
    /// 最终得分。
    /// 胜利时为翻倍后的总分，失败时为当前累计得分。
    /// </summary>
    public int FinalScore { get; }

    public VictoryFailUIData(bool isVictory, int finalScore = 0)
    {
        IsVictory = isVictory;
        FinalScore = finalScore;
    }
}
