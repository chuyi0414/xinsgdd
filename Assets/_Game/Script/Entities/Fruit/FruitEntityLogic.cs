using System;
using DG.Tweening;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 餐桌水果实体逻辑。
/// 负责根据水果 Code 切换 Sprite，并跟随桌位食物挂点显示。
/// </summary>
public sealed class FruitEntityLogic : EntityLogic
{
    /// <summary>
    /// SpriteRenderer 缓存。
    /// </summary>
    private SpriteRenderer _spriteRenderer;

    /// <summary>
    /// 当前正在执行的缩放动画 Tween。
    /// </summary>
    private Tweener _growTween;

    /// <summary>
    /// 当前正在执行的送达移动动画 Tween。
    /// </summary>
    private Tweener _deliverTween;

    /// <summary>
    /// 预制体默认精灵缓存。
    /// 当业务图标加载失败时回退显示。
    /// </summary>
    private Sprite _defaultSprite;

    /// <summary>
    /// 预制体默认局部缩放缓存。
    /// 挂接到桌位食物锚点时需要恢复这个缩放，避免被父节点默认值覆盖。
    /// </summary>
    private Vector3 _defaultLocalScale = Vector3.one;

    /// <summary>
    /// 初始化并缓存所需组件。
    /// </summary>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        _defaultLocalScale = CachedTransform.localScale;
        CacheReferences();
    }

    /// <summary>
    /// 实体显示时应用最新显示数据。
    /// </summary>
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        KillAllTweens();
        ApplyData(userData as FruitEntityData);
    }

    /// <summary>
    /// 实体隐藏时清理动画。
    /// </summary>
    protected override void OnHide(bool isShutdown, object userData)
    {
        KillAllTweens();
        base.OnHide(isShutdown, userData);
    }

    /// <summary>
    /// 应用水果实体显示数据。
    /// </summary>
    /// <param name="entityData">水果实体显示数据。</param>
    public void ApplyData(FruitEntityData entityData)
    {
        if (entityData == null)
        {
            return;
        }

        SetWorldPosition(entityData.WorldPosition);
        ApplyFruitSprite(entityData.FruitCode);
    }

    /// <summary>
    /// 更新水果实体世界位置。
    /// </summary>
    /// <param name="worldPosition">目标世界坐标。</param>
    public void SetWorldPosition(Vector3 worldPosition)
    {
        CachedTransform.position = worldPosition;
    }

    /// <summary>
    /// 挂接到父实体时重置局部变换。
    /// </summary>
    protected override void OnAttachTo(EntityLogic parentEntity, Transform parentTransform, object userData)
    {
        CachedTransform.SetParent(parentTransform, false);
        CachedTransform.localPosition = Vector3.zero;
        CachedTransform.localRotation = Quaternion.identity;
        CachedTransform.localScale = _defaultLocalScale;
    }

    /// <summary>
    /// 从父实体脱离后恢复默认局部变换。
    /// </summary>
    protected override void OnDetachFrom(EntityLogic parentEntity, object userData)
    {
        base.OnDetachFrom(parentEntity, userData);
        CachedTransform.localRotation = Quaternion.identity;
        CachedTransform.localScale = _defaultLocalScale;
    }

    /// <summary>
    /// 缓存 SpriteRenderer 与默认精灵。
    /// </summary>
    private void CacheReferences()
    {
        if (_spriteRenderer == null)
        {
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
            if (_spriteRenderer != null)
            {
                _defaultSprite = _spriteRenderer.sprite;
            }
        }

        if (_spriteRenderer == null)
        {
            Log.Warning("FruitEntityLogic can not find SpriteRenderer.");
        }
    }

    /// <summary>
    /// 播放果树生长动画：缩放从 0 → targetScale。
    /// </summary>
    /// <param name="duration">动画时长（秒）。</param>
    /// <param name="targetScale">目标缩放值。</param>
    /// <param name="onComplete">动画完成回调。</param>
    public void PlayGrowAnimation(float duration, float targetScale, Action onComplete = null)
    {
        KillGrowTween();
        CachedTransform.localScale = Vector3.zero;

        _growTween = CachedTransform
            .DOScale(targetScale, duration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                _growTween = null;
                onComplete?.Invoke();
            });
    }

    /// <summary>
    /// 播放送达动画：从当前位置移动到目标世界坐标。
    /// </summary>
    /// <param name="targetWorldPos">目标世界坐标。</param>
    /// <param name="duration">动画时长（秒）。</param>
    /// <param name="onComplete">动画完成回调。</param>
    public void PlayDeliverAnimation(Vector3 targetWorldPos, float duration, Action onComplete = null)
    {
        KillDeliverTween();

        // 送达前先脱离父实体，这样才能在世界坐标下自由移动。
        // 这里绝对不能再把实体挂到 null 根节点。
        // 原因是 UGF 的实体实例默认都应该处于 EntityComponent 的托管树下，
        // 一旦挂到场景根节点，退出游戏或切场景时就可能被 Unity 提前销毁，
        // 但 EntityManager 仍然以为这个实体还活着，最终在 Shutdown -> OnHide 阶段触发 MissingReferenceException。
        // DetachEntity 之后，EntityLogic.OnDetachFrom 的基类实现会把节点恢复到 OnInit 时缓存的原始父节点，
        // 也就是对应的 EntityGroup 根节点，因此这里不再额外改父节点，只保留当前世界坐标即可。
        if (Entity != null && Entity.Id > 0 && GameEntry.Entity != null)
        {
            Entity parentEntity = GameEntry.Entity.GetParentEntity(Entity.Id);
            if (parentEntity != null)
            {
                GameEntry.Entity.DetachEntity(Entity.Id);
            }
        }

        _deliverTween = CachedTransform
            .DOMove(targetWorldPos, duration)
            .SetEase(Ease.InQuad)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                _deliverTween = null;
                onComplete?.Invoke();
            });
    }

    /// <summary>
    /// 终止所有动画。
    /// </summary>
    private void KillAllTweens()
    {
        KillGrowTween();
        KillDeliverTween();
    }

    /// <summary>
    /// 终止缩放动画。
    /// </summary>
    private void KillGrowTween()
    {
        if (_growTween != null)
        {
            _growTween.Kill();
            _growTween = null;
        }
    }

    /// <summary>
    /// 终止送达动画。
    /// </summary>
    private void KillDeliverTween()
    {
        if (_deliverTween != null)
        {
            _deliverTween.Kill();
            _deliverTween = null;
        }
    }

    /// <summary>
    /// 按水果 Code 切换显示图标。
    /// </summary>
    /// <param name="fruitCode">水果 Code。</param>
    private void ApplyFruitSprite(string fruitCode)
    {
        CacheReferences();
        if (_spriteRenderer == null || string.IsNullOrWhiteSpace(fruitCode) || GameEntry.GameAssets == null)
        {
            return;
        }

        if (!GameEntry.GameAssets.TryGetFruitSprite(fruitCode, out Sprite sprite) || sprite == null)
        {
            Log.Warning("FruitEntityLogic can not find fruit sprite by code '{0}', fallback to prefab default sprite.", fruitCode);
            sprite = _defaultSprite;
        }

        _spriteRenderer.sprite = sprite;
        _spriteRenderer.color = Color.white;
    }
}
