using DG.Tweening;
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityGameFramework.Runtime;

/// <summary>
/// 消除卡片实体逻辑。
/// 主线纯消消乐模式下的卡片交互壳：
/// 1. 接收世界坐标、Sprite、颜色与排序值；
/// 2. 实现 IPointerClickHandler 处理点击；
/// 3. 管理 IsBlocked / IsMoving / IsInWaitingArea 状态；
/// 4. 点击后通过 OnClickCallback 通知控制器。
/// </summary>
public sealed class EliminateCardEntityLogic : EntityLogic, IPointerClickHandler
{
    // ───────────── 常量 ─────────────

    /// <summary>
    /// 被遮挡卡片的置灰颜色。
    /// 与参考项目 xinpgdd 的 blockedColor 口径一致。
    /// </summary>
    private static readonly Color BlockedTintColor = new Color(0.6f, 0.6f, 0.6f, 1f);

    // ───────────── 渲染组件 ─────────────

    /// <summary>
    /// 卡片精灵渲染器。
    /// 当前 prefab 结构简单，直接取子层级里的第一个 SpriteRenderer。
    /// </summary>
    private SpriteRenderer _spriteRenderer;

    /// <summary>
    /// 预制体默认精灵。
    /// 当外部没传 Sprite 时，至少还能显示一个保底图形。
    /// </summary>
    private Sprite _defaultSprite;

    /// <summary>
    /// 2D 碰撞体，由 prefab 自带。
    /// 用于 Physics2DRaycaster 射线检测命中本卡片。
    /// </summary>
    private BoxCollider2D _boxCollider2D;

    /// <summary>
    /// 标记是否已给主相机补挂 Physics2DRaycaster。
    /// 全局只需挂一次，避免重复挂载。
    /// </summary>
    private static bool _hasAddedPhysics2DRaycaster;

    // ───────────── 卡片状态 ─────────────

    /// <summary>
    /// 当前卡片是否被上层遮挡。
    /// 被遮挡的卡片不可点击，且显示为置灰状态。
    /// </summary>
    private bool _isBlocked;

    /// <summary>
    /// 当前卡片是否正在移动中。
    /// 移动中的卡片不可点击，防止快速连点重复入队。
    /// </summary>
    private bool _isMoving;

    /// <summary>
    /// 当前卡片是否已进入等待区。
    /// 已在等待区的卡片不可再次点击。
    /// </summary>
    private bool _isInWaitingArea;

    /// <summary>
    /// 卡片类型 Id。
    /// 用于同类型归组排列与结算。
    /// </summary>
    public int TypeId { get; private set; }

    /// <summary>
    /// 卡片布局索引。
    /// 主要用于调试和定位具体卡片。
    /// </summary>
    public int LayoutIndex { get; private set; }

    // ───────────── 点击回调 ─────────────

    /// <summary>
    /// 点击回调。
    /// 由 EliminateCardController 注入，卡片被点击且通过状态检查后触发。
    /// 参数为被点击的 EliminateCardEntityLogic 自身。
    /// </summary>
    public Action<EliminateCardEntityLogic> OnClickCallback { get; set; }

    // ───────────── 公开状态访问 ─────────────

    /// <summary>
    /// 当前卡片是否被遮挡。
    /// </summary>
    public bool IsBlocked => _isBlocked;

    /// <summary>
    /// 当前卡片是否正在移动。
    /// </summary>
    public bool IsMoving => _isMoving;

    /// <summary>
    /// 当前卡片是否已在等待区。
    /// </summary>
    public bool IsInWaitingArea => _isInWaitingArea;

    // ───────────── EntityLogic 生命周期 ─────────────

