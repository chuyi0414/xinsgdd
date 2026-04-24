using System;
using UnityEngine;
using UnityGameFramework.Runtime;
using WeChatWASM;

/// <summary>
/// 广告管理模块。
/// 封装微信小游戏激励视频广告（RewardedVideoAd）的完整生命周期：
/// 创建实例、加载、展示、关闭回调、防重入锁。
/// 所有业务层（道具购买、复活等）统一通过本模块发起广告播放请求。
/// </summary>
public class AdvertisementModule
{
    /// <summary>
    /// 默认激励视频广告位 ID。
    /// 【TODO】必须替换为在微信后台申请的真实广告位 ID，否则广告无法加载。
    /// </summary>
    private const string DefaultRewardedVideoAdUnitId = "adunit-c5c01a5ba0943c07";

    /// <summary>
    /// 广告请求最小冷却间隔（秒）。
    /// 防止玩家高频误触导致重复请求。
    /// </summary>
    private const float RewardedAdClickGuardSeconds = 0.8f;

    // ───────────── 广告实例与状态 ─────────────

    /// <summary>
    /// 当前持有的激励视频广告实例。
    /// 同一广告位 ID 的生命周期内复用已有实例。
    /// </summary>
    private WXRewardedVideoAd _rewardedVideoAd;

    /// <summary>
    /// 当前广告实例对应的广告位 ID。
    /// </summary>
    private string _currentAdUnitId;

    /// <summary>
    /// 广告素材是否已加载完成（可直接 Show）。
    /// 在 OnLoad / Load 成功回调中置 true；
    /// 在 Show 失败、OnError、OnClose 后置 false。
    /// </summary>
    private bool _isAdLoaded;

    /// <summary>
    /// 广告是否正在展示中。
    /// 展示开始到 OnClose 之间为 true，用于防重入。
    /// </summary>
    private bool _isAdShowing;

    /// <summary>
    /// 防重入锁：true 表示当前有广告正在请求或展示中，禁止再次触发 Show。
    /// </summary>
    private bool _isRewardedVideoAdRequesting;

    /// <summary>
    /// 上一次允许发起广告请求的时间戳（基于 Time.unscaledTime）。
    /// </summary>
    private float _nextRewardedVideoAdAllowedTime;

    /// <summary>
    /// 广告关闭事件回调引用。
    /// 用于在释放实例前统一注销 OffClose，防止事件泄漏。
    /// </summary>
    private Action<WXRewardedVideoAdOnCloseResponse> _rewardedVideoAdCloseHandler;

    /// <summary>
    /// 播放成功回调（在 OnClose 中 isEnded=true 时触发）。
    /// </summary>
    private Action _onSuccessCallback;

    /// <summary>
    /// 播放失败回调。
    /// </summary>
    private Action<string> _onFailCallback;

    // ───────────── 公共接口 ─────────────

    /// <summary>
    /// 全局预加载激励视频广告。
    /// 在合适的时机（如游戏启动、战斗开始前）调用一次，
    /// 确保玩家点击广告按钮时素材已就绪，可立即播放。
    /// 若当前广告位已加载完成，则跳过。
    /// </summary>
    /// <param name="adUnitId">可选广告位 ID；为空时使用默认广告位。</param>
    public void PreloadRewardedVideoAd(string adUnitId = null)
    {
        string resolvedAdUnitId = string.IsNullOrEmpty(adUnitId) ? DefaultRewardedVideoAdUnitId : adUnitId;

        // 若当前实例与目标广告位一致且已加载，无需重复预加载
        if (_rewardedVideoAd != null && _currentAdUnitId == resolvedAdUnitId && _isAdLoaded)
        {
            return;
        }

        // 确保实例存在（adUnitId 变化时会自动重建）
        EnsureRewardedVideoAd(resolvedAdUnitId);

        if (_rewardedVideoAd == null)
        {
            Log.Warning("[AdvertisementModule] 广告实例创建失败，无法预加载。");
            return;
        }

        _isAdLoaded = false;

        _rewardedVideoAd.Load(
            _res =>
            {
                _isAdLoaded = true;
                Log.Info("[AdvertisementModule] 预加载成功");
            },
            err =>
            {
                _isAdLoaded = false;
                Log.Warning($"[AdvertisementModule] 预加载失败：{err?.errMsg} (code={err?.errCode})");
            });
    }

