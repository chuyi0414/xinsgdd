using System;
using System.Text;
using UnityGameFramework.Runtime;

/// <summary>
/// 水果系统数据表行。
/// </summary>
public sealed class FruitDataRow : DataRowBase, ICodeDataRow
{
    /// <summary>
    /// 列拆分分隔符。
    /// </summary>
    private static readonly string[] ColumnSplitSeparator = { "\t" };

    /// <summary>
    /// 数据表固定列数。
    /// </summary>
    private const int ColumnCount = 9;

    /// <summary>
    /// 合法水果 Code 的前缀。
    /// </summary>
    private const string CodePrefix = "fruit_";

    /// <summary>
    /// 当前行的内部 Id 缓存。
    /// </summary>
    private int _id;

    /// <summary>
    /// 水果唯一 Id。
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
    /// 是否开局已解锁。
    /// </summary>
    public bool IsUnlocked { get; private set; }

    /// <summary>
    /// 图标资源路径。
    /// </summary>
    public string IconPath { get; private set; }

    /// <summary>
    /// 解锁所需金币。
    /// </summary>
    public int UnlockGold { get; private set; }

    /// <summary>
    /// 产出金币的概率。
    /// </summary>
    public int CoinProbability { get; private set; }

    /// <summary>
    /// 产出的金币数量。
    /// </summary>
    public int CoinAmount { get; private set; }

    /// <summary>
    /// 生产该水果所需秒数。
    /// </summary>
    public int ProduceSeconds { get; private set; }

    /// <summary>
    /// 产出物品的概率。
    /// </summary>
    public int ItemProbability => 100 - CoinProbability;

    /// <summary>
    /// 从文本行解析水果表数据。
    /// </summary>
    /// <param name="dataRowString">原始数据行文本。</param>
    /// <param name="userData">额外上下文。</param>
    /// <returns>是否解析成功。</returns>
    public override bool ParseDataRow(string dataRowString, object userData)
    {
        if (string.IsNullOrWhiteSpace(dataRowString))
        {
            Log.Warning("FruitDataRow parse failed because row string is empty.");
            return false;
        }

        string[] columns = dataRowString.Split(ColumnSplitSeparator, StringSplitOptions.None);
        if (columns.Length != ColumnCount)
        {
            Log.Warning("FruitDataRow parse failed because column count '{0}' is invalid, row '{1}'.", columns.Length, dataRowString);
            return false;
        }

        if (!int.TryParse(columns[0], out int id) || id <= 0)
        {
            Log.Warning("FruitDataRow parse failed because Id '{0}' is invalid.", columns[0]);
            return false;
        }

        string code = columns[1].Trim();
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(CodePrefix, StringComparison.Ordinal))
        {
            Log.Warning("FruitDataRow parse failed because Code '{0}' is invalid.", columns[1]);
            return false;
        }

        string name = columns[2].Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Log.Warning("FruitDataRow parse failed because Name is empty, code '{0}'.", code);
            return false;
        }

        if (!bool.TryParse(columns[3].Trim(), out bool isUnlocked))
        {
            Log.Warning("FruitDataRow parse failed because IsUnlocked '{0}' is invalid, code '{1}'.", columns[3], code);
            return false;
        }

        string iconPath = columns[4].Trim();
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            Log.Warning("FruitDataRow parse failed because IconPath is empty, code '{0}'.", code);
            return false;
        }

        if (!int.TryParse(columns[5], out int unlockGold) || unlockGold < 0)
        {
            Log.Warning("FruitDataRow parse failed because UnlockGold '{0}' is invalid, code '{1}'.", columns[5], code);
            return false;
        }

        if (isUnlocked && unlockGold != 0)
        {
            Log.Warning("FruitDataRow parse failed because unlocked fruit '{0}' must have UnlockGold = 0.", code);
            return false;
        }

        if (!isUnlocked && unlockGold <= 0)
        {
            Log.Warning("FruitDataRow parse failed because locked fruit '{0}' must have UnlockGold > 0.", code);
            return false;
        }

        if (!int.TryParse(columns[6], out int coinProbability) || coinProbability < 0 || coinProbability > 100)
        {
            Log.Warning("FruitDataRow parse failed because CoinProbability '{0}' is invalid, code '{1}'.", columns[6], code);
            return false;
        }

        if (!int.TryParse(columns[7], out int coinAmount) || coinAmount <= 0)
        {
            Log.Warning("FruitDataRow parse failed because CoinAmount '{0}' is invalid, code '{1}'.", columns[7], code);
            return false;
        }

        if (!int.TryParse(columns[8], out int produceSeconds) || produceSeconds <= 0)
        {
            Log.Warning("FruitDataRow parse failed because ProduceSeconds '{0}' is invalid, code '{1}'.", columns[8], code);
            return false;
        }

        _id = id;
        Code = code;
        Name = name;
        IsUnlocked = isUnlocked;
        IconPath = iconPath;
        UnlockGold = unlockGold;
        CoinProbability = coinProbability;
        CoinAmount = coinAmount;
        ProduceSeconds = produceSeconds;
        return true;
    }

    /// <summary>
    /// 从二进制数据解析水果表数据。
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
