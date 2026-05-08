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
    /// 宠物正常行走时的渲染顺序。
    /// 高于蛋实体(EggSortingOrder=10)，保证宠物走在蛋上方。
    /// </summary>
    private const int PetNormalSortingOrder = 20;

    /// <summary>
    /// 宠物在孵化器内时的渲染顺序。
    /// 低于孵化器(IncubatorSortingOrder=0)，保证孵化器框架覆盖宠物。
    /// </summary>
    private const int PetBehindIncubatorSortingOrder = 0;

    /// <summary>
    /// Spine 动画组件。
    /// </summary>
    private SkeletonAnimation _skeletonAnimation;

    /// <summary>
    /// 当前已经应用到实体上的宠物 Code。
    /// </summary>
    private string _currentPetCode;

    /// <summary>
    /// 当前等待按需加载 SkeletonData 的宠物 Code。
    /// 初始状态为空；当实体显示非默认宠物但缓存中暂时没有对应 SkeletonData 时写入，用于资源加载完成回调中精确重刷当前实体。
    /// </summary>
    private string _pendingPetCode;

    /// <summary>
    /// 当前已经发起过 SkeletonData 按需请求的宠物 Code。
    /// 初始状态为空；用于防止资源路径配置错误或加载失败时，被 PetSkeletonDataStateChanged 事件反复触发同一路径重试。
    /// </summary>
    private string _requestedPetCode;

    /// <summary>
    /// 当前正在等待加载完成的实体 SkeletonData 资源路径。
    /// 初始状态为空；资源成功或失败回调到达后用它过滤无关宠物资源事件。
    /// </summary>
    private string _pendingSkeletonDataPath;

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
    /// 当前奖励表现动画对应的 Spine TrackEntry。
    /// 该字段只在播放 Attack/Attack1 的非循环动画期间持有，用于隐藏或复用实体时解除 Complete 订阅。
    /// </summary>
    private TrackEntry _rewardAnimationTrackEntry;

    /// <summary>
    /// 当前奖励表现动画结束后的回调。
    /// 回调参数是宠物实例 Id，便于订单组件精确完成对应宠物的 RewardAnimating 状态。
    /// </summary>
    private Action<int> _rewardAnimationCompleteCallback;

    /// <summary>
    /// 当前正在播放奖励表现动画的宠物实例 Id。
    /// 初始值为 0，表示没有进行中的奖励动画。
    /// </summary>
    private int _rewardAnimationPetInstanceId;

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
    /// 当前是否正在播放吃完饭后的奖励表现动画。
    /// 场地刷新会用它避免重复播放 Idle/Move 打断 Attack/Attack1。
    /// </summary>
    public bool IsPlayingRewardAnimation => _rewardAnimationTrackEntry != null;

    /// <summary>
    /// 对外暴露的 Bubble 挂点。
    /// </summary>
    public Transform BubbleAnchor => _bubbleAnchor;

    /// <summary>
    /// 初始化并缓存 Spine 组件。
    /// </summary>
    protected override void OnInit(object userData)
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
        ReleaseGameAssetPreloadStateSubscription();
        _pendingPetCode = null;
        _requestedPetCode = null;
        _pendingSkeletonDataPath = null;
        ClearRewardAnimationCallback();
        StopMoveTween();
        base.OnHide(isShutdown, userData);
    }

    /// <summary>
    /// 挂接到父实体时重置局部变换。
    /// </summary>
    protected override void OnAttachTo(EntityLogic parentEntity, Transform parentTransform, object userData)
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
        ClearRewardAnimationCallback();
        if (!TryGetCurrentPetDataRow(out PetDataRow petDataRow))
        {
            return;
        }

        PlayAnimation(petDataRow.IdleAnimationName);
    }

    /// <summary>
    /// 播放宠物吃完饭后的奖励表现动画。
    /// 成功播放时会在动画 Complete 后自动恢复待机动画，并把宠物实例 Id 回传给订单组件。
    /// </summary>
    /// <param name="petInstanceId">当前宠物实例 Id。</param>
    /// <param name="onComplete">奖励表现动画完成回调。</param>
    /// <param name="animationDuration">实际播放动画的时长，供订单组件设置兜底超时。</param>
    /// <returns>是否成功开始播放非循环奖励动画。</returns>
    public bool TryPlayGiveGoldAnimation(int petInstanceId, Action<int> onComplete, out float animationDuration)
    {
        animationDuration = 0f;
        ClearRewardAnimationCallback();
        CacheComponents();
        if (petInstanceId <= 0 || _skeletonAnimation == null || _skeletonAnimation.AnimationState == null)
        {
            return false;
        }

        if (!TryGetCurrentPetDataRow(out PetDataRow petDataRow) || string.IsNullOrWhiteSpace(petDataRow.GiveGoldAnimationName))
        {
            return false;
        }

        if (_skeletonAnimation.Skeleton == null || _skeletonAnimation.Skeleton.Data == null)
        {
            return false;
        }

        Spine.Animation giveGoldAnimation = _skeletonAnimation.Skeleton.Data.FindAnimation(petDataRow.GiveGoldAnimationName);
        if (giveGoldAnimation == null)
        {
            Log.Warning("PetEntityLogic can not play give gold animation '{0}' because animation is missing, pet code '{1}'.", petDataRow.GiveGoldAnimationName, petDataRow.Code);
            return false;
        }

        animationDuration = giveGoldAnimation.Duration;
        _rewardAnimationPetInstanceId = petInstanceId;
        _rewardAnimationCompleteCallback = onComplete;
        _rewardAnimationTrackEntry = _skeletonAnimation.AnimationState.SetAnimation(0, giveGoldAnimation, false);
        _rewardAnimationTrackEntry.Complete += OnRewardAnimationComplete;
        return true;
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

            // 首次缓存时设置排序层与渲染顺序。
            // 改为与孵化器/蛋同处 Default 层，靠 sortingOrder 动态控制前后。
            // 默认 PetNormalSortingOrder=20（走在蛋上方），
            // 进入孵化器触发区时由 IncubatorEntityLogic 调用 SetBehindIncubator 降为 -10。
            if (_skeletonAnimation != null)
            {
                MeshRenderer meshRenderer = _skeletonAnimation.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    meshRenderer.sortingLayerName = "Default";
                    meshRenderer.sortingOrder = PetNormalSortingOrder;
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
    /// 设置宠物是否渲染在孵化器后方。
    /// 由 IncubatorEntityLogic 的触发器调用：
    /// 宠物进入孵化区触发器 → behind=true，sortingOrder 降为 -10，被孵化器覆盖；
    /// 宠物离开孵化区触发器 → behind=false，sortingOrder 恢复 20，走在蛋上方。
    /// </summary>
    /// <param name="behind">true=退到孵化器后方；false=恢复正常层级。</param>
    public void SetBehindIncubator(bool behind)
    {
        CacheComponents();
        if (_skeletonAnimation == null)
        {
            return;
        }

        MeshRenderer meshRenderer = _skeletonAnimation.GetComponent<MeshRenderer>();
        if (meshRenderer != null)
        {
            meshRenderer.sortingOrder = behind
                ? PetBehindIncubatorSortingOrder
                : PetNormalSortingOrder;
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
            _pendingPetCode = petCode;
            _pendingSkeletonDataPath = petDataRow.EntitySkeletonDataPath;
            SetSkeletonVisible(false);
            EnsureGameAssetPreloadStateSubscription();
            if (GameEntry.GameAssets != null && !string.Equals(_requestedPetCode, petCode, System.StringComparison.Ordinal))
            {
                _requestedPetCode = petCode;
                GameEntry.GameAssets.RequestPetEntitySkeletonDataAsset(petDataRow);
            }

            return;
        }

        _pendingPetCode = null;
        _requestedPetCode = null;
        _pendingSkeletonDataPath = null;
        SetSkeletonVisible(true);

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
    /// 确保当前实体已经监听宠物 SkeletonData 加载状态。
    /// 宠物实体只在 SkeletonData 缺失时临时监听指定资源路径，资源补齐或失败后会立即解绑，避免常驻事件订阅。
    /// </summary>
    private void EnsureGameAssetPreloadStateSubscription()
    {
        if (GameEntry.GameAssets == null)
        {
            return;
        }

        GameEntry.GameAssets.PetSkeletonDataStateChanged -= OnPetSkeletonDataStateChanged;
        GameEntry.GameAssets.PetSkeletonDataStateChanged += OnPetSkeletonDataStateChanged;
    }

    /// <summary>
    /// 取消宠物 SkeletonData 加载状态监听。
    /// 实体隐藏或资源补齐后调用，防止对象池复用时旧宠物 Code 的回调污染新实体。
    /// </summary>
    private void ReleaseGameAssetPreloadStateSubscription()
    {
        if (GameEntry.GameAssets == null)
        {
            return;
        }

        GameEntry.GameAssets.PetSkeletonDataStateChanged -= OnPetSkeletonDataStateChanged;
    }

    /// <summary>
    /// 宠物 SkeletonData 加载状态变化回调。
    /// 当按需请求的同一路径 SkeletonData 加载完成后，重新执行 ApplyPetVisual，把实体从预制体默认外观切换为真实孵化出的宠物外观。
    /// </summary>
    /// <param name="skeletonDataPath">发生变化的 SkeletonData 资源路径。</param>
    private void OnPetSkeletonDataStateChanged(string skeletonDataPath)
    {
        if (string.IsNullOrWhiteSpace(_pendingPetCode) || string.IsNullOrWhiteSpace(_pendingSkeletonDataPath))
        {
            ReleaseGameAssetPreloadStateSubscription();
            return;
        }

        if (!string.Equals(_pendingSkeletonDataPath, skeletonDataPath, System.StringComparison.Ordinal))
        {
            return;
        }

        string pendingPetCode = _pendingPetCode;
        ApplyPetVisual(pendingPetCode);
        if (string.IsNullOrWhiteSpace(_pendingPetCode))
        {
            ReleaseGameAssetPreloadStateSubscription();
            return;
        }

        Log.Warning("PetEntityLogic can not apply pet visual after SkeletonData state changed, pet code '{0}', path '{1}'.", pendingPetCode, skeletonDataPath);
        _pendingPetCode = null;
        _requestedPetCode = null;
        _pendingSkeletonDataPath = null;
        ReleaseGameAssetPreloadStateSubscription();
    }

    /// <summary>
    /// 控制 Spine 渲染节点显隐。
    /// 当真实宠物资源尚未加载完成时先隐藏预制体默认 Skeleton，避免玩家看到“孵化出来全是默认宠物”的错误表现。
    /// </summary>
    /// <param name="isVisible">是否显示 Spine 渲染对象。</param>
    private void SetSkeletonVisible(bool isVisible)
    {
        CacheComponents();
        if (_skeletonAnimation == null || _skeletonAnimation.gameObject.activeSelf == isVisible)
        {
            return;
        }

        _skeletonAnimation.gameObject.SetActive(isVisible);
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
        ClearRewardAnimationCallback();
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
    /// Spine 奖励表现动画完成回调。
    /// </summary>
    /// <param name="trackEntry">完成播放的 Spine TrackEntry。</param>
    private void OnRewardAnimationComplete(TrackEntry trackEntry)
    {
        if (!ReferenceEquals(trackEntry, _rewardAnimationTrackEntry))
        {
            return;
        }

        int petInstanceId = _rewardAnimationPetInstanceId;
        Action<int> completeCallback = _rewardAnimationCompleteCallback;
        ClearRewardAnimationCallback();

        if (TryGetCurrentPetDataRow(out PetDataRow petDataRow))
        {
            PlayAnimation(petDataRow.IdleAnimationName);
        }

        completeCallback?.Invoke(petInstanceId);
    }

    /// <summary>
    /// 清理奖励表现动画回调缓存。
    /// 该方法不会停止 Spine 当前动画，只负责解除事件订阅，避免对象池复用后旧回调误触发。
    /// </summary>
    private void ClearRewardAnimationCallback()
    {
        if (_rewardAnimationTrackEntry != null)
        {
            _rewardAnimationTrackEntry.Complete -= OnRewardAnimationComplete;
        }

        _rewardAnimationTrackEntry = null;
        _rewardAnimationCompleteCallback = null;
        _rewardAnimationPetInstanceId = 0;
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
