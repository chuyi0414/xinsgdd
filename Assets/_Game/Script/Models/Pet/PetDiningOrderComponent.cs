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

        if (!orchardModule.TryOccupySlot(orchardSlotIndex, petState.DesiredFruitCode, produceSeconds))
        {
            return false;
        }

        petState.OrchardSlotIndex = orchardSlotIndex;
        petState.DiningWishState = PetDiningWishState.Producing;
        petState.RemainingDiningStageSeconds = produceSeconds;
        petState.RemainingPostMealSeconds = 0f;
        GameEntry.PetPlacement.NotifyPlacementStateChanged();

        // 在果树上创建水果实体并播放生长动画
        // 生长时长 = ProduceSeconds - 送达飞行时长
        float growDuration = Mathf.Max(0.1f, produceSeconds - _gameplayRuleDataRow.DeliverAnimationDuration);

        // 捕获变量供闭包使用
        int capturedPetInstanceId = petInstanceId;
        int capturedOrchardIndex = orchardSlotIndex;
        int capturedTableIndex = petState.SlotIndex;
        string capturedFruitCode = petState.DesiredFruitCode;

        GameEntry.PlayfieldEntities?.TryShowOrchardFruit(
            orchardSlotIndex,
            petState.DesiredFruitCode,
            growDuration,
            () => OnOrchardGrowComplete(capturedPetInstanceId, capturedOrchardIndex, capturedTableIndex, capturedFruitCode));

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
        // 动画回调会驱动状态转换，这里不再主动转 Serving
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

        GameEntry.PlayfieldEntities.BeginOrchardFruitDelivery(
            orchardIndex,
            tableIndex,
            fruitCode,
            _gameplayRuleDataRow.DeliverAnimationDuration,
            () => OnOrchardDeliverComplete(petInstanceId, tableIndex));
    }

    /// <summary>
    /// 果树水果送达飞行动画完成回调。
    /// 水果已在桌上显示，切换到 Serving 阶段。
    /// </summary>
    private void OnOrchardDeliverComplete(int petInstanceId, int tableIndex)
    {
        if (GameEntry.PetPlacement == null)
        {
            return;
        }

        PetRuntimeState petState = GameEntry.PetPlacement.GetPetStateByInstanceId(petInstanceId);
        if (petState == null || petState.DiningWishState != PetDiningWishState.Producing)
        {
            return;
        }

        petState.OrchardSlotIndex = -1;
        petState.DiningWishState = PetDiningWishState.Serving;
        petState.RemainingDiningStageSeconds = _gameplayRuleDataRow.ServingDurationSeconds;
        GameEntry.PetPlacement.NotifyPlacementStateChanged();
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
