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
    /// 当前果园位是否已解锁。
    /// 未解锁时，实体将显示 Level 0 的占位精灵而非正常外观。
    /// </summary>
    public bool IsUnlocked { get; }

    /// <summary>
    /// 当前果园位的建筑等级。
    /// 已解锁时，根据等级从配置表加载对应精灵。
    /// </summary>
    public int Level { get; }

    /// <summary>
    /// 创建一份果园实体显示数据。
    /// </summary>
    /// <param name="orchardIndex">果园位索引。</param>
    /// <param name="worldPosition">世界坐标。</param>
    /// <param name="isUnlocked">当前果园位是否已解锁。</param>
    /// <param name="level">当前建筑等级。</param>
    public OrchardEntityData(int orchardIndex, Vector3 worldPosition, bool isUnlocked = true, int level = 0)
    {
        OrchardIndex = orchardIndex;
        WorldPosition = worldPosition;
        IsUnlocked = isUnlocked;
        Level = level;
    }
}
