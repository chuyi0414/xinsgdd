using System;
using System.Collections.Generic;
using GameFramework.Event;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 场地实体显示模块。
/// 负责把 UI 标记点投影成全局常驻的蛋、桌子、果园、宠物实体。
/// </summary>
public sealed class PlayfieldEntityModule
{
    /// <summary>
    /// 宠物离场时的目标 Y 坐标。
    /// 到达该高度后会彻底回收这只宠物。
    /// </summary>
    private const float LeavingTargetY = 20f;

    /// <summary>
    /// 孵化槽数量常量。
    /// </summary>
    public const int HatchSlotCountValue = 4;

    /// <summary>
    /// 排队位数量常量。
    /// </summary>
    public const int QueueSlotCountValue = PetPlacementModule.QueueSlotCountValue;

    /// <summary>
    /// 桌位数量。从 PlayerRuntimeModule 运行时数据读取。
    /// </summary>
    public int TableCount => GameEntry.Fruits?.DiningSeatCount ?? PetPlacementModule.DefaultDiningSeatCount;

    /// <summary>
    /// 果园位数量。从 PlayerRuntimeModule 运行时数据读取。
    /// </summary>
    public int OrchardCount => GameEntry.Fruits?.OrchardSlotCount ?? OrchardModule.DefaultOrchardSlotCount;

    /// <summary>
    /// 每个桌位当前绑定的实体 Id。
    /// 延迟到 Initialize 调用后才真正分配。
    /// </summary>
    private int[] _tableEntityIds = Array.Empty<int>();

    /// <summary>
    /// 每个果园位当前绑定的实体 Id。
    /// 延迟到 Initialize 调用后才真正分配。
    /// </summary>
    private int[] _orchardEntityIds = Array.Empty<int>();

    /// <summary>
    /// 每个孵化槽当前绑定的孵化器实体 Id。
    /// 孵化器显示规则是“已解锁槽位就显示”，与槽位里是否有蛋无关。
    /// </summary>
    private readonly int[] _incubatorEntityIds = new int[HatchSlotCountValue];

    /// <summary>
    /// 每个孵化槽当前绑定的蛋实体 Id。
    /// </summary>
    private readonly int[] _eggEntityIds = new int[HatchSlotCountValue];

    /// <summary>
    /// 每个桌位当前绑定的餐桌水果实体 Id。
    /// 延迟到 Initialize 调用后才真正分配。
    /// </summary>
    private int[] _diningFruitEntityIds = Array.Empty<int>();

    /// <summary>
    /// 每个果树当前绑定的生产水果实体 Id。
    /// 水果在果树上生长 → 飞行送达 → 隐藏后清零。
    /// 延迟到 Initialize 调用后才真正分配。
    /// </summary>
    private int[] _orchardFruitEntityIds = Array.Empty<int>();

    /// <summary>
    /// 宠物实例 Id 到实体 Id 的映射。
    /// </summary>
    private readonly Dictionary<int, int> _petEntityIdsByPetInstanceId = new Dictionary<int, int>();

    /// <summary>
    /// 等待桌子实体加载完成后再挂接的入座宠物动画标记。
    /// </summary>
    private readonly Dictionary<int, bool> _pendingDiningPetAnimations = new Dictionary<int, bool>();

    /// <summary>
    /// 本轮刷新后需要移除的宠物实例 Id 缓冲。
    /// </summary>
    private readonly List<int> _petInstanceIdsToRemove = new List<int>();

    /// <summary>
    /// 处理延迟入座时使用的遍历缓冲。
    /// </summary>
    private readonly List<int> _pendingDiningPetBuffer = new List<int>();

    /// <summary>
    /// 当前生效的场地标记点快照。
    /// </summary>
    private PlayfieldMarkerSnapshot _currentMarkerSnapshot;

    /// <summary>
    /// 果树水果生长动画请求缓存。
    /// 在实体加载完成后取出并播放缩放动画。
    /// Key = 水果实体 Id，Value = 生长参数。
    /// </summary>
    private readonly Dictionary<int, OrchardGrowRequest> _pendingOrchardGrow = new Dictionary<int, OrchardGrowRequest>();

    /// <summary>
    /// 是否已经订阅实体显示结果事件。
    /// </summary>
    private bool _isEntityEventSubscribed;

    /// <summary>
    /// 创建场地实体模块并准备事件监听。
    /// </summary>
    public PlayfieldEntityModule()
    {
        EnsureEntityEventSubscription();
    }

    /// <summary>
    /// 根据运行时数据初始化桌位和果园数组大小。
    /// 初始化和后续升级都走同一套扩容逻辑，避免实体缓存数组出现两套维护路径。
    /// </summary>
    /// <param name="tableCount">桌位数量，必须大于 0。</param>
    /// <param name="orchardCount">果园位数量，必须大于 0。</param>
    public void Initialize(int tableCount, int orchardCount)
    {
        EnsureCapacity(tableCount, orchardCount);
    }

    /// <summary>
    /// 确保桌位与果园位缓存容量至少达到指定数量。
    /// 这里只允许扩容，不允许缩容，避免破坏已加载实体与运行时索引的对应关系。
    /// </summary>
    /// <param name="tableCount">目标桌位数量。</param>
    /// <param name="orchardCount">目标果园位数量。</param>
    public void EnsureCapacity(int tableCount, int orchardCount)
    {
        if (tableCount <= 0)
        {
            tableCount = PetPlacementModule.DefaultDiningSeatCount;
        }

        if (orchardCount <= 0)
        {
            orchardCount = OrchardModule.DefaultOrchardSlotCount;
        }

        if (tableCount > _tableEntityIds.Length)
        {
            _tableEntityIds = ExpandIntArray(_tableEntityIds, tableCount);
        }

        if (orchardCount > _orchardEntityIds.Length)
        {
            _orchardEntityIds = ExpandIntArray(_orchardEntityIds, orchardCount);
        }

        if (tableCount > _diningFruitEntityIds.Length)
        {
            _diningFruitEntityIds = ExpandIntArray(_diningFruitEntityIds, tableCount);
        }

        if (orchardCount > _orchardFruitEntityIds.Length)
        {
            _orchardFruitEntityIds = ExpandIntArray(_orchardFruitEntityIds, orchardCount);
        }
    }

    /// <summary>
    /// 当前是否已经拿到可用的 UI 标记点快照。
    /// </summary>
    public bool HasValidMarkerSnapshot => _currentMarkerSnapshot != null && _currentMarkerSnapshot.IsValid;

    /// <summary>
    /// 当前可用的玩耍区数量。
    /// 这个数量来自 MainUIForm 手动拖拽的 PlayArea 列表转换后的世界矩形数组。
    /// </summary>
    public int PlayAreaCount => _currentMarkerSnapshot != null && _currentMarkerSnapshot.PlayAreaWorldRegions != null
        ? _currentMarkerSnapshot.PlayAreaWorldRegions.Length
        : 0;

    /// <summary>
    /// 扩容整型数组并保留旧内容。
    /// 这里只服务于低频升级事件，不会进入高频生命周期。
    /// </summary>
    /// <param name="source">原始数组。</param>
    /// <param name="targetLength">扩容后的目标长度。</param>
    /// <returns>保留旧内容的新数组。</returns>
    private static int[] ExpandIntArray(int[] source, int targetLength)
    {
        int[] expandedArray = new int[targetLength];
        if (source != null && source.Length > 0)
        {
            Array.Copy(source, expandedArray, source.Length);
        }

        return expandedArray;
    }

