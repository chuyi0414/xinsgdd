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
    /// 当前桌位是否已解锁。
    /// 未解锁时，实体将显示 Level 0 的占位精灵而非正常外观。
    /// </summary>
    public bool IsUnlocked { get; }

    /// <summary>
    /// 当前桌位的建筑等级。
    /// 已解锁时，根据等级从配置表加载对应精灵。
    /// </summary>
    public int Level { get; }

    /// <summary>
    /// 创建一份桌子实体显示数据。
    /// </summary>
    /// <param name="tableIndex">桌位索引。</param>
    /// <param name="worldPosition">世界坐标。</param>
    /// <param name="isUnlocked">当前桌位是否已解锁。</param>
    /// <param name="level">当前建筑等级。</param>
    public TableEntityData(int tableIndex, Vector3 worldPosition, bool isUnlocked = true, int level = 0)
    {
        TableIndex = tableIndex;
        WorldPosition = worldPosition;
        IsUnlocked = isUnlocked;
        Level = level;
    }
}
