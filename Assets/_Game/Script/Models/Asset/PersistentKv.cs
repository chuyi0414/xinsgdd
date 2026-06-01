using UnityEngine;
using UnityGameFramework.Runtime;
#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
using WeChatWASM;
#endif

/// <summary>
/// 跨平台持久化 KV 存储工具。
/// 设计动机：微信小游戏环境下 vconsole 启动会打印
///           "IndexedDB is not available. Data will not persist in cache and PlayerPrefs will not be saved."
///           但实际上微信 Unity SDK（WX-WASM-SDK-V2）已经在 storage.js 中把 PlayerPrefs 重定向到 wx.setStorageSync，
///           因此 PlayerPrefs.SetString / GetString 在小游戏侧底层就是 wx.setStorageSync / wx.getStorageSync，
///           这条警告是 SDK 模板固定输出的"假告警"，实际可正常持久化。
/// 
/// 但是！微信开发者工具在某些"编译运行/版本切换"操作下会清空小游戏 Storage（仅限开发者工具，真机不会），
/// 这会让开发者误以为 PlayerPrefs 失效。真机调试是唯一可信的验证方式。
/// 
/// 当前实现策略：
/// 　- 微信小游戏运行时双写 PlayerPrefs + WX.StorageXxxStringSync，确保两条链路都落盘；
/// 　- 读取时优先尝试 WX.StorageGetStringSync（直接走 wx.getStorageSync，绕过任何 SDK 中间层），
/// 　  fallback 到 PlayerPrefs.GetString。
/// 　- 编辑器与原生平台直接走 PlayerPrefs，开发体验不变。
/// </summary>
public static class PersistentKv
{
    /// <summary>
    /// 读取字符串值。
    /// </summary>
    /// <param name="key">持久化键名。</param>
    /// <param name="defaultValue">键不存在时返回的默认值。</param>
    /// <returns>已持久化的字符串；未找到时返回 defaultValue。</returns>
    public static string GetString(string key, string defaultValue = "")
    {
#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
        // 微信小游戏：直接走 wx.getStorageSync，绕过任何中间层，确保拿到磁盘真值。
        // 若 wx 侧无值，再 fallback 到 PlayerPrefs（如果走的是 SDK 拦截可能这里反而能命中）。
        string wxValue = WX.StorageGetStringSync(key, string.Empty);
        if (!string.IsNullOrEmpty(wxValue))
        {
            return wxValue;
        }

        string pp = PlayerPrefs.GetString(key, string.Empty);
        return string.IsNullOrEmpty(pp) ? defaultValue : pp;
#else
        return PlayerPrefs.GetString(key, defaultValue);
#endif
    }

    /// <summary>
    /// 写入字符串值。
    /// 微信小游戏：双写 PlayerPrefs 与 wx.setStorageSync，两条链路冗余兜底。
    /// </summary>
    /// <param name="key">持久化键名。</param>
    /// <param name="value">要写入的字符串值。</param>
    public static void SetString(string key, string value)
    {
#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
        // 双写：理论上微信 SDK 已经把 PlayerPrefs 重定向到 wx.setStorageSync，二者是同一份数据。
        // 但保留双写做防御：万一 SDK 拦截因配置或版本失效，wx 直写仍可命中。
        WX.StorageSetStringSync(key, value);
        PlayerPrefs.SetString(key, value);
        PlayerPrefs.Save();
#else
        PlayerPrefs.SetString(key, value);
        PlayerPrefs.Save();
#endif
    }

    /// <summary>
    /// 诊断辅助：在微信小游戏环境对比两条链路的真实值，便于排查"读不到"问题。
    /// 仅用于真机调试，正式版本可不调用。
    /// </summary>
    /// <param name="key">要诊断的键名。</param>
    public static void DiagnoseRead(string key)
    {
#if (UNITY_WEBGL || WEIXINMINIGAME) && !UNITY_EDITOR
        string wxValue = WX.StorageGetStringSync(key, "<wx-empty>");
        string ppValue = PlayerPrefs.GetString(key, "<pp-empty>");
        Log.Warning(string.Format(
            "[PersistentKv][DIAG] key='{0}' wx.getStorageSync='{1}'，PlayerPrefs.GetString='{2}'。",
            key, wxValue, ppValue));
#endif
    }
}