    /// <summary>
    /// 应用最新的 UI 标记点快照。
    /// </summary>
    public void ApplyMarkerSnapshot(PlayfieldMarkerSnapshot markerSnapshot)
    {
        EnsureEntityEventSubscription();
        if (markerSnapshot == null || !markerSnapshot.IsValid)
        {
            return;
        }

        _currentMarkerSnapshot = markerSnapshot.Clone();
        EnsureTableEntities();
        EnsureOrchardEntities();
        UpdateLoadedTableEntityPositions();
        UpdateLoadedOrchardEntityPositions();
        RefreshIncubatorEntities();
        UpdateLoadedIncubatorEntityPositions();
        RefreshEggEntities();
        RefreshPetEntities(false);
        ProcessPendingDiningPets();
    }

    /// <summary>
    /// 孵化槽状态变化后刷新蛋实体。
    /// </summary>
    public void NotifyEggStateChanged()
    {
        EnsureEntityEventSubscription();
        if (!HasValidMarkerSnapshot)
        {
            return;
        }

        RefreshIncubatorEntities();
        RefreshEggEntities();
    }

    /// <summary>
    /// 宠物排队/入座状态变化后刷新宠物实体。
    /// </summary>
    public void NotifyPetPlacementChanged()
    {
        EnsureEntityEventSubscription();
        if (!HasValidMarkerSnapshot)
        {
            return;
        }

        RefreshPetEntities(true);
        ProcessPendingDiningPets();
    }

    /// <summary>
    /// 按宠物实例 Id 查询当前已加载的宠物实体逻辑。
    /// UI 层只能通过这个受控入口拿到实体，不直接触碰内部映射表。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    /// <param name="petEntityLogic">命中的宠物实体逻辑。</param>
    /// <returns>是否命中一个已加载完成的宠物实体。</returns>
    public bool TryGetPetEntityLogic(int petInstanceId, out PetEntityLogic petEntityLogic)
    {
        petEntityLogic = null;
        if (!_petEntityIdsByPetInstanceId.TryGetValue(petInstanceId, out int entityId))
        {
            return false;
        }

        return TryGetEntityLogic(entityId, out petEntityLogic);
    }

