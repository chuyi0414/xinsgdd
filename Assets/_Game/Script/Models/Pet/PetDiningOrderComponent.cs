using System;
using UnityEngine;
using UnityGameFramework.Runtime;
using System.Collections.Generic;

/// <summary>
/// 宠物点餐生产组件。
/// 负责推进“点击开始生产 -> 生产完成上桌 -> 5 秒后收尾”的全局运行时流程。
/// </summary>
public sealed class PetDiningOrderComponent : GameFrameworkComponent
{
    /// <summary>
    /// 宠物吃完食物后产出金币时触发。
    /// 参数一：宠物实例 Id。
    /// 参数二：本次产出的金币数量（来自 FruitDataRow.CoinAmount）。
    /// </summary>
    public event Action<int, int> CoinDropRequested;

    /// <summary>
    /// 宠物吃完食物后产出物掉落时触发。
    /// 参数一：宠物实例 Id。
    /// 参数二：本次产出物的 Code。
    /// </summary>
    public event Action<int, string> ProduceDropRequested;

    /// <summary>
    /// 当前是否已经完成初始化。
    /// </summary>
    private bool _isInitialized;

    /// <summary>
    /// 当前是否可用于业务逻辑。
    /// </summary>
    private bool _isAvailable;

    /// <summary>
    /// 逐帧推进订单时复用的宠物状态缓冲。
    /// 避免在 Update 中通过 GetAllPets() 反复分配数组。
    /// </summary>
    private readonly List<PetRuntimeState> _petStateBuffer = new List<PetRuntimeState>(32);

    /// <summary>
    /// 全局玩法规则缓存。
    /// </summary>
    private GameplayRuleDataRow _gameplayRuleDataRow;

    private const float ProducingFallbackToleranceSeconds = 1f;

    /// <summary>
    /// 当前组件是否可用。
    /// </summary>
    public bool IsAvailable => _isInitialized && _isAvailable;

    /// <summary>
    /// 推进所有宠物订单的生产与上桌状态。
    /// </summary>
    private void Update()
    {
        if (!IsAvailable)
        {
            return;
        }

        float deltaTime = Time.unscaledDeltaTime;
        if (deltaTime <= 0f || GameEntry.PetPlacement == null)
        {
            return;
        }

        // 推进果树占用倒计时
        GameEntry.Orchards?.Tick(deltaTime);

        GameEntry.PetPlacement.GetAllPetsNonAlloc(_petStateBuffer);
        for (int i = 0; i < _petStateBuffer.Count; i++)
        {
            PetRuntimeState petState = _petStateBuffer[i];
            if (petState == null)
            {
                continue;
            }

            UpdatePostMealState(petState, deltaTime);
            UpdateSingleDiningOrder(petState, deltaTime);
        }
    }

    /// <summary>
    /// 确保组件已完成初始化。
    /// </summary>
    public void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        // 加载流程中 Fruit 表与 PetProduce 表是异步读取的，
        // 因此这里不能把“依赖尚未到齐”当成真正的错误。
        // 未准备好时保持未初始化状态，等后续注册完成后再次调用即可。
        if (!HasRequiredRuntimeDependencies())
        {
            _isAvailable = false;
            return;
        }

        _gameplayRuleDataRow = GameEntry.DataTables.GetDataRowByCode<GameplayRuleDataRow>(GameplayRuleDataRow.DefaultCode);
        if (_gameplayRuleDataRow == null)
        {
            _isAvailable = false;
            return;
        }

