using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 通用轻提示界面。放 Toast 组（Depth=400），游戏启动时打开一次常驻。
/// 
/// 架构：单 UIForm 容器 + 对象池子物体。
/// 每条 Toast 从屏幕中下方固定锚点出现，多条自动垂直堆叠，
/// 停留指定时长后向上漂移并渐隐消失，各条独立动画互不干扰。
/// 
/// 对外接口：
/// - ShowToast(string message)           默认 1.5 秒停留
/// - ShowToast(string message, float duration)  自定义停留时长
/// </summary>
public sealed class ToastUIForm : UIFormLogic
{
    /// <summary>
    /// 单条 Toast 默认停留时长（秒）。
    /// </summary>
    private const float DefaultDuration = 1.5f;

    /// <summary>
    /// Toast 上飘距离（anchoredPosition Y 偏移）。
    /// </summary>
    private const float FloatOffsetY = 80f;

    /// <summary>
    /// 多条 Toast 之间的垂直间距。
    /// </summary>
    private const float StackSpacing = 60f;

    /// <summary>
    /// 上飘渐隐动画时长（秒）。
    /// </summary>
    private const float FadeDuration = 0.3f;

    /// <summary>
    /// Toast 子物体模板（包含 TMP_Text）。
    /// 由用户在 Inspector 中拖入。自身需设为 disabled，仅用于克隆。
    /// </summary>
    [SerializeField]
    private GameObject _toastTemplate;

    /// <summary>
    /// Toast 容器。子物体都挂在此容器下，容器锚点决定 Toast 出现位置。
    /// 由用户在 Inspector 中拖入。
    /// </summary>
    [SerializeField]
    private RectTransform _container;

    /// <summary>
    /// 当前正在显示的 Toast 列表。用于计算堆叠偏移和批量回收。
    /// </summary>
    private readonly List<ToastItem> _activeToasts = new List<ToastItem>(8);

    /// <summary>
    /// 已回收到池中等待复用的 Toast 列表。
    /// </summary>
    private readonly Stack<ToastItem> _toastPool = new Stack<ToastItem>(8);

    /// <summary>
    /// 初始化。禁用模板物体，防止它参与显示。
    /// </summary>
    /// <param name="userData">界面打开附加参数。</param>
    protected override void OnInit(object userData)
    {
        base.OnInit(userData);

        if (_toastTemplate != null)
        {
            _toastTemplate.SetActive(false);
        }
    }

    /// <summary>
    /// 打开时清空残留 Toast。
    /// </summary>
    /// <param name="userData">界面打开附加参数。</param>
    protected override void OnOpen(object userData)
    {
        base.OnOpen(userData);
        RecycleAllActiveToasts();
    }

    /// <summary>
    /// 关闭时回收所有 Toast 并 Kill 残留 Tween。
    /// </summary>
    /// <param name="isShutdown">是否为关闭场景导致的关闭。</param>
    /// <param name="userData">界面关闭附加参数。</param>
    protected override void OnClose(bool isShutdown, object userData)
    {
        RecycleAllActiveToasts();
        base.OnClose(isShutdown, userData);
    }

    /// <summary>
    /// 显示一条通用提示，使用默认停留时长。
    /// </summary>
    /// <param name="message">提示文本。</param>
    public void ShowToast(string message)
    {
        ShowToast(message, DefaultDuration);
    }

    /// <summary>
    /// 显示一条通用提示，指定停留时长。
    /// Toast 从堆叠位置出现，停留 duration 秒后上飘渐隐消失。
    /// </summary>
    /// <param name="message">提示文本。</param>
    /// <param name="duration">停留时长（秒）。</param>
    public void ShowToast(string message, float duration)
    {
        ToastItem item = AcquireToastItem();
        if (item == null)
        {
            return;
        }

        // 堆叠偏移：当前活跃数量 × 间距，越新的越靠上。
        float stackOffsetY = _activeToasts.Count * StackSpacing;
        item.Play(message, stackOffsetY, duration, OnToastComplete);
        _activeToasts.Add(item);
    }

    /// <summary>
    /// 从池中获取或实例化一个 Toast 子物体。
    /// </summary>
    /// <returns>可用的 ToastItem；若模板缺失则返回 null。</returns>
    private ToastItem AcquireToastItem()
    {
        // 优先从池中复用
        while (_toastPool.Count > 0)
        {
            ToastItem pooledItem = _toastPool.Pop();
            if (pooledItem != null && pooledItem.gameObject != null)
            {
                pooledItem.gameObject.SetActive(true);
                return pooledItem;
            }
        }

        // 池为空，从模板克隆
        if (_toastTemplate == null || _container == null)
        {
            Log.Warning("ToastUIForm 缺少 ToastTemplate 或 Container 引用，无法创建 Toast。");
            return null;
        }

        GameObject toastObj = Instantiate(_toastTemplate, _container, false);
        toastObj.SetActive(true);

        ToastItem item = toastObj.GetComponent<ToastItem>();
        if (item == null)
        {
            item = toastObj.AddComponent<ToastItem>();
        }

        return item;
    }

