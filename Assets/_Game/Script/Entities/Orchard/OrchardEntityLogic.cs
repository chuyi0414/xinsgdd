using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 果园实体逻辑。
/// </summary>
public sealed class OrchardEntityLogic : EntityLogic
{
    /// <summary>
    /// 水果出生挂点。
    /// 由 Inspector 手动拖入 OrchardEntity prefab 内的子节点，
    /// 水果生成时将以此点作为初始世界位置与挂接后的局部坐标。
    /// </summary>
    [SerializeField]
    private Transform _fruitGenericPoint;

    /// <summary>
    /// 果树的精灵渲染器缓存。
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
    /// 初始化并缓存渲染组件。
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
        ApplyData(userData as OrchardEntityData);
    }

    /// <summary>
    /// 应用果园实体显示数据。
    /// 未解锁时从配置表加载 Level 0 精灵替换正常外观。
    /// </summary>
    public void ApplyData(OrchardEntityData entityData)
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
            ApplyLockedPlaceholderSprite(PlayerRuntimeModule.ArchitectureCategory.Fruiter);
        }
        else
        {
            ApplyLevelSprite(PlayerRuntimeModule.ArchitectureCategory.Fruiter, entityData.Level);
        }
    }

    /// <summary>
    /// 更新果园实体世界位置。
    /// </summary>
    /// <param name="worldPosition">目标世界坐标。</param>
    public void SetWorldPosition(Vector3 worldPosition)
    {
        CachedTransform.position = worldPosition;
    }

    /// <summary>
    /// 获取水果出生挂点的世界坐标。
    /// 供 PlayfieldEntityModule 在创建果树水果时使用，
    /// 优先返回 prefab 上手动拖入的 _fruitGenericPoint 世界坐标。
    /// </summary>
    /// <param name="worldPosition">命中 _fruitGenericPoint 时输出该挂点的世界坐标。</param>
    /// <returns>是否成功获取到有效的水果出生挂点。</returns>
    public bool TryGetFruitGenericWorldPosition(out Vector3 worldPosition)
    {
        if (_fruitGenericPoint != null)
        {
            worldPosition = _fruitGenericPoint.position;
            return true;
        }

        worldPosition = Vector3.zero;
        return false;
    }

    /// <summary>
    /// 获取水果出生挂点相对于果树根节点的局部坐标。
    /// 供水果 AttachEntity 后设置正确的 localPosition 使用，
    /// 使水果在挂接为果树子节点后仍对齐到 _fruitGenericPoint 的位置。
    /// </summary>
    /// <param name="localPosition">命中 _fruitGenericPoint 时输出该挂点的局部坐标。</param>
    /// <returns>是否成功获取到有效的水果出生挂点局部坐标。</returns>
    public bool TryGetFruitGenericLocalPosition(out Vector3 localPosition)
    {
        if (_fruitGenericPoint != null)
        {
            localPosition = _fruitGenericPoint.localPosition;
            return true;
        }

        localPosition = Vector3.zero;
        return false;
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
    /// 缓存渲染组件并恢复 prefab 原始显示设置。
    /// </summary>
    private void CacheReferences()
    {
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
    }
}
