using UnityEngine;

/// <summary>
/// 水果实体显示数据。
/// 同时用于餐桌水果（TableIndex >= 0）和果树生产水果（OrchardIndex >= 0）。
/// </summary>
public sealed class FruitEntityData
{
    /// <summary>
    /// 所属桌位索引。餐桌水果时有效，果树水果时为 -1。
    /// </summary>
    public int TableIndex { get; }

    /// <summary>
    /// 所属果树索引。果树水果时有效，餐桌水果时为 -1。
    /// </summary>
    public int OrchardIndex { get; }

    /// <summary>
    /// 当前要显示的水果 Code。
    /// </summary>
    public string FruitCode { get; }

    /// <summary>
    /// 水果实体在世界中的目标位置。
    /// </summary>
    public Vector3 WorldPosition { get; }

    /// <summary>
    /// 是否是果树上生产的水果（而非餐桌水果）。
    /// </summary>
    public bool IsOrchardFruit => OrchardIndex >= 0;

    /// <summary>
    /// 创建一份水果实体显示数据。
    /// </summary>
    /// <param name="tableIndex">所属桌位索引。</param>
    /// <param name="fruitCode">水果 Code。</param>
    /// <param name="worldPosition">目标世界坐标。</param>
    /// <param name="orchardIndex">所属果树索引，默认 -1 表示餐桌水果。</param>
    public FruitEntityData(int tableIndex, string fruitCode, Vector3 worldPosition, int orchardIndex = -1)
    {
        TableIndex = tableIndex;
        FruitCode = fruitCode;
        WorldPosition = worldPosition;
        OrchardIndex = orchardIndex;
    }
}
