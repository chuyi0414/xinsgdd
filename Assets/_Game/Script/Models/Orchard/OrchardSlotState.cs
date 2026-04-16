/// <summary>
/// 单个果园位的运行时状态。
/// 当前仅用于预留后续生产逻辑所需字段。
/// </summary>
public sealed class OrchardSlotState
{
    /// <summary>
    /// 当前正在生产的水果 Code。
    /// </summary>
    public string FruitCode { get; private set; }

    /// <summary>
    /// 当前剩余生产秒数。
    /// </summary>
    public float RemainingProduceSeconds { get; private set; }

    /// <summary>
    /// 当前是否正在生产。
    /// </summary>
    public bool IsProducing { get; private set; }

    /// <summary>
    /// 当前是否已经可以收取。
    /// </summary>
    public bool IsReadyToCollect { get; private set; }

    /// <summary>
    /// 当前果树是否空闲（未在生产）。
    /// </summary>
    public bool IsIdle => !IsProducing;

    /// <summary>
    /// 占用该果树开始生产。
    /// </summary>
    /// <param name="fruitCode">水果 Code。</param>
    /// <param name="produceSeconds">生产总时长（秒）。</param>
    public void Occupy(string fruitCode, float produceSeconds)
    {
        FruitCode = fruitCode;
        RemainingProduceSeconds = produceSeconds;
        IsProducing = true;
        IsReadyToCollect = false;
    }

    /// <summary>
    /// 释放该果树，恢复空闲。
    /// </summary>
    public void Release()
    {
        FruitCode = null;
        RemainingProduceSeconds = 0f;
        IsProducing = false;
        IsReadyToCollect = false;
    }

    /// <summary>
    /// 逐帧推进生产倒计时。
    /// 到期后自动标记可收取。
    /// 注意：这里故意不自动 Release，
    /// 因为果树的逻辑占用必须与场景中的水果视觉生命周期保持同步。
    /// 如果倒计时一到就立刻释放，会重新引入“逻辑空闲但视觉仍存在”的状态反同步问题。
    /// </summary>
    /// <param name="deltaTime">本帧推进秒数。</param>
    public void Tick(float deltaTime)
    {
        if (!IsProducing)
        {
            return;
        }

        RemainingProduceSeconds -= deltaTime;
        if (RemainingProduceSeconds <= 0f)
        {
            // 生产完成后只把剩余时间钳到 0，并标记为可收取。
            // 真正释放槽位必须由外部在送达成功、失败回滚或超时善后时显式调用。
            RemainingProduceSeconds = 0f;
            IsReadyToCollect = true;
        }
    }

    /// <summary>
    /// 清空当前果园位状态。
    /// </summary>
    public void Clear()
    {
        FruitCode = null;
        RemainingProduceSeconds = 0f;
        IsProducing = false;
        IsReadyToCollect = false;
    }
}
