using System;
using System.Collections;
using GameFramework;
using UnityEngine;
using UnityEngine.Networking;
using UnityGameFramework.Runtime;

/// <summary>
/// Addressables 远程资源版本解析器。
/// 作用：启动阶段读取 CDN 上的 version.json，决定本次运行是否需要额外加载一个远程 catalog。
/// 内置 3 次指数退避重试，覆盖弱网 / CDN 抖动 / HTTP/2 协商失败等 transient 错误，
/// 真正的"资源缺失"（404）会立即停止重试，避免徒增启动延迟。
/// </summary>
public static class AddressablesRemoteVersionResolver
{
    /// <summary>
    /// CDN 上固定的资源版本入口文件地址。
    /// 初始状态：每次启动都会尝试读取；读取失败时不影响包内默认 catalog。
    /// </summary>
    private const string VersionManifestUrl = "https://7469-tianxing-001-2g9lrxwh45e5182d-1385561715.tcb.qcloud.la/ServerData_sgdd/WebGL/version.json";

    /// <summary>
    /// version.json 单次请求超时时间，单位为秒。
    /// 初始状态：5 秒；弱网下超时后立即进入重试或回退，避免首屏长时间卡住。
    /// </summary>
    private const int RequestTimeoutSeconds = 5;

    /// <summary>
    /// 最大重试次数（包含首次请求）。
    /// 3 次合计最坏耗时约 5 + 0.5 + 5 + 1.5 + 5 ≈ 17 秒（含等待与超时）。
    /// </summary>
    private const int MaxAttemptCount = 3;

    /// <summary>
    /// 重试基础退避时间，单位为秒。
    /// 第 N 次重试前等待 InitialBackoffSeconds * BackoffGrowthFactor^(N-1) 秒，N 从 1 开始。
    /// </summary>
    private const float InitialBackoffSeconds = 0.5f;

    /// <summary>
    /// 退避增长因子。
    /// 1.5 倍递增，等待序列：0.5s → 0.75s（实际只用到第二次重试，0.5s）。
    /// </summary>
    private const float BackoffGrowthFactor = 1.5f;

    /// <summary>
    /// 协程驱动器单例。
    /// 全局只创建一个 GameObject，DontDestroyOnLoad 保证场景切换不丢失。
    /// </summary>
    private static RetryDriver s_retryDriver;

    /// <summary>
    /// 发起远程资源版本解析。
    /// 接口签名保持不变，调用方零感知；内部用协程做带退避的多次重试。
    /// </summary>
    /// <param name="onComplete">解析完成回调；成功时携带 catalogUrl，失败时携带回退原因。</param>
    public static void BeginResolve(Action<AddressablesRemoteVersionResolveResult> onComplete)
    {
        EnsureRetryDriver();
        s_retryDriver.StartCoroutine(ResolveWithRetryCoroutine(onComplete));
    }

