using UnityGameFramework.Runtime;

/// <summary>
/// Hybrid 资源辅助器：在 DefaultResourceHelper 基础上叠加 Addressables 句柄精确释放。
/// 由 GF 内核 ResourceManager 在 m_AssetPool 引用归零时调 IResourceHelper.Release(asset)，
/// 本类先反查 Addressables 反查表：命中则 Addressables.Release(handle)，未命中走原 DefaultResourceHelper.Release（Resources 资源行为）。
/// 这样 UI/Entity/DataTable/Sound 通过 GF 加载的 Addressables 资源也能在引用归零时精确释放，零句柄泄漏。
/// </summary>
public sealed class HybridResourceHelper : DefaultResourceHelper
{
    /// <summary>
    /// 释放资源。
    /// 优先尝试 Addressables 反查；未命中走 DefaultResourceHelper.Release（其内部对 AssetBundle 做 Unload，对普通资源 no-op）。
    /// </summary>
    /// <param name="objectToRelease">要释放的资源对象（GF 内核传入，类型为 object）。</param>
    public override void Release(object objectToRelease)
    {
        // ⚠️ 关键：必须先尝试 Addressables 句柄释放，避免漏调 Addressables.Release 导致句柄长期泄漏。
        UnityEngine.Object asset = objectToRelease as UnityEngine.Object;
        if (asset != null && ResourceComponentExtensions.TryReleaseAddressablesHandle(asset))
        {
            return;
        }

        // 未命中 Addressables 反查 → 资源是经由 Resources.LoadAsync 加载的，走父类原始行为。
        base.Release(objectToRelease);
    }
}
