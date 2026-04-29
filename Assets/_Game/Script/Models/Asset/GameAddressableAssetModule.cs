using System;
using System.Collections.Generic;
using UnityGameFramework.Runtime;
using Object = UnityEngine.Object;

/// <summary>
/// Addressables 大资源作用域。
/// 用于把 Arts、Audio 等大体积资源绑定到明确的业务生命周期，避免业务层散落手动 Release。
/// </summary>
public enum AddressableAssetScope
{
    /// <summary>
    /// 全局常驻资源。
    /// 适合通用按钮音效、默认占位图、全局公共图标等随游戏进程长期存在的资源。
    /// </summary>
    Global = 0,

    /// <summary>
    /// 主界面生命周期资源。
    /// 适合只在主界面大流程中使用，离开主界面后可以整体释放的资源。
    /// </summary>
    Main = 1,

    /// <summary>
    /// 战斗生命周期资源。
    /// 适合单局战斗中的卡图、战斗音效、战斗专用表现资源。
    /// </summary>
    Combat = 2,

    /// <summary>
    /// 每日关生命周期资源。
    /// 适合每日关卡图、每日关专属音频、每日关专属表现资源。
    /// </summary>
    Daily = 3,

    /// <summary>
    /// 临时资源。
    /// 适合预览、短期弹窗、一次性展示等短生命周期资源。
    /// </summary>
    Temporary = 4,
}

/// <summary>
/// Addressables 大资源统一加载模块。
/// 只负责 Arts、Audio 等大体积资源的加载、缓存、引用计数与释放；不负责 UI 赋值、音频播放或业务实例化。
/// </summary>
public sealed class GameAddressableAssetModule
{
    /// <summary>
    /// 当前支持的资源作用域数量。
    /// 必须与 AddressableAssetScope 枚举的连续取值保持一致，用于以数组方式零装箱记录每个作用域的引用数量。
    /// </summary>
    private const int ScopeCount = 5;

    /// <summary>
    /// 单个 Addressables 资源的缓存记录。
    /// 记录加载结果、总引用计数、分作用域引用计数以及等待加载完成的回调队列。
    /// 底层句柄由 ResourceComponentExtensions 统一管理，本记录仅保留资源对象与引用计数。
    /// </summary>
    private sealed class AssetRecord
    {
        /// <summary>
        /// 单资源加载结果。
        /// 由 GameEntry.Resource 的 Addressables 扩展方法回调写入；
        /// 释放时通过 GameEntry.Resource.UnloadAddressableAsset 反查 Addressables 句柄。
        /// 初始为 null；标签批量加载时保持 null（改用 LoadedAssets / LoadedAssetsRaw）。
        /// </summary>
        public Object LoadedAsset;

        /// <summary>
        /// 当前资源是否仍处于加载中。
        /// 初始状态为 true，加载完成回调返回后改为 false。
        /// </summary>
        public bool IsLoading;

        /// <summary>
        /// 当前资源总引用计数。
        /// 每次 LoadAsset 或 Retain 增加，Release 或 ReleaseScope 减少，归零后走 ReleaseRecord 调用扩展方法释放底层 Addressables 句柄。
        /// </summary>
        public int TotalRefCount;

        /// <summary>
        /// 每个作用域下的引用计数。
        /// 下标由 AddressableAssetScope 转换而来，用于 ReleaseScope 时只释放指定业务生命周期的引用。
        /// 全部计数归零后，ReleaseRecord 会调 GameEntry.Resource.UnloadAddressableAsset(s) 释放底层句柄。
        /// </summary>
        public readonly int[] RefCountsByScope = new int[ScopeCount];

        /// <summary>
        /// 等待本次异步加载完成的业务回调队列。
        /// 初始为空；同一个 Address 在加载中被重复请求时，会合并到同一次加载并追加回调。
        /// </summary>
        public readonly List<ILoadRequest> PendingRequests = new List<ILoadRequest>(4);

