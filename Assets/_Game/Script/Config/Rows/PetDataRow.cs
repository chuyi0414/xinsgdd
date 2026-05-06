using System;
using System.Text;
using UnityGameFramework.Runtime;

/// <summary>
/// 宠物系统数据表行。
/// </summary>
public sealed class PetDataRow : DataRowBase, ICodeDataRow
{
    /// <summary>
    /// 列拆分分隔符。
    /// </summary>
    private static readonly string[] ColumnSplitSeparator = { "\t" };

    /// <summary>
    /// 数据表固定列数。
    /// 新增 EatFruitCount / ProduceProbability 后为 15
    /// （Id Code Name Quality EntitySkeletonDataPath UiSkeletonDataPath IdleAnimationName MoveAnimationName GiveGoldAnimationName AttributeType AttributeValue RequiredStars EatFruitCount ProduceProbability Description）。
    /// </summary>
    private const int ColumnCount = 15;

    /// <summary>
    /// 合法宠物 Code 的前缀。
    /// </summary>
    private const string CodePrefix = "pet_";

    /// <summary>
    /// 当前行的内部 Id 缓存。
    /// </summary>
    private int _id;

    /// <summary>
    /// 宠物唯一 Id。
    /// </summary>
    public override int Id => _id;

    /// <summary>
    /// 机器码。
    /// </summary>
    public string Code { get; private set; }

    /// <summary>
    /// 显示名称。
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// 宠物品质。
    /// </summary>
    public QualityType Quality { get; private set; }

    /// <summary>
    /// 实体使用的 Spine SkeletonData 资源路径。
    /// 场景里的宠物实体只读取这条路径。
    /// </summary>
    public string EntitySkeletonDataPath { get; private set; }

    /// <summary>
    /// UI 使用的 Spine SkeletonData 资源路径。
    /// 图鉴、详情面板等 UI 角色只读取这条路径。
    /// </summary>
    public string UiSkeletonDataPath { get; private set; }

    /// <summary>
    /// 待机动画名。
    /// </summary>
    public string IdleAnimationName { get; private set; }

    /// <summary>
    /// 移动动画名。
    /// </summary>
    public string MoveAnimationName { get; private set; }

    /// <summary>
    /// 宠物吃完饭后用于表现奖励产出的非循环动画名。
    /// 当前表内 LuLu 使用 Attack1，其余宠物使用 Attack。
    /// </summary>
    public string GiveGoldAnimationName { get; private set; }

    /// <summary>
    /// 属性类型。
    /// </summary>
    public PetAttributeType AttributeType { get; private set; }

    /// <summary>
    /// 属性数值。
    /// </summary>
    public int AttributeValue { get; private set; }

    /// <summary>
    /// 该宠物进入孵化候选池所需的玩家累计星星阈值（含）。
    /// 0 表示无星星限制；运行时按品质抽取宠物时，会过滤掉 RequiredStars > GameEntry.Fruits.CurrentStars 的项。
    /// 与 EggDataRow.RequiredStars / ArchitectureSlotDataRow.RequiredStars 同语义，方便策划在不同维度统一调控解锁节奏。
    /// </summary>
    public int RequiredStars { get; private set; }

    /// <summary>
    /// 一只宠物从 Spawn 到离场的总进餐次数（即“吃水果次数”）。
    /// 必须为正整数；运行时由 PetRuntimeState.RemainingEatFruitCount 跟踪剩余次数。
    /// 进餐节奏：每次完整走完“上桌→生产→吃→奖励动画”流程消耗 1 次。
    /// </summary>
    public int EatFruitCount { get; private set; }

    /// <summary>
    /// 每次吃完水果时单独掷一次的“是否掉落产出物”概率（0-100 整数）。
    /// 该判定与 FruitDataRow.CoinProbability 完全独立，不再使用旧的“互斥分支”模型。
    /// 命中后，再从 PetProduce 同 PetId 池中等概率随机挑 1 条产出物。
    /// </summary>
    public int ProduceProbability { get; private set; }

    /// <summary>
    /// 备注描述。
    /// </summary>
    public string Description { get; private set; }

    /// <summary>
    /// 从文本行解析宠物表数据。
    /// </summary>
    /// <param name="dataRowString">原始数据行文本。</param>
    /// <param name="userData">额外上下文。</param>
    /// <returns>是否解析成功。</returns>
    public override bool ParseDataRow(string dataRowString, object userData)
    {
        if (string.IsNullOrWhiteSpace(dataRowString))
        {
            Log.Warning("PetDataRow parse failed because row string is empty.");
            return false;
        }

        string[] columns = dataRowString.Split(ColumnSplitSeparator, StringSplitOptions.None);
        if (columns.Length != ColumnCount)
        {
            Log.Warning("PetDataRow parse failed because column count '{0}' is invalid, row '{1}'.", columns.Length, dataRowString);
            return false;
        }

        if (!int.TryParse(columns[0], out int id) || id <= 0)
        {
            Log.Warning("PetDataRow parse failed because Id '{0}' is invalid.", columns[0]);
            return false;
        }

        string code = columns[1].Trim();
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(CodePrefix, StringComparison.Ordinal))
        {
            Log.Warning("PetDataRow parse failed because Code '{0}' is invalid.", columns[1]);
            return false;
        }

