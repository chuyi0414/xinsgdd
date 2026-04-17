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
    /// 新人礼包界面资源名。
    /// </summary>
    public static readonly string NewcomerPackageUIForm = AssetPath.GetUI("NewcomerPackage/NewcomerPackageUIform");

    /// <summary>
    /// 购买蛋界面资源名。
    /// </summary>
    public static readonly string PurchaseEggsUIForm = AssetPath.GetUI("Eggs/PurchaseEggsUIForm");

    /// <summary>
    /// 建筑升级界面资源名。
    /// </summary>
    public static readonly string ArchitectureUpgradeUIForm = AssetPath.GetUI("Architecture/ArchitectureUpgradeUIForm");

    /// <summary>
    /// 每日一关界面资源名。
    /// </summary>
    public static readonly string DailyChallengeUIForm = AssetPath.GetUI("DailyChallenge/DailyChallengeUIForm");

    /// <summary>
    /// 水果图鉴界面资源名。
    /// </summary>
    public static readonly string FruitTJUIForm = AssetPath.GetUI("Fruit/FruitTJUIForm");

    /// <summary>
    /// 宠物图鉴界面资源名。
    /// </summary>
    public static readonly string PetTJUIForm = AssetPath.GetUI("Pet/PetTJUIForm");

    /// <summary>
    /// 战斗界面资源名。
    /// </summary>
    public static readonly string CombatUIForm = AssetPath.GetUI("Combat/CombatUIForm");

    /// <summary>
    /// 退出确认界面资源名。
    /// </summary>
    public static readonly string IsExitUIForm = AssetPath.GetUI("Combat/IsExitUIForm");

    /// <summary>
    /// 消除规则说明界面资源名。
    /// </summary>
    public static readonly string EliminateRulesUIForm = AssetPath.GetUI("Combat/EliminateRulesUIForm");

    /// <summary>
    /// 胜利/失败弹窗界面资源名。
    /// </summary>
    public static readonly string VictoryFailUIForm = AssetPath.GetUI("Combat/VictoryFailUIForm");
}
