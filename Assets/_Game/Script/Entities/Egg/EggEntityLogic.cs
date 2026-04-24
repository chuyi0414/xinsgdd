using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 蛋实体逻辑。
/// </summary>
public sealed class EggEntityLogic : EntityLogic
{
    /// <summary>
    /// 蛋实体渲染顺序。
    /// 必须高于孵化器，保证蛋永远压在孵化器上层显示。
    /// </summary>
    private const int EggSortingOrder = 10;

    /// <summary>
    /// 蛋实体的精灵渲染器。
    /// </summary>
    private SpriteRenderer _spriteRenderer;

    /// <summary>
    /// 当前已经应用到实体上的蛋 Code。
    /// </summary>
    private string _currentEggCode;

    /// <summary>
    /// 预制体默认精灵，用于资源缺失时回退。
    /// </summary>
    private Sprite _defaultSprite;

    /// <summary>
    /// 动画
    /// </summary>
    [SerializeField] private Animator _animator;

    /// <summary>
    /// 当前蛋实体对应的孵化槽索引。
    /// 用于在结束动画播放完毕后通知场地模块隐藏本实体。
    /// </summary>
    private int _slotIndex;

    /// <summary>
    /// 当前是否正在播放孵化结束动画（蛋消失动画）。
    /// 动画期间不响应常规刷新，动画结束后由本实体主动通知场地模块关闭。
    /// </summary>
    private bool _isFinishing;

    /// <summary>
    /// 当前是否正在播放孵化结束动画。
    /// 场地模块通过此属性判断是否应延迟隐藏蛋实体。
    /// </summary>
    public bool IsFinishing => _isFinishing;

