using System;
using System.Globalization;
using System.Text;
using UnityGameFramework.Runtime;

/// <summary>
/// 全局玩法规则数据表行。
/// 用于承载散落在运行时模块中的全局数值配置。
/// </summary>
public sealed class GameplayRuleDataRow : DataRowBase, ICodeDataRow
{
    /// <summary>
    /// 列拆分分隔符。
    /// </summary>
    private static readonly string[] ColumnSplitSeparator = { "\t" };

    /// <summary>
    /// 数据表固定列数。
    /// </summary>
    private const int ColumnCount = 17;

    /// <summary>
    /// 合法规则 Code 的前缀。
    /// </summary>
    private const string CodePrefix = "rule_";

    /// <summary>
    /// 默认全局规则行的稳定 Code。
    /// </summary>
    public const string DefaultCode = "rule_default";

    /// <summary>
    /// 当前行的内部 Id 缓存。
    /// </summary>
    private int _id;

    /// <summary>
    /// 唯一 Id。
    /// </summary>
    public override int Id => _id;

    /// <summary>
    /// 机器码。
    /// </summary>
    public string Code { get; private set; }

    /// <summary>
    /// 规则名称。
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// 初始手动蛋库存数量。
    /// </summary>
    public int InitialManualEggCount { get; private set; }

    /// <summary>
    /// 手动蛋库存上限。
    /// </summary>
    public int MaxManualEggCount { get; private set; }

    /// <summary>
    /// 满槽时点击手动按钮所减少的补蛋秒数。
    /// </summary>
    public float ManualReduceSeconds { get; private set; }

    /// <summary>
    /// 自动补 1 个手动蛋所需秒数。
    /// </summary>
    public float RefillDurationSeconds { get; private set; }

    /// <summary>
    /// 手动孵化所使用的蛋 Code。
    /// </summary>
    public string ManualEggCode { get; private set; }

    /// <summary>
    /// 宠物吃完后前往玩耍区的概率。
    /// </summary>
    public int GoPlayAreaProbability { get; private set; }

    /// <summary>
    /// 宠物在玩耍区停留的秒数。
    /// </summary>
    public float PlayAreaStaySeconds { get; private set; }

    /// <summary>
    /// 餐桌水果上桌后的展示秒数。
    /// </summary>
    public float ServingDurationSeconds { get; private set; }

    /// <summary>
    /// 水果从果树飞向餐桌的动画时长。
    /// </summary>
    public float DeliverAnimationDuration { get; private set; }

    /// <summary>
    /// 点餐偏向已解锁水果桶的概率。
    /// </summary>
    public int PreferUnlockedFruitProbability { get; private set; }

    /// <summary>
    /// 宠物产出初级物的概率。
    /// </summary>
    public int PrimaryProduceProbability { get; private set; }

    /// <summary>
    /// 宠物产出中级物的概率。
    /// </summary>
    public int IntermediateProduceProbability { get; private set; }

    /// <summary>
    /// 宠物产出高级物的概率。
    /// </summary>
    public int AdvancedProduceProbability { get; private set; }

    /// <summary>
    /// 备注描述。
    /// </summary>
    public string Description { get; private set; }

    /// <summary>
    /// 从文本行解析玩法规则数据。
    /// </summary>
    /// <param name="dataRowString">原始数据行文本。</param>
    /// <param name="userData">额外上下文。</param>
    /// <returns>是否解析成功。</returns>
    public override bool ParseDataRow(string dataRowString, object userData)
    {
        if (string.IsNullOrWhiteSpace(dataRowString))
        {
            Log.Warning("GameplayRuleDataRow parse failed because row string is empty.");
            return false;
        }

        string[] columns = dataRowString.Split(ColumnSplitSeparator, StringSplitOptions.None);
        if (columns.Length != ColumnCount)
        {
            Log.Warning("GameplayRuleDataRow parse failed because column count '{0}' is invalid, row '{1}'.", columns.Length, dataRowString);
            return false;
        }

        if (!int.TryParse(columns[0], out int id) || id <= 0)
        {
            Log.Warning("GameplayRuleDataRow parse failed because Id '{0}' is invalid.", columns[0]);
            return false;
        }

        string code = columns[1].Trim();
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(CodePrefix, StringComparison.Ordinal))
        {
            Log.Warning("GameplayRuleDataRow parse failed because Code '{0}' is invalid.", columns[1]);
            return false;
        }

