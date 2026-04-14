using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI 界面定义。
/// 统一维护常用界面资源名与界面组名，避免业务层散落硬编码字符串。
/// </summary>
public static class UIFormDefine
{
    /// <summary>
    /// 主界面组名称。
    /// </summary>
    public const string MainGroup = "Main";



    /// <summary>
    /// 加载界面资源名。
    /// </summary>
    public static readonly string LoadUIForm = AssetPath.GetUI("Load/LoadUIForm");

    /// <summary>
    /// Main界面资源名。
    /// </summary>
    public static readonly string MainUIForm = AssetPath.GetUI("Main/MainUIForm");

    /// <summary>
    /// 建筑升级界面资源名。
    /// </summary>
    public static readonly string ArchitectureUpgradeUIForm = AssetPath.GetUI("Architecture/ArchitectureUpgradeUIForm");
}