    /// <summary>
    /// 判断指定宠物当前是否已经真正挂接到某一张餐桌实体上。
    /// 只有完成入座挂接后，UI 才应该显示点餐气泡。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    /// <returns>是否已经挂接到餐桌实体。</returns>
    public bool IsPetAttachedToDiningTable(int petInstanceId)
    {
        if (!_petEntityIdsByPetInstanceId.TryGetValue(petInstanceId, out int entityId) || !IsEntityLoaded(entityId) || GameEntry.Entity == null)
        {
            return false;
        }

        Entity parentEntity = GameEntry.Entity.GetParentEntity(entityId);
        if (parentEntity == null)
        {
            return false;
        }

        for (int i = 0; i < _tableEntityIds.Length; i++)
        {
            if (_tableEntityIds[i] == parentEntity.Id)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 尝试在指定桌位显示一个餐桌水果实体。
    /// 只有桌位实体已加载、当前桌面没有其他水果实体时才会成功。
    /// </summary>
    /// <param name="tableIndex">桌位索引。</param>
    /// <param name="fruitCode">水果 Code。</param>
    /// <returns>是否成功提交显示请求。</returns>
    public bool TryShowDiningFruit(int tableIndex, string fruitCode)
    {
        if (tableIndex < 0
            || tableIndex >= _diningFruitEntityIds.Length
            || string.IsNullOrWhiteSpace(fruitCode)
            || GameEntry.Entity == null)
        {
            return false;
        }

        int currentEntityId = _diningFruitEntityIds[tableIndex];
        if (IsEntityLoaded(currentEntityId) || IsEntityLoading(currentEntityId))
        {
            return false;
        }

        int tableEntityId = _tableEntityIds[tableIndex];
        if (!IsEntityLoaded(tableEntityId) || !TryGetEntityLogic(tableEntityId, out TableEntityLogic tableEntityLogic))
        {
            return false;
        }

        int fruitEntityId = AcquireEntityId();
        if (fruitEntityId <= 0)
        {
            return false;
        }

        _diningFruitEntityIds[tableIndex] = fruitEntityId;
        GameEntry.Entity.ShowEntity<FruitEntityLogic>(
            fruitEntityId,
            EntityDefine.FruitEntity,
            EntityDefine.FruitGroup,
            BuildFruitEntityData(tableIndex, fruitCode, tableEntityLogic));
        return true;
    }

    /// <summary>
    /// 隐藏指定桌位上的餐桌水果实体。
    /// </summary>
    /// <param name="tableIndex">桌位索引。</param>
    public void HideDiningFruit(int tableIndex)
    {
        if (tableIndex < 0 || tableIndex >= _diningFruitEntityIds.Length)
        {
            return;
        }

        int entityId = _diningFruitEntityIds[tableIndex];
        if (entityId <= 0)
        {
            return;
        }

        _diningFruitEntityIds[tableIndex] = 0;
        HideEntityIfNeeded(entityId);
    }

    /// <summary>
    /// 尝试在指定果树上创建水果实体并播放生长动画。
    /// </summary>
    /// <param name="orchardIndex">果树索引。</param>
    /// <param name="fruitCode">水果 Code。</param>
    /// <param name="growDuration">生长动画时长（秒）。</param>
    /// <param name="onGrowComplete">生长动画完成回调。</param>
    /// <returns>是否成功提交显示请求。</returns>
    public bool TryShowOrchardFruit(int orchardIndex, string fruitCode, float growDuration, System.Action onGrowComplete)
    {
        if (orchardIndex < 0
            || orchardIndex >= _orchardFruitEntityIds.Length
            || string.IsNullOrWhiteSpace(fruitCode)
            || GameEntry.Entity == null)
        {
            return false;
        }

        int currentEntityId = _orchardFruitEntityIds[orchardIndex];
        if (IsEntityLoaded(currentEntityId) || IsEntityLoading(currentEntityId))
        {
            return false;
        }

        int orchardEntityId = _orchardEntityIds[orchardIndex];
        if (!IsEntityLoaded(orchardEntityId))
        {
            return false;
        }

        int fruitEntityId = AcquireEntityId();
        if (fruitEntityId <= 0)
        {
            return false;
        }

        _orchardFruitEntityIds[orchardIndex] = fruitEntityId;

        // 果树水果使用果树世界坐标作为初始位置
        Vector3 orchardWorldPos = _currentMarkerSnapshot.OrchardWorldPositions[orchardIndex];
        FruitEntityData fruitData = new FruitEntityData(-1, fruitCode, orchardWorldPos, orchardIndex);

        // 临时存储生长参数，在 OnShowEntitySuccess 中取出执行动画
        _pendingOrchardGrow[fruitEntityId] = new OrchardGrowRequest(growDuration, onGrowComplete);

        GameEntry.Entity.ShowEntity<FruitEntityLogic>(
            fruitEntityId,
            EntityDefine.FruitEntity,
            EntityDefine.FruitGroup,
            fruitData);
        return true;
    }

    /// <summary>
    /// 开始果树水果送达动画：从果树位置飞到 BJRight 左边界后隐藏，然后在桌上显示水果。
    /// </summary>
    /// <param name="orchardIndex">果树索引。</param>
    /// <param name="tableIndex">目标桌位索引。</param>
    /// <param name="fruitCode">水果 Code。</param>
    /// <param name="deliverDuration">飞行动画时长（秒）。</param>
    /// <param name="onDeliverComplete">送达完成回调（桌上水果已显示后触发）。</param>
    public void BeginOrchardFruitDelivery(int orchardIndex, int tableIndex, string fruitCode, float deliverDuration, System.Action onDeliverComplete)
    {
        if (orchardIndex < 0 || orchardIndex >= _orchardFruitEntityIds.Length || !HasValidMarkerSnapshot)
        {
            onDeliverComplete?.Invoke();
            return;
        }

        int fruitEntityId = _orchardFruitEntityIds[orchardIndex];
        if (!TryGetEntityLogic(fruitEntityId, out FruitEntityLogic fruitEntityLogic))
        {
            HideOrchardFruit(orchardIndex);
            onDeliverComplete?.Invoke();
            return;
        }

        // 飞行目标：BJRight 左边界 X，保持当前 Y
        float targetX = _currentMarkerSnapshot.RightPageLeftEdgeWorldX;
        Vector3 currentPos = fruitEntityLogic.CachedTransform.position;
        Vector3 deliverTarget = new Vector3(targetX, currentPos.y, currentPos.z);

        // 捕获变量供闭包使用
        int capturedOrchardIndex = orchardIndex;
        int capturedTableIndex = tableIndex;
        string capturedFruitCode = fruitCode;
        System.Action capturedCallback = onDeliverComplete;

        fruitEntityLogic.PlayDeliverAnimation(deliverTarget, deliverDuration, () =>
        {
            // 飞到边界后隐藏果树水果
            HideOrchardFruit(capturedOrchardIndex);

            // 直接在桌子上显示水果
            TryShowDiningFruit(capturedTableIndex, capturedFruitCode);

            capturedCallback?.Invoke();
        });
    }

    /// <summary>
    /// 隐藏指定果树上的水果实体。
    /// </summary>
    /// <param name="orchardIndex">果树索引。</param>
    public void HideOrchardFruit(int orchardIndex)
    {
        if (orchardIndex < 0 || orchardIndex >= _orchardFruitEntityIds.Length)
        {
            return;
        }

        int entityId = _orchardFruitEntityIds[orchardIndex];
        if (entityId <= 0)
        {
            return;
        }

        _orchardFruitEntityIds[orchardIndex] = 0;
        _pendingOrchardGrow.Remove(entityId);
        HideEntityIfNeeded(entityId);
    }

    /// <summary>
    /// 按当前孵化槽解锁状态刷新孵化器实体。
    /// 只要槽位已经解锁，就显示一个孵化器；是否有蛋由蛋实体自己决定。
    /// </summary>
    private void RefreshIncubatorEntities()
    {
        EggHatchComponent eggHatch = GameEntry.EggHatch;
        if (eggHatch == null || !eggHatch.IsAvailable)
        {
            for (int i = 0; i < _incubatorEntityIds.Length; i++)
            {
                HideIncubatorEntity(i);
            }

            return;
        }

        for (int i = 0; i < _incubatorEntityIds.Length; i++)
        {
            EggHatchSlotState slotState = eggHatch.GetSlotState(i);
            if (slotState == null)
            {
                HideIncubatorEntity(i);
                continue;
            }

            EnsureIncubatorEntity(i);
        }
    }

    /// <summary>
    /// 按当前孵化槽状态刷新蛋实体。
    /// </summary>
    private void RefreshEggEntities()
    {
        EggHatchComponent eggHatch = GameEntry.EggHatch;
        if (eggHatch == null || !eggHatch.IsAvailable)
        {
            for (int i = 0; i < _eggEntityIds.Length; i++)
            {
                HideEggEntity(i);
            }

            return;
        }

        for (int i = 0; i < _eggEntityIds.Length; i++)
        {
            EggHatchSlotState slotState = eggHatch.GetSlotState(i);
            bool isOccupied = slotState != null && slotState.IsOccupied;
            if (!isOccupied)
            {
                HideEggEntity(i);
                continue;
            }

            EnsureEggEntity(i, slotState);
            ApplyEggDataToLoadedEntity(i, slotState);
        }
    }

    /// <summary>
    /// 按当前宠物运行时状态刷新宠物实体。
    /// </summary>
    private void RefreshPetEntities(bool animateMovement)
    {
        PetPlacementModule petPlacement = GameEntry.PetPlacement;
        if (petPlacement == null)
        {
            HideAllPetEntities();
            return;
        }

        PetRuntimeState[] petStates = petPlacement.GetAllPets();
        _petInstanceIdsToRemove.Clear();
        foreach (KeyValuePair<int, int> pair in _petEntityIdsByPetInstanceId)
        {
            _petInstanceIdsToRemove.Add(pair.Key);
        }

        for (int i = 0; i < petStates.Length; i++)
        {
            PetRuntimeState petState = petStates[i];
            if (petState == null)
            {
                continue;
            }

            _petInstanceIdsToRemove.Remove(petState.InstanceId);
            EnsurePetEntity(petState);
            ApplyPetPlacementState(petState, animateMovement);
        }

        for (int i = 0; i < _petInstanceIdsToRemove.Count; i++)
        {
            HidePetEntity(_petInstanceIdsToRemove[i]);
        }

        _petInstanceIdsToRemove.Clear();
    }

    /// <summary>
    /// 确保桌位实体已创建并提交显示请求。
    /// </summary>
    private void EnsureTableEntities()
    {
        for (int i = 0; i < _tableEntityIds.Length; i++)
        {
            int entityId = _tableEntityIds[i];
            if (IsEntityLoaded(entityId) || IsEntityLoading(entityId))
            {
                continue;
            }

            entityId = AcquireEntityId();
            if (entityId <= 0)
            {
                continue;
            }

            _tableEntityIds[i] = entityId;
            GameEntry.Entity.ShowEntity<TableEntityLogic>(
                entityId,
                EntityDefine.TableEntity,
                EntityDefine.TableGroup,
                new TableEntityData(i, _currentMarkerSnapshot.TableWorldPositions[i]));
        }
    }

    /// <summary>
    /// 确保果园实体已创建并提交显示请求。
    /// </summary>
    private void EnsureOrchardEntities()
    {
        for (int i = 0; i < _orchardEntityIds.Length; i++)
        {
            int entityId = _orchardEntityIds[i];
            if (IsEntityLoaded(entityId) || IsEntityLoading(entityId))
            {
                continue;
            }

            entityId = AcquireEntityId();
            if (entityId <= 0)
            {
                continue;
            }

            _orchardEntityIds[i] = entityId;
            GameEntry.Entity.ShowEntity<OrchardEntityLogic>(
                entityId,
                EntityDefine.OrchardEntity,
                EntityDefine.OrchardGroup,
                BuildOrchardEntityData(i));
        }
    }

    /// <summary>
    /// 确保指定孵化槽的孵化器实体存在。
    /// </summary>
    private void EnsureIncubatorEntity(int slotIndex)
    {
        int entityId = _incubatorEntityIds[slotIndex];
        if (IsEntityLoaded(entityId) || IsEntityLoading(entityId))
        {
            return;
        }

        entityId = AcquireEntityId();
        if (entityId <= 0)
        {
            return;
        }

        _incubatorEntityIds[slotIndex] = entityId;
        GameEntry.Entity.ShowEntity<IncubatorEntityLogic>(
            entityId,
            EntityDefine.IncubatorEntity,
            EntityDefine.IncubatorGroup,
            BuildIncubatorEntityData(slotIndex));
    }

    /// <summary>
    /// 确保指定孵化槽的蛋实体存在。
    /// </summary>
    private void EnsureEggEntity(int slotIndex, EggHatchSlotState slotState)
    {
        int entityId = _eggEntityIds[slotIndex];
        if (IsEntityLoaded(entityId) || IsEntityLoading(entityId))
        {
            return;
        }

        entityId = AcquireEntityId();
        if (entityId <= 0)
        {
            return;
        }

        _eggEntityIds[slotIndex] = entityId;
        GameEntry.Entity.ShowEntity<EggEntityLogic>(
            entityId,
            EntityDefine.EggEntity,
            EntityDefine.EggGroup,
            BuildEggEntityData(slotIndex, slotState));
    }

    /// <summary>
    /// 确保指定宠物对应的实体存在。
    /// </summary>
    private void EnsurePetEntity(PetRuntimeState petState)
    {
        if (petState == null)
        {
            return;
        }

        if (_petEntityIdsByPetInstanceId.TryGetValue(petState.InstanceId, out int entityId)
            && (IsEntityLoaded(entityId) || IsEntityLoading(entityId)))
        {
            return;
        }

        entityId = AcquireEntityId();
        if (entityId <= 0)
        {
            return;
        }

        _petEntityIdsByPetInstanceId[petState.InstanceId] = entityId;
        GameEntry.Entity.ShowEntity<PetEntityLogic>(
            entityId,
            EntityDefine.PetEntity,
            EntityDefine.PetGroup,
            BuildPetEntityData(petState));
    }

    /// <summary>
    /// 更新已加载桌位实体的位置。
    /// </summary>
    private void UpdateLoadedTableEntityPositions()
    {
        for (int i = 0; i < _tableEntityIds.Length; i++)
        {
            if (!TryGetEntityLogic(_tableEntityIds[i], out TableEntityLogic tableEntityLogic))
            {
                continue;
            }

            tableEntityLogic.SetWorldPosition(_currentMarkerSnapshot.TableWorldPositions[i]);
        }
    }

    /// <summary>
    /// 更新已加载果园实体的位置。
    /// </summary>
    private void UpdateLoadedOrchardEntityPositions()
    {
        for (int i = 0; i < _orchardEntityIds.Length; i++)
        {
            if (!TryGetEntityLogic(_orchardEntityIds[i], out OrchardEntityLogic orchardEntityLogic))
            {
                continue;
            }

            orchardEntityLogic.SetWorldPosition(_currentMarkerSnapshot.OrchardWorldPositions[i]);
        }
    }

    /// <summary>
    /// 更新已加载孵化器实体的位置。
    /// </summary>
    private void UpdateLoadedIncubatorEntityPositions()
    {
        for (int i = 0; i < _incubatorEntityIds.Length; i++)
        {
            if (!TryGetEntityLogic(_incubatorEntityIds[i], out IncubatorEntityLogic incubatorEntityLogic))
            {
                continue;
            }

            incubatorEntityLogic.SetWorldPosition(_currentMarkerSnapshot.HatchSlotWorldPositions[i]);
        }
    }

    /// <summary>
    /// 将最新蛋数据应用到已加载的实体逻辑。
    /// </summary>
    private void ApplyEggDataToLoadedEntity(int slotIndex, EggHatchSlotState slotState)
    {
        if (!TryGetEntityLogic(_eggEntityIds[slotIndex], out EggEntityLogic eggEntityLogic))
        {
            return;
        }

        eggEntityLogic.ApplyData(BuildEggEntityData(slotIndex, slotState));
    }

    /// <summary>
    /// 根据宠物站位状态更新实体位置、挂接关系和动画。
    /// </summary>
    private void ApplyPetPlacementState(PetRuntimeState petState, bool animateMovement)
    {
        if (petState == null || !_petEntityIdsByPetInstanceId.TryGetValue(petState.InstanceId, out int entityId))
        {
            return;
        }

        if (!TryGetEntityLogic(entityId, out PetEntityLogic petEntityLogic))
        {
            if (petState.PlacementType == PetPlacementType.DiningSeat)
            {
                RegisterPendingDiningPet(petState.InstanceId, animateMovement || petState.PendingSpawnHatchSlotIndex >= 0 || petState.PendingPromoteToDining);
            }

            return;
        }

        bool shouldAnimateMovement = animateMovement || petState.PendingSpawnHatchSlotIndex >= 0 || petState.PendingPromoteToDining;

        Entity parentEntity = GameEntry.Entity.GetParentEntity(entityId);
        if (petState.PlacementType == PetPlacementType.Queue)
        {
            _pendingDiningPetAnimations.Remove(petState.InstanceId);
            if (parentEntity != null)
            {
                GameEntry.Entity.DetachEntity(entityId);
            }

            if (!shouldAnimateMovement && petEntityLogic.IsMoving)
            {
                return;
            }

            if (TryGetQueueTargetWorldPosition(petState, out Vector3 queueWorldPosition))
            {
                if (shouldAnimateMovement)
                {
                    petEntityLogic.MoveToWorldPosition(queueWorldPosition, petEntityLogic.PlayIdleAnimation);
                    petState.PendingSpawnHatchSlotIndex = -1;
                }
                else
                {
                    petEntityLogic.SnapToWorldPosition(queueWorldPosition);
                    petEntityLogic.PlayIdleAnimation();
                }
            }
            return;
        }

        if (petState.PlacementType == PetPlacementType.PlayArea)
        {
            _pendingDiningPetAnimations.Remove(petState.InstanceId);
            if (parentEntity != null)
            {
                GameEntry.Entity.DetachEntity(entityId);
            }

            if (!TryGetPlayAreaTargetWorldPosition(petState, out Vector3 playAreaWorldPosition))
            {
                petEntityLogic.PlayIdleAnimation();
                return;
            }

            if (shouldAnimateMovement)
            {
                petEntityLogic.MoveToWorldPosition(
                    playAreaWorldPosition,
                    () => CompletePlayAreaArrival(petState.InstanceId));
            }
            else if (!petEntityLogic.IsMoving)
            {
                petEntityLogic.SnapToWorldPosition(playAreaWorldPosition);
                CompletePlayAreaArrival(petState.InstanceId);
            }

            return;
        }

        if (petState.PlacementType == PetPlacementType.Leaving)
        {
            _pendingDiningPetAnimations.Remove(petState.InstanceId);
            if (parentEntity != null)
            {
                GameEntry.Entity.DetachEntity(entityId);
            }

            if (!shouldAnimateMovement && petEntityLogic.IsMoving)
            {
                return;
            }

            Vector3 leavingWorldPosition = GetLeavingTargetWorldPosition(petState, petEntityLogic);
            petEntityLogic.MoveToWorldPosition(
                leavingWorldPosition,
                () => CompletePetLeaving(petState.InstanceId));
            return;
        }

        if (!animateMovement && petEntityLogic.IsMoving)
        {
            return;
        }

        int tableIndex = petState.SlotIndex;
        if (tableIndex < 0 || tableIndex >= _tableEntityIds.Length)
        {
            return;
        }

        int tableEntityId = _tableEntityIds[tableIndex];
        if (!IsEntityLoaded(tableEntityId))
        {
            RegisterPendingDiningPet(petState.InstanceId, shouldAnimateMovement);
            return;
        }

        if (!TryGetEntityLogic(tableEntityId, out TableEntityLogic tableEntityLogic))
        {
            RegisterPendingDiningPet(petState.InstanceId, shouldAnimateMovement);
            return;
        }

        if (!shouldAnimateMovement)
        {
            if (parentEntity == null || parentEntity.Id != tableEntityId)
            {
                GameEntry.Entity.AttachEntity(entityId, tableEntityId, TableEntityLogic.DiningAnchorTransformPath, null);
            }

            petEntityLogic.PlayIdleAnimation();
            _pendingDiningPetAnimations.Remove(petState.InstanceId);
            return;
        }

        if (parentEntity != null)
        {
            GameEntry.Entity.DetachEntity(entityId);
        }

        Vector3 diningWorldPosition = GetDiningTargetWorldPosition(tableEntityLogic, tableIndex);
        petEntityLogic.MoveToWorldPosition(
            diningWorldPosition,
            () => CompleteDiningAttachment(petState.InstanceId, entityId, tableEntityId));
        petState.PendingSpawnHatchSlotIndex = -1;
        petState.PendingPromoteToDining = false;
        _pendingDiningPetAnimations.Remove(petState.InstanceId);
    }

    /// <summary>
    /// 重试处理等待桌位实体完成加载的入座宠物。
    /// </summary>
    private void ProcessPendingDiningPets()
    {
        if (_pendingDiningPetAnimations.Count == 0)
        {
            return;
        }

        _pendingDiningPetBuffer.Clear();
        foreach (KeyValuePair<int, bool> pair in _pendingDiningPetAnimations)
        {
            _pendingDiningPetBuffer.Add(pair.Key);
        }

        for (int i = 0; i < _pendingDiningPetBuffer.Count; i++)
        {
            int petInstanceId = _pendingDiningPetBuffer[i];
            PetRuntimeState petState = GetPetStateByInstanceId(petInstanceId);
            if (petState == null)
            {
                _pendingDiningPetAnimations.Remove(petInstanceId);
                continue;
            }

            bool animateMovement = _pendingDiningPetAnimations.TryGetValue(petInstanceId, out bool pendingAnimateMovement)
                && pendingAnimateMovement;
            ApplyPetPlacementState(petState, animateMovement);
        }

        _pendingDiningPetBuffer.Clear();
    }

    /// <summary>
    /// 隐藏指定孵化槽的孵化器实体。
    /// </summary>
    private void HideIncubatorEntity(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _incubatorEntityIds.Length)
        {
            return;
        }

        int entityId = _incubatorEntityIds[slotIndex];
        if (entityId <= 0)
        {
            return;
        }

        _incubatorEntityIds[slotIndex] = 0;
        HideEntityIfNeeded(entityId);
    }

    /// <summary>
    /// 隐藏指定孵化槽的蛋实体。
    /// </summary>
    private void HideEggEntity(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _eggEntityIds.Length)
        {
            return;
        }

        int entityId = _eggEntityIds[slotIndex];
        if (entityId <= 0)
        {
            return;
        }

        _eggEntityIds[slotIndex] = 0;
        HideEntityIfNeeded(entityId);
    }

