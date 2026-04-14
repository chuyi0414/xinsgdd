using System;
using System.Text;
using UnityGameFramework.Runtime;

/// <summary>
/// 蛋系统数据表行。
/// </summary>
public sealed class EggDataRow : DataRowBase, ICodeDataRow
{
    /// <summary>
    /// 列拆分分隔符。
    /// </summary>
    private static readonly string[] ColumnSplitSeparator = { "\t" };

    /// <summary>
    /// 概率字段拆分分隔符。
    /// </summary>
    private static readonly char[] ProbabilitySplitSeparator = { '|' };

    /// <summary>
    /// 获取方式字段拆分分隔符。
    /// </summary>
    private static readonly char[] AcquireWaySplitSeparator = { '|' };

    /// <summary>
    /// 数据表固定列数。
    /// </summary>
    private const int ColumnCount = 10;

    /// <summary>
    /// 合法蛋 Code 的前缀。
    /// </summary>
    private const string CodePrefix = "egg_";

    /// <summary>
    /// 当前行的内部 Id 缓存。
    /// </summary>
    private int _id;

    /// <summary>
    /// 蛋的获取方式枚举。
    /// </summary>
    [Flags]
    public enum EggAcquireWay
    {
        /// <summary>
        /// 无获取方式。
        /// </summary>
        None = 0,

        /// <summary>
        /// 赠送获得。
        /// </summary>
        Gift = 1 << 0,

        /// <summary>
        /// 免费获得。
        /// </summary>
        Free = 1 << 1,

        /// <summary>
        /// 商店购买获得。
        /// </summary>
        Shop = 1 << 2,

        /// <summary>
        /// 广告奖励获得。
        /// </summary>
        Ad = 1 << 3,
    }

    /// <summary>
    /// 蛋唯一 Id。
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
    /// 图标资源路径。
    /// </summary>
    public string IconPath { get; private set; }

    /// <summary>
    /// 基础孵化时长，单位秒。
    /// </summary>
    public int HatchSeconds { get; private set; }

    /// <summary>
    /// 蛋品质。
    /// </summary>
    public QualityType Quality { get; private set; }

    /// <summary>
    /// 普通宠物孵化概率。
    /// </summary>
    public int NormalRate { get; private set; }

    /// <summary>
    /// 稀有宠物孵化概率。
    /// </summary>
    public int RareRate { get; private set; }

    /// <summary>
    /// 史诗宠物孵化概率。
    /// </summary>
    public int EpicRate { get; private set; }

    /// <summary>
    /// 传说宠物孵化概率。
    /// </summary>
    public int LegendaryRate { get; private set; }

    /// <summary>
    /// 神话宠物孵化概率。
    /// </summary>
    public int MythicRate { get; private set; }

    /// <summary>
    /// 获取方式集合。
    /// </summary>
    public EggAcquireWay AcquireWays { get; private set; }

    /// <summary>
    /// 购买所需金币。
    /// </summary>
    public int PurchaseGold { get; private set; }

    /// <summary>
    /// 备注描述。
    /// </summary>
    public string Description { get; private set; }

    /// <summary>
    /// 从文本行解析蛋表数据。
    /// </summary>
    /// <param name="dataRowString">原始数据行文本。</param>
    /// <param name="userData">额外上下文。</param>
    /// <returns>是否解析成功。</returns>
    public override bool ParseDataRow(string dataRowString, object userData)
    {
        if (string.IsNullOrWhiteSpace(dataRowString))
        {
            Log.Warning("EggDataRow parse failed because row string is empty.");
            return false;
        }

        string[] columns = dataRowString.Split(ColumnSplitSeparator, StringSplitOptions.None);
        if (columns.Length != ColumnCount)
        {
            Log.Warning("EggDataRow parse failed because column count '{0}' is invalid, row '{1}'.", columns.Length, dataRowString);
            return false;
        }

        if (!int.TryParse(columns[0], out int id) || id <= 0)
        {
            Log.Warning("EggDataRow parse failed because Id '{0}' is invalid.", columns[0]);
            return false;
        }

        string code = columns[1].Trim();
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(CodePrefix, StringComparison.Ordinal))
        {
            Log.Warning("EggDataRow parse failed because Code '{0}' is invalid.", columns[1]);
            return false;
        }

