using System;
using UnityGameFramework.Runtime;
#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
using WeChatWASM;
#endif

/// <summary>
/// 微信小游戏 AssetBundle 缓存运维工具。
/// 该类只在微信小游戏运行时真正生效；编辑器与其他平台调用全部 no-op，
/// 业务侧无须散落 #if WEIXINMINIGAME 宏，调用代码可保持单分支。
/// </summary>
public static class WechatBundleCacheUtility
{
    /// <summary>
    /// 打印当前微信缓存的内存/磁盘统计。
    /// 用法：真机首启 vs 二启分别在加载流程结束打一次，二启 diskBundles 不为 0 即说明缓存命中正常。
    /// </summary>
    /// <param name="tag">日志前缀标记，便于在长日志里检索。</param>
    public static void LogCacheStats(string tag)
    {
#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
        // 这四个 API 都是 jslib 透出的同步调用，不分配托管堆，可放在加载主链路上随便打。
        Log.Info(string.Format(
            "[WXCache][{0}] memBundles={1} memBytes={2} diskBundles={3} diskBytes={4} pluginCachePath={5}",
            tag,
            WX.GetBundleNumberInMemory(),
            WX.GetBundleSizeInMemory(),
            WX.GetBundleNumberOnDisk(),
            WX.GetBundleSizeOnDisk(),
            WX.PluginCachePath));
#endif
    }

    /// <summary>
    /// 打印托管堆与 Unity 总内存分配/保留量。
    /// 每处关键流程节点打一次，对比差值即可判断内存峰值来源。
    /// </summary>
    /// <param name="tag">日志前缀标记。</param>
    public static void LogWasmMemory(string tag)
    {
        long managed = System.GC.GetTotalMemory(false);
        long totalAllocated = (long)UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
        long totalReserved = (long)UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
        Log.Info(string.Format(
            "[WASM MEM][{0}] managed={1:F1}MB allocated={2:F1}MB reserved={3:F1}MB",
            tag,
            managed / 1048576.0,
            totalAllocated / 1048576.0,
            totalReserved / 1048576.0));
    }

    /// <summary>
    /// 清空微信小游戏文件缓存（包含 AssetBundle、纹理等所有 __GAME_FILE_CACHE 内容）。
    /// 仅在版本切换或资源损坏检测到不一致时调用，常规启动绝对不要调，否则下次冷启会触发整包重下。
    /// 该调用是异步的，不会阻塞当前帧；本次启动依旧走老缓存，下次启动开始走新版本资源。
    /// </summary>
    /// <param name="onComplete">清理完成回调；参数 true 表示底层成功，false 表示底层失败。</param>
    public static void CleanAll(Action<bool> onComplete = null)
    {
#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
        // ⚠️ 弱网下大规模清缓存后冷启会非常痛苦（要重下所有远程 bundle），
        //    因此只允许在 resourceVersion 变化等"必要"事件触发。
        WX.CleanAllFileCache(success =>
        {
            Log.Warning("[WXCache] CleanAllFileCache result=" + success);
            onComplete?.Invoke(success);
        });
#else
        // 编辑器/PC/移动原生平台没有微信缓存概念，直接派发成功避免业务分支。
        onComplete?.Invoke(true);
#endif
    }
}
