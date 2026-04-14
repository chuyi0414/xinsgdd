using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 桌子实体逻辑。
/// </summary>
public sealed class TableEntityLogic : EntityLogic
{
    /// <summary>
    /// 宠物入座挂点路径。
    /// </summary>
    public const string DiningAnchorTransformPath = "Pet";

    /// <summary>
    /// 食物挂点路径。
    /// </summary>
    public const string FoodAnchorTransformPath = "Fruit";

    /// <summary>
    /// 宠物入座挂点。
    /// </summary>
    private Transform _diningAnchor;

    /// <summary>
    /// 食物挂点。
    /// </summary>
    private Transform _foodAnchor;

    /// <summary>
    /// 对外暴露的宠物入座挂点。
    /// </summary>
    public Transform DiningAnchor => _diningAnchor;

    /// <summary>
    /// 对外暴露的食物挂点。
    /// </summary>
    public Transform FoodAnchor => _foodAnchor;

    /// <summary>
    /// 初始化并缓存挂点引用。
    /// </summary>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        CacheReferences();
    }

    /// <summary>
    /// 实体显示时应用最新显示数据。
    /// </summary>
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        ApplyData(userData as TableEntityData);
    }

    /// <summary>
    /// 应用桌子实体显示数据。
    /// </summary>
    public void ApplyData(TableEntityData entityData)
    {
        if (entityData == null)
        {
            return;
        }

        SetWorldPosition(entityData.WorldPosition);
    }

    /// <summary>
    /// 更新桌子实体世界位置。
    /// </summary>
    public void SetWorldPosition(Vector3 worldPosition)
    {
        CachedTransform.position = worldPosition;
    }

    /// <summary>
    /// 缓存桌子的挂点引用。
    /// </summary>
    private void CacheReferences()
    {
        if (_diningAnchor == null)
        {
            _diningAnchor = CachedTransform.Find(DiningAnchorTransformPath);
        }

        if (_foodAnchor == null)
        {
            _foodAnchor = CachedTransform.Find(FoodAnchorTransformPath);
        }

        if (_diningAnchor == null)
        {
            Log.Warning("TableEntityLogic can not find dining anchor '{0}'.", DiningAnchorTransformPath);
        }

        if (_foodAnchor == null)
        {
            Log.Warning("TableEntityLogic can not find food anchor '{0}'.", FoodAnchorTransformPath);
        }
    }
}
