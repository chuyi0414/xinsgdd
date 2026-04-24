using System;
using System.Text;
using UnityGameFramework.Runtime;

/// <summary>
/// 建筑图片配置数据表行。
/// 用于配置每个建筑类别下每个等级（含 0 级未解锁）对应的升级界面指示器精灵路径与主界面实体占位精灵路径。
/// </summary>
public sealed class ArchitectureDataRow : DataRowBase, ICodeDataRow
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
    /// 合法 Code 的前缀。
    /// </summary>
    private const string CodePrefix = "arch_";

    /// <summary>
    /// 当前行内部 Id 缓存。
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
    /// 建筑等级。
    /// 0 = 未解锁占位；1~10 = 各升级等级。
    /// </summary>
    public int Level { get; private set; }

    /// <summary>
    /// 升级界面等级指示器精灵的 Addressables 资源路径。
    /// 用于 ArchitectureUpgradeUIForm 中每个等级指示物的 Image.sprite 赋值。
    /// </summary>
    public string IndicatorSpritePath { get; private set; }

    /// <summary>
    /// 主界面实体占位精灵的 Addressables 资源路径。
    /// 用于 PlayfieldEntityModule 中未解锁槽位的 LockedPlaceholderEntity 显示。
    /// </summary>
    public string EntitySpritePath { get; private set; }

    /// <summary>
    /// 备注描述。
    /// </summary>
    public string Description { get; private set; }

    /// <summary>
    /// 从文本行解析建筑图片配置。
    /// </summary>
    /// <param name="dataRowString">原始数据行文本。</param>
    /// <param name="userData">额外上下文。</param>
    /// <returns>是否解析成功。</returns>
    public override bool ParseDataRow(string dataRowString, object userData)
    {
        if (string.IsNullOrWhiteSpace(dataRowString))
        {
            Log.Warning("ArchitectureDataRow parse failed because row string is empty.");
            return false;
        }

        string[] columns = dataRowString.Split(ColumnSplitSeparator, StringSplitOptions.None);
        if (columns.Length != ColumnCount)
        {
            Log.Warning("ArchitectureDataRow parse failed because column count '{0}' is invalid, row '{1}'.", columns.Length, dataRowString);
            return false;
        }

        if (!int.TryParse(columns[0], out int id) || id <= 0)
        {
            Log.Warning("ArchitectureDataRow parse failed because Id '{0}' is invalid.", columns[0]);
            return false;
        }

        string code = columns[1].Trim();
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(CodePrefix, StringComparison.Ordinal))
        {
            Log.Warning("ArchitectureDataRow parse failed because Code '{0}' is invalid.", columns[1]);
            return false;
        }

        if (!Enum.TryParse(columns[2].Trim(), true, out PlayerRuntimeModule.ArchitectureCategory category)
            || !Enum.IsDefined(typeof(PlayerRuntimeModule.ArchitectureCategory), category))
        {
            Log.Warning("ArchitectureDataRow parse failed because Category '{0}' is invalid, code '{1}'.", columns[2], code);
            return false;
        }

        if (!int.TryParse(columns[3], out int level) || level < 0)
        {
            Log.Warning("ArchitectureDataRow parse failed because Level '{0}' is invalid, code '{1}'.", columns[3], code);
            return false;
        }

        // 精灵路径允许为空字符串（美术资源尚未配置时），但不能为 null。
        string indicatorSpritePath = columns[4].Trim();
        string entitySpritePath = columns[5].Trim();

        _id = id;
        Code = code;
        Category = category;
        Level = level;
        IndicatorSpritePath = indicatorSpritePath;
        EntitySpritePath = entitySpritePath;
        Description = columns[6].Trim();
        return true;
    }

    /// <summary>
    /// 从二进制数据解析建筑图片配置。
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