    /// <summary>
    /// 隐藏指定宠物对应的实体。
    /// </summary>
    private void HidePetEntity(int petInstanceId)
    {
        if (!_petEntityIdsByPetInstanceId.TryGetValue(petInstanceId, out int entityId))
        {
            return;
        }

        _petEntityIdsByPetInstanceId.Remove(petInstanceId);
        _pendingDiningPetAnimations.Remove(petInstanceId);
        HideEntityIfNeeded(entityId);
    }

    /// <summary>
    /// 隐藏当前所有宠物实体。
    /// </summary>
    private void HideAllPetEntities()
    {
        _petInstanceIdsToRemove.Clear();
        foreach (KeyValuePair<int, int> pair in _petEntityIdsByPetInstanceId)
        {
            _petInstanceIdsToRemove.Add(pair.Key);
        }

        for (int i = 0; i < _petInstanceIdsToRemove.Count; i++)
        {
            HidePetEntity(_petInstanceIdsToRemove[i]);
        }

        _petInstanceIdsToRemove.Clear();
    }

    /// <summary>
    /// 如果实体仍存在或仍在加载，则发起隐藏；否则直接回收实体 Id。
    /// </summary>
    private void HideEntityIfNeeded(int entityId)
    {
        if (entityId <= 0 || GameEntry.Entity == null)
        {
            return;
        }

        if (!GameEntry.Entity.HasEntity(entityId) && !GameEntry.Entity.IsLoadingEntity(entityId))
        {
            GameEntry.EntityIdPool?.Release(entityId);
            return;
        }

        GameEntry.Entity.HideEntity(entityId);
    }

