using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 玩家运行时模块。
/// 负责维护当前会话内的水果解锁进度、宠物抽取期望水果、
/// 玩家金币以及宠物产出物库存。
/// </summary>
public sealed partial class PlayerRuntimeModule
{
    /// <summary>
    /// 建筑条目类型。
    /// </summary>
    public enum ArchitectureCategory
    {
        /// <summary>
        /// 孵化区。
        /// </summary>
        Hatch = 1,

        /// <summary>
        /// 饮食区。
        /// </summary>
        Diet = 2,

        /// <summary>
        /// 农场区。
        /// </summary>
        Fruiter = 3,

        /// <summary>
        /// 存钱罐。
        /// </summary>
        SavingPot = 4,
    }

    /// <summary>
    /// 建筑条目当前可执行的动作类型。
    /// </summary>
    public enum ArchitectureActionType
    {
        /// <summary>
        /// 当前没有可执行动作。
        /// </summary>
        None = 0,

        /// <summary>
        /// 当前可购买。
        /// </summary>
        Buy = 1,

        /// <summary>
        /// 当前可升级。
        /// </summary>
        Upgrade = 2,

        /// <summary>
        /// 当前已满级。
        /// </summary>
        Max = 3,
    }

    /// <summary>
    /// 建筑执行购买/升级动作失败的原因。
    /// 由 TryExecuteArchitectureAction 通过 out 参数返回，UI 据此分流 Toast。
    /// </summary>
    public enum ArchitectureActionFailureReason
    {
        /// <summary>
        /// 没有失败。仅在 TryExecuteArchitectureAction 返回 true 时使用。
        /// </summary>
        None = 0,

        /// <summary>
        /// 当前不可购买/升级（顺序未达成、已满级、位置非法或未初始化）。
        /// </summary>
        NotPurchasable = 1,

        /// <summary>
        /// 星星不足以满足前置阈值。
        /// </summary>
        NotEnoughStars = 2,

        /// <summary>
        /// 金币不足以支付当前花费。
        /// </summary>
        NotEnoughGold = 3,
    }

    /// <summary>
    /// 水果购买失败原因。
    /// 由 TryPurchaseFruit 通过 out 参数返回，UI 据此区分金币不足与星星不足。
    /// </summary>
    public enum FruitPurchaseFailureReason
    {
        /// <summary>
        /// 没有失败。仅在 TryPurchaseFruit 返回 true 时使用。
        /// </summary>
        None = 0,

        /// <summary>
        /// 水果编码无效、已解锁或运行时模块未初始化。
        /// </summary>
        NotPurchasable = 1,

        /// <summary>
        /// 星星不足以满足解锁阈值。
        /// </summary>
        NotEnoughStars = 2,

        /// <summary>
        /// 金币不足以支付解锁花费。
        /// </summary>
        NotEnoughGold = 3,
    }

    /// <summary>
    /// 概率计算统一使用 100 作为满值。
    /// </summary>
    private const int FullProbability = 100;

    /// <summary>
    /// 单只宠物的产出物随机池。
    /// 三档（Primary / Intermediate / Advanced）已废弃；产出物由 Pet.ProduceProbability 决定是否触发，
    /// 触发后从同 PetId 的池子里等概率随机挑 1 条。Warmup 阶段一次性建池，运行时零 GC。
    /// </summary>
    private sealed class PetProducePool
    {
        /// <summary>
        /// 该宠物名下的全部产出物配置（顺序按 PetProduce.txt 出现顺序）。
        /// </summary>
        public readonly List<PetProduceDataRow> Items = new List<PetProduceDataRow>(4);
    }

    /// <summary>
    /// 单个建筑格子的运行时状态。
    /// </summary>
    private sealed class ArchitectureSlotState
    {
        /// <summary>
        /// 当前格子是否已经解锁。
        /// </summary>
        public bool IsUnlocked;

        /// <summary>
        /// 当前格子的建筑等级。
        /// 未解锁时固定为 0，解锁后从 1 级起步。
        /// </summary>
        public int Level;
    }

