using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityGameFramework.Runtime;
#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
using WeChatWASM;
#endif

/// <summary>
/// 每日一关排行榜模块。
/// 负责低频请求今日榜、提交结算分数，并把服务端确认后的历史最高分同步回玩家运行时模块。
/// </summary>
public sealed class DailyChallengeLeaderboardModule
{
    /// <summary>
    /// 云函数名称。
    /// 必须与微信云开发中部署的排行榜/存档云函数名保持一致。
    /// </summary>
    private const string CloudFunctionName = "sgdd_server";

    /// <summary>
    /// 云开发环境 Id。
    /// 与 CloudSaveModule 保持同一环境，确保排行榜和玩家存档在同一套云数据库内。
    /// </summary>
    private const string CloudEnvironmentId = "tianxing-001-2g9lrxwh45e5182d";

    /// <summary>
    /// 拉取今日排行榜的云函数动作名。
    /// </summary>
    private const string LoadLeaderboardAction = "loadDailyChallengeLeaderboard";

    /// <summary>
    /// 提交本局分数的云函数动作名。
    /// </summary>
    private const string SubmitScoreAction = "submitDailyChallengeScore";

    /// <summary>
    /// 每日一关排行榜缓存有效时长。
    /// 10 分钟内重复打开 DailyChallengeUIForm 时直接复用本地缓存，避免反复请求云函数。
    /// </summary>
    private const long LeaderboardResponseCacheValidTicks = TimeSpan.TicksPerMinute * 10;

    /// <summary>
    /// 是否已经从服务端响应中缓存过今日最高分。
    /// 初始为 false；只有成功拉榜或提交后才会置 true，用于避免无缓存时误拦截首次提交。
    /// </summary>
    private bool _hasCachedTodayBestScore;

    /// <summary>
    /// 已缓存今日最高分对应的日期键。
    /// 来自服务端响应 dateKey，格式为 yyyyMMdd。
    /// </summary>
    private string _cachedTodayBestDateKey = string.Empty;

    /// <summary>
    /// 已缓存的当前玩家今日最高分。
    /// 仅当 _hasCachedTodayBestScore 为 true 且日期键仍是本地北京时间当天时，才用于提交前拦截。
    /// </summary>
    private int _cachedTodayBestScore;

    /// <summary>
    /// 已缓存的完整排行榜响应。
    /// 只服务于 DailyChallengeUIForm 重复打开时的低频展示缓存。
    /// </summary>
    private DailyChallengeLeaderboardResponse _cachedLeaderboardResponse;

    /// <summary>
    /// 完整排行榜响应缓存写入时的 UTC Tick。
    /// 用于判断缓存是否仍处于 10 分钟有效期内。
    /// </summary>
    private long _cachedLeaderboardResponseUtcTicks;

    /// <summary>
    /// 完整排行榜响应缓存对应的日期键。
    /// 日期变化时必须丢弃缓存，避免跨天继续显示旧榜单。
    /// </summary>
    private string _cachedLeaderboardResponseDateKey = string.Empty;

    /// <summary>
    /// 是否需要忽略 10 分钟缓存，在下次打开榜单时强制请求一次云端。
    /// 当玩家本局提交成功并刷新今日记录后置 true；下一次拉榜成功后置 false。
    /// </summary>
    private bool _forceRefreshLeaderboardOnNextLoad;

#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
    /// <summary>
    /// 微信小游戏运行时云能力是否已经初始化。
    /// </summary>
    private bool _isWechatCloudInitialized;
#endif

    /// <summary>
    /// 请求今日排行榜数据。
    /// </summary>
    /// <param name="onSuccess">请求成功回调。</param>
    /// <param name="onFailure">请求失败回调。</param>
    /// <returns>是否成功发起请求。</returns>
    public bool LoadTodayLeaderboard(Action<DailyChallengeLeaderboardResponse> onSuccess, Action<string> onFailure)
    {
        if (TryUseCachedLeaderboardResponse(onSuccess))
        {
            return true;
        }

        CallCloudFunction(
            LoadLeaderboardAction,
            null,
            responseJson => HandleLeaderboardResponse(
                responseJson,
                response =>
                {
                    CacheLeaderboardResponse(response);
                    _forceRefreshLeaderboardOnNextLoad = false;
                    onSuccess?.Invoke(response);
                },
                onFailure),
            onFailure);
        return true;
    }