    /// <summary>
    /// 带重试的 version.json 请求协程。
    /// 设计要点：
    /// 　1) 用 do-while 控制重试次数，最多 MaxAttemptCount 次；
    /// 　2) HTTP 4xx（含 404）不重试，仅 ConnectionError / 5xx / DataProcessingError 才重试；
    /// 　3) 每次重试前按指数退避 sleep，避免雪崩；
    /// 　4) 任意一次成功立即派发结果。
    /// </summary>
    /// <param name="onComplete">解析完成回调。</param>
    private static IEnumerator ResolveWithRetryCoroutine(Action<AddressablesRemoteVersionResolveResult> onComplete)
    {
        AddressablesRemoteVersionResolveResult lastResult = null;
        for (int attempt = 1; attempt <= MaxAttemptCount; attempt++)
        {
            string requestUrl = BuildRequestUrl();
            UnityWebRequest request = UnityWebRequest.Get(requestUrl);
            request.timeout = RequestTimeoutSeconds;

            // SendWebRequest 在极少数极端情况下同步抛异常（例如 URL 非法），用 try 兜底。
            UnityWebRequestAsyncOperation operation = null;
            Exception sendException = null;
            try
            {
                operation = request.SendWebRequest();
            }
            catch (Exception ex)
            {
                sendException = ex;
            }

            if (sendException != null)
            {
                request.Dispose();
                lastResult = AddressablesRemoteVersionResolveResult.Fallback(
                    "version.json 请求启动异常：" + sendException.Message);
                Log.Warning(Utility.Text.Format(
                    "[AddressablesRemoteVersion] 第 {0}/{1} 次请求 version.json 启动失败：{2}",
                    attempt, MaxAttemptCount, sendException.Message));
                if (TryWaitForRetry(attempt, out IEnumerator wait))
                {
                    yield return wait;
                    continue;
                }
                break;
            }

            // 等待请求完成；UnityWebRequestAsyncOperation 可被直接 yield。
            yield return operation;

            ParseResultOutcome parseOutcome = ParseRequestResultDetailed(request);
            request.Dispose();

            // 成功直接派发并退出。
            // 「成功」包括两种语义：拿到远程 catalog 可加载（HasRemoteCatalog），
            // 或仅拿到 resourceVersion 用于缓存版本比对（HasResourceVersion）。
            // 后者对应"工程关闭 BuildRemoteCatalog、version.json 仅下发 resourceVersion"的合法场景，
            // 不应该被当作失败重试，更不该打 Warning。
            if (parseOutcome.Result.HasRemoteCatalog || parseOutcome.Result.HasResourceVersion)
            {
                lastResult = parseOutcome.Result;
                break;
            }

            // 不可重试的失败（404 / JSON 非法 / 字段缺失）：立即退出循环用最后结果回退。
            lastResult = parseOutcome.Result;
            if (!parseOutcome.IsRetryable)
            {
                Log.Warning(Utility.Text.Format(
                    "[AddressablesRemoteVersion] 第 {0}/{1} 次请求 version.json 失败且不可重试，原因：{2}",
                    attempt, MaxAttemptCount, lastResult.Reason));
                break;
            }

            Log.Warning(Utility.Text.Format(
                "[AddressablesRemoteVersion] 第 {0}/{1} 次请求 version.json 失败，准备重试。原因：{2}",
                attempt, MaxAttemptCount, lastResult.Reason));

            // 还有机会重试 → 退避后继续；否则退出循环走 fallback。
            if (TryWaitForRetry(attempt, out IEnumerator backoff))
            {
                yield return backoff;
                continue;
            }
        }

        // 兜底：理论上 lastResult 不会为 null（循环至少执行一次），双保险。
        if (lastResult == null)
        {
            lastResult = AddressablesRemoteVersionResolveResult.Fallback("version.json 请求未产生任何结果。");
        }

        Complete(onComplete, lastResult);
    }

    /// <summary>
    /// 计算第 N 次重试前的退避秒数。
    /// </summary>
    /// <param name="finishedAttempt">刚刚结束的尝试编号（从 1 开始）。</param>
    /// <param name="backoff">协程层 yield 用的等待迭代器。</param>
    /// <returns>仍可重试时返回 true。</returns>
    private static bool TryWaitForRetry(int finishedAttempt, out IEnumerator backoff)
    {
        backoff = null;
        if (finishedAttempt >= MaxAttemptCount)
        {
            return false;
        }

        // 第 1 次失败后等 InitialBackoffSeconds，第 2 次失败后等 InitialBackoffSeconds * BackoffGrowthFactor，依此类推。
        float waitSeconds = InitialBackoffSeconds * Mathf.Pow(BackoffGrowthFactor, finishedAttempt - 1);
        backoff = new WaitForSecondsRealtime(waitSeconds);
        return true;
    }

    /// <summary>
    /// 构建带防缓存参数的 version.json 请求地址。
    /// 时间戳每次都不同，确保 CDN 中间节点不会返回旧缓存。
    /// </summary>
    /// <returns>包含 app 版本与时间戳查询参数的 URL。</returns>
    private static string BuildRequestUrl()
    {
        string separator = VersionManifestUrl.IndexOf("?", StringComparison.Ordinal) >= 0 ? "&" : "?";
        string appVersion = UnityWebRequest.EscapeURL(Application.version);
        string timestamp = DateTime.UtcNow.Ticks.ToString();
        return VersionManifestUrl + separator + "app=" + appVersion + "&ts=" + timestamp;
    }

