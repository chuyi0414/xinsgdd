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
    /// 桌位总数量（包含未解锁）。
    /// 实体数组按总数量分配，未解锁槽位显示 Level 0 占位精灵。
    /// </summary>
    public int TableCount => GameEntry.Fruits?.TotalDiningSeatCount ?? PlayerRuntimeModule.DietArchitectureCountValue;

    /// <summary>
    /// 果园位总数量（包含未解锁）。
    /// 实体数组按总数量分配，未解锁槽位显示 Level 0 占位精灵。
    /// </summary>
    public int OrchardCount => GameEntry.Fruits?.TotalOrchardSlotCount ?? PlayerRuntimeModule.FruiterArchitectureCountValue;

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
            tableCount = PlayerRuntimeModule.DietArchitectureCountValue;
        }

        if (orchardCount <= 0)
        {
            orchardCount = PlayerRuntimeModule.FruiterArchitectureCountValue;
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
    /// 建筑状态变化后刷新对应槽位的实体。
    /// 只刷新发生变化的那个槽位，避免全量遍历。
    /// </summary>
    /// <param name="category">建筑类别。</param>
    /// <param name="slotIndex">1 基索引的建筑槽位。</param>
    public void NotifyArchitectureSlotChanged(PlayerRuntimeModule.ArchitectureCategory category, int slotIndex)
    {
        if (!HasValidMarkerSnapshot)
        {
            return;
        }

        // slotIndex 为 1 基，数组下标为 0 基。
        int arrayIndex = slotIndex - 1;

        switch (category)
        {
            case PlayerRuntimeModule.ArchitectureCategory.Hatch:
                // 孵化器：确保实体存在并重新应用数据。
                EnsureIncubatorEntity(arrayIndex);
                ApplySingleIncubatorEntityData(arrayIndex);
                break;

            case PlayerRuntimeModule.ArchitectureCategory.Diet:
                // 餐桌：确保实体存在并重新应用数据。
                EnsureTableEntity(arrayIndex);
                ApplySingleTableEntityData(arrayIndex);
                break;

            case PlayerRuntimeModule.ArchitectureCategory.Fruiter:
                // 果园：确保实体存在并重新应用数据。
                EnsureOrchardEntity(arrayIndex);
                ApplySingleOrchardEntityData(arrayIndex);
                break;
        }
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
            // 基础参数或实体系统无效时，不允许发起桌面水果显示。
            return false;
        }

        int currentEntityId = _diningFruitEntityIds[tableIndex];
        if (IsEntityLoaded(currentEntityId) || IsEntityLoading(currentEntityId))
        {
            // 同一张桌子在任一时刻只允许存在一个水果实体，
            // 这里返回 false 的语义是“桌面显示链路未成功建立”。
            return false;
        }

        int tableEntityId = _tableEntityIds[tableIndex];
        if (!IsEntityLoaded(tableEntityId) || !TryGetEntityLogic(tableEntityId, out TableEntityLogic tableEntityLogic))
        {
            // 桌位实体自己都还没准备好时，不能把水果挂上去。
            return false;
        }

        int fruitEntityId = AcquireEntityId();
        if (fruitEntityId <= 0)
        {
            // 运行时实体 Id 申请失败，说明这次显示请求没有真正提交成功。
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
        // 先清本地记录，再隐藏实体，避免异步回调阶段继续读到旧 Id。
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
            // 果树索引非法、水果码为空、实体系统不可用时，直接拒绝进入果树生长链路。
            return false;
        }

        int currentEntityId = _orchardFruitEntityIds[orchardIndex];
        if (IsEntityLoaded(currentEntityId) || IsEntityLoading(currentEntityId))
        {
            // 同一棵果树同一时刻不允许重复挂两个水果实体。
            return false;
        }

        int orchardEntityId = _orchardEntityIds[orchardIndex];
        if (!IsEntityLoaded(orchardEntityId))
        {
            // 果树实体还未加载完成时，水果无法正确挂接到果树节点。
            return false;
        }

        int fruitEntityId = AcquireEntityId();
        if (fruitEntityId <= 0)
        {
            // 实体 Id 申请失败意味着这次果树水果显示没有真正发起。
            return false;
        }

        _orchardFruitEntityIds[orchardIndex] = fruitEntityId;

        // 果树水果使用果树世界坐标作为初始位置
        Vector3 orchardWorldPos = _currentMarkerSnapshot.OrchardWorldPositions[orchardIndex];
        FruitEntityData fruitData = new FruitEntityData(-1, fruitCode, orchardWorldPos, orchardIndex);

        // 临时存储生长参数，在 OnShowEntitySuccess 中取出执行动画
        // 这里不直接播动画，是因为实体要等真正 Show 成功后才能拿到对应逻辑对象。
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
    /// <param name="onDeliverComplete">送达完成回调。参数为 true 表示桌上水果已真正显示成功，false 表示桌面显示链路失败。</param>
    public void BeginOrchardFruitDelivery(int orchardIndex, int tableIndex, string fruitCode, float deliverDuration, System.Action<bool> onDeliverComplete)
    {
        if (orchardIndex < 0 || orchardIndex >= _orchardFruitEntityIds.Length || !HasValidMarkerSnapshot)
        {
            // Marker 快照无效时，连飞行目标都无法计算，直接按失败回传。
            onDeliverComplete?.Invoke(false);
            return;
        }

        int fruitEntityId = _orchardFruitEntityIds[orchardIndex];
        if (!TryGetEntityLogic(fruitEntityId, out FruitEntityLogic fruitEntityLogic))
        {
            // 果树水果逻辑对象已经丢失时，说明这次送达链路无法继续。
            // 这里先清理果树残留，再把失败结果回传给上层回滚状态机。
            HideOrchardFruit(orchardIndex);
            onDeliverComplete?.Invoke(false);
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
        System.Action<bool> capturedCallback = onDeliverComplete;

        fruitEntityLogic.PlayDeliverAnimation(deliverTarget, deliverDuration, () =>
        {
            // 飞到边界后隐藏果树水果
            // 这一步表示果树阶段已经结束，视觉上不应继续保留果树上的水果。
            HideOrchardFruit(capturedOrchardIndex);

            // 直接在桌子上显示水果
            // 这里的 bool 结果就是整个送达链最终要回传给点餐状态机的“真成功”定义。
            bool didShowDiningFruit = TryShowDiningFruit(capturedTableIndex, capturedFruitCode);

            capturedCallback?.Invoke(didShowDiningFruit);
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
        // 生长完成回调参数也要一起清掉，避免旧水果实体释放后仍残留脏请求。
        _pendingOrchardGrow.Remove(entityId);
        HideEntityIfNeeded(entityId);
    }

    /// <summary>
    /// 按当前孵化槽状态刷新孵化器实体。
    /// 所有槽位都显示孵化器实体：已解锁的正常显示，未解锁的显示 Level 0 占位精灵。
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
            // 无论是否解锁，都创建孵化器实体。
            // 未解锁时实体内部会自动切换为 Level 0 占位精灵。
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
                // 蛋实体正在播放结束动画时，暂不隐藏，等动画播完由实体自行通知
                int eggEntityId = _eggEntityIds[i];
                if (eggEntityId > 0 && TryGetEntityLogic(eggEntityId, out EggEntityLogic finishingEgg))
                {
                    if (finishingEgg.IsFinishing)
                    {
                        continue;
                    }

                    // 蛋实体存在且尚未开始结束动画，触发动画而非直接隐藏
                    finishingEgg.PlayFinishAnimation();
                    continue;
                }

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
    /// 所有桌位都创建实体：已解锁的正常显示，未解锁的显示 Level 0 占位精灵。
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
                BuildTableEntityData(i));
        }
    }

    /// <summary>
    /// 确保果园实体已创建并提交显示请求。
    /// 所有果园位都创建实体：已解锁的正常显示，未解锁的显示 Level 0 占位精灵。
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

        if (petState.PendingSpawnHatchSlotIndex >= 0
            && petState.PendingSpawnHatchSlotIndex < _incubatorEntityIds.Length
            && !IsEntityLoaded(_incubatorEntityIds[petState.PendingSpawnHatchSlotIndex]))
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
    /// 蛋实体结束动画播放完毕后的回调入口。
    /// 由 EggEntityLogic 在动画结束时主动调用，触发真正的隐藏流程。
    /// </summary>
    /// <param name="slotIndex">孵化槽索引。</param>
    public void NotifyEggFinishAnimationCompleted(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _eggEntityIds.Length)
        {
            return;
        }

        EggHatchSlotState slotState = GameEntry.EggHatch != null ? GameEntry.EggHatch.GetSlotState(slotIndex) : null;
        if (slotState != null && slotState.IsOccupied)
        {
            ApplyEggDataToLoadedEntity(slotIndex, slotState);
            return;
        }

        HideEggEntity(slotIndex);
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
        bool isUnlocked = IsHatchSlotUnlocked(slotIndex);
        int level = isUnlocked ? GameEntry.Fruits.GetArchitectureEntryState(PlayerRuntimeModule.ArchitectureCategory.Hatch, slotIndex + 1).Level : 0;
        return new IncubatorEntityData(
            slotIndex,
            _currentMarkerSnapshot.HatchSlotWorldPositions[slotIndex],
            isUnlocked,
            level);
    }

    /// <summary>
    /// 构建蛋实体显示数据。
    /// </summary>
    private EggEntityData BuildEggEntityData(int slotIndex, EggHatchSlotState slotState)
    {
        return new EggEntityData(
            slotIndex,
            slotState != null ? slotState.EggCode : null,
            GetHatchGenericWorldPosition(slotIndex));
    }

    /// <summary>
    /// 构建果园实体显示数据。
    /// </summary>
    private OrchardEntityData BuildOrchardEntityData(int orchardIndex)
    {
        bool isUnlocked = IsOrchardSlotUnlocked(orchardIndex);
        int level = isUnlocked ? GameEntry.Fruits.GetArchitectureEntryState(PlayerRuntimeModule.ArchitectureCategory.Fruiter, orchardIndex + 1).Level : 0;
        return new OrchardEntityData(
            orchardIndex,
            _currentMarkerSnapshot.OrchardWorldPositions[orchardIndex],
            isUnlocked,
            level);
    }

    /// <summary>
    /// 构建餐桌实体显示数据。
    /// </summary>
    private TableEntityData BuildTableEntityData(int tableIndex)
    {
        bool isUnlocked = IsTableSlotUnlocked(tableIndex);
        int level = isUnlocked ? GameEntry.Fruits.GetArchitectureEntryState(PlayerRuntimeModule.ArchitectureCategory.Diet, tableIndex + 1).Level : 0;
        return new TableEntityData(
            tableIndex,
            _currentMarkerSnapshot.TableWorldPositions[tableIndex],
            isUnlocked,
            level);
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
                initialWorldPosition = GetHatchGenericWorldPosition(petState.PendingSpawnHatchSlotIndex);
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
    /// 获取孵化槽通用出生世界坐标。
    /// </summary>
    /// <param name="hatchSlotIndex">孵化槽索引，用于定位对应的 IncubatorEntity。</param>
    /// <returns>优先返回 IncubatorEntity 的 PetGenericPoint；实体未就绪时回退到孵化槽 UI marker 投影点。</returns>
    private Vector3 GetHatchGenericWorldPosition(int hatchSlotIndex)
    {
        if (hatchSlotIndex >= 0
            && hatchSlotIndex < _incubatorEntityIds.Length
            && TryGetEntityLogic(_incubatorEntityIds[hatchSlotIndex], out IncubatorEntityLogic incubatorEntityLogic)
            && incubatorEntityLogic.TryGetPetGenericWorldPosition(out Vector3 petGenericWorldPosition))
        {
            return petGenericWorldPosition;
        }

        if (_currentMarkerSnapshot != null
            && _currentMarkerSnapshot.HatchSlotWorldPositions != null
            && hatchSlotIndex >= 0
            && hatchSlotIndex < _currentMarkerSnapshot.HatchSlotWorldPositions.Length)
        {
            return _currentMarkerSnapshot.HatchSlotWorldPositions[hatchSlotIndex];
        }

        return Vector3.zero;
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

            // 实体 ID 不匹配时才隐藏（说明是过期请求）。
            // 不再依赖 slotState 判断是否隐藏，因为未解锁槽位也需要显示孵化器实体。
            if (_incubatorEntityIds[incubatorEntityData.SlotIndex] != ne.Entity.Id)
            {
                HideEntityIfNeeded(ne.Entity.Id);
                return;
            }

            if (TryGetEntityLogic(ne.Entity.Id, out IncubatorEntityLogic incubatorEntityLogic))
            {
                incubatorEntityLogic.ApplyData(BuildIncubatorEntityData(incubatorEntityData.SlotIndex));
            }

            RefreshEggEntities();
            RefreshPetEntities(true);

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
    /// 查询指定孵化槽是否已解锁。
    /// slotIndex 为 0 基索引，对应建筑系统的 slotIndex+1。
    /// </summary>
    private static bool IsHatchSlotUnlocked(int slotIndex)
    {
        if (GameEntry.Fruits == null)
        {
            return true;
        }

        PlayerRuntimeModule.ArchitectureEntryState entryState =
            GameEntry.Fruits.GetArchitectureEntryState(PlayerRuntimeModule.ArchitectureCategory.Hatch, slotIndex + 1);
        return entryState.IsUnlocked;
    }

    /// <summary>
    /// 查询指定桌位是否已解锁。
    /// tableIndex 为 0 基索引，对应建筑系统的 slotIndex+1。
    /// </summary>
    private static bool IsTableSlotUnlocked(int tableIndex)
    {
        if (GameEntry.Fruits == null)
        {
            return true;
        }

        PlayerRuntimeModule.ArchitectureEntryState entryState =
            GameEntry.Fruits.GetArchitectureEntryState(PlayerRuntimeModule.ArchitectureCategory.Diet, tableIndex + 1);
        return entryState.IsUnlocked;
    }

    /// <summary>
    /// 查询指定果园位是否已解锁。
    /// orchardIndex 为 0 基索引，对应建筑系统的 slotIndex+1。
    /// </summary>
    private static bool IsOrchardSlotUnlocked(int orchardIndex)
    {
        if (GameEntry.Fruits == null)
        {
            return true;
        }

        PlayerRuntimeModule.ArchitectureEntryState entryState =
            GameEntry.Fruits.GetArchitectureEntryState(PlayerRuntimeModule.ArchitectureCategory.Fruiter, orchardIndex + 1);
        return entryState.IsUnlocked;
    }

    /// <summary>
    /// 对指定孵化器实体重新应用最新数据。
    /// </summary>
    /// <param name="slotIndex">0 基索引。</param>
    private void ApplySingleIncubatorEntityData(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _incubatorEntityIds.Length)
        {
            return;
        }

        int entityId = _incubatorEntityIds[slotIndex];
        if (entityId <= 0 || !IsEntityLoaded(entityId))
        {
            return;
        }

        if (TryGetEntityLogic(entityId, out IncubatorEntityLogic logic))
        {
            logic.ApplyData(BuildIncubatorEntityData(slotIndex));
        }
    }

    /// <summary>
    /// 对指定餐桌实体重新应用最新数据。
    /// </summary>
    /// <param name="slotIndex">0 基索引。</param>
    private void ApplySingleTableEntityData(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _tableEntityIds.Length)
        {
            return;
        }

        int entityId = _tableEntityIds[slotIndex];
        if (entityId <= 0 || !IsEntityLoaded(entityId))
        {
            return;
        }

        if (TryGetEntityLogic(entityId, out TableEntityLogic logic))
        {
            logic.ApplyData(BuildTableEntityData(slotIndex));
        }
    }

    /// <summary>
    /// 对指定果园实体重新应用最新数据。
    /// </summary>
    /// <param name="slotIndex">0 基索引。</param>
    private void ApplySingleOrchardEntityData(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _orchardEntityIds.Length)
        {
            return;
        }

        int entityId = _orchardEntityIds[slotIndex];
        if (entityId <= 0 || !IsEntityLoaded(entityId))
        {
            return;
        }

        if (TryGetEntityLogic(entityId, out OrchardEntityLogic logic))
        {
            logic.ApplyData(BuildOrchardEntityData(slotIndex));
        }
    }

    /// <summary>
    /// 确保指定餐桌槽位的实体存在。
    /// </summary>
    /// <param name="slotIndex">0 基索引。</param>
    private void EnsureTableEntity(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _tableEntityIds.Length)
        {
            return;
        }

        int entityId = _tableEntityIds[slotIndex];
        if (IsEntityLoaded(entityId) || IsEntityLoading(entityId))
        {
            return;
        }

        entityId = AcquireEntityId();
        if (entityId <= 0)
        {
            return;
        }

        _tableEntityIds[slotIndex] = entityId;
        GameEntry.Entity.ShowEntity<TableEntityLogic>(
            entityId,
            EntityDefine.TableEntity,
            EntityDefine.TableGroup,
            BuildTableEntityData(slotIndex));
    }

    /// <summary>
    /// 确保指定果园槽位的实体存在。
    /// </summary>
    /// <param name="slotIndex">0 基索引。</param>
    private void EnsureOrchardEntity(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= _orchardEntityIds.Length)
        {
            return;
        }

        int entityId = _orchardEntityIds[slotIndex];
        if (IsEntityLoaded(entityId) || IsEntityLoading(entityId))
        {
            return;
        }

        entityId = AcquireEntityId();
        if (entityId <= 0)
        {
            return;
        }

        _orchardEntityIds[slotIndex] = entityId;
        GameEntry.Entity.ShowEntity<OrchardEntityLogic>(
            entityId,
            EntityDefine.OrchardEntity,
            EntityDefine.OrchardGroup,
            BuildOrchardEntityData(slotIndex));
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
