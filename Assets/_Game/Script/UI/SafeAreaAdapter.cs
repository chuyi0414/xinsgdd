using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityGameFramework.Runtime;
using WeChatWASM;

/// <summary>
/// 安全区域适配模式
/// </summary>
public enum SafeAreaMode
{
    /// <summary>
    /// 自动检测模式（推荐）
    /// 微信小游戏环境自动使用微信API，其他环境使用标准API
    /// </summary>
    Auto,

    /// <summary>
    /// 强制使用标准模式
    /// 始终使用Unity的Screen.safeArea，适配刘海屏、圆角屏等
    /// </summary>
    ForceStandard,

    /// <summary>
    /// 强制使用微信模式
    /// 始终尝试使用微信API（WX.GetWindowInfo），失败则降级到标准模式
    /// </summary>
    ForceWeChat
}

/// <summary>
/// 微信小游戏适配策略
/// </summary>
public enum WeChatAdaptStrategy
{
    /// <summary>
    /// 完整适配（默认）
    /// 同时适配系统安全区域（刘海）和微信胶囊按钮
    /// </summary>
    Full,

    /// <summary>
    /// 仅适配系统安全区域
    /// 只适配刘海、圆角等系统安全区域，忽略微信胶囊按钮
    /// </summary>
    SystemOnly
}

