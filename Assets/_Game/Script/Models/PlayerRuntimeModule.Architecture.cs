using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 玩家运行时模块 — 建筑/金币/容量管理部分。
/// 负责建筑槽位解锁/升级、金币增减、场地区容量扩展。
/// </summary>
public sealed partial class PlayerRuntimeModule
{
    // ───────────── 建筑常量 ─────────────

    /// <summary>
    /// 建筑等级从 1 开始。
    /// 未解锁条目固定视为 0 级。
    /// </summary>
    private const int InitialArchitectureLevel = 1;

    /// <summary>
    /// 建筑最大等级。
    /// </summary>
    private const int FallbackInitialUnlockedSlotCount = 1;

    // ───────────── 建筑字段 ─────────────

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
    /// 建筑图片配置缓存。
    /// Key1 为建筑类别，Key2 为等级（0=未解锁，1~10=各升级等级）。
    /// </summary>
    private readonly Dictionary<ArchitectureCategory, Dictionary<int, ArchitectureDataRow>> _architectureRowsByCategory =
        new Dictionary<ArchitectureCategory, Dictionary<int, ArchitectureDataRow>>();

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

    // ───────────── 建筑事件 ─────────────

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
    /// 场地区容量发生变化时触发。
    /// 参数一为最新餐桌位数量，参数二为最新果园位数量。
    /// UI 层收到后需要重采样 marker，并把新快照同步给全局实体模块。
    /// </summary>
    public event Action<int, int> PlayfieldCapacityChanged;

    // ───────────── 建筑属性 ─────────────

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
    /// 餐桌位总数量（包含未解锁）。
    /// 等于饮食区建筑条目总数 DietArchitectureCountValue。
    /// 用于实体系统和 UI 层为所有槽位创建占位。
    /// </summary>
    public int TotalDiningSeatCount => DietArchitectureCountValue;

    /// <summary>
    /// 果树位总数量（包含未解锁）。
    /// 等于农场区建筑条目总数 FruiterArchitectureCountValue。
    /// 用于实体系统和 UI 层为所有槽位创建占位。
    /// </summary>
    public int TotalOrchardSlotCount => FruiterArchitectureCountValue;

    /// <summary>
    /// 当前持有的金币总额。
    /// </summary>
    public int CurrentGold => _currentGold;

    // ───────────── 建筑/金币公共接口 ─────────────

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
                NotifyArchitectureStateChanged(category, slotIndex);
                return true;

            case ArchitectureActionType.Upgrade:
                if (!TryConsumeGold(cost))
                {
                    return false;
                }

                slotState.Level = Mathf.Clamp(slotState.Level + 1, InitialArchitectureLevel, GetMaxArchitectureLevel(category));
                NotifyArchitectureStateChanged(category, slotIndex);
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

    /// <summary>
    /// 获取孵化区指定槽位的孵化时长缩放因子。
    /// </summary>
    /// <param name="slotIndex">1 基索引。</param>
    /// <returns>时长缩放因子（0.01~1.0）。</returns>
    public float GetHatchDurationScale(int slotIndex)
    {
        return GetArchitectureDurationScale(ArchitectureCategory.Hatch, slotIndex);
    }

    /// <summary>
    /// 获取饮食区指定槽位的金币加成。
    /// </summary>
    /// <param name="slotIndex">1 基索引。</param>
    /// <returns>金币加成参数。</returns>
    public int GetDietCoinBonus(int slotIndex)
    {
        return GetArchitectureEffectParam(ArchitectureCategory.Diet, slotIndex);
    }

    /// <summary>
    /// 获取农场区指定槽位的时长缩放因子。
    /// </summary>
    /// <param name="slotIndex">1 基索引。</param>
    /// <returns>时长缩放因子（0.01~1.0）。</returns>
    public float GetFruiterDurationScale(int slotIndex)
    {
        return GetArchitectureDurationScale(ArchitectureCategory.Fruiter, slotIndex);
    }

