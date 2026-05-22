using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 嵌套场景下的 ScrollRect。
/// 解决 Unity 原生 ScrollRect 在父子嵌套时"子级吞掉所有拖拽事件"的问题：
/// 　- 自身是横向 ScrollRect，玩家做纵向滑动 → 把整段拖拽事件转发给最近的祖先 ScrollRect；
/// 　- 自身是纵向 ScrollRect，玩家做横向滑动 → 同理；
/// 　- 主轴方向与自身一致 → 仍然走原生 ScrollRect 行为，不影响横向/纵向滚动手感。
/// 部署方式：把内层 Scroll View 上的 ScrollRect 组件类型改成本类即可，外层主 ScrollRect 不需要改。
/// </summary>
[AddComponentMenu("UI/Nested Scroll Rect", 38)]
public class NestedScrollRect : ScrollRect
{
    /// <summary>
    /// 当前拖拽序列是否已判定为"应路由给父级"。
    /// 在 OnBeginDrag 阶段一次性确定，OnDrag/OnEndDrag 直接消费该结果，避免每帧重复判定。
    /// </summary>
    private bool _routeDragToParent;

    /// <summary>
    /// 最近的祖先 ScrollRect 缓存。
    /// Awake / OnTransformParentChanged 时刷新，运行时不重复 GetComponentInParent，避免 GC。
    /// </summary>
    private ScrollRect _parentScrollRect;

    /// <summary>
    /// 初始化时缓存父级 ScrollRect。
    /// </summary>
    protected override void Awake()
    {
        base.Awake();
        ResolveParentScrollRect();
    }

    /// <summary>
    /// 父节点变化时重新缓存。
    /// 例如运行时把 Scroll View 移动到另一个父节点，缓存会失效，必须刷新。
    /// </summary>
    protected override void OnTransformParentChanged()
    {
        base.OnTransformParentChanged();
        ResolveParentScrollRect();
    }

    /// <summary>
    /// 沿父链查找最近的 ScrollRect 实例。
    /// 不命中时 _parentScrollRect = null，等价于退化成原生 ScrollRect 行为。
    /// </summary>
    private void ResolveParentScrollRect()
    {
        _parentScrollRect = null;
        Transform parent = transform.parent;
        while (parent != null)
        {
            // 父链中可能挂了任何 ScrollRect 子类（如更外层的 NestedScrollRect），都视作可路由对象。
            ScrollRect candidate = parent.GetComponent<ScrollRect>();
            if (candidate != null && candidate != this)
            {
                _parentScrollRect = candidate;
                return;
            }

            parent = parent.parent;
        }
    }

    /// <summary>
    /// PotentialDrag 阶段：自身与父级都需要初始化。
    /// 父级原生 ScrollRect 依赖该回调清理 inertia/velocity，缺少这一步会导致路由后惯性继承失败。
    /// </summary>
    public override void OnInitializePotentialDrag(PointerEventData eventData)
    {
        _routeDragToParent = false;
        base.OnInitializePotentialDrag(eventData);
        if (_parentScrollRect != null)
        {
            _parentScrollRect.OnInitializePotentialDrag(eventData);
        }
    }

    /// <summary>
    /// BeginDrag 阶段：基于"按下到当前"累积位移判定主轴方向。
    /// 累积位移比 eventData.delta 更稳定，避免按下后微小抖动被误判为反向轴。
    /// 主轴在自身支持轴上 → 走 base；否则整段拖拽切到父级。
    /// </summary>
    public override void OnBeginDrag(PointerEventData eventData)
    {
        if (_parentScrollRect == null)
        {
            base.OnBeginDrag(eventData);
            return;
        }

        // 累积位移 = 当前位置 - 按下位置。eventData.pressPosition 由 EventSystem 在 OnPointerDown 时记录。
        Vector2 dragVector = eventData.position - eventData.pressPosition;
        bool isHorizontalDrag = Mathf.Abs(dragVector.x) >= Mathf.Abs(dragVector.y);

        // 自身支持当前主轴 → 不路由，原地处理。
        // horizontal / vertical 是 ScrollRect 的 public 序列化字段，由 Inspector 配置。
        bool selfHandlesAxis = (isHorizontalDrag && horizontal) || (!isHorizontalDrag && vertical);
        if (selfHandlesAxis)
        {
            _routeDragToParent = false;
            base.OnBeginDrag(eventData);
            return;
        }

        // 自身轴不支持 → 整段拖拽事件路由给父级 ScrollRect。
        _routeDragToParent = true;
        _parentScrollRect.OnBeginDrag(eventData);
    }

    /// <summary>
    /// Drag 阶段：直接消费 OnBeginDrag 时确定的路由结果。
    /// </summary>
    public override void OnDrag(PointerEventData eventData)
    {
        if (_routeDragToParent && _parentScrollRect != null)
        {
            _parentScrollRect.OnDrag(eventData);
            return;
        }

        base.OnDrag(eventData);
    }

    /// <summary>
    /// EndDrag 阶段：路由结束后清状态，避免下次拖拽继承上次的判定结果。
    /// </summary>
    public override void OnEndDrag(PointerEventData eventData)
    {
        if (_routeDragToParent && _parentScrollRect != null)
        {
            _parentScrollRect.OnEndDrag(eventData);
            _routeDragToParent = false;
            return;
        }

        base.OnEndDrag(eventData);
        _routeDragToParent = false;
    }

    /// <summary>
    /// 鼠标滚轮 / 触控板滑动事件路由。
    /// 与拖拽不同，OnScroll 不需要状态机，每次独立判定主轴即可。
    /// 移动端通常不会走这条路径，但保留以兼容 Editor / Standalone。
    /// </summary>
    public override void OnScroll(PointerEventData eventData)
    {
        if (_parentScrollRect == null)
        {
            base.OnScroll(eventData);
            return;
        }

        Vector2 scrollDelta = eventData.scrollDelta;
        bool isHorizontalScroll = Mathf.Abs(scrollDelta.x) > Mathf.Abs(scrollDelta.y);
        bool selfHandlesAxis = (isHorizontalScroll && horizontal) || (!isHorizontalScroll && vertical);
        if (selfHandlesAxis)
        {
            base.OnScroll(eventData);
            return;
        }

        _parentScrollRect.OnScroll(eventData);
    }
}
