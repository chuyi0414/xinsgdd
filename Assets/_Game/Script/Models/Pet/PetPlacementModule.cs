using System;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 宠物站位运行时模块。
/// 负责孵化结果抽取以及饮食区、排队区的占位管理。
/// </summary>
public sealed class PetPlacementModule
{
    /// <summary>
    /// 餐桌位数量默认值。
    /// 实际运行时数量由 Initialize 方法从 PlayerRuntimeModule 读取。
    /// </summary>
    public const int DefaultDiningSeatCount = 1;

    /// <summary>
    /// 排队位数量常量。
    /// </summary>
    public const int QueueSlotCountValue = 14;

    /// <summary>
    /// 每个餐桌位当前占用的宠物实例 Id。
    /// 延迟到 Initialize 调用后才真正分配，避免硬编码数组大小。
    /// </summary>
    private int[] _diningSeatInstanceIds = Array.Empty<int>();

    /// <summary>
    /// 每个排队位当前占用的宠物实例 Id。
    /// </summary>
    private readonly int[] _queueInstanceIds = new int[QueueSlotCountValue];

    /// <summary>
    /// 当前所有在场宠物的运行时状态。
    /// </summary>
    private readonly Dictionary<int, PetRuntimeState> _petStates = new Dictionary<int, PetRuntimeState>();

    /// <summary>
    /// 下一个可分配的宠物实例 Id。
    /// </summary>
    private int _nextInstanceId = 1;

    /// <summary>
    /// 全局玩法规则缓存。
    /// </summary>
    private GameplayRuleDataRow _gameplayRuleDataRow;

    /// <summary>
    /// 按品质预热好的宠物候选缓存。
    /// 运行时孵化时直接命中对应数组，避免临时 List 分配。
    /// </summary>
    private readonly Dictionary<QualityType, PetDataRow[]> _petCandidatesByQuality =
        new Dictionary<QualityType, PetDataRow[]>();

    /// <summary>
    /// 宠物站位变化事件。
    /// UI 层可通过它延迟重建一次展示缓存，避免在每帧轮询宠物列表。
    /// </summary>
    public event Action PlacementChanged;

    /// <summary>
    /// 根据运行时数据初始化餐桌位数量。
    /// 初始化和后续升级都走同一套扩容逻辑，避免重复维护两份数组分配代码。
    /// </summary>
    /// <param name="seatCount">餐桌位数量，必须大于 0。</param>
    public void Initialize(int seatCount)
    {
        EnsureDiningSeatCapacity(seatCount);
    }

    /// <summary>
    /// 预热按品质分组的宠物候选缓存。
    /// 该过程在数据表注册阶段执行一次，后续孵化逻辑只读缓存数组。
    /// </summary>
    /// <returns>是否预热成功。</returns>
    public bool WarmupPetSelectionCatalog()
    {
        if (GameEntry.DataTables == null || !GameEntry.DataTables.IsAvailable<PetDataRow>())
        {
            Log.Warning("PetPlacementModule can not warmup pet selection catalog because PetDataTable is unavailable.");
            return false;
        }

        PetDataRow[] petRows = GameEntry.DataTables.GetAllDataRows<PetDataRow>();
        if (petRows == null || petRows.Length == 0)
        {
            Log.Warning("PetPlacementModule can not warmup pet selection catalog because PetDataTable is empty.");
            return false;
        }

        Dictionary<QualityType, List<PetDataRow>> candidateListsByQuality = new Dictionary<QualityType, List<PetDataRow>>();
        _petCandidatesByQuality.Clear();

        for (int i = 0; i < petRows.Length; i++)
        {
            PetDataRow petRow = petRows[i];
            if (petRow == null || string.IsNullOrWhiteSpace(petRow.Code))
            {
                continue;
            }

            if (!candidateListsByQuality.TryGetValue(petRow.Quality, out List<PetDataRow> candidates))
            {
                candidates = new List<PetDataRow>();
                candidateListsByQuality.Add(petRow.Quality, candidates);
            }

            candidates.Add(petRow);
        }

        foreach (KeyValuePair<QualityType, List<PetDataRow>> pair in candidateListsByQuality)
        {
            if (pair.Value == null || pair.Value.Count == 0)
            {
                continue;
            }

            _petCandidatesByQuality[pair.Key] = pair.Value.ToArray();
        }

        return _petCandidatesByQuality.Count > 0;
    }

    /// <summary>
    /// 预热玩法规则缓存。
    /// 规则表注册完成后调用一次，避免运行时重复查表。
    /// </summary>
    /// <returns>是否命中有效规则。</returns>
    public bool WarmupGameplayRuleCache()
    {
        if (GameEntry.DataTables == null || !GameEntry.DataTables.IsAvailable<GameplayRuleDataRow>())
        {
            _gameplayRuleDataRow = null;
            return false;
        }

        _gameplayRuleDataRow = GameEntry.DataTables.GetDataRowByCode<GameplayRuleDataRow>(GameplayRuleDataRow.DefaultCode);
        return _gameplayRuleDataRow != null;
    }

