using System;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 每日一关得分配置数据表行。
/// 承载得分计算、连击窗口、胜利翻倍等数值配置。
/// </summary>
public sealed class DailyChallengeScoreDataRow : DataRowBase
{
    /// <summary>
    /// 列拆分分隔符。
    /// </summary>
    private static readonly string[] ColumnSplitSeparator = { "\t" };

    /// <summary>
    /// 数据表固定列数。
    /// </summary>
    private const int ColumnCount = 14;

    /// <summary>
    /// 当前行的内部 Id 缓存。
    /// </summary>
    private int _id;

    /// <summary>
    /// 唯一 Id。
    /// </summary>
    public override int Id => _id;

    /// <summary>
    /// 每轮基础分。
    /// 满格清空时，每轮叠加一次此分值到单卡分上（不按类型重复叠加）。
    /// </summary>
    public int BaseScorePerCard { get; private set; }

    /// <summary>
    /// 基础分增长间隔（按轮次）。
    /// 每经过 N 轮后，下一轮生效一次基础分增长。
    /// 例如 interval=10 时，第11轮开始应用第1次增长。
    /// </summary>
    public int BaseScoreIncreaseRoundInterval { get; private set; }

    /// <summary>
    /// 基础分每次增长的数值。
    /// 当轮次达到增长间隔门槛后，基础分按该值阶梯递增。
    /// </summary>
    public int BaseScoreIncreasePerInterval { get; private set; }

    /// <summary>
    /// 同类型恰好 2 张时的分量分。
    /// 该档位为独立分量分，不叠加 BaseScorePerCard。
    /// 所有同类型≥2 的分量分累加后再加上每轮基础分，构成单卡总分。
    /// </summary>
    public int SameTypeTwoScorePerCard { get; private set; }

    /// <summary>
    /// 同类型恰好 3 张时的分量分。
    /// 独立分量分，不叠加 SameTypeTwoScorePerCard。
    /// </summary>
    public int SameTypeThreeBonusPerCard { get; private set; }

    /// <summary>
    /// 同类型恰好 4 张时的分量分。
    /// </summary>
    public int SameTypeFourBonusPerCard { get; private set; }

    /// <summary>
    /// 同类型恰好 5 张时的分量分。
    /// </summary>
    public int SameTypeFiveBonusPerCard { get; private set; }

    /// <summary>
    /// 同类型恰好 6 张时的分量分。
    /// </summary>
    public int SameTypeSixBonusPerCard { get; private set; }

    /// <summary>
    /// 同类型恰好 7 张时的分量分。
    /// </summary>
    public int SameTypeSevenBonusPerCard { get; private set; }

    /// <summary>
    /// 同类型恰好 8 张时的分量分。
    /// </summary>
    public int SameTypeEightBonusPerCard { get; private set; }

    /// <summary>
    /// 连击时间窗口（秒）。
    /// 两次满格清空之间的时间间隔若小于此值，则连击计数 +1。
    /// </summary>
    public float ComboWindowSeconds { get; private set; }

    /// <summary>
    /// 连击倍率系数。
    /// 连击数≥2时，倍率因子 = 连击数 × comboMultiplier。
    /// 连击数=1时固定1倍，不使用该系数。
    /// 值为 1 表示连击数直接作为倍率因子。
    /// </summary>
    public float ComboMultiplier { get; private set; }

    /// <summary>
    /// 胜利分数翻倍倍率。
    /// 全部消除胜利时，总分乘以此倍率。
    /// </summary>
    public int VictoryScoreMultiplier { get; private set; }

    /// <summary>
    /// 根据同类型卡片数量获取该类型的分量分。
    /// 对齐参考项目 xinpgdd 的 GetMainlineFullAreaPerCardTypeComponentScore：
    /// - 1 张或 0 张：不参与叠加分，返回 0；
    /// - 2 张：返回 SameTypeTwoScorePerCard（独立分量分）；
    /// - 3~8 张：返回对应 BonusPerCard（独立分量分，不叠加 2 张基础）；
    /// - 超出 8 张按 8 张处理。
    /// 所有同类型≥2 的分量分累加后 + 每轮基础分 = 单卡总分。
    /// </summary>
    /// <param name="sameTypeCount">同类型卡片数量。</param>
    /// <returns>该类型的分量分。</returns>
    public int GetComponentScore(int sameTypeCount)
    {
        // ⚠️ 避坑：单牌类型不参与叠加分，返回 0
        if (sameTypeCount <= 1)
        {
            return 0;
        }

        if (sameTypeCount == 2)
        {
            return SameTypeTwoScorePerCard;
        }

        // 3~8 张：独立分量分，不叠加 2 张基础
        // ⚠️ 避坑：这里用 switch 而非数组索引，避免运行时越界
        return sameTypeCount switch
        {
            3 => SameTypeThreeBonusPerCard,
            4 => SameTypeFourBonusPerCard,
            5 => SameTypeFiveBonusPerCard,
            6 => SameTypeSixBonusPerCard,
            7 => SameTypeSevenBonusPerCard,
            _ => SameTypeEightBonusPerCard  // >=8 按 8 张处理
        };
    }

