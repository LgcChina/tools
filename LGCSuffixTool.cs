
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// LGC - 物体后缀批处理工具
/// 菜单入口：LGC/后缀批处理工具
/// 功能：拖入物体，批量为名称添加或移除后缀，默认应用到所有子级
/// 仅在编辑器编译（#if UNITY_EDITOR）
/// </summary>
public class LGCSuffixTool : EditorWindow
{
    // 配置项
    private string suffix = "_LGC";                  // 要添加/移除的后缀
    private string separator = "";                   // 可选分隔符，如 "_" 或 "-"
    private bool applyToChildren = true;             // 默认勾选：应用到所有子级
    private bool includeInactive = true;             // 是否处理非激活子物体
    private bool ignoreCase = true;                  // 后缀匹配是否忽略大小写
    private bool trimWhitespace = true;              // 处理前是否修剪空格
    private bool showPreview = false;                // 预览折叠

    // 拖拽/列表
    private readonly HashSet<GameObject> targets = new HashSet<GameObject>();

    // GUI 风格
    private GUIStyle dropBoxStyle;

    [MenuItem("LGC/后缀批处理工具")]
    public static void ShowWindow()
    {
        var win = GetWindow<LGCSuffixTool>("后缀批处理工具");
        win.minSize = new Vector2(460, 320);
        win.Show();
    }

    private void OnGUI()
    {
        if (dropBoxStyle == null)
        {
            dropBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
        }

        DrawHeader();
        EditorGUILayout.Space(6);
        DrawDropArea();
        EditorGUILayout.Space(6);
        DrawTargetList();
        EditorGUILayout.Space(4);
        DrawPreview();
        EditorGUILayout.Space(4);
        DrawActions();
    }

    private void DrawHeader()
    {
        EditorGUILayout.LabelField("批量后缀处理", EditorStyles.boldLabel);

        suffix = EditorGUILayout.TextField(new GUIContent("后缀", "需要添加或移除的后缀文本"), suffix);
        separator = EditorGUILayout.TextField(new GUIContent("分隔符", "添加到后缀前的分隔符（例如 _ 或 -）。留空则直接拼接后缀。"), separator);

        EditorGUILayout.BeginHorizontal();
        applyToChildren = EditorGUILayout.ToggleLeft(new GUIContent("应用到所有子级（默认）", "对所有子 Transform 应用"), applyToChildren);
        includeInactive = EditorGUILayout.ToggleLeft(new GUIContent("包含未激活子物体", "也处理非激活对象"), includeInactive);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        ignoreCase = EditorGUILayout.ToggleLeft(new GUIContent("后缀匹配忽略大小写", "判断是否已存在后缀时忽略大小写"), ignoreCase);
        trimWhitespace = EditorGUILayout.ToggleLeft(new GUIContent("去除名称两端空格", "处理前先 Trim()"), trimWhitespace);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("添加当前选中对象", GUILayout.Height(24)))
        {
            foreach (var go in Selection.gameObjects)
            {
                if (go != null) targets.Add(go);
            }
        }