    /// <summary>
    /// 把 UnityWebRequest 的结果转换为远程版本解析结果，并标记是否值得重试。
    /// 区分 transient 失败（值得重试）与 permanent 失败（立即放弃）的核心逻辑。
    /// </summary>
    /// <param name="request">已经完成的 version.json 请求。</param>
    /// <returns>包含解析结果与"是否可重试"标记的复合对象。</returns>
    private static ParseResultOutcome ParseRequestResultDetailed(UnityWebRequest request)
    {
        if (request.result != UnityWebRequest.Result.Success)
        {
            // HTTP 4xx 视为 permanent：404/403/401 等都是 CDN 上没东西或权限问题，重试也没用。
            // 5xx / 0（无响应）/ ConnectionError / DataProcessingError 全部视为 transient，可重试。
            long responseCode = request.responseCode;
            bool isPermanentFailure = responseCode >= 400 && responseCode < 500;
            return new ParseResultOutcome
            {
                Result = AddressablesRemoteVersionResolveResult.Fallback(
                    "version.json 请求失败，result=" + request.result +
                    "，httpCode=" + responseCode + "，error=" + request.error),
                IsRetryable = !isPermanentFailure
            };
        }

        string json = request.downloadHandler != null ? request.downloadHandler.text : null;
        if (string.IsNullOrEmpty(json))
        {
            // 服务端正常返回但 body 为空：可能是 CDN 中间节点抖动，重试一次有意义。
            return new ParseResultOutcome
            {
                Result = AddressablesRemoteVersionResolveResult.Fallback("version.json 内容为空。"),
                IsRetryable = true
            };
        }

        RemoteVersionManifestData manifest;
        try
        {
            manifest = JsonUtility.FromJson<RemoteVersionManifestData>(json);
        }
        catch (Exception ex)
        {
            // JSON 损坏属于 permanent，重试同样的损坏内容毫无意义。
            return new ParseResultOutcome
            {
                Result = AddressablesRemoteVersionResolveResult.Fallback("version.json 解析异常：" + ex.Message),
                IsRetryable = false
            };
        }

        if (manifest == null)
        {
            return new ParseResultOutcome
            {
                Result = AddressablesRemoteVersionResolveResult.Fallback("version.json 解析结果为空。"),
                IsRetryable = false
            };
        }

        if (string.IsNullOrEmpty(manifest.catalogUrl))
        {
            // 不再把 catalogUrl 缺失视为失败：当前工程关闭了 BuildRemoteCatalog，
            // 所有资源信息走包内 catalog，CDN 上压根没有 catalog_xxx.json 文件。
            // 只要拿到 resourceVersion 就足以驱动版本切换和缓存清理。
            return new ParseResultOutcome
            {
                Result = AddressablesRemoteVersionResolveResult.VersionOnly(manifest.resourceVersion),
                IsRetryable = false
            };
        }

        return new ParseResultOutcome
        {
            Result = AddressablesRemoteVersionResolveResult.RemoteCatalog(manifest.catalogUrl, manifest.resourceVersion),
            IsRetryable = false
        };
    }

    /// <summary>
    /// 持久化"上次成功使用的 resourceVersion"用 PlayerPrefs key。
    /// 在微信小游戏底层映射到 wx.setStorageSync，跨启动保留；首次启动取空串，等价于"必清一次"。
    /// </summary>
    private const string PrefsKeyResourceVersion = "wx.cache.lastResourceVersion";

    /// <summary>
    /// 安全派发解析完成回调，并在版本号变化时主动清理微信文件缓存。
    /// 设计要点：
    /// 　1) 只要拿到 resourceVersion 就参与版本比对，不要求一定拿到远程 catalog；
    /// 　2) 清缓存是异步调用，不阻塞 onComplete 派发，避免首屏被卡；
    /// 　3) 本次启动依旧走老缓存（清缓存是为下次启动生效），符合"小步换新"安全策略；
    /// 　4) 编辑器与非微信平台 WechatBundleCacheUtility 内部 no-op，调用方零分支。
    /// </summary>
    /// <param name="onComplete">外部传入的解析完成回调。</param>
    /// <param name="result">本次解析结果。</param>
    private static void Complete(Action<AddressablesRemoteVersionResolveResult> onComplete, AddressablesRemoteVersionResolveResult result)
    {
        if (result.HasRemoteCatalog)
        {
            Log.Info("[AddressablesRemoteVersion] 使用远程资源版本，resourceVersion=" + result.ResourceVersion + "，catalogUrl=" + result.CatalogUrl);
        }
        else if (result.HasResourceVersion)
        {
            Log.Info("[AddressablesRemoteVersion] 仅获取到资源版本号（无远程 catalog），resourceVersion=" + result.ResourceVersion + "，沿用包内默认 catalog。");
        }
        else
        {
            Log.Warning("[AddressablesRemoteVersion] 使用包内默认 Addressables catalog。原因：" + result.Reason);
        }

        // 只要服务端下发了 resourceVersion 就驱动版本比对，与 catalogUrl 是否存在解耦。
        TryCleanWechatCacheOnVersionChanged(result.ResourceVersion);

        onComplete?.Invoke(result);
    }

