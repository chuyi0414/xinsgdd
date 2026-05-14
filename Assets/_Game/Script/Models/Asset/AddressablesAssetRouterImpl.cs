using System;
using System.Collections.Generic;
using GameFramework;
using GameFramework.Resource;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityGameFramework.Runtime;

/// <summary>
/// 项目层 Addressables 资源路由实现。
/// 注入到 GF 内核 ResourceManager 后，所有走 GF LoadAsset 的请求（UI/Entity/DataTable/Sound 全包）
/// 会先经过本路由判定：命中 Addressables 群组 → 走 Addressables 异步加载并按 LoadAssetCallbacks 协议派发；
/// 未命中 → 返回 false 让 GF 主链路继续走 Resources.LoadAsync。
/// 业务侧零感知，路径字符串两边复用。
/// </summary>
public static class AddressablesAssetRouterImpl
{
    /// <summary>
    /// Addressables catalog 是否已经初始化完毕。
    /// 必须为 true 时路由才能命中：未初始化时 ResourceLocators 为空，Locate 永远返回 false，所有请求自动回退 GF 主链路。
    /// 由 GameEntry 在 Addressables.InitializeAsync 完成后置位。
    /// </summary>
    private static bool s_IsAddressablesReady;

    /// <summary>
    /// 是否已向 GF 内核注入路由（防止重复注入）。
    /// </summary>
    private static bool s_RouterInjected;

    /// <summary>
    /// 通过 version.json 追加加载成功的远程 catalog 定位器。
    /// 初始状态：null；加载成功后优先用于资源定位，确保远程资源版本覆盖包内默认 catalog。
    /// </summary>
    private static IResourceLocator s_RemoteCatalogLocator;

    /// <summary>
    /// Unity Addressables 内置 Resources 兼容 Provider 的完整标识。
    /// 初始状态：固定字符串；用于过滤掉会走 Resources.LoadAsync 的伪 Addressables 命中。
    /// </summary>
    private const string LegacyResourcesProviderId = "UnityEngine.ResourceManagement.ResourceProviders.LegacyResourcesProvider";

    /// <summary>
    /// catalog 是否已经准备好。
    /// 仅 GameEntry 启动逻辑读取，业务层不应依赖。
    /// </summary>
    public static bool IsAddressablesReady
    {
        get { return s_IsAddressablesReady; }
    }

    /// <summary>
    /// 启动 Addressables 初始化并注入路由。
    /// 推荐用法：
    /// <code>
    /// AddressablesAssetRouterImpl.BeginInitialize(onComplete: () =&gt; InitCustomComponents());
    /// </code>
    /// 在 InitializeAsync 完成前调用 LoadAsset 不会报错，但路由会全部 miss，请求自动走 GF 主链路。
    /// 完成后 IsAddressablesReady = true，后续命中 Addressables 群组的请求自动走 Addressables。
    /// 重入保护：catalog 已就绪时直接同步派发 onComplete，不重复调 InitializeAsync 避免状态抖动。
    /// </summary>
    /// <param name="onComplete">初始化完成回调，无论成功失败都会触发；可空。</param>
    public static void BeginInitialize(Action onComplete)
    {
        // 必须先把路由注入到 GF 内核，避免初始化期间 LoadAsset 找不到路由导致跑偏。
        // 此时 IsAddressablesReady == false，路由委托内部 Locate 必然返回 false，请求走 GF 主链路。
        InjectRouterIfNeeded();

        // ⚠️ 重入保护：catalog 已就绪时避免重复调 InitializeAsync。
        // 场景重加载 / GameEntry 被重新 Start 时，如果重调会导致 Completed 同步触发，
        // s_IsAddressablesReady 被重新赋值可能出现短暂状态抖动。
        if (s_IsAddressablesReady)
        {
            onComplete?.Invoke();
            return;
        }

        AsyncOperationHandle<IResourceLocator> initHandle = Addressables.InitializeAsync();
        initHandle.Completed += op =>
        {
            // 即使初始化失败也要继续：失败时 IsAddressablesReady 保持 false，所有请求走 GF 主链路兜底，避免业务卡死。
            if (op.Status != AsyncOperationStatus.Succeeded)
            {
                s_IsAddressablesReady = false;
                Log.Error("[AddressablesRouter] Addressables.InitializeAsync 失败，所有请求将退回到 Resources.LoadAsync 兜底。Error: "
                    + (op.OperationException != null ? op.OperationException.Message : "unknown"));
                onComplete?.Invoke();
                return;
            }

            BeginLoadRemoteVersionCatalog(onComplete);
        };
    }

