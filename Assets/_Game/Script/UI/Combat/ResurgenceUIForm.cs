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
    /// 点击后播放微信小游戏激励视频广告，完整观看后免费复活。
    /// </summary>
    [SerializeField]
    private Button _buttonAdvertising;

    /// <summary>
    /// 广告复活流程防重入锁。
    /// 防止玩家在广告播放期间重复点击广告按钮。
    /// </summary>
    private bool _isAdResurgenceProcessing;

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

        if (_buttonAdvertising != null)
        {
            _buttonAdvertising.onClick.RemoveListener(OnBtnAdvertising);
            _buttonAdvertising.onClick.AddListener(OnBtnAdvertising);
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
    /// 关闭时重置广告防重入锁，防止窗体回池后残留状态。
    /// </summary>
    protected override void OnClose(bool isShutdown, object userData)
    {
        _isAdResurgenceProcessing = false;
        base.OnClose(isShutdown, userData);
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

        if (_buttonAdvertising != null)
        {
            _buttonAdvertising.onClick.RemoveListener(OnBtnAdvertising);
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
    /// 广告复活按钮点击回调。
    /// 调用微信小游戏激励视频广告，完整观看后执行复活逻辑（移出 + Combo*10）。
    /// </summary>
    private void OnBtnAdvertising()
    {
        UIInteractionSound.PlayClick();

        if (_isAdResurgenceProcessing)
        {
            return;
        }

        CombatUIForm combatUIForm = ResolveCombatUIForm();
        if (combatUIForm == null)
        {
            CloseSelf();
            return;
        }

        if (GameEntry.Advertisement == null)
        {
            Log.Warning("[ResurgenceUIForm] AdvertisementModule 未初始化，无法播放广告。");
            return;
        }

        _isAdResurgenceProcessing = true;
        GameEntry.Advertisement.ShowRewardedVideoAdGuarded(
            button: _buttonAdvertising,
            onSuccess: () =>
            {
                _isAdResurgenceProcessing = false;
                if (!combatUIForm.TryReviveAfterFailureByAd())
                {
                    combatUIForm.EnterFailureSettlementAfterResurgence();
                }
            },
            onFail: error =>
            {
                _isAdResurgenceProcessing = false;
                Log.Info("[ResurgenceUIForm] 广告观看失败：{0}", error);
            });
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
    /// 委托给 GameDataTableModule 统一查询方法。
    /// </summary>
    /// <returns>复活价格；读取失败时返回 0。</returns>
    private static int GetResurgenceGoldCost()
    {
        if (GameEntry.DataTables == null)
        {
            return 0;
        }

        return GameEntry.DataTables.GetResurgenceGoldCost();
    }

    /// <summary>
    /// 获取当前仍处于保活状态的 CombatUIForm 逻辑对象。
    /// 委托给 CombatUIForm.ResolveCurrent() 统一入口。
    /// </summary>
    /// <returns>当前 CombatUIForm 逻辑对象；不存在时返回 null。</returns>
    private static CombatUIForm ResolveCombatUIForm()
    {
        return CombatUIForm.ResolveCurrent();
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
