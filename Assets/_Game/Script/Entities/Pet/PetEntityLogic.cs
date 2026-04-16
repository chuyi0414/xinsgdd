using System;
using DG.Tweening;
using Spine;
using Spine.Unity;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 宠物实体逻辑。
/// </summary>
public sealed class PetEntityLogic : EntityLogic
{
    /// <summary>
    /// 宠物移动速度，单位为世界坐标单位每秒。
    /// </summary>
    private const float MoveSpeed = 2f;

    /// <summary>
    /// 宠物资源默认朝向。
    /// 当前项目所有宠物默认朝左。
    /// </summary>
    private const int DefaultFacingDirection = -1;

    /// <summary>
    /// Spine 动画组件。
    /// </summary>
    private SkeletonAnimation _skeletonAnimation;

    /// <summary>
    /// 当前已经应用到实体上的宠物 Code。
    /// </summary>
    private string _currentPetCode;

    /// <summary>
    /// Spine Skeleton 初始化后的默认 ScaleX。
    /// 用于在运行时朝向切换时保留 Spine 自身的翻转语义。
    /// </summary>
    private float _defaultSkeletonScaleX = 1f;

    /// <summary>
    /// 当前朝向，1 表示朝右，-1 表示朝左。
    /// </summary>
    private int _facingDirection = DefaultFacingDirection;

    /// <summary>
    /// 当前正在执行的位移 Tween。
    /// </summary>
    private Tweener _moveTween;

    /// <summary>
    /// 宠物头顶 Bubble 挂点。
    /// UI 会把屏幕空间气泡投影到这里。
    /// </summary>
    [SerializeField]
    private Transform _bubbleAnchor;

    /// <summary>
    /// 是否已经输出过缺失 SkeletonAnimation 的警告。
    /// </summary>
    private bool _hasLoggedMissingSkeletonAnimation;

    /// <summary>
    /// 是否已经输出过缺失 Bubble 挂点的警告。
    /// </summary>
    private bool _hasLoggedMissingBubbleAnchor;

    /// <summary>
    /// 当前是否仍在执行位移动画。
    /// </summary>
    public bool IsMoving => _moveTween != null && _moveTween.IsActive();

    /// <summary>
    /// 对外暴露的 Bubble 挂点。
    /// </summary>
    public Transform BubbleAnchor => _bubbleAnchor;

    /// <summary>
    /// 初始化并缓存 Spine 组件。
    /// </summary>
    protected  override void OnInit(object userData)
    {
        base.OnInit(userData);
        CacheComponents();
    }