    /// <summary>
    /// 尝试按 CDN 上的 version.json 追加加载远程 catalog。
    /// Editor Play Mode 默认跳过远程 catalog，让 Addressables Play Mode Script（Fastest / Use Asset Database）真正接管本地资源定位。
    /// </summary>
    /// <param name="onComplete">Addressables 完整初始化完成回调；无论远程 catalog 是否成功都会触发。</param>
    private static void BeginLoadRemoteVersionCatalog(Action onComplete)
    {
#if UNITY_EDITOR
        // 编辑器内调试资产时必须避免自动叠加 CDN catalog：
        // 1. Play Mode Script=Fastest 只影响 Addressables.InitializeAsync 生成的本地 locator；
        // 2. 如果这里继续 LoadContentCatalogAsync 远程 catalog，TryLocate 会优先命中 s_RemoteCatalogLocator；
        // 3. 结果就是本地材质 / SkeletonData 修改被远程 bundle 覆盖，表现为编辑器里仍然看到旧资源或粉色材质。
        // Player 包不走此分支，仍保留线上 version.json 热更新链路。
        s_RemoteCatalogLocator = null;
        Log.Info("[AddressablesRouter] Unity Editor Play Mode 跳过远程 Addressables catalog，使用本地 Addressables locator。");
        CompleteSuccessfulInitialize(onComplete);
        return;
#endif

        AddressablesRemoteVersionResolver.BeginResolve(result =>
        {
            if (!result.HasRemoteCatalog)
            {
                CompleteSuccessfulInitialize(onComplete);
                return;
            }

            AsyncOperationHandle<IResourceLocator> remoteCatalogHandle;
            try
            {
                remoteCatalogHandle = Addressables.LoadContentCatalogAsync(result.CatalogUrl, true);
            }
            catch (Exception ex)
            {
                Log.Error("[AddressablesRouter] LoadContentCatalogAsync 启动异常，将继续使用包内默认 catalog。catalogUrl='"
                    + result.CatalogUrl + "'，error='" + ex + "'。");
                CompleteSuccessfulInitialize(onComplete);
                return;
            }

            remoteCatalogHandle.Completed += op =>
            {
                if (op.Status == AsyncOperationStatus.Succeeded && op.Result != null)
                {
                    s_RemoteCatalogLocator = op.Result;
                    Log.Info("[AddressablesRouter] 远程 Addressables catalog 加载成功，resourceVersion='"
                        + result.ResourceVersion + "'，catalogUrl='" + result.CatalogUrl + "'。");
                }
                else
                {
                    Log.Error("[AddressablesRouter] 远程 Addressables catalog 加载失败，将继续使用包内默认 catalog。catalogUrl='"
                        + result.CatalogUrl + "'，error='"
                        + (op.OperationException != null ? op.OperationException.ToString() : "unknown") + "'。");
                }

                CompleteSuccessfulInitialize(onComplete);
            };
        });
    }

    /// <summary>
    /// 完成 Addressables 初始化并放行业务启动流程。
    /// </summary>
    /// <param name="onComplete">外部传入的初始化完成回调；可空。</param>
    private static void CompleteSuccessfulInitialize(Action onComplete)
    {
        s_IsAddressablesReady = true;
        onComplete?.Invoke();
    }