    /// <summary>
    /// 初始化阶段：缓存渲染组件 + 缓存 prefab 自带的 BoxCollider2D。
    /// </summary>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);
        CacheComponents();
        CacheBoxCollider2D();
    }

    /// <summary>
    /// 实体显示时应用最新显示数据。
    /// </summary>
    protected override void OnShow(object userData)
    {
        base.OnShow(userData);
        // 重置运行时状态，防止对象池复用时残留脏数据
        _isBlocked = false;
        _isMoving = false;
        _isInWaitingArea = false;
        ApplyData(userData as EliminateCardEntityData);

        // 自动注册点击回调与卡片逻辑引用
        // 控制器为纯 C# 对象，通过静态 Instance 暴露，卡片 OnShow 时自动绑定
        OnClickCallback = EliminateCardController.Instance != null
            ? EliminateCardController.Instance.HandleCardClick
            : null;
        EliminateCardController.Instance?.RegisterCardLogic(Entity.Id, this);
    }

    /// <summary>
    /// 实体隐藏时清理残留显示状态与动画。
    /// </summary>
    protected override void OnHide(bool isShutdown, object userData)
    {
        // 终止所有残留 Tween，防止隐藏后回调仍在执行
        CachedTransform.DOKill(false);

        if (_spriteRenderer != null)
        {
            _spriteRenderer.sprite = _defaultSprite;
            _spriteRenderer.color = Color.white;
            _spriteRenderer.sortingOrder = 0;
        }

        _isBlocked = false;
        _isMoving = false;
        _isInWaitingArea = false;
        OnClickCallback = null;

        // 自动反注册卡片逻辑引用
        EliminateCardController.Instance?.UnregisterCardLogic(Entity.Id);

        base.OnHide(isShutdown, userData);
    }

    // ───────────── IPointerClickHandler ─────────────

    /// <summary>
    /// 点击处理入口。
    /// 状态检查链路：被遮挡 → 忽略；移动中 → 忽略；已在等待区 → 忽略。
    /// 全部通过后回调控制器。
    /// </summary>
    public void OnPointerClick(PointerEventData eventData)
    {
        // 被遮挡的卡片不可点击
        if (_isBlocked)
        {
            return;
        }

        // 移动中的卡片不可点击（防止快速连点重复入队）
        if (_isMoving)
        {
            return;
        }

        // 已在等待区的卡片不可再次点击
        if (_isInWaitingArea)
        {
            return;
        }

        OnClickCallback?.Invoke(this);
    }

    // ───────────── 外部状态更新接口 ─────────────

    /// <summary>
    /// 设置遮挡状态。
    /// 被遮挡的卡片会置灰显示并禁用 Collider，防止射线命中。
    /// </summary>
    /// <param name="blocked">true=被遮挡，false=未被遮挡</param>
    public void SetBlocked(bool blocked)
    {
        _isBlocked = blocked;

        if (_spriteRenderer != null)
        {
            // 被遮挡时使用置灰颜色，未被遮挡时恢复原始白色
            _spriteRenderer.color = blocked ? BlockedTintColor : Color.white;
        }

        // 被遮挡时禁用 Collider2D，让射线直接穿透到下层卡片
        if (_boxCollider2D != null)
        {
            _boxCollider2D.enabled = !blocked;
        }
    }

    /// <summary>
    /// 设置移动状态。
    /// 移动中的卡片不可点击，避免快速连点重复入队。
    /// </summary>
    /// <param name="moving">true=移动中，false=移动结束</param>
    public void SetMoving(bool moving)
    {
        _isMoving = moving;
    }

    /// <summary>
    /// 标记卡片已进入等待区。
    /// 进入等待区后不可再次点击。
    /// </summary>
    public void SetInWaitingArea()
    {
        _isInWaitingArea = true;
    }

    // ───────────── 内部方法 ─────────────

    /// <summary>
    /// 把一份卡片显示数据真正应用到实体上。
    /// </summary>
    public void ApplyData(EliminateCardEntityData entityData)
    {
        if (entityData == null)
        {
            return;
        }

        CacheComponents();
        CachedTransform.position = entityData.WorldPosition;

        // 缓存类型与布局索引，供等待区归组使用
        TypeId = entityData.TypeId;
        LayoutIndex = entityData.LayoutIndex;

        if (_spriteRenderer == null)
        {
            return;
        }

        _spriteRenderer.sprite = entityData.DisplaySprite != null ? entityData.DisplaySprite : _defaultSprite;
        _spriteRenderer.sortingOrder = entityData.SortingOrder;

        // Sprite 设置后更新 BoxCollider2D 尺寸
        // prefab 自带的 BoxCollider2D 可能尺寸不对，此处根据实际 Sprite 重新适配
        UpdateBoxCollider2DSize();

        // 确保主相机挂有 Physics2DRaycaster（全局只需一次）
        EnsurePhysics2DRaycaster();

        // 应用遮挡状态：被遮挡卡置灰 + 禁用 Collider
        SetBlocked(entityData.IsBlocked);
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

        if (_spriteRenderer != null && _defaultSprite == null)
        {
            _defaultSprite = _spriteRenderer.sprite;
        }
    }

    /// <summary>
    /// 缓存 prefab 自带的 BoxCollider2D。
    /// prefab 上已挂 BoxCollider2D，此处只做缓存，不补挂。
    /// </summary>
    private void CacheBoxCollider2D()
    {
        _boxCollider2D = GetComponent<BoxCollider2D>();
        if (_boxCollider2D == null)
        {
            // 兜底：如果 prefab 没带，在子层级中查找
            _boxCollider2D = GetComponentInChildren<BoxCollider2D>(true);
        }
    }

    /// <summary>
    /// 根据 SpriteRenderer.bounds 更新 BoxCollider2D 尺寸和偏移。
    /// 必须在 Sprite 已设置后调用。
    /// BoxCollider2D.size 是局部坐标，需要从世界 bounds 转换。
    /// </summary>
    private void UpdateBoxCollider2DSize()
    {
        if (_boxCollider2D == null || _spriteRenderer == null || _spriteRenderer.sprite == null)
        {
            return;
        }

        Bounds bounds = _spriteRenderer.bounds;
        // BoxCollider2D.size 是局部坐标，需要从世界空间转换
        Vector3 localSize = transform.InverseTransformVector(bounds.size);
        _boxCollider2D.size = new Vector2(Mathf.Abs(localSize.x), Mathf.Abs(localSize.y));
        // offset 从 bounds 中心减去 transform 位置的局部偏移
        Vector3 localCenter = transform.InverseTransformPoint(bounds.center);
        _boxCollider2D.offset = new Vector2(localCenter.x, localCenter.y);
    }

    /// <summary>
    /// 确保主相机挂有 Physics2DRaycaster。
    /// IPointerClickHandler + BoxCollider2D 需要 Physics2DRaycaster 才能工作。
    /// 全局只需挂一次，使用静态标记避免重复挂载。
    /// </summary>
    private static void EnsurePhysics2DRaycaster()
    {
        if (_hasAddedPhysics2DRaycaster)
        {
            return;
        }

        Camera mainCam = Camera.main;
        if (mainCam == null)
        {
            return;
        }

        if (mainCam.GetComponent<Physics2DRaycaster>() == null)
        {
            mainCam.gameObject.AddComponent<Physics2DRaycaster>();
        }

        _hasAddedPhysics2DRaycaster = true;
    }
}