    /// <summary>
    /// 构建孵化器实体显示数据。
    /// </summary>
    private IncubatorEntityData BuildIncubatorEntityData(int slotIndex)
    {
        return new IncubatorEntityData(
            slotIndex,
            _currentMarkerSnapshot.HatchSlotWorldPositions[slotIndex]);
    }

    /// <summary>
    /// 构建蛋实体显示数据。
    /// </summary>
    private EggEntityData BuildEggEntityData(int slotIndex, EggHatchSlotState slotState)
    {
        return new EggEntityData(
            slotIndex,
            slotState != null ? slotState.EggCode : null,
            _currentMarkerSnapshot.HatchSlotWorldPositions[slotIndex]);
    }

    /// <summary>
    /// 构建果园实体显示数据。
    /// </summary>
    private OrchardEntityData BuildOrchardEntityData(int orchardIndex)
    {
        return new OrchardEntityData(
            orchardIndex,
            _currentMarkerSnapshot.OrchardWorldPositions[orchardIndex]);
    }

    /// <summary>
    /// 构建餐桌水果实体显示数据。
    /// </summary>
    /// <param name="tableIndex">桌位索引。</param>
    /// <param name="fruitCode">水果 Code。</param>
    /// <param name="tableEntityLogic">已加载的桌位实体逻辑。</param>
    /// <returns>水果实体显示数据。</returns>
    private FruitEntityData BuildFruitEntityData(int tableIndex, string fruitCode, TableEntityLogic tableEntityLogic)
    {
        Vector3 worldPosition = Vector3.zero;
        if (tableEntityLogic != null && tableEntityLogic.FoodAnchor != null)
        {
            worldPosition = tableEntityLogic.FoodAnchor.position;
        }
        else if (_currentMarkerSnapshot != null
            && _currentMarkerSnapshot.TableWorldPositions != null
            && tableIndex >= 0
            && tableIndex < _currentMarkerSnapshot.TableWorldPositions.Length)
        {
            worldPosition = _currentMarkerSnapshot.TableWorldPositions[tableIndex];
        }

        return new FruitEntityData(tableIndex, fruitCode, worldPosition);
    }

