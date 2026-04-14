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
    /// 宠物吃完后进入玩耍区的概率。
    /// 剩余概率直接走离场逻辑。
    /// </summary>
    private const int GoPlayAreaProbability = 50;

    /// <summary>
    /// 宠物进入玩耍区后的停留秒数。
    /// 到时后再次做 50/50 去向判定。
    /// </summary>
    private const float PlayAreaStaySeconds = 5f;

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

        return TryPlacePet(petCode, petQuality, hatchSlotIndex, out petState);
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

        Log.Warning("PetPlacementModule can not place pet '{0}' because dining seats and queue slots are both full.", petCode);
        return false;
    }

    /// <summary>
    /// 按品质随机挑选一只宠物。
    /// </summary>
    private static bool TryPickPetCodeByQuality(QualityType petQuality, out string petCode)
    {
        petCode = null;
        PetDataRow[] petRows = GameEntry.DataTables.GetAllDataRows<PetDataRow>();
        if (petRows == null || petRows.Length == 0)
        {
            Log.Error("PetPlacementModule can not pick pet because PetDataTable is empty.");
            return false;
        }

        List<PetDataRow> candidates = new List<PetDataRow>();
        for (int i = 0; i < petRows.Length; i++)
        {
            PetDataRow petRow = petRows[i];
            if (petRow == null || petRow.Quality != petQuality)
            {
                continue;
            }

            candidates.Add(petRow);
        }

        if (candidates.Count == 0)
        {
            Log.Error("PetPlacementModule can not pick pet because quality '{0}' has no candidates.", petQuality);
            return false;
        }

        PetDataRow selectedPet = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        petCode = selectedPet.Code;
        return true;
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
            Log.Warning("PetPlacementModule can not create pet because pet code is empty.");
            return false;
        }

        int instanceId = AcquireInstanceId();
        if (instanceId <= 0)
        {
            Log.Error("PetPlacementModule can not create pet because instance id is invalid.");
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
            PendingSpawnHatchSlotIndex = hatchSlotIndex
        };

        AssignDiningWishFruitIfNeeded(petState);
        _petStates.Add(instanceId, petState);
        NotifyPlacementChanged();
        return true;
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
    /// 规则是 50% 去玩耍区停留 5 秒，50% 直接离场；若玩耍区不可用则直接离场。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    public void ResolvePostMealOutcome(int petInstanceId)
    {
        PetRuntimeState petState = GetPetStateByInstanceId(petInstanceId);
        if (petState == null)
        {
            return;
        }

        bool releasedDiningSeat = ReleasePlacementSlotIfNeeded(petState);
        if (releasedDiningSeat)
        {
            PromoteQueuePetsIfPossible();
        }

        petState.DiningWishState = PetDiningWishState.Completed;
        petState.RemainingDiningStageSeconds = 0f;
        petState.PendingSpawnHatchSlotIndex = -1;

        int playAreaCount = GameEntry.PlayfieldEntities != null ? GameEntry.PlayfieldEntities.PlayAreaCount : 0;
        bool goPlayArea = playAreaCount > 0 && UnityEngine.Random.Range(0, 100) < GoPlayAreaProbability;
        if (goPlayArea)
        {
            petState.PlacementType = PetPlacementType.PlayArea;
            petState.PlayAreaIndex = UnityEngine.Random.Range(0, playAreaCount);
            petState.PlayAreaRandomPosition01 = new Vector2(UnityEngine.Random.value, UnityEngine.Random.value);
            petState.SlotIndex = petState.PlayAreaIndex;
            // 计时要等宠物真正走到 PlayArea 后才开始，
            // 否则移动过程会吃掉停留时间，体感上就不是完整 5 秒。
            petState.RemainingPostMealSeconds = 0f;
            NotifyPlacementChanged();
            return;
        }

        BeginLeaving(petState);
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

        petState.RemainingPostMealSeconds = PlayAreaStaySeconds;
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
            Log.Error("PetPlacementModule has run out of instance ids.");
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
