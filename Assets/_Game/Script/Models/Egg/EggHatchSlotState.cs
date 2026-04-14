/// <summary>
/// 单个孵化槽位的运行时状态。
/// </summary>
public sealed class EggHatchSlotState
{
    /// <summary>
    /// 当前槽位是否正在孵化。
    /// </summary>
    public bool IsOccupied { get; internal set; }

    /// <summary>
    /// 当前孵化中的蛋 Code。
    /// </summary>
    public string EggCode { get; internal set; }

    /// <summary>
    /// 总孵化时长，单位秒。
    /// </summary>
    public float TotalSeconds { get; internal set; }

    /// <summary>
    /// 剩余孵化时长，单位秒。
    /// </summary>
    public float RemainingSeconds { get; internal set; }

    /// <summary>
    /// 清空槽位状态。
    /// </summary>
    internal void Clear()
    {
        IsOccupied = false;
        EggCode = null;
        TotalSeconds = 0f;
        RemainingSeconds = 0f;
    }

    /// <summary>
    /// 占用当前槽位。
    /// </summary>
    internal void Occupy(string eggCode, float totalSeconds)
    {
        IsOccupied = true;
        EggCode = eggCode;
        TotalSeconds = totalSeconds;
        RemainingSeconds = totalSeconds;
    }
}