        string name = columns[2].Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Log.Warning("PetDataRow parse failed because Name is empty, code '{0}'.", code);
            return false;
        }

        if (!Enum.TryParse(columns[3].Trim(), true, out QualityType quality) || !Enum.IsDefined(typeof(QualityType), quality) || quality == QualityType.Universal)
        {
            Log.Warning("PetDataRow parse failed because Quality '{0}' is invalid, code '{1}'.", columns[3], code);
            return false;
        }

        string entitySkeletonDataPath = columns[4].Trim();
        if (string.IsNullOrWhiteSpace(entitySkeletonDataPath))
        {
            Log.Warning("PetDataRow parse failed because EntitySkeletonDataPath is empty, code '{0}'.", code);
            return false;
        }

        string uiSkeletonDataPath = columns[5].Trim();
        if (string.IsNullOrWhiteSpace(uiSkeletonDataPath))
        {
            Log.Warning("PetDataRow parse failed because UiSkeletonDataPath is empty, code '{0}'.", code);
            return false;
        }

        string idleAnimationName = columns[6].Trim();
        if (string.IsNullOrWhiteSpace(idleAnimationName))
        {
            Log.Warning("PetDataRow parse failed because IdleAnimationName is empty, code '{0}'.", code);
            return false;
        }

        string moveAnimationName = columns[7].Trim();
        if (string.IsNullOrWhiteSpace(moveAnimationName))
        {
            Log.Warning("PetDataRow parse failed because MoveAnimationName is empty, code '{0}'.", code);
            return false;
        }

        string giveGoldAnimationName = columns[8].Trim();
        if (string.IsNullOrWhiteSpace(giveGoldAnimationName))
        {
            Log.Warning("PetDataRow parse failed because GiveGoldAnimationName is empty, code '{0}'.", code);
            return false;
        }

        if (!Enum.TryParse(columns[9].Trim(), true, out PetAttributeType attributeType) || !Enum.IsDefined(typeof(PetAttributeType), attributeType))
        {
            Log.Warning("PetDataRow parse failed because AttributeType '{0}' is invalid, code '{1}'.", columns[9], code);
            return false;
        }

        if (!int.TryParse(columns[10], out int attributeValue))
        {
            Log.Warning("PetDataRow parse failed because AttributeValue '{0}' is invalid, code '{1}'.", columns[10], code);
            return false;
        }

        if (attributeType == PetAttributeType.None && attributeValue != 0)
        {
            Log.Warning("PetDataRow parse failed because AttributeType None requires AttributeValue 0, code '{0}'.", code);
            return false;
        }

        if (attributeType != PetAttributeType.None && attributeValue <= 0)
        {
            Log.Warning("PetDataRow parse failed because AttributeValue must be > 0, code '{0}'.", code);
            return false;
        }

        // RequiredStars：进入孵化候选池所需的玩家星星阈值；0 表示不限，负值不合法。
        // 之所以放在 AttributeValue 与 EatFruitCount 之间，是为了与 Egg/ArchitectureSlot/ArchitectureUpgrade 三表的列序保持完全一致，便于策划维护。
        if (!int.TryParse(columns[11], out int requiredStars) || requiredStars < 0)
        {
            Log.Warning("PetDataRow parse failed because RequiredStars '{0}' is invalid, code '{1}'.", columns[11], code);
            return false;
        }

        // EatFruitCount：必须为正整数。0 等价于“宠物不会吃饭”，没有任何业务意义，禁止配置。
        if (!int.TryParse(columns[12], out int eatFruitCount) || eatFruitCount <= 0)
        {
            Log.Warning("PetDataRow parse failed because EatFruitCount '{0}' is invalid, code '{1}'.", columns[12], code);
            return false;
        }

        // ProduceProbability：0-100 整数；0 表示宠物永远不掉产出物，100 表示每次必掉。
        if (!int.TryParse(columns[13], out int produceProbability) || produceProbability < 0 || produceProbability > 100)
        {
            Log.Warning("PetDataRow parse failed because ProduceProbability '{0}' is invalid, code '{1}'.", columns[13], code);
            return false;
        }

        _id = id;
        Code = code;
        Name = name;
        Quality = quality;
        EntitySkeletonDataPath = entitySkeletonDataPath;
        UiSkeletonDataPath = uiSkeletonDataPath;
        IdleAnimationName = idleAnimationName;
        MoveAnimationName = moveAnimationName;
        GiveGoldAnimationName = giveGoldAnimationName;
        AttributeType = attributeType;
        AttributeValue = attributeValue;
        RequiredStars = requiredStars;
        EatFruitCount = eatFruitCount;
        ProduceProbability = produceProbability;
        Description = columns[14].Trim();
        return true;
    }

    /// <summary>
    /// 从二进制数据解析宠物表数据。
    /// </summary>
    /// <param name="dataRowBytes">原始字节数组。</param>
    /// <param name="startIndex">起始下标。</param>
    /// <param name="length">读取长度。</param>
    /// <param name="userData">额外上下文。</param>
    /// <returns>是否解析成功。</returns>
    public override bool ParseDataRow(byte[] dataRowBytes, int startIndex, int length, object userData)
    {
        return ParseDataRow(Encoding.UTF8.GetString(dataRowBytes, startIndex, length), userData);
    }
}
