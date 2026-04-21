using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;

/// <summary>
/// 等待区分数字图片渲染器：
/// 1) 负责把整数转换为多位数字图片；
/// 2) 按从左到右排布并保持整体居中；
/// 3) 精灵来源从 GameAssetModule 预加载缓存读取（Score/1 套图，64×64）。
/// 移植自 xinpgdd 的 WaitingAreaScoreSpriteRenderer，适配当前项目的 GameAssetModule 异步预加载链路。
/// </summary>
[DisallowMultipleComponent]
public class WaitingAreaScoreSpriteRenderer : MonoBehaviour
{
    // ───────────── 常量 ─────────────

    /// <summary>
    /// 数字总数（0~9）。
    /// </summary>
    private const int DigitCount = 10;

    /// <summary>
    /// 数字位模板节点名。
    /// 首次初始化时查找或创建此名称的子物体作为克隆模板。
    /// </summary>
    private const string DigitTemplateName = "DigitTemplate";

    /// <summary>
    /// 运行时克隆的数字位节点名前缀。
    /// </summary>
    private const string DigitNodeNamePrefix = "Digit_";

    // ───────────── Inspector 配置 ─────────────

    [Header("位布局")]
    [Tooltip("数字位之间的本地 X 方向间距。")]
    [SerializeField]
    private float _digitSpacing = 0.16f;

    [Tooltip("数字整体的本地偏移。")]
    [SerializeField]
    private Vector3 _digitRootOffset = Vector3.zero;

    [Header("模板")]
    [Tooltip("数字位模板（含 SpriteRenderer）。未配置时会自动创建。")]
    [SerializeField]
    private SpriteRenderer _digitTemplate;

    [Tooltip("模板排序层级偏移，避免数字被等待区底图遮挡。")]
    [SerializeField]
    private int _sortingOrderOffset = 10;

    // ───────────── 运行时状态 ─────────────

    /// <summary>
    /// 运行时克隆出的数字位 SpriteRenderer 列表。
    /// 按位数从低到高排列（个位、十位、百位……）。
    /// </summary>
    private readonly List<SpriteRenderer> _digitRenderers = new List<SpriteRenderer>(8);

    /// <summary>
    /// 初始化是否已完成。
    /// </summary>
    private bool _initialized;

    /// <summary>
    /// 整体是否可见。
    /// </summary>
    private bool _isVisible = true;

    /// <summary>
    /// 当前有效数字位数。
    /// </summary>
    private int _activeDigitCount;

    // ───────────── 公开接口 ─────────────

    /// <summary>
    /// 设置显示数值（自动多位数）。
    /// </summary>
    /// <param name="value">要显示的整数值，负数会被钳位为 0。</param>
    public void SetNumber(int value)
    {
        EnsureInitialized();

        int safeValue = Mathf.Max(0, value);
        string numberText = safeValue.ToString();
        _activeDigitCount = numberText.Length;

        EnsureDigitRendererCount(_activeDigitCount);
        UpdateDigitSprites(numberText);
        LayoutDigits(_activeDigitCount);
        ApplyVisibility();
    }

    /// <summary>
    /// 设置整体显示/隐藏。
    /// </summary>
    /// <param name="visible">true=显示，false=隐藏所有数字位。</param>
    public void SetVisible(bool visible)
    {
        EnsureInitialized();
        _isVisible = visible;
        ApplyVisibility();
    }

    /// <summary>
    /// 重置到默认数值并按可见性显示。
    /// </summary>
    /// <param name="defaultValue">默认数值。</param>
    /// <param name="visible">是否可见。</param>
    public void ResetToDefault(int defaultValue, bool visible)
    {
        EnsureInitialized();
        _isVisible = visible;
        SetNumber(Mathf.Max(0, defaultValue));
        ApplyVisibility();
    }

    // ───────────── 初始化 ─────────────

    private void Awake()
    {
        EnsureInitialized();
    }

    /// <summary>
    /// 惰性初始化：准备模板、排序层级。
    /// 仅首次调用时执行，后续调用直接返回。
    /// </summary>
    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        if (_digitSpacing < 0f)
        {
            _digitSpacing = 0f;
        }

        EnsureDigitTemplate();

