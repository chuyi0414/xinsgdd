using System;
using System.Text;
using UnityGameFramework.Runtime;

/// <summary>
/// 建筑槽位配置数据表行。
/// 用于配置每个建筑类别下每个槽位的初始解锁状态与购买价格。
/// </summary>
public sealed class ArchitectureSlotDataRow : DataRowBase, ICodeDataRow
{
    /// <summary>
    /// 列拆分分隔符。
    /// </summary>
    private static readonly string[] ColumnSplitSeparator = { "\t" };

    /// <summary>
    /// 数据表固定列数。
    /// </summary>
    private const int ColumnCount = 7;

    /// <summary>
    /// 合法槽位 Code 的前缀。
    /// </summary>
    private const string CodePrefix = "archslot_";

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
    /// 建筑类别。
    /// </summary>
    public PlayerRuntimeModule.ArchitectureCategory Category { get; private set; }

    /// <summary>
    /// 1 基槽位索引。
    /// </summary>
    public int SlotIndex { get; private set; }

    /// <summary>
    /// 是否开局已解锁。
    /// </summary>
    public bool IsInitiallyUnlocked { get; private set; }

    /// <summary>
    /// 购买该槽位所需金币。
    /// </summary>
    public int UnlockGold { get; private set; }

    /// <summary>
    /// 备注描述。
    /// </summary>
    public string Description { get; private set; }

    /// <summary>
    /// 从文本行解析建筑槽位配置。
    /// </summary>
    /// <param name="dataRowString">原始数据行文本。</param>
    /// <param name="userData">额外上下文。</param>
    /// <returns>是否解析成功。</returns>
    public override bool ParseDataRow(string dataRowString, object userData)
    {
        if (string.IsNullOrWhiteSpace(dataRowString))
        {
            Log.Warning("ArchitectureSlotDataRow parse failed because row string is empty.");
            return false;
        }

        string[] columns = dataRowString.Split(ColumnSplitSeparator, StringSplitOptions.None);
        if (columns.Length != ColumnCount)
        {
            Log.Warning("ArchitectureSlotDataRow parse failed because column count '{0}' is invalid, row '{1}'.", columns.Length, dataRowString);
            return false;
        }

        if (!int.TryParse(columns[0], out int id) || id <= 0)
        {
            Log.Warning("ArchitectureSlotDataRow parse failed because Id '{0}' is invalid.", columns[0]);
            return false;
        }

        string code = columns[1].Trim();
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(CodePrefix, StringComparison.Ordinal))
        {
            Log.Warning("ArchitectureSlotDataRow parse failed because Code '{0}' is invalid.", columns[1]);
            return false;
        }

        if (!Enum.TryParse(columns[2].Trim(), true, out PlayerRuntimeModule.ArchitectureCategory category)
            || !Enum.IsDefined(typeof(PlayerRuntimeModule.ArchitectureCategory), category))
        {
            Log.Warning("ArchitectureSlotDataRow parse failed because Category '{0}' is invalid, code '{1}'.", columns[2], code);
            return false;
        }

        if (!int.TryParse(columns[3], out int slotIndex) || slotIndex <= 0)
        {
            Log.Warning("ArchitectureSlotDataRow parse failed because SlotIndex '{0}' is invalid, code '{1}'.", columns[3], code);
            return false;
        }

        if (!bool.TryParse(columns[4].Trim(), out bool isInitiallyUnlocked))
        {
            Log.Warning("ArchitectureSlotDataRow parse failed because IsInitiallyUnlocked '{0}' is invalid, code '{1}'.", columns[4], code);
            return false;
        }

        if (!int.TryParse(columns[5], out int unlockGold) || unlockGold < 0)
        {
            Log.Warning("ArchitectureSlotDataRow parse failed because UnlockGold '{0}' is invalid, code '{1}'.", columns[5], code);
            return false;
        }

        if (isInitiallyUnlocked && unlockGold != 0)
        {
            Log.Warning("ArchitectureSlotDataRow parse failed because initially unlocked slot '{0}' must have UnlockGold = 0.", code);
            return false;
        }

        if (!isInitiallyUnlocked && unlockGold <= 0)
        {
            Log.Warning("ArchitectureSlotDataRow parse failed because locked slot '{0}' must have UnlockGold > 0.", code);
            return false;
        }

        _id = id;
        Code = code;
        Category = category;
        SlotIndex = slotIndex;
        IsInitiallyUnlocked = isInitiallyUnlocked;
        UnlockGold = unlockGold;
        Description = columns[6].Trim();
        return true;
    }

    /// <summary>
    /// 从二进制数据解析建筑槽位配置。
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
