using System;
using UnityEngine;
using UnityEngine.Networking;
using UnityGameFramework.Runtime;

/// <summary>
/// Addressables 远程资源版本解析器。
/// 作用：启动阶段读取 CDN 上的 version.json，决定本次运行是否需要额外加载一个远程 catalog。
/// </summary>
public static class AddressablesRemoteVersionResolver
{
    /// <summary>
    /// CDN 上固定的资源版本入口文件地址。
    /// 初始状态：每次启动都会尝试读取；读取失败时不影响包内默认 catalog。
    /// </summary>
    private const string VersionManifestUrl = "https://7469-tianxing-001-2g9lrxwh45e5182d-1385561715.tcb.qcloud.la/ServerData_sgdd/version.json";

    /// <summary>
    /// version.json 请求超时时间，单位为秒。
    /// 初始状态：5 秒；弱网下超时后立即回退包内默认 catalog，避免首屏长时间卡住。
    /// </summary>
    private const int RequestTimeoutSeconds = 5;

    /// <summary>
    /// 发起远程资源版本解析。
    /// </summary>
    /// <param name="onComplete">解析完成回调；成功时携带 catalogUrl，失败时携带回退原因。</param>
    public static void BeginResolve(Action<AddressablesRemoteVersionResolveResult> onComplete)
    {
        string requestUrl = BuildRequestUrl();
        UnityWebRequest request = UnityWebRequest.Get(requestUrl);
        request.timeout = RequestTimeoutSeconds;

        UnityWebRequestAsyncOperation operation;
        try
        {
            operation = request.SendWebRequest();
        }
        catch (Exception ex)
        {
            request.Dispose();
            Complete(onComplete, AddressablesRemoteVersionResolveResult.Fallback("version.json 请求启动异常：" + ex.Message));
            return;
        }

        operation.completed += _ =>
        {
            AddressablesRemoteVersionResolveResult result = ParseRequestResult(request);
            request.Dispose();
            Complete(onComplete, result);
        };
    }

    /// <summary>
    /// 构建带防缓存参数的 version.json 请求地址。
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
    /// 将 UnityWebRequest 的结果转换为远程版本解析结果。
    /// </summary>
    /// <param name="request">已经完成的 version.json 请求。</param>
    /// <returns>成功时返回远程 catalog 信息；失败时返回回退原因。</returns>
    private static AddressablesRemoteVersionResolveResult ParseRequestResult(UnityWebRequest request)
    {
        if (request.result != UnityWebRequest.Result.Success)
        {
            return AddressablesRemoteVersionResolveResult.Fallback(
                "version.json 请求失败，result=" + request.result + "，error=" + request.error);
        }

        string json = request.downloadHandler != null ? request.downloadHandler.text : null;
        if (string.IsNullOrEmpty(json))
        {
            return AddressablesRemoteVersionResolveResult.Fallback("version.json 内容为空。");
        }

        RemoteVersionManifestData manifest;
        try
        {
            manifest = JsonUtility.FromJson<RemoteVersionManifestData>(json);
        }
        catch (Exception ex)
        {
            return AddressablesRemoteVersionResolveResult.Fallback("version.json 解析异常：" + ex.Message);
        }

        if (manifest == null)
        {
            return AddressablesRemoteVersionResolveResult.Fallback("version.json 解析结果为空。");
        }

        if (string.IsNullOrEmpty(manifest.catalogUrl))
        {
            return AddressablesRemoteVersionResolveResult.Fallback("version.json 未配置 catalogUrl。");
        }

        return AddressablesRemoteVersionResolveResult.RemoteCatalog(manifest.catalogUrl, manifest.resourceVersion);
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
