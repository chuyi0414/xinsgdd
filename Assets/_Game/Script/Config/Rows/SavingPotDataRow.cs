using System;
using System.Text;
using UnityGameFramework.Runtime;

/// <summary>
/// 存钱罐配置数据表行。
/// 用于配置存钱罐每个等级的升级消耗与效果参数。
/// </summary>
public sealed class SavingPotDataRow : DataRowBase, ICodeDataRow
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
    /// 合法 Code 前缀。
    /// </summary>
    private const string CodePrefix = "savingpot_";

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
    /// 是否初始解锁。
    /// </summary>
    public bool IsInitiallyUnlocked { get; private set; }

    /// <summary>
    /// 当前等级（1 基）。
    /// </summary>
    public int CurrentLevel { get; private set; }

    /// <summary>
    /// 升级到此等级所需的星星阈值。
    /// </summary>
    public int RequiredStars { get; private set; }

    /// <summary>
    /// 升级到此等级后获得的星星。
    /// </summary>
    public int RewardStars { get; private set; }

    /// <summary>
    /// 升级到此等级所需的金币。
    /// 初始解锁等级固定为 0。
    /// </summary>
    public int UpgradeGold { get; private set; }

    /// <summary>
    /// 效果参数。
    /// </summary>
    public int EffectParam { get; private set; }

    /// <summary>
    /// 备注描述。
    /// </summary>
    public string Description { get; private set; }

    /// <summary>
    /// 从文本行解析存钱罐配置。
    /// </summary>
    /// <param name="dataRowString">原始数据行文本。</param>
    /// <param name="userData">额外上下文。</param>
    /// <returns>是否解析成功。</returns>
    public override bool ParseDataRow(string dataRowString, object userData)
    {
        if (string.IsNullOrWhiteSpace(dataRowString))
        {
            Log.Warning("SavingPotDataRow parse failed because row string is empty.");
            return false;
        }

        string[] columns = dataRowString.Split(ColumnSplitSeparator, StringSplitOptions.None);
        if (columns.Length != ColumnCount)
        {
            Log.Warning("SavingPotDataRow parse failed because column count '{0}' is invalid, row '{1}'.", columns.Length, dataRowString);
            return false;
        }

        if (!int.TryParse(columns[0], out int id) || id <= 0)
        {
            Log.Warning("SavingPotDataRow parse failed because Id '{0}' is invalid.", columns[0]);
            return false;
        }

        string code = columns[1].Trim();
        if (string.IsNullOrWhiteSpace(code) || !code.StartsWith(CodePrefix, StringComparison.Ordinal))
        {
            Log.Warning("SavingPotDataRow parse failed because Code '{0}' is invalid.", columns[1]);
            return false;
        }

        if (!bool.TryParse(columns[2].Trim(), out bool isInitiallyUnlocked))
        {
            Log.Warning("SavingPotDataRow parse failed because IsInitiallyUnlocked '{0}' is invalid, code '{1}'.", columns[2], code);
            return false;
        }

        if (!int.TryParse(columns[3], out int currentLevel) || currentLevel <= 0)
        {
            Log.Warning("SavingPotDataRow parse failed because CurrentLevel '{0}' is invalid, code '{1}'.", columns[3], code);
            return false;
        }

        if (!int.TryParse(columns[4], out int requiredStars) || requiredStars < 0)
        {
            Log.Warning("SavingPotDataRow parse failed because RequiredStars '{0}' is invalid, code '{1}'.", columns[4], code);
            return false;
        }

        if (!int.TryParse(columns[5], out int rewardStars) || rewardStars < 0)
        {
            Log.Warning("SavingPotDataRow parse failed because RewardStars '{0}' is invalid, code '{1}'.", columns[5], code);
            return false;
        }

        if (!int.TryParse(columns[6], out int upgradeGold) || upgradeGold < 0)
        {
            Log.Warning("SavingPotDataRow parse failed because UpgradeGold '{0}' is invalid, code '{1}'.", columns[6], code);
            return false;
        }

        if (!int.TryParse(columns[7], out int effectParam) || effectParam < 0)
        {
            Log.Warning("SavingPotDataRow parse failed because EffectParam '{0}' is invalid, code '{1}'.", columns[7], code);
            return false;
        }

        if (isInitiallyUnlocked && currentLevel == 1 && (upgradeGold != 0 || requiredStars != 0 || rewardStars != 0))
        {
            Log.Warning("SavingPotDataRow parse failed because initially unlocked row '{0}' must have UpgradeGold=0, RequiredStars=0, RewardStars=0.", code);
            return false;
        }

        if (!isInitiallyUnlocked && upgradeGold <= 0)
        {
            Log.Warning("SavingPotDataRow parse failed because locked row '{0}' must have UpgradeGold > 0.", code);
            return false;
        }

        _id = id;
        Code = code;
        IsInitiallyUnlocked = isInitiallyUnlocked;
        CurrentLevel = currentLevel;
        RequiredStars = requiredStars;
        RewardStars = rewardStars;
        UpgradeGold = upgradeGold;
        EffectParam = effectParam;
        Description = columns[8].Trim();
        return true;
    }

    /// <summary>
    /// 从二进制数据解析存钱罐配置。
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
