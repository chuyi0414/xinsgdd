using UnityEngine;

/// <summary>
/// 单只宠物在场景中的运行时状态。
/// </summary>
public sealed class PetRuntimeState
{
    /// <summary>
    /// 宠物实例 Id。
    /// </summary>
    public int InstanceId { get; internal set; }

    /// <summary>
    /// 宠物配置 Code。
    /// </summary>
    public string PetCode { get; internal set; }

    /// <summary>
    /// 宠物品质。
    /// </summary>
    public QualityType Quality { get; internal set; }

    /// <summary>
    /// 当前站位区域。
    /// </summary>
    public PetPlacementType PlacementType { get; internal set; }

    /// <summary>
    /// 当前所在的区域内索引。
    /// </summary>
    public int SlotIndex { get; internal set; }

    /// <summary>
    /// 当前这只宠物在本次会话内期望的水果 Code。
    /// 仅在宠物进入餐桌位时抽取一次，之后保持不变。
    /// </summary>
    public string DesiredFruitCode { get; internal set; }

    /// <summary>
    /// 当前点餐流程状态。
    /// 只有处于 Pending 时才允许点击气泡开始生产。
    /// </summary>
    public PetDiningWishState DiningWishState { get; internal set; }

    /// <summary>
    /// 当前点餐流程阶段剩余秒数。
    /// Producing 阶段表示剩余生产时间，Serving 阶段表示剩余上桌展示时间。
    /// </summary>
    public float RemainingDiningStageSeconds { get; internal set; }

    /// <summary>
    /// 当前进入的玩耍区索引。
    /// 仅在 PlacementType = PlayArea 时有效。
    /// </summary>
    public int PlayAreaIndex { get; internal set; } = -1;

    /// <summary>
    /// 当前在玩耍区矩形内命中的归一化随机点。
    /// 通过这个值可以在分辨率或布局变化后重新换算到新的世界坐标。
    /// </summary>
    public Vector2 PlayAreaRandomPosition01 { get; internal set; }

    /// <summary>
    /// 吃完后的停留阶段剩余秒数。
    /// 当前仅用于 PlayArea 停留 5 秒后再次做 50/50 判定。
    /// </summary>
    public float RemainingPostMealSeconds { get; internal set; }

    /// <summary>
    /// 待消费的首次出生孵化槽索引。
    /// 仅在宠物第一次出场时用于确定起始位置，消费后重置为 -1。
    /// </summary>
    public int PendingSpawnHatchSlotIndex { get; internal set; } = -1;

    /// <summary>
    /// 当前使用的果树索引。
    /// 仅在 Producing 阶段有效，生产完成后重置为 -1。
    /// </summary>
    public int OrchardSlotIndex { get; internal set; } = -1;

    /// <summary>
    /// 排队宠物被补位到餐桌时，是否需要执行一次入座移动动画。
    /// 仅在 PromoteQueuePetsIfPossible 中置 true，在真正发起 MoveToWorldPosition 后清 false。
    /// 解决：购买新餐桌时排队宠物直接 AttachEntity 瞬移的 Bug。
    /// </summary>
    public bool PendingPromoteToDining { get; internal set; }
}