    /// <summary>
    /// 仅在 resourceVersion 与上次记录不一致时，触发一次微信文件缓存全量清理。
    /// 注意：
    /// 　- 服务端 version.json 的 resourceVersion 字段必须保证"小版本递增 / 资源变化即变更"，否则清缓存不生效；
    /// 　- 第一次上线该逻辑时，所有老用户 lastVersion 为空字符串，必触发一次清理 → 弱网下首启会重下，属正常阵痛；
    /// 　- 异常路径下 PersistentKv.SetString 失败可能导致下次启动重复清一次，可接受。
    /// 　- 持久化必须走 PersistentKv 而非 PlayerPrefs：微信小游戏 webview 禁用了 IndexedDB，
    ///     vconsole 启动会打 "IndexedDB is not available. Data will not persist..."，
    ///     PlayerPrefs 写入永远不会落盘。PersistentKv 在小游戏侧改走 wx.setStorageSync / wx.getStorageSync，跨启动稳定持久化。
    /// </summary>
    /// <param name="currentResourceVersion">本次 version.json 解析得到的资源版本号；空字符串视为"未知"，不触发清理。</param>
    private static void TryCleanWechatCacheOnVersionChanged(string currentResourceVersion)
    {
        if (string.IsNullOrEmpty(currentResourceVersion))
        {
            // 服务端没下发版本号 → 不掌握任何切换证据，保守不清，防止误删。
            return;
        }

        string lastResourceVersion = PersistentKv.GetString(PrefsKeyResourceVersion, string.Empty);
        if (string.Equals(lastResourceVersion, currentResourceVersion, StringComparison.Ordinal))
        {
            // 版本未变化 → 缓存仍然有效，按命中走，不触发任何 IO。
            return;
        }

        Log.Warning(Utility.Text.Format(
            "[AddressablesRemoteVersion] resourceVersion 变化：{0} -> {1}，触发 WX.CleanAllFileCache。",
            string.IsNullOrEmpty(lastResourceVersion) ? "<empty>" : lastResourceVersion,
            currentResourceVersion));

        // ⚠️ 必须先写 KV：清缓存是 fire-and-forget 异步调用，
        //    若先等回调再写 KV，本次进程被强杀（玩家划掉小游戏）会导致下次启动再清一次。
        //    先写 KV 等价于"本次决意切换版本"，即使清失败也不会陷入死循环。
        PersistentKv.SetString(PrefsKeyResourceVersion, currentResourceVersion);

        WechatBundleCacheUtility.CleanAll(null);
    }

    /// <summary>
    /// 确保协程驱动器单例存在。
    /// 必须在主线程调用，BeginResolve 由启动流程触发，天然在主线程。
    /// </summary>
    private static void EnsureRetryDriver()
    {
        if (s_retryDriver != null)
        {
            return;
        }

        GameObject driverGo = new GameObject("[AddressablesRemoteVersion.RetryDriver]");
        UnityEngine.Object.DontDestroyOnLoad(driverGo);
        // hideFlags 设置成 HideAndDontSave，避免出现在 Hierarchy 干扰策划查看。
        driverGo.hideFlags = HideFlags.HideAndDontSave;
        s_retryDriver = driverGo.AddComponent<RetryDriver>();
    }

    /// <summary>
    /// version.json 的 JSON 数据结构。
    /// 字段名必须与 CDN 上的 JSON 保持一致，供 Unity JsonUtility 直接反序列化。
    /// </summary>
    [Serializable]
    private sealed class RemoteVersionManifestData
    {
        /// <summary>
        /// 远程资源版本号；初始状态允许为空，仅用于日志与运维识别。
        /// </summary>
        public string resourceVersion;

        /// <summary>
        /// 需要额外加载的 Addressables catalog 完整 URL；初始状态必须配置，否则回退包内默认 catalog。
        /// </summary>
        public string catalogUrl;
    }

    /// <summary>
    /// 解析一次 UnityWebRequest 结果的复合输出。
    /// </summary>
    private struct ParseResultOutcome
    {
        /// <summary>
        /// 解析得到的版本结果（成功 or fallback）。
        /// </summary>
        public AddressablesRemoteVersionResolveResult Result;

