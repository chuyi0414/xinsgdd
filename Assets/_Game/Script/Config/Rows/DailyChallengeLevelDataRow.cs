using System;
using System.Text;
using UnityGameFramework.Runtime;

/// <summary>
/// 每日一关默认关卡标识配置数据表行。
/// 只存储关卡标识码（如 "4-2"），完整资源路径由业务层拼接 "Configs/Levels/" + Code 得到。
/// 云端下发新关卡时只需更新此行的 Code 字段或通过 MainUIForm.OverrideDailyChallengeLevel 覆盖。
/// </summary>
public sealed class DailyChallengeLevelDataRow : DataRowBase, ICodeDataRow
{
    /// <summary>
    /// 列拆分分隔符。与项目其他数据表一致，使用 Tab 分隔。
    /// </summary>
    private static readonly string[] ColumnSplitSeparator = { "\t" };

    /// <summary>
    /// 数据表固定列数：Id + Code，共 2 列。
    /// </summary>
    private const int ColumnCount = 2;

    /// <summary>
    /// 默认配置编码。通过 Code 查询时使用此常量。
    /// </summary>
    public const string DefaultCode = "default_daily_challenge_level";

    /// <summary>
    /// 当前行的内部 Id 缓存。
    /// </summary>
    private int _id;

    /// <summary>
    /// 唯一 Id。
    /// </summary>
    public override int Id => _id;

    /// <summary>
    /// 配置编码。固定为 DefaultCode。
    /// </summary>
    public string Code { get; private set; }

    /// <summary>
    /// 关卡标识码。例如 "4-2"、"bbl1"。
    /// 由服务器/策划配置，业务代码不应硬编码此值。
    /// </summary>
    public string LevelCode { get; private set; }

    /// <summary>
    /// 完整的关卡资源路径。
    /// 对应 Resources 目录下的 Configs/Levels/{LevelCode}.txt 文本资源。
    /// </summary>
    public string LevelAssetPath => "Configs/Levels/" + LevelCode;

    /// <summary>
    /// 从文本行解析每日一关关卡标识配置数据。
    /// </summary>
    /// <param name="dataRowString">原始数据行文本。</param>
    /// <param name="userData">额外上下文。</param>
    /// <returns>是否解析成功。</returns>
    public override bool ParseDataRow(string dataRowString, object userData)
    {
        if (string.IsNullOrWhiteSpace(dataRowString))
        {
            Log.Warning("DailyChallengeLevelDataRow parse failed because row string is empty.");
            return false;
        }

        string[] columns = dataRowString.Split(ColumnSplitSeparator, StringSplitOptions.None);
        if (columns.Length != ColumnCount)
        {
            Log.Warning("DailyChallengeLevelDataRow parse failed because column count '{0}' is invalid, row '{1}'.", columns.Length, dataRowString);
            return false;
        }

        if (!int.TryParse(columns[0], out int id) || id <= 0)
        {
            Log.Warning("DailyChallengeLevelDataRow parse failed because Id '{0}' is invalid.", columns[0]);
            return false;
        }

        string code = columns[1].Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            Log.Warning("DailyChallengeLevelDataRow parse failed because LevelCode is empty.");
            return false;
        }

        // 允许非 DefaultCode 的 Code 值，便于后续云端下发时 Code 字段携带动态标识。
        // 但查询默认行时仍然通过 DefaultCode 来定位。

        _id = id;
        Code = DefaultCode;
        LevelCode = code;
        return true;
    }

    /// <summary>
    /// 从二进制数据解析每日一关关卡标识配置数据。
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
