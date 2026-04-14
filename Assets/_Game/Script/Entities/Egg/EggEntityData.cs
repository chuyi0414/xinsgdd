using UnityEngine;

/// <summary>
/// 蛋实体显示数据。
/// </summary>
public sealed class EggEntityData
{
    /// <summary>
    /// 孵化槽索引。
    /// </summary>
    public int SlotIndex { get; }

    /// <summary>
    /// 当前显示的蛋配置 Code。
    /// </summary>
    public string EggCode { get; }

    /// <summary>
    /// 蛋实体在世界中的目标位置。
    /// </summary>
    public Vector3 WorldPosition { get; }

    /// <summary>
    /// 创建一份蛋实体显示数据。
    /// </summary>
    /// <param name="slotIndex">对应的孵化槽索引。</param>
    /// <param name="eggCode">蛋配置 Code。</param>
    /// <param name="worldPosition">世界坐标。</param>
    public EggEntityData(int slotIndex, string eggCode, Vector3 worldPosition)
    {
        SlotIndex = slotIndex;
        EggCode = eggCode;
        WorldPosition = worldPosition;
    }
}