    /// <summary>
    /// 确保餐桌位容量至少达到指定数量。
    /// 这里只允许扩容，不允许缩容，避免打乱当前宠物的座位索引。
    /// </summary>
    /// <param name="seatCount">目标餐桌位数量。</param>
    /// <returns>本次是否实际发生了扩容。</returns>
    public bool EnsureDiningSeatCapacity(int seatCount)
    {
        if (seatCount <= 0)
        {
            seatCount = DefaultDiningSeatCount;
        }

        if (seatCount <= _diningSeatInstanceIds.Length)
        {
            return false;
        }

        int[] expandedSeatInstanceIds = new int[seatCount];
        if (_diningSeatInstanceIds.Length > 0)
        {
            Array.Copy(_diningSeatInstanceIds, expandedSeatInstanceIds, _diningSeatInstanceIds.Length);
        }

        _diningSeatInstanceIds = expandedSeatInstanceIds;
        return true;
    }

    /// <summary>
    /// 饮食区座位数量。
    /// </summary>
    public int DiningSeatCount => _diningSeatInstanceIds.Length;

    /// <summary>
    /// 排队区位置数量。
    /// </summary>
    public int QueueSlotCount => QueueSlotCountValue;

    /// <summary>
    /// 获取当前所有已入场宠物。
    /// </summary>
    public PetRuntimeState[] GetAllPets()
    {
        if (_petStates.Count == 0)
        {
            return Array.Empty<PetRuntimeState>();
        }

        List<PetRuntimeState> petStates = new List<PetRuntimeState>(_petStates.Values);
        petStates.Sort(ComparePetStates);
        return petStates.ToArray();
    }

    /// <summary>
    /// 将当前所有宠物状态写入调用方提供的缓冲列表。
    /// 不做排序，供高频运行时模块无 GC 遍历使用。
    /// </summary>
    /// <param name="results">外部复用的缓冲列表。</param>
    public void GetAllPetsNonAlloc(List<PetRuntimeState> results)
    {
        if (results == null)
        {
            return;
        }

        results.Clear();
        foreach (KeyValuePair<int, PetRuntimeState> pair in _petStates)
        {
            if (pair.Value != null)
            {
                results.Add(pair.Value);
            }
        }
    }

    /// <summary>
    /// 按实例 Id 获取单只宠物的运行时状态。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    /// <returns>命中的宠物运行时状态；若不存在则返回 null。</returns>
    public PetRuntimeState GetPetStateByInstanceId(int petInstanceId)
    {
        if (petInstanceId <= 0)
        {
            return null;
        }

        _petStates.TryGetValue(petInstanceId, out PetRuntimeState petState);
        return petState;
    }

    /// <summary>
    /// 根据蛋配置抽取并放置一只宠物。
    /// </summary>
    public bool TryHatchPetFromEggCode(string eggCode, int hatchSlotIndex, out PetRuntimeState petState)
    {
        petState = null;
        if (string.IsNullOrWhiteSpace(eggCode))
        {
            Log.Warning("PetPlacementModule can not hatch pet because egg code is empty.");
            return false;
        }

        if (GameEntry.DataTables == null
            || !GameEntry.DataTables.IsAvailable<EggDataRow>()
            || !GameEntry.DataTables.IsAvailable<PetDataRow>())
        {
            Log.Warning("PetPlacementModule can not hatch pet because required data tables are unavailable.");
            return false;
        }

        EggDataRow eggDataRow = GameEntry.DataTables.GetDataRowByCode<EggDataRow>(eggCode);
        if (eggDataRow == null)
        {
            Log.Warning("PetPlacementModule can not hatch pet because egg '{0}' can not be found.", eggCode);
            return false;
        }

        if (!TryRollPetQuality(eggDataRow, out QualityType petQuality))
        {
            Log.Warning("PetPlacementModule can not hatch pet because egg '{0}' failed to roll quality.", eggCode);
            return false;
        }

        if (!TryPickPetCodeByQuality(petQuality, out string petCode))
        {
            return false;
        }

        if (!TryPlacePet(petCode, petQuality, hatchSlotIndex, out petState))
        {
            return false;
        }

        // 宠物孵化成功后，立即把当前宠物编码写入玩家运行时图鉴缓存。
        // 这样宠物图鉴界面下次打开时，就能直接把该宠物显示为已解锁状态。
        if (GameEntry.Fruits != null)
        {
            GameEntry.Fruits.TryUnlockPet(petCode);
        }

        return true;
    }

    /// <summary>
    /// 根据当前场地情况为宠物分配站位。
    /// </summary>
    private bool TryPlacePet(string petCode, QualityType petQuality, int hatchSlotIndex, out PetRuntimeState petState)
    {
        petState = null;
        if (TryGetEmptyDiningSeatIndex(out int diningSeatIndex))
        {
            return CreatePetState(petCode, petQuality, PetPlacementType.DiningSeat, diningSeatIndex, hatchSlotIndex, out petState);
        }

        if (TryGetEmptyQueueSlotIndex(out int queueSlotIndex))
        {
            return CreatePetState(petCode, petQuality, PetPlacementType.Queue, queueSlotIndex, hatchSlotIndex, out petState);
        }

        Log.Warning("PetPlacementModule 无法放置宠物 '{0}'，餐桌和排队位均已满。", petCode);
        return false;
    }