    /// <summary>
    /// 单条 Toast 动画完成回调。回收到池中。
    /// </summary>
    /// <param name="item">完成动画的 Toast 项。</param>
    private void OnToastComplete(ToastItem item)
    {
        ReleaseToastItem(item);
    }

    /// <summary>
    /// 回收指定 Toast 到池中。
    /// </summary>
    /// <param name="item">要回收的 Toast 项。</param>
    private void ReleaseToastItem(ToastItem item)
    {
        if (item == null)
        {
            return;
        }

        _activeToasts.Remove(item);
        item.gameObject.SetActive(false);
        _toastPool.Push(item);
    }

    /// <summary>
    /// 回收所有活跃 Toast。
    /// </summary>
    private void RecycleAllActiveToasts()
    {
        for (int i = _activeToasts.Count - 1; i >= 0; i--)
        {
            ToastItem item = _activeToasts[i];
            if (item != null)
            {
                item.KillAnimation();
                item.gameObject.SetActive(false);
                _toastPool.Push(item);
            }
        }

        _activeToasts.Clear();
    }

    // ──────────────────────────────────────────────────────────
    //  ToastItem：单条 Toast 子物体，负责文本显示 + 上飘渐隐动画
    // ──────────────────────────────────────────────────────────

    /// <summary>
    /// 单条 Toast 子物体。
    /// 挂在 ToastTemplate 克隆体上，负责显示文本并播放上飘渐隐动画。
    /// 
    /// 动画分两阶段：
    /// 1. 停留阶段：文本完全可见，等待 duration 秒；
    /// 2. 上飘渐隐阶段：向上漂移 FloatOffsetY 并同时淡出。
    /// </summary>
    private sealed class ToastItem : MonoBehaviour
    {
        /// <summary>
        /// 自身 RectTransform 缓存。
        /// </summary>
        private RectTransform _rectTransform;

        /// <summary>
        /// 文本组件缓存。
        /// </summary>
        private TextMeshProUGUI _text;

        /// <summary>
        /// 文本初始颜色缓存，用于每次复用时恢复 alpha。
        /// </summary>
        private Color _initialTextColor = Color.white;

        /// <summary>
        /// 是否已缓存初始文本颜色。
        /// </summary>
        private bool _hasCachedInitialTextColor;

        /// <summary>
        /// 当前动画序列。
        /// </summary>
        private Sequence _sequence;

        /// <summary>
        /// 动画完成回调。
        /// </summary>
        private System.Action<ToastItem> _onComplete;

        private void Awake()
        {
            CacheReferences();
        }

        private void OnDisable()
        {
            KillAnimation();
        }

        private void OnDestroy()
        {
            KillAnimation();
        }

        /// <summary>
        /// 播放一次 Toast 动画。
        /// </summary>
        /// <param name="message">要显示的文本。</param>
        /// <param name="stackOffsetY">堆叠偏移（Y 方向）。</param>
        /// <param name="duration">停留时长（秒）。</param>
        /// <param name="onComplete">动画结束回调。</param>
        public void Play(string message, float stackOffsetY, float duration,
            System.Action<ToastItem> onComplete)
        {
            CacheReferences();
            KillAnimation();
            _onComplete = onComplete;

            if (_rectTransform == null)
            {
                NotifyComplete();
                return;
            }

            // 设置初始位置：基于堆叠偏移
            _rectTransform.anchoredPosition = new Vector2(0f, stackOffsetY);
            _rectTransform.localScale = Vector3.one;

            if (_text != null)
            {
                _text.text = message;
                ResetTextColor();
            }

            // 构建动画序列：停留 → 上飘渐隐
            _sequence = DOTween.Sequence()
                .SetUpdate(true)
                .OnComplete(OnSequenceComplete);

            // 阶段1：停留 duration 秒
            _sequence.AppendInterval(duration);

            // 阶段2：上飘 + 渐隐
            float endY = stackOffsetY + FloatOffsetY;
            _sequence.Append(
                _rectTransform.DOAnchorPosY(endY, FadeDuration).SetEase(Ease.OutCubic));

            if (_text != null)
            {
                _sequence.Join(
                    _text.DOFade(0f, FadeDuration).SetEase(Ease.InQuad));
            }
        }

        /// <summary>
        /// 终止当前动画。
        /// </summary>
        public void KillAnimation()
        {
            if (_sequence == null)
            {
                _onComplete = null;
                return;
            }

            _sequence.Kill();
            _sequence = null;
            _onComplete = null;
        }

        private void CacheReferences()
        {
            if (_rectTransform == null)
            {
                _rectTransform = transform as RectTransform;
            }

            if (_text == null)
            {
                _text = GetComponentInChildren<TextMeshProUGUI>(true);
                if (_text != null && !_hasCachedInitialTextColor)
                {
                    _initialTextColor = _text.color;
                    _hasCachedInitialTextColor = true;
                }
            }
        }

        private void ResetTextColor()
        {
            if (_text == null)
            {
                return;
            }

            _text.color = _hasCachedInitialTextColor ? _initialTextColor : _text.color;
        }

        private void OnSequenceComplete()
        {
            _sequence = null;
            NotifyComplete();
        }

        private void NotifyComplete()
        {
            var callback = _onComplete;
            _onComplete = null;
            callback?.Invoke(this);
        }
    }
}