    /// <summary>
    /// 提交每日一关结算分数。
    /// </summary>
    /// <param name="score">本局最终结算分数。</param>
    /// <param name="onSuccess">提交成功回调。</param>
    /// <param name="onFailure">提交失败回调。</param>
    /// <returns>是否成功发起请求。</returns>
    public bool SubmitScore(int score, Action<DailyChallengeLeaderboardResponse> onSuccess, Action<string> onFailure)
    {
        int normalizedScore = Mathf.Max(0, score);
        bool hasCachedTodayBestBeforeSubmit = TryGetCachedTodayBestScoreForLocalDate(out int cachedTodayBestBeforeSubmit);
        if (CanSkipSubmitByCachedTodayBestScore(normalizedScore))
        {
            return false;
        }

        TrySaveDirtyCloudBeforeScoreSubmit();
        SubmitScoreToCloud(normalizedScore, hasCachedTodayBestBeforeSubmit, cachedTodayBestBeforeSubmit, onSuccess, onFailure);
        return true;
    }

    /// <summary>
    /// 向云端提交每日一关分数。
    /// 该方法只负责排行榜请求本身，调用前由 SubmitScore 决定是否需要先同步一次云存档。
    /// </summary>
    /// <param name="normalizedScore">已经归一化后的非负分数。</param>
    /// <param name="hasCachedTodayBestBeforeSubmit">提交前是否存在本地当天今日最高分缓存。</param>
    /// <param name="cachedTodayBestBeforeSubmit">提交前缓存的今日最高分。</param>
    /// <param name="onSuccess">提交成功回调。</param>
    /// <param name="onFailure">提交失败回调。</param>
    private void SubmitScoreToCloud(int normalizedScore, bool hasCachedTodayBestBeforeSubmit, int cachedTodayBestBeforeSubmit, Action<DailyChallengeLeaderboardResponse> onSuccess, Action<string> onFailure)
    {
        Dictionary<string, object> payload = new Dictionary<string, object>(1)
        {
            { "score", normalizedScore }
        };

        CallCloudFunction(
            SubmitScoreAction,
            payload,
            responseJson => HandleLeaderboardResponse(
                responseJson,
                response =>
                {
                    if (ShouldForceRefreshAfterSubmit(response, normalizedScore, hasCachedTodayBestBeforeSubmit, cachedTodayBestBeforeSubmit))
                    {
                        _forceRefreshLeaderboardOnNextLoad = true;
                    }

                    onSuccess?.Invoke(response);
                },
                onFailure),
            onFailure);
    }

    /// <summary>
    /// 如果当前云存档存在未同步变化，则在提交排行榜分数前先发起一次保存。
    /// 该方法只负责触发保存请求，不等待保存完成，避免排行榜上传流程被云存档结果阻塞。
    /// </summary>
    private static void TrySaveDirtyCloudBeforeScoreSubmit()
    {
        if (GameEntry.CloudSave == null || !GameEntry.CloudSave.HasDirtyChanges)
        {
            return;
        }

        GameEntry.CloudSave.SaveNow(true);
    }

    /// <summary>
    /// 执行排行榜云函数请求。
    /// </summary>
    /// <param name="action">业务动作名。</param>
    /// <param name="payload">业务附加参数。</param>
    /// <param name="onSuccess">云函数成功回调。</param>
    /// <param name="onFailure">云函数失败回调。</param>
    private void CallCloudFunction(string action, Dictionary<string, object> payload, Action<string> onSuccess, Action<string> onFailure)
    {
        Dictionary<string, object> requestData = new Dictionary<string, object>(payload != null ? payload.Count + 1 : 1)
        {
            { "action", action }
        };

        if (payload != null)
        {
            foreach (KeyValuePair<string, object> pair in payload)
            {
                requestData[pair.Key] = pair.Value ?? string.Empty;
            }
        }

#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
        Action<string> failureWrapper = errorMessage =>
        {
            onFailure?.Invoke(errorMessage);
        };
        try
        {
            EnsureWechatCloudInitialized(
                () => ExecuteWechatCloudFunction(requestData, onSuccess, failureWrapper),
                failureWrapper);
        }
        catch (Exception exception)
        {
            onFailure?.Invoke(exception.Message);
        }
#else
        DailyChallengeLeaderboardResponse localResponse = CreateLocalResponse(action, payload);
        onSuccess?.Invoke(JsonUtility.ToJson(localResponse));
#endif
    }

