using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 玩家运行时模块。
/// 负责维护当前会话内的水果解锁进度、宠物抽取期望水果、
/// 玩家金币以及宠物产出物库存。
/// </summary>
public sealed class PlayerRuntimeModule
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
    /// 概率计算统一使用 100 作为满值。
    /// </summary>
    private const int FullProbability = 100;

    /// <summary>
    /// 单只宠物的三档产出缓存。
    /// 运行时抽产出时只命中这一层缓存，避免重复扫描数据表。
    /// </summary>
    private sealed class PetProduceBucket
    {
        /// <summary>
        /// 该宠物的初级产出配置。
        /// </summary>
        public PetProduceDataRow Primary;

        /// <summary>
        /// 该宠物的中级产出配置。
        /// </summary>
        public PetProduceDataRow Intermediate;

        /// <summary>
        /// 该宠物的高级产出配置。
        /// </summary>
        public PetProduceDataRow Advanced;
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
    /// 建筑等级从 1 开始。
    /// 未解锁条目固定视为 0 级。
    /// </summary>
    private const int InitialArchitectureLevel = 1;

    /// <summary>
    /// 建筑最大等级。
    /// </summary>
    private const int FallbackInitialUnlockedSlotCount = 1;

    /// <summary>
    /// 当前会话内已解锁的水果 Code 集合。
    /// </summary>
    private readonly HashSet<string> _unlockedFruitCodes = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前会话内已解锁的宠物 Code 集合。
    /// 宠物孵化成功后会写入这里，供宠物图鉴界面直接查询当前解锁状态。
    /// </summary>
    private readonly HashSet<string> _unlockedPetCodes = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// 当前已缓存的全部水果行。
    /// </summary>
    private FruitDataRow[] _allFruitRows = Array.Empty<FruitDataRow>();

    /// <summary>
    /// 已解锁候选桶缓存。
    /// </summary>
    private FruitDataRow[] _unlockedFruitCandidates = Array.Empty<FruitDataRow>();

    /// <summary>
    /// 未解锁候选桶缓存。
    /// </summary>
    private FruitDataRow[] _lockedFruitCandidates = Array.Empty<FruitDataRow>();

    /// <summary>
    /// 已解锁候选数量。
    /// </summary>
    private int _unlockedFruitCandidateCount;

    /// <summary>
    /// 未解锁候选数量。
    /// </summary>
    private int _lockedFruitCandidateCount;

    /// <summary>
    /// 当前运行时模块是否已经完成初始化。
    /// </summary>
    private bool _isInitialized;

    /// <summary>
    /// 候选桶缓存是否需要重建。
    /// </summary>
    private bool _isCandidateCacheDirty = true;

    /// <summary>
    /// 宠物 Code 到宠物 Id 的缓存。
    /// 产出逻辑使用宠物 Id 与 PetProduceDataRow.PetId 对齐。
    /// </summary>
    private readonly Dictionary<string, int> _petIdsByCode = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// 宠物 Id 到三档产出配置的缓存。
    /// </summary>
    private readonly Dictionary<int, PetProduceBucket> _produceBucketsByPetId = new Dictionary<int, PetProduceBucket>();

    /// <summary>
    /// 当前会话内的产出物库存。
    /// Key 为产出物 Code，Value 为当前持有数量。
    /// </summary>
    private readonly Dictionary<string, int> _produceCountsByCode = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// 产出物缓存是否已经完成预热。
    /// </summary>
    private bool _isProduceCatalogInitialized;

    // ──────────────────────────────────────────────────────────
    //  建筑 / 金币管理
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 孵化区 4 个条目的运行时状态。
    /// 当前只有 1 号位默认解锁，其余需要顺序购买。
    /// </summary>
    private readonly ArchitectureSlotState[] _hatchArchitectureStates = CreateArchitectureStateArray(HatchArchitectureCountValue);

    /// <summary>
    /// 饮食区 6 个条目的运行时状态。
    /// 当前只有 1 号位默认解锁，其余需要顺序购买。
    /// </summary>
    private readonly ArchitectureSlotState[] _dietArchitectureStates = CreateArchitectureStateArray(DietArchitectureCountValue);

    /// <summary>
    /// 农场区 6 个条目的运行时状态。
    /// 当前只有 1 号位默认解锁，其余需要顺序购买。
    /// </summary>
    private readonly ArchitectureSlotState[] _fruiterArchitectureStates = CreateArchitectureStateArray(FruiterArchitectureCountValue);

    /// <summary>
    /// 全局玩法规则缓存。
    /// </summary>
    private GameplayRuleDataRow _gameplayRuleDataRow;

    /// <summary>
    /// 建筑槽位配置缓存。
    /// Key1 为建筑类别，Key2 为 1 基槽位索引。
    /// </summary>
    private readonly Dictionary<ArchitectureCategory, Dictionary<int, ArchitectureSlotDataRow>> _architectureSlotRowsByCategory =
        new Dictionary<ArchitectureCategory, Dictionary<int, ArchitectureSlotDataRow>>();

    /// <summary>
    /// 建筑升级配置缓存。
    /// Key1 为建筑类别，Key2 为当前等级。
    /// </summary>
    private readonly Dictionary<ArchitectureCategory, Dictionary<int, ArchitectureUpgradeDataRow>> _architectureUpgradeRowsByCategory =
        new Dictionary<ArchitectureCategory, Dictionary<int, ArchitectureUpgradeDataRow>>();

    /// <summary>
    /// 各建筑类别的最大等级缓存。
    /// </summary>
    private readonly Dictionary<ArchitectureCategory, int> _maxArchitectureLevelsByCategory =
        new Dictionary<ArchitectureCategory, int>();

    /// <summary>
    /// 当前已购买的孵化槽数量，默认 1。
    /// 后续建筑购买时允许只增不减地扩容。
    /// </summary>
    private int _hatchSlotCount = 1;

    /// <summary>
    /// 当前餐桌位数量，默认 1。
    /// 后续建筑升级时允许只增不减地扩容。
    /// </summary>
    private int _diningSeatCount = 1;

    /// <summary>
    /// 当前果树位数量，默认 1。
    /// 后续建筑升级时允许只增不减地扩容。
    /// </summary>
    private int _orchardSlotCount = 1;

    /// <summary>
    /// 当前会话内的金币总额。
    /// </summary>
    private int _currentGold;

    /// <summary>
    /// 金币数量发生变化时触发。
    /// 参数为变化后的最新金币总额，UI 层监听此事件刷新显示。
    /// </summary>
    public event Action<int> GoldChanged;

    /// <summary>
    /// 建筑状态发生变化时触发。
    /// 建筑升级窗体监听该事件刷新 16 个条目。
    /// </summary>
    public event Action ArchitectureStateChanged;

    /// <summary>
    /// 产出物库存发生变化时触发。
    /// 参数一：产出物 Code。
    /// 参数二：变化后的最新数量。
    /// </summary>
    public event Action<string, int> ProduceChanged;

    /// <summary>
    /// 场地区容量发生变化时触发。
    /// 参数一为最新餐桌位数量，参数二为最新果园位数量。
    /// UI 层收到后需要重采样 marker，并把新快照同步给全局实体模块。
    /// </summary>
    public event Action<int, int> PlayfieldCapacityChanged;

    /// <summary>
    /// 当前孵化槽数量。
    /// </summary>
    public int HatchSlotCount => _hatchSlotCount;

    /// <summary>
    /// 当前餐桌位数量。
    /// </summary>
    public int DiningSeatCount => _diningSeatCount;

    /// <summary>
    /// 当前果树位数量。
    /// </summary>
    public int OrchardSlotCount => _orchardSlotCount;

    /// <summary>
    /// 当前持有的金币总额。
    /// </summary>
    public int CurrentGold => _currentGold;

    /// <summary>
    /// 构造玩家运行时模块，并初始化建筑默认状态。
    /// </summary>
    public PlayerRuntimeModule()
    {
        ResetArchitectureRuntimeState();
    }

    /// <summary>
    /// 获取指定建筑条目的快照。
    /// </summary>
    /// <param name="category">建筑条目类型。</param>
    /// <param name="slotIndex">1 基索引。</param>
    /// <returns>可供 UI 消费的只读快照。</returns>
    public ArchitectureEntryState GetArchitectureEntryState(ArchitectureCategory category, int slotIndex)
    {
        if (!EnsureInitialized())
        {
            return new ArchitectureEntryState(category, slotIndex, false, 0, InitialArchitectureLevel, ArchitectureActionType.None, 0);
        }

        ArchitectureSlotState slotState = GetArchitectureSlotState(category, slotIndex);
        int maxLevel = GetMaxArchitectureLevel(category);
        if (slotState == null)
        {
            return new ArchitectureEntryState(category, slotIndex, false, 0, maxLevel, ArchitectureActionType.None, 0);
        }

        ArchitectureActionType actionType = EvaluateArchitectureActionType(category, slotIndex, slotState, out int cost);
        return new ArchitectureEntryState(
            category,
            slotIndex,
            slotState.IsUnlocked,
            slotState.Level,
            maxLevel,
            actionType,
            cost);
    }

    /// <summary>
    /// 尝试执行一次建筑购买或升级。
    /// UI 层只调这一层，不直接操作金币与容量。
    /// </summary>
    /// <param name="category">建筑条目类型。</param>
    /// <param name="slotIndex">1 基索引。</param>
    /// <returns>是否执行成功。</returns>
    public bool TryExecuteArchitectureAction(ArchitectureCategory category, int slotIndex)
    {
        if (!EnsureInitialized())
        {
            return false;
        }

        ArchitectureSlotState slotState = GetArchitectureSlotState(category, slotIndex);
        if (slotState == null)
        {
            return false;
        }

        ArchitectureActionType actionType = EvaluateArchitectureActionType(category, slotIndex, slotState, out int cost);
        switch (actionType)
        {
            case ArchitectureActionType.Buy:
                if (!CanUnlockArchitectureSlot(category, slotIndex) || !TryConsumeGold(cost))
                {
                    return false;
                }

                slotState.IsUnlocked = true;
                slotState.Level = InitialArchitectureLevel;
                ApplyArchitectureUnlock(category, slotIndex);
                NotifyArchitectureStateChanged();
                return true;

            case ArchitectureActionType.Upgrade:
                if (!TryConsumeGold(cost))
                {
                    return false;
                }

                slotState.Level = Mathf.Clamp(slotState.Level + 1, InitialArchitectureLevel, GetMaxArchitectureLevel(category));
                NotifyArchitectureStateChanged();
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// 尝试扣除指定数量的金币。
    /// 金币不足时不做任何修改，直接返回 false。
    /// </summary>
    /// <param name="amount">要扣除的金币数量。</param>
    /// <returns>是否扣除成功。</returns>
    public bool TryConsumeGold(int amount)
    {
        if (amount <= 0)
        {
            return false;
        }

        if (_currentGold < amount)
        {
            return false;
        }

        _currentGold -= amount;
        GoldChanged?.Invoke(_currentGold);
        return true;
    }

    /// <summary>
    /// 设置孵化槽数量。
    /// 当前实现只支持容量扩张，不支持运行时缩容，避免打乱已有槽位索引。
    /// </summary>
    /// <param name="count">孵化槽数量，必须大于 0。</param>
    public void SetHatchSlotCount(int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (count < _hatchSlotCount)
        {
            Log.Warning(
                "PlayerRuntimeModule 无法在运行时将孵化槽位从 '{0}' 缩容至 '{1}'。",
                _hatchSlotCount,
                count);
            return;
        }

        if (count == _hatchSlotCount)
        {
            return;
        }

        _hatchSlotCount = count;

        // 孵化槽数量变化后，立即通知场地实体层刷新孵化器和蛋实体。
        // 这里不走餐桌/果园那套容量事件，因为孵化区 marker 本来就是固定 4 个，
        // 只需要让显示层按最新解锁数量重新显隐即可。
        GameEntry.PlayfieldEntities?.NotifyEggStateChanged();
    }

    /// <summary>
    /// 设置餐桌位数量。
    /// 当前实现只支持容量扩张，不支持运行时缩容，避免打乱已有宠物座位索引。
    /// </summary>
    /// <param name="count">餐桌位数量，必须大于 0。</param>
    public void SetDiningSeatCount(int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (count < _diningSeatCount)
        {
            Log.Warning(
                "PlayerRuntimeModule 无法在运行时将餐桌位从 '{0}' 缩容至 '{1}'。",
                _diningSeatCount,
                count);
            return;
        }

        if (count == _diningSeatCount)
        {
            return;
        }

        _diningSeatCount = count;

        bool promotedQueuedPets = false;
        if (GameEntry.PetPlacement != null)
        {
            GameEntry.PetPlacement.EnsureDiningSeatCapacity(_diningSeatCount);
            promotedQueuedPets = GameEntry.PetPlacement.PromoteQueuePetsIfPossible();
        }

        GameEntry.PlayfieldEntities?.EnsureCapacity(_diningSeatCount, _orchardSlotCount);

        if (promotedQueuedPets && GameEntry.PetPlacement != null)
        {
            GameEntry.PetPlacement.NotifyPlacementStateChanged();
        }

        NotifyPlayfieldCapacityChanged();
    }

    /// <summary>
    /// 设置果树位数量。
    /// 当前实现只支持容量扩张，不支持运行时缩容，避免打乱已有果树索引。
    /// </summary>
    /// <param name="count">果树位数量，必须大于 0。</param>
    public void SetOrchardSlotCount(int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (count < _orchardSlotCount)
        {
            Log.Warning(
                "PlayerRuntimeModule 无法在运行时将果树位从 '{0}' 缩容至 '{1}'。",
                _orchardSlotCount,
                count);
            return;
        }

        if (count == _orchardSlotCount)
        {
            return;
        }

        _orchardSlotCount = count;
        GameEntry.Orchards?.EnsureSlotCapacity(_orchardSlotCount);
        GameEntry.PlayfieldEntities?.EnsureCapacity(_diningSeatCount, _orchardSlotCount);
        NotifyPlayfieldCapacityChanged();
    }

    /// <summary>
    /// 增加金币。
    /// </summary>
    /// <param name="amount">增加的金币数量，必须大于 0。</param>
    public void AddGold(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        _currentGold += amount;
        GoldChanged?.Invoke(_currentGold);
    }

    public float GetHatchDurationScale(int slotIndex)
    {
        return GetArchitectureDurationScale(ArchitectureCategory.Hatch, slotIndex);
    }

    public int GetDietCoinBonus(int slotIndex)
    {
        return GetArchitectureEffectParam(ArchitectureCategory.Diet, slotIndex);
    }

    public float GetFruiterDurationScale(int slotIndex)
    {
        return GetArchitectureDurationScale(ArchitectureCategory.Fruiter, slotIndex);
    }

    /// <summary>
    /// 重置建筑运行时状态。
    /// 当前版本没有存档，因此每次运行都按默认建筑进度启动。
    /// </summary>
    private void ResetArchitectureRuntimeState()
    {
        _hatchSlotCount = FallbackInitialUnlockedSlotCount;
        _diningSeatCount = FallbackInitialUnlockedSlotCount;
        _orchardSlotCount = FallbackInitialUnlockedSlotCount;
        ResetArchitectureCategoryState(_hatchArchitectureStates, FallbackInitialUnlockedSlotCount);
        ResetArchitectureCategoryState(_dietArchitectureStates, FallbackInitialUnlockedSlotCount);
        ResetArchitectureCategoryState(_fruiterArchitectureStates, FallbackInitialUnlockedSlotCount);
    }

    /// <summary>
    /// 按配置表重置建筑运行时状态。
    /// 该过程只在初始化阶段调用一次，用表数据覆盖构造阶段的兜底默认值。
    /// </summary>
    private void ResetArchitectureRuntimeStateFromConfig()
    {
        int hatchUnlockedCount = CountInitiallyUnlockedSlots(ArchitectureCategory.Hatch);
        int dietUnlockedCount = CountInitiallyUnlockedSlots(ArchitectureCategory.Diet);
        int fruiterUnlockedCount = CountInitiallyUnlockedSlots(ArchitectureCategory.Fruiter);

        ResetArchitectureCategoryState(_hatchArchitectureStates, hatchUnlockedCount);
        ResetArchitectureCategoryState(_dietArchitectureStates, dietUnlockedCount);
        ResetArchitectureCategoryState(_fruiterArchitectureStates, fruiterUnlockedCount);

        SetHatchSlotCount(hatchUnlockedCount);
        SetDiningSeatCount(dietUnlockedCount);
        SetOrchardSlotCount(fruiterUnlockedCount);
    }

    /// <summary>
    /// 按“前 N 个解锁，其余锁定”的规则重置一个建筑类别的状态。
    /// </summary>
    /// <param name="slotStates">目标类别状态数组。</param>
    /// <param name="unlockedCount">默认解锁数量。</param>
    private static void ResetArchitectureCategoryState(ArchitectureSlotState[] slotStates, int unlockedCount)
    {
        if (slotStates == null)
        {
            return;
        }

        for (int i = 0; i < slotStates.Length; i++)
        {
            ArchitectureSlotState slotState = slotStates[i];
            if (slotState == null)
            {
                continue;
            }

            bool isUnlocked = i < unlockedCount;
            slotState.IsUnlocked = isUnlocked;
            slotState.Level = isUnlocked ? InitialArchitectureLevel : 0;
        }
    }

    /// <summary>
    /// 统计指定建筑类别的初始解锁数量。
    /// 配置表经过前置校验后，初始解锁区间一定是连续前缀。
    /// </summary>
    /// <param name="category">建筑类别。</param>
    /// <returns>初始解锁数量。</returns>
    private int CountInitiallyUnlockedSlots(ArchitectureCategory category)
    {
        if (!_architectureSlotRowsByCategory.TryGetValue(category, out Dictionary<int, ArchitectureSlotDataRow> rowsBySlotIndex)
            || rowsBySlotIndex == null
            || rowsBySlotIndex.Count == 0)
        {
            return FallbackInitialUnlockedSlotCount;
        }

        int unlockedCount = 0;
        for (int slotIndex = 1; rowsBySlotIndex.TryGetValue(slotIndex, out ArchitectureSlotDataRow row) && row != null; slotIndex++)
        {
            if (!row.IsInitiallyUnlocked)
            {
                break;
            }

            unlockedCount++;
        }

        return Mathf.Max(FallbackInitialUnlockedSlotCount, unlockedCount);
    }

    private float GetArchitectureDurationScale(ArchitectureCategory category, int slotIndex)
    {
        int effectParam = GetArchitectureEffectParam(category, slotIndex);
        return Mathf.Max(0.01f, 1f - (effectParam * 0.01f));
    }

    private int GetArchitectureEffectParam(ArchitectureCategory category, int slotIndex)
    {
        if (!EnsureInitialized())
        {
            return 0;
        }

        ArchitectureSlotState slotState = GetArchitectureSlotState(category, slotIndex);
        if (slotState == null || !slotState.IsUnlocked || slotState.Level <= InitialArchitectureLevel)
        {
            return 0;
        }

        int effectLevel = slotState.Level - 1;
        if (!_architectureUpgradeRowsByCategory.TryGetValue(category, out Dictionary<int, ArchitectureUpgradeDataRow> rowsByCurrentLevel)
            || rowsByCurrentLevel == null
            || !rowsByCurrentLevel.TryGetValue(effectLevel, out ArchitectureUpgradeDataRow row)
            || row == null)
        {
            return 0;
        }

        return Mathf.Max(0, row.EffectParam);
    }

    /// <summary>
    /// 获取指定建筑类别的最大等级。
    /// 最大等级由升级表的最后一个 CurrentLevel + 1 推导得出。
    /// </summary>
    /// <param name="category">建筑类别。</param>
    /// <returns>最大等级。</returns>
    private int GetMaxArchitectureLevel(ArchitectureCategory category)
    {
        return _maxArchitectureLevelsByCategory.TryGetValue(category, out int maxLevel)
            ? Mathf.Max(InitialArchitectureLevel, maxLevel)
            : InitialArchitectureLevel;
    }

    /// <summary>
    /// 根据建筑配置表重建槽位价格、升级价格与最大等级缓存。
    /// </summary>
    /// <param name="slotRows">建筑槽位表行集合。</param>
    /// <param name="upgradeRows">建筑升级表行集合。</param>
    /// <returns>是否重建成功。</returns>
    private bool RebuildArchitectureConfigCaches(
        ArchitectureSlotDataRow[] slotRows,
        ArchitectureUpgradeDataRow[] upgradeRows)
    {
        if (slotRows == null || slotRows.Length == 0 || upgradeRows == null || upgradeRows.Length == 0)
        {
            Log.Warning("PlayerRuntimeModule 无法重建建筑配置缓存，所需数据行为空。");
            return false;
        }

        _architectureSlotRowsByCategory.Clear();
        _architectureUpgradeRowsByCategory.Clear();
        _maxArchitectureLevelsByCategory.Clear();

        for (int i = 0; i < slotRows.Length; i++)
        {
            ArchitectureSlotDataRow row = slotRows[i];
            if (row == null)
            {
                continue;
            }

            if (!_architectureSlotRowsByCategory.TryGetValue(row.Category, out Dictionary<int, ArchitectureSlotDataRow> rowsBySlotIndex))
            {
                rowsBySlotIndex = new Dictionary<int, ArchitectureSlotDataRow>();
                _architectureSlotRowsByCategory.Add(row.Category, rowsBySlotIndex);
            }

            rowsBySlotIndex[row.SlotIndex] = row;
        }

        for (int i = 0; i < upgradeRows.Length; i++)
        {
            ArchitectureUpgradeDataRow row = upgradeRows[i];
            if (row == null)
            {
                continue;
            }

            if (!_architectureUpgradeRowsByCategory.TryGetValue(row.Category, out Dictionary<int, ArchitectureUpgradeDataRow> rowsByCurrentLevel))
            {
                rowsByCurrentLevel = new Dictionary<int, ArchitectureUpgradeDataRow>();
                _architectureUpgradeRowsByCategory.Add(row.Category, rowsByCurrentLevel);
            }

            rowsByCurrentLevel[row.CurrentLevel] = row;

            int maxLevel = row.CurrentLevel + 1;
            if (!_maxArchitectureLevelsByCategory.TryGetValue(row.Category, out int currentMaxLevel) || maxLevel > currentMaxLevel)
            {
                _maxArchitectureLevelsByCategory[row.Category] = maxLevel;
            }
        }

        return true;
    }

    /// <summary>
    /// 获取指定建筑类别的内部状态数组。
    /// </summary>
    /// <param name="category">建筑条目类型。</param>
    /// <returns>对应的状态数组；无效类型返回 null。</returns>
    private ArchitectureSlotState[] GetArchitectureStateArray(ArchitectureCategory category)
    {
        switch (category)
        {
            case ArchitectureCategory.Hatch:
                return _hatchArchitectureStates;

            case ArchitectureCategory.Diet:
                return _dietArchitectureStates;

            case ArchitectureCategory.Fruiter:
                return _fruiterArchitectureStates;

            default:
                return null;
        }
    }

    /// <summary>
    /// 获取单个建筑格子的内部状态。
    /// 这里的索引统一使用 1 基，以便直接对齐 UI 名称中的“X 号”。
    /// </summary>
    /// <param name="category">建筑条目类型。</param>
    /// <param name="slotIndex">1 基索引。</param>
    /// <returns>内部状态对象；索引非法时返回 null。</returns>
    private ArchitectureSlotState GetArchitectureSlotState(ArchitectureCategory category, int slotIndex)
    {
        ArchitectureSlotState[] slotStates = GetArchitectureStateArray(category);
        if (slotStates == null || slotIndex <= 0 || slotIndex > slotStates.Length)
        {
            return null;
        }

        return slotStates[slotIndex - 1];
    }

    /// <summary>
    /// 评估指定建筑条目当前应该展示的动作类型与价格。
    /// </summary>
    /// <param name="category">建筑条目类型。</param>
    /// <param name="slotIndex">1 基索引。</param>
    /// <param name="slotState">内部状态对象。</param>
    /// <param name="cost">返回当前动作需要消耗的金币。</param>
    /// <returns>当前动作类型。</returns>
    private ArchitectureActionType EvaluateArchitectureActionType(
        ArchitectureCategory category,
        int slotIndex,
        ArchitectureSlotState slotState,
        out int cost)
    {
        cost = 0;
        if (slotState == null)
        {
            return ArchitectureActionType.None;
        }

        if (!slotState.IsUnlocked)
        {
            cost = GetArchitectureUnlockCost(category, slotIndex);
            return cost > 0 ? ArchitectureActionType.Buy : ArchitectureActionType.None;
        }

        int maxLevel = GetMaxArchitectureLevel(category);
        if (slotState.Level >= maxLevel)
        {
            return ArchitectureActionType.Max;
        }

        cost = GetArchitectureUpgradeCost(category, slotState.Level);
        return cost > 0 ? ArchitectureActionType.Upgrade : ArchitectureActionType.None;
    }

    /// <summary>
    /// 判断当前锁定位是否允许购买。
    /// 这里只允许按顺序解锁“当前下一个未解锁位”。
    /// </summary>
    /// <param name="category">建筑条目类型。</param>
    /// <param name="slotIndex">1 基索引。</param>
    /// <returns>是否允许购买。</returns>
    private bool CanUnlockArchitectureSlot(ArchitectureCategory category, int slotIndex)
    {
        ArchitectureSlotState[] slotStates = GetArchitectureStateArray(category);
        if (slotStates == null || slotIndex <= 0 || slotIndex > slotStates.Length)
        {
            return false;
        }

        for (int i = 0; i < slotStates.Length; i++)
        {
            ArchitectureSlotState slotState = slotStates[i];
            if (slotState == null || slotState.IsUnlocked)
            {
                continue;
            }

            return i == slotIndex - 1;
        }

        return false;
    }

    /// <summary>
    /// 获取购买一个新格子需要消耗的金币。
    /// </summary>
    /// <param name="slotIndex">1 基索引。</param>
    /// <returns>金币消耗；非法索引返回 0。</returns>
    private int GetArchitectureUnlockCost(ArchitectureCategory category, int slotIndex)
    {
        if (slotIndex <= 0)
        {
            return 0;
        }

        if (!_architectureSlotRowsByCategory.TryGetValue(category, out Dictionary<int, ArchitectureSlotDataRow> rowsBySlotIndex)
            || rowsBySlotIndex == null
            || !rowsBySlotIndex.TryGetValue(slotIndex, out ArchitectureSlotDataRow row)
            || row == null)
        {
            return 0;
        }

        return row.UnlockGold;
    }

    /// <summary>
    /// 获取当前等级升到下一级时的金币消耗。
    /// </summary>
    /// <param name="currentLevel">当前等级。</param>
    /// <returns>升级消耗；已满级或等级非法时返回 0。</returns>
    private int GetArchitectureUpgradeCost(ArchitectureCategory category, int currentLevel)
    {
        if (currentLevel < InitialArchitectureLevel)
        {
            return 0;
        }

        if (!_architectureUpgradeRowsByCategory.TryGetValue(category, out Dictionary<int, ArchitectureUpgradeDataRow> rowsByCurrentLevel)
            || rowsByCurrentLevel == null
            || !rowsByCurrentLevel.TryGetValue(currentLevel, out ArchitectureUpgradeDataRow row)
            || row == null)
        {
            return 0;
        }

        return row.UpgradeGold;
    }

    /// <summary>
    /// 应用一次“购买新格子”带来的底层容量变化。
    /// 当前孵化区、饮食区和农场区都会在这里同步到底层容量模型。
    /// </summary>
    /// <param name="category">建筑条目类型。</param>
    /// <param name="slotIndex">1 基索引。</param>
    private void ApplyArchitectureUnlock(ArchitectureCategory category, int slotIndex)
    {
        switch (category)
        {
            case ArchitectureCategory.Hatch:
                SetHatchSlotCount(slotIndex);
                break;

            case ArchitectureCategory.Diet:
                SetDiningSeatCount(slotIndex);
                break;

            case ArchitectureCategory.Fruiter:
                SetOrchardSlotCount(slotIndex);
                break;
        }
    }

    /// <summary>
    /// 派发一次建筑状态变化通知。
    /// </summary>
    private void NotifyArchitectureStateChanged()
    {
        ArchitectureStateChanged?.Invoke();
    }

    /// <summary>
    /// 创建一个固定长度的建筑状态数组，并预先实例化每个格子的状态对象。
    /// 这样后续刷新时就不会再发生额外分配。
    /// </summary>
    /// <param name="count">格子数量。</param>
    /// <returns>完成初始化的状态数组。</returns>
    private static ArchitectureSlotState[] CreateArchitectureStateArray(int count)
    {
        ArchitectureSlotState[] slotStates = new ArchitectureSlotState[count];
        for (int i = 0; i < slotStates.Length; i++)
        {
            slotStates[i] = new ArchitectureSlotState();
        }

        return slotStates;
    }

    /// <summary>
    /// 派发一次场地区容量变化通知。
    /// 统一由这里向 UI 层广播，避免餐桌和果园各自维护一套刷新入口。
    /// </summary>
    private void NotifyPlayfieldCapacityChanged()
    {
        PlayfieldCapacityChanged?.Invoke(_diningSeatCount, _orchardSlotCount);
    }

    // ──────────────────────────────────────────────────────────
    //  产出物库存与抽取
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 预热宠物产出缓存。
    /// 该过程只在加载完成后执行一次，避免运行时重复重建。
    /// </summary>
    /// <returns>是否预热成功。</returns>
    public bool WarmupProduceCatalog()
    {
        if (_isProduceCatalogInitialized)
        {
            return true;
        }

        if (GameEntry.DataTables == null
            || !GameEntry.DataTables.IsAvailable<PetDataRow>()
            || !GameEntry.DataTables.IsAvailable<PetProduceDataRow>())
        {
            Log.Warning("PlayerRuntimeModule 无法预热产出目录，所需数据表不可用。");
            return false;
        }

        PetDataRow[] petRows = GameEntry.DataTables.GetAllDataRows<PetDataRow>();
        PetProduceDataRow[] produceRows = GameEntry.DataTables.GetAllDataRows<PetProduceDataRow>();
        if (petRows == null || petRows.Length == 0 || produceRows == null || produceRows.Length == 0)
        {
            Log.Warning("PlayerRuntimeModule 无法预热产出目录，宠物或产出表为空。");
            return false;
        }

        _petIdsByCode.Clear();
        _produceBucketsByPetId.Clear();
        _produceCountsByCode.Clear();

        for (int i = 0; i < petRows.Length; i++)
        {
            PetDataRow petRow = petRows[i];
            if (petRow == null || string.IsNullOrWhiteSpace(petRow.Code))
            {
                continue;
            }

            _petIdsByCode[petRow.Code] = petRow.Id;
        }

        for (int i = 0; i < produceRows.Length; i++)
        {
            PetProduceDataRow produceRow = produceRows[i];
            if (produceRow == null || string.IsNullOrWhiteSpace(produceRow.Code))
            {
                continue;
            }

            if (!_produceBucketsByPetId.TryGetValue(produceRow.PetId, out PetProduceBucket produceBucket))
            {
                produceBucket = new PetProduceBucket();
                _produceBucketsByPetId.Add(produceRow.PetId, produceBucket);
            }

            switch (produceRow.Grade)
            {
                case ProduceGradeType.Primary:
                    produceBucket.Primary = produceRow;
                    break;

                case ProduceGradeType.Intermediate:
                    produceBucket.Intermediate = produceRow;
                    break;

                case ProduceGradeType.Advanced:
                    produceBucket.Advanced = produceRow;
                    break;
            }

            _produceCountsByCode[produceRow.Code] = 0;
        }

        _isProduceCatalogInitialized = true;
        return true;
    }

    /// <summary>
    /// 为指定产出物增加库存数量。
    /// </summary>
    /// <param name="produceCode">产出物 Code。</param>
    /// <returns>是否成功入库。</returns>
    public bool AddProduce(string produceCode)
    {
        if (!EnsureProduceCatalogInitialized() || string.IsNullOrWhiteSpace(produceCode))
        {
            return false;
        }

        if (!_produceCountsByCode.TryGetValue(produceCode, out int currentCount))
        {
            Log.Warning("PlayerRuntimeModule 无法添加产出物，编码 '{0}' 无效。", produceCode);
            return false;
        }

        currentCount++;
        _produceCountsByCode[produceCode] = currentCount;
        ProduceChanged?.Invoke(produceCode, currentCount);
        return true;
    }

    /// <summary>
    /// 获取指定产出物在当前会话内的库存数量。
    /// </summary>
    /// <param name="produceCode">产出物 Code。</param>
    /// <returns>当前库存数量；无效 Code 返回 0。</returns>
    public int GetProduceCount(string produceCode)
    {
        if (!EnsureProduceCatalogInitialized() || string.IsNullOrWhiteSpace(produceCode))
        {
            return 0;
        }

        return _produceCountsByCode.TryGetValue(produceCode, out int count) ? count : 0;
    }

    /// <summary>
    /// 根据宠物 Code 抽取本次产出物。
    /// </summary>
    /// <param name="petCode">宠物 Code。</param>
    /// <param name="produceDataRow">命中的产出物配置行。</param>
    /// <returns>是否抽取成功。</returns>
    public bool TryRollPetProduce(string petCode, out PetProduceDataRow produceDataRow)
    {
        produceDataRow = null;
        if (!EnsureInitialized() || !EnsureProduceCatalogInitialized() || string.IsNullOrWhiteSpace(petCode))
        {
            return false;
        }

        if (!_petIdsByCode.TryGetValue(petCode, out int petId))
        {
            Log.Warning("PlayerRuntimeModule 无法抽取产出物，宠物编码 '{0}' 无效。", petCode);
            return false;
        }

        if (!_produceBucketsByPetId.TryGetValue(petId, out PetProduceBucket produceBucket) || produceBucket == null)
        {
            Log.Warning("PlayerRuntimeModule 无法抽取产出物，宠物 Id '{0}' 无产出桶。", petId);
            return false;
        }

        produceDataRow = RollProduceByGrade(produceBucket);
        if (produceDataRow == null)
        {
            Log.Warning("PlayerRuntimeModule 无法抽取产出物，宠物 Id '{0}' 的产出桶不完整。", petId);
            return false;
        }

        return true;
    }

    // ──────────────────────────────────────────────────────────
    //  水果解锁与抽取
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 确保水果运行时状态已根据数据表完成初始化。
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
            || !GameEntry.DataTables.IsAvailable<ArchitectureUpgradeDataRow>())
        {
            Log.Warning("PlayerRuntimeModule 无法初始化，所需数据表不可用。");
            return false;
        }

        FruitDataRow[] fruitRows = GameEntry.DataTables.GetAllDataRows<FruitDataRow>();
        ArchitectureSlotDataRow[] architectureSlotRows = GameEntry.DataTables.GetAllDataRows<ArchitectureSlotDataRow>();
        ArchitectureUpgradeDataRow[] architectureUpgradeRows = GameEntry.DataTables.GetAllDataRows<ArchitectureUpgradeDataRow>();
        GameplayRuleDataRow gameplayRuleDataRow = GameEntry.DataTables.GetDataRowByCode<GameplayRuleDataRow>(GameplayRuleDataRow.DefaultCode);
        if (fruitRows == null
            || fruitRows.Length == 0
            || architectureSlotRows == null
            || architectureSlotRows.Length == 0
            || architectureUpgradeRows == null
            || architectureUpgradeRows.Length == 0
            || gameplayRuleDataRow == null)
        {
            Log.Warning("PlayerRuntimeModule 无法初始化，所需数据行为空。");
            return false;
        }

        if (!RebuildArchitectureConfigCaches(architectureSlotRows, architectureUpgradeRows))
        {
            Log.Warning("PlayerRuntimeModule 无法初始化，建筑配置缓存重建失败。");
            return false;
        }

        _gameplayRuleDataRow = gameplayRuleDataRow;
        _allFruitRows = fruitRows;
        _unlockedFruitCandidates = new FruitDataRow[fruitRows.Length];
        _lockedFruitCandidates = new FruitDataRow[fruitRows.Length];
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
                _unlockedFruitCodes.Add(fruitRow.Code);
            }
        }

        ResetArchitectureRuntimeStateFromConfig();
        _isInitialized = true;
        _isCandidateCacheDirty = true;
        RebuildCandidateCachesIfNeeded();
        return true;
    }

    /// <summary>
    /// 判断指定水果在当前会话内是否已解锁。
    /// </summary>
    /// <param name="fruitCode">水果 Code。</param>
    /// <returns>是否已解锁。</returns>
    public bool IsFruitUnlocked(string fruitCode)
    {
        if (!EnsureInitialized() || string.IsNullOrWhiteSpace(fruitCode))
        {
            return false;
        }

        return _unlockedFruitCodes.Contains(fruitCode);
    }

    /// <summary>
    /// 在当前会话内解锁指定水果。
    /// </summary>
    /// <param name="fruitCode">水果 Code。</param>
    /// <returns>是否成功解锁。</returns>
    public bool TryUnlockFruit(string fruitCode)
    {
        if (!EnsureInitialized() || string.IsNullOrWhiteSpace(fruitCode))
        {
            return false;
        }

        FruitDataRow fruitRow = GameEntry.DataTables.GetDataRowByCode<FruitDataRow>(fruitCode);
        if (fruitRow == null)
        {
            Log.Warning("PlayerRuntimeModule can not unlock fruit because code '{0}' is invalid.", fruitCode);
            return false;
        }

        if (!_unlockedFruitCodes.Add(fruitCode))
        {
            return true;
        }

        _isCandidateCacheDirty = true;
        return true;
    }

    /// <summary>
    /// 判断指定宠物在当前会话内是否已解锁。
    /// </summary>
    /// <param name="petCode">宠物 Code。</param>
    /// <returns>是否已解锁。</returns>
    public bool IsPetUnlocked(string petCode)
    {
        if (!EnsureInitialized() || string.IsNullOrWhiteSpace(petCode))
        {
            return false;
        }

        return _unlockedPetCodes.Contains(petCode);
    }

    /// <summary>
    /// 在当前会话内解锁指定宠物。
    /// </summary>
    /// <param name="petCode">宠物 Code。</param>
    /// <returns>是否成功解锁。</returns>
    public bool TryUnlockPet(string petCode)
    {
        if (!EnsureInitialized() || string.IsNullOrWhiteSpace(petCode))
        {
            return false;
        }

        PetDataRow petRow = GameEntry.DataTables.GetDataRowByCode<PetDataRow>(petCode);
        if (petRow == null)
        {
            Log.Warning("PlayerRuntimeModule can not unlock pet because code '{0}' is invalid.", petCode);
            return false;
        }

        if (!_unlockedPetCodes.Add(petCode))
        {
            return true;
        }

        return true;
    }

    /// <summary>
    /// 原子购买接口：校验数据行存在 → 校验未解锁 → 校验金币充足 → 扣金币 → 解锁水果。
    /// UI 层只需调用此单一接口即可完成完整购买事务，无需自行拆分扣款与解锁。
    /// </summary>
    /// <param name="fruitCode">水果 Code。</param>
    /// <returns>是否购买成功。</returns>
    public bool TryPurchaseFruit(string fruitCode)
    {
        if (!EnsureInitialized() || string.IsNullOrWhiteSpace(fruitCode))
        {
            return false;
        }

        FruitDataRow fruitRow = GameEntry.DataTables.GetDataRowByCode<FruitDataRow>(fruitCode);
        if (fruitRow == null)
        {
            Log.Warning("PlayerRuntimeModule 无法购买水果，编码 '{0}' 无效。", fruitCode);
            return false;
        }

        // 已默认解锁或已运行时解锁的水果不允许重复购买
        if (fruitRow.IsUnlocked || IsFruitUnlocked(fruitCode))
        {
            return false;
        }

        // 解锁金币必须大于 0（数据表校验保证了这一点，此处做防御性检查）
        if (fruitRow.UnlockGold <= 0)
        {
            return false;
        }

        // 扣金币失败说明余额不足
        if (!TryConsumeGold(fruitRow.UnlockGold))
        {
            return false;
        }

        // 扣款成功，执行解锁
        return TryUnlockFruit(fruitCode);
    }

    /// <summary>
    /// 为入座宠物抽取本次期望水果。
    /// </summary>
    /// <param name="fruitDataRow">命中的水果配置行。</param>
    /// <returns>是否抽取成功。</returns>
    public bool TryRollDiningWishFruit(out FruitDataRow fruitDataRow)
    {
        fruitDataRow = null;
        if (!EnsureInitialized() || _gameplayRuleDataRow == null)
        {
            return false;
        }

        RebuildCandidateCachesIfNeeded();

        bool preferUnlocked = UnityEngine.Random.Range(0, FullProbability) < _gameplayRuleDataRow.PreferUnlockedFruitProbability;
        if (TryPickFruitFromBucket(preferUnlocked, out fruitDataRow))
        {
            return true;
        }

        if (TryPickFruitFromBucket(!preferUnlocked, out fruitDataRow))
        {
            return true;
        }

        Log.Warning("PlayerRuntimeModule can not roll dining wish fruit because both candidate buckets are empty.");
        return false;
    }

    /// <summary>
    /// 按指定桶类型挑选一个水果。
    /// </summary>
    /// <param name="pickUnlockedBucket">是否挑选已解锁桶。</param>
    /// <param name="fruitDataRow">命中的水果配置行。</param>
    /// <returns>是否成功命中。</returns>
    private bool TryPickFruitFromBucket(bool pickUnlockedBucket, out FruitDataRow fruitDataRow)
    {
        fruitDataRow = null;

        FruitDataRow[] candidates = pickUnlockedBucket ? _unlockedFruitCandidates : _lockedFruitCandidates;
        int candidateCount = pickUnlockedBucket ? _unlockedFruitCandidateCount : _lockedFruitCandidateCount;
        if (candidates == null || candidateCount <= 0)
        {
            return false;
        }

        int randomIndex = UnityEngine.Random.Range(0, candidateCount);
        fruitDataRow = candidates[randomIndex];
        return fruitDataRow != null;
    }

    /// <summary>
    /// 确保产出物缓存已经初始化。
    /// </summary>
    /// <returns>是否可用。</returns>
    private bool EnsureProduceCatalogInitialized()
    {
        return _isProduceCatalogInitialized || WarmupProduceCatalog();
    }

    /// <summary>
    /// 按三档固定概率从缓存桶中抽取一个产出物。
    /// </summary>
    /// <param name="produceBucket">当前宠物的产出桶。</param>
    /// <returns>命中的产出物配置；若桶不完整则返回 null。</returns>
    private PetProduceDataRow RollProduceByGrade(PetProduceBucket produceBucket)
    {
        if (produceBucket == null || _gameplayRuleDataRow == null)
        {
            return null;
        }

        int roll = UnityEngine.Random.Range(0, FullProbability);
        if (roll < _gameplayRuleDataRow.PrimaryProduceProbability)
        {
            return produceBucket.Primary;
        }

        if (roll < _gameplayRuleDataRow.PrimaryProduceProbability + _gameplayRuleDataRow.IntermediateProduceProbability)
        {
            return produceBucket.Intermediate;
        }

        return produceBucket.Advanced;
    }

    /// <summary>
    /// 重建已解锁桶与未解锁桶缓存。
    /// </summary>
    private void RebuildCandidateCachesIfNeeded()
    {
        if (!_isCandidateCacheDirty)
        {
            return;
        }

        _unlockedFruitCandidateCount = 0;
        _lockedFruitCandidateCount = 0;

        for (int i = 0; i < _allFruitRows.Length; i++)
        {
            FruitDataRow fruitRow = _allFruitRows[i];
            if (fruitRow == null || string.IsNullOrWhiteSpace(fruitRow.Code))
            {
                continue;
            }

            if (_unlockedFruitCodes.Contains(fruitRow.Code))
            {
                _unlockedFruitCandidates[_unlockedFruitCandidateCount] = fruitRow;
                _unlockedFruitCandidateCount++;
                continue;
            }

            _lockedFruitCandidates[_lockedFruitCandidateCount] = fruitRow;
            _lockedFruitCandidateCount++;
        }

        _isCandidateCacheDirty = false;
    }
}