    /// <summary>
    /// 展示激励视频广告。
    /// 若已全局预加载完成，可立即播放；否则先 Load 再 Show（兜底）。
    /// 完整观看后触发 onSuccess，加载失败、播放失败或用户中途关闭触发 onFail。
    /// 广告关闭后（无论结果）会自动触发下一次预加载，保持全局可用态。
    /// </summary>
    /// <param name="onSuccess">完整观看成功回调。</param>
    /// <param name="onFail">失败回调，参数为失败原因字符串。</param>
    /// <param name="adUnitId">可选广告位 ID；为空时使用默认广告位。</param>
    public void ShowRewardedVideoAd(Action onSuccess, Action<string> onFail, string adUnitId = null)
    {
        string resolvedAdUnitId = string.IsNullOrEmpty(adUnitId) ? DefaultRewardedVideoAdUnitId : adUnitId;

        // 防重入与冷却检查
        float now = Time.unscaledTime;
        if (_isRewardedVideoAdRequesting || _isAdShowing || now < _nextRewardedVideoAdAllowedTime)
        {
            onFail?.Invoke("广告请求过于频繁，请稍后再试。");
            return;
        }

        _onSuccessCallback = onSuccess;
        _onFailCallback = onFail;
        _isRewardedVideoAdRequesting = true;
        _isAdShowing = true;
        _nextRewardedVideoAdAllowedTime = now + RewardedAdClickGuardSeconds;

        // 若 adUnitId 变化，需要重建实例；重建后 _isAdLoaded 会被置 false
        EnsureRewardedVideoAd(resolvedAdUnitId);

        // 执行展示流程（优先利用已预加载的素材）
        ShowRewardedVideoAdInternal(resolvedAdUnitId);
    }

    // ───────────── 内部实现 ─────────────

    /// <summary>
    /// 带按钮保护的激励视频广告播放。
    /// 自动管理：按钮禁用/恢复 + 成功/失败回调。
    /// 适用于 ResurgenceUIForm / PropPurchaseUIForm 等场景，
    /// 消除各 UIForm 中重复的按钮禁用样板代码。
    /// 调用方需自行管理防重入锁（因为 C# 不允许 ref 参数在 lambda 中使用）。
    /// </summary>
    /// <param name="button">
    /// 需要在播放期间禁用的按钮引用；可为 null（无按钮需禁用）。
    /// </param>
    /// <param name="onSuccess">完整观看成功回调。</param>
    /// <param name="onFail">失败回调；为 null 时由本方法提供默认空操作。</param>
    /// <param name="adUnitId">可选广告位 ID；为空时使用默认广告位。</param>
    public void ShowRewardedVideoAdGuarded(
        UnityEngine.UI.Button button,
        Action onSuccess,
        Action<string> onFail = null,
        string adUnitId = null)
    {
        // 播放期间禁用按钮，防止重复点击
        if (button != null)
        {
            button.interactable = false;
        }

        // 提供默认失败回调，确保按钮状态总能恢复
        Action<string> safeOnFail = onFail ?? (_ => { });

        ShowRewardedVideoAd(
            onSuccess: () =>
            {
                if (button != null)
                {
                    button.interactable = true;
                }
                onSuccess?.Invoke();
            },
            onFail: error =>
            {
                if (button != null)
                {
                    button.interactable = true;
                }
                safeOnFail.Invoke(error);
            },
            adUnitId: adUnitId);
    }

    /// <summary>
    /// 确保指定广告位 ID 的激励视频广告实例已初始化。
    /// 若 adUnitId 变化，会销毁旧实例并创建新实例。
    /// </summary>
    private void EnsureRewardedVideoAd(string adUnitId)
    {
        if (_rewardedVideoAd != null && _currentAdUnitId == adUnitId)
        {
            return;
        }

        // 清理旧实例，避免事件泄漏；旧素材已失效
        DisposeCurrentRewardedVideoAd();
        _isAdLoaded = false;

        _currentAdUnitId = adUnitId;

        var createParam = new WXCreateRewardedVideoAdParam
        {
            adUnitId = adUnitId,
            multiton = true
        };

        _rewardedVideoAd = WX.CreateRewardedVideoAd(createParam);

        // 注册加载成功事件：仅在非主动预加载流程中（由 Show 内部触发的兜底 Load）
        // 也需要更新 _isAdLoaded，保证 Show 流程可继续
        _rewardedVideoAd.OnLoad((res) =>
        {
            _isAdLoaded = true;
            Log.Info("[AdvertisementModule] 激励视频广告加载成功");
        });

        // 注册错误事件：若当前正处于 Show 请求中，直接收口失败
        _rewardedVideoAd.OnError((err) =>
        {
            _isAdLoaded = false;
            Log.Warning($"[AdvertisementModule] 激励视频广告错误：{err?.errMsg} (code={err?.errCode})");
            if (_isRewardedVideoAdRequesting)
            {
                _isRewardedVideoAdRequesting = false;
                _isAdShowing = false;
                _onFailCallback?.Invoke(err != null ? err.errMsg : "广告加载或播放时发生错误");
            }
        });
    }