    /// <summary>
    /// 把路由委托注入到 GF 内核 ResourceManager。
    /// </summary>
    private static void InjectRouterIfNeeded()
    {
        if (s_RouterInjected)
        {
            return;
        }

        IResourceManager resourceManager = GameFrameworkEntry.GetModule<IResourceManager>();
        if (resourceManager == null)
        {
            Log.Error("[AddressablesRouter] GF IResourceManager 模块未就绪，无法注入路由。");
            return;
        }

        resourceManager.SetAddressablesAssetRouter(TryRoute);
        // 同时注入释放路由：GF 内核 UnloadAsset 入口会优先咨询该路由，
        // 命中 Addressables 资源时精确 Release 句柄并跳过 m_AssetPool.Unspawn（Addressables 资源未注册到对象池）。
        resourceManager.SetAddressablesAssetReleaseRouter(TryReleaseRoute);
        s_RouterInjected = true;
    }

    /// <summary>
    /// GF 内核 LoadAsset 入口注入的路由委托主体。
    /// 同步判定 Addressables 是否注册了 assetName：
    /// 1. catalog 未就绪 → 返回 false，请求走 GF 主链路；
    /// 2. ResourceLocators 全部 Locate 失败 → 返回 false，走 GF 主链路；
    /// 3. 任一 Locator 命中 → 发起 Addressables 异步加载，登记反查表，按 LoadAssetCallbacks 协议派发完成事件，返回 true。
    /// </summary>
    /// <param name="assetName">资源名（与 GF.LoadAsset 同名）。</param>
    /// <param name="assetType">资源类型，可空（GF 部分调用不传）。</param>
    /// <param name="loadAssetCallbacks">GF 加载回调集合。</param>
    /// <param name="userData">业务自定义数据，仅透传。</param>
    /// <returns>路由是否接管。</returns>
    private static bool TryRoute(string assetName, Type assetType, LoadAssetCallbacks loadAssetCallbacks, object userData)
    {
        if (!s_IsAddressablesReady)
        {
            AddressablesFallbackDiagnostic.SetReason(assetName, "Addressables catalog 尚未初始化完成，自动路由无法判定该资源，已回退到 Resources 后端。");
            return false;
        }

        if (string.IsNullOrEmpty(assetName))
        {
            return false;
        }

        // 同步判定 key 是否在 catalog 中：遍历每个 Locator 调 Locate，并过滤掉本质走 Resources.LoadAsync 的 LegacyResourcesProvider。
        if (!TryLocate(assetName, assetType, out IList<IResourceLocation> locations, out IResourceLocation selectedLocation, out int locatorCount))
        {
            string assetTypeName = GetAssetTypeName(assetType);
            AddressablesFallbackDiagnostic.SetReason(
                assetName,
                Utility.Text.Format(
                    "Addressables catalog 已就绪，但没有任何非 LegacyResourcesProvider 的 ResourceLocator 命中该 key。asset='{0}', assetType='{1}', locatorCount={2}。请检查 Addressables Address 是否与 GF 路径完全一致，以及资源是否已加入 Addressables Group。",
                    assetName,
                    assetTypeName,
                    locatorCount));
            return false;
        }

        DateTime startTime = DateTime.UtcNow;

        // 必须按 GF 传入的 assetType 发起 Addressables 泛型加载。
        // 不能统一用 UnityEngine.Object：Addressables 的 Built In Resources locator 会按类型过滤，
        // Sprite 请求如果被 Object 泛型重查，可能命中失败或返回非预期对象，导致业务侧 asset as Sprite 为 null。
        // ⚠️ 防御性 try-catch：理论上 Addressables.LoadAssetAsync 在 key 不存在时返回 Failed handle 而非报错，
        //     但 catalog 损坏 / 极端情况下可能同步抛异常；捕获后走 LoadAssetFailureCallback 避免 GF 调用栈中断。
        AsyncOperationHandle handle;
        try
        {
            handle = LoadAddressablesAsset(selectedLocation, assetType);
        }
        catch (Exception ex)
        {
            string errorMessage = ex.ToString();
            Log.Error(Utility.Text.Format(
                "[AddressablesRouter] LoadAssetAsync 同步抛异常，asset='{0}'，assetType='{1}'，error='{2}'。",
                assetName,
                GetAssetTypeName(assetType),
                errorMessage));
            if (loadAssetCallbacks.LoadAssetFailureCallback != null)
            {
                loadAssetCallbacks.LoadAssetFailureCallback(assetName, LoadResourceStatus.NotExist, errorMessage, userData);
            }
            return true; // 路由已声明接管（失败路径），不再走 GF 主链路。
        }

        handle.Completed += op =>
        {
            float elapseSeconds = (float)(DateTime.UtcNow - startTime).TotalSeconds;
            if (op.Status == AsyncOperationStatus.Succeeded && op.Result != null)
            {
                UnityEngine.Object unityAsset = op.Result as UnityEngine.Object;
                AddressablesShaderRepair.Repair(unityAsset);
                // ⚠️ 必须先登记反查表再派发回调：业务侧拿到 asset 后立即 UnloadAsset 时，HybridResourceHelper 才能命中。
                ResourceComponentExtensions.RegisterAddressablesHandle(unityAsset, op);
                if (loadAssetCallbacks.LoadAssetSuccessCallback != null)
                {
                    loadAssetCallbacks.LoadAssetSuccessCallback(assetName, op.Result, elapseSeconds, userData);
                }
            }
            else
            {
                // 加载失败：立即 Release 防止 Addressables 内部句柄泄漏。
                string rawErrorMessage = op.OperationException != null
                    ? op.OperationException.ToString()
                    : "Addressables 加载失败：句柄状态非 Succeeded。";
                string errorMessage = Utility.Text.Format(
                    "[AddressablesRouter] Addressables 加载失败，asset='{0}'，assetType='{1}'，status='{2}'，locations='{3}'，error='{4}'。",
                    assetName,
                    GetAssetTypeName(assetType),
                    op.Status.ToString(),
                    BuildLocationsSummary(locations),
                    rawErrorMessage);
                Log.Error(errorMessage);
                if (op.IsValid())
                {
                    Addressables.Release(op);
                }
                if (loadAssetCallbacks.LoadAssetFailureCallback != null)
                {
                    loadAssetCallbacks.LoadAssetFailureCallback(assetName, LoadResourceStatus.NotExist, errorMessage, userData);
                }
            }
        };

        return true;
    }

