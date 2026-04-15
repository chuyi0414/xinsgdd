using System;
using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// 金币点击提示 Toast 项。
/// 挂在 GoldCoinToast.prefab 上，负责显示“+金币数”，并播放上飘渐隐动画。
/// </summary>
public sealed class GoldCoinToastItem : MonoBehaviour
{
    /// <summary>
    /// Toast 在一次动画中向上漂移的距离。
    /// 初始值为 120，表示从点击点继续向上抬升 120 个 UI 单位。
    /// </summary>
    private const float FloatOffsetY = 120f;

    /// <summary>
    /// Toast 动画总时长（秒）。
    /// 初始值为 0.45，时长过短会显得生硬，过长会影响点击反馈节奏。
    /// </summary>
    private const float AnimationDuration = 0.45f;

    /// <summary>
    /// 自身 RectTransform 缓存。
    /// 初始状态为 null，首次访问时自动缓存。
    /// </summary>
    private RectTransform _cachedRectTransform;

    /// <summary>
    /// 预制体上的 TextMeshProUGUI 文本组件缓存。
    /// 初始状态为 null，首次访问时自动缓存。
    /// </summary>
    private TextMeshProUGUI _text;

    /// <summary>
    /// 当前正在执行的 Toast 动画序列。
    /// 初始状态为 null；当对象被回收或销毁时必须主动 Kill，避免补间残留。
    /// </summary>
    private Sequence _toastSequence;

    /// <summary>
    /// 动画播放完成后的回调。
    /// 由外层 MainUIForm 传入，用于把 Toast 回收到对象池。
    /// </summary>
    private Action<GoldCoinToastItem> _onComplete;

    /// <summary>
    /// 文本组件的初始颜色缓存。
    /// 用于每次复用前恢复透明度，避免上一次渐隐后的 alpha 残留。
    /// </summary>
    private Color _initialTextColor = Color.white;

    /// <summary>
    /// 是否已经成功缓存过初始文本颜色。
    /// false 表示尚未从预制体组件读取过颜色。
    /// </summary>
    private bool _hasCachedInitialTextColor;

    /// <summary>
    /// 对外暴露的 RectTransform 缓存。
    /// </summary>
    public RectTransform CachedRectTransform
    {
        get
        {
            CacheReferences();
            return _cachedRectTransform;
        }
    }

    /// <summary>
    /// 初始化时缓存组件引用。
    /// </summary>
    private void Awake()
    {
        CacheReferences();
    }

    /// <summary>
    /// 对象失活时终止补间。
    /// 这里必须停止动画，否则对象回收到池里后补间仍可能继续驱动旧引用。
    /// </summary>
    private void OnDisable()
    {
        KillToastSequence();
    }

    /// <summary>
    /// 对象销毁时终止补间。
    /// </summary>
    private void OnDestroy()
    {
        KillToastSequence();
    }

    /// <summary>
    /// 播放一次金币 Toast 动画。
    /// </summary>
    /// <param name="coinAmount">本次要显示的金币数量。</param>
    /// <param name="startLocalPos">Toast 在父容器下的起始局部坐标。</param>
    /// <param name="onComplete">动画结束后的回调。</param>
    public void PlayToast(int coinAmount, Vector2 startLocalPos, Action<GoldCoinToastItem> onComplete)
    {
        CacheReferences();
        KillToastSequence();
        _onComplete = onComplete;

        if (_cachedRectTransform == null)
        {
            NotifyAnimationComplete();
            return;
        }

        // 每次复用时都先把位置恢复到点击瞬间的金币坐标，
        // 这样上飘动画的起点就能严格贴合金币当前所在位置。
        _cachedRectTransform.anchoredPosition = startLocalPos;
        _cachedRectTransform.localScale = Vector3.one;

        if (_text != null)
        {
            // 使用 TMP 的格式化接口写入“+金币数”，
            // 避免手写字符串拼接，让复用路径保持更干净。
            _text.SetText("+{0}", coinAmount);
            ResetTextColor();
        }

        // 这里同时驱动两个轨道：
        // 1. RectTransform 纵向上飘；
        // 2. 文本 alpha 从 1 渐隐到 0。
        // 两条轨道 Join 到同一个 Sequence 中，确保时间轴完全同步。
        _toastSequence = DOTween.Sequence()
            .SetUpdate(true)
            .OnComplete(OnToastSequenceComplete);

        _toastSequence.Append(
            _cachedRectTransform
                .DOAnchorPosY(startLocalPos.y + FloatOffsetY, AnimationDuration)
                .SetEase(Ease.OutCubic));

        if (_text != null)
        {
            _toastSequence.Join(
                _text
                    .DOFade(0f, AnimationDuration)
                    .SetEase(Ease.OutQuad));
        }
    }

    /// <summary>
    /// 缓存所需组件引用。
    /// </summary>
    private void CacheReferences()
    {
        if (_cachedRectTransform == null)
        {
            _cachedRectTransform = transform as RectTransform;
        }

        if (_text == null)
        {
            _text = GetComponent<TextMeshProUGUI>();
            if (_text != null && !_hasCachedInitialTextColor)
            {
                _initialTextColor = _text.color;
                _hasCachedInitialTextColor = true;
            }
        }
    }

    /// <summary>
    /// 恢复文本颜色到预制体初始状态。
    /// 重点是把 alpha 重新设回初始值，避免对象池复用后直接透明。
    /// </summary>
    private void ResetTextColor()
    {
        if (_text == null)
        {
            return;
        }

        _text.color = _hasCachedInitialTextColor ? _initialTextColor : _text.color;
    }

    /// <summary>
    /// Toast 动画完成回调。
    /// </summary>
    private void OnToastSequenceComplete()
    {
        _toastSequence = null;
        NotifyAnimationComplete();
    }

    /// <summary>
    /// 通知外层当前 Toast 已经完成本轮显示。
    /// </summary>
    private void NotifyAnimationComplete()
    {
        Action<GoldCoinToastItem> onComplete = _onComplete;
        _onComplete = null;
        onComplete?.Invoke(this);
    }

    /// <summary>
    /// 终止当前动画序列。
    /// </summary>
    private void KillToastSequence()
    {
        if (_toastSequence == null)
        {
            _onComplete = null;
            return;
        }

        _toastSequence.Kill();
        _toastSequence = null;
        _onComplete = null;
    }
}
