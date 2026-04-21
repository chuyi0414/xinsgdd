using System;
using System.Text;
using UnityGameFramework.Runtime;

/// <summary>
/// 每日一关道具/复活价格配置数据表行。
/// </summary>
public sealed class DailyChallengeCostDataRow : DataRowBase, ICodeDataRow
{
    /// <summary>
    /// 列拆分分隔符。
    /// </summary>
    private static readonly string[] ColumnSplitSeparator = { "\t" };

    /// <summary>
    /// 数据表固定列数。
    /// </summary>
    private const int ColumnCount = 6;

    /// <summary>
    /// 默认配置编码。
    /// </summary>
    public const string DefaultCode = "daily_challenge_cost_default";

    /// <summary>
    /// 当前行的内部 Id 缓存。
    /// </summary>
    private int _id;

    /// <summary>
    /// 唯一 Id。
    /// </summary>
    public override int Id => _id;

    /// <summary>
    /// 配置编码。
    /// </summary>
    public string Code { get; private set; }

    /// <summary>
    /// 移出道具价格。
    /// </summary>
    public int RemoveGold { get; private set; }

    /// <summary>
    /// 拿取道具价格。
    /// </summary>
    public int RetrieveGold { get; private set; }

    /// <summary>
    /// 随机道具价格。
    /// </summary>
    public int ShuffleGold { get; private set; }

    /// <summary>
    /// 复活价格。
    /// </summary>
    public int ResurgenceGold { get; private set; }

    /// <summary>
    /// 从文本行解析每日一关价格配置数据。
    /// </summary>
    /// <param name="dataRowString">原始数据行文本。</param>
    /// <param name="userData">额外上下文。</param>
    /// <returns>是否解析成功。</returns>
    public override bool ParseDataRow(string dataRowString, object userData)
    {
        if (string.IsNullOrWhiteSpace(dataRowString))
        {
            Log.Warning("DailyChallengeCostDataRow parse failed because row string is empty.");
            return false;
        }

        string[] columns = dataRowString.Split(ColumnSplitSeparator, StringSplitOptions.None);
        if (columns.Length != ColumnCount)
        {
            Log.Warning("DailyChallengeCostDataRow parse failed because column count '{0}' is invalid, row '{1}'.", columns.Length, dataRowString);
            return false;
        }

        if (!int.TryParse(columns[0], out int id) || id <= 0)
        {
            Log.Warning("DailyChallengeCostDataRow parse failed because Id '{0}' is invalid.", columns[0]);
            return false;
        }

        string code = columns[1].Trim();
        if (!string.Equals(code, DefaultCode, StringComparison.Ordinal))
        {
            Log.Warning("DailyChallengeCostDataRow parse failed because Code '{0}' is invalid.", columns[1]);
            return false;
        }

        if (!int.TryParse(columns[2], out int removeGold) || removeGold <= 0)
        {
            Log.Warning("DailyChallengeCostDataRow parse failed because RemoveGold '{0}' is invalid.", columns[2]);
            return false;
        }

        if (!int.TryParse(columns[3], out int retrieveGold) || retrieveGold <= 0)
        {
            Log.Warning("DailyChallengeCostDataRow parse failed because RetrieveGold '{0}' is invalid.", columns[3]);
            return false;
        }

        if (!int.TryParse(columns[4], out int shuffleGold) || shuffleGold <= 0)
        {
            Log.Warning("DailyChallengeCostDataRow parse failed because ShuffleGold '{0}' is invalid.", columns[4]);
            return false;
        }

        if (!int.TryParse(columns[5], out int resurgenceGold) || resurgenceGold <= 0)
        {
            Log.Warning("DailyChallengeCostDataRow parse failed because ResurgenceGold '{0}' is invalid.", columns[5]);
            return false;
        }

        _id = id;
        Code = code;
        RemoveGold = removeGold;
        RetrieveGold = retrieveGold;
        ShuffleGold = shuffleGold;
        ResurgenceGold = resurgenceGold;
        return true;
    }

    /// <summary>
    /// 从二进制数据解析每日一关价格配置数据。
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