/// <summary>
/// 安全区域适配组件，用于避免刘海和微信胶囊遮挡 UI。
/// </summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public class SafeAreaAdapter : MonoBehaviour
{
    /// <summary>
    /// 当前安全区域适配模式。
    /// </summary>
    [SerializeField]
    private SafeAreaMode adaptMode = SafeAreaMode.Auto;

    /// <summary>
    /// 需要应用安全区域的目标 RectTransform。
    /// </summary>
    [SerializeField]
    private RectTransform target;

    /// <summary>
    /// 是否应用左侧安全区域。
    /// </summary>
    [SerializeField]
    private bool applyLeft = false;

    /// <summary>
    /// 是否应用右侧安全区域。
    /// </summary>
    [SerializeField]
    private bool applyRight = false;

    /// <summary>
    /// 是否应用顶部安全区域。
    /// </summary>
    [SerializeField]
    private bool applyTop = false;

    /// <summary>
    /// 是否应用底部安全区域。
    /// </summary>
    [SerializeField]
    private bool applyBottom = false;

    /// <summary>
    /// 在安全区域基础上额外附加的边距。
    /// </summary>
    [SerializeField]
    private Vector2 extraPadding = Vector2.zero;

    /// <summary>
    /// 是否输出调试日志。
    /// </summary>
    [SerializeField]
    private bool logChanges = false;

    [Header("初始锚点设置")]
    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("左锚点（从左往右，0-1）")]
    private float initialAnchorMinX = 0f;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("右锚点（从左往右，0-1）")]
    private float initialAnchorMaxX = 1f;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("UI顶部位置（从屏幕顶部往下，0-1）。0表示屏幕顶部，0.1表示从顶部往下10%")]
    private float topPosition = 0f;

    [SerializeField]
    [Range(0f, 1f)]
    [Tooltip("UI底部位置（从屏幕顶部往下，0-1）。0.8表示从顶部往下80%，即底部留20%空白")]
    private float bottomPosition = 1f;

    [Header("锚点精度对齐")]
    [SerializeField]
    [Tooltip("左锚点精度。0=不对齐，0.01=百分位，0.001=千分位")]
    private float anchorPrecisionLeft = 0.001f;

    [SerializeField]
    [Tooltip("右锚点精度。0=不对齐，0.01=百分位，0.001=千分位")]
    private float anchorPrecisionRight = 0.001f;

    [SerializeField]
    [Tooltip("上锚点精度。0=不对齐，0.01=百分位，0.001=千分位")]
    private float anchorPrecisionTop = 0.001f;

    [SerializeField]
    [Tooltip("下锚点精度。0=不对齐，0.01=百分位，0.001=千分位")]
    private float anchorPrecisionBottom = 0.001f;

    [Header("微信小游戏设置")]
    [SerializeField]
    [Tooltip("微信小游戏的适配策略：完整适配（含胶囊）或仅系统安全区域（仅刘海）")]
    private WeChatAdaptStrategy wechatStrategy = WeChatAdaptStrategy.Full;

    [Header("编辑器模拟（仅用于测试）")]
    [SerializeField]
    [Tooltip("在编辑器中模拟安全区域，用于测试ForceStandard模式")]
    private bool simulateSafeAreaInEditor = false;

    [SerializeField]
    [Tooltip("模拟的顶部安全高度（像素）")]
    private float simulatedTopInset = 44f;

    [SerializeField]
    [Tooltip("模拟的底部安全高度（像素）")]
    private float simulatedBottomInset = 34f;

    [SerializeField]
    [Tooltip("模拟的左侧安全宽度（像素）")]
    private float simulatedLeftInset = 0f;

    [SerializeField]
    [Tooltip("模拟的右侧安全宽度（像素）")]
    private float simulatedRightInset = 0f;

    /// <summary>
    /// 上一次应用过的安全区域矩形。
    /// </summary>
    private Rect _lastSafeRect = Rect.zero;

    /// <summary>
    /// 上一次应用时记录的屏幕尺寸。
    /// </summary>
    private Vector2Int _lastScreenSize = Vector2Int.zero;

    // 记录初始锚点值，用于增量式适配
    /// <summary>
    /// 初始最小锚点缓存。
    /// </summary>
    private Vector2 _initialAnchorMin = Vector2.zero;

    /// <summary>
    /// 初始最大锚点缓存。
    /// </summary>
    private Vector2 _initialAnchorMax = Vector2.one;

    /// <summary>
    /// 是否已经保存过初始锚点。
    /// </summary>
    private bool _initialAnchorsSaved = false;

    /// <summary>
    /// 当前是否至少启用了一个安全区域边的适配。
    /// </summary>
    private bool HasAnySafeAreaEdgeEnabled => applyLeft || applyRight || applyTop || applyBottom;

    /// <summary>
    /// 当前安全区域适配模式。
    /// </summary>
    public SafeAreaMode AdaptMode
    {
        get => adaptMode;
        set
        {
            if (adaptMode == value)
            {
                return;
            }

            adaptMode = value;
            ApplySafeArea(true);
        }
    }

    /// <summary>
    /// 当前适配目标 RectTransform。
    /// </summary>
    public RectTransform Target
    {
        get => target;
        set
        {
            if (target == value)
            {
                return;
            }

            target = value;
            ApplySafeArea(true);
        }
    }

    /// <summary>
    /// 是否应用左侧安全区域。
    /// </summary>
    public bool ApplyLeft
    {
        get => applyLeft;
        set
        {
            if (applyLeft == value)
            {
                return;
            }

            applyLeft = value;
            ApplySafeArea(true);
        }
    }

    /// <summary>
    /// 是否应用右侧安全区域。
    /// </summary>
    public bool ApplyRight
    {
        get => applyRight;
        set
        {
            if (applyRight == value)
            {
                return;
            }

            applyRight = value;
            ApplySafeArea(true);
        }
    }

    /// <summary>
    /// 是否应用顶部安全区域。
    /// </summary>
    public bool ApplyTop
    {
        get => applyTop;
        set
        {
            if (applyTop == value)
            {
                return;
            }

            applyTop = value;
            ApplySafeArea(true);
        }
    }

    /// <summary>
    /// 是否应用底部安全区域。
    /// </summary>
    public bool ApplyBottom
    {
        get => applyBottom;
        set
        {
            if (applyBottom == value)
            {
                return;
            }

            applyBottom = value;
            ApplySafeArea(true);
        }
    }

    /// <summary>
    /// 是否应用水平适配（左右锚点都启用）
    /// </summary>
    public bool ApplyHorizontal
    {
        get => applyLeft && applyRight;
        set
        {
            applyLeft = value;
            applyRight = value;
            ApplySafeArea(true);
        }
    }

    /// <summary>
    /// 是否应用垂直适配（上下锚点都启用）
    /// </summary>
    public bool ApplyVertical
    {
        get => applyTop && applyBottom;
        set
        {
            applyTop = value;
            applyBottom = value;
            ApplySafeArea(true);
        }
    }

    /// <summary>
    /// 额外边距设置。
    /// </summary>
    public Vector2 ExtraPadding
    {
        get => extraPadding;
        set
        {
            if (extraPadding == value)
            {
                return;
            }

            extraPadding = value;
            ApplySafeArea(true);
        }
    }

    /// <summary>
    /// 是否输出调试日志。
    /// </summary>
    public bool LogChanges
    {
        get => logChanges;
        set
        {
            logChanges = value;
        }
    }

    /// <summary>
    /// 外部配置入口（简化版）。
    /// </summary>
    public void Configure(bool horizontal, bool vertical, Vector2 padding)
    {
        applyLeft = horizontal;
        applyRight = horizontal;
        applyTop = vertical;
        applyBottom = vertical;
        extraPadding = padding;
        ApplySafeArea(true);
    }

    /// <summary>
    /// 外部配置入口（完整版，独立控制四个锚点）。
    /// </summary>
    public void ConfigureDetailed(bool left, bool right, bool top, bool bottom, Vector2 padding)
    {
        applyLeft = left;
        applyRight = right;
        applyTop = top;
        applyBottom = bottom;
        extraPadding = padding;
        ApplySafeArea(true);
    }

    /// <summary>
    /// 重置组件引用并保存初始锚点。
    /// </summary>
    private void Reset()
    {
        target = transform as RectTransform;

        // 重置时也要保存初始锚点
        SaveInitialAnchors();
    }

    /// <summary>
    /// 保存初始锚点值（使用自定义设置）
    /// </summary>
    private void SaveInitialAnchors()
    {
        if (target == null)
        {
            target = transform as RectTransform;
        }

        if (target != null)
        {
            // 左右锚点：直接使用设置值（从左往右，0-1）
            // 上下位置：从"从上往下"转换为Unity"从下往上"的锚点值
            // topPosition=0（顶部） → anchorMaxY = 1.0 - 0 = 1.0
            // bottomPosition=0.8（从顶部往下80%） → anchorMinY = 1.0 - 0.8 = 0.2
            _initialAnchorMin = new Vector2(initialAnchorMinX, 1f - bottomPosition);
            _initialAnchorMax = new Vector2(initialAnchorMaxX, 1f - topPosition);
            _initialAnchorsSaved = true;

            if (logChanges)
            {
                Log.Info($"[SafeAreaAdapter] 初始锚点设置 - Min: {_initialAnchorMin}, Max: {_initialAnchorMax} " +
                    $"(左:{initialAnchorMinX:F2} 右:{initialAnchorMaxX:F2} 顶部位置:{topPosition:F2} 底部位置:{bottomPosition:F2})");
            }
        }
    }

    /// <summary>
    /// 重置为初始锚点（调试用）
    /// </summary>
    public void ResetToInitialAnchors()
    {
        if (target != null && _initialAnchorsSaved)
        {
            target.anchorMin = _initialAnchorMin;
            target.anchorMax = _initialAnchorMax;

            if (logChanges)
            {
                Log.Info($"[SafeAreaAdapter] 重置为初始锚点 - Min: {_initialAnchorMin}, Max: {_initialAnchorMax}");
            }
        }
    }

    /// <summary>
    /// 从设置中刷新初始锚点并重新应用适配（供Editor调用）
    /// </summary>
    public void RefreshFromSettings()
    {
        SaveInitialAnchors();

#if UNITY_EDITOR
        // 编辑器下调整初始锚点配置时，只同步基础锚点，不直接套用安全区域，
        // 避免切换 Game 视图分辨率时把预览结果写回场景或 Prefab。
        if (!Application.isPlaying)
        {
            ResetToInitialAnchors();
            return;
        }
#endif

        ApplySafeArea(true);
    }

    /// <summary>
    /// 初始化目标节点并立即应用一次适配。
    /// </summary>
    private void Awake()
    {
        if (target == null)
        {
            target = transform as RectTransform;
        }

        // 保存初始锚点值
        SaveInitialAnchors();

        ApplySafeArea(true);
    }

    /// <summary>
    /// 启用时确保初始锚点存在并刷新适配。
    /// </summary>
    private void OnEnable()
    {
        // 如果还没保存初始锚点，先保存
        if (!_initialAnchorsSaved)
        {
            SaveInitialAnchors();
        }

        ApplySafeArea(true);
    }

    /// <summary>
    /// 每帧检查屏幕或安全区域变化。
    /// </summary>
    private void Update()
    {
        ApplySafeArea();
    }

    /// <summary>
    /// 尺寸变化时重新应用安全区域。
    /// </summary>
    private void OnRectTransformDimensionsChange()
    {
        ApplySafeArea();
    }

    /// <summary>
    /// 将值对齐到指定精度
    /// </summary>
    private float RoundToPrecision(float value, float precision)
    {
        if (precision <= 0f) return value;
        return Mathf.Round(value / precision) * precision;
    }

    /// <summary>
    /// 根据当前模式计算并应用安全区域锚点。
    /// </summary>
    private void ApplySafeArea(bool force = false)
    {
        if (target == null)
        {
            target = transform as RectTransform;
            if (target == null)
            {
                return;
            }
        }

        if (!HasAnySafeAreaEdgeEnabled)
        {
            return;
        }

#if UNITY_EDITOR
        // 编辑器非运行态默认不自动改 RectTransform，只有显式开启模拟安全区域时才允许预览，
        // 避免单纯切换 Game 分辨率就把布局结果持久化到资源。
        if (!Application.isPlaying && !simulateSafeAreaInEditor)
        {
            return;
        }
#endif

        if (!TryGetSafeArea(out Rect safeRect, out Vector2Int screenSize))
        {
            return;
        }

        if (!force && safeRect == _lastSafeRect && screenSize == _lastScreenSize)
        {
            return;
        }
        _lastSafeRect = safeRect;
        _lastScreenSize = screenSize;

        // 如果没有保存初始锚点，直接返回
        if (!_initialAnchorsSaved)
        {
            return;
        }

        // 始终从初始锚点开始计算，避免累积误差
        Vector2 anchorMin = _initialAnchorMin;
        Vector2 anchorMax = _initialAnchorMax;

        // 计算安全区域的偏移量（相对于全屏）
        float safeLeftOffset = screenSize.x > 0 ? Mathf.Clamp01(safeRect.x / screenSize.x) : 0f;
        float safeRightOffset = screenSize.x > 0 ? Mathf.Clamp01((screenSize.x - (safeRect.x + safeRect.width)) / screenSize.x) : 0f;
        float safeBottomOffset = screenSize.y > 0 ? Mathf.Clamp01(safeRect.y / screenSize.y) : 0f;
        float safeTopOffset = screenSize.y > 0 ? Mathf.Clamp01((screenSize.y - (safeRect.y + safeRect.height)) / screenSize.y) : 0f;

        // 独立控制左锚点（在初始值基础上叠加安全区域偏移）
        if (applyLeft)
        {
            // 新的左锚点 = 初始左锚点 + 安全区域左偏移
            anchorMin.x = Mathf.Clamp01(_initialAnchorMin.x + safeLeftOffset);
        }

        // 独立控制右锚点（在初始值基础上叠加安全区域偏移）
        if (applyRight)
        {
            // 新的右锚点 = 初始右锚点 - 安全区域右偏移
            anchorMax.x = Mathf.Clamp01(_initialAnchorMax.x - safeRightOffset);
        }

        // 独立控制下锚点（在初始值基础上叠加安全区域偏移）
        if (applyBottom)
        {
            // 新的下锚点 = 初始下锚点 + 安全区域下偏移
            anchorMin.y = Mathf.Clamp01(_initialAnchorMin.y + safeBottomOffset);
        }

        // 独立控制上锚点（在初始值基础上叠加安全区域偏移）
        if (applyTop)
        {
            // 新的上锚点 = 初始上锚点 - 安全区域上偏移
            anchorMax.y = Mathf.Clamp01(_initialAnchorMax.y - safeTopOffset);
        }

        // 对齐锚点精度，消除细微缝隙（分别控制四个锚点）
        if (anchorPrecisionLeft > 0f)
        {
            anchorMin.x = RoundToPrecision(anchorMin.x, anchorPrecisionLeft);
        }
        if (anchorPrecisionRight > 0f)
        {
            anchorMax.x = RoundToPrecision(anchorMax.x, anchorPrecisionRight);
        }
        if (anchorPrecisionBottom > 0f)
        {
            anchorMin.y = RoundToPrecision(anchorMin.y, anchorPrecisionBottom);
        }
        if (anchorPrecisionTop > 0f)
        {
            anchorMax.y = RoundToPrecision(anchorMax.y, anchorPrecisionTop);
        }

        target.anchorMin = anchorMin;
        target.anchorMax = anchorMax;

        Vector2 offsetMin = target.offsetMin;
        Vector2 offsetMax = target.offsetMax;

        // 左侧和下侧的额外边距
        if (applyLeft)
        {
            offsetMin.x = extraPadding.x;
        }
        if (applyBottom)
        {
            offsetMin.y = extraPadding.y;
        }

        // 右侧和上侧的额外边距
        if (applyRight)
        {
            offsetMax.x = -extraPadding.x;
        }
        if (applyTop)
        {
            offsetMax.y = -extraPadding.y;
        }

        target.offsetMin = offsetMin;
        target.offsetMax = offsetMax;

        if (logChanges)
        {
            Log.Info($"[SafeAreaAdapter] ========== 应用安全区域 ==========");
            Log.Info($"  SafeRect: {safeRect}, 屏幕: {screenSize.x}x{screenSize.y}");
            Log.Info($"  初始锚点: anchorMin={_initialAnchorMin}, anchorMax={_initialAnchorMax}");
            Log.Info($"  最终锚点: anchorMin={anchorMin}, anchorMax={anchorMax}");
            Log.Info($"  偏移量: offsetMin={offsetMin}, offsetMax={offsetMax}");
            Log.Info($"  应用边: 左={applyLeft}, 右={applyRight}, 上={applyTop}, 下={applyBottom}");

            // 计算实际偏移用于调试
            float debugLeftOffset = screenSize.x > 0 ? Mathf.Clamp01(safeRect.x / screenSize.x) : 0f;
            float debugTopOffset = screenSize.y > 0 ? Mathf.Clamp01((screenSize.y - (safeRect.y + safeRect.height)) / screenSize.y) : 0f;
            Log.Info($"  安全区域偏移百分比: 左={debugLeftOffset:F3}, 顶={debugTopOffset:F3}");
            Log.Info($"  顶部像素偏移: {screenSize.y - (safeRect.y + safeRect.height)}px");
        }
    }

    /// <summary>
    /// 获取当前应使用的安全区域矩形。
    /// </summary>
    private bool TryGetSafeArea(out Rect safeRect, out Vector2Int screenSize)
    {
        // 根据适配模式决定使用哪种API
        bool shouldUseWeChat = false;

        switch (adaptMode)
        {
            case SafeAreaMode.Auto:
                // 自动模式：在微信环境且定义了宏时使用微信API
#if WEIXINMINIGAME && !UNITY_EDITOR
                    shouldUseWeChat = true;
#endif
                break;

            case SafeAreaMode.ForceWeChat:
                // 强制微信模式：始终尝试使用微信API
                shouldUseWeChat = true;
                break;

            case SafeAreaMode.ForceStandard:
                // 强制标准模式：始终使用Unity API
                shouldUseWeChat = false;
                break;
        }

        // 尝试使用微信API
        if (shouldUseWeChat)
        {
#if UNITY_WEBGL || WEIXINMINIGAME || UNITY_EDITOR
            try
            {
                WindowInfo windowInfo = WX.GetWindowInfo();
                if (windowInfo != null)
                {
                    screenSize = new Vector2Int(
                        Mathf.Max(1, Mathf.RoundToInt((float)windowInfo.windowWidth)),
                        Mathf.Max(1, Mathf.RoundToInt((float)windowInfo.windowHeight))
                    );

                    var safeArea = windowInfo.safeArea;
                    if (safeArea != null)
                    {
                        float left = (float)safeArea.left;
                        float right = (float)safeArea.right;
                        float top = (float)safeArea.top;
                        float bottom = (float)safeArea.bottom;

                        // 根据微信适配策略处理
                        if (wechatStrategy == WeChatAdaptStrategy.Full)
                        {
                            // 完整适配模式：确保top至少包含胶囊底部
                            if (logChanges)
                            {
                                Log.Info($"[SafeAreaAdapter] <color=green>=========== 完整适配模式 ===========</color>");
                                Log.Info($"[SafeAreaAdapter] 屏幕尺寸: {screenSize.x} x {screenSize.y}");
                                Log.Info($"[SafeAreaAdapter] safeArea原始值: left={left}, right={right}, top={top}, bottom={bottom}");
                            }

                            // 获取胶囊信息并修正top值
                            try
                            {
                                var menuButton = WX.GetMenuButtonBoundingClientRect();
                                if (menuButton != null)
                                {
                                    float capsuleBottom = (float)menuButton.top + (float)menuButton.height;

                                    if (logChanges)
                                    {
                                        Log.Info($"[SafeAreaAdapter] <color=yellow>胶囊位置: left={menuButton.left}, top={menuButton.top}, width={menuButton.width}, height={menuButton.height}</color>");
                                        Log.Info($"[SafeAreaAdapter] <color=yellow>胶囊底部: {capsuleBottom}px, safeArea.top: {top}px</color>");
                                    }

                                    // 如果胶囊底部大于safeArea.top，使用胶囊底部
                                    if (capsuleBottom > top)
                                    {
                                        float oldTop = top;
                                        top = capsuleBottom;

                                        if (logChanges)
                                        {
                                            Log.Info($"[SafeAreaAdapter] <color=cyan>✓ 修正top值: {oldTop}px → {top}px（使用胶囊底部）</color>");
                                        }
                                    }
                                    else
                                    {
                                        if (logChanges)
                                        {
                                            Log.Info($"[SafeAreaAdapter] <color=green>✓ safeArea.top 已包含胶囊区域</color>");
                                        }
                                    }
                                }
                                else
                                {
                                    if (logChanges)
                                    {
                                        Log.Warning("[SafeAreaAdapter] 无法获取胶囊按钮信息（menuButton为null）");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                if (logChanges)
                                {
                                    Log.Warning($"[SafeAreaAdapter] 获取胶囊信息异常: {ex.Message}");
                                }
                            }
                        }
                        else if (wechatStrategy == WeChatAdaptStrategy.SystemOnly)
                        {
                            // 仅适配系统安全区域（刘海等），移除胶囊按钮的影响
                            try
                            {
                                var menuButton = WX.GetMenuButtonBoundingClientRect();
                                if (menuButton != null)
                                {
                                    float capsuleBottom = (float)menuButton.top + (float)menuButton.height;

                                    // 获取系统状态栏高度（通常是刘海区域）
                                    var systemInfo = WX.GetSystemInfoSync();
                                    float statusBarHeight = systemInfo != null
                                        ? (float)systemInfo.statusBarHeight
                                        : 0f;

                                    // 如果top被胶囊影响（top >= 胶囊底部），则使用状态栏高度代替
                                    if (top >= capsuleBottom && statusBarHeight > 0)
                                    {
                                        top = statusBarHeight;

                                        if (logChanges)
                                        {
                                            Log.Info($"[SafeAreaAdapter] <color=cyan>仅系统安全区域模式</color> - 使用状态栏高度: {statusBarHeight}px (原safeArea.top: {top}px, 胶囊底部: {capsuleBottom}px)");
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                if (logChanges)
                                {
                                    Log.Warning($"[SafeAreaAdapter] 获取胶囊信息失败，使用原始safeArea: {ex.Message}");
                                }
                            }
                        }

                        // 计算安全区域尺寸（注意：修改了top后必须重新计算height）
                        float width = right - left;
                        float height = bottom - top;

                        float yFromBottom = Mathf.Max(0f, screenSize.y - bottom);

                        safeRect = new Rect(left, yFromBottom, Mathf.Max(0f, width), Mathf.Max(0f, height));

                        if (logChanges)
                        {
                            string strategyDesc = wechatStrategy == WeChatAdaptStrategy.SystemOnly
                                ? "<color=cyan>仅系统安全区域</color>"
                                : "<color=green>完整适配（含胶囊）</color>";
                            Log.Info($"[SafeAreaAdapter] 使用微信API获取安全区域成功 [{strategyDesc}]");
                            Log.Info($"  计算结果: width={width}, height={height}, yFromBottom={yFromBottom}");
                            Log.Info($"  最终safeRect: x={safeRect.x}, y={safeRect.y}, width={safeRect.width}, height={safeRect.height}");
                            Log.Info($"  顶部偏移量(从屏幕顶部): {top}px");
                            Log.Info($"  底部偏移量(从屏幕底部): {screenSize.y - bottom}px");
                        }
                        return true;
                    }
                }
            }
            catch (Exception e)
            {
                if (logChanges)
                {
                    Log.Warning($"[SafeAreaAdapter] 获取微信安全区域失败，降级使用标准模式: {e.Message}");
                }
            }
#else
                if (logChanges)
                {
                    Log.Warning("[SafeAreaAdapter] 当前平台不支持微信API，降级使用标准模式");
                }
#endif
        }

        // 使用标准Unity API（降级方案或ForceStandard模式）
        screenSize = new Vector2Int(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
        Rect unitySafeArea = Screen.safeArea;

        // 编辑器模拟模式
#if UNITY_EDITOR
        if (simulateSafeAreaInEditor && (adaptMode == SafeAreaMode.ForceStandard || adaptMode == SafeAreaMode.ForceWeChat))
        {
            // 计算模拟的安全区域
            float left = simulatedLeftInset;
            float right = screenSize.x - simulatedRightInset;
            float bottom = simulatedBottomInset;
            float top = screenSize.y - simulatedTopInset;

            float width = right - left;
            float height = top - bottom;

            unitySafeArea = new Rect(left, bottom, width, height);

            if (logChanges)
            {
                Log.Info($"[SafeAreaAdapter] <color=yellow>编辑器模拟模式</color> - 模拟SafeArea: {unitySafeArea}, 屏幕: {screenSize}");
            }
        }
        else
#endif
        {
            if (unitySafeArea.width <= 0f || unitySafeArea.height <= 0f)
            {
                unitySafeArea = new Rect(0f, 0f, screenSize.x, screenSize.y);
            }
        }

        safeRect = unitySafeArea;

        if (logChanges)
        {
            // 检查是否有安全区域偏移
            bool hasSafeArea = (unitySafeArea.x > 0 || unitySafeArea.y > 0 ||
                               unitySafeArea.width < screenSize.x ||
                               unitySafeArea.height < screenSize.y);

            if (hasSafeArea)
            {
                Log.Info($"[SafeAreaAdapter] 使用标准模式 Screen.safeArea: {unitySafeArea}, 屏幕: {screenSize}");
            }
            else
            {
                Log.Warning($"[SafeAreaAdapter] 使用标准模式，但Screen.safeArea等于全屏（没有刘海/圆角）\n" +
                               $"SafeArea: {unitySafeArea}, 屏幕: {screenSize}\n" +
                               $"这是正常的，说明当前设备/编辑器没有需要适配的安全区域");
            }
        }

        return true;
    }
}