    /// <summary>
    /// 处理排行榜云函数响应。
    /// </summary>
    /// <param name="responseJson">云函数返回 JSON。</param>
    /// <param name="onSuccess">业务成功回调。</param>
    /// <param name="onFailure">业务失败回调。</param>
    private void HandleLeaderboardResponse(string responseJson, Action<DailyChallengeLeaderboardResponse> onSuccess, Action<string> onFailure)
    {
        DailyChallengeLeaderboardResponse response = ParseResponse(responseJson);
        if (response == null)
        {
            onFailure?.Invoke("排行榜响应解析失败");
            return;
        }

        if (!response.ok)
        {
            onFailure?.Invoke(response.errMsg ?? string.Empty);
            return;
        }

        CacheTodayBestScore(response);
        GameEntry.Fruits?.ApplyDailyChallengeHistoricalBestScore(response.historicalBestScore, response.historicalBestTime);
        onSuccess?.Invoke(response);
    }

    /// <summary>
    /// 判断是否可以基于当前缓存的今日最高分跳过提交。
    /// </summary>
    /// <param name="score">本局最终结算分数。</param>
    /// <returns>true 表示本局分数没有超过缓存今日最高分，可以不上传。</returns>
    private bool CanSkipSubmitByCachedTodayBestScore(int score)
    {
        if (!TryGetCachedTodayBestScoreForLocalDate(out int cachedTodayBestScore))
        {
            return false;
        }

        return score <= cachedTodayBestScore;
    }

    /// <summary>
    /// 尝试获取本地当天可用的当前玩家今日最高分缓存。
    /// </summary>
    /// <param name="todayBestScore">缓存的今日最高分。</param>
    /// <returns>true 表示缓存存在且日期仍是本地北京时间当天。</returns>
    private bool TryGetCachedTodayBestScoreForLocalDate(out int todayBestScore)
    {
        todayBestScore = 0;
        if (!_hasCachedTodayBestScore)
        {
            return false;
        }

        if (!string.Equals(_cachedTodayBestDateKey, GetLocalBeijingDateKey(), StringComparison.Ordinal))
        {
            return false;
        }

        todayBestScore = _cachedTodayBestScore;
        return true;
    }

    /// <summary>
    /// 从服务端响应缓存当前玩家今日最高分。
    /// </summary>
    /// <param name="response">排行榜云函数响应。</param>
    private void CacheTodayBestScore(DailyChallengeLeaderboardResponse response)
    {
        if (response == null)
        {
            return;
        }

        _cachedTodayBestDateKey = string.IsNullOrWhiteSpace(response.dateKey) ? GetLocalBeijingDateKey() : response.dateKey;
        _cachedTodayBestScore = Mathf.Max(0, response.todayBestScore);
        _hasCachedTodayBestScore = true;
    }

    /// <summary>
    /// 尝试直接使用 10 分钟内的排行榜响应缓存。
    /// </summary>
    /// <param name="onSuccess">缓存命中时的成功回调。</param>
    /// <returns>true 表示已使用缓存，不需要请求云函数。</returns>
    private bool TryUseCachedLeaderboardResponse(Action<DailyChallengeLeaderboardResponse> onSuccess)
    {
        if (_forceRefreshLeaderboardOnNextLoad || _cachedLeaderboardResponse == null)
        {
            return false;
        }

        string localDateKey = GetLocalBeijingDateKey();
        if (!string.Equals(_cachedLeaderboardResponseDateKey, localDateKey, StringComparison.Ordinal))
        {
            return false;
        }

        long elapsedTicks = DateTime.UtcNow.Ticks - _cachedLeaderboardResponseUtcTicks;
        if (elapsedTicks < 0 || elapsedTicks > LeaderboardResponseCacheValidTicks)
        {
            return false;
        }

        onSuccess?.Invoke(_cachedLeaderboardResponse);
        return true;
    }

    /// <summary>
    /// 缓存一次完整排行榜响应。
    /// </summary>
    /// <param name="response">成功拉取到的排行榜响应。</param>
    private void CacheLeaderboardResponse(DailyChallengeLeaderboardResponse response)
    {
        if (response == null || !response.ok)
        {
            return;
        }

        _cachedLeaderboardResponse = response;
        _cachedLeaderboardResponseDateKey = string.IsNullOrWhiteSpace(response.dateKey) ? GetLocalBeijingDateKey() : response.dateKey;
        _cachedLeaderboardResponseUtcTicks = DateTime.UtcNow.Ticks;
    }

