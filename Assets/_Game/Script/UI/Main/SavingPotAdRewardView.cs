using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// 主界面内嵌的存钱罐广告领奖确认弹窗。
/// 玩家点击入口按钮后显示本弹窗：
/// 1. 点击“是”播放激励视频广告；
/// 2. 完整观看后按当前存钱罐离线收益上限发放金币；
/// 3. 发奖成功后隐藏弹窗，并通过 ToastUtility 给玩家明确反馈；
/// 4. 点击“否”只隐藏弹窗，不修改任何运行时数据。
/// </summary>
public sealed class SavingPotAdRewardView : MonoBehaviour
{
    /// <summary>
    /// “是”按钮。
    /// 初始状态由 Inspector 绑定；若漏绑，Initialize 会尝试按子节点名 BtnYes 兜底查找。
    /// </summary>
    [SerializeField]
    private Button _btnYes;

    /// <summary>
    /// “否”按钮。
    /// 初始状态由 Inspector 绑定；若漏绑，Initialize 会尝试按子节点名 BtnNo 兜底查找。
    /// </summary>
    [SerializeField]
    private Button _btnNo;

    /// <summary>
    /// 当前是否已经注册过按钮监听。
    /// 初始为 false；Initialize 成功注册后置 true，Dispose 或 OnDestroy 后置 false。
    /// </summary>
    private bool _isInitialized;

    /// <summary>
    /// 当前是否处于广告请求或播放流程中。
    /// 初始为 false；点击“是”并进入广告流程后置 true，广告成功/失败回调收口后恢复 false。
    /// </summary>
    private bool _isAdRewardProcessing;

