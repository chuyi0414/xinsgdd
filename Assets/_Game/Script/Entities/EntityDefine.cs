/// <summary>
/// 实体定义。
/// 统一维护常用实体资源名与实体组名，避免业务层散落硬编码字符串。
/// </summary>
public static class EntityDefine
{
    /// <summary>
    /// 蛋实体所在分组。
    /// </summary>
    public const string EggGroup = "Item";

    /// <summary>
    /// 桌子实体所在分组。
    /// </summary>
    public const string TableGroup = "Environment";

    /// <summary>
    /// 果园实体所在分组。
    /// </summary>
    public const string OrchardGroup = "Environment";

    /// <summary>
    /// 宠物实体所在分组。
    /// </summary>
    public const string PetGroup = "Character";

    /// <summary>
    /// 餐桌水果实体所在分组。
    /// </summary>
    public const string FruitGroup = "Item";

    /// <summary>
    /// 蛋实体资源名。
    /// </summary>
    public static readonly string EggEntity = AssetPath.GetEntity("Egg/EggEntity");

    /// <summary>
    /// 桌子实体资源名。
    /// </summary>
    public static readonly string TableEntity = AssetPath.GetEntity("Table/TableEntity");

    /// <summary>
    /// 果园实体资源名。
    /// </summary>
    public static readonly string OrchardEntity = AssetPath.GetEntity("Orchard/OrchardEntity");

    /// <summary>
    /// 宠物实体资源名。
    /// </summary>
    public static readonly string PetEntity = AssetPath.GetEntity("Pet/PetEntity");

    /// <summary>
    /// 餐桌水果实体资源名。
    /// </summary>
    public static readonly string FruitEntity = AssetPath.GetEntity("Pet/FruitEntity");
}
