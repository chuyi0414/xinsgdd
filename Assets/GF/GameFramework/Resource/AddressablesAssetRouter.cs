//------------------------------------------------------------
// Game Framework
// Project Extension: 项目层 Addressables 路由委托声明。
//------------------------------------------------------------

using System;

namespace GameFramework.Resource
{
    /// <summary>
    /// Addressables 资源路由委托。
    /// 由项目层（_Game）实现并注入到 ResourceManager；GF 内核 LoadAsset 入口会优先咨询本委托：
    /// 1. 委托返回 true：表示路由已接管异步加载，必须按 LoadAssetCallbacks 协议派发完成事件，GF 主链路放弃；
    /// 2. 委托返回 false：表示当前资源不归 Addressables 管，GF 主链路（ResourceMode.Resource → Resources.LoadAsync）继续执行。
    /// </summary>
    /// <param name="assetName">资源名（与 GF.LoadAsset 同名，业务侧不感知前缀差异）。</param>
    /// <param name="assetType">资源类型，可空。</param>
    /// <param name="loadAssetCallbacks">GF 加载回调集合，路由命中时必须按其协议派发成功/失败回调。</param>
    /// <param name="userData">业务自定义数据，模块仅透传。</param>
    /// <returns>路由是否接管:true 表示已开始 Addressables 加载；false 表示走 GF 主链路。</returns>
    public delegate bool AddressablesAssetRouter(string assetName, Type assetType, LoadAssetCallbacks loadAssetCallbacks, object userData);

    /// <summary>
    /// Addressables 资源释放路由委托。
    /// 由项目层（_Game）实现并注入到 ResourceManager；GF 内核 UnloadAsset 入口会优先咨询本委托：
    /// 1. 委托返回 true：表示该 asset 是 Addressables 加载产物，路由已 Addressables.Release(handle) 完成释放，
    ///    GF 主链路必须跳过 m_AssetPool.Unspawn —— 因为 Addressables 资源根本未注册到对象池，强行 Unspawn 会抛
    ///    GameFrameworkException("Can not find target in object pool")；
    /// 2. 委托返回 false：表示该 asset 不是 Addressables 产物，GF 主链路（m_AssetPool.Unspawn → 引用归零 → Helper.Release）正常执行。
    /// </summary>
    /// <param name="asset">要释放的资源对象（GF 内核传入，类型为 object）。</param>
    /// <returns>路由是否接管：true 表示 Addressables 已精确释放；false 表示走 GF 主链路。</returns>
    public delegate bool AddressablesAssetReleaseRouter(object asset);
}
