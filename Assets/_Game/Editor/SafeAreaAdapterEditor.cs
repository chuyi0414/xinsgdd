using System.Collections;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// SafeAreaAdapter 自定义编辑器
/// 提供友好的界面和快捷操作
/// </summary>
[CustomEditor(typeof(SafeAreaAdapter))]
public class SafeAreaAdapterEditor : UnityEditor.Editor
{
    private SerializedProperty adaptModeProperty;
    private SerializedProperty targetProperty;
    private SerializedProperty applyLeftProperty;
    private SerializedProperty applyRightProperty;
    private SerializedProperty applyTopProperty;
    private SerializedProperty applyBottomProperty;
    private SerializedProperty extraPaddingProperty;
    private SerializedProperty logChangesProperty;

    private SerializedProperty initialAnchorMinXProperty;
    private SerializedProperty initialAnchorMaxXProperty;
    private SerializedProperty topPositionProperty;
    private SerializedProperty bottomPositionProperty;

    private SerializedProperty anchorPrecisionLeftProperty;
    private SerializedProperty anchorPrecisionRightProperty;
    private SerializedProperty anchorPrecisionTopProperty;
    private SerializedProperty anchorPrecisionBottomProperty;

    private SerializedProperty wechatStrategyProperty;

    private SerializedProperty simulateSafeAreaInEditorProperty;
    private SerializedProperty simulatedTopInsetProperty;
    private SerializedProperty simulatedBottomInsetProperty;
    private SerializedProperty simulatedLeftInsetProperty;
    private SerializedProperty simulatedRightInsetProperty;

    private void OnEnable()
    {
        adaptModeProperty = serializedObject.FindProperty("adaptMode");
        targetProperty = serializedObject.FindProperty("target");
        applyLeftProperty = serializedObject.FindProperty("applyLeft");
        applyRightProperty = serializedObject.FindProperty("applyRight");
        applyTopProperty = serializedObject.FindProperty("applyTop");
        applyBottomProperty = serializedObject.FindProperty("applyBottom");
        extraPaddingProperty = serializedObject.FindProperty("extraPadding");
        logChangesProperty = serializedObject.FindProperty("logChanges");

        initialAnchorMinXProperty = serializedObject.FindProperty("initialAnchorMinX");
        initialAnchorMaxXProperty = serializedObject.FindProperty("initialAnchorMaxX");
        topPositionProperty = serializedObject.FindProperty("topPosition");
        bottomPositionProperty = serializedObject.FindProperty("bottomPosition");

        anchorPrecisionLeftProperty = serializedObject.FindProperty("anchorPrecisionLeft");
        anchorPrecisionRightProperty = serializedObject.FindProperty("anchorPrecisionRight");
        anchorPrecisionTopProperty = serializedObject.FindProperty("anchorPrecisionTop");
        anchorPrecisionBottomProperty = serializedObject.FindProperty("anchorPrecisionBottom");

        wechatStrategyProperty = serializedObject.FindProperty("wechatStrategy");

        simulateSafeAreaInEditorProperty = serializedObject.FindProperty("simulateSafeAreaInEditor");
        simulatedTopInsetProperty = serializedObject.FindProperty("simulatedTopInset");
        simulatedBottomInsetProperty = serializedObject.FindProperty("simulatedBottomInset");
        simulatedLeftInsetProperty = serializedObject.FindProperty("simulatedLeftInset");
        simulatedRightInsetProperty = serializedObject.FindProperty("simulatedRightInset");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var adapter = target as SafeAreaAdapter;

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "【安全区域适配器】\n" +
            "自动调整UI以适配不同设备的安全区域（刘海屏、圆角屏、微信胶囊等）",
            MessageType.Info);

        EditorGUILayout.Space();

