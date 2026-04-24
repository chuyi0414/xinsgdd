using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 新人礼包界面。
/// 负责处理首次进入主界面时的礼包领取逻辑。
/// </summary>
public sealed class NewcomerPackageUIForm : UIFormLogic
{
    /// <summary>
    /// 新人礼包发放的手动蛋数量。
    /// </summary>
    private const int RewardManualEggCount = 6;

    /// <summary>
    /// 新人礼包发放的金币数量。
    /// </summary>
    private const int RewardGoldAmount = 1000000;

    /// <summary>
    /// 确认领取按钮。
    /// </summary>
    [SerializeField]
    private Button _btnYes;

    /// <summary>
    /// 当前界面实例是否已经完成奖励领取。
    /// </summary>
    private bool _hasClaimedReward;

    /// <summary>
    /// 界面初始化。
    /// </summary>
    /// <param name="userData">用户自定义数据。</param>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        CacheReferences();

        if (_btnYes == null)
        {
            Log.Warning("NewcomerPackageUIForm 找不到 BtnYes。");
            return;
        }

        _btnYes.onClick.RemoveListener(OnBtnYesClicked);
        _btnYes.onClick.AddListener(OnBtnYesClicked);
    }

    /// <summary>
    /// 界面打开时重置本次实例的领取状态。
    /// </summary>
    /// <param name="userData">用户自定义数据。</param>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        _hasClaimedReward = false;
    }

    /// <summary>
    /// 对象销毁时移除按钮监听。
    /// </summary>
    private void OnDestroy()
    {
        if (_btnYes != null)
        {
            _btnYes.onClick.RemoveListener(OnBtnYesClicked);
        }
    }

    /// <summary>
    /// 缓存界面节点引用。
    /// </summary>
    private void CacheReferences()
    {
        if (_btnYes == null)
        {
            Transform yesButton = transform.Find("Frame/BtnYes");
            if (yesButton != null)
            {
                _btnYes = yesButton.GetComponent<Button>();
            }
        }
    }

    /// <summary>
    /// 确认领取按钮点击回调。
    /// </summary>
    private void OnBtnYesClicked()
    {
        // 播放点击音效
        UIInteractionSound.PlayClick();
        
        if (_hasClaimedReward)
        {
            return;
        }

        _hasClaimedReward = true;

        if (GameEntry.EggHatch != null)
        {
            GameEntry.EggHatch.AddManualEggs(RewardManualEggCount);
        }

        if (GameEntry.Fruits != null)
        {
            GameEntry.Fruits.AddGold(RewardGoldAmount);
        }

        if (UIForm == null || GameEntry.UI == null)
        {
            return;
        }

        GameEntry.UI.CloseUIForm(UIForm.SerialId);
    }
}
