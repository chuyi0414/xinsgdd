using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using TMPro;

/// <summary>
/// 编辑器窗口：批量替换预制体中所有 Text (TMP) 组件的字体资源。
/// </summary>
public class TMPFontReplacerWindow : EditorWindow
{
    /// <summary>目标字体资源</summary>
    private TMP_FontAsset _targetFont;

    /// <summary>待处理的预制体列表</summary>
    private readonly List<GameObject> _prefabs = new();

    private Vector2 _scrollPos;

    [MenuItem("Tools/TMP 字体替换")]
    public static void ShowWindow()
    {
        GetWindow<TMPFontReplacerWindow>("TMP 字体替换");
    }

    private void OnGUI()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

        GUILayout.Label("批量替换预制体中 Text (TMP) 的字体", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        _targetFont = (TMP_FontAsset)EditorGUILayout.ObjectField("目标字体", _targetFont, typeof(TMP_FontAsset), false);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("预制体列表", EditorStyles.boldLabel);

        // 拖拽区域
        var dropRect = GUILayoutUtility.GetRect(0, 40, GUILayout.ExpandWidth(true));
        GUI.Box(dropRect, "将预制体拖拽到此处添加");
        HandleDragAndDrop(dropRect);

        EditorGUILayout.Space();

        // 显示列表
        for (int i = _prefabs.Count - 1; i >= 0; i--)
        {
            EditorGUILayout.BeginHorizontal();
            _prefabs[i] = (GameObject)EditorGUILayout.ObjectField(_prefabs[i], typeof(GameObject), false);
            if (GUILayout.Button("×", GUILayout.Width(25)))
            {
                _prefabs.RemoveAt(i);
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space();

        if (GUILayout.Button("清空列表"))
        {
            _prefabs.Clear();
        }

        EditorGUILayout.Space();

        GUI.enabled = _prefabs.Count > 0 && _targetFont != null;
        if (GUILayout.Button("替换列表中预制体的字体"))
        {
            ReplaceFonts(_prefabs);
        }
        GUI.enabled = true;

        EditorGUILayout.EndScrollView();
    }

    /// <summary>处理拖拽到窗口区域的预制体。</summary>
    private void HandleDragAndDrop(Rect dropRect)
    {
        Event evt = Event.current;
        if (evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
            return;
        if (!dropRect.Contains(evt.mousePosition))
            return;

        DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

        if (evt.type == EventType.DragPerform)
        {
            DragAndDrop.AcceptDrag();
            foreach (var obj in DragAndDrop.objectReferences)
            {
                if (obj is GameObject go && !_prefabs.Contains(go))
                {
                    _prefabs.Add(go);
                }
            }
        }

        evt.Use();
    }

    /// <summary>遍历预制体列表，替换所有 TextMeshProUGUI 和 TextMeshPro 的字体。</summary>
    private void ReplaceFonts(List<GameObject> targets)
    {
        int count = 0;
        foreach (var go in targets)
        {
            if (go == null) continue;

            // 预制体资产需要用 LoadPrefabContents 加载后修改再保存
            string assetPath = AssetDatabase.GetAssetPath(go);
            if (!string.IsNullOrEmpty(assetPath))
            {
                var root = PrefabUtility.LoadPrefabContents(assetPath);
                int changed = ReplaceFontOnGameObject(root);
                if (changed > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, assetPath);
                    count += changed;
                }
                PrefabUtility.UnloadPrefabContents(root);
            }
            else
            {
                // 场景中的对象直接修改
                count += ReplaceFontOnGameObject(go);
            }
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[TMPFontReplacer] 已将 {count} 个 Text (TMP) 组件的字体替换为 {_targetFont.name}。");
    }

    /// <summary>替换单个 GameObject 及其子物体上所有 TMP 组件的字体。</summary>
    private int ReplaceFontOnGameObject(GameObject go)
    {
        int count = 0;

        foreach (var tmp in go.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            tmp.font = _targetFont;
            count++;
        }

        foreach (var tmp in go.GetComponentsInChildren<TextMeshPro>(true))
        {
            tmp.font = _targetFont;
            count++;
        }

        return count;
    }
}
