using UnityEngine;

/// <summary>
/// 桌子实体显示数据。
/// </summary>
public sealed class TableEntityData
{
    /// <summary>
    /// 桌位索引。
    /// </summary>
    public int TableIndex { get; }

    /// <summary>
    /// 桌子实体在世界中的目标位置。
    /// </summary>
    public Vector3 WorldPosition { get; }

    /// <summary>
    /// 创建一份桌子实体显示数据。
    /// </summary>
    /// <param name="tableIndex">桌位索引。</param>
    /// <param name="worldPosition">世界坐标。</param>
    public TableEntityData(int tableIndex, Vector3 worldPosition)
    {
        TableIndex = tableIndex;
        WorldPosition = worldPosition;
    }
}