        /// <summary>
        /// 标签批量加载的结果列表（Object 视图）。
        /// 仅在通过 LoadAssetsByLabel 加载时写入；供 PendingLabelRequests 派发与 TryGetLoadedByLabel 同步读取使用。
        /// </summary>
        public IList<Object> LoadedAssets;

        /// <summary>
        /// 标签批量加载的原始 IList 引用（实际类型为 IList&lt;T&gt;）。
        /// 用作 GameEntry.Resource.UnloadAddressableAssets 的反查 key——必须传回原始引用才能精确命中静态字典。
        /// 仅在通过 LoadAssetsByLabel 加载时写入，单资源加载时保持 null。
        /// </summary>
        public object LoadedAssetsRaw;

        /// <summary>
        /// 标签批量加载的回调队列。
        /// 仅在通过 LoadAssetsByLabel 加载时使用，与 PendingRequests 互相独立。
        /// </summary>
        public readonly List<ILabelLoadRequest> PendingLabelRequests = new List<ILabelLoadRequest>(2);
    }

    /// <summary>
    /// 单次加载请求的回调抽象。
    /// 用于把不同资源泛型类型的回调统一存放到 AssetRecord.PendingRequests 中。
    /// </summary>
    private interface ILoadRequest
    {
        /// <summary>
        /// 分发加载成功回调。
        /// </summary>
        /// <param name="address">资源 Address。</param>
        /// <param name="asset">加载完成的 Unity 资源对象。</param>
        void InvokeSuccess(string address, Object asset);

        /// <summary>
        /// 分发加载失败回调。
        /// </summary>
        /// <param name="address">资源 Address。</param>
        /// <param name="errorMessage">失败原因。</param>
        void InvokeFailure(string address, string errorMessage);
    }

    /// <summary>
    /// 指定资源类型的单次加载请求。
    /// 保存业务层传入的成功/失败回调以及 UserData，Addressables 完成后由模块统一分发。
    /// </summary>
    /// <typeparam name="T">资源类型，例如 Sprite、AudioClip、SkeletonDataAsset。</typeparam>
    private sealed class LoadRequest<T> : ILoadRequest where T : Object
    {
        /// <summary>
        /// 加载成功回调。
        /// 初始状态由 LoadAsset 调用方传入，可以为空；为空时只做缓存预热，不通知业务层。
        /// </summary>
        public Action<string, T, object> SuccessCallback;

        /// <summary>
        /// 加载失败回调。
        /// 初始状态由 LoadAsset 调用方传入，可以为空；为空时失败只保留在 Addressables 日志中。
        /// </summary>
        public Action<string, string, object> FailureCallback;

        /// <summary>
        /// 业务自定义数据。
        /// 初始状态由 LoadAsset 调用方传入，模块只透传，不读取也不修改。
        /// </summary>
        public object UserData;

        /// <summary>
        /// 分发加载成功回调。
        /// </summary>
        /// <param name="address">资源 Address。</param>
        /// <param name="asset">加载完成的 Unity 资源对象。</param>
        public void InvokeSuccess(string address, Object asset)
        {
            T typedAsset = asset as T;
            if (typedAsset == null)
            {
                InvokeFailure(address, "Loaded asset type does not match requested type.");
                return;
            }

            if (SuccessCallback != null)
            {
                SuccessCallback(address, typedAsset, UserData);
            }
        }

        /// <summary>
        /// 分发加载失败回调。
        /// </summary>
        /// <param name="address">资源 Address。</param>
        /// <param name="errorMessage">失败原因。</param>
        public void InvokeFailure(string address, string errorMessage)
        {
            if (FailureCallback != null)
            {
                FailureCallback(address, errorMessage, UserData);
            }
        }
    }

    /// <summary>
    /// 标签批量加载请求的回调抽象。
    /// 用于在标签加载完成后一次性分发整个资源列表。
    /// </summary>
    private interface ILabelLoadRequest
    {
        void InvokeSuccess(string label, IList<Object> assets);
        void InvokeFailure(string label, string errorMessage);
    }

