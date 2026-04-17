using UnityEngine;

/// <summary>
/// 消除区域实体显示数据。
/// 只传递世界位置与排序值，Sprite/Color 由 prefab 自带。
/// </summary>
public sealed class EliminateTheAreaEntityData
{
    /// <summary>
    /// 区域目标世界位置。
    /// </summary>
    public Vector3 WorldPosition { get; }

    /// <summary>
    /// 区域渲染排序值。
    /// 默认 0，确保区域底图在卡片（基准 200）之下。
    /// </summary>
    public int SortingOrder { get; }

    /// <summary>
    /// 构造一份消除区域实体显示数据。
    /// </summary>
    /// <param name="worldPosition">区域在世界中的目标位置。</param>
    /// <param name="sortingOrder">区域渲染排序值。</param>
    public EliminateTheAreaEntityData(Vector3 worldPosition, int sortingOrder)
    {
        WorldPosition = worldPosition;
        SortingOrder = sortingOrder;
    }
}
