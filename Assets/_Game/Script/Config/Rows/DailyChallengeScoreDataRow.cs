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
    private const int ColumnCount = 7;

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
    /// 普通三消时，每张被消除卡牌按当前轮基础分计分。
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
    /// 连击时间窗口（秒）。
    /// 复活奖励 Combo UI 的倒计时窗口。
    /// </summary>
    public float ComboWindowSeconds { get; private set; }

    /// <summary>
    /// 连击倍率系数。
    /// 当前保留给复活 Combo 奖励扩展，不参与普通三消每槽/类型分量计算。
    /// </summary>
    public float ComboMultiplier { get; private set; }

    /// <summary>
    /// 胜利分数翻倍倍率。
    /// 全部消除胜利时，总分乘以此倍率。
    /// </summary>
    public int VictoryScoreMultiplier { get; private set; }

    /// <summary>
    /// 根据当前轮次计算每轮基础分。
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

        if (!float.TryParse(columns[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float comboWindowSeconds) || comboWindowSeconds <= 0f)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because ComboWindowSeconds '{0}' is invalid.", columns[2]);
            return false;
        }

        if (!float.TryParse(columns[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float comboMultiplier) || comboMultiplier < 0f)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because ComboMultiplier '{0}' is invalid.", columns[3]);
            return false;
        }

        if (!int.TryParse(columns[4], out int victoryScoreMultiplier) || victoryScoreMultiplier < 1)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because VictoryScoreMultiplier '{0}' is invalid.", columns[4]);
            return false;
        }

        if (!int.TryParse(columns[5], out int baseScoreIncreaseRoundInterval) || baseScoreIncreaseRoundInterval < 0)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because BaseScoreIncreaseRoundInterval '{0}' is invalid.", columns[5]);
            return false;
        }

        if (!int.TryParse(columns[6], out int baseScoreIncreasePerInterval) || baseScoreIncreasePerInterval < 0)
        {
            Log.Warning("DailyChallengeScoreDataRow parse failed because BaseScoreIncreasePerInterval '{0}' is invalid.", columns[6]);
            return false;
        }

        _id = id;
        BaseScorePerCard = baseScorePerCard;
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