    /// <summary>
    /// 按品质随机挑选一只宠物。
    /// 运行时会用玩家当前星星总额过滤候选池：仅当 PetDataRow.RequiredStars &lt;= currentStars 时该宠物才可能被抽到。
    /// 同品质若没有任何宠物达到星星条件，则回退为该品质内 RequiredStars 最低的那一只，避免破坏蛋的孵化体验。
    /// 整个过程使用两遍数组扫描，无任何 List/字符串分配，满足 Zero GC 要求。
    /// </summary>
    private bool TryPickPetCodeByQuality(QualityType petQuality, out string petCode)
    {
        petCode = null;
        if (_petCandidatesByQuality.Count == 0 && !WarmupPetSelectionCatalog())
        {
            Log.Error("PetPlacementModule 无法挑选宠物，宠物选择目录不可用。");
            return false;
        }

        if (!_petCandidatesByQuality.TryGetValue(petQuality, out PetDataRow[] candidates)
            || candidates == null
            || candidates.Length == 0)
        {
            Log.Error("PetPlacementModule 无法挑选宠物，品质 '{0}' 无候选宠物。", petQuality);
            return false;
        }

        // 读取玩家当前累计星星，作为本次抽取的过滤阈值；GameEntry.Fruits 缺失时按 0 处理（仅命中 RequiredStars=0 的宠物）。
        int currentStars = GameEntry.Fruits != null ? GameEntry.Fruits.CurrentStars : 0;

        // 第一遍扫描：统计达标个数 eligibleCount，并顺手记录 RequiredStars 最低的下标 fallbackIndex 作为保底。
        // fallbackIndex 始终指向同品质里"最容易解锁"的那只，等价于策划在表里把它排在前列即可控制兜底优先级。
        int eligibleCount = 0;
        int fallbackIndex = 0;
        int fallbackThreshold = int.MaxValue;
        for (int i = 0; i < candidates.Length; i++)
        {
            PetDataRow candidate = candidates[i];
            if (candidate == null)
            {
                continue;
            }

            if (candidate.RequiredStars <= currentStars)
            {
                eligibleCount++;
            }

            if (candidate.RequiredStars < fallbackThreshold)
            {
                fallbackThreshold = candidate.RequiredStars;
                fallbackIndex = i;
            }
        }

        // 没人达标：用保底兜底，确保孵化流程不中断。正常配置（至少 1 只 RequiredStars=0）永不触发此分支。
        if (eligibleCount == 0)
        {
            PetDataRow fallbackPet = candidates[fallbackIndex];
            if (fallbackPet == null)
            {
                Log.Error("PetPlacementModule 无法挑选宠物，品质 '{0}' 候选数据非法。", petQuality);
                return false;
            }

            petCode = fallbackPet.Code;
            return true;
        }

        // 第二遍扫描：在 [0, eligibleCount) 内均匀抽取，再线性命中第 pick 个达标项。
        // 这里的"均匀概率"特指品质内的均匀分布，与 EggDataRow 的品质概率是两个独立维度，互不影响。
        int pick = UnityEngine.Random.Range(0, eligibleCount);
        for (int i = 0; i < candidates.Length; i++)
        {
            PetDataRow candidate = candidates[i];
            if (candidate == null || candidate.RequiredStars > currentStars)
            {
                continue;
            }

            if (pick == 0)
            {
                petCode = candidate.Code;
                return true;
            }

            pick--;
        }

        // 理论不可达：第一遍统计为 eligibleCount > 0，第二遍必然能命中。这里仅作为防御性兜底。
        Log.Error("PetPlacementModule 抽取宠物时第二遍扫描失败，品质 '{0}'。", petQuality);
        return false;
    }

    /// <summary>
    /// 按蛋表概率抽取宠物品质。
    /// </summary>
    private static bool TryRollPetQuality(EggDataRow eggDataRow, out QualityType petQuality)
    {
        petQuality = QualityType.Universal;
        if (eggDataRow == null)
        {
            return false;
        }

        int randomValue = UnityEngine.Random.Range(0, 100);
        if (randomValue < eggDataRow.NormalRate)
        {
            petQuality = QualityType.Normal;
            return true;
        }

        randomValue -= eggDataRow.NormalRate;
        if (randomValue < eggDataRow.RareRate)
        {
            petQuality = QualityType.Rare;
            return true;
        }

        randomValue -= eggDataRow.RareRate;
        if (randomValue < eggDataRow.EpicRate)
        {
            petQuality = QualityType.Epic;
            return true;
        }

        randomValue -= eggDataRow.EpicRate;
        if (randomValue < eggDataRow.LegendaryRate)
        {
            petQuality = QualityType.Legendary;
            return true;
        }

        randomValue -= eggDataRow.LegendaryRate;
        if (randomValue < eggDataRow.MythicRate)
        {
            petQuality = QualityType.Mythic;
            return true;
        }

        return false;
    }

    /// <summary>
    /// 创建并登记一只宠物的运行时状态。
    /// </summary>
    private bool CreatePetState(
        string petCode,
        QualityType petQuality,
        PetPlacementType placementType,
        int slotIndex,
        int hatchSlotIndex,
        out PetRuntimeState petState)
    {
        petState = null;
        if (string.IsNullOrWhiteSpace(petCode))
        {
            Log.Warning("PetPlacementModule 无法创建宠物，宠物编码为空。");
            return false;
        }

        int instanceId = AcquireInstanceId();
        if (instanceId <= 0)
        {
            Log.Error("PetPlacementModule 无法创建宠物，实例 Id 无效。");
            return false;
        }

        if (!TryOccupySlot(placementType, slotIndex, instanceId))
        {
            return false;
        }

        petState = new PetRuntimeState
        {
            InstanceId = instanceId,
            PetCode = petCode,
            Quality = petQuality,
            PlacementType = placementType,
            SlotIndex = slotIndex,
            DiningWishState = PetDiningWishState.None,
            PlayAreaIndex = -1,
            PendingSpawnHatchSlotIndex = hatchSlotIndex,
            // 从 PetDataRow.EatFruitCount 读取本只宠物本会话总计可吃次数。
            // 查表失败时退化为 1，避免取到 0 后宠物刚出生就被判定为“吃完”。
            RemainingEatFruitCount = ResolveInitialEatFruitCount(petCode)
        };

        AssignDiningWishFruitIfNeeded(petState);
        _petStates.Add(instanceId, petState);
        NotifyPlacementChanged();
        return true;
    }