    /// <summary>
    /// 构建宠物实体显示数据。
    /// </summary>
    private PetEntityData BuildPetEntityData(PetRuntimeState petState)
    {
        Vector3 worldPosition = Vector3.zero;
        Vector3 initialWorldPosition = Vector3.zero;
        int tableIndex = -1;
        bool useInitialWorldPositionOnShow = false;
        if (petState != null)
        {
            if (petState.PlacementType == PetPlacementType.Queue
                && petState.SlotIndex >= 0
                && petState.SlotIndex < _currentMarkerSnapshot.QueueWorldPositions.Length)
            {
                worldPosition = _currentMarkerSnapshot.QueueWorldPositions[petState.SlotIndex];
            }
            else if (petState.PlacementType == PetPlacementType.DiningSeat
                && petState.SlotIndex >= 0
                && petState.SlotIndex < _currentMarkerSnapshot.TableWorldPositions.Length)
            {
                worldPosition = _currentMarkerSnapshot.TableWorldPositions[petState.SlotIndex];
                tableIndex = petState.SlotIndex;
            }
            else if (petState.PlacementType == PetPlacementType.PlayArea
                && TryGetPlayAreaTargetWorldPosition(petState, out Vector3 playAreaWorldPosition))
            {
                worldPosition = playAreaWorldPosition;
            }
            else if (petState.PlacementType == PetPlacementType.Leaving)
            {
                if (TryGetPlayAreaTargetWorldPosition(petState, out Vector3 leavingPlayAreaWorldPosition))
                {
                    worldPosition = leavingPlayAreaWorldPosition;
                }
                else if (petState.SlotIndex >= 0
                    && _currentMarkerSnapshot != null
                    && _currentMarkerSnapshot.TableWorldPositions != null
                    && petState.SlotIndex < _currentMarkerSnapshot.TableWorldPositions.Length)
                {
                    worldPosition = _currentMarkerSnapshot.TableWorldPositions[petState.SlotIndex];
                }
            }

            if (petState.PendingSpawnHatchSlotIndex >= 0
                && petState.PendingSpawnHatchSlotIndex < _currentMarkerSnapshot.HatchSlotWorldPositions.Length)
            {
                initialWorldPosition = _currentMarkerSnapshot.HatchSlotWorldPositions[petState.PendingSpawnHatchSlotIndex];
                useInitialWorldPositionOnShow = true;
            }
            else
            {
                initialWorldPosition = worldPosition;
            }
        }

        return new PetEntityData(
            petState != null ? petState.InstanceId : 0,
            petState != null ? petState.PetCode : null,
            petState != null ? petState.PlacementType : PetPlacementType.None,
            petState != null ? petState.SlotIndex : -1,
            tableIndex,
            worldPosition,
            initialWorldPosition,
            useInitialWorldPositionOnShow);
    }

    /// <summary>
    /// 按实例 Id 查找宠物运行时状态。
    /// </summary>
    private PetRuntimeState GetPetStateByInstanceId(int petInstanceId)
    {
        PetPlacementModule petPlacement = GameEntry.PetPlacement;
        if (petPlacement == null)
        {
            return null;
        }

        return petPlacement.GetPetStateByInstanceId(petInstanceId);
    }

    /// <summary>
    /// 从实体 Id 池申请一个新的实体实例 Id。
    /// </summary>
    private int AcquireEntityId()
    {
        if (GameEntry.EntityIdPool == null)
        {
            Log.Warning("PlayfieldEntityModule 无法申请实体 Id，EntityIdPoolComponent 缺失。");
            return 0;
        }

        return GameEntry.EntityIdPool.Acquire();
    }

    /// <summary>
    /// 判断实体是否已经加载完成。
    /// </summary>
    private bool IsEntityLoaded(int entityId)
    {
        return entityId > 0 && GameEntry.Entity != null && GameEntry.Entity.HasEntity(entityId);
    }

    /// <summary>
    /// 判断实体是否仍处于加载中。
    /// </summary>
    private bool IsEntityLoading(int entityId)
    {
        return entityId > 0 && GameEntry.Entity != null && GameEntry.Entity.IsLoadingEntity(entityId);
    }

    /// <summary>
    /// 尝试获取指定实体对应的逻辑脚本。
    /// </summary>
    private bool TryGetEntityLogic<T>(int entityId, out T entityLogic) where T : EntityLogic
    {
        entityLogic = null;
        if (!IsEntityLoaded(entityId))
        {
            return false;
        }

        Entity entity = GameEntry.Entity.GetEntity(entityId);
        entityLogic = entity != null ? entity.Logic as T : null;
        return entityLogic != null;
    }

    /// <summary>
    /// 记录一个等待桌子实体完成加载的入座宠物。
    /// </summary>
    private void RegisterPendingDiningPet(int petInstanceId, bool animateMovement)
    {
        if (_pendingDiningPetAnimations.TryGetValue(petInstanceId, out bool currentAnimateMovement))
        {
            _pendingDiningPetAnimations[petInstanceId] = currentAnimateMovement || animateMovement;
            return;
        }

        _pendingDiningPetAnimations.Add(petInstanceId, animateMovement);
    }

    /// <summary>
    /// 获取排队位的目标世界坐标。
    /// </summary>
    private bool TryGetQueueTargetWorldPosition(PetRuntimeState petState, out Vector3 worldPosition)
    {
        if (petState != null
            && petState.SlotIndex >= 0
            && _currentMarkerSnapshot != null
            && _currentMarkerSnapshot.QueueWorldPositions != null
            && petState.SlotIndex < _currentMarkerSnapshot.QueueWorldPositions.Length)
        {
            worldPosition = _currentMarkerSnapshot.QueueWorldPositions[petState.SlotIndex];
            return true;
        }

        worldPosition = Vector3.zero;
        return false;
    }

    /// <summary>
    /// 获取玩耍区的目标世界坐标。
    /// </summary>
    /// <param name="petState">宠物运行时状态。</param>
    /// <param name="worldPosition">命中的玩耍区世界坐标。</param>
    /// <returns>是否命中有效的玩耍区目标点。</returns>
    private bool TryGetPlayAreaTargetWorldPosition(PetRuntimeState petState, out Vector3 worldPosition)
    {
        if (petState != null
            && petState.PlayAreaIndex >= 0
            && _currentMarkerSnapshot != null
            && _currentMarkerSnapshot.PlayAreaWorldRegions != null
            && petState.PlayAreaIndex < _currentMarkerSnapshot.PlayAreaWorldRegions.Length)
        {
            PlayAreaWorldRegion region = _currentMarkerSnapshot.PlayAreaWorldRegions[petState.PlayAreaIndex];
            if (region.IsValid)
            {
                worldPosition = region.Evaluate(petState.PlayAreaRandomPosition01);
                return true;
            }
        }

        worldPosition = Vector3.zero;
        return false;
    }

    /// <summary>
    /// 获取入座移动时使用的目标世界坐标。
    /// </summary>
    private Vector3 GetDiningTargetWorldPosition(TableEntityLogic tableEntityLogic, int tableIndex)
    {
        if (tableEntityLogic != null && tableEntityLogic.DiningAnchor != null)
        {
            return tableEntityLogic.DiningAnchor.position;
        }

        if (_currentMarkerSnapshot != null
            && _currentMarkerSnapshot.TableWorldPositions != null
            && tableIndex >= 0
            && tableIndex < _currentMarkerSnapshot.TableWorldPositions.Length)
        {
            return _currentMarkerSnapshot.TableWorldPositions[tableIndex];
        }

        return Vector3.zero;
    }