    /// <summary>
    /// 判断本次提交成功后是否需要让下次打开排行榜绕过 10 分钟缓存。
    /// </summary>
    /// <param name="response">提交成功后服务端返回的排行榜响应。</param>
    /// <param name="submittedScore">本局提交分数。</param>
    /// <param name="hasCachedTodayBestBeforeSubmit">提交前是否存在本地当天今日最高分缓存。</param>
    /// <param name="cachedTodayBestBeforeSubmit">提交前缓存的今日最高分。</param>
    /// <returns>true 表示本局按客户端缓存判断刷新了今日记录。</returns>
    private static bool ShouldForceRefreshAfterSubmit(DailyChallengeLeaderboardResponse response, int submittedScore, bool hasCachedTodayBestBeforeSubmit, int cachedTodayBestBeforeSubmit)
    {
        if (response == null || submittedScore <= 0 || !hasCachedTodayBestBeforeSubmit)
        {
            return false;
        }

        if (submittedScore <= cachedTodayBestBeforeSubmit)
        {
            return false;
        }

        return Mathf.Max(0, response.todayBestScore) >= submittedScore;
    }

    /// <summary>
    /// 获取本地北京时间日期键。
    /// 只用于提交前缓存有效性判断；真正排行榜日期仍以云函数返回 dateKey 为准。
    /// </summary>
    /// <returns>yyyyMMdd 日期键。</returns>
    private static string GetLocalBeijingDateKey()
    {
        return DateTime.UtcNow.AddHours(8).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 解析排行榜响应 JSON。
    /// </summary>
    /// <param name="responseJson">云函数返回 JSON。</param>
    /// <returns>解析后的响应对象；失败返回 null。</returns>
    private static DailyChallengeLeaderboardResponse ParseResponse(string responseJson)
    {
        if (string.IsNullOrWhiteSpace(responseJson))
        {
            return null;
        }

        try
        {
            DailyChallengeLeaderboardResponse response = JsonUtility.FromJson<DailyChallengeLeaderboardResponse>(responseJson);
            if (response != null)
            {
                response.entries = response.entries ?? Array.Empty<DailyChallengeLeaderboardEntry>();
            }

            return response;
        }
        catch (Exception exception)
        {
            Log.Warning("DailyChallengeLeaderboardModule 解析响应失败：{0}", exception.Message);
            return null;
        }
    }

    /// <summary>
    /// 创建编辑器与非微信环境的本地响应。
    /// </summary>
    /// <param name="action">业务动作名。</param>
    /// <param name="payload">业务附加参数。</param>
    /// <returns>本地响应对象。</returns>
    private static DailyChallengeLeaderboardResponse CreateLocalResponse(string action, Dictionary<string, object> payload)
    {
        int score = 0;
        if (payload != null && payload.TryGetValue("score", out object scoreObject) && scoreObject != null)
        {
            int.TryParse(Convert.ToString(scoreObject, CultureInfo.InvariantCulture), NumberStyles.Integer, CultureInfo.InvariantCulture, out score);
        }

        int historicalBestScore = GameEntry.Fruits != null ? GameEntry.Fruits.DailyChallengeHistoricalBestScore : 0;
        string historicalBestTime = GameEntry.Fruits != null ? GameEntry.Fruits.DailyChallengeHistoricalBestTime ?? string.Empty : string.Empty;
        DailyChallengeLeaderboardEntry myEntry = null;
        DailyChallengeLeaderboardEntry[] entries = Array.Empty<DailyChallengeLeaderboardEntry>();
        if (string.Equals(action, SubmitScoreAction, StringComparison.Ordinal) && score > 0)
        {
            if (score > historicalBestScore)
            {
                historicalBestScore = score;
                historicalBestTime = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
                GameEntry.Fruits?.ApplyDailyChallengeHistoricalBestScore(historicalBestScore, historicalBestTime);
            }

            myEntry = new DailyChallengeLeaderboardEntry
            {
                rank = 1,
                openid = "local_editor",
                playerName = GameEntry.Fruits != null ? GameEntry.Fruits.PlayerName ?? string.Empty : string.Empty,
                headPortraitCode = GameEntry.Fruits != null ? GameEntry.Fruits.SelectedHeadPortraitCode ?? string.Empty : string.Empty,
                headPortraitFrameCode = GameEntry.Fruits != null ? GameEntry.Fruits.SelectedHeadPortraitFrameCode ?? string.Empty : string.Empty,
                score = score,
                scoreAchievedAt = historicalBestTime
            };
            entries = new[] { myEntry };
        }

        return new DailyChallengeLeaderboardResponse
        {
            ok = true,
            openid = "local_editor",
            dateKey = DateTime.UtcNow.AddHours(8).ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            entries = entries,
            myEntry = myEntry,
            todayBestScore = myEntry != null ? myEntry.score : 0,
            historicalBestScore = historicalBestScore,
            historicalBestTime = historicalBestTime
        };
    }

#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
    /// <summary>
    /// 确保微信云开发能力已经初始化。
    /// </summary>
    /// <param name="onReady">初始化完成回调。</param>
    /// <param name="onFailure">初始化失败回调。</param>
    private void EnsureWechatCloudInitialized(Action onReady, Action<string> onFailure)
    {
        if (_isWechatCloudInitialized)
        {
            onReady?.Invoke();
            return;
        }

        WX.InitSDK(code =>
        {
            try
            {
                ICloudConfig cloudConfig = new ICloudConfig
                {
                    env = string.IsNullOrWhiteSpace(CloudEnvironmentId) ? "_default_" : CloudEnvironmentId,
                    traceUser = false
                };
                WX.cloud.Init(cloudConfig);
                _isWechatCloudInitialized = true;
                onReady?.Invoke();
            }
            catch (Exception exception)
            {
                onFailure?.Invoke(exception.Message);
            }
        });
    }

    /// <summary>
    /// 在微信小游戏环境执行云函数调用。
    /// </summary>
    /// <param name="requestData">云函数根级参数。</param>
    /// <param name="onSuccess">成功回调。</param>
    /// <param name="onFailure">失败回调。</param>
    private static void ExecuteWechatCloudFunction(Dictionary<string, object> requestData, Action<string> onSuccess, Action<string> onFailure)
    {
        WX.cloud.CallFunction(new CallFunctionParam
        {
            name = CloudFunctionName,
            data = requestData,
            success = response =>
            {
                onSuccess?.Invoke(response != null ? response.result : null);
            },
            fail = error =>
            {
                onFailure?.Invoke(error != null ? error.errMsg : "wx.cloud.CallFunction fail");
            }
        });
    }
#endif
}

/// <summary>
/// 每日一关排行榜响应。
/// </summary>
[Serializable]
public sealed class DailyChallengeLeaderboardResponse
{
    /// <summary>
    /// 本次请求是否成功。
    /// </summary>
    public bool ok;