    /// <summary>
    /// 单个建筑格子的只读快照。
    /// UI 层只消费这个快照，不直接读写内部状态。
    /// </summary>
    public readonly struct ArchitectureEntryState
    {
        /// <summary>
        /// 建筑条目类型。
        /// </summary>
        public readonly ArchitectureCategory Category;

        /// <summary>
        /// 1 基索引。
        /// 例如“孵化区 1 号”这里就是 1。
        /// </summary>
        public readonly int SlotIndex;

        /// <summary>
        /// 当前是否已经解锁。
        /// </summary>
        public readonly bool IsUnlocked;

        /// <summary>
        /// 当前等级。
        /// 未解锁时固定为 0，解锁后从 1 开始。
        /// </summary>
        public readonly int Level;

        /// <summary>
        /// 当前最大等级。
        /// </summary>
        public readonly int MaxLevel;

        /// <summary>
        /// 当前可执行动作。
        /// </summary>
        public readonly ArchitectureActionType ActionType;

        /// <summary>
        /// 当前动作需要消耗的金币。
        /// 当前无动作或已满级时固定为 0。
        /// </summary>
        public readonly int Cost;

        /// <summary>
        /// 当前是否已经满级。
        /// </summary>
        public bool IsMaxLevel => IsUnlocked && Level >= MaxLevel;

        /// <summary>
        /// 构造一个建筑条目快照。
        /// </summary>
        /// <param name="category">建筑条目类型。</param>
        /// <param name="slotIndex">1 基索引。</param>
        /// <param name="isUnlocked">是否已解锁。</param>
        /// <param name="level">当前等级。</param>
        /// <param name="maxLevel">最大等级。</param>
        /// <param name="actionType">当前动作类型。</param>
        /// <param name="cost">当前动作金币消耗。</param>
        public ArchitectureEntryState(
            ArchitectureCategory category,
            int slotIndex,
            bool isUnlocked,
            int level,
            int maxLevel,
            ArchitectureActionType actionType,
            int cost)
        {
            Category = category;
            SlotIndex = slotIndex;
            IsUnlocked = isUnlocked;
            Level = level;
            MaxLevel = maxLevel;
            ActionType = actionType;
            Cost = cost;
        }
    }

    /// <summary>
    /// 孵化区建筑条目数量。
    /// 当前 UI 与孵化模块都只支持 4 个条目。
    /// </summary>
    public const int HatchArchitectureCountValue = 4;

    /// <summary>
    /// 饮食区建筑条目数量。
    /// </summary>
    public const int DietArchitectureCountValue = 6;

    /// <summary>
    /// 农场区建筑条目数量。
    /// </summary>
    public const int FruiterArchitectureCountValue = 6;

    /// <summary>
    /// 存钱罐建筑条目数量。
    /// 存钱罐只有 1 个槽位，包含 5 个升级等级。
    /// </summary>
    public const int SavingPotArchitectureCountValue = 1;

    /// <summary>
    /// 当前运行时模块是否已经完成初始化。
    /// </summary>
    private bool _isInitialized;

    /// <summary>
    /// 全局玩法规则缓存。
    /// </summary>
    private GameplayRuleDataRow _gameplayRuleDataRow;

    /// <summary>
    /// 云端分配的玩家显示名。
    /// 当前用于每日一关排行榜展示，由云函数在创建账号时生成。
    /// </summary>
    private string _playerName = string.Empty;

    /// <summary>
    /// 云端分配的玩家唯一编号。
    /// 固定为玩家昵称后 10 位数字，创建账号后通常不会变化。
    /// </summary>
    private string _playerCode = string.Empty;

    /// <summary>
    /// 每日一关历史最高分缓存。
    /// 由云存档读取和排行榜提交响应同步，供每日关入口展示。
    /// </summary>
    private int _dailyChallengeHistoricalBestScore;

    /// <summary>
    /// 每日一关历史最高分达成时间。
    /// 使用服务端返回的字符串，仅作为低频展示/排查字段。
    /// </summary>
    private string _dailyChallengeHistoricalBestTime = string.Empty;

    /// <summary>
    /// 云端分配的玩家显示名。
    /// </summary>
    public string PlayerName => _playerName;

    /// <summary>
    /// 云端分配的玩家唯一编号。
    /// </summary>
    public string PlayerCode => _playerCode;

    /// <summary>
    /// 每日一关历史最高分。
    /// </summary>
    public int DailyChallengeHistoricalBestScore => _dailyChallengeHistoricalBestScore;

    /// <summary>
    /// 每日一关历史最高分达成时间。
    /// </summary>
    public string DailyChallengeHistoricalBestTime => _dailyChallengeHistoricalBestTime;

    /// <summary>
    /// 构造玩家运行时模块，并初始化建筑默认状态。
    /// </summary>
    public PlayerRuntimeModule()
    {
        ResetArchitectureRuntimeState();
    }