    /// <summary>
    /// 初始化并缓存常用组件。
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
        ApplyData(userData as EggEntityData, true);
    }

    /// <summary>
    /// 实体隐藏时清空显示状态。
    /// </summary>
    protected override void OnHide(bool isShutdown, object userData)
    {
        // 无论是否正在播放结束动画，隐藏时都要重置 finishing 标记
        // 防止实体被回收到池后再取出时仍残留旧状态
        _isFinishing = false;

        if (_spriteRenderer != null)
        {
            _spriteRenderer.sprite = null;
        }

        _currentEggCode = null;
        base.OnHide(isShutdown, userData);
    }

    /// <summary>
    /// 应用当前蛋实体显示数据。
    /// </summary>
    public void ApplyData(EggEntityData entityData)
    {
        ApplyData(entityData, false);
    }

    /// <summary>
    /// 应用当前蛋实体显示数据。
    /// </summary>
    /// <param name="entityData">蛋实体显示数据。</param>
    /// <param name="forceAnimatorReset">是否强制把 Animator 重置到待机动画首帧。</param>
    private void ApplyData(EggEntityData entityData, bool forceAnimatorReset)
    {
        if (entityData == null)
        {
            return;
        }

        // 缓存槽位索引，结束动画播放完毕后需要用它通知场地模块
        _slotIndex = entityData.SlotIndex;
        SetWorldPosition(entityData.WorldPosition);
        ResetActiveEggVisualState(forceAnimatorReset);
        ApplyEggVisual(entityData.EggCode);
    }

    /// <summary>
    /// 更新蛋实体世界位置。
    /// </summary>
    public void SetWorldPosition(Vector3 worldPosition)
    {
        CachedTransform.position = worldPosition;
    }

    /// <summary>
    /// 缓存渲染组件并记录默认精灵。
    /// </summary>
    private void CacheComponents()
    {
        if (_spriteRenderer == null)
        {
            _spriteRenderer = GetComponentInChildren<SpriteRenderer>(true);
        }

        if (_spriteRenderer != null)
        {
            if (_defaultSprite == null)
            {
                _defaultSprite = _spriteRenderer.sprite;
            }

            _spriteRenderer.sortingOrder = EggSortingOrder;
        }
    }

    /// <summary>
    /// 将蛋实体恢复到“正在孵化中”的可见状态。
    /// </summary>
    private void ResetActiveEggVisualState(bool forceAnimatorReset)
    {
        CacheComponents();
        bool wasFinishing = _isFinishing;
        _isFinishing = false;

        if (_animator != null && (forceAnimatorReset || wasFinishing))
        {
            // 强制从第 0 帧重新采样待机动画，避免复用实体时继续停留在 EggFinishAnimation 的透明帧。
            _animator.Play("EggAnimation", 0, 0f);
            _animator.Update(0f);
        }

        if (_spriteRenderer != null && (forceAnimatorReset || wasFinishing || _spriteRenderer.color.a < 1f))
        {
            // 即使新旧蛋 Code 相同导致 ApplyEggVisual 走缓存返回，也必须把结束动画写入的 alpha 还原。
            _spriteRenderer.enabled = true;
            _spriteRenderer.color = Color.white;
        }
    }

    /// <summary>
    /// 按蛋配置刷新实体外观。
    /// </summary>
    private void ApplyEggVisual(string eggCode)
    {
        CacheComponents();
        if (_spriteRenderer == null || string.IsNullOrWhiteSpace(eggCode) || GameEntry.DataTables == null)
        {
            return;
        }

        if (string.Equals(_currentEggCode, eggCode, System.StringComparison.Ordinal))
        {
            return;
        }

        EggDataRow eggDataRow = GameEntry.DataTables.GetDataRowByCode<EggDataRow>(eggCode);
        if (eggDataRow == null)
        {
            Log.Warning("EggEntityLogic can not find egg data row by code '{0}'.", eggCode);
            return;
        }

        Sprite eggSprite = null;
        if (GameEntry.GameAssets != null)
        {
            GameEntry.GameAssets.TryGetEggSprite(eggDataRow.IconPath, out eggSprite);
        }

        if (eggSprite == null)
        {
            if (_defaultSprite == null)
            {
                Log.Warning("EggEntityLogic can not find cached egg sprite by path '{0}', and prefab default sprite is also missing.", eggDataRow.IconPath);
                return;
            }

            Log.Warning("EggEntityLogic can not find cached egg sprite by path '{0}', fallback to prefab default sprite.", eggDataRow.IconPath);
            eggSprite = _defaultSprite;
        }

        _spriteRenderer.sprite = eggSprite;
        _spriteRenderer.color = Color.white;
        _currentEggCode = eggCode;
    }

    /// <summary>
    /// 播放蛋孵化结束动画（蛋消失动画）。
    /// 动画播放完毕后，本实体会主动通知场地模块执行隐藏。
    /// 若 Animator 未挂接，则直接走完成流程，避免实体永远卡在 finishing 状态。
    /// </summary>
    public void PlayFinishAnimation()
    {
        if (_isFinishing)
        {
            // 防止重复触发
            return;
        }

        _isFinishing = true;

        if (_animator == null)
        {
            // Animator 未挂接时直接走完成流程，避免实体永远卡在 finishing 状态
            NotifyFinishAnimationCompleted();
            return;
        }

        // 直接跳转到 EggFinishAnimation 状态，无需 Trigger/Transition
        // EggFinishAnimation 是一次性的淡出动画（1.5秒），m_LoopTime 已设为 0
        _animator.Play("EggFinishAnimation", 0);
    }

    /// <summary>
    /// 每帧检测结束动画是否播放完毕。
    /// 仅在 _isFinishing 为 true 时执行检测逻辑，其余帧零开销。
    /// </summary>
    protected override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        base.OnUpdate(elapseSeconds, realElapseSeconds);

        if (!_isFinishing || _animator == null)
        {
            return;
        }

        // ⚠️ 避坑：GetCurrentAnimatorStateInfo 必须传入正确的 layer 索引（0 = Base Layer）
        // normalizedTime 在非循环动画中：0 = 刚开始，1 = 播放到末尾
        // EggFinishAnimation 总时长 1.5 秒，normalizedTime >= 1f 表示已播完
        AnimatorStateInfo stateInfo = _animator.GetCurrentAnimatorStateInfo(0);
        if (stateInfo.IsName("EggFinishAnimation") && stateInfo.normalizedTime >= 1f)
        {
            NotifyFinishAnimationCompleted();
        }
    }

    /// <summary>
    /// 结束动画播放完毕，通知场地模块隐藏本蛋实体。
    /// </summary>
    private void NotifyFinishAnimationCompleted()
    {
        _isFinishing = false;
        GameEntry.PlayfieldEntities?.NotifyEggFinishAnimationCompleted(_slotIndex);
    }
}
