/// <summary>
/// 宠物当前点餐流程的运行时状态。
/// </summary>
public enum PetDiningWishState
{
    /// <summary>
    /// 当前没有可处理的点餐需求。
    /// </summary>
    None = 0,

    /// <summary>
    /// 已拿到期望水果，等待玩家点击气泡开始生产。
    /// </summary>
    Pending = 1,

    /// <summary>
    /// 已经开始生产，仍在等待生产时间结束。
    /// </summary>
    Producing = 2,

    /// <summary>
    /// 水果已经上桌，正处于展示/食用阶段。
    /// </summary>
    Serving = 3,

    /// <summary>
    /// 奖励按钮已经创建，宠物正在播放吃完饭后的 Attack 表现动画。
    /// 该状态结束前不允许宠物离桌、去游玩区或离场。
    /// </summary>
    RewardAnimating = 4,

    /// <summary>
    /// 本次点餐流程已经结束，不再重新出现需求。
    /// </summary>
    Completed = 5,
}