    /// <summary>
    /// 查表读取宠物本会话可吃次数初值。
    /// 运行期仅在 Spawn 调用一次，后续结算只读 PetRuntimeState.RemainingEatFruitCount。
    /// 查表失败退化为 1，避免获得“可吃次数 0”的退化宠物。
    /// </summary>
    /// <param name="petCode">宠物机器码。</param>
    /// <returns>初始可吃次数。</returns>
    private static int ResolveInitialEatFruitCount(string petCode)
    {
        if (GameEntry.DataTables == null || string.IsNullOrWhiteSpace(petCode))
        {
            return 1;
        }

        PetDataRow petDataRow = GameEntry.DataTables.GetDataRowByCode<PetDataRow>(petCode);
        if (petDataRow == null || petDataRow.EatFruitCount <= 0)
        {
            // PetDataRow 解析已保证 EatFruitCount > 0，这里走到表示完全拿不到配置，退化位避免除零逻辑。
            Log.Warning("PetPlacementModule 未能从 PetDataRow 获取吃水果次数，退化为 1，宠物 '{0}'。", petCode);
            return 1;
        }

        return petDataRow.EatFruitCount;
    }

    /// <summary>
    /// 如果宠物当前直接进入餐桌位，则在进入时抽取一次期望水果。
    /// </summary>
    /// <param name="petState">待处理的宠物状态。</param>
    private static void AssignDiningWishFruitIfNeeded(PetRuntimeState petState)
    {
        if (petState == null
            || petState.PlacementType != PetPlacementType.DiningSeat
            || !string.IsNullOrWhiteSpace(petState.DesiredFruitCode)
            || GameEntry.Fruits == null)
        {
            return;
        }

        if (!GameEntry.Fruits.TryRollDiningWishFruit(out FruitDataRow fruitDataRow) || fruitDataRow == null)
        {
            return;
        }

        petState.DesiredFruitCode = fruitDataRow.Code;
        petState.DiningWishState = PetDiningWishState.Pending;
    }

    /// <summary>
    /// 统一派发宠物站位变化通知。
    /// </summary>
    private void NotifyPlacementChanged()
    {
        PlacementChanged?.Invoke();
        GameEntry.PlayfieldEntities?.NotifyPetPlacementChanged();
    }

    /// <summary>
    /// 外部在仅修改宠物运行时展示状态时，主动派发一次站位相关刷新通知。
    /// 用于 bubble 或点餐流程状态变化时重建 UI，而不改变真正的座位信息。
    /// </summary>
    public void NotifyPlacementStateChanged()
    {
        NotifyPlacementChanged();
    }

    /// <summary>
    /// 处理宠物本次吃完后的去向。
    /// 状态机：
    /// 　1) 按 GoPlayAreaProbability 抽 50/50：命中上半 → 释放原占位 + Promote + 进 PlayArea。
    /// 　2) 未命中下半且还有吃饭次数：
    /// 　   - 在餐桌 且 Queue 无人 → 保留原坐位重新抽 wish（避免“释放→Promote→拼回”的无意义折腾）。
    /// 　   - 其他情况（在 PlayArea / Queue 有人等）→ 释放 + Promote + TryEnqueueForRedining，进 Queue 后补位机制会自动补上餐桌。
    /// 　3) 次数耗尽 或 Queue 满 → 释放 + Promote + BeginLeaving。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    public void ResolvePostMealOutcome(int petInstanceId)
    {
        PetRuntimeState petState = GetPetStateByInstanceId(petInstanceId);
        if (petState == null)
        {
            return;
        }

        if (!TryGetGameplayRuleDataRow(out GameplayRuleDataRow gameplayRuleDataRow))
        {
            Log.Warning("PetPlacementModule can not resolve post meal outcome because GameplayRuleDataRow is unavailable.");
            ReleaseSeatAndPromoteIfNeeded(petState);
            BeginLeaving(petState);
            return;
        }

        // 进出期间能预先清一些进餐阶段遗留。​
        // ​这里不提前清 DesiredFruitCode，以便“留原桌”分支走 KeepDiningSeatAndReorder 手动重新抽 wish。
        petState.DiningWishState = PetDiningWishState.Completed;
        petState.RemainingDiningStageSeconds = 0f;
        petState.PendingSpawnHatchSlotIndex = -1;

        int playAreaCount = GameEntry.PlayfieldEntities != null ? GameEntry.PlayfieldEntities.PlayAreaCount : 0;
        bool goPlayArea = playAreaCount > 0
            && UnityEngine.Random.Range(0, 100) < gameplayRuleDataRow.GoPlayAreaProbability;
        if (goPlayArea)
        {
            ReleaseSeatAndPromoteIfNeeded(petState);
            BeginPlayAreaPlacement(petState, playAreaCount);
            return;
        }

        // 下半分支（“老顾客优先”策略）：
        // 　- 还有次数 + 当前在 DiningSeat → 不释放餐桌，直接重新抽 wish 继续吃。
        // 　  这条路径绝不进 Queue 也绝不让位，即使 Queue 有人在排队，也由 Queue 自行等待下一次空桌。
        // 　- 还有次数 + 当前在 PlayArea → 没有原桌可坐，进 Queue 等待补位（Queue 内部 Promote 会在有空桌时自动拼上）。
        // 　- 次数耗尽 / Queue 满 → BeginLeaving。
        bool wasOnDiningSeat = petState.PlacementType == PetPlacementType.DiningSeat;
        bool stillHasMeal = petState.RemainingEatFruitCount > 0;
        if (stillHasMeal && wasOnDiningSeat)
        {
            // 原桌不释放 + 重新抽 wish + 通知 UI 重建气泡。
            KeepDiningSeatAndReorder(petState);
            return;
        }

        // 走到这里只剩两种情况：在 PlayArea 且还有次数 → 优先占空 DiningSeat / 否则进 Queue；或次数耗尽 → 离场。
        // 两种情况都需要先释放原占位（PlayArea 转出时 ReleasePlacementSlotIfNeeded 自然 no-op）。
        ReleaseSeatAndPromoteIfNeeded(petState);

        if (stillHasMeal && TrySeatOrEnqueueForRedining(petState))
        {
            return;
        }

        // 次数耗尽 或 Queue 满 → 离场。
        BeginLeaving(petState);
    }