    /// <summary>
    /// 获取 GF 本次资源请求传入的运行时资源类型名称。
    /// </summary>
    /// <param name="assetType">GF 调用方期望加载的资源类型；为空时表示 GF 未显式传入类型。</param>
    /// <returns>用于日志输出的完整类型名；为空时返回 null 字符串，避免日志拼接时丢失诊断信息。</returns>
    private static string GetAssetTypeName(Type assetType)
    {
        return assetType != null ? assetType.FullName : "null";
    }

    /// <summary>
    /// 构造 Addressables 已命中的资源位置摘要。
    /// </summary>
    /// <param name="locations">Addressables ResourceLocator 命中的资源位置列表。</param>
    /// <returns>包含 PrimaryKey、InternalId、ProviderId、ResourceType 与依赖摘要的诊断字符串。</returns>
    private static string BuildLocationsSummary(IList<IResourceLocation> locations)
    {
        if (locations == null || locations.Count == 0)
        {
            return "empty";
        }

        int count = locations.Count;
        int limit = count > 3 ? 3 : count;
        string summary = string.Empty;
        for (int i = 0; i < limit; i++)
        {
            IResourceLocation location = locations[i];
            if (location == null)
            {
                summary += Utility.Text.Format("#{0}[null]", i);
                continue;
            }

            string resourceTypeName = location.ResourceType != null ? location.ResourceType.FullName : "null";
            string dependencySummary = BuildDependencySummary(location.Dependencies);
            summary += Utility.Text.Format(
                "#{0}[PrimaryKey='{1}', InternalId='{2}', ProviderId='{3}', ResourceType='{4}', Dependencies='{5}']",
                i,
                location.PrimaryKey,
                location.InternalId,
                location.ProviderId,
                resourceTypeName,
                dependencySummary);
        }

        if (count > limit)
        {
            summary += Utility.Text.Format("...total={0}", count);
        }

        return summary;
    }

