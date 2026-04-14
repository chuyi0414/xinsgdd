/// <summary>
/// 宠物当前所在的场地区域类型。
/// </summary>
public enum PetPlacementType
{
    /// <summary>
    /// 未分配区域。
    /// </summary>
    None = 0,

    /// <summary>
    /// 已入座到餐桌位。
    /// </summary>
    DiningSeat = 1,

    /// <summary>
    /// 位于排队区。
    /// </summary>
    Queue = 2,

    /// <summary>
    /// 位于玩耍区。
    /// </summary>
    PlayArea = 3,

    /// <summary>
    /// 正在离场。
    /// </summary>
    Leaving = 4,
}