        _initialized = true;
    }

    /// <summary>
    /// 准备数字位模板。
    /// 若没有现成模板则运行时创建一个隐藏模板用于克隆。
    /// </summary>
    private void EnsureDigitTemplate()
    {
        if (_digitTemplate == null)
        {
            Transform templateTransform = transform.Find(DigitTemplateName);
            if (templateTransform != null)
            {
                _digitTemplate = templateTransform.GetComponent<SpriteRenderer>();
            }
        }

        if (_digitTemplate == null)
        {
            // ⚠️ 避坑：运行时创建模板节点，仅用于克隆，自身保持隐藏。
            var templateGo = new GameObject(DigitTemplateName);
            templateGo.transform.SetParent(transform, false);
            templateGo.transform.localPosition = _digitRootOffset;
            _digitTemplate = templateGo.AddComponent<SpriteRenderer>();
            _digitTemplate.sortingOrder = _sortingOrderOffset;
        }
        else
        {
            _digitTemplate.transform.SetParent(transform, false);
            _digitTemplate.transform.localPosition = _digitRootOffset;
            _digitTemplate.sortingOrder += _sortingOrderOffset;
        }

        // 模板自身必须隐藏，仅用于克隆
        if (_digitTemplate.gameObject.activeSelf)
        {
            _digitTemplate.gameObject.SetActive(false);
        }
    }

    // ───────────── 数字位管理 ─────────────

    /// <summary>
    /// 确保用于展示的 SpriteRenderer 数量满足位数需求。
    /// 不足时通过模板克隆补充。
    /// </summary>
    /// <param name="count">需要的数字位数。</param>
    private void EnsureDigitRendererCount(int count)
    {
        while (_digitRenderers.Count < count)
        {
            int digitIndex = _digitRenderers.Count;
            var renderer = CreateDigitRenderer(digitIndex);
            _digitRenderers.Add(renderer);
        }
    }

    /// <summary>
    /// 通过模板克隆一个数字位渲染器。
    /// </summary>
    /// <param name="index">数字位索引（0=个位，1=十位……）。</param>
    /// <returns>新创建的 SpriteRenderer。</returns>
    private SpriteRenderer CreateDigitRenderer(int index)
    {
        GameObject digitGo;
        if (_digitTemplate != null)
        {
            digitGo = Instantiate(_digitTemplate.gameObject, transform);
        }
        else
        {
            digitGo = new GameObject($"{DigitNodeNamePrefix}{index}");
            digitGo.transform.SetParent(transform, false);
            digitGo.AddComponent<SpriteRenderer>();
        }

        digitGo.name = $"{DigitNodeNamePrefix}{index}";
        digitGo.SetActive(true);

        var renderer = digitGo.GetComponent<SpriteRenderer>();
        if (renderer == null)
        {
            renderer = digitGo.AddComponent<SpriteRenderer>();
        }

        // 从模板继承渲染属性，保证视觉一致性
        if (_digitTemplate != null)
        {
            renderer.color = _digitTemplate.color;
            renderer.sortingLayerID = _digitTemplate.sortingLayerID;
            renderer.sortingOrder = _digitTemplate.sortingOrder;
            renderer.flipX = _digitTemplate.flipX;
            renderer.flipY = _digitTemplate.flipY;
        }

        return renderer;
    }

    // ───────────── 精灵更新 ─────────────

    /// <summary>
    /// 按字符串逐位更新贴图。
    /// 精灵来源：GameAssetModule 预加载的 Score/1 套图（64×64）。
    /// </summary>
    /// <param name="numberText">数字字符串，如 "123"。</param>
    private void UpdateDigitSprites(string numberText)
    {
        for (int i = 0; i < numberText.Length; i++)
        {
            var renderer = _digitRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            int digit = numberText[i] - '0';
            renderer.sprite = GetDigitSprite(digit);
        }
    }

    /// <summary>
    /// 获取指定数字的精灵。
    /// 优先从 GameAssetModule 预加载缓存读取（Score/1 套图）；
    /// 缓存未命中时返回 null 并输出警告。
    /// </summary>
    /// <param name="digit">数字 0~9。</param>
    /// <returns>对应的精灵；未命中返回 null。</returns>
    private Sprite GetDigitSprite(int digit)
    {
        if (digit < 0 || digit >= DigitCount)
        {
            Log.Warning($"[WaitingAreaScoreSpriteRenderer] 非法数字索引：{digit}");
            return null;
        }

        // ── 从 GameAssetModule 预加载缓存读取 Score/1 套图 ──
        if (GameEntry.GameAssets != null
            && GameEntry.GameAssets.TryGetScoreDigitSmallSprite(digit, out Sprite sprite)
            && sprite != null)
        {
            return sprite;
        }

        // ⚠️ 避坑：预加载未完成或资源缺失时，返回 null，数字位显示为空白。
        // 这比 Resources.Load 同步加载更安全，不会卡主线程。
        Log.Warning($"[WaitingAreaScoreSpriteRenderer] Score/1 数字精灵未命中缓存，Digit={digit}。请检查 GameAssetModule 预加载是否完成。");
        return null;
    }

    // ───────────── 布局 ─────────────

    /// <summary>
    /// 按位数居中布局：总宽 = (N-1) * spacing，首位从 -总宽/2 开始。
    /// </summary>
    /// <param name="digitCount">有效数字位数。</param>
    private void LayoutDigits(int digitCount)
    {
        if (digitCount <= 0)
        {
            return;
        }

        float totalWidth = (digitCount - 1) * _digitSpacing;
        float startX = -totalWidth * 0.5f + _digitRootOffset.x;

        for (int i = 0; i < digitCount; i++)
        {
            var renderer = _digitRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            Transform tr = renderer.transform;
            Vector3 localPos = tr.localPosition;
            localPos.x = startX + i * _digitSpacing;
            localPos.y = _digitRootOffset.y;
            localPos.z = _digitRootOffset.z;
            tr.localPosition = localPos;
        }
    }

    // ───────────── 显隐控制 ─────────────

    /// <summary>
    /// 根据当前可见性和有效位数统一更新子节点显隐。
    /// </summary>
    private void ApplyVisibility()
    {
        for (int i = 0; i < _digitRenderers.Count; i++)
        {
            var renderer = _digitRenderers[i];
            if (renderer == null || renderer.gameObject == null)
            {
                continue;
            }

            bool shouldActive = _isVisible && i < _activeDigitCount;
            if (renderer.gameObject.activeSelf != shouldActive)
            {
                renderer.gameObject.SetActive(shouldActive);
            }
        }
    }
}
