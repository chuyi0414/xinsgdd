using UnityEngine;
using UnityEngine.UI;
using UnityGameFramework.Runtime;

/// <summary>
/// MainUIForm 自动孵化分部。
/// 负责 GoZiDongFuHua 入口：点击播放激励视频广告 → 完整观看后给 15 个独立随机蛋 →
/// 这 15 个蛋由 EggHatchComponent 在每帧 Tick 末尾按"有空槽就回补"自动入孵化。
/// 与 MainUIForm.Hatch.cs（手动孵化）完全平行，不共享任何状态字段。
/// </summary>
public partial class MainUIForm
{
    /// <summary>
    /// GoZiDongFuHua 节点上的 Button 组件，整个节点本身就是看广告入口。
    /// 用户在 Inspector 中拖入；运行时若漏拖会按固定路径兜底缓存。
    /// </summary>
    [SerializeField]
    private Button _btnAutoHatchAd;

    /// <summary>
    /// GoZiDongFuHua/GoDanShuLiang 根节点。
    /// 下方固定 15 个小点 Image，与 GoShoDongFuHua/GoDanShuLiang 同构，仅子节点数从 6 变为 15。
    /// </summary>
    [SerializeField]
    private RectTransform _autoEggCountRoot;

    /// <summary>
    /// 一次广告奖励发放的蛋总数：85% 普通 / 8% 稀有 / 4% 史诗 / 2% 传说 / 1% 神话。
    /// 如需后续改成奖励翻倍/广告组合券，仅改这里即可。
    /// </summary>
    private const int AdRewardAutoEggCount = 15;

    /// <summary>
    /// 自动孵化指示点 GameObject 缓存。
    /// 长度等于 _autoEggCountRoot.childCount，按下标控制 SetActive。
    /// </summary>
    private GameObject[] _autoEggIndicators;

    /// <summary>
    /// 自动孵化指示点 Graphic 缓存，按品质着色。
    /// 复用 MainUIForm.Hatch.cs 中的 GetManualEggIndicatorColor 静态方法，避免重复定义颜色表。
    /// </summary>
    private Graphic[] _autoEggIndicatorGraphics;

    /// <summary>
    /// 自动孵化视图是否已完成初始化。
    /// </summary>
    private bool _isAutoHatchViewReady;

    /// <summary>
    /// 广告播放防重入锁。
    /// 仅在本类内部使用，与 AdvertisementModule 内部冷却互为双保险。
    /// </summary>
    private bool _isAutoHatchAdInProgress;

    /// <summary>
    /// 缓存自动孵化相关节点引用。
    /// </summary>
    private void CacheAutoHatchReferences()
    {
        if (_pageCenter == null)
        {
            return;
        }

        if (_btnAutoHatchAd == null)
        {
            // GoZiDongFuHua 与 GoShoDongFuHua 同级，挂在 GoYouWan 下。
            Transform autoHatchAd = _pageCenter.Find("GoYouWan/GoZiDongFuHua");
            if (autoHatchAd != null)
            {
                _btnAutoHatchAd = autoHatchAd.GetComponent<Button>();
            }
        }

        if (_autoEggCountRoot == null)
        {
            _autoEggCountRoot = _pageCenter.Find("GoYouWan/GoZiDongFuHua/GoDanShuLiang") as RectTransform;
        }
    }

    /// <summary>
    /// 初始化自动孵化界面。
    /// </summary>
    private void InitializeAutoHatchView()
    {
        CacheAutoHatchReferences();
        _isAutoHatchViewReady = BuildAutoHatchViewCache();
        if (!_isAutoHatchViewReady)
        {
            return;
        }

        _btnAutoHatchAd.onClick.RemoveListener(OnAutoHatchAdClicked);
        _btnAutoHatchAd.onClick.AddListener(OnAutoHatchAdClicked);
        RefreshAutoHatchView();
    }

    /// <summary>
    /// 打开自动孵化界面时刷新一次状态。
    /// </summary>
    private void OpenAutoHatchView()
    {
        RefreshAutoHatchView();
    }

    /// <summary>
    /// 关闭自动孵化界面：仅刷新一次显示，按钮监听由 DestroyAutoHatchView 统一移除。
    /// </summary>
    private void CloseAutoHatchView()
    {
        RefreshAutoHatchView();
    }