        string name = columns[2].Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Log.Warning("GameplayRuleDataRow parse failed because Name is empty, code '{0}'.", code);
            return false;
        }

        if (!int.TryParse(columns[3], out int initialManualEggCount) || initialManualEggCount <= 0)
        {
            Log.Warning("GameplayRuleDataRow parse failed because InitialManualEggCount '{0}' is invalid, code '{1}'.", columns[3], code);
            return false;
        }

        if (!int.TryParse(columns[4], out int maxManualEggCount) || maxManualEggCount < initialManualEggCount)
        {
            Log.Warning("GameplayRuleDataRow parse failed because MaxManualEggCount '{0}' is invalid, code '{1}'.", columns[4], code);
            return false;
        }

        if (!TryParsePositiveFloat(columns[5], out float manualReduceSeconds))
        {
            Log.Warning("GameplayRuleDataRow parse failed because ManualReduceSeconds '{0}' is invalid, code '{1}'.", columns[5], code);
            return false;
        }

        if (!TryParsePositiveFloat(columns[6], out float refillDurationSeconds))
        {
            Log.Warning("GameplayRuleDataRow parse failed because RefillDurationSeconds '{0}' is invalid, code '{1}'.", columns[6], code);
            return false;
        }

        string manualEggCode = columns[7].Trim();
        if (string.IsNullOrWhiteSpace(manualEggCode) || !manualEggCode.StartsWith("egg_", StringComparison.Ordinal))
        {
            Log.Warning("GameplayRuleDataRow parse failed because ManualEggCode '{0}' is invalid, code '{1}'.", columns[7], code);
            return false;
        }

        if (!TryParseProbability(columns[8], out int goPlayAreaProbability))
        {
            Log.Warning("GameplayRuleDataRow parse failed because GoPlayAreaProbability '{0}' is invalid, code '{1}'.", columns[8], code);
            return false;
        }

        if (!TryParsePositiveFloat(columns[9], out float playAreaStaySeconds))
        {
            Log.Warning("GameplayRuleDataRow parse failed because PlayAreaStaySeconds '{0}' is invalid, code '{1}'.", columns[9], code);
            return false;
        }

        if (!TryParsePositiveFloat(columns[10], out float servingDurationSeconds))
        {
            Log.Warning("GameplayRuleDataRow parse failed because ServingDurationSeconds '{0}' is invalid, code '{1}'.", columns[10], code);
            return false;
        }

        if (!TryParsePositiveFloat(columns[11], out float deliverAnimationDuration))
        {
            Log.Warning("GameplayRuleDataRow parse failed because DeliverAnimationDuration '{0}' is invalid, code '{1}'.", columns[11], code);
            return false;
        }

        if (!TryParseProbability(columns[12], out int preferUnlockedFruitProbability))
        {
            Log.Warning("GameplayRuleDataRow parse failed because PreferUnlockedFruitProbability '{0}' is invalid, code '{1}'.", columns[12], code);
            return false;
        }

        if (!TryParseProbability(columns[13], out int primaryProduceProbability))
        {
            Log.Warning("GameplayRuleDataRow parse failed because PrimaryProduceProbability '{0}' is invalid, code '{1}'.", columns[13], code);
            return false;
        }

        if (!TryParseProbability(columns[14], out int intermediateProduceProbability))
        {
            Log.Warning("GameplayRuleDataRow parse failed because IntermediateProduceProbability '{0}' is invalid, code '{1}'.", columns[14], code);
            return false;
        }

        if (!TryParseProbability(columns[15], out int advancedProduceProbability))
        {
            Log.Warning("GameplayRuleDataRow parse failed because AdvancedProduceProbability '{0}' is invalid, code '{1}'.", columns[15], code);
            return false;
        }

        if (primaryProduceProbability + intermediateProduceProbability + advancedProduceProbability != 100)
        {
            Log.Warning(
                "GameplayRuleDataRow parse failed because produce probabilities total is '{0}', code '{1}'.",
                primaryProduceProbability + intermediateProduceProbability + advancedProduceProbability,
                code);
            return false;
        }

        _id = id;
        Code = code;
        Name = name;
        InitialManualEggCount = initialManualEggCount;
        MaxManualEggCount = maxManualEggCount;
        ManualReduceSeconds = manualReduceSeconds;
        RefillDurationSeconds = refillDurationSeconds;
        ManualEggCode = manualEggCode;
        GoPlayAreaProbability = goPlayAreaProbability;
        PlayAreaStaySeconds = playAreaStaySeconds;
        ServingDurationSeconds = servingDurationSeconds;
        DeliverAnimationDuration = deliverAnimationDuration;
        PreferUnlockedFruitProbability = preferUnlockedFruitProbability;
        PrimaryProduceProbability = primaryProduceProbability;
        IntermediateProduceProbability = intermediateProduceProbability;
        AdvancedProduceProbability = advancedProduceProbability;
        Description = columns[16].Trim();
        return true;
    }

    /// <summary>
    /// 从二进制数据解析玩法规则数据。
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

    /// <summary>
    /// 解析正整数概率。
    /// </summary>
    private static bool TryParseProbability(string value, out int probability)
    {
        probability = 0;
        return int.TryParse(value, out probability) && probability >= 0 && probability <= 100;
    }

    /// <summary>
    /// 解析正浮点数。
    /// </summary>
    private static bool TryParsePositiveFloat(string value, out float result)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && result > 0f;
    }
}
