//------------------------------------------------------------
// Game Framework
// Project Extension: Addressables fallback diagnostics.
//------------------------------------------------------------

using System.Collections.Generic;

namespace GameFramework.Resource
{
    /// <summary>
    /// Addressables 路由回退诊断缓存。
    /// 用途：当 Addressables 自动路由没有接管资源加载并回退到 GF 主链路时，
    /// 先把“为什么没有走 Addressables”的原因暂存下来；如果后续 Resources 兜底也失败，
    /// ResourcesLoadResourceAgentHelper 会把这段原因拼进最终错误，避免日志只看到 Resources 失败。
    /// </summary>
    public static class AddressablesFallbackDiagnostic
    {
        /// <summary>
        /// 最近一次 Addressables 路由回退原因表。
        /// Key：GF 资源名 / Addressables Address，例如 Prefabs/UI/Load/LoadUIForm。
        /// Value：Addressables 路由没有接管的具体原因。
        /// 初始容量 32：覆盖启动阶段 UI、表格、音频等常见并发加载；超出自动扩容。
        /// </summary>
        private static readonly Dictionary<string, string> s_ReasonsByAssetName = new Dictionary<string, string>(32);

        /// <summary>
        /// 记录指定资源的 Addressables 路由回退原因。
        /// </summary>
        /// <param name="assetName">GF 资源名 / Addressables Address。</param>
        /// <param name="reason">Addressables 路由没有接管的原因。</param>
        public static void SetReason(string assetName, string reason)
        {
            if (string.IsNullOrEmpty(assetName) || string.IsNullOrEmpty(reason))
            {
                return;
            }

            s_ReasonsByAssetName[assetName] = reason;
        }

        /// <summary>
        /// 尝试读取并移除指定资源的 Addressables 路由回退原因。
        /// 读取后立即移除，避免一次历史 miss 污染后续同名资源的成功加载或新错误。
        /// </summary>
        /// <param name="assetName">GF 资源名 / Addressables Address。</param>
        /// <param name="reason">输出的回退原因；未命中时为 null。</param>
        /// <returns>存在回退原因时返回 true；否则返回 false。</returns>
        public static bool TryGetAndRemoveReason(string assetName, out string reason)
        {
            reason = null;
            if (string.IsNullOrEmpty(assetName))
            {
                return false;
            }

            if (!s_ReasonsByAssetName.TryGetValue(assetName, out reason))
            {
                return false;
            }

            s_ReasonsByAssetName.Remove(assetName);
            return true;
        }

        /// <summary>
        /// 清理指定资源的 Addressables 路由回退原因。
        /// 用于 Resources 兜底加载成功时丢弃诊断，避免缓存长期持有字符串。
        /// </summary>
        /// <param name="assetName">GF 资源名 / Addressables Address。</param>
        public static void ClearReason(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                return;
            }

            s_ReasonsByAssetName.Remove(assetName);
        }
    }
}