    /// <summary>
    /// 销毁自动孵化界面时清理按钮监听。
    /// </summary>
    private void DestroyAutoHatchView()
    {
        if (_btnAutoHatchAd != null)
        {
            _btnAutoHatchAd.onClick.RemoveListener(OnAutoHatchAdClicked);
        }

        _isAutoHatchAdInProgress = false;
    }

    /// <summary>
    /// 每帧刷新自动孵化界面。
    /// 与手动孵化同频，避免广告奖励入库后 UI 一帧延迟。
    /// </summary>
    private void UpdateAutoHatchView()
    {
        RefreshAutoHatchView();
    }

    /// <summary>
    /// 构建自动孵化视图缓存。
    /// </summary>
    private bool BuildAutoHatchViewCache()
    {
        if (_btnAutoHatchAd == null || _autoEggCountRoot == null)
        {
            // 自动孵化是新加功能：缺节点不致命，仅在 Inspector 漏拖或 prefab 还没加 GoDanShuLiang 子节点时跳过。
            // 走 Warning 而不是 Error，避免阻断主界面其他子视图的初始化。
            Log.Warning("MainUIForm 自动孵化视图初始化跳过：缺少 GoZiDongFuHua/Button 或 GoDanShuLiang 节点。");
            return false;
        }

        if (_autoEggCountRoot.childCount <= 0)
        {
            Log.Warning("MainUIForm 自动孵化视图初始化跳过：GoZiDongFuHua/GoDanShuLiang 没有子节点。");
            return false;
        }

        // 这里不强制要求 childCount 必须等于 EggHatchComponent.MaxAutoEggCount，
        // 是为了兼容策划阶段调整 prefab 子点数量的过渡期。
        // 运行期若 child 数量小于库存上限，多余的库存会因为没有指示点显示不出来，但不会异常。
        int indicatorCount = _autoEggCountRoot.childCount;
        _autoEggIndicators = new GameObject[indicatorCount];
        _autoEggIndicatorGraphics = new Graphic[indicatorCount];
        for (int i = 0; i < indicatorCount; i++)
        {
            Transform indicatorTransform = _autoEggCountRoot.GetChild(i);
            _autoEggIndicators[i] = indicatorTransform.gameObject;
            _autoEggIndicatorGraphics[i] = indicatorTransform.GetComponent<Graphic>();
            if (_autoEggIndicatorGraphics[i] == null)
            {
                _autoEggIndicatorGraphics[i] = indicatorTransform.GetComponentInChildren<Graphic>(true);
            }

            if (_autoEggIndicatorGraphics[i] == null)
            {
                Log.Warning("MainUIForm 自动孵化指示器 '{0}' 缺少 Graphic 组件，将不参与品质着色。", indicatorTransform.name);
            }
        }

        return true;
    }

    /// <summary>
    /// 刷新自动孵化区显示。
    /// 数量来自 EggHatchComponent.AutoEggCount，品质来自 TryGetAutoEggAt。
    /// </summary>
    private void RefreshAutoHatchView()
    {
        if (!_isAutoHatchViewReady)
        {
            return;
        }

        EggHatchComponent eggHatch = GameEntry.EggHatch;
        bool isAvailable = eggHatch != null && eggHatch.IsAvailable;

        if (_autoEggIndicators == null)
        {
            return;
        }

        for (int i = 0; i < _autoEggIndicators.Length; i++)
        {
            GameObject indicator = _autoEggIndicators[i];
            if (indicator == null)
            {
                continue;
            }

            QualityType quality = QualityType.Universal;
            bool hasEgg = isAvailable && eggHatch.TryGetAutoEggAt(i, out _, out quality);
            if (indicator.activeSelf != hasEgg)
            {
                indicator.SetActive(hasEgg);
            }

            if (hasEgg
                && _autoEggIndicatorGraphics != null
                && i < _autoEggIndicatorGraphics.Length
                && _autoEggIndicatorGraphics[i] != null)
            {
                // 复用 MainUIForm.Hatch.cs 中已有的颜色映射，保持手动 / 自动两侧视觉一致。
                _autoEggIndicatorGraphics[i].color = GetManualEggIndicatorColor(quality);
            }
        }
    }