    /// <summary>
    /// 释放当前广告实例并注销事件监听。
    /// </summary>
    private void DisposeCurrentRewardedVideoAd()
    {
        if (_rewardedVideoAd == null)
        {
            return;
        }

        if (_rewardedVideoAdCloseHandler != null)
        {
            _rewardedVideoAd.OffClose(_rewardedVideoAdCloseHandler);
            _rewardedVideoAdCloseHandler = null;
        }

        _rewardedVideoAd = null;
        _currentAdUnitId = null;
    }

    /// <summary>
    /// 执行激励视频广告展示。
    /// 优先直接 Show；若失败则先 Load 再重试一次 Show。
    /// </summary>
    private void ShowRewardedVideoAdInternal(string adUnitId)
    {
        if (_rewardedVideoAd == null)
        {
            _isRewardedVideoAdRequesting = false;
            _isAdShowing = false;
            _onFailCallback?.Invoke("广告实例未初始化");
            return;
        }

        // 若素材未预加载（如实例刚重建或预加载失败），先 Load 再 Show
        if (!_isAdLoaded)
        {
            _rewardedVideoAd.Load(
                loadSuccess =>
                {
                    _isAdLoaded = true;
                    ExecuteShow(adUnitId);
                },
                loadFail =>
                {
                    _isRewardedVideoAdRequesting = false;
                    _isAdShowing = false;
                    _onFailCallback?.Invoke(loadFail != null ? loadFail.errMsg : "广告加载失败");
                });
            return;
        }

        // 已预加载：直接 Show
        ExecuteShow(adUnitId);
    }

    /// <summary>
    /// 执行真正的 Show 调用并注册 OnClose。
    /// 与加载/重试逻辑分离，保持代码可读性。
    /// </summary>
    private void ExecuteShow(string adUnitId)
    {
        _rewardedVideoAd.Show(
            success =>
            {
                Log.Info("[AdvertisementModule] 激励视频广告展示成功");
            },
            fail =>
            {
                Log.Info("[AdvertisementModule] 激励视频广告直接展示失败，尝试加载后重试");
                _isAdLoaded = false;
                _rewardedVideoAd.Load(
                    loadSuccess =>
                    {
                        _isAdLoaded = true;
                        _rewardedVideoAd.Show(
                            success2 => { },
                            fail2 =>
                            {
                                _isRewardedVideoAdRequesting = false;
                                _isAdShowing = false;
                                _onFailCallback?.Invoke(fail2 != null ? fail2.errMsg : "广告展示失败");
                                // 兜底：无论成功失败，关闭后都应触发预加载
                                PreloadRewardedVideoAd(adUnitId);
                            });
                    },
                    loadFail =>
                    {
                        _isRewardedVideoAdRequesting = false;
                        _isAdShowing = false;
                        _onFailCallback?.Invoke(loadFail != null ? loadFail.errMsg : "广告加载失败");
                        PreloadRewardedVideoAd(adUnitId);
                    });
            });

        // 注册关闭监听：只有收到 OnClose 才能判断用户是否完整观看
        _rewardedVideoAdCloseHandler = (res) =>
        {
            _rewardedVideoAd?.OffClose(_rewardedVideoAdCloseHandler);
            _rewardedVideoAdCloseHandler = null;

            _isRewardedVideoAdRequesting = false;
            _isAdShowing = false;
            // 广告被展示后素材已消耗，需要重新加载
            _isAdLoaded = false;

            if (res != null && res.isEnded)
            {
                Log.Info("[AdvertisementModule] 激励视频广告完整观看，发放奖励");
                _onSuccessCallback?.Invoke();
            }
            else
            {
                string reason = res == null ? "广告关闭数据异常" : "用户未完整观看广告";
                Log.Info($"[AdvertisementModule] {reason}，不发放奖励");
                _onFailCallback?.Invoke(reason);
            }

            // 无论成功失败，关闭后立刻预加载下一条，保持全局可用态
            PreloadRewardedVideoAd(adUnitId);
        };

        _rewardedVideoAd.OnClose(_rewardedVideoAdCloseHandler);
    }
}
