using UnityEngine;

/// <summary>
/// 消除卡片实体显示数据。
/// </summary>
public sealed class EliminateCardEntityData
{
    /// <summary>
    /// 卡片布局索引。
    /// 主要用于调试和后续扩展点击链路时定位具体卡片。
    /// </summary>
    public int LayoutIndex { get; }

    /// <summary>
    /// 卡片类型 Id。
    /// 当前主要用于区分显示颜色与后续玩法扩展。
    /// </summary>
    public int TypeId { get; }

    /// <summary>
    /// 卡片目标世界位置。
    /// </summary>
    public Vector3 WorldPosition { get; }

    /// <summary>
    /// 本张卡片的渲染排序值。
    /// </summary>
    public int SortingOrder { get; }

    /// <summary>
    /// 当前卡片应显示的精灵资源。
    /// 第一阶段直接把 Sprite 引用传给实体，避免在实体侧重复加载资源。
    /// </summary>
    public Sprite DisplaySprite { get; }

    /// <summary>
    /// 当前卡片显示颜色。
    /// 当真实卡池图标还没迁完整时，用颜色先把不同类型区分出来。
    /// </summary>
    public Color TintColor { get; }

    /// <summary>
    /// 当前卡片是否被上层遮挡。
    /// 被遮挡的卡片不可点击，且显示为置灰状态。
    /// </summary>
    public bool IsBlocked { get; }

    /// <summary>
    /// 构造一份消除卡片实体显示数据。
    /// </summary>
    public EliminateCardEntityData(
        int layoutIndex,
        int typeId,
        Vector3 worldPosition,
        int sortingOrder,
        Sprite displaySprite,
        Color tintColor,
        bool isBlocked = false)
    {
        LayoutIndex = layoutIndex;
        TypeId = typeId;
        WorldPosition = worldPosition;
        SortingOrder = sortingOrder;
        DisplaySprite = displaySprite;
        TintColor = tintColor;
        IsBlocked = isBlocked;
    }
}
