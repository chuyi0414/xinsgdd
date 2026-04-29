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
            s_IsAddressablesReady = op.Status == AsyncOperationStatus.Succeeded;
            if (!s_IsAddressablesReady)
            {
                Log.Error("[AddressablesRouter] Addressables.InitializeAsync 失败，所有请求将退回到 Resources.LoadAsync 兜底。Error: "
                    + (op.OperationException != null ? op.OperationException.Message : "unknown"));
            }

            onComplete?.Invoke();
        };
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
            return false;
        }

        if (string.IsNullOrEmpty(assetName))
        {
            return false;
        }

        // 同步判定 key 是否在 catalog 中：遍历每个 Locator 调 Locate（同步、零 IO、零分配）。
        if (!TryLocate(assetName, assetType, out _))
        {
            return false;
        }

        DateTime startTime = DateTime.UtcNow;

        // 用 UnityEngine.Object 泛型加载，业务侧可在 LoadAssetSuccessCallback 里 as 到具体类型。
        // GF UI/Entity/DataTable/Sound 的 LoadAssetCallbacks 内部 as 处理已经存在，行为兼容。
        // ⚠️ 防御性 try-catch：理论上 Addressables.LoadAssetAsync 在 key 不存在时返回 Failed handle 而非报错，
        //     但 catalog 损坏 / 极端情况下可能同步抛异常；捕获后走 LoadAssetFailureCallback 避免 GF 调用栈中断。
        AsyncOperationHandle<UnityEngine.Object> handle;
        try
        {
            handle = Addressables.LoadAssetAsync<UnityEngine.Object>(assetName);
        }
        catch (Exception ex)
        {
            Log.Error("[AddressablesRouter] LoadAssetAsync 同步抛异常，asset='" + assetName + "'，error: " + ex.Message);
            if (loadAssetCallbacks.LoadAssetFailureCallback != null)
            {
                loadAssetCallbacks.LoadAssetFailureCallback(assetName, LoadResourceStatus.NotExist, ex.Message, userData);
            }
            return true; // 路由已声明接管（失败路径），不再走 GF 主链路。
        }

        handle.Completed += op =>
        {
            float elapseSeconds = (float)(DateTime.UtcNow - startTime).TotalSeconds;
            if (op.Status == AsyncOperationStatus.Succeeded && op.Result != null)
            {
                // ⚠️ 必须先登记反查表再派发回调：业务侧拿到 asset 后立即 UnloadAsset 时，HybridResourceHelper 才能命中。
                ResourceComponentExtensions.RegisterAddressablesHandle(op.Result, op);
                if (loadAssetCallbacks.LoadAssetSuccessCallback != null)
                {
                    loadAssetCallbacks.LoadAssetSuccessCallback(assetName, op.Result, elapseSeconds, userData);
                }
            }
            else
            {
                // 加载失败：立即 Release 防止 Addressables 内部句柄泄漏。
                string errorMessage = op.OperationException != null
                    ? op.OperationException.Message
                    : "Addressables 加载失败：句柄状态非 Succeeded。";
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
    /// 遍历 Addressables.ResourceLocators 调 Locate；任一 Locator 命中即返回 true。
    /// 此方法在 catalog 已初始化的情况下保证零 IO 零分配（locations 输出复用 Locator 内部缓存）。
    /// </summary>
    private static bool TryLocate(string assetName, Type assetType, out IList<IResourceLocation> locations)
    {
        locations = null;
        // ⚠️ Addressables.ResourceLocators 是 IEnumerable<IResourceLocator>，遍历会产生迭代器对象（少量 GC）；
        //     如果未来需要严格零 GC，可缓存 ResourceLocators 列表的 ToArray 副本，但项目当前频次较低不必优化。
        foreach (IResourceLocator locator in Addressables.ResourceLocators)
        {
            if (locator.Locate(assetName, assetType, out locations))
            {
                return true;
            }
        }
        return false;
    }
}
