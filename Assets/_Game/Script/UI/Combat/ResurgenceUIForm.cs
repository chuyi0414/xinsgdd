using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 每日一关复活确认界面。
/// </summary>
public sealed class ResurgenceUIForm : UIFormLogic
{
    /// <summary>
    /// 金币复活确认按钮。
    /// </summary>
    [SerializeField]
    private Button _buttonYes;

    /// <summary>
    /// 取消按钮。
    /// </summary>
    [SerializeField]
    private Button _buttonNo;

    /// <summary>
    /// 广告按钮。
    /// 当前业务口径不接逻辑，仅保留显示。
    /// </summary>
    [SerializeField]
    private Button _buttonAdvertising;

    /// <summary>
    /// 提示文本。
    /// </summary>
    [SerializeField]
    private TextMeshProUGUI _txtPrompt;

    /// <summary>
    /// 初始化复活确认界面，绑定按钮事件。
    /// </summary>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);

        if (_buttonYes != null)
        {
            _buttonYes.onClick.RemoveListener(OnBtnYes);
            _buttonYes.onClick.AddListener(OnBtnYes);
        }

        if (_buttonNo != null)
        {
            _buttonNo.onClick.RemoveListener(OnBtnNo);
            _buttonNo.onClick.AddListener(OnBtnNo);
        }
    }

    /// <summary>
    /// 打开时刷新文案。
    /// </summary>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);

        UpdatePromptText();
    }

    /// <summary>
    /// 销毁时移除按钮监听。
    /// </summary>
    private void OnDestroy()
    {
        if (_buttonYes != null)
        {
            _buttonYes.onClick.RemoveListener(OnBtnYes);
        }

        if (_buttonNo != null)
        {
            _buttonNo.onClick.RemoveListener(OnBtnNo);
        }
    }

    /// <summary>
    /// 金币复活按钮点击回调。
    /// </summary>
    private void OnBtnYes()
    {
        UIInteractionSound.PlayClick();

        CombatUIForm combatUIForm = ResolveCombatUIForm();
        if (combatUIForm == null)
        {
            CloseSelf();
            return;
        }

        if (!combatUIForm.TryReviveAfterFailure())
        {
            combatUIForm.EnterFailureSettlementAfterResurgence();
        }
    }

    /// <summary>
    /// 取消按钮点击回调。
    /// </summary>
    private void OnBtnNo()
    {
        UIInteractionSound.PlayClick();

        CombatUIForm combatUIForm = ResolveCombatUIForm();
        if (combatUIForm != null)
        {
            combatUIForm.EnterFailureSettlementAfterResurgence();
            return;
        }

        CloseSelf();
    }

    /// <summary>
    /// 刷新提示文本。
    /// </summary>
    private void UpdatePromptText()
    {
        if (_txtPrompt == null)
        {
            return;
        }

        int resurgenceGold = GetResurgenceGoldCost();
        _txtPrompt.text = resurgenceGold > 0
            ? $"是否花费{resurgenceGold}金币进行复活？\n复活后获得连击*10"
            : "是否进行复活？\n复活后获得连击*10";
    }

    /// <summary>
    /// 获取复活价格。
    /// </summary>
    /// <returns>复活价格；读取失败时返回 0。</returns>
    private static int GetResurgenceGoldCost()
    {
        if (GameEntry.DataTables == null || !GameEntry.DataTables.IsAvailable<DailyChallengeCostDataRow>())
        {
            return 0;
        }

        DailyChallengeCostDataRow costDataRow = GameEntry.DataTables.GetDataRowByCode<DailyChallengeCostDataRow>(DailyChallengeCostDataRow.DefaultCode);
        return costDataRow != null ? costDataRow.ResurgenceGold : 0;
    }

    /// <summary>
    /// 获取当前仍处于保活状态的 CombatUIForm 逻辑对象。
    /// </summary>
    /// <returns>当前 CombatUIForm 逻辑对象；不存在时返回 null。</returns>
    private static CombatUIForm ResolveCombatUIForm()
    {
        if (GameEntry.UI == null)
        {
            return null;
        }

        UIForm combatUI = GameEntry.UI.GetUIForm(UIFormDefine.CombatUIForm);
        return combatUI != null ? combatUI.Logic as CombatUIForm : null;
    }

    /// <summary>
    /// 关闭当前复活窗。
    /// </summary>
    private void CloseSelf()
    {
        if (UIForm != null && GameEntry.UI != null && GameEntry.UI.HasUIForm(UIForm.SerialId))
        {
            GameEntry.UI.CloseUIForm(UIForm.SerialId);
        }
    }
}