        string name = columns[2].Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            Log.Warning("EggDataRow parse failed because Name is empty, code '{0}'.", code);
            return false;
        }

        string iconPath = columns[3].Trim();
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            Log.Warning("EggDataRow parse failed because IconPath is empty, code '{0}'.", code);
            return false;
        }

        if (!int.TryParse(columns[4], out int hatchSeconds) || hatchSeconds <= 0)
        {
            Log.Warning("EggDataRow parse failed because HatchSeconds '{0}' is invalid, code '{1}'.", columns[4], code);
            return false;
        }

        if (!Enum.TryParse(columns[5].Trim(), true, out QualityType quality) || !Enum.IsDefined(typeof(QualityType), quality))
        {
            Log.Warning("EggDataRow parse failed because Quality '{0}' is invalid, code '{1}'.", columns[5], code);
            return false;
        }

        if (!TryParseProbabilities(columns[6], code, out int normalRate, out int rareRate, out int epicRate, out int legendaryRate, out int mythicRate))
        {
            return false;
        }

        if (!TryParseAcquireWays(columns[7], code, out EggAcquireWay acquireWays))
        {
            return false;
        }

        if (!int.TryParse(columns[8], out int purchaseGold) || purchaseGold < 0)
        {
            Log.Warning("EggDataRow parse failed because PurchaseGold '{0}' is invalid, code '{1}'.", columns[8], code);
            return false;
        }

        bool canPurchase = (acquireWays & EggAcquireWay.Shop) != 0;
        if (canPurchase && purchaseGold <= 0)
        {
            Log.Warning("EggDataRow parse failed because Shop egg '{0}' must have PurchaseGold > 0.", code);
            return false;
        }

        if (!canPurchase && purchaseGold != 0)
        {
            Log.Warning("EggDataRow parse failed because non-shop egg '{0}' must have PurchaseGold = 0.", code);
            return false;
        }

        _id = id;
        Code = code;
        Name = name;
        IconPath = iconPath;
        HatchSeconds = hatchSeconds;
        Quality = quality;
        NormalRate = normalRate;
        RareRate = rareRate;
        EpicRate = epicRate;
        LegendaryRate = legendaryRate;
        MythicRate = mythicRate;
        AcquireWays = acquireWays;
        PurchaseGold = purchaseGold;
        Description = columns[9].Trim();
        return true;
    }

    /// <summary>
    /// 从二进制数据解析蛋表数据。
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
    /// 解析品质概率字段。
    /// </summary>
    private static bool TryParseProbabilities(
        string probabilityValue,
        string code,
        out int normalRate,
        out int rareRate,
        out int epicRate,
        out int legendaryRate,
        out int mythicRate)
    {
        normalRate = 0;
        rareRate = 0;
        epicRate = 0;
        legendaryRate = 0;
        mythicRate = 0;

        string[] probabilityColumns = probabilityValue.Split(ProbabilitySplitSeparator, StringSplitOptions.None);
        if (probabilityColumns.Length != 5)
        {
            Log.Warning("EggDataRow parse failed because HatchProbability '{0}' is invalid, code '{1}'.", probabilityValue, code);
            return false;
        }

        int[] rates = new int[probabilityColumns.Length];
        int totalRate = 0;
        for (int i = 0; i < probabilityColumns.Length; i++)
        {
            if (!int.TryParse(probabilityColumns[i].Trim(), out rates[i]) || rates[i] < 0)
            {
                Log.Warning("EggDataRow parse failed because HatchProbability '{0}' contains invalid rate, code '{1}'.", probabilityValue, code);
                return false;
            }

            totalRate += rates[i];
        }

        if (totalRate != 100)
        {
            Log.Warning("EggDataRow parse failed because HatchProbability '{0}' total is '{1}', code '{2}'.", probabilityValue, totalRate, code);
            return false;
        }

        normalRate = rates[0];
        rareRate = rates[1];
        epicRate = rates[2];
        legendaryRate = rates[3];
        mythicRate = rates[4];
        return true;
    }

    /// <summary>
    /// 解析获取方式字段。
    /// </summary>
    private static bool TryParseAcquireWays(string acquireWaysValue, string code, out EggAcquireWay acquireWays)
    {
        acquireWays = EggAcquireWay.None;
        if (string.IsNullOrWhiteSpace(acquireWaysValue))
        {
            Log.Warning("EggDataRow parse failed because AcquireWays is empty, code '{0}'.", code);
            return false;
        }

        string[] acquireWayColumns = acquireWaysValue.Split(AcquireWaySplitSeparator, StringSplitOptions.RemoveEmptyEntries);
        if (acquireWayColumns.Length == 0)
        {
            Log.Warning("EggDataRow parse failed because AcquireWays is empty, code '{0}'.", code);
            return false;
        }

        for (int i = 0; i < acquireWayColumns.Length; i++)
        {
            string acquireWayName = acquireWayColumns[i].Trim();
            if (!Enum.TryParse(acquireWayName, true, out EggAcquireWay acquireWay) || acquireWay == EggAcquireWay.None)
            {
                Log.Warning("EggDataRow parse failed because AcquireWay '{0}' is invalid, code '{1}'.", acquireWayName, code);
                return false;
            }

            acquireWays |= acquireWay;
        }

        return acquireWays != EggAcquireWay.None;
    }
}