        if (GUILayout.Button("清空列表", GUILayout.Height(24)))
        {
            targets.Clear();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawDropArea()
    {
        var rect = GUILayoutUtility.GetRect(0, 60, GUILayout.ExpandWidth(true));
        GUI.Box(rect, "将物体从层级（Hierarchy）拖到此区域", dropBoxStyle);

        // 处理拖拽
        var evt = Event.current;
        if (!rect.Contains(evt.mousePosition)) return;

        switch (evt.type)
        {
            case EventType.DragUpdated:
            case EventType.DragPerform:
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
                if (evt.type == EventType.DragPerform)
                {
                    DragAndDrop.AcceptDrag();
                    foreach (var obj in DragAndDrop.objectReferences)
                    {
                        var go = obj as GameObject;
                        if (go != null)
                        {
                            // 这里只处理场景中的 GameObject（Hierarchy）。如果是 Prefab 资产，会在层级拖入实例时正常处理。
                            targets.Add(go);
                        }
                    }
                }
                Event.current.Use();
                break;
        }
    }

    private void DrawTargetList()
    {
        EditorGUILayout.LabelField($"对象列表（{targets.Count}）", EditorStyles.boldLabel);

        if (targets.Count == 0)
        {
            EditorGUILayout.HelpBox("尚未添加任何对象。可拖拽层级中的物体到上方区域，或点击“添加当前选中对象”。", MessageType.Info);
            return;
        }

        foreach (var go in targets.ToList())
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.ObjectField(go, typeof(GameObject), true);
            if (GUILayout.Button("移除", GUILayout.Width(60)))
            {
                targets.Remove(go);
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    private void DrawPreview()
    {
        showPreview = EditorGUILayout.Foldout(showPreview, "预览（基于当前配置）");
        if (!showPreview || targets.Count == 0) return;

        var desiredSuffix = BuildDesiredSuffix();
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        foreach (var go in targets)
        {
            var list = EnumerateTransforms(go, applyToChildren, includeInactive).ToList();
            int willAdd = 0, willRemove = 0;

            foreach (var t in list)
            {
                string currentName = GetPreparedName(t.name);
                if (EndsWithSuffix(currentName, desiredSuffix, ignoreCase))
                    willRemove++;
                else
                    willAdd++;
            }

            EditorGUILayout.LabelField(
                $"{go.name} → 将添加：{willAdd} 个；将移除：{willRemove} 个（包含子级：{applyToChildren}）");
        }
        EditorGUILayout.EndVertical();
    }

    private void DrawActions()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginDisabledGroup(string.IsNullOrEmpty(suffix) || targets.Count == 0);
            if (GUILayout.Button("添加后缀", GUILayout.Height(28)))
            {
                ProcessTargets(Operation.Add);
            }
            if (GUILayout.Button("移除后缀", GUILayout.Height(28)))
            {
                ProcessTargets(Operation.Remove);
            }
            EditorGUI.EndDisabledGroup();
        }
    }

    private enum Operation { Add, Remove }

    private void ProcessTargets(Operation op)
    {
        string desiredSuffix = BuildDesiredSuffix();
        int total = 0;

        foreach (var go in targets)
        {
            foreach (var t in EnumerateTransforms(go, applyToChildren, includeInactive))
            {
                Undo.RecordObject(t, op == Operation.Add ? "LGC 添加后缀" : "LGC 移除后缀");
                string currentName = GetPreparedName(t.name);

                if (op == Operation.Add)
                {
                    // 已存在后缀则跳过
                    if (EndsWithSuffix(currentName, desiredSuffix, ignoreCase)) continue;
                    t.name = currentName + desiredSuffix;
                    total++;
                }
                else // Remove
                {
                    if (!EndsWithSuffix(currentName, desiredSuffix, ignoreCase)) continue;
                    t.name = RemoveSuffix(currentName, desiredSuffix, ignoreCase);
                    total++;
                }

                EditorUtility.SetDirty(t);
            }
        }

        if (total > 0)
        {
            Debug.Log($"[LGC后缀工具] { (op == Operation.Add ? "添加" : "移除") }完成，共处理 {total} 个对象名称。");
        }
        else
        {
            Debug.Log("[LGC后缀工具] 没有需要更改的对象（可能已存在相同后缀或列表为空）。");
        }
    }

    // —— 辅助方法 —— //

    private IEnumerable<Transform> EnumerateTransforms(GameObject root, bool includeChildren, bool includeInactiveChildren)
    {
        if (root == null) yield break;

        // 根节点
        yield return root.transform;

        if (!includeChildren) yield break;

        // 遍历子级
        var stack = new Stack<Transform>();
        for (int i = 0; i < root.transform.childCount; i++)
            stack.Push(root.transform.GetChild(i));

        while (stack.Count > 0)
        {
            var tr = stack.Pop();
            if (includeInactiveChildren || tr.gameObject.activeInHierarchy)
                yield return tr;

            for (int i = 0; i < tr.childCount; i++)
                stack.Push(tr.GetChild(i));
        }
    }

    private string BuildDesiredSuffix()
    {
        string sep = string.IsNullOrEmpty(separator) ? "" : separator;
        string suf = string.IsNullOrEmpty(suffix) ? "" : suffix;
        return sep + suf;
    }

    private string GetPreparedName(string name)
    {
        return trimWhitespace ? name.Trim() : name;
    }

    private bool EndsWithSuffix(string name, string desiredSuffix, bool ignoreCaseMatch)
    {
        if (string.IsNullOrEmpty(desiredSuffix)) return false;
        var comparison = ignoreCaseMatch ? System.StringComparison.OrdinalIgnoreCase : System.StringComparison.Ordinal;
        return name.EndsWith(desiredSuffix, comparison);
    }

    private string RemoveSuffix(string name, string desiredSuffix, bool ignoreCaseMatch)
    {
        if (!EndsWithSuffix(name, desiredSuffix, ignoreCaseMatch)) return name;
        return name.Substring(0, name.Length - desiredSuffix.Length);
    }
}
#endif