    /// <summary>
    /// 初始化弹窗按钮引用与监听。
    /// MainUIForm.OnInit 会主动调用；Show 中也会兜底调用，避免 prefab 初始 inactive 时遗漏初始化。
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized)
        {
            return;
        }

        CacheReferences();
        RegisterButtonListeners();
        _isInitialized = true;
        Hide();
    }

    /// <summary>
    /// 清理弹窗按钮监听。
    /// MainUIForm.OnDestroy 和本组件 OnDestroy 都会调用，RemoveListener 可重复执行，保证 UIForm 复用不叠加回调。
    /// </summary>
    public void Dispose()
    {
        UnregisterButtonListeners();
        _isAdRewardProcessing = false;
        _isInitialized = false;
    }

    /// <summary>
    /// 显示存钱罐广告领奖弹窗。
    /// 每次显示都会恢复按钮可点状态，避免上一次广告失败或隐藏后留下不可交互状态。
    /// </summary>
    public void Show()
    {
        Initialize();
        _isAdRewardProcessing = false;
        SetButtonsInteractable(true);

        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// 隐藏存钱罐广告领奖弹窗。
    /// 只控制当前弹窗根节点显隐，不销毁对象，避免反复打开产生额外 GC 和实例化成本。
    /// </summary>
    public void Hide()
    {
        _isAdRewardProcessing = false;

        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }
    }

    /// <summary>
    /// Unity 销毁回调。
    /// 组件被销毁时必须移除按钮监听，避免按钮持有已销毁组件的方法引用。
    /// </summary>
    private void OnDestroy()
    {
        Dispose();
    }

    /// <summary>
    /// 缓存“是/否”按钮。
    /// Inspector 绑定优先；兜底查找只发生在低频初始化阶段，不进入 Update。
    /// </summary>
    private void CacheReferences()
    {
        if (_btnYes == null)
        {
            Transform yesButton = transform.Find("BtnYes");
            if (yesButton != null)
            {
                _btnYes = yesButton.GetComponent<Button>();
            }
        }

        if (_btnNo == null)
        {
            Transform noButton = transform.Find("BtnNo");
            if (noButton != null)
            {
                _btnNo = noButton.GetComponent<Button>();
            }
        }
    }

    /// <summary>
    /// 注册“是/否”按钮点击监听。
    /// 先 Remove 再 Add，防止 UIForm 复用或手动重复 Initialize 时出现一次点击触发多次。
    /// </summary>
    private void RegisterButtonListeners()
    {
        if (_btnYes != null)
        {
            _btnYes.onClick.RemoveListener(OnBtnYesClicked);
            _btnYes.onClick.AddListener(OnBtnYesClicked);
        }
        else
        {
            Log.Warning("SavingPotAdRewardView 缺少 BtnYes，请在 Inspector 中绑定 _btnYes。");
        }

        if (_btnNo != null)
        {
            _btnNo.onClick.RemoveListener(OnBtnNoClicked);
            _btnNo.onClick.AddListener(OnBtnNoClicked);
        }
        else
        {
            Log.Warning("SavingPotAdRewardView 缺少 BtnNo，请在 Inspector 中绑定 _btnNo。");
        }
    }

    /// <summary>
    /// 取消“是/否”按钮点击监听。
    /// 该方法只解除本组件添加的监听，不影响 prefab 上其他手动配置的 UnityEvent。
    /// </summary>
    private void UnregisterButtonListeners()
    {
        if (_btnYes != null)
        {
            _btnYes.onClick.RemoveListener(OnBtnYesClicked);
        }

        if (_btnNo != null)
        {
            _btnNo.onClick.RemoveListener(OnBtnNoClicked);
        }
    }

    /// <summary>
    /// 设置“是/否”按钮是否可交互。
    /// 广告请求期间会关闭两个按钮，避免玩家在广告加载过程中点击“否”隐藏弹窗后仍收到广告成功回调。
    /// </summary>
    /// <param name="isInteractable">true 表示允许点击；false 表示临时禁止点击。</param>
    private void SetButtonsInteractable(bool isInteractable)
    {
        if (_btnYes != null)
        {
            _btnYes.interactable = isInteractable;
        }

        if (_btnNo != null)
        {
            _btnNo.interactable = isInteractable;
        }
    }

    /// <summary>
    /// “是”按钮点击回调。
    /// 完整观看激励视频广告后发放当前存钱罐存储上限对应的金币。
    /// </summary>
    private void OnBtnYesClicked()
    {
        UIInteractionSound.PlayClick();

        if (_isAdRewardProcessing)
        {
            return;
        }

        if (!TryResolveRewardGold(out _))
        {
            ToastUtility.Show("存钱罐数据未初始化");
            return;
        }

#if UNITY_EDITOR
        // 编辑器环境没有稳定的微信激励视频运行链路，这里沿用项目内自动孵化广告的 Editor 旁路。
        // 该分支只在 UNITY_EDITOR 编译，正式包不会包含，不会绕过线上广告。
        GrantRewardAfterAd("Editor 旁路");
        return;
#else
        if (GameEntry.Advertisement == null)
        {
            Log.Warning("[SavingPotAdRewardView] AdvertisementModule 未初始化，无法播放广告。");
            ToastUtility.Show("广告暂不可用");
            return;
        }

        _isAdRewardProcessing = true;
        SetButtonsInteractable(false);
        GameEntry.Advertisement.ShowRewardedVideoAdGuarded(
            button: _btnYes,
            onSuccess: () =>
            {
                _isAdRewardProcessing = false;
                GrantRewardAfterAd("广告完成");
            },
            onFail: error =>
            {
                _isAdRewardProcessing = false;
                SetButtonsInteractable(true);
                Log.Info("[SavingPotAdRewardView] 广告观看失败：{0}", error);
            });
#endif
    }

    /// <summary>
    /// “否”按钮点击回调。
    /// 玩家取消时只关闭当前弹窗，不播放广告、不发金币、不标记存档。
    /// </summary>
    private void OnBtnNoClicked()
    {
        UIInteractionSound.PlayClick();

        if (_isAdRewardProcessing)
        {
            return;
        }

        Hide();
    }

    /// <summary>
    /// 获取当前广告奖励金币数。
    /// 奖励金额必须取“当前存钱罐离线收益存储上限”，因此每次点击和广告完成后都会重新读取。
    /// </summary>
    /// <param name="rewardGold">输出：当前可发放的金币数量。</param>
    /// <returns>true 表示 rewardGold 有效且大于 0；false 表示运行时模块或配置尚不可用。</returns>
    private static bool TryResolveRewardGold(out int rewardGold)
    {
        rewardGold = 0;
        if (GameEntry.Fruits == null || !GameEntry.Fruits.EnsureInitialized())
        {
            return false;
        }

        rewardGold = GameEntry.Fruits.OfflineEarningCapacity;
        return rewardGold > 0;
    }

    /// <summary>
    /// 广告完整观看后的统一奖励发放入口。
    /// 这里直接增加玩家金币、标记云存档为脏、请求立即保存，然后隐藏弹窗并弹 Toast。
    /// </summary>
    /// <param name="trigger">触发来源描述，仅用于日志排查。</param>
    private void GrantRewardAfterAd(string trigger)
    {
        if (!TryResolveRewardGold(out int rewardGold))
        {
            SetButtonsInteractable(true);
            ToastUtility.Show("存钱罐数据未初始化");
            return;
        }

        GameEntry.Fruits.AddGold(rewardGold);
        GameEntry.CloudSave?.MarkDirty(CloudSaveDirtyModule.PlayerProgress);
        GameEntry.CloudSave?.SaveNow(true);

        Hide();
        ToastUtility.Show(string.Format("获得{0}金币", rewardGold));
        Log.Info("[SavingPotAdRewardView] {0}，发放存钱罐广告奖励金币：{1}。", trigger, rewardGold);
    }
}