    /// <summary>
    /// 指定资源类型的标签批量加载请求。
    /// </summary>
    private sealed class LabelLoadRequest<T> : ILabelLoadRequest where T : Object
    {
        public Action<string, IList<T>, object> SuccessCallback;
        public Action<string, string, object> FailureCallback;
        public object UserData;

        public void InvokeSuccess(string label, IList<Object> assets)
        {
            if (SuccessCallback == null)
            {
                return;
            }

            List<T> typedList = new List<T>(assets.Count);
            for (int i = 0; i < assets.Count; i++)
            {
                if (assets[i] is T typedAsset)
                {
                    typedList.Add(typedAsset);
                }
            }

            SuccessCallback(label, typedList, UserData);
        }

        public void InvokeFailure(string label, string errorMessage)
        {
            FailureCallback?.Invoke(label, errorMessage, UserData);
        }
    }

    /// <summary>
    /// Address 到资源缓存记录的映射。
    /// Key 为短 Address，例如 "Arts/Fruit/FruitCard/WP_80001" 或 "Audio/Music/BgmMain"。
    /// 标签加载时 Key 为标签名，例如 "Load"。
    /// </summary>
    private readonly Dictionary<string, AssetRecord> _recordsByAddress = new Dictionary<string, AssetRecord>(StringComparer.Ordinal);

    /// <summary>
    /// ReleaseScope 时复用的临时释放列表。
    /// 用字段复用避免每次按作用域释放都分配新的 List。
    /// </summary>
    private readonly List<string> _releaseBuffer = new List<string>(32);

    /// <summary>
    /// 按 Addressables Address 异步加载资源。
    /// 如果同一 Address 已加载完成，会立即同步回调；如果正在加载，会合并到同一个 Handle，避免重复 IO。
    /// </summary>
    /// <typeparam name="T">资源类型，例如 Sprite、AudioClip、SkeletonDataAsset。</typeparam>
    /// <param name="address">Addressables Address。</param>
    /// <param name="scope">资源生命周期作用域。</param>
    /// <param name="successCallback">加载成功回调。</param>
    /// <param name="failureCallback">加载失败回调。</param>
    /// <param name="userData">业务自定义数据。</param>
    public void LoadAsset<T>(string address, AddressableAssetScope scope, Action<string, T, object> successCallback, Action<string, string, object> failureCallback = null, object userData = null) where T : Object
    {
        if (string.IsNullOrEmpty(address))
        {
            if (failureCallback != null)
            {
                failureCallback(address, "Address is invalid.", userData);
            }

            return;
        }

        if (_recordsByAddress.TryGetValue(address, out AssetRecord record))
        {
            AddReference(record, scope);

            if (record.IsLoading)
            {
                AddPendingRequest(record, successCallback, failureCallback, userData);
                return;
            }

            if (record.LoadedAsset != null)
            {
                InvokeImmediateSuccess(address, record.LoadedAsset, successCallback, failureCallback, userData);
                return;
            }

            ReleaseRecord(address, record);
        }

        AssetRecord newRecord = new AssetRecord
        {
            IsLoading = true,
            TotalRefCount = 0,
        };

        AddReference(newRecord, scope);
        AddPendingRequest(newRecord, successCallback, failureCallback, userData);
        _recordsByAddress.Add(address, newRecord);

        // 走 ResourceComponent 的 Addressables 扩展方法发起加载，与 GF Resources.LoadAsync 主链路完全隔离。
        // userData 透传 address，回调时通过它定位 record。
        GameEntry.Resource.LoadAddressableAssetAsync<T>(address,
            OnAddressableLoadSuccess,
            OnAddressableLoadFailure,
            address);
    }

