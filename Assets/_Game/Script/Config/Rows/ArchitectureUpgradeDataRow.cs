using System;
using System.Text;
using UnityGameFramework.Runtime;

/// <summary>
/// 建筑升级配置数据表行。
/// 每行描述一个建筑类别在某个当前等级下升级到下一级所需的金币。
/// </summary>
public sealed class ArchitectureUpgradeDataRow : DataRowBase, ICodeDataRow
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
    /// 合法升级 Code 的前缀。
    /// </summary>
    private const string CodePrefix = "archup_";

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
    /// 当前等级。
    /// 该行表示从 CurrentLevel 升到 CurrentLevel + 1 的价格。
    /// </summary>
    public int CurrentLevel { get; private set; }

    /// <summary>
    /// 升级所需金币。
    /// </summary>
    public int UpgradeGold { get; private set; }

    public int EffectParam { get; private set; }

    /// <summary>
    /// 备注描述。
    /// </summary>
    public string Description { get; private set; }

    /// <summary>
    /// 从文本行解析建筑升级配置。
    /// </summary>
    /// <param name="dataRowString">原始数据行文本。</param>
    /// <param name="userData">额外上下文。</param>
    /// <returns>是否解析成功。</returns>
    public override bool ParseDataRow(string dataRowString, object userData)
    {
        if (string.IsNullOrWhiteSpace(dataRowString))
        {
            Log.Warning("ArchitectureUpgradeDataRow parse failed because row string is empty.");
            return false;
        }

        string[] columns = dataRowString.Split(ColumnSplitSeparator, StringSplitOptions.None);
        if (columns.Length != ColumnCount)
        {
            Log.Warning("ArchitectureUpgradeDataRow parse failed because column count '{0}' is invalid, row '{1}'.", columns.Length, dataRowString);
            return false;
        }

        if (!int.TryParse(columns[0], out int id) || id <= 0)
        {
            Log.Warning("ArchitectureUpgradeDataRow parse failed because Id '{0}' is invalid.", columns[0]);
            return false;
        }

        string code = columns[1].Trim();
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(CodePrefix, StringComparison.Ordinal))
        {
            Log.Warning("ArchitectureUpgradeDataRow parse failed because Code '{0}' is invalid.", columns[1]);
            return false;
        }

        if (!Enum.TryParse(columns[2].Trim(), true, out PlayerRuntimeModule.ArchitectureCategory category)
            || !Enum.IsDefined(typeof(PlayerRuntimeModule.ArchitectureCategory), category))
        {
            Log.Warning("ArchitectureUpgradeDataRow parse failed because Category '{0}' is invalid, code '{1}'.", columns[2], code);
            return false;
        }

        if (!int.TryParse(columns[3], out int currentLevel) || currentLevel <= 0)
        {
            Log.Warning("ArchitectureUpgradeDataRow parse failed because CurrentLevel '{0}' is invalid, code '{1}'.", columns[3], code);
            return false;
        }

        if (!int.TryParse(columns[4], out int upgradeGold) || upgradeGold <= 0)
        {
            Log.Warning("ArchitectureUpgradeDataRow parse failed because UpgradeGold '{0}' is invalid, code '{1}'.", columns[4], code);
            return false;
        }

        if (!int.TryParse(columns[5], out int effectParam) || effectParam < 0)
        {
            Log.Warning("ArchitectureUpgradeDataRow parse failed because EffectParam '{0}' is invalid, code '{1}'.", columns[5], code);
            return false;
        }

        _id = id;
        Code = code;
        Category = category;
        CurrentLevel = currentLevel;
        UpgradeGold = upgradeGold;
        EffectParam = effectParam;
        Description = columns[6].Trim();
        return true;
    }

    /// <summary>
    /// 从二进制数据解析建筑升级配置。
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