    /// <summary>
    /// 构造 Addressables 资源位置的直接依赖摘要。
    /// </summary>
    /// <param name="dependencies">资源位置上登记的直接依赖列表，通常能暴露缺失或下载失败的 bundle。</param>
    /// <returns>包含依赖数量、PrimaryKey、InternalId 与 ProviderId 的诊断字符串。</returns>
    private static string BuildDependencySummary(IList<IResourceLocation> dependencies)
    {
        if (dependencies == null || dependencies.Count == 0)
        {
            return "empty";
        }

        int count = dependencies.Count;
        int limit = count > 5 ? 5 : count;
        string summary = Utility.Text.Format("count={0};", count);
        for (int i = 0; i < limit; i++)
        {
            IResourceLocation dependency = dependencies[i];
            if (dependency == null)
            {
                summary += Utility.Text.Format("#{0}[null]", i);
                continue;
            }

            summary += Utility.Text.Format(
                "#{0}[PrimaryKey='{1}', InternalId='{2}', ProviderId='{3}']",
                i,
                dependency.PrimaryKey,
                dependency.InternalId,
                dependency.ProviderId);
        }

        if (count > limit)
        {
            summary += Utility.Text.Format("...total={0}", count);
        }

        return summary;
    }

    /// <summary>
    /// 按运行时 Type 发起 Addressables 泛型加载。
    /// </summary>
    /// <param name="location">已经由 TryLocate 选定的非 LegacyResourcesProvider 资源位置。</param>
    /// <param name="assetType">GF 调用方期望的资源类型；为空时回退到 UnityEngine.Object。</param>
    /// <returns>非泛型句柄，便于统一注册 Completed 回调和释放。</returns>
    private static AsyncOperationHandle LoadAddressablesAsset(IResourceLocation location, Type assetType)
    {
        if (assetType == typeof(UnityEngine.Sprite))
        {
            return Addressables.LoadAssetAsync<UnityEngine.Sprite>(location);
        }

        if (assetType == typeof(UnityEngine.GameObject))
        {
            return Addressables.LoadAssetAsync<UnityEngine.GameObject>(location);
        }

        if (assetType == typeof(UnityEngine.TextAsset))
        {
            return Addressables.LoadAssetAsync<UnityEngine.TextAsset>(location);
        }

        if (assetType == typeof(Spine.Unity.SkeletonDataAsset))
        {
            return Addressables.LoadAssetAsync<Spine.Unity.SkeletonDataAsset>(location);
        }

        if (assetType == typeof(UnityEngine.AudioClip))
        {
            return Addressables.LoadAssetAsync<UnityEngine.AudioClip>(location);
        }

        if (assetType == typeof(UnityEngine.Material))
        {
            return Addressables.LoadAssetAsync<UnityEngine.Material>(location);
        }

        if (assetType == typeof(UnityEngine.Texture2D))
        {
            return Addressables.LoadAssetAsync<UnityEngine.Texture2D>(location);
        }

        return Addressables.LoadAssetAsync<UnityEngine.Object>(location);
    }

    /// <summary>
    /// GF 内核 UnloadAsset 入口注入的释放路由委托主体。
    /// 反查 ResourceComponentExtensions 静态字典：命中则 Addressables.Release(handle) 后返回 true，
    /// 未命中返回 false 让 GF 主链路走 m_AssetPool.Unspawn 引用计数机制。
    /// </summary>
    /// <param name="asset">要释放的资源对象。</param>
    /// <returns>true 表示 Addressables 已精确释放；false 表示该资源不属于 Addressables。</returns>
    private static bool TryReleaseRoute(object asset)
    {
        UnityEngine.Object unityAsset = asset as UnityEngine.Object;
        if (unityAsset == null)
        {
            return false;
        }

        return ResourceComponentExtensions.TryReleaseAddressablesHandle(unityAsset);
    }

