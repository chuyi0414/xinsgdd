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
    /// 已解锁水果桶的命中概率。
    /// </summary>
    private const int PreferUnlockedProbability = 80;

    /// <summary>
    /// 初级产出物命中概率。
    /// </summary>
    private const int PrimaryProduceProbability = 82;

    /// <summary>
    /// 中级产出物命中概率。
    /// </summary>
    private const int IntermediateProduceProbability = 15;

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
    /// 当前会话内已解锁的水果 Code 集合。
    /// </summary>
    private readonly HashSet<string> _unlockedFruitCodes = new HashSet<string>(StringComparer.Ordinal);

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
    //  金币管理
    // ──────────────────────────────────────────────────────────

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
                "PlayerRuntimeModule can not shrink dining seat count from '{0}' to '{1}' at runtime.",
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
                "PlayerRuntimeModule can not shrink orchard slot count from '{0}' to '{1}' at runtime.",
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
            Log.Warning("PlayerRuntimeModule can not warmup produce catalog because required data tables are unavailable.");
            return false;
        }

        PetDataRow[] petRows = GameEntry.DataTables.GetAllDataRows<PetDataRow>();
        PetProduceDataRow[] produceRows = GameEntry.DataTables.GetAllDataRows<PetProduceDataRow>();
        if (petRows == null || petRows.Length == 0 || produceRows == null || produceRows.Length == 0)
        {
            Log.Warning("PlayerRuntimeModule can not warmup produce catalog because pet or produce table is empty.");
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
            Log.Warning("PlayerRuntimeModule can not add produce because code '{0}' is invalid.", produceCode);
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
        if (!EnsureProduceCatalogInitialized() || string.IsNullOrWhiteSpace(petCode))
        {
            return false;
        }

        if (!_petIdsByCode.TryGetValue(petCode, out int petId))
        {
            Log.Warning("PlayerRuntimeModule can not roll produce because pet code '{0}' is invalid.", petCode);
            return false;
        }

        if (!_produceBucketsByPetId.TryGetValue(petId, out PetProduceBucket produceBucket) || produceBucket == null)
        {
            Log.Warning("PlayerRuntimeModule can not roll produce because pet id '{0}' has no produce bucket.", petId);
            return false;
        }

        produceDataRow = RollProduceByGrade(produceBucket);
        if (produceDataRow == null)
        {
            Log.Warning("PlayerRuntimeModule can not roll produce because pet id '{0}' has an incomplete produce bucket.", petId);
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

        if (GameEntry.DataTables == null || !GameEntry.DataTables.IsAvailable<FruitDataRow>())
        {
            Log.Warning("PlayerRuntimeModule can not initialize because FruitDataRow table is unavailable.");
            return false;
        }

        FruitDataRow[] fruitRows = GameEntry.DataTables.GetAllDataRows<FruitDataRow>();
        if (fruitRows == null || fruitRows.Length == 0)
        {
            Log.Warning("PlayerRuntimeModule can not initialize because FruitDataRow table is empty.");
            return false;
        }

        _allFruitRows = fruitRows;
        _unlockedFruitCandidates = new FruitDataRow[fruitRows.Length];
        _lockedFruitCandidates = new FruitDataRow[fruitRows.Length];
        _unlockedFruitCodes.Clear();

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
    /// 为入座宠物抽取本次期望水果。
    /// </summary>
    /// <param name="fruitDataRow">命中的水果配置行。</param>
    /// <returns>是否抽取成功。</returns>
    public bool TryRollDiningWishFruit(out FruitDataRow fruitDataRow)
    {
        fruitDataRow = null;
        if (!EnsureInitialized())
        {
            return false;
        }

        RebuildCandidateCachesIfNeeded();

        bool preferUnlocked = UnityEngine.Random.Range(0, 100) < PreferUnlockedProbability;
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
    private static PetProduceDataRow RollProduceByGrade(PetProduceBucket produceBucket)
    {
        if (produceBucket == null)
        {
            return null;
        }

        int roll = UnityEngine.Random.Range(0, FullProbability);
        if (roll < PrimaryProduceProbability)
        {
            return produceBucket.Primary;
        }

        if (roll < PrimaryProduceProbability + IntermediateProduceProbability)
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