    /// <summary>
    /// 强制让宠物走 PlayArea 分支，不走 50/50，不消耗吃饭次数。
    /// 主要供“点击气泡但水果未解锁”等退化场景使用：这种场景不该重点付费一次次数，也不该出现“主动插队”。
    /// PlayArea 不可用时退化为本次什么也不做，仅释放原桌 + Promote，避免宠物被迫离场丢掉未消耗的次数。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    public void ForceGoPlayArea(int petInstanceId)
    {
        PetRuntimeState petState = GetPetStateByInstanceId(petInstanceId);
        if (petState == null)
        {
            return;
        }

        // 同样干净一下进餐阶段遗留，但不消耗次数。
        petState.DiningWishState = PetDiningWishState.Completed;
        petState.RemainingDiningStageSeconds = 0f;
        petState.PendingSpawnHatchSlotIndex = -1;

        int playAreaCount = GameEntry.PlayfieldEntities != null ? GameEntry.PlayfieldEntities.PlayAreaCount : 0;
        if (playAreaCount <= 0)
        {
            // PlayArea 不可用的边界场景：留原桌 或 什么也不做均可。​
            // 这里选择不动，仅补一次 NotifyPlacementChanged，让 UI 重新评估气泡状态。
            // PetDiningOrderComponent 调用者已经重置 DiningWishState=Completed，不会重复弹气泡。
            NotifyPlacementChanged();
            return;
        }

        ReleaseSeatAndPromoteIfNeeded(petState);
        BeginPlayAreaPlacement(petState, playAreaCount);
    }

    /// <summary>
    /// 释放宠物原占位，并在释放了 DiningSeat 时顺手跳一次 PromoteQueuePetsIfPossible。
    /// 从 PlayArea 转出时该函数是 no-op，ReleasePlacementSlotIfNeeded 内部会返回 false。
    /// </summary>
    /// <param name="petState">待释放原位的宠物状态。</param>
    private void ReleaseSeatAndPromoteIfNeeded(PetRuntimeState petState)
    {
        if (petState == null)
        {
            return;
        }

        bool releasedDiningSeat = ReleasePlacementSlotIfNeeded(petState);
        if (releasedDiningSeat)
        {
            PromoteQueuePetsIfPossible();
        }
    }

    /// <summary>
    /// 把宠物状态切换到 PlayArea 分支。
    /// 调用前调用者需保证 playAreaCount > 0。
    /// </summary>
    /// <param name="petState">待进入 PlayArea 的宠物状态。</param>
    /// <param name="playAreaCount">PlayArea 可用总数。</param>
    private void BeginPlayAreaPlacement(PetRuntimeState petState, int playAreaCount)
    {
        if (petState == null || playAreaCount <= 0)
        {
            return;
        }

        petState.PlacementType = PetPlacementType.PlayArea;
        petState.PlayAreaIndex = UnityEngine.Random.Range(0, playAreaCount);
        petState.PlayAreaRandomPosition01 = new Vector2(UnityEngine.Random.value, UnityEngine.Random.value);
        petState.SlotIndex = petState.PlayAreaIndex;
        // 计时要等宠物真正走到 PlayArea 后才开始，否则移动过程会吃掉停留时间。
        petState.RemainingPostMealSeconds = 0f;
        // 吃饭、生产、点餐状态都不再适用 PlayArea，顺手清一道避免脱离餐桌后还挂着旧气泡。
        petState.DiningWishState = PetDiningWishState.None;
        petState.DesiredFruitCode = null;
        petState.OrchardSlotIndex = -1;
        NotifyPlacementChanged();
    }

