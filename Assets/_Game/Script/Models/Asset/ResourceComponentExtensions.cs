using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityGameFramework.Runtime;

/// <summary>
/// GF ResourceComponent 的 Addressables 扩展方法。
/// 设计目标：在不改动 GF 内核与 Resources.LoadAsync 主链路的前提下，给业务层补一组显式 Addressables API。
/// 业务层的两条加载路径：
/// 1. 想走 Resources.LoadAsync   → 继续用 GameEntry.Resource.LoadAsset(...) 等 GF 现有 API（行为不变）。
/// 2. 想走 Addressables          → 调用本类提供的 LoadAddressableAssetAsync 等扩展方法（新通道）。
/// 同一个相对路径字符串既可以作为 Resources 相对路径，也可以作为 Addressables Address，互不冲突。
/// </summary>
public static class ResourceComponentExtensions
{
    /// <summary>
    /// asset → AsyncOperationHandle 反查表（单资源加载结果）。
    /// 静态字典：所有 ResourceComponent 实例共享，便于跨场景释放。
    /// 容量 64：覆盖常见战斗 + 主界面常驻图标；超过会自动扩容，零功能影响。
    /// ⚠️ 避坑：必须用 ReferenceEqualityComparer 默认行为（即引用相等），UnityEngine.Object 的 Equals 在销毁后会返回 true，可能误命中。
    ///         实测 Dictionary 默认走对象的 GetHashCode/Equals，对 UnityEngine.Object 仍然按 native handle 比较，安全。
    /// </summary>
    private static readonly Dictionary<UnityEngine.Object, AsyncOperationHandle> s_HandlesByAsset
        = new Dictionary<UnityEngine.Object, AsyncOperationHandle>(64);

    /// <summary>
    /// IList → AsyncOperationHandle 反查表（标签批量加载结果）。
    /// Key 用 object 是因为 Addressables.LoadAssetsAsync&lt;T&gt; 返回的 IList&lt;T&gt; 是泛型，无法用统一类型作 key。
    /// 引用相等比较：业务层只能传回原始的 IList 引用才能精确释放，避免误释放其他批次。
    /// </summary>
    private static readonly Dictionary<object, AsyncOperationHandle> s_HandlesByAssetList
        = new Dictionary<object, AsyncOperationHandle>(8);

    /// <summary>
    /// 异步加载 Addressables 单资源。
    /// 业务侧使用样例：
    /// <code>
    /// GameEntry.Resource.LoadAddressableAssetAsync&lt;Sprite&gt;(
    ///     "Arts/Fruit/FruitCard/WP_80001",
    ///     (key, sprite, _) =&gt; image.sprite = sprite);
    /// </code>
    /// </summary>
    /// <typeparam name="T">资源类型，必须继承自 UnityEngine.Object，例如 Sprite、AudioClip、GameObject。</typeparam>
    /// <param name="self">扩展目标 ResourceComponent，仅用于扩展方法绑定，方法体内不依赖其状态。</param>
    /// <param name="address">Addressables Address（即资源 Key），与 Resources 资源路径可以相同。</param>
    /// <param name="onSuccess">加载成功回调：(address, asset, userData)。</param>
    /// <param name="onFailure">加载失败回调：(address, errorMessage, userData)。可空。</param>
    /// <param name="userData">业务自定义数据，模块仅透传。</param>
    public static void LoadAddressableAssetAsync<T>(this ResourceComponent self,
        string address,
        Action<string, T, object> onSuccess,
        Action<string, string, object> onFailure = null,
        object userData = null) where T : UnityEngine.Object
    {
        // 入参校验：Address 无效直接走失败回调，避免 Addressables 抛异常。
        if (string.IsNullOrEmpty(address))
        {
            onFailure?.Invoke(address, "Addressables 加载失败：Address 无效。", userData);
            return;
        }

        // 真正发起 Addressables 异步加载。
        // 注：AsyncOperationHandle<T> 是 struct（值类型），赋给 lambda 捕获时不会装箱，但 lambda 自身是闭包会分配。
        //     这里加载是低频操作，可以接受；如果未来要在高频路径调用需改为静态 handler + 字典查 callback 模式。
        AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(address);