    /// <summary>
    /// 按 Addressables 标签批量加载一组同类型资源。
    /// 标签下所有资源加载完后一次性回调，适合加载界面、集合类图标等场景。
    /// 同一标签重复请求会合并到同一次加载，避免重复下载。
    /// </summary>
    /// <typeparam name="T">资源类型，例如 Sprite、AudioClip。</typeparam>
    /// <param name="label">Addressables 标签。</param>
    /// <param name="scope">资源生命周期作用域。</param>
    /// <param name="successCallback">加载成功回调，第一个参数为标签名。</param>
    /// <param name="failureCallback">加载失败回调。</param>
    /// <param name="userData">业务自定义数据。</param>
    public void LoadAssetsByLabel<T>(string label, AddressableAssetScope scope, Action<string, IList<T>, object> successCallback, Action<string, string, object> failureCallback = null, object userData = null) where T : Object
    {
        if (string.IsNullOrEmpty(label))
        {
            failureCallback?.Invoke(label, "Label is invalid.", userData);
            return;
        }

        if (_recordsByAddress.TryGetValue(label, out AssetRecord record))
        {
            AddReference(record, scope);

            if (record.IsLoading)
            {
                AddPendingLabelRequest(record, successCallback, failureCallback, userData);
                return;
            }

            if (record.LoadedAssets != null)
            {
                InvokeLabelSuccess(label, record.LoadedAssets, successCallback, userData);
                return;
            }

            ReleaseRecord(label, record);
        }

        AssetRecord newRecord = new AssetRecord
        {
            IsLoading = true,
            TotalRefCount = 0,
        };

        AddReference(newRecord, scope);
        AddPendingLabelRequest(newRecord, successCallback, failureCallback, userData);
        _recordsByAddress.Add(label, newRecord);

        // 走 ResourceComponent 的 Addressables 标签扩展方法加载，userData 透传 label，回调时通过它定位 record。
        GameEntry.Resource.LoadAddressableAssetsAsyncByLabel<T>(label,
            OnAddressableLabelLoadSuccess,
            OnAddressableLabelLoadFailure,
            label);
    }

    /// <summary>
    /// 尝试同步获取通过标签加载的资源列表。
    /// 不触发加载，不增加引用计数。
    /// </summary>
    /// <typeparam name="T">资源类型。</typeparam>
    /// <param name="label">Addressables 标签。</param>
    /// <param name="assets">输出的资源列表，未命中时为 null。</param>
    /// <returns>标签已加载完成时返回 true，否则返回 false。</returns>
    public bool TryGetLoadedByLabel<T>(string label, out IList<T> assets) where T : Object
    {
        assets = null;

        if (string.IsNullOrEmpty(label))
        {
            return false;
        }

        if (!_recordsByAddress.TryGetValue(label, out AssetRecord record))
        {
            return false;
        }

        if (record.LoadedAssets == null || record.IsLoading)
        {
            return false;
        }

        List<T> typedList = new List<T>(record.LoadedAssets.Count);
        for (int i = 0; i < record.LoadedAssets.Count; i++)
        {
            if (record.LoadedAssets[i] is T typedAsset)
            {
                typedList.Add(typedAsset);
            }
        }

        assets = typedList;
        return typedList.Count > 0;
    }

    /// <summary>
    /// 尝试同步获取已经加载完成的资源。
    /// 不触发加载，不增加引用计数，适合 UI 刷新阶段读取缓存。
    /// </summary>
    /// <typeparam name="T">资源类型。</typeparam>
    /// <param name="address">Addressables Address。</param>
    /// <param name="asset">输出资源对象。</param>
    /// <returns>资源已经加载完成且类型匹配时返回 true，否则返回 false。</returns>
    public bool TryGetLoaded<T>(string address, out T asset) where T : Object
    {
        asset = null;

        if (string.IsNullOrEmpty(address))
        {
            return false;
        }

        if (!_recordsByAddress.TryGetValue(address, out AssetRecord record))
        {
            return false;
        }

        if (record.IsLoading || record.LoadedAsset == null)
        {
            return false;
        }

        asset = record.LoadedAsset as T;
        return asset != null;
    }

    /// <summary>
    /// 判断指定 Address 是否正在加载中。
    /// </summary>
    /// <param name="address">Addressables Address。</param>
    /// <returns>存在加载记录且尚未完成时返回 true，否则返回 false。</returns>
    public bool IsLoading(string address)
    {
        if (string.IsNullOrEmpty(address))
        {
            return false;
        }

        return _recordsByAddress.TryGetValue(address, out AssetRecord record) && record.IsLoading;
    }