    /// <summary>
    /// 获取宠物离场时的目标世界坐标。
    /// 离场逻辑只修改 Y 坐标到 20，保持当前 X/Z 不变。
    /// </summary>
    /// <param name="petState">宠物运行时状态。</param>
    /// <param name="petEntityLogic">当前已加载的宠物实体逻辑。</param>
    /// <returns>离场目标世界坐标。</returns>
    private Vector3 GetLeavingTargetWorldPosition(PetRuntimeState petState, PetEntityLogic petEntityLogic)
    {
        Vector3 leavingWorldPosition = Vector3.zero;
        if (petEntityLogic != null)
        {
            leavingWorldPosition = petEntityLogic.CachedTransform.position;
        }
        else if (TryGetPlayAreaTargetWorldPosition(petState, out Vector3 playAreaWorldPosition))
        {
            leavingWorldPosition = playAreaWorldPosition;
        }
        else if (_currentMarkerSnapshot != null
            && _currentMarkerSnapshot.TableWorldPositions != null
            && petState != null
            && petState.SlotIndex >= 0
            && petState.SlotIndex < _currentMarkerSnapshot.TableWorldPositions.Length)
        {
            leavingWorldPosition = _currentMarkerSnapshot.TableWorldPositions[petState.SlotIndex];
        }

        leavingWorldPosition.y = LeavingTargetY;
        return leavingWorldPosition;
    }

    /// <summary>
    /// 宠物移动到桌位后完成挂接。
    /// </summary>
    private void CompleteDiningAttachment(int petInstanceId, int entityId, int tableEntityId)
    {
        if (!_petEntityIdsByPetInstanceId.TryGetValue(petInstanceId, out int currentEntityId) || currentEntityId != entityId)
        {
            return;
        }

        if (!TryGetEntityLogic(entityId, out PetEntityLogic petEntityLogic))
        {
            return;
        }

        if (!IsEntityLoaded(tableEntityId))
        {
            RegisterPendingDiningPet(petInstanceId, false);
            return;
        }

        Entity parentEntity = GameEntry.Entity.GetParentEntity(entityId);
        if (parentEntity == null || parentEntity.Id != tableEntityId)
        {
            GameEntry.Entity.AttachEntity(entityId, tableEntityId, TableEntityLogic.DiningAnchorTransformPath, null);
        }

        petEntityLogic.PlayIdleAnimation();
    }

    /// <summary>
    /// 宠物到达 PlayArea 后开始真正的停留计时。
    /// 只有从这一刻起才开始计算 5 秒游玩时间。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    private void CompletePlayAreaArrival(int petInstanceId)
    {
        if (!TryGetPetEntityLogic(petInstanceId, out PetEntityLogic petEntityLogic) || petEntityLogic == null)
        {
            return;
        }

        petEntityLogic.PlayIdleAnimation();
        GameEntry.PetPlacement?.BeginPlayAreaStay(petInstanceId);
    }

    /// <summary>
    /// 宠物离场移动完成后的回调。
    /// 真正的运行时状态删除交给 PetPlacementModule 处理。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    private void CompletePetLeaving(int petInstanceId)
    {
        GameEntry.PetPlacement?.RemovePet(petInstanceId);
    }

    /// <summary>
    /// 确保实体显示成功/失败事件已完成订阅。
    /// </summary>
    private void EnsureEntityEventSubscription()
    {
        if (_isEntityEventSubscribed || GameEntry.Event == null)
        {
            return;
        }

        GameEntry.Event.Subscribe(ShowEntitySuccessEventArgs.EventId, OnShowEntitySuccess);
        GameEntry.Event.Subscribe(ShowEntityFailureEventArgs.EventId, OnShowEntityFailure);
        _isEntityEventSubscribed = true;
    }

    /// <summary>
    /// 实体显示成功后，按业务类型补做一次状态校准。
    /// </summary>
    private void OnShowEntitySuccess(object sender, GameEventArgs e)
    {
        ShowEntitySuccessEventArgs ne = e as ShowEntitySuccessEventArgs;
        if (ne == null || !HasValidMarkerSnapshot)
        {
            return;
        }

        if (ne.UserData is TableEntityData tableEntityData)
        {
            if (tableEntityData.TableIndex >= 0 && tableEntityData.TableIndex < _tableEntityIds.Length)
            {
                _tableEntityIds[tableEntityData.TableIndex] = ne.Entity.Id;
                if (TryGetEntityLogic(ne.Entity.Id, out TableEntityLogic tableEntityLogic))
                {
                    tableEntityLogic.SetWorldPosition(_currentMarkerSnapshot.TableWorldPositions[tableEntityData.TableIndex]);
                }
            }

            ProcessPendingDiningPets();
            return;
        }

        if (ne.UserData is FruitEntityData fruitEntityData)
        {
            // 果树水果：挂接到果树并播放生长动画
            if (fruitEntityData.IsOrchardFruit)
            {
                int orchardIndex = fruitEntityData.OrchardIndex;
                if (orchardIndex < 0
                    || orchardIndex >= _orchardFruitEntityIds.Length
                    || _orchardFruitEntityIds[orchardIndex] != ne.Entity.Id)
                {
                    HideEntityIfNeeded(ne.Entity.Id);
                    return;
                }

                int orchardEntityId = _orchardEntityIds[orchardIndex];
                if (IsEntityLoaded(orchardEntityId))
                {
                    GameEntry.Entity.AttachEntity(ne.Entity.Id, orchardEntityId);
                }

                // 取出并执行生长动画
                if (_pendingOrchardGrow.TryGetValue(ne.Entity.Id, out OrchardGrowRequest growRequest))
                {
                    _pendingOrchardGrow.Remove(ne.Entity.Id);
                    if (TryGetEntityLogic(ne.Entity.Id, out FruitEntityLogic orchardFruitLogic))
                    {
                        orchardFruitLogic.PlayGrowAnimation(growRequest.Duration, 0.3f, growRequest.OnComplete);
                    }
                    else
                    {
                        growRequest.OnComplete?.Invoke();
                    }
                }

                return;
            }

            // 餐桌水果：挂接到桌子食物锚点
            if (fruitEntityData.TableIndex < 0
                || fruitEntityData.TableIndex >= _diningFruitEntityIds.Length
                || _diningFruitEntityIds[fruitEntityData.TableIndex] != ne.Entity.Id)
            {
                HideEntityIfNeeded(ne.Entity.Id);
                return;
            }

            int tableEntityId = _tableEntityIds[fruitEntityData.TableIndex];
            if (!IsEntityLoaded(tableEntityId) || !TryGetEntityLogic(tableEntityId, out TableEntityLogic tableEntityLogic))
            {
                HideDiningFruit(fruitEntityData.TableIndex);
                return;
            }

            if (TryGetEntityLogic(ne.Entity.Id, out FruitEntityLogic fruitEntityLogic))
            {
                fruitEntityLogic.ApplyData(BuildFruitEntityData(fruitEntityData.TableIndex, fruitEntityData.FruitCode, tableEntityLogic));
            }

            Entity parentEntity = GameEntry.Entity.GetParentEntity(ne.Entity.Id);
            if (parentEntity == null || parentEntity.Id != tableEntityId)
            {
                GameEntry.Entity.AttachEntity(ne.Entity.Id, tableEntityId, TableEntityLogic.FoodAnchorTransformPath, null);
            }

            return;
        }

        if (ne.UserData is OrchardEntityData orchardEntityData)
        {
            if (orchardEntityData.OrchardIndex >= 0 && orchardEntityData.OrchardIndex < _orchardEntityIds.Length)
            {
                _orchardEntityIds[orchardEntityData.OrchardIndex] = ne.Entity.Id;
                if (TryGetEntityLogic(ne.Entity.Id, out OrchardEntityLogic orchardEntityLogic))
                {
                    orchardEntityLogic.SetWorldPosition(_currentMarkerSnapshot.OrchardWorldPositions[orchardEntityData.OrchardIndex]);
                }
            }

            return;
        }

        if (ne.UserData is IncubatorEntityData incubatorEntityData)
        {
            if (incubatorEntityData.SlotIndex < 0 || incubatorEntityData.SlotIndex >= _incubatorEntityIds.Length)
            {
                HideEntityIfNeeded(ne.Entity.Id);
                return;
            }

            EggHatchSlotState slotState = GameEntry.EggHatch != null ? GameEntry.EggHatch.GetSlotState(incubatorEntityData.SlotIndex) : null;
            if (slotState == null || _incubatorEntityIds[incubatorEntityData.SlotIndex] != ne.Entity.Id)
            {
                HideEntityIfNeeded(ne.Entity.Id);
                return;
            }

            if (TryGetEntityLogic(ne.Entity.Id, out IncubatorEntityLogic incubatorEntityLogic))
            {
                incubatorEntityLogic.ApplyData(BuildIncubatorEntityData(incubatorEntityData.SlotIndex));
            }

            return;
        }

        if (ne.UserData is EggEntityData eggEntityData)
        {
            if (eggEntityData.SlotIndex < 0 || eggEntityData.SlotIndex >= _eggEntityIds.Length)
            {
                HideEntityIfNeeded(ne.Entity.Id);
                return;
            }

            EggHatchSlotState slotState = GameEntry.EggHatch != null ? GameEntry.EggHatch.GetSlotState(eggEntityData.SlotIndex) : null;
            if (slotState == null || !slotState.IsOccupied || _eggEntityIds[eggEntityData.SlotIndex] != ne.Entity.Id)
            {
                HideEntityIfNeeded(ne.Entity.Id);
                return;
            }

            ApplyEggDataToLoadedEntity(eggEntityData.SlotIndex, slotState);
            return;
        }

        if (ne.UserData is PetEntityData petEntityData)
        {
            if (!_petEntityIdsByPetInstanceId.TryGetValue(petEntityData.PetInstanceId, out int entityId)
                || entityId != ne.Entity.Id)
            {
                HideEntityIfNeeded(ne.Entity.Id);
                return;
            }

            PetRuntimeState petState = GetPetStateByInstanceId(petEntityData.PetInstanceId);
            if (petState == null)
            {
                HidePetEntity(petEntityData.PetInstanceId);
                return;
            }

            ApplyPetPlacementState(petState, petState.PendingSpawnHatchSlotIndex >= 0 || petState.PendingPromoteToDining);
            ProcessPendingDiningPets();
        }
    }

