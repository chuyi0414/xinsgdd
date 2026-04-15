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
    /// 孵化器 prefab 上配置的默认颜色。
    /// 这里必须缓存下来，避免实体复用后颜色被运行时状态污染。
    /// </summary>
    private Color _defaultColor = Color.white;

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