    /// <summary>
    /// 判断指定 Address 是否已经加载完成。
    /// </summary>
    /// <param name="address">Addressables Address。</param>
    /// <returns>存在有效句柄且加载成功时返回 true，否则返回 false。</returns>
    public bool IsLoaded(string address)
    {
        if (string.IsNullOrEmpty(address))
        {
            return false;
        }

        return _recordsByAddress.TryGetValue(address, out AssetRecord record)
            && !record.IsLoading
            && record.LoadedAsset != null;
    }

    /// <summary>
    /// 主动增加指定资源在指定作用域下的一次引用。
    /// 适合同一资源已经加载完成，但新的业务对象需要延长其生命周期的场景。
    /// </summary>
    /// <param name="address">Addressables Address。</param>
    /// <param name="scope">资源生命周期作用域。</param>
    public void Retain(string address, AddressableAssetScope scope)
    {
        if (string.IsNullOrEmpty(address))
        {
            return;
        }

        if (_recordsByAddress.TryGetValue(address, out AssetRecord record))
        {
            AddReference(record, scope);
        }
    }

    /// <summary>
    /// 释放指定资源在指定作用域下的一次引用。
    /// 引用计数归零时真正释放 Addressables Handle。
    /// </summary>
    /// <param name="address">Addressables Address。</param>
    /// <param name="scope">资源生命周期作用域。</param>
    public void Release(string address, AddressableAssetScope scope)
    {
        if (string.IsNullOrEmpty(address))
        {
            return;
        }

        if (!_recordsByAddress.TryGetValue(address, out AssetRecord record))
        {
            return;
        }

        int scopeIndex = GetScopeIndex(scope);
        if (record.RefCountsByScope[scopeIndex] <= 0)
        {
            return;
        }

        record.RefCountsByScope[scopeIndex]--;
        record.TotalRefCount--;

        if (record.TotalRefCount <= 0)
        {
            ReleaseRecord(address, record);
        }
    }

    /// <summary>
    /// 释放指定资源的一次引用。
    /// 该重载会按 Temporary、Daily、Combat、Main、Global 的顺序扣减一个已有引用，适合调用方不关心具体作用域的兜底释放。
    /// </summary>
    /// <param name="address">Addressables Address。</param>
    public void Release(string address)
    {
        if (string.IsNullOrEmpty(address))
        {
            return;
        }

        if (!_recordsByAddress.TryGetValue(address, out AssetRecord record))
        {
            return;
        }

        for (int i = ScopeCount - 1; i >= 0; i--)
        {
            if (record.RefCountsByScope[i] <= 0)
            {
                continue;
            }

            record.RefCountsByScope[i]--;
            record.TotalRefCount--;
            break;
        }

        if (record.TotalRefCount <= 0)
        {
            ReleaseRecord(address, record);
        }
    }

    /// <summary>
    /// 释放指定作用域下的全部资源引用。
    /// 常用于退出战斗、退出每日关、关闭主界面大流程等明确生命周期边界。
    /// </summary>
    /// <param name="scope">需要释放的资源作用域。</param>
    public void ReleaseScope(AddressableAssetScope scope)
    {
        int scopeIndex = GetScopeIndex(scope);
        _releaseBuffer.Clear();

        foreach (KeyValuePair<string, AssetRecord> pair in _recordsByAddress)
        {
            AssetRecord record = pair.Value;
            int scopedRefCount = record.RefCountsByScope[scopeIndex];
            if (scopedRefCount <= 0)
            {
                continue;
            }

            record.RefCountsByScope[scopeIndex] = 0;
            record.TotalRefCount -= scopedRefCount;

            if (record.TotalRefCount <= 0)
            {
                _releaseBuffer.Add(pair.Key);
            }
        }

        for (int i = 0; i < _releaseBuffer.Count; i++)
        {
            string address = _releaseBuffer[i];
            if (_recordsByAddress.TryGetValue(address, out AssetRecord record))
            {
                ReleaseRecord(address, record);
            }
        }

        _releaseBuffer.Clear();
    }