    /// <summary>
    /// 实体显示失败后，清理缓存并回收实体 Id。
    /// </summary>
    private void OnShowEntityFailure(object sender, GameEventArgs e)
    {
        ShowEntityFailureEventArgs ne = e as ShowEntityFailureEventArgs;
        if (ne == null)
        {
            return;
        }

        if (ne.UserData is TableEntityData tableEntityData)
        {
            if (tableEntityData.TableIndex >= 0 && tableEntityData.TableIndex < _tableEntityIds.Length && _tableEntityIds[tableEntityData.TableIndex] == ne.EntityId)
            {
                _tableEntityIds[tableEntityData.TableIndex] = 0;
            }
        }
        else if (ne.UserData is IncubatorEntityData incubatorEntityData)
        {
            if (incubatorEntityData.SlotIndex >= 0
                && incubatorEntityData.SlotIndex < _incubatorEntityIds.Length
                && _incubatorEntityIds[incubatorEntityData.SlotIndex] == ne.EntityId)
            {
                _incubatorEntityIds[incubatorEntityData.SlotIndex] = 0;
            }
        }
        else if (ne.UserData is OrchardEntityData orchardEntityData)
        {
            if (orchardEntityData.OrchardIndex >= 0 && orchardEntityData.OrchardIndex < _orchardEntityIds.Length && _orchardEntityIds[orchardEntityData.OrchardIndex] == ne.EntityId)
            {
                _orchardEntityIds[orchardEntityData.OrchardIndex] = 0;
            }
        }
        else if (ne.UserData is EggEntityData eggEntityData)
        {
            if (eggEntityData.SlotIndex >= 0 && eggEntityData.SlotIndex < _eggEntityIds.Length && _eggEntityIds[eggEntityData.SlotIndex] == ne.EntityId)
            {
                _eggEntityIds[eggEntityData.SlotIndex] = 0;
            }
        }
        else if (ne.UserData is FruitEntityData fruitEntityData)
        {
            if (fruitEntityData.IsOrchardFruit)
            {
                int orchardIndex = fruitEntityData.OrchardIndex;
                if (orchardIndex >= 0
                    && orchardIndex < _orchardFruitEntityIds.Length
                    && _orchardFruitEntityIds[orchardIndex] == ne.EntityId)
                {
                    _orchardFruitEntityIds[orchardIndex] = 0;
                }

                _pendingOrchardGrow.Remove(ne.EntityId);
            }
            else
            {
                if (fruitEntityData.TableIndex >= 0
                    && fruitEntityData.TableIndex < _diningFruitEntityIds.Length
                    && _diningFruitEntityIds[fruitEntityData.TableIndex] == ne.EntityId)
                {
                    _diningFruitEntityIds[fruitEntityData.TableIndex] = 0;
                }
            }
        }
        else if (ne.UserData is PetEntityData petEntityData)
        {
            if (_petEntityIdsByPetInstanceId.TryGetValue(petEntityData.PetInstanceId, out int entityId) && entityId == ne.EntityId)
            {
                _petEntityIdsByPetInstanceId.Remove(petEntityData.PetInstanceId);
                _pendingDiningPetAnimations.Remove(petEntityData.PetInstanceId);
            }
        }

        GameEntry.EntityIdPool?.Release(ne.EntityId);
        Log.Warning(
            "PlayfieldEntityModule 显示实体失败。Id='{0}'，Asset='{1}'，Group='{2}'，Error='{3}'。",
            ne.EntityId,
            ne.EntityAssetName,
            ne.EntityGroupName,
            ne.ErrorMessage);
    }

    /// <summary>
    /// 果树水果生长动画请求参数。
    /// </summary>
    private readonly struct OrchardGrowRequest
    {
        /// <summary>
        /// 生长动画时长（秒）。
        /// </summary>
        public readonly float Duration;

        /// <summary>
        /// 生长动画完成回调。
        /// </summary>
        public readonly System.Action OnComplete;

        public OrchardGrowRequest(float duration, System.Action onComplete)
        {
            Duration = duration;
            OnComplete = onComplete;
        }
    }
}
