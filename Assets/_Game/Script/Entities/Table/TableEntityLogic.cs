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
    /// 桌子的精灵渲染器缓存。
    /// 未解锁时用于替换 Level 0 占位精灵。
    /// </summary>
    private SpriteRenderer _spriteRenderer;

    /// <summary>
    /// prefab 上配置的默认颜色缓存。
    /// </summary>
    private Color _defaultColor = Color.white;

    /// <summary>
    /// prefab 上配置的默认精灵缓存。
    /// 未解锁时被替换为 Level 0 占位精灵，解锁后需要恢复。
    /// </summary>
    private Sprite _defaultSprite;

    /// <summary>
    /// 是否已经缓存过 prefab 默认颜色。
    /// </summary>
    private bool _hasCachedDefaultColor;

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
    /// 未解锁时从配置表加载 Level 0 精灵替换正常外观。
    /// </summary>
    public void ApplyData(TableEntityData entityData)
    {
        if (entityData == null)
        {
            return;
        }

        CacheReferences();
        SetWorldPosition(entityData.WorldPosition);

        // 未解锁时替换为 Level 0 占位精灵；已解锁时根据等级加载对应精灵。
        if (!entityData.IsUnlocked)
        {
            ApplyLockedPlaceholderSprite(PlayerRuntimeModule.ArchitectureCategory.Diet);
        }
        else
        {
            ApplyLevelSprite(PlayerRuntimeModule.ArchitectureCategory.Diet, entityData.Level);
        }
    }

    /// <summary>
    /// 更新桌子实体世界位置。
    /// </summary>
    public void SetWorldPosition(Vector3 worldPosition)
    {
        CachedTransform.position = worldPosition;
    }

    /// <summary>
    /// 从 GameAssetModule 预加载缓存中读取 Level 0 的实体精灵并赋给 SpriteRenderer。
    /// </summary>
    /// <param name="category">建筑类别。</param>
    private void ApplyLockedPlaceholderSprite(PlayerRuntimeModule.ArchitectureCategory category)
    {
        if (_spriteRenderer == null || GameEntry.Fruits == null || GameEntry.GameAssets == null)
        {
            return;
        }

        string spritePath = GameEntry.Fruits.GetEntitySpritePath(category, 0);
        if (string.IsNullOrEmpty(spritePath))
        {
            return;
        }

        if (GameEntry.GameAssets.TryGetArchitectureSprite(spritePath, out Sprite loadedSprite) && loadedSprite != null)
        {
            _spriteRenderer.sprite = loadedSprite;
        }
    }

    /// <summary>
    /// 从 GameAssetModule 预加载缓存中读取指定等级的实体精灵并赋给 SpriteRenderer。
    /// 如果缓存中没有对应等级的精灵，则恢复 prefab 原始精灵。
    /// </summary>
    /// <param name="category">建筑类别。</param>
    /// <param name="level">建筑等级。</param>
    private void ApplyLevelSprite(PlayerRuntimeModule.ArchitectureCategory category, int level)
    {
        if (_spriteRenderer == null || GameEntry.Fruits == null)
        {
            return;
        }

        string spritePath = GameEntry.Fruits.GetEntitySpritePath(category, level);
        if (GameEntry.GameAssets != null && !string.IsNullOrEmpty(spritePath))
        {
            if (GameEntry.GameAssets.TryGetArchitectureSprite(spritePath, out Sprite loadedSprite) && loadedSprite != null)
            {
                _spriteRenderer.sprite = loadedSprite;
                if (_hasCachedDefaultColor)
                {
                    _spriteRenderer.color = _defaultColor;
                }
                return;
            }
        }

        return;
    }

    /// <summary>
    /// 缓存桌子的挂点引用和精灵渲染器。
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

        if (_spriteRenderer == null)
        {
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
            if (_spriteRenderer != null)
            {
                _defaultColor = _spriteRenderer.color;
                _defaultSprite = _spriteRenderer.sprite;
                _hasCachedDefaultColor = true;
            }
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