        /// <summary>
        /// 当前失败是否值得重试。
        /// 仅在 Result.HasRemoteCatalog == false 时才被检查；成功结果忽略此字段。
        /// </summary>
        public bool IsRetryable;
    }

    /// <summary>
    /// 协程驱动器：仅负责承载 StartCoroutine。
    /// 保持空 MonoBehaviour，无业务逻辑。
    /// </summary>
    private sealed class RetryDriver : MonoBehaviour
    {
    }
}

/// <summary>
/// Addressables 远程资源版本解析结果。
/// </summary>
public sealed class AddressablesRemoteVersionResolveResult
{
    /// <summary>
    /// 是否存在可加载的远程 catalog。
    /// 初始状态：false 时表示继续使用包内默认 catalog。
    /// </summary>
    public readonly bool HasRemoteCatalog;

    /// <summary>
    /// 是否已经成功拿到 resourceVersion。
    /// 初始状态：true 表示服务端下发了非空版本号，可参与版本比对与缓存清理；
    /// 与 HasRemoteCatalog 互不强约束：拿到版本号但没有远程 catalog 是合法状态。
    /// </summary>
    public readonly bool HasResourceVersion;

    /// <summary>
    /// 远程 catalog 完整 URL。
    /// 初始状态：仅 HasRemoteCatalog 为 true 时有效。
    /// </summary>
    public readonly string CatalogUrl;

    /// <summary>
    /// 远程资源版本号。
    /// 初始状态：HasResourceVersion 为 true 时非空，可作为 PlayerPrefs 比对键。
    /// </summary>
    public readonly string ResourceVersion;

    /// <summary>
    /// 回退包内默认 catalog 的原因。
    /// 初始状态：仅在 HasRemoteCatalog 与 HasResourceVersion 均为 false 时才有意义。
    /// </summary>
    public readonly string Reason;

    /// <summary>
    /// 创建远程版本解析结果。
    /// </summary>
    /// <param name="hasRemoteCatalog">是否存在可加载的远程 catalog。</param>
    /// <param name="catalogUrl">远程 catalog 完整 URL。</param>
    /// <param name="resourceVersion">远程资源版本号。</param>
    /// <param name="reason">回退包内默认 catalog 的原因。</param>
    private AddressablesRemoteVersionResolveResult(bool hasRemoteCatalog, string catalogUrl, string resourceVersion, string reason)
    {
        HasRemoteCatalog = hasRemoteCatalog;
        CatalogUrl = catalogUrl;
        ResourceVersion = resourceVersion;
        Reason = reason;
        HasResourceVersion = !string.IsNullOrEmpty(resourceVersion);
    }

    /// <summary>
    /// 创建远程 catalog 可用结果。
    /// </summary>
    /// <param name="catalogUrl">远程 catalog 完整 URL。</param>
    /// <param name="resourceVersion">远程资源版本号。</param>
    /// <returns>可被 Addressables.LoadContentCatalogAsync 使用的解析结果。</returns>
    public static AddressablesRemoteVersionResolveResult RemoteCatalog(string catalogUrl, string resourceVersion)
    {
        return new AddressablesRemoteVersionResolveResult(true, catalogUrl, resourceVersion, null);
    }

    /// <summary>
    /// 创建"仅版本号、无远程 catalog"结果。
    /// 用于工程关闭 BuildRemoteCatalog 的场景：CDN 上不会产出 catalog_xxx.json，
    /// version.json 只用来下发 resourceVersion 触发缓存清理与热更感知。
    /// </summary>
    /// <param name="resourceVersion">远程资源版本号；空字符串视为"未下发"，调用方应改用 Fallback。</param>
    /// <returns>HasResourceVersion = true、HasRemoteCatalog = false 的解析结果。</returns>
    public static AddressablesRemoteVersionResolveResult VersionOnly(string resourceVersion)
    {
        return new AddressablesRemoteVersionResolveResult(false, null, resourceVersion, null);
    }

    /// <summary>
    /// 创建回退包内默认 catalog 的结果。
    /// </summary>
    /// <param name="reason">回退原因。</param>
    /// <returns>表示不加载额外远程 catalog 的解析结果。</returns>
    public static AddressablesRemoteVersionResolveResult Fallback(string reason)
    {
        return new AddressablesRemoteVersionResolveResult(false, null, null, reason);
    }
}
