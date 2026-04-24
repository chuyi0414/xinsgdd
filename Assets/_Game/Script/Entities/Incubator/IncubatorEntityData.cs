using UnityEngine;

/// <summary>
/// 孵化器实体显示数据。
/// </summary>
public sealed class IncubatorEntityData
{
    /// <summary>
    /// 孵化槽索引。
    /// 这个索引用来把实体和固定 4 个 UI 孵化槽 marker 一一对应起来。
    /// </summary>
    public int SlotIndex { get; }

    /// <summary>
    /// 孵化器实体在世界中的目标位置。
    /// 这里直接使用 MainUI 投影出来的槽位世界坐标。
    /// </summary>
    public Vector3 WorldPosition { get; }

    /// <summary>
    /// 当前槽位是否已解锁。
    /// 未解锁时，实体将显示 Level 0 的占位精灵而非正常外观。
    /// </summary>
    public bool IsUnlocked { get; }

    /// <summary>
    /// 当前槽位的建筑等级。
    /// 已解锁时，根据等级从配置表加载对应精灵。
    /// </summary>
    public int Level { get; }

    /// <summary>
    /// 创建一份孵化器实体显示数据。
    /// </summary>
    /// <param name="slotIndex">对应的孵化槽索引。</param>
    /// <param name="worldPosition">孵化器要摆放到的世界坐标。</param>
    /// <param name="isUnlocked">当前槽位是否已解锁。</param>
    /// <param name="level">当前建筑等级。</param>
    public IncubatorEntityData(int slotIndex, Vector3 worldPosition, bool isUnlocked = true, int level = 0)
    {
        SlotIndex = slotIndex;
        WorldPosition = worldPosition;
        IsUnlocked = isUnlocked;
        Level = level;
    }
}
