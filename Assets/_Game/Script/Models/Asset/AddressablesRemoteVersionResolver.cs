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
    private const string VersionManifestUrl = "https://7469-tianxing-001-2g9lrxwh45e5182d-1385561715.tcb.qcloud.la/ServerData_sgdd/version.json";

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
            if (parseOutcome.Result.HasRemoteCatalog)
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
            return new ParseResultOutcome
            {
                Result = AddressablesRemoteVersionResolveResult.Fallback("version.json 未配置 catalogUrl。"),
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
    /// 安全派发解析完成回调。
    /// </summary>
    /// <param name="onComplete">外部传入的解析完成回调。</param>
    /// <param name="result">本次解析结果。</param>
    private static void Complete(Action<AddressablesRemoteVersionResolveResult> onComplete, AddressablesRemoteVersionResolveResult result)
    {
        if (result.HasRemoteCatalog)
        {
            Log.Info("[AddressablesRemoteVersion] 使用远程资源版本，resourceVersion=" + result.ResourceVersion + "，catalogUrl=" + result.CatalogUrl);
        }
        else
        {
            Log.Warning("[AddressablesRemoteVersion] 使用包内默认 Addressables catalog。原因：" + result.Reason);
        }

        onComplete?.Invoke(result);
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
    /// 远程 catalog 完整 URL。
    /// 初始状态：仅 HasRemoteCatalog 为 true 时有效。
    /// </summary>
    public readonly string CatalogUrl;

    /// <summary>
    /// 远程资源版本号。
    /// 初始状态：仅用于日志输出，可为空。
    /// </summary>
    public readonly string ResourceVersion;

    /// <summary>
    /// 回退包内默认 catalog 的原因。
    /// 初始状态：仅 HasRemoteCatalog 为 false 时有效。
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
    /// 创建回退包内默认 catalog 的结果。
    /// </summary>
    /// <param name="reason">回退原因。</param>
    /// <returns>表示不加载额外远程 catalog 的解析结果。</returns>
    public static AddressablesRemoteVersionResolveResult Fallback(string reason)
    {
        return new AddressablesRemoteVersionResolveResult(false, null, null, reason);
    }
}
