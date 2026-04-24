using System;
using UnityEngine;

/// <summary>
/// MainUI 标记点转换后的世界坐标快照。
/// </summary>
public sealed class PlayfieldMarkerSnapshot
{
    /// <summary>
    /// 孵化槽世界坐标。
    /// </summary>
    public Vector3[] HatchSlotWorldPositions { get; }

    /// <summary>
    /// 排队位世界坐标。
    /// </summary>
    public Vector3[] QueueWorldPositions { get; }

    /// <summary>
    /// 桌子实体世界坐标。
    /// </summary>
    public Vector3[] TableWorldPositions { get; }

    /// <summary>
    /// 果园实体世界坐标。
    /// </summary>
    public Vector3[] OrchardWorldPositions { get; }

    /// <summary>
    /// 玩耍区世界坐标。
    /// 这是可选数组，不参与当前固定场地区快照的有效性校验。
    /// </summary>
    public PlayAreaWorldRegion[] PlayAreaWorldRegions { get; }

    /// <summary>
    /// BJRight 左边界的世界坐标 X 值。
    /// 水果送达动画飞到此 X 坐标后隐藏。
    /// </summary>
    public float RightPageLeftEdgeWorldX { get; }

    /// <summary>
    /// 当前快照是否满足场地区固定数量要求。
    /// 使用总槽位数（包含未解锁）验证，确保所有槽位都有对应的世界坐标。
    /// </summary>
    public bool IsValid =>
        HatchSlotWorldPositions != null
        && HatchSlotWorldPositions.Length == PlayfieldEntityModule.HatchSlotCountValue
        && QueueWorldPositions != null
        && QueueWorldPositions.Length == PlayfieldEntityModule.QueueSlotCountValue
        && TableWorldPositions != null
        && TableWorldPositions.Length == (GameEntry.Fruits?.TotalDiningSeatCount ?? PlayerRuntimeModule.DietArchitectureCountValue)
        && OrchardWorldPositions != null
        && OrchardWorldPositions.Length == (GameEntry.Fruits?.TotalOrchardSlotCount ?? PlayerRuntimeModule.FruiterArchitectureCountValue);

    /// <summary>
    /// 创建一份场地标记点快照。
    /// </summary>
    /// <param name="hatchSlotWorldPositions">孵化槽世界坐标集合。</param>
    /// <param name="queueWorldPositions">排队位世界坐标集合。</param>
    /// <param name="tableWorldPositions">桌位世界坐标集合。</param>
    /// <param name="orchardWorldPositions">果园位世界坐标集合。</param>
    public PlayfieldMarkerSnapshot(
        Vector3[] hatchSlotWorldPositions,
        Vector3[] queueWorldPositions,
        Vector3[] tableWorldPositions,
        Vector3[] orchardWorldPositions,
        PlayAreaWorldRegion[] playAreaWorldRegions = null,
        float rightPageLeftEdgeWorldX = 0f)
    {
        HatchSlotWorldPositions = ClonePositions(hatchSlotWorldPositions);
        QueueWorldPositions = ClonePositions(queueWorldPositions);
        TableWorldPositions = ClonePositions(tableWorldPositions);
        OrchardWorldPositions = ClonePositions(orchardWorldPositions);
        PlayAreaWorldRegions = CloneRegions(playAreaWorldRegions);
        RightPageLeftEdgeWorldX = rightPageLeftEdgeWorldX;
    }

    /// <summary>
    /// 复制当前快照，避免外部数组共享引用。
    /// </summary>
    public PlayfieldMarkerSnapshot Clone()
    {
        return new PlayfieldMarkerSnapshot(HatchSlotWorldPositions, QueueWorldPositions, TableWorldPositions, OrchardWorldPositions, PlayAreaWorldRegions, RightPageLeftEdgeWorldX);
    }

    /// <summary>
    /// 复制坐标数组，避免外部修改污染快照。
    /// </summary>
    private static Vector3[] ClonePositions(Vector3[] positions)
    {
        if (positions == null)
        {
            return null;
        }

        Vector3[] clonedPositions = new Vector3[positions.Length];
        Array.Copy(positions, clonedPositions, positions.Length);
        return clonedPositions;
    }

    /// <summary>
    /// 复制玩耍区数组，避免外部修改污染快照。
    /// </summary>
    private static PlayAreaWorldRegion[] CloneRegions(PlayAreaWorldRegion[] regions)
    {
        if (regions == null)
        {
            return null;
        }

        PlayAreaWorldRegion[] clonedRegions = new PlayAreaWorldRegion[regions.Length];
        Array.Copy(regions, clonedRegions, regions.Length);
        return clonedRegions;
    }
}
