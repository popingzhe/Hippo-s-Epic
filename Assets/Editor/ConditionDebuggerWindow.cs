using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class ConditionDebuggerWindow : EditorWindow
{
    private Vector2 _scroll;
    private string _search = "";
    private int _forceId;

    [MenuItem("Tools/条件调试")]
    public static void ShowWindow()
    {
        GetWindow<ConditionDebuggerWindow>("条件调试");
    }

    private void OnGUI()
    {
        EditorGUILayout.Space(4);

        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(60)))
            Repaint();
        GUILayout.FlexibleSpace();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        DrawForceSection();
        EditorGUILayout.Space(8);
        DrawConditionList();
    }

    private void DrawForceSection()
    {
        EditorGUILayout.LabelField("强制激活条件", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        _forceId = EditorGUILayout.IntField("条件 ID", _forceId);
        if (GUILayout.Button("激活", GUILayout.Width(60)))
        {
            if (Application.isPlaying)
                ConditionSystem.Instance.ForceSetCondition(_forceId, true);
            else
                Debug.LogWarning("[ConditionDebugger] 需要在运行模式下使用");
        }
        if (GUILayout.Button("关闭", GUILayout.Width(60)))
        {
            if (Application.isPlaying)
                ConditionSystem.Instance.ForceSetCondition(_forceId, false);
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawConditionList()
    {
        EditorGUILayout.LabelField("条件列表", EditorStyles.boldLabel);

        _search = EditorGUILayout.TextField("搜索", _search);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("需要在运行模式下查看条件状态", MessageType.Info);
            return;
        }

        var ids = ConditionSystem.Instance.GetAllConditionIds();
        if (ids == null || ids.Count == 0)
        {
            EditorGUILayout.HelpBox("无条件数据，请确认 Tables 已加载", MessageType.Warning);
            return;
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        foreach (var id in ids)
        {
            if (!string.IsNullOrEmpty(_search) && !id.ToString().Contains(_search))
                continue;

            var info = ConditionSystem.Instance.GetConditionInfo(id)
                     + "  " + ConditionSystem.Instance.GetConditionBrief(id);
            EditorGUILayout.BeginHorizontal("box");
            EditorGUILayout.LabelField(info, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("满足", GUILayout.Width(50)))
                ConditionSystem.Instance.ForceSetCondition(id, true);
            if (GUILayout.Button("未满足", GUILayout.Width(58)))
                ConditionSystem.Instance.ForceSetCondition(id, false);
            if (ConditionSystem.Instance.HasOverride(id))
            {
                if (GUILayout.Button("清覆盖", GUILayout.Width(58)))
                    ConditionSystem.Instance.ClearOverride(id);
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.EndScrollView();
    }
}
