using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 孵化器实体逻辑。
/// 负责把孵化器稳定投影到孵化槽世界坐标，并固定渲染层级。
/// </summary>
public sealed class IncubatorEntityLogic : EntityLogic
{
    /// <summary>
    /// 孵化器渲染顺序。
    /// 必须低于蛋实体，保证蛋总是显示在孵化器上层。
    /// </summary>
    private const int IncubatorSortingOrder = 0;

    /// <summary>
    /// 孵化器的精灵渲染器缓存。
    /// 当前 prefab 只有一个主要 SpriteRenderer，这里缓存后可避免重复查找。
    /// </summary>
    private SpriteRenderer _spriteRenderer;

    /// <summary>
    /// 宠物孵化出生挂点。
    /// 初始状态由 Inspector 手动拖入 IncubatorEntity prefab 内的 PetGenericPoint 节点。
    /// </summary>
    [SerializeField]
    private Transform _petGenericPoint;

    /// <summary>
    /// 孵化器 prefab 上配置的默认颜色。
    /// 这里必须缓存下来，避免实体复用后颜色被运行时状态污染。
    /// </summary>
    private Color _defaultColor = Color.white;

    /// <summary>
    /// 孵化器 prefab 上配置的默认精灵。
    /// 未解锁时被替换为 Level 0 占位精灵，解锁后需要恢复。
    /// </summary>
    private Sprite _defaultSprite;

    /// <summary>
    /// 是否已经成功缓存过 prefab 默认颜色。
    /// 只在第一次拿到 SpriteRenderer 时记录，后续始终恢复这一份原始颜色。
    /// </summary>
    private bool _hasCachedDefaultColor;

    /// <summary>
    /// 初始化并缓存常用组件。
    /// </summary>
    /// <param name="userData">框架透传的初始化数据，这里不直接使用。</param>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        CacheReferences();
    }

    /// <summary>
    /// 实体显示时应用最新显示数据。
    /// </summary>
    /// <param name="userData">显示阶段透传的业务数据，应为 <see cref="IncubatorEntityData"/>。</param>
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        ApplyData(userData as IncubatorEntityData);
    }

    /// <summary>
    /// 应用孵化器实体显示数据。
    /// 未解锁时从配置表加载 Level 0 精灵替换正常外观。
    /// </summary>
    /// <param name="entityData">孵化器显示数据。</param>
    public void ApplyData(IncubatorEntityData entityData)
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
            ApplyLockedPlaceholderSprite(PlayerRuntimeModule.ArchitectureCategory.Hatch);
        }
        else
        {
            ApplyLevelSprite(PlayerRuntimeModule.ArchitectureCategory.Hatch, entityData.Level);
        }
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
    /// 更新孵化器实体世界位置。
    /// </summary>
    /// <param name="worldPosition">目标世界坐标。</param>
    public void SetWorldPosition(Vector3 worldPosition)
    {
        CachedTransform.position = worldPosition;
    }

    /// <summary>
    /// 获取宠物孵化出生挂点的世界坐标。
    /// </summary>
    /// <param name="worldPosition">命中 PetGenericPoint 时输出该挂点的世界坐标。</param>
    /// <returns>是否成功获取到有效的宠物出生挂点。</returns>
    public bool TryGetPetGenericWorldPosition(out Vector3 worldPosition)
    {
        CacheReferences();
        if (_petGenericPoint != null)
        {
            worldPosition = _petGenericPoint.position;
            return true;
        }

        worldPosition = Vector3.zero;
        return false;
    }

    /// <summary>
    /// 缓存渲染组件并恢复 prefab 原始显示设置。
    /// 这里会持续强制写入 sortingOrder，
    /// 但颜色必须恢复成 prefab 默认值，不能写死成白色，否则会覆盖美术在预制体上的配色。
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

        if (_spriteRenderer == null)
        {
            Log.Warning("IncubatorEntityLogic can not find SpriteRenderer.");
            return;
        }

        _spriteRenderer.sortingOrder = IncubatorSortingOrder;
        if (_hasCachedDefaultColor)
        {
            _spriteRenderer.color = _defaultColor;
        }
    }
}