    // ───────────── 建筑初始化（由 EnsureInitialized 调用） ─────────────

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
    /// 根据建筑配置表重建槽位价格、升级价格与最大等级缓存。
    /// </summary>
    /// <param name="slotRows">建筑槽位表行集合。</param>
    /// <param name="upgradeRows">建筑升级表行集合。</param>
    /// <returns>是否重建成功。</returns>
    private bool RebuildArchitectureConfigCaches(
        ArchitectureSlotDataRow[] slotRows,
        ArchitectureUpgradeDataRow[] upgradeRows,
        ArchitectureDataRow[] architectureRows)
    {
        if (slotRows == null || slotRows.Length == 0 || upgradeRows == null || upgradeRows.Length == 0)
        {
            Log.Warning("PlayerRuntimeModule 无法重建建筑配置缓存，所需数据行为空。");
            return false;
        }

        _architectureSlotRowsByCategory.Clear();
        _architectureUpgradeRowsByCategory.Clear();
        _maxArchitectureLevelsByCategory.Clear();
        _architectureRowsByCategory.Clear();

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

        // 重建建筑图片配置缓存（允许为空，美术资源尚未配置时不阻塞初始化）。
        if (architectureRows != null)
        {
            for (int i = 0; i < architectureRows.Length; i++)
            {
                ArchitectureDataRow row = architectureRows[i];
                if (row == null)
                {
                    continue;
                }

                if (!_architectureRowsByCategory.TryGetValue(row.Category, out Dictionary<int, ArchitectureDataRow> rowsByLevel))
                {
                    rowsByLevel = new Dictionary<int, ArchitectureDataRow>();
                    _architectureRowsByCategory.Add(row.Category, rowsByLevel);
                }

                rowsByLevel[row.Level] = row;
            }
        }

        return true;
    }

    // ───────────── 建筑内部方法 ─────────────

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
    /// 这里的索引统一使用 1 基，以便直接对齐 UI 名称中的"X 号"。
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
    /// 这里只允许按顺序解锁"当前下一个未解锁位"。
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
    /// 应用一次"购买新格子"带来的底层容量变化。
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
    private void NotifyArchitectureStateChanged(ArchitectureCategory category, int slotIndex)
    {
        ArchitectureStateChanged?.Invoke();
        // 建筑购买/升级后，通知实体层刷新对应槽位的精灵。
        GameEntry.PlayfieldEntities?.NotifyArchitectureSlotChanged(category, slotIndex);
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
    /// 按"前 N 个解锁，其余锁定"的规则重置一个建筑类别的状态。
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
    /// 派发一次场地区容量变化通知。
    /// 统一由这里向 UI 层广播，避免餐桌和果园各自维护一套刷新入口。
    /// </summary>
    private void NotifyPlayfieldCapacityChanged()
    {
        PlayfieldCapacityChanged?.Invoke(_diningSeatCount, _orchardSlotCount);
    }

    // ───────────── 建筑图片公共查询 ─────────────

    /// <summary>
    /// 获取指定建筑类别和等级对应的升级界面指示器精灵路径。
    /// </summary>
    /// <param name="category">建筑类别。</param>
    /// <param name="level">建筑等级（0=未解锁，1~10=各升级等级）。</param>
    /// <returns>精灵路径；未找到时返回空字符串。</returns>
    public string GetIndicatorSpritePath(ArchitectureCategory category, int level)
    {
        if (_architectureRowsByCategory.TryGetValue(category, out Dictionary<int, ArchitectureDataRow> rowsByLevel)
            && rowsByLevel != null
            && rowsByLevel.TryGetValue(level, out ArchitectureDataRow row)
            && row != null)
        {
            return row.IndicatorSpritePath;
        }

        return string.Empty;
    }

    /// <summary>
    /// 获取指定建筑类别和等级对应的主界面实体占位精灵路径。
    /// </summary>
    /// <param name="category">建筑类别。</param>
    /// <param name="level">建筑等级（0=未解锁，1~10=各升级等级）。</param>
    /// <returns>精灵路径；未找到时返回空字符串。</returns>
    public string GetEntitySpritePath(ArchitectureCategory category, int level)
    {
        if (_architectureRowsByCategory.TryGetValue(category, out Dictionary<int, ArchitectureDataRow> rowsByLevel)
            && rowsByLevel != null
            && rowsByLevel.TryGetValue(level, out ArchitectureDataRow row)
            && row != null)
        {
            return row.EntitySpritePath;
        }

        return string.Empty;
    }
}