        // 适配模式选择
        EditorGUILayout.LabelField("适配模式", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(adaptModeProperty, new GUIContent("模式选择", "选择安全区域适配的模式"));

        // 根据选择的模式显示说明
        SafeAreaMode currentMode = (SafeAreaMode)adaptModeProperty.enumValueIndex;
        EditorGUILayout.Space();

        switch (currentMode)
        {
            case SafeAreaMode.Auto:
                EditorGUILayout.HelpBox(
                    "【自动模式（推荐）】\n" +
                    "✓ 自动检测运行环境\n" +
                    "• 微信小游戏环境：使用微信API（WX.GetWindowInfo）\n" +
                    "  → 自动避开顶部胶囊按钮\n" +
                    "• 其他环境：使用标准API（Screen.safeArea）\n" +
                    "  → 适配刘海屏、圆角屏、底部指示器等",
                    MessageType.Info);
                break;

            case SafeAreaMode.ForceStandard:
                EditorGUILayout.HelpBox(
                    "【标准模式】\n" +
                    "✓ 强制使用Unity标准API\n" +
                    "• 始终使用：Screen.safeArea\n" +
                    "• 适用于：iOS刘海屏、Android圆角屏、底部Home指示器等\n" +
                    "• 不会使用微信API，即使在微信环境中\n\n" +
                    "⚠️ 注意：\n" +
                    "• 编辑器中Screen.safeArea通常等于全屏（无效果）\n" +
                    "• 在没有刘海/圆角的设备上也会等于全屏（正常现象）\n" +
                    "• 请在有刘海的真机上测试以查看实际效果\n" +
                    "• 建议开启【显示调试日志】查看详细信息",
                    MessageType.Warning);
                break;

            case SafeAreaMode.ForceWeChat:
                EditorGUILayout.HelpBox(
                    "【微信模式】\n" +
                    "✓ 强制使用微信API（WX.GetWindowInfo）\n" +
                    "• 可以选择不同的适配策略（见下方）\n" +
                    "• 降级方案：如果微信API不可用，降级到Screen.safeArea\n" +
                    "• 适用于：微信小游戏环境",
                    MessageType.Info);

                // 微信适配策略选择
                if (wechatStrategyProperty != null)
                {
                    EditorGUILayout.Space();
                    EditorGUI.indentLevel++;
                    EditorGUILayout.LabelField("微信适配策略", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(wechatStrategyProperty,
                        new GUIContent("策略", "选择微信小游戏的适配策略"));

                    WeChatAdaptStrategy currentStrategy = (WeChatAdaptStrategy)wechatStrategyProperty.enumValueIndex;
                    EditorGUILayout.Space();

                    if (currentStrategy == WeChatAdaptStrategy.Full)
                    {
                        EditorGUILayout.HelpBox(
                            "【完整适配】（推荐）\n" +
                            "✓ 同时适配刘海 + 微信胶囊按钮\n" +
                            "• 顶部会同时避开刘海和胶囊按钮\n" +
                            "• 适用于大部分UI界面\n" +
                            "• 确保UI不被任何元素遮挡",
                            MessageType.Info);
                    }
                    else
                    {
                        EditorGUILayout.HelpBox(
                            "【仅系统安全区域】\n" +
                            "✓ 只适配刘海等系统安全区域\n" +
                            "✗ 不会为微信胶囊按钮留白\n" +
                            "• 适用于需要更大顶部空间的UI\n" +
                            "• 注意：UI可能会被胶囊按钮部分遮挡\n" +
                            "• 建议：确保重要内容不在胶囊区域",
                            MessageType.Warning);
                    }
                    EditorGUI.indentLevel--;
                }
                else
                {
                    EditorGUILayout.Space();
                    EditorGUILayout.HelpBox(
                        "⚠️ 编辑器脚本需要重新编译\n" +
                        "请尝试：Assets → Refresh 或重新选择此对象",
                        MessageType.Warning);
                }
                break;
        }

        EditorGUILayout.Space();

        // 目标RectTransform
        EditorGUILayout.LabelField("目标设置", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(targetProperty, new GUIContent("目标RectTransform", "要应用安全区域适配的RectTransform，默认为自身"));

        if (targetProperty.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox("未指定目标，将使用自身的RectTransform", MessageType.Info);
        }

        EditorGUILayout.Space();

        // 方向设置 - 独立控制四个锚点
        EditorGUILayout.LabelField("适配方向（独立控制）", EditorStyles.boldLabel);

        EditorGUILayout.PropertyField(applyLeftProperty, new GUIContent("左锚点", "是否适配左侧安全区域"));
        EditorGUILayout.PropertyField(applyRightProperty, new GUIContent("右锚点", "是否适配右侧安全区域"));
        EditorGUILayout.PropertyField(applyTopProperty, new GUIContent("上锚点", "是否适配顶部安全区域（刘海、胶囊等）"));
        EditorGUILayout.PropertyField(applyBottomProperty, new GUIContent("下锚点", "是否适配底部安全区域（指示器等）"));

        EditorGUILayout.Space();

        // 额外设置
        EditorGUILayout.LabelField("额外设置", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(extraPaddingProperty, new GUIContent("额外边距", "在安全区域基础上额外添加的边距"));
        EditorGUILayout.PropertyField(logChangesProperty, new GUIContent("显示调试日志", "在Console中输出适配信息"));

        EditorGUILayout.Space();

        // 初始锚点设置
        EditorGUILayout.LabelField("初始锚点设置", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "水平：从左往右设置位置（0-1）\n" +
            "垂直：从屏幕顶部往下设置位置（0-1）\n" +
            "例如：顶部0，底部0.8 → UI从顶部0%到80%，底部留20%空白\n\n" +
            "💡 调整这些值时，RectTransform锚点会实时同步更新",
            MessageType.Info);

        // 检测初始锚点值是否改变
        EditorGUI.BeginChangeCheck();

        EditorGUILayout.PropertyField(initialAnchorMinXProperty, new GUIContent("左锚点", "从左往右（0-1）"));
        EditorGUILayout.PropertyField(initialAnchorMaxXProperty, new GUIContent("右锚点", "从左往右（0-1）"));
        EditorGUILayout.PropertyField(topPositionProperty, new GUIContent("顶部位置", "从屏幕顶部往下（0-1）。0=顶部，0.1=从顶部往下10%"));
        EditorGUILayout.PropertyField(bottomPositionProperty, new GUIContent("底部位置", "从屏幕顶部往下（0-1）。0.8=从顶部往下80%，底部留20%"));

        // 如果值改变了，立即更新RectTransform
        if (EditorGUI.EndChangeCheck())
        {
            serializedObject.ApplyModifiedProperties();

            // 刷新初始锚点并重新应用适配
            if (adapter != null)
            {
                adapter.RefreshFromSettings();
                EditorUtility.SetDirty(adapter);
            }
        }

        EditorGUILayout.Space();

        // 快捷预设
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("左10%"))
        {
            initialAnchorMinXProperty.floatValue = 0f;
            initialAnchorMaxXProperty.floatValue = 0.1f;
            ApplyAnchorChanges();
        }
        if (GUILayout.Button("右90%"))
        {
            initialAnchorMinXProperty.floatValue = 0.1f;
            initialAnchorMaxXProperty.floatValue = 1f;
            ApplyAnchorChanges();
        }
        if (GUILayout.Button("全屏"))
        {
            initialAnchorMinXProperty.floatValue = 0f;
            initialAnchorMaxXProperty.floatValue = 1f;
            topPositionProperty.floatValue = 0f;
            bottomPositionProperty.floatValue = 1f;
            ApplyAnchorChanges();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("顶部20%"))
        {
            topPositionProperty.floatValue = 0f;
            bottomPositionProperty.floatValue = 0.2f;
            ApplyAnchorChanges();
        }
        if (GUILayout.Button("底部20%"))
        {
            topPositionProperty.floatValue = 0.8f;
            bottomPositionProperty.floatValue = 1f;
            ApplyAnchorChanges();
        }
        if (GUILayout.Button("中间60%"))
        {
            topPositionProperty.floatValue = 0.2f;
            bottomPositionProperty.floatValue = 0.8f;
            ApplyAnchorChanges();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space();

        // 锚点精度设置 - 四个独立控制
        EditorGUILayout.LabelField("锚点精度对齐（消除缝隙）", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "设置每个锚点的对齐精度，消除UI之间的细微缝隙\n" +
            "0 = 不对齐  |  0.01 = 百分位  |  0.001 = 千分位（推荐）",
            MessageType.Info);

        // 左锚点精度
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(anchorPrecisionLeftProperty, new GUIContent("左锚点精度", "左侧锚点的对齐精度"));
        if (GUILayout.Button("0.01", GUILayout.Width(45)))
        {
            anchorPrecisionLeftProperty.floatValue = 0.01f;
            serializedObject.ApplyModifiedProperties();
        }
        if (GUILayout.Button("0.001", GUILayout.Width(45)))
        {
            anchorPrecisionLeftProperty.floatValue = 0.001f;
            serializedObject.ApplyModifiedProperties();
        }
        EditorGUILayout.EndHorizontal();

        // 右锚点精度
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(anchorPrecisionRightProperty, new GUIContent("右锚点精度", "右侧锚点的对齐精度"));
        if (GUILayout.Button("0.01", GUILayout.Width(45)))
        {
            anchorPrecisionRightProperty.floatValue = 0.01f;
            serializedObject.ApplyModifiedProperties();
        }
        if (GUILayout.Button("0.001", GUILayout.Width(45)))
        {
            anchorPrecisionRightProperty.floatValue = 0.001f;
            serializedObject.ApplyModifiedProperties();
        }
        EditorGUILayout.EndHorizontal();

        // 上锚点精度
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(anchorPrecisionTopProperty, new GUIContent("上锚点精度", "顶部锚点的对齐精度"));
        if (GUILayout.Button("0.01", GUILayout.Width(45)))
        {
            anchorPrecisionTopProperty.floatValue = 0.01f;
            serializedObject.ApplyModifiedProperties();
        }
        if (GUILayout.Button("0.001", GUILayout.Width(45)))
        {
            anchorPrecisionTopProperty.floatValue = 0.001f;
            serializedObject.ApplyModifiedProperties();
        }
        EditorGUILayout.EndHorizontal();

        // 下锚点精度
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PropertyField(anchorPrecisionBottomProperty, new GUIContent("下锚点精度", "底部锚点的对齐精度"));
        if (GUILayout.Button("0.01", GUILayout.Width(45)))
        {
            anchorPrecisionBottomProperty.floatValue = 0.01f;
            serializedObject.ApplyModifiedProperties();
        }
        if (GUILayout.Button("0.001", GUILayout.Width(45)))
        {
            anchorPrecisionBottomProperty.floatValue = 0.001f;
            serializedObject.ApplyModifiedProperties();
        }
        EditorGUILayout.EndHorizontal();

        // 快捷按钮：全部设置
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("全部 0.01"))
        {
            anchorPrecisionLeftProperty.floatValue = 0.01f;
            anchorPrecisionRightProperty.floatValue = 0.01f;
            anchorPrecisionTopProperty.floatValue = 0.01f;
            anchorPrecisionBottomProperty.floatValue = 0.01f;
            serializedObject.ApplyModifiedProperties();
        }
        if (GUILayout.Button("全部 0.001"))
        {
            anchorPrecisionLeftProperty.floatValue = 0.001f;
            anchorPrecisionRightProperty.floatValue = 0.001f;
            anchorPrecisionTopProperty.floatValue = 0.001f;
            anchorPrecisionBottomProperty.floatValue = 0.001f;
            serializedObject.ApplyModifiedProperties();
        }
        if (GUILayout.Button("全部关闭"))
        {
            anchorPrecisionLeftProperty.floatValue = 0f;
            anchorPrecisionRightProperty.floatValue = 0f;
            anchorPrecisionTopProperty.floatValue = 0f;
            anchorPrecisionBottomProperty.floatValue = 0f;
            serializedObject.ApplyModifiedProperties();
        }
        EditorGUILayout.EndHorizontal();

        // 编辑器模拟（仅在ForceStandard模式下显示）
        if (currentMode == SafeAreaMode.ForceStandard)
        {
            EditorGUILayout.Space();

            EditorGUILayout.PropertyField(simulateSafeAreaInEditorProperty,
                new GUIContent("模拟安全区域", "在编辑器中模拟安全区域，用于测试ForceStandard模式（仅编辑器有效）"));

            if (simulateSafeAreaInEditorProperty.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox(
                    "模拟模式已启用，将使用下方设置的安全区域值\n" +
                    "这样可以在编辑器中预览ForceStandard的效果",
                    MessageType.Info);

                EditorGUILayout.PropertyField(simulatedTopInsetProperty, new GUIContent("顶部留白（px）", "模拟顶部安全高度，如刘海、状态栏"));
                EditorGUILayout.PropertyField(simulatedBottomInsetProperty, new GUIContent("底部留白（px）", "模拟底部安全高度，如Home指示器"));
                EditorGUILayout.PropertyField(simulatedLeftInsetProperty, new GUIContent("左侧留白（px）", "模拟左侧安全宽度"));
                EditorGUILayout.PropertyField(simulatedRightInsetProperty, new GUIContent("右侧留白（px）", "模拟右侧安全宽度"));

                // 快捷预设按钮
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("快捷预设", EditorStyles.miniLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("iPhone X"))
                {
                    simulatedTopInsetProperty.floatValue = 44f;
                    simulatedBottomInsetProperty.floatValue = 34f;
                    simulatedLeftInsetProperty.floatValue = 0f;
                    simulatedRightInsetProperty.floatValue = 0f;
                    serializedObject.ApplyModifiedProperties();
                }
                if (GUILayout.Button("iPhone 14 Pro"))
                {
                    simulatedTopInsetProperty.floatValue = 59f;
                    simulatedBottomInsetProperty.floatValue = 34f;
                    simulatedLeftInsetProperty.floatValue = 0f;
                    simulatedRightInsetProperty.floatValue = 0f;
                    serializedObject.ApplyModifiedProperties();
                }
                if (GUILayout.Button("清除"))
                {
                    simulatedTopInsetProperty.floatValue = 0f;
                    simulatedBottomInsetProperty.floatValue = 0f;
                    simulatedLeftInsetProperty.floatValue = 0f;
                    simulatedRightInsetProperty.floatValue = 0f;
                    serializedObject.ApplyModifiedProperties();
                }
                EditorGUILayout.EndHorizontal();

                EditorGUI.indentLevel--;
            }
        }

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("快捷操作", EditorStyles.boldLabel);

        // 快捷配置按钮 - 第一行：整体控制
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("全部适配"))
        {
            applyLeftProperty.boolValue = true;
            applyRightProperty.boolValue = true;
            applyTopProperty.boolValue = true;
            applyBottomProperty.boolValue = true;
            serializedObject.ApplyModifiedProperties();
        }
        if (GUILayout.Button("仅垂直"))
        {
            applyLeftProperty.boolValue = false;
            applyRightProperty.boolValue = false;
            applyTopProperty.boolValue = true;
            applyBottomProperty.boolValue = true;
            serializedObject.ApplyModifiedProperties();
        }
        if (GUILayout.Button("仅水平"))
        {
            applyLeftProperty.boolValue = true;
            applyRightProperty.boolValue = true;
            applyTopProperty.boolValue = false;
            applyBottomProperty.boolValue = false;
            serializedObject.ApplyModifiedProperties();
        }
        if (GUILayout.Button("全部关闭"))
        {
            applyLeftProperty.boolValue = false;
            applyRightProperty.boolValue = false;
            applyTopProperty.boolValue = false;
            applyBottomProperty.boolValue = false;
            serializedObject.ApplyModifiedProperties();
        }
        EditorGUILayout.EndHorizontal();

        // 快捷配置按钮 - 第二行：单独锚点
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("仅上"))
        {
            applyLeftProperty.boolValue = false;
            applyRightProperty.boolValue = false;
            applyTopProperty.boolValue = true;
            applyBottomProperty.boolValue = false;
            serializedObject.ApplyModifiedProperties();
        }
        if (GUILayout.Button("仅下"))
        {
            applyLeftProperty.boolValue = false;
            applyRightProperty.boolValue = false;
            applyTopProperty.boolValue = false;
            applyBottomProperty.boolValue = true;
            serializedObject.ApplyModifiedProperties();
        }
        if (GUILayout.Button("仅左"))
        {
            applyLeftProperty.boolValue = true;
            applyRightProperty.boolValue = false;
            applyTopProperty.boolValue = false;
            applyBottomProperty.boolValue = false;
            serializedObject.ApplyModifiedProperties();
        }
        if (GUILayout.Button("仅右"))
        {
            applyLeftProperty.boolValue = false;
            applyRightProperty.boolValue = true;
            applyTopProperty.boolValue = false;
            applyBottomProperty.boolValue = false;
            serializedObject.ApplyModifiedProperties();
        }
        EditorGUILayout.EndHorizontal();

        // 快捷配置按钮 - 第三行：常用组合
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("上+左"))
        {
            applyLeftProperty.boolValue = true;
            applyRightProperty.boolValue = false;
            applyTopProperty.boolValue = true;
            applyBottomProperty.boolValue = false;
            serializedObject.ApplyModifiedProperties();
        }
        if (GUILayout.Button("上+右"))
        {
            applyLeftProperty.boolValue = false;
            applyRightProperty.boolValue = true;
            applyTopProperty.boolValue = true;
            applyBottomProperty.boolValue = false;
            serializedObject.ApplyModifiedProperties();
        }
        if (GUILayout.Button("下+左"))
        {
            applyLeftProperty.boolValue = true;
            applyRightProperty.boolValue = false;
            applyTopProperty.boolValue = false;
            applyBottomProperty.boolValue = true;
            serializedObject.ApplyModifiedProperties();
        }
        if (GUILayout.Button("下+右"))
        {
            applyLeftProperty.boolValue = false;
            applyRightProperty.boolValue = true;
            applyTopProperty.boolValue = false;
            applyBottomProperty.boolValue = true;
            serializedObject.ApplyModifiedProperties();
        }
        EditorGUILayout.EndHorizontal();