    /// <summary>
    /// 释放全部 Addressables 大资源。
    /// 通常只应在退出游戏、切换账号或需要强制清空大资源缓存时调用。
    /// </summary>
    public void ReleaseAll()
    {
        // 一次性清空扩展方法持有的所有 Addressables 句柄。
        // 注意：GameEntry.Resource 在 GameEntry.OnDestroy 期间可能已被销毁，做 null 检查防御。
        if (GameEntry.Resource != null)
        {
            GameEntry.Resource.ReleaseAllAddressableAssets();
        }

        foreach (KeyValuePair<string, AssetRecord> pair in _recordsByAddress)
        {
            AssetRecord record = pair.Value;
            record.PendingRequests.Clear();
            record.PendingLabelRequests.Clear();
            record.LoadedAsset = null;
            record.LoadedAssets = null;
            record.LoadedAssetsRaw = null;
        }

        _recordsByAddress.Clear();
        _releaseBuffer.Clear();
    }

    /// <summary>
    /// 获取指定资源当前总引用计数。
    /// </summary>
    /// <param name="address">Addressables Address。</param>
    /// <returns>存在缓存记录时返回总引用计数，否则返回 0。</returns>
    public int GetRefCount(string address)
    {
        if (string.IsNullOrEmpty(address))
        {
            return 0;
        }

        return _recordsByAddress.TryGetValue(address, out AssetRecord record) ? record.TotalRefCount : 0;
    }

    /// <summary>
    /// 获取指定资源在指定作用域下的引用计数。
    /// </summary>
    /// <param name="address">Addressables Address。</param>
    /// <param name="scope">资源生命周期作用域。</param>
    /// <returns>存在缓存记录时返回该作用域引用计数，否则返回 0。</returns>
    public int GetRefCount(string address, AddressableAssetScope scope)
    {
        if (string.IsNullOrEmpty(address))
        {
            return 0;
        }

        if (!_recordsByAddress.TryGetValue(address, out AssetRecord record))
        {
            return 0;
        }

        return record.RefCountsByScope[GetScopeIndex(scope)];
    }

    /// <summary>
    /// 单资源加载成功回调。
    /// 由 GameEntry.Resource.LoadAddressableAssetAsync 完成后调入；userData 即资源 Address。
    /// </summary>
    /// <typeparam name="T">资源类型。</typeparam>
    /// <param name="address">资源 Address。</param>
    /// <param name="asset">加载完成的资源对象。</param>
    /// <param name="userData">透传的 userData，等于 address。</param>
    private void OnAddressableLoadSuccess<T>(string address, T asset, object userData) where T : Object
    {
        if (!_recordsByAddress.TryGetValue(address, out AssetRecord record))
        {
            return;
        }

        record.IsLoading = false;
        record.LoadedAsset = asset;

        if (record.LoadedAsset == null)
        {
            InvokePendingFailure(address, record, "Loaded asset is null.");
            ReleaseRecord(address, record);
            return;
        }

        for (int i = 0; i < record.PendingRequests.Count; i++)
        {
            record.PendingRequests[i].InvokeSuccess(address, record.LoadedAsset);
        }

        record.PendingRequests.Clear();
    }

    /// <summary>
    /// 单资源加载失败回调。
    /// 由 GameEntry.Resource.LoadAddressableAssetAsync 失败时调入；负责派发 PendingRequests 的失败通知并清掉记录。
    /// </summary>
    /// <param name="address">资源 Address。</param>
    /// <param name="errorMessage">失败原因。</param>
    /// <param name="userData">透传的 userData，等于 address。</param>
    private void OnAddressableLoadFailure(string address, string errorMessage, object userData)
    {
        if (!_recordsByAddress.TryGetValue(address, out AssetRecord record))
        {
            return;
        }

        record.IsLoading = false;
        InvokePendingFailure(address, record, errorMessage);
        ReleaseRecord(address, record);
    }