    /// <summary>
    /// 根据当前轮次计算每轮基础分。
    /// 对齐参考项目 xinpgdd 的 GetMainlineFullAreaBaseScorePerRound：
    /// baseScore = baseScorePerCard + completedIntervals × baseScoreIncreasePerInterval。
    /// completedIntervals = Max(0, (currentRound - 1) / interval)。
    /// </summary>
    /// <param name="currentRound">当前轮次（从1开始）。</param>
    /// <returns>当前轮次的基础分。</returns>
    public int GetBaseScorePerRound(int currentRound)
    {
        int baseScore = BaseScorePerCard;
        int interval = BaseScoreIncreaseRoundInterval;
        int increase = BaseScoreIncreasePerInterval;

        if (interval <= 0 || increase <= 0)
        {
            return baseScore;
        }

        int completedIntervals = Mathf.Max(0, (currentRound - 1) / interval);
        return baseScore + completedIntervals * increase;
    }

    /// <summary>
    /// 从文本行解析每日一关得分配置数据。
    /// </summary>
    /// <param name="dataRowString">原始数据行文本。</param>
    /// <param name="userData">额外上下文。</param>
    /// <returns>是否解析成功。</returns>
    public override bool ParseDataRow(string dataRowString, object userData)
    {
        if (string.IsNullOrWhiteSpace(dataRowString))
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because row string is empty.");
            return false;
        }

        string[] columns = dataRowString.Split(ColumnSplitSeparator, StringSplitOptions.None);
        if (columns.Length != ColumnCount)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because column count '{0}' is invalid, row '{1}'.", columns.Length, dataRowString);
            return false;
        }

        if (!int.TryParse(columns[0], out int id) || id <= 0)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because Id '{0}' is invalid.", columns[0]);
            return false;
        }

        if (!int.TryParse(columns[1], out int baseScorePerCard) || baseScorePerCard < 0)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because BaseScorePerCard '{0}' is invalid.", columns[1]);
            return false;
        }

        if (!int.TryParse(columns[2], out int sameTypeTwoScorePerCard) || sameTypeTwoScorePerCard < 0)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because SameTypeTwoScorePerCard '{0}' is invalid.", columns[2]);
            return false;
        }

        if (!int.TryParse(columns[3], out int sameTypeThreeBonusPerCard) || sameTypeThreeBonusPerCard < 0)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because SameTypeThreeBonusPerCard '{0}' is invalid.", columns[3]);
            return false;
        }

        if (!int.TryParse(columns[4], out int sameTypeFourBonusPerCard) || sameTypeFourBonusPerCard < 0)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because SameTypeFourBonusPerCard '{0}' is invalid.", columns[4]);
            return false;
        }

        if (!int.TryParse(columns[5], out int sameTypeFiveBonusPerCard) || sameTypeFiveBonusPerCard < 0)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because SameTypeFiveBonusPerCard '{0}' is invalid.", columns[5]);
            return false;
        }

        if (!int.TryParse(columns[6], out int sameTypeSixBonusPerCard) || sameTypeSixBonusPerCard < 0)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because SameTypeSixBonusPerCard '{0}' is invalid.", columns[6]);
            return false;
        }

        if (!int.TryParse(columns[7], out int sameTypeSevenBonusPerCard) || sameTypeSevenBonusPerCard < 0)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because SameTypeSevenBonusPerCard '{0}' is invalid.", columns[7]);
            return false;
        }

        if (!int.TryParse(columns[8], out int sameTypeEightBonusPerCard) || sameTypeEightBonusPerCard < 0)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because SameTypeEightBonusPerCard '{0}' is invalid.", columns[8]);
            return false;
        }

        if (!float.TryParse(columns[9], NumberStyles.Float, CultureInfo.InvariantCulture, out float comboWindowSeconds) || comboWindowSeconds <= 0f)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because ComboWindowSeconds '{0}' is invalid.", columns[9]);
            return false;
        }

        if (!float.TryParse(columns[10], NumberStyles.Float, CultureInfo.InvariantCulture, out float comboMultiplier) || comboMultiplier < 0f)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because ComboMultiplier '{0}' is invalid.", columns[10]);
            return false;
        }

        if (!int.TryParse(columns[11], out int victoryScoreMultiplier) || victoryScoreMultiplier < 1)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because VictoryScoreMultiplier '{0}' is invalid.", columns[11]);
            return false;
        }

        if (!int.TryParse(columns[12], out int baseScoreIncreaseRoundInterval) || baseScoreIncreaseRoundInterval < 0)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because BaseScoreIncreaseRoundInterval '{0}' is invalid.", columns[12]);
            return false;
        }

        if (!int.TryParse(columns[13], out int baseScoreIncreasePerInterval) || baseScoreIncreasePerInterval < 0)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because BaseScoreIncreasePerInterval '{0}' is invalid.", columns[13]);
            return false;
        }

        _id = id;
        BaseScorePerCard = baseScorePerCard;
        SameTypeTwoScorePerCard = sameTypeTwoScorePerCard;
        SameTypeThreeBonusPerCard = sameTypeThreeBonusPerCard;
        SameTypeFourBonusPerCard = sameTypeFourBonusPerCard;
        SameTypeFiveBonusPerCard = sameTypeFiveBonusPerCard;
        SameTypeSixBonusPerCard = sameTypeSixBonusPerCard;
        SameTypeSevenBonusPerCard = sameTypeSevenBonusPerCard;
        SameTypeEightBonusPerCard = sameTypeEightBonusPerCard;
        ComboWindowSeconds = comboWindowSeconds;
        ComboMultiplier = comboMultiplier;
        VictoryScoreMultiplier = victoryScoreMultiplier;
        BaseScoreIncreaseRoundInterval = baseScoreIncreaseRoundInterval;
        BaseScoreIncreasePerInterval = baseScoreIncreasePerInterval;
        return true;
    }

    /// <summary>
    /// 从二进制数据解析每日一关得分配置数据。
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