    /// <summary>
    /// 同步判定 Addressables key 是否注册。
    /// 遍历 Addressables.ResourceLocators 调 Locate；只有命中非 LegacyResourcesProvider 时才返回 true。
    /// 此方法在 catalog 已初始化的情况下保证零 IO，locations 输出复用 Locator 内部缓存。
    /// </summary>
    private static bool TryLocate(string assetName, Type assetType, out IList<IResourceLocation> locations)
    {
        return TryLocate(assetName, assetType, out locations, out _, out _);
    }

    /// <summary>
    /// 同步判定 Addressables key 是否注册，并输出参与判定的 Locator 数量。
    /// locatorCount 专门用于兜底错误诊断：当 Resources 也加载失败时，最终日志能看出是 catalog 空、Address 不匹配，还是类型过滤不匹配。
    /// 如果多个 catalog 同时命中同一个 key，会保留最后一个非 LegacyResourcesProvider 命中，确保启动阶段追加加载的远程 catalog 优先级更高。
    /// </summary>
    private static bool TryLocate(string assetName, Type assetType, out IList<IResourceLocation> locations, out IResourceLocation selectedLocation, out int locatorCount)
    {
        locations = null;
        selectedLocation = null;
        locatorCount = 0;
        if (s_RemoteCatalogLocator != null)
        {
            locatorCount++;
            if (s_RemoteCatalogLocator.Locate(assetName, assetType, out IList<IResourceLocation> remoteLocations))
            {
                IResourceLocation remoteLocation = FindFirstNonLegacyLocation(remoteLocations);
                if (remoteLocation != null)
                {
                    locations = remoteLocations;
                    selectedLocation = remoteLocation;
                    return true;
                }
            }
        }

        // ⚠️ Addressables.ResourceLocators 是 IEnumerable<IResourceLocator>，遍历会产生迭代器对象（少量 GC）；
        //     如果未来需要严格零 GC，可缓存 ResourceLocators 列表的 ToArray 副本，但项目当前频次较低不必优化。
        foreach (IResourceLocator locator in Addressables.ResourceLocators)
        {
            if (ReferenceEquals(locator, s_RemoteCatalogLocator))
            {
                continue;
            }

            locatorCount++;
            if (!locator.Locate(assetName, assetType, out IList<IResourceLocation> candidateLocations))
            {
                continue;
            }

            IResourceLocation candidateLocation = FindFirstNonLegacyLocation(candidateLocations);
            if (candidateLocation == null)
            {
                continue;
            }

            locations = candidateLocations;
            selectedLocation = candidateLocation;
        }

        return selectedLocation != null;
    }

    /// <summary>
    /// 从命中的 Addressables 资源位置列表里选择第一个真正的非 Resources Provider 位置。
    /// </summary>
    /// <param name="locations">ResourceLocator 命中的资源位置列表。</param>
    /// <returns>第一个非 LegacyResourcesProvider 位置；没有可用位置时返回 null。</returns>
    private static IResourceLocation FindFirstNonLegacyLocation(IList<IResourceLocation> locations)
    {
        if (locations == null || locations.Count == 0)
        {
            return null;
        }

        for (int i = 0; i < locations.Count; i++)
        {
            IResourceLocation location = locations[i];
            if (location != null && !IsLegacyResourcesLocation(location))
            {
                return location;
            }
        }

        return null;
    }

    /// <summary>
    /// 判断指定 Addressables 资源位置是否来自 Unity 内置 Resources 兼容 Provider。
    /// </summary>
    /// <param name="location">需要检查的 Addressables 资源位置。</param>
    /// <returns>如果该位置最终会走 Resources.LoadAsync，则返回 true；否则返回 false。</returns>
    private static bool IsLegacyResourcesLocation(IResourceLocation location)
    {
        return location != null && string.Equals(location.ProviderId, LegacyResourcesProviderId, StringComparison.Ordinal);
    }
}