    /// <summary>
    /// 标签批量加载成功回调。
    /// 由 GameEntry.Resource.LoadAddressableAssetsAsyncByLabel 完成后调入；userData 即 label。
    /// </summary>
    /// <typeparam name="T">资源类型。</typeparam>
    /// <param name="label">资源 Label。</param>
    /// <param name="assets">加载完成的资源列表（Addressables 内部 IList&lt;T&gt;，必须保留原始引用以便释放）。</param>
    /// <param name="userData">透传的 userData，等于 label。</param>
    private void OnAddressableLabelLoadSuccess<T>(string label, IList<T> assets, object userData) where T : Object
    {
        if (!_recordsByAddress.TryGetValue(label, out AssetRecord record))
        {
            return;
        }

        record.IsLoading = false;

        if (assets == null || assets.Count == 0)
        {
            InvokeLabelPendingFailure(label, record, "Label loaded empty result.");
            ReleaseRecord(label, record);
            return;
        }

        // 关键：必须存原始 IList 引用作为 UnloadAddressableAssets 的反查 key，否则后续无法精确释放。
        record.LoadedAssetsRaw = assets;

        // Object 视图供 PendingLabelRequests 派发使用（同一资源可能被不同泛型类型业务请求，统一存为 Object 兜底）。
        record.LoadedAssets = new List<Object>(assets.Count);
        for (int i = 0; i < assets.Count; i++)
        {
            record.LoadedAssets.Add(assets[i]);
        }

        for (int i = 0; i < record.PendingLabelRequests.Count; i++)
        {
            record.PendingLabelRequests[i].InvokeSuccess(label, record.LoadedAssets);
        }

        record.PendingLabelRequests.Clear();
    }

    /// <summary>
    /// 标签批量加载失败回调。
    /// 由 GameEntry.Resource.LoadAddressableAssetsAsyncByLabel 失败时调入。
    /// </summary>
    /// <param name="label">资源 Label。</param>
    /// <param name="errorMessage">失败原因。</param>
    /// <param name="userData">透传的 userData，等于 label。</param>
    private void OnAddressableLabelLoadFailure(string label, string errorMessage, object userData)
    {
        if (!_recordsByAddress.TryGetValue(label, out AssetRecord record))
        {
            return;
        }

        record.IsLoading = false;
        InvokeLabelPendingFailure(label, record, errorMessage);
        ReleaseRecord(label, record);
    }

    /// <summary>
    /// 为资源记录增加一次指定作用域引用。
    /// </summary>
    /// <param name="record">资源缓存记录。</param>
    /// <param name="scope">资源生命周期作用域。</param>
    private void AddReference(AssetRecord record, AddressableAssetScope scope)
    {
        int scopeIndex = GetScopeIndex(scope);
        record.RefCountsByScope[scopeIndex]++;
        record.TotalRefCount++;
    }

    /// <summary>
    /// 将业务回调追加到资源记录的等待队列。
    /// </summary>
    /// <typeparam name="T">资源类型。</typeparam>
    /// <param name="record">资源缓存记录。</param>
    /// <param name="successCallback">加载成功回调。</param>
    /// <param name="failureCallback">加载失败回调。</param>
    /// <param name="userData">业务自定义数据。</param>
    private void AddPendingRequest<T>(AssetRecord record, Action<string, T, object> successCallback, Action<string, string, object> failureCallback, object userData) where T : Object
    {
        if (successCallback == null && failureCallback == null)
        {
            return;
        }

        record.PendingRequests.Add(new LoadRequest<T>
        {
            SuccessCallback = successCallback,
            FailureCallback = failureCallback,
            UserData = userData,
        });
    }

    /// <summary>
    /// 将标签批量加载回调追加到资源记录的等待队列。
    /// </summary>
    private void AddPendingLabelRequest<T>(AssetRecord record, Action<string, IList<T>, object> successCallback, Action<string, string, object> failureCallback, object userData) where T : Object
    {
        if (successCallback == null && failureCallback == null)
        {
            return;
        }

        record.PendingLabelRequests.Add(new LabelLoadRequest<T>
        {
            SuccessCallback = successCallback,
            FailureCallback = failureCallback,
            UserData = userData,
        });
    }

