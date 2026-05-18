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
    /// 当前 Fruit.txt 结构为：Id、Code、Name、IsUnlocked、IconPath、DailyChallengePath、UnlockGold、RequiredStars、RewardStars、CoinProbability、CoinAmount、ProduceSeconds、Description。
    /// </summary>
    private const int ColumnCount = 13;

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
    /// 用于水果图鉴、水果实体等常规展示场景。
    /// </summary>
    public string IconPath { get; private set; }

    /// <summary>
    /// 每日关卡图资源路径。
    /// 专供每日一关消除卡使用的卡图路径，与 IconPath 区分。
    /// 允许为空：为空时回退到 IconPath（通过 EffectiveDailyChallengePath 统一获取）。
    /// </summary>
    public string DailyChallengePath { get; private set; }

    /// <summary>
    /// 获取每日关卡实际使用的资源路径。
    /// 若 DailyChallengePath 非空则直接返回，否则回退到 IconPath。
    /// 业务层应始终通过此属性取路径，避免手动判空。
    /// </summary>
    public string EffectiveDailyChallengePath
        => !string.IsNullOrWhiteSpace(DailyChallengePath) ? DailyChallengePath : IconPath;

    /// <summary>
    /// 解锁所需金币。
    /// </summary>
    public int UnlockGold { get; private set; }

    /// <summary>
    /// 解锁所需星星。
    /// 仅作为阈值校验，不会被消耗；0 表示无星星限制。
    /// </summary>
    public int RequiredStars { get; private set; }

    /// <summary>
    /// 首次解锁该水果时发放给玩家的星星。
    /// 0 表示不发星；IsUnlocked=true的默认解锁水果必须为 0，与 ArchitectureSlotDataRow.RewardStars 同语义。
    /// </summary>
    public int RewardStars { get; private set; }

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
    /// 水果图鉴详情描述。
    /// 该字段只负责 FruitTJUIForm 的 _txtParticularsDescription 展示，不参与运行时数值计算。
    /// </summary>
    public string Description { get; private set; }

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

        // 每日关卡图路径：允许为空，为空时回退到 IconPath。
        string dailyChallengePath = columns[5].Trim();

        if (!int.TryParse(columns[6], out int unlockGold) || unlockGold < 0)
        {
            Log.Warning("FruitDataRow parse failed because UnlockGold '{0}' is invalid, code '{1}'.", columns[6], code);
            return false;
        }

        // 默认解锁水果(IsUnlocked=true)的 UnlockGold 在运行时不会被任何模块消费：
        //   - ShuiGuoTJUIForm 的解锁按钮/金币文案在 isUnlocked=true 时整体 SetActive(false)；
        //   - PlayerRuntimeModule.TryPurchaseFruit 永远不会被默认解锁水果触发（默认解锁走 InitializeFruitCatalog）。
        // 但策划/数据可能误把"如果未来要重置时的售价"也填到这里，硬 fail 会拖垮整张数据表加载——
        // 改为"容错 + 强制归零 + Warning"：保护数据表加载链路，并保证运行时语义无副作用。
        if (isUnlocked && unlockGold != 0)
        {
            Log.Warning("FruitDataRow: unlocked fruit '{0}' has non-zero UnlockGold '{1}', will be coerced to 0 (default-unlocked fruits never consume UnlockGold).", code, unlockGold.ToString());
            unlockGold = 0;
        }

        // 未解锁水果允许 UnlockGold = 0：表示免费解锁（玩家点按钮即可拿到，仍按 RewardStars 发星）。
        // > 0 走金币购买路径；< 0 已在前面 columns[6] 解析时被挡掉，这里无需再防御。

        if (!int.TryParse(columns[7], out int requiredStars) || requiredStars < 0)
        {
            Log.Warning("FruitDataRow parse failed because RequiredStars '{0}' is invalid, code '{1}'.", columns[7], code);
            return false;
        }

        // [8] RewardStars — 允许 0（不发星）但禁止负数。
        // 仅在 TryUnlockFruit（金币购买/未来看广告等运行时解锁）成功首解时才会调 AddStars 发放；
        // 默认解锁(IsUnlocked=true)的水果走 InitializeFruitCatalog 的"只入集合不发星"路径，
        // 此处即使填非 0 也不会被消费——字段保留是为日后"账号首登一次性奖励"等持久化路径预留。
        if (!int.TryParse(columns[8], out int rewardStars) || rewardStars < 0)
        {
            Log.Warning("FruitDataRow parse failed because RewardStars '{0}' is invalid, code '{1}'.", columns[8], code);
            return false;
        }

        if (!int.TryParse(columns[9], out int coinProbability) || coinProbability < 0 || coinProbability > 100)
        {
            Log.Warning("FruitDataRow parse failed because CoinProbability '{0}' is invalid, code '{1}'.", columns[9], code);
            return false;
        }

        if (!int.TryParse(columns[10], out int coinAmount) || coinAmount <= 0)
        {
            Log.Warning("FruitDataRow parse failed because CoinAmount '{0}' is invalid, code '{1}'.", columns[10], code);
            return false;
        }

        if (!int.TryParse(columns[11], out int produceSeconds) || produceSeconds <= 0)
        {
            Log.Warning("FruitDataRow parse failed because ProduceSeconds '{0}' is invalid, code '{1}'.", columns[11], code);
            return false;
        }

        string description = columns[12].Trim();
        if (string.IsNullOrWhiteSpace(description))
        {
            Log.Warning("FruitDataRow parse failed because Description is empty, code '{0}'.", code);
            return false;
        }

        _id = id;
        Code = code;
        Name = name;
        IsUnlocked = isUnlocked;
        IconPath = iconPath;
        DailyChallengePath = dailyChallengePath;
        UnlockGold = unlockGold;
        RequiredStars = requiredStars;
        RewardStars = rewardStars;
        CoinProbability = coinProbability;
        CoinAmount = coinAmount;
        ProduceSeconds = produceSeconds;
        Description = description;
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