        // 运行时信息
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("设备信息", EditorStyles.boldLabel);

        // 始终显示屏幕信息（即使不在运行）
        EditorGUILayout.LabelField("屏幕分辨率", $"{Screen.width} x {Screen.height}");

        Rect currentSafeArea = Screen.safeArea;
        EditorGUILayout.LabelField("Screen.safeArea", currentSafeArea.ToString());

        // 检查是否有安全区域偏移
        bool hasSafeAreaOffset = (currentSafeArea.x > 0 || currentSafeArea.y > 0 ||
                                 currentSafeArea.width < Screen.width ||
                                 currentSafeArea.height < Screen.height);

        if (!hasSafeAreaOffset)
        {
            EditorGUILayout.HelpBox(
                "当前Screen.safeArea等于全屏（无偏移）\n" +
                "ForceStandard模式在此情况下不会有视觉效果",
                MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "✓ 检测到安全区域偏移\n" +
                "ForceStandard模式会应用此偏移",
                MessageType.Info);
        }

        if (Application.isPlaying)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("当前RectTransform状态", EditorStyles.boldLabel);

            var rectTransform = adapter.Target;
            if (rectTransform != null)
            {
                EditorGUILayout.LabelField("锚点Min", rectTransform.anchorMin.ToString("F3"));
                EditorGUILayout.LabelField("锚点Max", rectTransform.anchorMax.ToString("F3"));
                EditorGUILayout.LabelField("偏移Min", rectTransform.offsetMin.ToString("F1"));
                EditorGUILayout.LabelField("偏移Max", rectTransform.offsetMax.ToString("F1"));
            }
        }
        else
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "点击播放按钮后将显示RectTransform的实时状态",
                MessageType.Info);
        }

        // 编译器宏定义提示
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("编译器信息", EditorStyles.boldLabel);

#if WEIXINMINIGAME
            EditorGUILayout.HelpBox(
                "✓ WEIXINMINIGAME 宏定义已启用\n" +
                "自动模式将在微信环境中使用微信API",
                MessageType.Info);
#else
        EditorGUILayout.HelpBox(
            "○ WEIXINMINIGAME 宏定义未启用\n" +
            "自动模式将使用标准Screen.safeArea\n\n" +
            "如需启用微信支持：\n" +
            "File → Build Settings → Player Settings → Scripting Define Symbols\n" +
            "添加：WEIXINMINIGAME",
            MessageType.Info);
#endif

        serializedObject.ApplyModifiedProperties();
    }

    /// <summary>
    /// 应用初始锚点设置的改变到RectTransform
    /// </summary>
    private void ApplyAnchorChanges()
    {
        serializedObject.ApplyModifiedProperties();

        var adapter = target as SafeAreaAdapter;
        if (adapter != null)
        {
            adapter.RefreshFromSettings();
            EditorUtility.SetDirty(adapter);
        }
    }
}