    /// <summary>
    /// GoZiDongFuHua 按钮点击：播放激励视频广告，完整观看后发放 15 个独立随机蛋。
    /// 防重入：本类锁 + AdvertisementModule.ShowRewardedVideoAdGuarded 双保险。
    /// 编辑器（UNITY_EDITOR）下跳过广告 SDK，直接走奖励，方便策划 / QA 在 Editor 中验证自动孵化链路。
    /// 库存非空时直接 Toast 拦截，避免玩家在上轮自动蛋未消耗完时重复刷新（看广告 = 一次性补满）。
    /// </summary>
    private void OnAutoHatchAdClicked()
    {
        // 播放点击音效（与其他主界面按钮保持一致）。
        // 即便走拒绝分支也保留点击反馈，与 OnBtnDailyChallenge 锁定态行为一致。
        UIInteractionSound.PlayClick();

        if (_isAutoHatchAdInProgress)
        {
            return;
        }

        if (GameEntry.EggHatch == null)
        {
            Log.Warning("[MainUIForm.AutoHatch] EggHatchComponent 未初始化，无法发放奖励。");
            return;
        }

        // 库存闸门：上一轮的自动孵化蛋还没全部消耗完时，只 Toast 提示，不再弹广告也不走 Editor 旁路。
        // 该判断必须放在 Editor 旁路与广告分支之前，确保两条路径都走同一份"库存非空 → 拒绝"逻辑。
        // ToastUtility.Show 内部使用 ToastUIForm.prefab。
        if (GameEntry.EggHatch.AutoEggCount > 0)
        {
            ToastUtility.Show("自动孵化的蛋还没用完");
            return;
        }

#if UNITY_EDITOR
        // ─── 编辑器旁路：不走广告 SDK，直接发奖励 ───
        // 注意：仅 UNITY_EDITOR 宏内有效，Build 出来的 WebGL/小游戏包会被预处理器整段剥离，不会泄漏奖励。
        GrantAutoHatchAdReward("Editor 旁路");
        return;
#else
        if (GameEntry.Advertisement == null)
        {
            Log.Warning("[MainUIForm.AutoHatch] AdvertisementModule 未初始化，无法播放广告。");
            ToastUtility.Show("广告暂不可用");
            return;
        }

        _isAutoHatchAdInProgress = true;
        // ShowRewardedVideoAdGuarded 会在播放期间把 _btnAutoHatchAd.interactable 置 false，
        // 成功 / 失败回调中再恢复，无需在 UI 层手动管理按钮状态。
        GameEntry.Advertisement.ShowRewardedVideoAdGuarded(
            button: _btnAutoHatchAd,
            onSuccess: () =>
            {
                _isAutoHatchAdInProgress = false;
                GrantAutoHatchAdReward("广告完成");
            },
            onFail: error =>
            {
                _isAutoHatchAdInProgress = false;
                Log.Info("[MainUIForm.AutoHatch] 广告观看失败：{0}", error);
            });
#endif
    }

    /// <summary>
    /// 实际发放广告奖励：调 EggHatchComponent.AddRandomAutoEggs，并立即刷新 UI。
    /// 抽出独立方法是因为编辑器旁路与正式广告流程都需要相同的入库 + 刷新动作。
    /// </summary>
    /// <param name="trigger">触发来源描述，仅用于日志排查。</param>
    private void GrantAutoHatchAdReward(string trigger)
    {
        int granted = GameEntry.EggHatch.AddRandomAutoEggs(AdRewardAutoEggCount);
        if (granted > 0)
        {
            // 立即刷一次，避免等到下一帧 Update 才看到指示点亮起。
            RefreshAutoHatchView();
            Log.Info("[MainUIForm.AutoHatch] {0} 奖励发放：{1}/{2} 个蛋。", trigger, granted, AdRewardAutoEggCount);
        }
        else
        {
            Log.Warning("[MainUIForm.AutoHatch] {0} 奖励发放失败：返回 0 个蛋。", trigger);
        }
    }

#if UNITY_EDITOR
    /// <summary>
    /// 编辑器右键菜单：在运行态选中 MainUIForm 根节点，从 Inspector 右上角 ⋮ 菜单点击即可立即发奖励。
    /// 仅 Editor 编译，Build 输出不会包含此方法。
    /// </summary>
    [ContextMenu("Editor/发放 15 个自动孵化蛋")]
    private void EditorGrantAutoHatchEggs()
    {
        if (GameEntry.EggHatch == null)
        {
            Log.Warning("[MainUIForm.AutoHatch] 编辑器右键发奖励失败：EggHatchComponent 不可用。");
            return;
        }

        GrantAutoHatchAdReward("Editor 右键菜单");
    }
#endif
}
