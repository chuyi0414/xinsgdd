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
    /// 创建一份孵化器实体显示数据。
    /// </summary>
    /// <param name="slotIndex">对应的孵化槽索引。</param>
    /// <param name="worldPosition">孵化器要摆放到的世界坐标。</param>
    public IncubatorEntityData(int slotIndex, Vector3 worldPosition)
    {
        SlotIndex = slotIndex;
        WorldPosition = worldPosition;
    }
}