    /// <summary>
    /// 实体显示时应用最新显示数据。
    /// </summary>
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        ApplyData(userData as PetEntityData);
    }

    /// <summary>
    /// 实体隐藏时停止位移动画。
    /// </summary>
    protected override void OnHide(bool isShutdown, object userData)
    {
        StopMoveTween();
        base.OnHide(isShutdown, userData);
    }

    /// <summary>
    /// 挂接到父实体时重置局部变换。
    /// </summary>
    protected  override void OnAttachTo(EntityLogic parentEntity, Transform parentTransform, object userData)
    {
        CachedTransform.SetParent(parentTransform, false);
        CachedTransform.localPosition = Vector3.zero;
        CachedTransform.localRotation = Quaternion.identity;
        CachedTransform.localScale = Vector3.one;
    }

    /// <summary>
    /// 从父实体脱离后重置局部旋转和缩放。
    /// </summary>
    protected override void OnDetachFrom(EntityLogic parentEntity, object userData)
    {
        base.OnDetachFrom(parentEntity, userData);
        CachedTransform.localRotation = Quaternion.identity;
        CachedTransform.localScale = Vector3.one;
    }

    /// <summary>
    /// 应用宠物实体显示数据。
    /// </summary>
    public void ApplyData(PetEntityData entityData)
    {
        if (entityData == null)
        {
            return;
        }

        if (entityData.UseInitialWorldPositionOnShow)
        {
            SnapToWorldPosition(entityData.InitialWorldPosition);
        }
        else
        {
            SnapToWorldPosition(entityData.WorldPosition);
        }

        ApplyPetVisual(entityData.PetCode);
    }

    /// <summary>
    /// 立即更新宠物实体世界位置。
    /// </summary>
    public void SnapToWorldPosition(Vector3 worldPosition)
    {
        StopMoveTween();
        CachedTransform.position = worldPosition;
    }

    /// <summary>
    /// 平滑移动到指定世界坐标。
    /// </summary>
    public void MoveToWorldPosition(Vector3 worldPosition, Action onComplete = null)
    {
        StopMoveTween();

        Vector3 startWorldPosition = CachedTransform.position;
        float distance = Vector3.Distance(startWorldPosition, worldPosition);
        if (distance <= 0.001f)
        {
            CachedTransform.position = worldPosition;
            onComplete?.Invoke();
            return;
        }

        UpdateFacingDirection(worldPosition - startWorldPosition);
        PlayMoveAnimation();
        float duration = distance / MoveSpeed;
        _moveTween = CachedTransform.DOMove(worldPosition, duration)
            .SetEase(Ease.Linear)
            .SetUpdate(true)
            .OnComplete(() =>
            {
                _moveTween = null;
                onComplete?.Invoke();
            });
    }

    /// <summary>
    /// 兼容旧调用的立即定位接口。
    /// </summary>
    public void SetWorldPosition(Vector3 worldPosition)
    {
        SnapToWorldPosition(worldPosition);
    }

    /// <summary>
    /// 播放当前宠物的待机动画。
    /// </summary>
    public void PlayIdleAnimation()
    {
        if (!TryGetCurrentPetDataRow(out PetDataRow petDataRow))
        {
            return;
        }

        PlayAnimation(petDataRow.IdleAnimationName);
    }

    /// <summary>
    /// 播放当前宠物的移动动画。
    /// </summary>
    public void PlayMoveAnimation()
    {
        if (!TryGetCurrentPetDataRow(out PetDataRow petDataRow))
        {
            return;
        }

        PlayAnimation(petDataRow.MoveAnimationName);
    }

    /// <summary>
    /// 缓存 Spine 动画组件。
    /// </summary>
    private void CacheComponents()
    {
        if (_skeletonAnimation == null)
        {
            _skeletonAnimation = GetComponentInChildren<SkeletonAnimation>(true);

            // 首次缓存时设置排序层，避免每次换宠物重复赋值。
            if (_skeletonAnimation != null)
            {
                MeshRenderer meshRenderer = _skeletonAnimation.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    meshRenderer.sortingLayerName = "Pet";
                }
            }
        }

        if (_skeletonAnimation == null && !_hasLoggedMissingSkeletonAnimation)
        {
            _hasLoggedMissingSkeletonAnimation = true;
            Log.Warning("PetEntityLogic can not find SkeletonAnimation.");
        }

        if (_bubbleAnchor == null && !_hasLoggedMissingBubbleAnchor)
        {
            _hasLoggedMissingBubbleAnchor = true;
            Log.Warning("PetEntityLogic bubble anchor is not assigned.");
        }
    }

    /// <summary>
    /// 按宠物配置刷新实体外观与待机动画。
    /// </summary>
    private void ApplyPetVisual(string petCode)
    {
        CacheComponents();
        if (_skeletonAnimation == null || string.IsNullOrWhiteSpace(petCode) || GameEntry.DataTables == null)
        {
            return;
        }

        PetDataRow petDataRow = GameEntry.DataTables.GetDataRowByCode<PetDataRow>(petCode);
        if (petDataRow == null)
        {
            Log.Warning("PetEntityLogic can not find pet data row by code '{0}'.", petCode);
            return;
        }

        SkeletonDataAsset skeletonDataAsset = null;
        if (GameEntry.GameAssets != null)
        {
            GameEntry.GameAssets.TryGetPetSkeletonDataAsset(petDataRow.EntitySkeletonDataPath, out skeletonDataAsset);
        }

        if (skeletonDataAsset == null)
        {
            Log.Warning("PetEntityLogic can not find cached entity skeleton data by path '{0}'.", petDataRow.EntitySkeletonDataPath);
            return;
        }

        if (_skeletonAnimation.skeletonDataAsset != skeletonDataAsset || !string.Equals(_currentPetCode, petCode, System.StringComparison.Ordinal))
        {
            _skeletonAnimation.skeletonDataAsset = skeletonDataAsset;
            _skeletonAnimation.initialSkinName = "default";
            _skeletonAnimation.Initialize(true);
            CacheDefaultSkeletonScaleX();
            _currentPetCode = petCode;
        }

        ApplyFacingDirection(_facingDirection);
        PlayAnimation(petDataRow.IdleAnimationName);
    }

    /// <summary>
    /// 根据移动向量更新当前朝向。
    /// 仅在 X 方向有明显变化时翻转。
    /// </summary>
    private void UpdateFacingDirection(Vector3 moveDelta)
    {
        if (moveDelta.x >= 0.001f)
        {
            ApplyFacingDirection(1);
        }
        else if (moveDelta.x <= -0.001f)
        {
            ApplyFacingDirection(-1);
        }
    }

    /// <summary>
    /// 应用当前朝向到 Spine 渲染节点。
    /// </summary>
    private void ApplyFacingDirection(int facingDirection)
    {
        CacheComponents();
        if (_skeletonAnimation == null || _skeletonAnimation.Skeleton == null)
        {
            return;
        }

        _facingDirection = facingDirection >= 0 ? 1 : -1;
        float defaultScaleX = Mathf.Abs(_defaultSkeletonScaleX) <= 0.0001f ? 1f : _defaultSkeletonScaleX;
        _skeletonAnimation.Skeleton.ScaleX = _facingDirection == DefaultFacingDirection
            ? defaultScaleX
            : -defaultScaleX;
    }

    /// <summary>
    /// 缓存 Skeleton 初始化后的默认 ScaleX。
    /// </summary>
    private void CacheDefaultSkeletonScaleX()
    {
        if (_skeletonAnimation == null || _skeletonAnimation.Skeleton == null)
        {
            return;
        }

        float scaleX = _skeletonAnimation.Skeleton.ScaleX;
        _defaultSkeletonScaleX = Mathf.Abs(scaleX) <= 0.0001f ? 1f : scaleX;
    }

    /// <summary>
    /// 获取当前宠物的配置行。
    /// </summary>
    private bool TryGetCurrentPetDataRow(out PetDataRow petDataRow)
    {
        petDataRow = null;
        if (string.IsNullOrWhiteSpace(_currentPetCode) || GameEntry.DataTables == null)
        {
            return false;
        }

        petDataRow = GameEntry.DataTables.GetDataRowByCode<PetDataRow>(_currentPetCode);
        return petDataRow != null;
    }

    /// <summary>
    /// 播放指定名称的循环动画。
    /// </summary>
    private void PlayAnimation(string animationName)
    {
        if (_skeletonAnimation == null || _skeletonAnimation.AnimationState == null || string.IsNullOrWhiteSpace(animationName))
        {
            return;
        }

        TrackEntry currentTrack = _skeletonAnimation.AnimationState.GetCurrent(0);
        if (currentTrack != null
            && currentTrack.Animation != null
            && string.Equals(currentTrack.Animation.Name, animationName, System.StringComparison.Ordinal))
        {
            return;
        }

        _skeletonAnimation.AnimationState.SetAnimation(0, animationName, true);
    }

    /// <summary>
    /// 停止当前位移动画。
    /// </summary>
    private void StopMoveTween()
    {
        if (_moveTween == null)
        {
            return;
        }

        _moveTween.Kill();
        _moveTween = null;
    }
}