    /// <summary>
    /// 对已加载缓存资源立即分发标签批量成功回调。
    /// </summary>
    private void InvokeLabelSuccess<T>(string label, IList<Object> assets, Action<string, IList<T>, object> successCallback, object userData) where T : Object
    {
        if (successCallback == null)
        {
            return;
        }

        List<T> typedList = new List<T>(assets.Count);
        for (int i = 0; i < assets.Count; i++)
        {
            if (assets[i] is T typedAsset)
            {
                typedList.Add(typedAsset);
            }
        }

        successCallback(label, typedList, userData);
    }

    /// <summary>
    /// 对已加载缓存资源立即分发成功回调。
    /// </summary>
    /// <typeparam name="T">资源类型。</typeparam>
    /// <param name="address">资源 Address。</param>
    /// <param name="asset">已加载资源对象。</param>
    /// <param name="successCallback">加载成功回调。</param>
    /// <param name="failureCallback">加载失败回调。</param>
    /// <param name="userData">业务自定义数据。</param>
    private void InvokeImmediateSuccess<T>(string address, Object asset, Action<string, T, object> successCallback, Action<string, string, object> failureCallback, object userData) where T : Object
    {
        T typedAsset = asset as T;
        if (typedAsset == null)
        {
            if (failureCallback != null)
            {
                failureCallback(address, "Loaded asset type does not match requested type.", userData);
            }

            return;
        }

        if (successCallback != null)
        {
            successCallback(address, typedAsset, userData);
        }
    }

    /// <summary>
    /// 对等待队列中的所有请求分发失败回调。
    /// </summary>
    /// <param name="address">资源 Address。</param>
    /// <param name="record">资源缓存记录。</param>
    /// <param name="errorMessage">失败原因。</param>
    private void InvokePendingFailure(string address, AssetRecord record, string errorMessage)
    {
        for (int i = 0; i < record.PendingRequests.Count; i++)
        {
            record.PendingRequests[i].InvokeFailure(address, errorMessage);
        }

        record.PendingRequests.Clear();
    }

    /// <summary>
    /// 对标签加载等待队列中的所有请求分发失败回调。
    /// </summary>
    private void InvokeLabelPendingFailure(string label, AssetRecord record, string errorMessage)
    {
        for (int i = 0; i < record.PendingLabelRequests.Count; i++)
        {
            record.PendingLabelRequests[i].InvokeFailure(label, errorMessage);
        }

        record.PendingLabelRequests.Clear();
    }

    /// <summary>
    /// 释放单条资源记录。
    /// </summary>
    /// <param name="address">资源 Address。</param>
    /// <param name="record">资源缓存记录。</param>
    private void ReleaseRecord(string address, AssetRecord record)
    {
        // 单资源句柄释放：通过扩展方法反查 Addressables Handle。
        // GameEntry.Resource 在 OnDestroy 期间可能已被销毁，做 null 检查防御。
        if (record.LoadedAsset != null && GameEntry.Resource != null)
        {
            GameEntry.Resource.UnloadAddressableAsset(record.LoadedAsset);
        }

        // 标签批量句柄释放：必须用原始 IList 引用作为 unload key（即 LoadedAssetsRaw）。
        if (record.LoadedAssetsRaw != null && GameEntry.Resource != null)
        {
            GameEntry.Resource.UnloadAddressableAssets(record.LoadedAssetsRaw);
        }

        record.PendingRequests.Clear();
        record.PendingLabelRequests.Clear();
        record.LoadedAsset = null;
        record.LoadedAssets = null;
        record.LoadedAssetsRaw = null;
        _recordsByAddress.Remove(address);
    }

    /// <summary>
    /// 将作用域枚举转换为数组下标。
    /// </summary>
    /// <param name="scope">资源生命周期作用域。</param>
    /// <returns>合法数组下标。</returns>
    private int GetScopeIndex(AddressableAssetScope scope)
    {
        int index = (int)scope;
        if (index < 0 || index >= ScopeCount)
        {
            return (int)AddressableAssetScope.Temporary;
        }

        return index;
    }
}