        _isInitialized = true;
        _isAvailable = true;
    }

    /// <summary>
    /// 判断点餐运行时依赖是否已经全部就绪。
    /// </summary>
    /// <returns>依赖是否可用。</returns>
    private static bool HasRequiredRuntimeDependencies()
    {
        return GameEntry.DataTables != null
            && GameEntry.DataTables.IsAvailable<FruitDataRow>()
            && GameEntry.DataTables.IsAvailable<PetProduceDataRow>()
            && GameEntry.DataTables.IsAvailable<GameplayRuleDataRow>()
            && GameEntry.Fruits != null;
    }

    /// <summary>
    /// 统一处理宠物食物气泡点击。
    /// 已解锁水果会开始生产；未解锁水果会直接消费这次需求并进入吃完后的去向判定。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    /// <returns>是否成功处理点击。</returns>
    public bool HandlePetFoodBubbleClick(int petInstanceId)
    {
        EnsureInitialized();
        if (!IsAvailable || GameEntry.PetPlacement == null || GameEntry.PlayfieldEntities == null)
        {
            return false;
        }

        PetRuntimeState petState = GameEntry.PetPlacement.GetPetStateByInstanceId(petInstanceId);
        if (!CanStartDiningOrder(petState))
        {
            return false;
        }

        if (GameEntry.Fruits == null || !GameEntry.Fruits.IsFruitUnlocked(petState.DesiredFruitCode))
        {
            petState.DiningWishState = PetDiningWishState.Completed;
            petState.RemainingDiningStageSeconds = 0f;
            petState.RemainingPostMealSeconds = 0f;
            GameEntry.PetPlacement.ResolvePostMealOutcome(petInstanceId);
            return true;
        }

        FruitDataRow fruitDataRow = GameEntry.DataTables.GetDataRowByCode<FruitDataRow>(petState.DesiredFruitCode);
        if (fruitDataRow == null)
        {
            return false;
        }

        // 尝试占用一棵空闲果树，无空闲则不执行任何逻辑
        OrchardModule orchardModule = GameEntry.Orchards;
        if (orchardModule == null)
        {
            return false;
        }

        if (!orchardModule.TryGetIdleSlotIndex(out int orchardSlotIndex))
        {
            // 没有空闲果树，不执行任何逻辑也不关闭气泡
            return false;
        }

        float produceSeconds = fruitDataRow.ProduceSeconds;
        if (GameEntry.Fruits != null)
        {
            produceSeconds = Mathf.Max(
                _gameplayRuleDataRow.DeliverAnimationDuration + 0.1f,
                produceSeconds * GameEntry.Fruits.GetFruiterDurationScale(orchardSlotIndex + 1));
        }

        // 生产超时要略大于理论生产时长。
        // 这里额外加一个容差，专门兜底动画回调丢失、实体显示失败等异常链路，
        // 防止宠物长期卡死在 Producing 状态。
        float producingTimeoutSeconds = produceSeconds + ProducingFallbackToleranceSeconds;

        if (!orchardModule.TryOccupySlot(orchardSlotIndex, petState.DesiredFruitCode, producingTimeoutSeconds))
        {
            return false;
        }

        // 在果树上创建水果实体并播放生长动画
        // 生长时长 = ProduceSeconds - 送达飞行时长
        float growDuration = Mathf.Max(0.1f, produceSeconds - _gameplayRuleDataRow.DeliverAnimationDuration);

        // 捕获变量供闭包使用
        int capturedPetInstanceId = petInstanceId;
        int capturedOrchardIndex = orchardSlotIndex;
        int capturedTableIndex = petState.SlotIndex;
        string capturedFruitCode = petState.DesiredFruitCode;

        // 先尝试把果树水果实体真正提交到场景。
        // 只有这一步成功，后续才允许切换宠物状态并关闭气泡。
        if (GameEntry.PlayfieldEntities == null
            || !GameEntry.PlayfieldEntities.TryShowOrchardFruit(
                orchardSlotIndex,
                petState.DesiredFruitCode,
                growDuration,
                () => OnOrchardGrowComplete(capturedPetInstanceId, capturedOrchardIndex, capturedTableIndex, capturedFruitCode)))
        {
            // 果树水果实体提交失败时，必须立即释放刚刚占用的果树位。
            // 这里不能把宠物切到 Producing，否则会出现气泡消失但桌上没食物的假状态。
            orchardModule.ReleaseSlot(orchardSlotIndex);
            return false;
        }

        // 到这里说明果树水果已经成功进入显示/生长链路，
        // 现在才可以安全写入 Producing 状态并让 UI 按规则隐藏气泡。
        petState.OrchardSlotIndex = orchardSlotIndex;
        petState.DiningWishState = PetDiningWishState.Producing;
        petState.RemainingDiningStageSeconds = producingTimeoutSeconds;
        petState.RemainingPostMealSeconds = 0f;
        GameEntry.PetPlacement.NotifyPlacementStateChanged();

        return true;
    }

    /// <summary>
    /// 兼容旧调用的开始生产接口。
    /// 现在它内部直接转发到统一点击处理入口。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    /// <returns>是否成功处理点击。</returns>
    public bool TryStartDiningOrder(int petInstanceId)
    {
        return HandlePetFoodBubbleClick(petInstanceId);
    }

    /// <summary>
    /// 判断指定宠物当前是否允许开始生产。
    /// </summary>
    /// <param name="petState">宠物运行时状态。</param>
    /// <returns>是否允许开始生产。</returns>
    private static bool CanStartDiningOrder(PetRuntimeState petState)
    {
        if (petState == null
            || petState.PlacementType != PetPlacementType.DiningSeat
            || petState.DiningWishState != PetDiningWishState.Pending
            || string.IsNullOrWhiteSpace(petState.DesiredFruitCode)
            || GameEntry.PlayfieldEntities == null
            || !GameEntry.PlayfieldEntities.IsPetAttachedToDiningTable(petState.InstanceId))
        {
            return false;
        }

        return GameEntry.DataTables != null && GameEntry.DataTables.GetDataRowByCode<FruitDataRow>(petState.DesiredFruitCode) != null;
    }

    /// <summary>
    /// 推进单只宠物的点餐状态。
    /// </summary>
    /// <param name="petState">宠物运行时状态。</param>
    /// <param name="deltaTime">本帧推进秒数。</param>
    private void UpdateSingleDiningOrder(PetRuntimeState petState, float deltaTime)
    {
        switch (petState.DiningWishState)
        {
            case PetDiningWishState.Producing:
                UpdateProducingOrder(petState, deltaTime);
                break;

            case PetDiningWishState.Serving:
                UpdateServingOrder(petState, deltaTime);
                break;
        }
    }

    /// <summary>
    /// 推进生产中的订单。
    /// 现在 Producing 阶段由动画回调驱动状态转换，
    /// 这里只做 RemainingDiningStageSeconds 的 fallback 推进。
    /// 正常情况下 OnOrchardDeliverComplete 会在动画结束时切换到 Serving。
    /// </summary>
    /// <param name="petState">宠物运行时状态。</param>
    /// <param name="deltaTime">本帧推进秒数。</param>
    private void UpdateProducingOrder(PetRuntimeState petState, float deltaTime)
    {
        petState.RemainingDiningStageSeconds -= deltaTime;
        if (petState.RemainingDiningStageSeconds > 0f)
        {
            return;
        }

        HandleProducingOrderTimeout(petState);
    }

    /// <summary>
    /// 果树水果生长动画完成回调。
    /// 立即开始送达飞行动画（飞向 BJRight 左边界）。
    /// </summary>
    private void OnOrchardGrowComplete(int petInstanceId, int orchardIndex, int tableIndex, string fruitCode)
    {
        if (GameEntry.PlayfieldEntities == null)
        {
            return;
        }

        // 送达完成回调会把“桌面水果是否真正显示成功”的结果回传回来，
        // 由上层统一决定进入 Serving 还是整单回滚到 Pending。
        GameEntry.PlayfieldEntities.BeginOrchardFruitDelivery(
            orchardIndex,
            tableIndex,
            fruitCode,
            _gameplayRuleDataRow.DeliverAnimationDuration,
            deliveredToTable => OnOrchardDeliverComplete(petInstanceId, orchardIndex, tableIndex, deliveredToTable));
    }

    /// <summary>
    /// 果树水果送达飞行动画完成回调。
    /// 水果已在桌上显示，切换到 Serving 阶段。
    /// </summary>
    private void OnOrchardDeliverComplete(int petInstanceId, int orchardIndex, int tableIndex, bool deliveredToTable)
    {
        if (GameEntry.PetPlacement == null)
        {
            return;
        }

        PetRuntimeState petState = GameEntry.PetPlacement.GetPetStateByInstanceId(petInstanceId);
        // 二次校验当前宠物是否仍然对应这一次点餐链路。
        // 如果中途已经被超时回滚或状态重置，迟到的旧回调必须直接丢弃，
        // 否则会误清理新订单或把宠物推进到错误状态。
        if (petState == null
            || petState.DiningWishState != PetDiningWishState.Producing
            || petState.OrchardSlotIndex != orchardIndex
            || petState.SlotIndex != tableIndex)
        {
            return;
        }

        if (!deliveredToTable)
        {
            // deliveredToTable == false 的语义是：飞行动画虽然结束，
            // 但桌面水果实体没有成功显示。
            // 这里必须整单回滚，否则会再次出现宠物霸占桌位但桌上无食物的错误状态。
            CleanupDiningOrderArtifacts(orchardIndex, tableIndex);
            RestorePendingDiningWish(petState);
            GameEntry.PetPlacement.NotifyPlacementStateChanged();
            return;
        }

        // 桌面水果已经显示成功，果树侧的职责到此结束。
        // 现在可以安全释放果树占用，并把宠物推进到 Serving 阶段。
        if (orchardIndex >= 0)
        {
            GameEntry.Orchards?.ReleaseSlot(orchardIndex);
        }

        petState.OrchardSlotIndex = -1;
        petState.DiningWishState = PetDiningWishState.Serving;
        petState.RemainingDiningStageSeconds = _gameplayRuleDataRow.ServingDurationSeconds;
        GameEntry.PetPlacement.NotifyPlacementStateChanged();
    }

    /// <summary>
    /// 处理 Producing 阶段超时。
    /// 当生长动画、送达动画或实体显示回调链路异常中断时，
    /// 这里负责清理残留并把宠物恢复到 Pending，避免桌位永久卡死。
    /// </summary>
    /// <param name="petState">当前超时的宠物运行时状态。</param>
    private void HandleProducingOrderTimeout(PetRuntimeState petState)
    {
        if (petState == null || petState.DiningWishState != PetDiningWishState.Producing)
        {
            return;
        }

        // 超时后的善后必须同时处理视觉残留与逻辑残留，
        // 然后再把宠物恢复为 Pending，让气泡重新可见。
        CleanupDiningOrderArtifacts(petState.OrchardSlotIndex, petState.SlotIndex);
        RestorePendingDiningWish(petState);
        GameEntry.PetPlacement?.NotifyPlacementStateChanged();
    }

    /// <summary>
    /// 清理一次点餐流程可能残留的果树水果、桌面水果以及果树占用。
    /// 该方法只负责做显式善后，不直接修改宠物自身状态。
    /// </summary>
    /// <param name="orchardIndex">需要清理的果树索引，传负数表示跳过果树侧清理。</param>
    /// <param name="tableIndex">需要清理的桌位索引，传负数表示跳过桌面侧清理。</param>
    private void CleanupDiningOrderArtifacts(int orchardIndex, int tableIndex)
    {
        if (orchardIndex >= 0)
        {
            // 果树侧残留要同时清理“视觉实体 + 逻辑占用”，
            // 否则后续新订单会因为占用状态不一致再次出错。
            GameEntry.PlayfieldEntities?.HideOrchardFruit(orchardIndex);
            GameEntry.Orchards?.ReleaseSlot(orchardIndex);
        }

        if (tableIndex >= 0)
        {
            // 桌面侧只要有残留水果，也必须一并移除，
            // 避免旧订单的视觉结果污染新订单。
            GameEntry.PlayfieldEntities?.HideDiningFruit(tableIndex);
        }
    }

    /// <summary>
    /// 将宠物点餐状态恢复为 Pending。
    /// 恢复后 UI 层会重新生成对应气泡，允许玩家再次发起点餐。
    /// </summary>
    /// <param name="petState">需要回滚的宠物运行时状态。</param>
    private static void RestorePendingDiningWish(PetRuntimeState petState)
    {
        if (petState == null)
        {
            return;
        }

        // 这里要把 Producing/Serving 过程中写入的临时状态全部清空，
        // 确保后续重新点餐时不会读到脏数据。
        petState.OrchardSlotIndex = -1;
        petState.DiningWishState = PetDiningWishState.Pending;
        petState.RemainingDiningStageSeconds = 0f;
        petState.RemainingPostMealSeconds = 0f;
    }

    /// <summary>
    /// 推进上桌中的订单。
    /// </summary>
    /// <param name="petState">宠物运行时状态。</param>
    /// <param name="deltaTime">本帧推进秒数。</param>
    private void UpdateServingOrder(PetRuntimeState petState, float deltaTime)
    {
        petState.RemainingDiningStageSeconds -= deltaTime;
        if (petState.RemainingDiningStageSeconds > 0f)
        {
            return;
        }

        GameEntry.PlayfieldEntities?.HideDiningFruit(petState.SlotIndex);

        // 统一在吃完阶段做一次奖励结算：
        // 先按水果表 CoinProbability 决定是否掉金币，
        // 未命中金币的剩余概率全部进入产出物分支。
        ResolveMealReward(petState);

        petState.DiningWishState = PetDiningWishState.Completed;
        petState.RemainingDiningStageSeconds = 0f;
        GameEntry.PetPlacement?.ResolvePostMealOutcome(petState.InstanceId);
    }

    /// <summary>
    /// 统一结算宠物吃完后的奖励。
    /// 奖励先按水果表 CoinProbability 掷金币概率，
    /// 未命中金币时再进入产出物分支。
    /// </summary>
    /// <param name="petState">宠物运行时状态。</param>
    private void ResolveMealReward(PetRuntimeState petState)
    {
        if (petState == null || string.IsNullOrWhiteSpace(petState.DesiredFruitCode) || GameEntry.DataTables == null)
        {
            return;
        }

        FruitDataRow fruitDataRow = GameEntry.DataTables.GetDataRowByCode<FruitDataRow>(petState.DesiredFruitCode);
        if (fruitDataRow == null)
        {
            return;
        }

        // CoinProbability 本身就是金币掉落概率。
        // 例如 CoinProbability = 80，则 80% 掉金币，剩余 20% 全部进入产出物分支。
        int roll = UnityEngine.Random.Range(0, 100);
        if (roll < fruitDataRow.CoinProbability)
        {
            TryDropCoin(petState, fruitDataRow);
            return;
        }

        TryDropProduce(petState);
    }

    /// <summary>
    /// 触发金币掉落事件。
    /// </summary>
    /// <param name="petState">宠物运行时状态。</param>
    /// <param name="fruitDataRow">当前桌上水果配置。</param>
    private void TryDropCoin(PetRuntimeState petState, FruitDataRow fruitDataRow)
    {
        if (petState == null || fruitDataRow == null || fruitDataRow.CoinAmount <= 0)
        {
            return;
        }

        int coinAmount = fruitDataRow.CoinAmount;
        if (GameEntry.Fruits != null)
        {
            coinAmount += GameEntry.Fruits.GetDietCoinBonus(petState.SlotIndex + 1);
        }

        if (coinAmount <= 0)
        {
            return;
        }

        CoinDropRequested?.Invoke(petState.InstanceId, coinAmount);
    }

    /// <summary>
    /// 根据当前宠物的产出表配置触发产出物掉落事件。
    /// </summary>
    /// <param name="petState">宠物运行时状态。</param>
    private void TryDropProduce(PetRuntimeState petState)
    {
        if (petState == null || string.IsNullOrWhiteSpace(petState.PetCode) || GameEntry.Fruits == null)
        {
            return;
        }

        if (!GameEntry.Fruits.TryRollPetProduce(petState.PetCode, out PetProduceDataRow produceDataRow)
            || produceDataRow == null
            || string.IsNullOrWhiteSpace(produceDataRow.Code))
        {
            return;
        }

        ProduceDropRequested?.Invoke(petState.InstanceId, produceDataRow.Code);
    }

    /// <summary>
    /// 推进宠物吃完后的去向状态。
    /// 当前只处理 PlayArea 停留 5 秒后再次做 50/50 判定。
    /// </summary>
    /// <param name="petState">宠物运行时状态。</param>
    /// <param name="deltaTime">本帧推进秒数。</param>
    private void UpdatePostMealState(PetRuntimeState petState, float deltaTime)
    {
        if (petState == null || petState.PlacementType != PetPlacementType.PlayArea || petState.RemainingPostMealSeconds <= 0f)
        {
            return;
        }

        petState.RemainingPostMealSeconds -= deltaTime;
        if (petState.RemainingPostMealSeconds > 0f)
        {
            return;
        }

        petState.RemainingPostMealSeconds = _gameplayRuleDataRow.PlayAreaStaySeconds;
        GameEntry.PetPlacement?.ResolvePostMealOutcome(petState.InstanceId);
    }
}
