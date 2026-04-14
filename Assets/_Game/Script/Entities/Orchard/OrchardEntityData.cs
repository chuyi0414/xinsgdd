using UnityEngine;

/// <summary>
/// 果园实体显示数据。
/// </summary>
public sealed class OrchardEntityData
{
    /// <summary>
    /// 果园位索引。
    /// </summary>
    public int OrchardIndex { get; }

    /// <summary>
    /// 果园实体在世界中的目标位置。
    /// </summary>
    public Vector3 WorldPosition { get; }

    /// <summary>
    /// 创建一份果园实体显示数据。
    /// </summary>
    /// <param name="orchardIndex">果园位索引。</param>
    /// <param name="worldPosition">世界坐标。</param>
    public OrchardEntityData(int orchardIndex, Vector3 worldPosition)
    {
        OrchardIndex = orchardIndex;
        WorldPosition = worldPosition;
    }
}