    /// <summary>
    /// 错误信息。
    /// </summary>
    public string errMsg = string.Empty;

    /// <summary>
    /// 当前玩家 openid。
    /// </summary>
    public string openid = string.Empty;

    /// <summary>
    /// 当前榜单日期键。
    /// </summary>
    public string dateKey = string.Empty;

    /// <summary>
    /// 今日前 100 名。
    /// </summary>
    public DailyChallengeLeaderboardEntry[] entries = Array.Empty<DailyChallengeLeaderboardEntry>();

    /// <summary>
    /// 当前玩家今日记录。
    /// </summary>
    public DailyChallengeLeaderboardEntry myEntry;

    /// <summary>
    /// 当前玩家今日最高分。
    /// </summary>
    public int todayBestScore;

    /// <summary>
    /// 当前玩家历史最高分。
    /// </summary>
    public int historicalBestScore;

    /// <summary>
    /// 当前玩家历史最高分达成时间。
    /// </summary>
    public string historicalBestTime = string.Empty;
}

/// <summary>
/// 每日一关排行榜单条记录。
/// </summary>
[Serializable]
public sealed class DailyChallengeLeaderboardEntry
{
    /// <summary>
    /// 当前名次。
    /// 大于 0 表示进入前 100，等于 0 表示当前玩家有今日成绩但未进入前 100。
    /// </summary>
    public int rank;

    /// <summary>
    /// 玩家 openid。
    /// </summary>
    public string openid = string.Empty;

    /// <summary>
    /// 玩家显示名。
    /// </summary>
    public string playerName = string.Empty;

    /// <summary>
    /// 头像 Code。
    /// </summary>
    public string headPortraitCode = string.Empty;

    /// <summary>
    /// 头像框 Code。
    /// </summary>
    public string headPortraitFrameCode = string.Empty;

    /// <summary>
    /// 今日最高分。
    /// </summary>
    public int score;

    /// <summary>
    /// 分数达成时间。
    /// </summary>
    public string scoreAchievedAt = string.Empty;
}