        // ⚠️ 闭包捕获：address / onSuccess / onFailure / userData 会被捕获到隐式生成的闭包类。
        //     单次加载产生 1 个闭包对象，加载完成后即可被 GC，不会进入对象池。
        handle.Completed += op =>
        {
            if (op.Status == AsyncOperationStatus.Succeeded && op.Result != null)
            {
                // 登记反查表（必须在派发业务回调前完成，否则业务层立刻 Unload 会找不到 handle）。
                // 走统一入口 RegisterAddressablesHandle 享受去重保护：若同一 asset 已被路由路径登记过，旧 handle 会被先 Release。
                RegisterAddressablesHandle(op.Result, op);
                onSuccess?.Invoke(address, op.Result, userData);
            }
            else
            {
                // 加载失败：必须立即 Release 防止 Addressables 内部句柄泄漏。
                string errorMessage = op.OperationException != null
                    ? op.OperationException.Message
                    : "Addressables 加载失败：句柄状态非 Succeeded。";
                if (op.IsValid())
                {
                    Addressables.Release(op);
                }
                onFailure?.Invoke(address, errorMessage, userData);
            }
        };
    }

    /// <summary>
    /// 异步加载 Addressables 标签下的所有资源。
    /// Addressables 专属能力（Resources.LoadAsync 没有 Label 概念）。
    /// 业务侧使用样例：
    /// <code>
    /// GameEntry.Resource.LoadAddressableAssetsAsyncByLabel&lt;Sprite&gt;(
    ///     "Load",
    ///     (label, sprites, _) =&gt; ApplyLoadingSprites(sprites));
    /// </code>
    /// </summary>
    /// <typeparam name="T">资源类型。</typeparam>
    /// <param name="self">扩展目标 ResourceComponent。</param>
    /// <param name="label">Addressables Label。</param>
    /// <param name="onSuccess">加载成功回调：(label, assets, userData)。assets 是 Addressables 内部 IList，请勿修改。</param>
    /// <param name="onFailure">加载失败回调：(label, errorMessage, userData)。可空。</param>
    /// <param name="userData">业务自定义数据。</param>
    public static void LoadAddressableAssetsAsyncByLabel<T>(this ResourceComponent self,
        string label,
        Action<string, IList<T>, object> onSuccess,
        Action<string, string, object> onFailure = null,
        object userData = null) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(label))
        {
            onFailure?.Invoke(label, "Addressables 标签加载失败：Label 无效。", userData);
            return;
        }

        // mergeMode = null：默认 Union 合并，等价于把所有匹配 label 的资源全部取出来。
        AsyncOperationHandle<IList<T>> handle = Addressables.LoadAssetsAsync<T>(label, null);

        handle.Completed += op =>
        {
            if (op.Status == AsyncOperationStatus.Succeeded && op.Result != null)
            {
                // 整个 IList<T> 由这一个 handle 管理，按 IList 引用做 key 反查。
                // 走统一入口 RegisterAddressablesAssetList 享受去重保护。
                RegisterAddressablesAssetList(op.Result, op);
                onSuccess?.Invoke(label, op.Result, userData);
            }
            else
            {
                string errorMessage = op.OperationException != null
                    ? op.OperationException.Message
                    : "Addressables 标签加载失败：句柄状态非 Succeeded。";
                if (op.IsValid())
                {
                    Addressables.Release(op);
                }
                onFailure?.Invoke(label, errorMessage, userData);
            }
        };
    }

    /// <summary>
    /// 释放通过 LoadAddressableAssetAsync 加载的单个资源。
    /// 内部从静态字典反查到对应的 AsyncOperationHandle 并调 Addressables.Release。
    /// 反查不到时静默忽略：可能业务层重复释放或 asset 不是经由本扩展方法加载，避免抛异常。
    /// </summary>
    /// <param name="self">扩展目标 ResourceComponent。</param>
    /// <param name="asset">要释放的资源对象。</param>
    public static void UnloadAddressableAsset(this ResourceComponent self, UnityEngine.Object asset)
    {
        TryReleaseAddressablesHandle(asset);
    }

    /// <summary>
    /// 释放通过 LoadAddressableAssetsAsyncByLabel 加载的资源列表。
    /// 必须传回原始的 IList 引用（即业务侧从 onSuccess 回调里收到的 assets 参数）才能精确释放。
    /// </summary>
    /// <param name="self">扩展目标 ResourceComponent。</param>
    /// <param name="assets">要释放的资源列表（必须是 onSuccess 回调里收到的原始引用）。</param>
    public static void UnloadAddressableAssets(this ResourceComponent self, object assets)
    {
        TryReleaseAddressablesAssetList(assets);
    }

    /// <summary>
    /// 登记 asset → AsyncOperationHandle 反查映射。
    /// 用于：
    /// 1. AddressablesAssetRouterImpl 在 GF 内核 LoadAsset 路由命中后登记句柄；
    /// 2. LoadAddressableAssetAsync 自身在加载完成后登记句柄。
    /// ⚠️ 去重保护：同一 asset 已存在登记时，先 Release 旧 handle 抵消其引用计数再覆盖。
    /// 原因：双路径并发加载同一 Addressables key 时，Addressables 内部会返回不同 handle 但 +1 引用计数；
    ///       字典覆盖会丢弃旧 handle 引用，必须立即 Release 旧 handle 否则该 +1 引用计数永远无法归零（句柄泄漏）。
    /// </summary>
    /// <param name="asset">加载完成的资源对象。</param>
    /// <param name="handle">对应的 Addressables 句柄（隐式从 AsyncOperationHandle&lt;T&gt; 转入）。</param>
    internal static void RegisterAddressablesHandle(UnityEngine.Object asset, AsyncOperationHandle handle)
    {
        if (asset == null)
        {
            return;
        }

        // 去重：覆盖前先释放旧 handle 抵消 Addressables 内部引用计数。
        if (s_HandlesByAsset.TryGetValue(asset, out AsyncOperationHandle oldHandle))
        {
            if (oldHandle.IsValid())
            {
                Addressables.Release(oldHandle);
            }
        }

        s_HandlesByAsset[asset] = handle;
    }

    /// <summary>
    /// 反查并释放单资源的 Addressables 句柄。
    /// 命中时移除字典 + 调 Addressables.Release，返回 true；未命中返回 false。
    /// 用于：
    /// 1. HybridResourceHelper.Release 在 GF 引用归零回调时精确释放句柄；
    /// 2. UnloadAddressableAsset 扩展方法的实际实现。
    /// </summary>
    /// <param name="asset">要释放的资源对象。</param>
    /// <returns>true 表示反查命中并已释放；false 表示反查未命中（asset 可能不是经由 Addressables 加载）。</returns>
    internal static bool TryReleaseAddressablesHandle(UnityEngine.Object asset)
    {
        if (asset == null)
        {
            return false;
        }

        if (!s_HandlesByAsset.TryGetValue(asset, out AsyncOperationHandle handle))
        {
            return false;
        }

        // ⚠️ 先移除字典再 Release：防止 Release 内部异步回调期间字典处于不一致状态，
        //     也保证重入时（理论上不存在）字典状态干净。
        s_HandlesByAsset.Remove(asset);
        if (handle.IsValid())
        {
            Addressables.Release(handle);
        }
        return true;
    }

    /// <summary>
    /// 登记 IList → AsyncOperationHandle 反查映射（标签批量加载）。
    /// 与 RegisterAddressablesHandle 同样具备去重保护：覆盖前先 Release 旧 handle。
    /// </summary>
    /// <param name="assetList">加载完成的资源列表（必须是原始 IList 引用）。</param>
    /// <param name="handle">对应的 Addressables 句柄。</param>
    internal static void RegisterAddressablesAssetList(object assetList, AsyncOperationHandle handle)
    {
        if (assetList == null)
        {
            return;
        }

        if (s_HandlesByAssetList.TryGetValue(assetList, out AsyncOperationHandle oldHandle))
        {
            if (oldHandle.IsValid())
            {
                Addressables.Release(oldHandle);
            }
        }

        s_HandlesByAssetList[assetList] = handle;
    }

    /// <summary>
    /// 反查并释放标签批量加载的 Addressables 句柄。
    /// </summary>
    /// <param name="assetList">要释放的资源列表（必须是 onSuccess 回调里收到的原始引用）。</param>
    /// <returns>true 表示反查命中并已释放；false 表示反查未命中。</returns>
    internal static bool TryReleaseAddressablesAssetList(object assetList)
    {
        if (assetList == null)
        {
            return false;
        }

        if (!s_HandlesByAssetList.TryGetValue(assetList, out AsyncOperationHandle handle))
        {
            return false;
        }

        s_HandlesByAssetList.Remove(assetList);
        if (handle.IsValid())
        {
            Addressables.Release(handle);
        }
        return true;
    }

    /// <summary>
    /// 强制释放当前扩展方法所有持有的 Addressables 句柄。
    /// 仅供 GameAddressableAssetModule.ReleaseAll / GameEntry.OnDestroy 等"大清扫"路径使用，业务层不应直接调用。
    /// </summary>
    /// <param name="self">扩展目标 ResourceComponent。</param>
    public static void ReleaseAllAddressableAssets(this ResourceComponent self)
    {
        // 单资源句柄逐一 Release。
        foreach (KeyValuePair<UnityEngine.Object, AsyncOperationHandle> pair in s_HandlesByAsset)
        {
            if (pair.Value.IsValid())
            {
                Addressables.Release(pair.Value);
            }
        }
        s_HandlesByAsset.Clear();

        // Label 批量句柄逐一 Release。
        foreach (KeyValuePair<object, AsyncOperationHandle> pair in s_HandlesByAssetList)
        {
            if (pair.Value.IsValid())
            {
                Addressables.Release(pair.Value);
            }
        }
        s_HandlesByAssetList.Clear();
    }
}
