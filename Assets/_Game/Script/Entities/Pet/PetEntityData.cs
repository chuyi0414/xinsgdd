using UnityEngine;

/// <summary>
/// 宠物实体显示数据。
/// </summary>
public sealed class PetEntityData
{
    /// <summary>
    /// 宠物实例 Id。
    /// </summary>
    public int PetInstanceId { get; }

    /// <summary>
    /// 宠物配置 Code。
    /// </summary>
    public string PetCode { get; }

    /// <summary>
    /// 宠物当前所在区域类型。
    /// </summary>
    public PetPlacementType PlacementType { get; }

    /// <summary>
    /// 宠物在当前区域内的槽位索引。
    /// </summary>
    public int SlotIndex { get; }

    /// <summary>
    /// 若宠物已入座，对应桌位索引；否则为 -1。
    /// </summary>
    public int TableIndex { get; }

    /// <summary>
    /// 宠物实体在世界中的目标位置。
    /// </summary>
    public Vector3 WorldPosition { get; }

    /// <summary>
    /// 宠物实体首次显示时的初始世界位置。
    /// </summary>
    public Vector3 InitialWorldPosition { get; }

    /// <summary>
    /// 首次显示时是否应先使用初始世界位置。
    /// </summary>
    public bool UseInitialWorldPositionOnShow { get; }

    /// <summary>
    /// 创建一份宠物实体显示数据。
    /// </summary>
    /// <param name="petInstanceId">宠物实例 Id。</param>
    /// <param name="petCode">宠物配置 Code。</param>
    /// <param name="placementType">站位区域类型。</param>
    /// <param name="slotIndex">区域内槽位索引。</param>
    /// <param name="tableIndex">桌位索引。</param>
    /// <param name="worldPosition">世界坐标。</param>
    /// <param name="initialWorldPosition">首次显示时的起始世界坐标。</param>
    /// <param name="useInitialWorldPositionOnShow">首次显示时是否使用起始世界坐标。</param>
    public PetEntityData(
        int petInstanceId,
        string petCode,
        PetPlacementType placementType,
        int slotIndex,
        int tableIndex,
        Vector3 worldPosition,
        Vector3 initialWorldPosition,
        bool useInitialWorldPositionOnShow)
    {
        PetInstanceId = petInstanceId;
        PetCode = petCode;
        PlacementType = placementType;
        SlotIndex = slotIndex;
        TableIndex = tableIndex;
        WorldPosition = worldPosition;
        InitialWorldPosition = initialWorldPosition;
        UseInitialWorldPositionOnShow = useInitialWorldPositionOnShow;
    }
}