    /// <summary>
    /// 确保水果运行时状态已根据数据表完成初始化。
    /// 内部按领域分发到各子初始化方法，保持单一入口。
    /// </summary>
    /// <returns>是否初始化成功。</returns>
    public bool EnsureInitialized()
    {
        if (_isInitialized)
        {
            return true;
        }

        if (GameEntry.DataTables == null
            || !GameEntry.DataTables.IsAvailable<FruitDataRow>()
            || !GameEntry.DataTables.IsAvailable<GameplayRuleDataRow>()
            || !GameEntry.DataTables.IsAvailable<ArchitectureSlotDataRow>()
            || !GameEntry.DataTables.IsAvailable<ArchitectureUpgradeDataRow>()
            || !GameEntry.DataTables.IsAvailable<ArchitectureDataRow>()
            || !GameEntry.DataTables.IsAvailable<SavingPotDataRow>())
        {
            Log.Warning("PlayerRuntimeModule 无法初始化，所需数据表不可用。");
            return false;
        }

        FruitDataRow[] fruitRows = GameEntry.DataTables.GetAllDataRows<FruitDataRow>();
        ArchitectureSlotDataRow[] architectureSlotRows = GameEntry.DataTables.GetAllDataRows<ArchitectureSlotDataRow>();
        ArchitectureUpgradeDataRow[] architectureUpgradeRows = GameEntry.DataTables.GetAllDataRows<ArchitectureUpgradeDataRow>();
        ArchitectureDataRow[] architectureRows = GameEntry.DataTables.GetAllDataRows<ArchitectureDataRow>();
        GameplayRuleDataRow gameplayRuleDataRow = GameEntry.DataTables.GetDataRowByCode<GameplayRuleDataRow>(GameplayRuleDataRow.DefaultCode);
        if (fruitRows == null
            || fruitRows.Length == 0
            || architectureSlotRows == null
            || architectureSlotRows.Length == 0
            || architectureUpgradeRows == null
            || architectureUpgradeRows.Length == 0
            || architectureRows == null
            || architectureRows.Length == 0
            || gameplayRuleDataRow == null)
        {
            Log.Warning("PlayerRuntimeModule 无法初始化，所需数据行为空。");
            return false;
        }

        // 1. 建筑配置缓存
        if (!RebuildArchitectureConfigCaches(architectureSlotRows, architectureUpgradeRows, architectureRows))
        {
            Log.Warning("PlayerRuntimeModule 无法初始化，建筑配置缓存重建失败。");
            return false;
        }

        // 1.1 存钱罐配置缓存
        SavingPotDataRow[] savingPotRows = GameEntry.DataTables.GetAllDataRows<SavingPotDataRow>();
        if (savingPotRows == null || savingPotRows.Length == 0)
        {
            Log.Warning("PlayerRuntimeModule 无法初始化，存钱罐数据行为空。");
            return false;
        }

        RebuildSavingPotConfigCaches(savingPotRows);

        // 2. 玩法规则
        _gameplayRuleDataRow = gameplayRuleDataRow;

        // 2.1 运行时星星计数器：从 GameplayRule.InitialStars 注入，并以事件广播一次，保证订阅方收到初值。
        _currentStars = gameplayRuleDataRow.InitialStars;
        StarsChanged?.Invoke(_currentStars);

        // 3. 水果目录
        InitializeFruitCatalog(fruitRows);

        // 4. 外观装饰（头像/头像框）
        InitializeCosmetics();

        // 5. 建筑运行时状态（依赖建筑配置缓存，必须在其后）
        ResetArchitectureRuntimeStateFromConfig();

        _isInitialized = true;
        _isCandidateCacheDirty = true;
        RebuildCandidateCachesIfNeeded();
        return true;
    }

    /// <summary>
    /// 根据排行榜云函数响应刷新每日一关历史最高分缓存。
    /// </summary>
    /// <param name="score">服务端确认后的历史最高分。</param>
    /// <param name="achievedTime">历史最高分达成时间。</param>
    public void ApplyDailyChallengeHistoricalBestScore(int score, string achievedTime)
    {
        int normalizedScore = Mathf.Max(0, score);
        if (normalizedScore < _dailyChallengeHistoricalBestScore)
        {
            return;
        }

        _dailyChallengeHistoricalBestScore = normalizedScore;
        _dailyChallengeHistoricalBestTime = achievedTime ?? string.Empty;
    }

    /// <summary>
    /// 初始化水果目录缓存。
    /// 从数据表中读取所有水果行，将 IsUnlocked 的水果写入运行时集合。
    /// </summary>
    /// <param name="fruitRows">全部水果数据行。</param>
    private void InitializeFruitCatalog(FruitDataRow[] fruitRows)
    {
        _allFruitRows = fruitRows;
        _unlockedFruitCandidates = new FruitDataRow[fruitRows.Length];
        _lockedFruitCandidates = new FruitDataRow[fruitRows.Length];
        _otherUnlockedFruitCandidates = new FruitDataRow[fruitRows.Length];
        _unlockedFruitCodes.Clear();
        _unlockedPetCodes.Clear();

        for (int i = 0; i < fruitRows.Length; i++)
        {
            FruitDataRow fruitRow = fruitRows[i];
            if (fruitRow == null)
            {
                continue;
            }

            if (fruitRow.IsUnlocked)
            {
                // 默认解锁路径“只入集合不发星”：当前项目未接入存档，每次启动都会清空 _unlockedFruitCodes 重新注入，
                // 若在这里调 AddStars 会导致每次重启都重复派发默认解锁星，造成星星通胀。
                // FruitDataRow.RewardStars 字段对默认解锁水果继续保留——为日后“账号首登一次性奖励”等持久化路径预留语义。
                _unlockedFruitCodes.Add(fruitRow.Code);
            }
        }
    }
}
