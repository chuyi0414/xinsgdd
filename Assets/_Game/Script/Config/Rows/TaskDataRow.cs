using System;
using System.Text;
using UnityGameFramework.Runtime;

/// <summary>
/// 任务条件类型枚举。
/// 决定 TaskModule 订阅哪个游戏事件来推进进度。
/// </summary>
public enum TaskCondition
{
    /// <summary>
    /// 累计完成宠物孵化次数。
    /// 订阅 EggHatchComponent.HatchStateChanged，读取 TotalHatchCount。
    /// </summary>
    HatchComplete = 0,

    /// <summary>
    /// 累计完成宠物喂养次数。
    /// 订阅 PetDiningOrderComponent.CoinDropRequested，每次触发 +1。
    /// </summary>
    FeedComplete = 1,

    /// <summary>
    /// 当前持有金币达到指定数量。
    /// 订阅 PlayerRuntimeModule.GoldChanged，读取 CurrentGold。
    /// </summary>
    GoldTotal = 2,

    /// <summary>
    /// 已解锁孵化区槽位数量达到指定值。
    /// 订阅 PlayerRuntimeModule.ArchitectureStateChanged，读取 HatchSlotCount。
    /// </summary>
    HatchSlotCount = 3,

    /// <summary>
    /// 已解锁餐桌槽位数量达到指定值。
    /// 订阅 PlayerRuntimeModule.ArchitectureStateChanged，读取 DiningSeatCount。
    /// </summary>
    DietSlotCount = 4,

    /// <summary>
    /// 已解锁果树槽位数量达到指定值。
    /// 订阅 PlayerRuntimeModule.ArchitectureStateChanged，读取 OrchardSlotCount。
    /// </summary>
    FruiterSlotCount = 5,
}

/// <summary>
/// 任务数据表行。
/// 定义每个任务的配置：名称、条件类型、目标数量、奖励金币。
/// </summary>
public sealed class TaskDataRow : DataRowBase, ICodeDataRow
{
    /// <summary>
    /// 列拆分分隔符。
    /// </summary>
    private static readonly string[] ColumnSplitSeparator = { "\t" };

    /// <summary>
    /// 数据表固定列数：Id + Code + TaskNumber + Name + ConditionType + TargetCount + AwardGold，共 7 列。
    /// </summary>
    private const int ColumnCount = 7;

    /// <summary>
    /// 合法任务 Code 的前缀。
    /// </summary>
    private const string CodePrefix = "task_";

    /// <summary>
    /// 当前行的内部 Id 缓存。
    /// </summary>
    private int _id;

    /// <summary>
    /// 任务唯一 Id。
    /// </summary>
    public override int Id => _id;

    /// <summary>
    /// 机器码。
    /// </summary>
    public string Code { get; private set; }

    /// <summary>
    /// 任务编号（如 "1-1"、"1-2"）。
    /// 用于在 UI 上显示任务的序号。
    /// </summary>
    public string TaskNumber { get; private set; }

    /// <summary>
    /// 任务显示名称。
    /// </summary>
    public string Name { get; private set; }

    /// <summary>
    /// 任务条件类型。
    /// </summary>
    public TaskCondition ConditionType { get; private set; }

    /// <summary>
    /// 任务目标数量。
    /// 进度达到此值后任务状态变为 Claimable（可领取）。
    /// </summary>
    public int TargetCount { get; private set; }

    /// <summary>
    /// 任务完成后可领取的金币数量。
    /// </summary>
    public int AwardGold { get; private set; }

    /// <summary>
    /// 从文本行解析任务表数据。
    /// </summary>
    /// <param name="dataRowString">原始数据行文本。</param>
    /// <param name="userData">额外上下文。</param>
    /// <returns>是否解析成功。</returns>
    public override bool ParseDataRow(string dataRowString, object userData)
    {
        if (string.IsNullOrWhiteSpace(dataRowString))
        {
            Log.Warning("TaskDataRow parse failed because row string is empty.");
            return false;
        }

        string[] columns = dataRowString.Split(ColumnSplitSeparator, StringSplitOptions.None);
        if (columns.Length != ColumnCount)
        {
            Log.Warning("TaskDataRow parse failed because column count '{0}' is invalid, row '{1}'.", columns.Length, dataRowString);
            return false;
        }

        // ─── Id ───
        if (!int.TryParse(columns[0], out int id) || id <= 0)
        {
            Log.Warning("TaskDataRow parse failed because Id '{0}' is invalid.", columns[0]);
            return false;
        }

        // ─── Code ───
        string code = columns[1].Trim();
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(CodePrefix, StringComparison.Ordinal))
        {
            Log.Warning("TaskDataRow parse failed because Code '{0}' is invalid.", columns[1]);
            return false;
        }

        // ─── TaskNumber ───
        string taskNumber = columns[2].Trim();
        if (string.IsNullOrWhiteSpace(taskNumber))
        {
            Log.Warning("TaskDataRow parse failed because TaskNumber is empty, code '{0}'.", code);
            return false;
        }

        // ─── Name ───
        string name = columns[3].Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Log.Warning("TaskDataRow parse failed because Name is empty, code '{0}'.", code);
            return false;
        }

        // ─── ConditionType ───
        string conditionTypeStr = columns[4].Trim();
        if (!Enum.TryParse(conditionTypeStr, out TaskCondition conditionType))
        {
            Log.Warning("TaskDataRow parse failed because ConditionType '{0}' is invalid, code '{1}'.", conditionTypeStr, code);
            return false;
        }

        // ─── TargetCount ───
        if (!int.TryParse(columns[5], out int targetCount) || targetCount <= 0)
        {
            Log.Warning("TaskDataRow parse failed because TargetCount '{0}' is invalid, code '{1}'.", columns[5], code);
            return false;
        }

        // ─── AwardGold ───
        if (!int.TryParse(columns[6], out int awardGold) || awardGold <= 0)
        {
            Log.Warning("TaskDataRow parse failed because AwardGold '{0}' is invalid, code '{1}'.", columns[6], code);
            return false;
        }

        _id = id;
        Code = code;
        TaskNumber = taskNumber;
        Name = name;
        ConditionType = conditionType;
        TargetCount = targetCount;
        AwardGold = awardGold;
        return true;
    }

    /// <summary>
    /// 从二进制数据解析任务表数据。
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
