using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI 界面定义。
/// 统一维护常用界面资源名与界面组名，避免业务层散落硬编码字符串。
/// </summary>
public static class UIFormDefine
{
    // ---- 界面组名称 ----
    // 与 MyGameFramework.prefab 中 UIComponent.m_UIGroups 配置一一对应。
    // Depth 越大渲染越靠后，视觉上覆盖低 Depth 组。

    /// <summary>
    /// 背景界面组名称。Depth=0，用于全屏背景/启动加载。
    /// </summary>
    public const string BJGroup = "BJ";

    /// <summary>
    /// 主界面组名称。Depth=100，用于常驻主界面与战斗界面。
    /// </summary>
    public const string MainGroup = "Main";

    /// <summary>
    /// 信息提示界面组名称。Depth=200，用于规则说明等非阻塞信息。
    /// </summary>
    public const string InfoGroup = "Info";

    /// <summary>
    /// 弹窗界面组名称。Depth=300，用于需要用户确认/操作的弹窗。
    /// </summary>
    public const string PopupGroup = "Popup";

    /// <summary>
    /// 轻提示界面组名称。Depth=400，用于 Toast 临时提示。
    /// </summary>
    public const string ToastGroup = "Toast";

    /// <summary>
    /// 引导界面组名称。Depth=500，用于新手引导遮罩。
    /// </summary>
    public const string GuideGroup = "Guide";

    /// <summary>
    /// 加载过渡界面组名称。Depth=600，用于全屏加载遮罩，覆盖所有业务界面。
    /// </summary>
    public const string LoadingGroup = "Loading";

    /// <summary>
    /// 顶层界面组名称。Depth=700，用于断线重连等必须置顶的界面。
    /// </summary>
    public const string TopGroup = "Top";

    // ---- 界面资源路径 ----
    // 每个路径后的注释标注该界面应打开到哪个分组。

    /// <summary>
    /// 启动加载界面资源名。→ BJ
    /// </summary>
    public static readonly string LoadUIForm = AssetPath.GetUI("Load/LoadUIForm");

    /// <summary>
    /// 主界面资源名。→ Main
    /// </summary>
    public static readonly string MainUIForm = AssetPath.GetUI("Main/MainUIForm");

    /// <summary>
    /// 战斗界面资源名。→ Main
    /// </summary>
    public static readonly string CombatUIForm = AssetPath.GetUI("Combat/CombatUIForm");

    /// <summary>
    /// 每日一关界面资源名。→ Main
    /// </summary>
    public static readonly string DailyChallengeUIForm = AssetPath.GetUI("DailyChallenge/DailyChallengeUIForm");

    /// <summary>
    /// 水果图鉴界面资源名。→ Main
    /// </summary>
    public static readonly string FruitTJUIForm = AssetPath.GetUI("Fruit/FruitTJUIForm");

    /// <summary>
    /// 宠物图鉴界面资源名。→ Main
    /// </summary>
    public static readonly string PetTJUIForm = AssetPath.GetUI("Pet/PetTJUIForm");

    /// <summary>
    /// 消除规则说明界面资源名。→ Info
    /// </summary>
    public static readonly string EliminateRulesUIForm = AssetPath.GetUI("Combat/EliminateRulesUIForm");

    /// <summary>
    /// 退出确认界面资源名。→ Popup
    /// </summary>
    public static readonly string IsExitUIForm = AssetPath.GetUI("Combat/IsExitUIForm");

    /// <summary>
    /// 胜利/失败弹窗界面资源名。→ Popup
    /// </summary>
    public static readonly string VictoryFailUIForm = AssetPath.GetUI("Combat/VictoryFailUIForm");

    /// <summary>
    /// 复活确认界面资源名。→ Popup
    /// </summary>
    public static readonly string ResurgenceUIForm = AssetPath.GetUI("Combat/ResurgenceUIForm");

    /// <summary>
    /// 道具购买界面资源名。→ Popup
    /// </summary>
    public static readonly string PropPurchaseUIForm = AssetPath.GetUI("Combat/PropPurchaseUIForm");

    /// <summary>
    /// 通用购买确认弹窗资源名。→ Popup
    /// </summary>
    public static readonly string PurchaseUIForm = AssetPath.GetUI("Purchase/PurchaseUIForm");

    /// <summary>
    /// 购买蛋界面资源名。→ Popup
    /// </summary>
    public static readonly string PurchaseEggsUIForm = AssetPath.GetUI("Eggs/PurchaseEggsUIForm");

    /// <summary>
    /// 建筑升级界面资源名。→ Popup
    /// </summary>
    public static readonly string ArchitectureUpgradeUIForm = AssetPath.GetUI("Architecture/ArchitectureUpgradeUIForm");

    /// <summary>
    /// 新人礼包界面资源名。→ Popup
    /// </summary>
    public static readonly string NewcomerPackageUIForm = AssetPath.GetUI("NewcomerPackage/NewcomerPackageUIform");

    /// <summary>
    /// 个人设置界面资源名。→ Popup
    /// </summary>
    public static readonly string PersonalSettingUIForm = AssetPath.GetUI("PersonalSetting/PersonalSettingUIForm");

    /// <summary>
    /// 通用轻提示界面资源名。→ Toast
    /// </summary>
    public static readonly string ToastUIForm = AssetPath.GetUI("Toast/ToastUIForm");

    /// <summary>
    /// 战斗加载过渡界面资源名。→ Loading
    /// </summary>
    public static readonly string LoadingUIForm = AssetPath.GetUI("Loading/LoadingUIForm");
}