    /// <summary>
    /// 检查当前是否还有实例在排队。
    /// 仅扫描 _queueInstanceIds，不分配任何堆内存。
    /// </summary>
    /// <returns>当前排队区是否有任何宠物。</returns>
    private bool HasAnyQueuedPet()
    {
        for (int i = 0; i < _queueInstanceIds.Length; i++)
        {
            if (_queueInstanceIds[i] != 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 从 PlayArea 转出 + 还有吃饭次数 时的入座决策。
    /// 　- Queue 无人 且 餐桌有空位 → 直接占空餐桌，避免“先进 Queue 再 Promote”产生中间态闪烁。
    /// 　- Queue 有人 或 餐桌全满 → 退回走 TryEnqueueForRedining（进 Queue 等补位）。
    /// </summary>
    /// <param name="petState">待入座的宠物状态。</param>
    /// <returns>是否成功入座 / 进队。</returns>
    private bool TrySeatOrEnqueueForRedining(PetRuntimeState petState)
    {
        if (petState == null)
        {
            return false;
        }

        // Queue 无人走快路线：优先占空餐桌。
        if (!HasAnyQueuedPet() && TrySeatOnEmptyDiningSeat(petState))
        {
            return true;
        }

        return TryEnqueueForRedining(petState);
    }

    /// <summary>
    /// 尝试把宠物直接占到一个空餐桌位。
    /// 调用前需保证宠物原占位已释放（PlayArea 转出本身不占位，符合该要求）。
    /// 成功后状态与 Queue 补位到 DiningSeat 等价：重新抽 wish + 触发入座动画。
    /// </summary>
    /// <param name="petState">待入座的宠物状态。</param>
    /// <returns>是否成功占空餐桌。</returns>
    private bool TrySeatOnEmptyDiningSeat(PetRuntimeState petState)
    {
        if (petState == null)
        {
            return false;
        }

        if (!TryGetEmptyDiningSeatIndex(out int diningSeatIndex))
        {
            return false;
        }

        if (!TryOccupySlot(PetPlacementType.DiningSeat, diningSeatIndex, petState.InstanceId))
        {
            return false;
        }

        // 与 PromoteQueuePetsIfPossible 里对 Queue 补位到 DiningSeat 的字段重置保持一致，
        // 避免 PlayArea 遗留字段污染新一轮点餐。
        petState.PlacementType = PetPlacementType.DiningSeat;
        petState.SlotIndex = diningSeatIndex;
        petState.PlayAreaIndex = -1;
        petState.PlayAreaRandomPosition01 = Vector2.zero;
        petState.RemainingPostMealSeconds = 0f;
        petState.RemainingDiningStageSeconds = 0f;
        petState.DiningWishState = PetDiningWishState.None;
        petState.DesiredFruitCode = null;
        petState.OrchardSlotIndex = -1;
        // PendingPromoteToDining = true 以触发“入座移动动画”，避免实体从 PlayArea 瓬移到餐桌。
        petState.PendingPromoteToDining = true;

        // 重新抽取期望水果 + 通知 UI：一个原子动作，UI 只会看到“占餐桌 + 出气泡”这一个最终状态。
        AssignDiningWishFruitIfNeeded(petState);
        NotifyPlacementChanged();
        return true;
    }

    /// <summary>
    /// 让当前仍坐在餐桌位的宠物不释放餐桌，直接重新抽一份 wish 并重建气泡。
    /// “老顾客优先”：只要还有吃饭次数 + 当前在 DiningSeat，就走这条路径，不让位给排队宠物。
    /// </summary>
    /// <param name="petState">待“原桌重新点餐”的宠物状态。</param>
    private void KeepDiningSeatAndReorder(PetRuntimeState petState)
    {
        if (petState == null)
        {
            return;
        }

        // 清掉上一轮进餐流程遗留的临时字段，让 AssignDiningWishFruitIfNeeded 能重新抽。
        // PlacementType、SlotIndex 保持不动 — 宠物付仍占原餐桌位。
        petState.DesiredFruitCode = null;
        petState.DiningWishState = PetDiningWishState.None;
        petState.OrchardSlotIndex = -1;
        petState.RemainingDiningStageSeconds = 0f;
        petState.RemainingPostMealSeconds = 0f;
        petState.PendingPromoteToDining = false;

        // 重新抽取期望水果；AssignDiningWishFruitIfNeeded 会在 PlacementType=DiningSeat 且 DesiredFruitCode 为空时生效。
        AssignDiningWishFruitIfNeeded(petState);
        NotifyPlacementChanged();
    }

    /// <summary>
    /// 尝试把“还有吃饭次数”的宠物重新推进排队区，等待下一轮补位进 DiningSeat。
    /// 调用前需保证宠物已释放原占位（DiningSeat 转出时 ReleasePlacementSlotIfNeeded 已释放；
    /// PlayArea 转出时本身未占任何餐桌/排队槽）。
    /// </summary>
    /// <param name="petState">待推进排队的宠物状态。</param>
    /// <returns>是否成功进入排队（包括随后立刻被 Promote 补位的情况）。</returns>
    private bool TryEnqueueForRedining(PetRuntimeState petState)
    {
        if (petState == null)
        {
            return false;
        }

        // 只要还有空 Queue 槽就能出发，否则返回 false 让调用方走 Leaving。
        if (!TryGetEmptyQueueSlotIndex(out int queueSlotIndex))
        {
            return false;
        }

        if (!TryOccupySlot(PetPlacementType.Queue, queueSlotIndex, petState.InstanceId))
        {
            return false;
        }

        // 切换到 Queue 状态同时统一清理 PlayArea / Producing / Pending 遗留字段。
        // （PromoteQueuePetsIfPossible 内部也会再清一道，这里提前清是为“进 Queue 但未被立即补位”的侧支打补丁。
        petState.PlacementType = PetPlacementType.Queue;
        petState.SlotIndex = queueSlotIndex;
        petState.PlayAreaIndex = -1;
        petState.PlayAreaRandomPosition01 = Vector2.zero;
        petState.RemainingPostMealSeconds = 0f;
        petState.RemainingDiningStageSeconds = 0f;
        petState.DiningWishState = PetDiningWishState.None;
        petState.DesiredFruitCode = null;
        petState.PendingPromoteToDining = false;
        NotifyPlacementChanged();

        // 进 Queue 后立刻走一次补位：若此时餐桌有空位，宠物将被拼接到 DiningSeat 并重新 AssignDiningWishFruitIfNeeded。
        PromoteQueuePetsIfPossible();
        return true;
    }

    /// <summary>
    /// 彻底移除一只宠物。
    /// 用于离场移动到目标高度后回收运行时状态与实体。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    public void RemovePet(int petInstanceId)
    {
        if (!_petStates.TryGetValue(petInstanceId, out PetRuntimeState petState) || petState == null)
        {
            return;
        }

        bool releasedDiningSeat = ReleasePlacementSlotIfNeeded(petState);
        if (releasedDiningSeat)
        {
            if (petState.SlotIndex >= 0)
            {
                GameEntry.PlayfieldEntities?.HideDiningFruit(petState.SlotIndex);
            }

            PromoteQueuePetsIfPossible();
        }

        _petStates.Remove(petInstanceId);
        NotifyPlacementChanged();
    }

    /// <summary>
    /// 尝试把排队区最前面的宠物补位到空餐桌。
    /// 补位会压缩整个队列索引，保证队列头始终是最小索引。
    /// </summary>
    public bool PromoteQueuePetsIfPossible()
    {
        bool promotedAnyPet = false;
        while (TryGetEmptyDiningSeatIndex(out int diningSeatIndex) && TryGetFirstQueuedPet(out PetRuntimeState queuedPet))
        {
            if (queuedPet == null)
            {
                break;
            }

            int previousQueueSlotIndex = queuedPet.SlotIndex;
            if (previousQueueSlotIndex < 0 || previousQueueSlotIndex >= _queueInstanceIds.Length)
            {
                break;
            }

            _queueInstanceIds[previousQueueSlotIndex] = 0;
            ShiftQueueStatesForward(previousQueueSlotIndex);

            _diningSeatInstanceIds[diningSeatIndex] = queuedPet.InstanceId;
            queuedPet.PlacementType = PetPlacementType.DiningSeat;
            queuedPet.SlotIndex = diningSeatIndex;
            queuedPet.PlayAreaIndex = -1;
            queuedPet.PlayAreaRandomPosition01 = Vector2.zero;
            queuedPet.RemainingPostMealSeconds = 0f;
            queuedPet.RemainingDiningStageSeconds = 0f;
            queuedPet.DiningWishState = PetDiningWishState.None;
            queuedPet.DesiredFruitCode = null;
            queuedPet.PendingSpawnHatchSlotIndex = -1;
            queuedPet.PendingPromoteToDining = true;
            AssignDiningWishFruitIfNeeded(queuedPet);
            promotedAnyPet = true;
        }

        return promotedAnyPet;
    }

    /// <summary>
    /// 宠物真正到达 PlayArea 后，开始 5 秒停留计时。
    /// 重复调用是安全的，已经在停留中的宠物不会被重置计时。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    public void BeginPlayAreaStay(int petInstanceId)
    {
        PetRuntimeState petState = GetPetStateByInstanceId(petInstanceId);
        if (petState == null || petState.PlacementType != PetPlacementType.PlayArea || petState.RemainingPostMealSeconds > 0f)
        {
            return;
        }

        if (!TryGetGameplayRuleDataRow(out GameplayRuleDataRow gameplayRuleDataRow))
        {
            return;
        }

        petState.RemainingPostMealSeconds = gameplayRuleDataRow.PlayAreaStaySeconds;
    }

    /// <summary>
    /// 获取全局玩法规则缓存。
    /// 缓存缺失时会尝试从数据表模块回填一次。
    /// </summary>
    /// <param name="gameplayRuleDataRow">命中的玩法规则行。</param>
    /// <returns>是否命中有效规则。</returns>
    private bool TryGetGameplayRuleDataRow(out GameplayRuleDataRow gameplayRuleDataRow)
    {
        if (_gameplayRuleDataRow == null)
        {
            WarmupGameplayRuleCache();
        }

        gameplayRuleDataRow = _gameplayRuleDataRow;
        return gameplayRuleDataRow != null;
    }

    /// <summary>
    /// 开始一只宠物的离场流程。
    /// 它会立即脱离任何座位占用，并进入 Leaving 状态等待实体移动完毕后被移除。
    /// </summary>
    /// <param name="petState">宠物运行时状态。</param>
    private void BeginLeaving(PetRuntimeState petState)
    {
        if (petState == null)
        {
            return;
        }

        petState.PlacementType = PetPlacementType.Leaving;
        petState.RemainingPostMealSeconds = 0f;
        NotifyPlacementChanged();
    }

    /// <summary>
    /// 释放宠物当前占用的餐桌或队列槽位。
    /// </summary>
    /// <param name="petState">宠物运行时状态。</param>
    /// <returns>本次是否释放了餐桌位。</returns>
    private bool ReleasePlacementSlotIfNeeded(PetRuntimeState petState)
    {
        if (petState == null)
        {
            return false;
        }

        if (petState.PlacementType == PetPlacementType.DiningSeat
            && petState.SlotIndex >= 0
            && petState.SlotIndex < _diningSeatInstanceIds.Length
            && _diningSeatInstanceIds[petState.SlotIndex] == petState.InstanceId)
        {
            _diningSeatInstanceIds[petState.SlotIndex] = 0;
            return true;
        }

        if (petState.PlacementType == PetPlacementType.Queue
            && petState.SlotIndex >= 0
            && petState.SlotIndex < _queueInstanceIds.Length
            && _queueInstanceIds[petState.SlotIndex] == petState.InstanceId)
        {
            int previousQueueSlotIndex = petState.SlotIndex;
            _queueInstanceIds[petState.SlotIndex] = 0;
            ShiftQueueStatesForward(previousQueueSlotIndex);
        }

        return false;
    }

    /// <summary>
    /// 获取当前队列最前面的宠物。
    /// </summary>
    /// <param name="petState">命中的宠物运行时状态。</param>
    /// <returns>是否命中有效的队列宠物。</returns>
    private bool TryGetFirstQueuedPet(out PetRuntimeState petState)
    {
        for (int i = 0; i < _queueInstanceIds.Length; i++)
        {
            int petInstanceId = _queueInstanceIds[i];
            if (petInstanceId == 0)
            {
                continue;
            }

            if (_petStates.TryGetValue(petInstanceId, out petState) && petState != null)
            {
                return true;
            }
        }

        petState = null;
        return false;
    }

    /// <summary>
    /// 在队列中移除一个槽位后，把后续宠物向前压缩一格。
    /// </summary>
    /// <param name="removedSlotIndex">刚被移除的队列槽位索引。</param>
    private void ShiftQueueStatesForward(int removedSlotIndex)
    {
        if (removedSlotIndex < 0 || removedSlotIndex >= _queueInstanceIds.Length)
        {
            return;
        }

        for (int i = removedSlotIndex + 1; i < _queueInstanceIds.Length; i++)
        {
            int shiftedPetInstanceId = _queueInstanceIds[i];
            _queueInstanceIds[i - 1] = shiftedPetInstanceId;
            if (shiftedPetInstanceId != 0
                && _petStates.TryGetValue(shiftedPetInstanceId, out PetRuntimeState shiftedPetState)
                && shiftedPetState != null
                && shiftedPetState.PlacementType == PetPlacementType.Queue
                && shiftedPetState.SlotIndex == i)
            {
                shiftedPetState.SlotIndex = i - 1;
            }
        }

        _queueInstanceIds[_queueInstanceIds.Length - 1] = 0;
    }

    /// <summary>
    /// 尝试占用目标槽位。
    /// </summary>
    private bool TryOccupySlot(PetPlacementType placementType, int slotIndex, int instanceId)
    {
        switch (placementType)
        {
            case PetPlacementType.DiningSeat:
                if (slotIndex < 0 || slotIndex >= _diningSeatInstanceIds.Length || _diningSeatInstanceIds[slotIndex] != 0)
                {
                    Log.Warning("PetPlacementModule can not occupy dining seat '{0}'.", slotIndex);
                    return false;
                }

                _diningSeatInstanceIds[slotIndex] = instanceId;
                return true;

            case PetPlacementType.Queue:
                if (slotIndex < 0 || slotIndex >= _queueInstanceIds.Length || _queueInstanceIds[slotIndex] != 0)
                {
                    Log.Warning("PetPlacementModule can not occupy queue slot '{0}'.", slotIndex);
                    return false;
                }

                _queueInstanceIds[slotIndex] = instanceId;
                return true;

            default:
                Log.Warning("PetPlacementModule can not occupy slot because placement type '{0}' is invalid.", placementType);
                return false;
        }
    }

    /// <summary>
    /// 查找第一个空餐桌位。
    /// </summary>
    private bool TryGetEmptyDiningSeatIndex(out int slotIndex)
    {
        for (int i = 0; i < _diningSeatInstanceIds.Length; i++)
        {
            if (_diningSeatInstanceIds[i] != 0)
            {
                continue;
            }

            // 跳过跳购导致的未解锁"洞"：例如 1/3 号位解锁但 2 号位仍锁着，2 号位不可放宠物。
            // _diningSeatInstanceIds 长度由 SetDiningSeatCount 扩张到已购买的最大索引，但中间可能存在未解锁槽位。
            if (GameEntry.Fruits != null && !GameEntry.Fruits.IsArchitectureSlotUnlocked(PlayerRuntimeModule.ArchitectureCategory.Diet, i + 1))
            {
                continue;
            }

            slotIndex = i;
            return true;
        }

        slotIndex = -1;
        return false;
    }

    /// <summary>
    /// 查找第一个空排队位。
    /// </summary>
    private bool TryGetEmptyQueueSlotIndex(out int slotIndex)
    {
        for (int i = 0; i < _queueInstanceIds.Length; i++)
        {
            if (_queueInstanceIds[i] != 0)
            {
                continue;
            }

            slotIndex = i;
            return true;
        }

        slotIndex = -1;
        return false;
    }

    /// <summary>
    /// 分配新的宠物实例 Id。
    /// </summary>
    private int AcquireInstanceId()
    {
        if (_nextInstanceId >= int.MaxValue)
        {
            Log.Error("PetPlacementModule 实例 Id 已耗尽。");
            return 0;
        }

        return _nextInstanceId++;
    }

    /// <summary>
    /// 宠物列表排序规则。
    /// </summary>
    private static int ComparePetStates(PetRuntimeState left, PetRuntimeState right)
    {
        if (ReferenceEquals(left, right))
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        int placementCompare = left.PlacementType.CompareTo(right.PlacementType);
        if (placementCompare != 0)
        {
            return placementCompare;
        }

        int slotCompare = left.SlotIndex.CompareTo(right.SlotIndex);
        if (slotCompare != 0)
        {
            return slotCompare;
        }

        return left.InstanceId.CompareTo(right.InstanceId);
    }
}
