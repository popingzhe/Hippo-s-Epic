using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(Interactable))]
public class InteractableEditor : Editor
{
    private SerializedProperty _phasesProp;

    private void OnEnable()
    {
        _phasesProp = serializedObject.FindProperty("phases");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        for (int i = 0; i < _phasesProp.arraySize; i++)
        {
            var phase = _phasesProp.GetArrayElementAtIndex(i);
            DrawPhase(phase, i);
        }

        if (GUILayout.Button("+ 添加 Phase"))
        {
            _phasesProp.InsertArrayElementAtIndex(_phasesProp.arraySize);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawPhase(SerializedProperty phase, int index)
    {
        var stateProp = phase.FindPropertyRelative("state");
        var hintTextProp = phase.FindPropertyRelative("hintText");
        var buttonsProp = phase.FindPropertyRelative("buttons");
        var conditionTriggersProp = phase.FindPropertyRelative("conditionTriggers");
        var activeChildNamesProp = phase.FindPropertyRelative("activeChildNames");
        var playAnimationProp = phase.FindPropertyRelative("playAnimation");
        var animationControllerProp = phase.FindPropertyRelative("animationController");
        var compositionProp = phase.FindPropertyRelative("composition");
        var canRepeatProp = phase.FindPropertyRelative("canRepeat");
        var hideAfterExecuteProp = phase.FindPropertyRelative("hideAfterExecute");
        var destroySelfProp = phase.FindPropertyRelative("destroySelf");
        var deactivateSelfProp = phase.FindPropertyRelative("deactivateSelf");
        var autoTriggerProp = phase.FindPropertyRelative("autoTriggerFirstButton");

        EditorGUILayout.BeginHorizontal();
        phase.isExpanded = EditorGUILayout.Foldout(phase.isExpanded, $"Phase {stateProp.intValue}", true, EditorStyles.boldLabel);
        if (GUILayout.Button("删除", EditorStyles.miniButton, GUILayout.Width(45)))
        {
            if (EditorUtility.DisplayDialog("确认删除", $"确定要删除 Phase {stateProp.intValue} 吗？", "删除", "取消"))
            {
                _phasesProp.DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
                EditorGUILayout.EndHorizontal();
                return;
            }
        }
        EditorGUILayout.EndHorizontal();

        if (!phase.isExpanded) return;

        EditorGUI.indentLevel++;

        EditorGUILayout.PropertyField(stateProp);

        EditorGUILayout.LabelField("提示文字");
        hintTextProp.stringValue = EditorGUILayout.TextArea(hintTextProp.stringValue, GUILayout.Height(EditorGUIUtility.singleLineHeight * 4));

        EditorGUILayout.PropertyField(buttonsProp, true);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("条件触发器", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(conditionTriggersProp, true);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("子节点样式", EditorStyles.boldLabel);
        DrawActiveChildNames(activeChildNamesProp);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("动画插槽", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(playAnimationProp, new GUIContent("播放动画"));
        if (playAnimationProp.boolValue)
        {
            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(animationControllerProp, new GUIContent("AnimationController"));
            DrawCompositionDropdown(compositionProp, animationControllerProp, playAnimationProp);
            EditorGUI.indentLevel--;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("行为", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(canRepeatProp);
        EditorGUILayout.PropertyField(hideAfterExecuteProp);
        EditorGUILayout.PropertyField(destroySelfProp);
        EditorGUILayout.PropertyField(deactivateSelfProp);

        EditorGUILayout.Space(2);
        EditorGUILayout.PropertyField(autoTriggerProp, new GUIContent("进入范围自动触发第一个按钮", "勾选后：玩家进入交互范围时直接触发本阶段第一个可用按钮，不等按键/点击"));

        EditorGUI.indentLevel--;

        EditorGUILayout.Space(4);
    }

    private void DrawActiveChildNames(SerializedProperty activeChildNamesProp)
    {
        var interactable = (Interactable)target;
        if (interactable == null)
        {
            EditorGUILayout.PropertyField(activeChildNamesProp, true);
            return;
        }

        var styleRoot = interactable.transform.Find("Style");
        var childNames = new List<string>();
        if (styleRoot != null)
        {
            for (int i = 0; i < styleRoot.childCount; i++)
                childNames.Add(styleRoot.GetChild(i).name);
        }

        if (childNames.Count == 0)
        {
            EditorGUILayout.HelpBox("Style 节点下没有子节点", MessageType.Info);
            EditorGUILayout.PropertyField(activeChildNamesProp, true);
            return;
        }

        var childSet = new HashSet<string>(childNames);

        for (int i = activeChildNamesProp.arraySize - 1; i >= 0; i--)
        {
            if (!childSet.Contains(activeChildNamesProp.GetArrayElementAtIndex(i).stringValue))
                activeChildNamesProp.DeleteArrayElementAtIndex(i);
        }

        var selected = new HashSet<string>();
        for (int i = 0; i < activeChildNamesProp.arraySize; i++)
            selected.Add(activeChildNamesProp.GetArrayElementAtIndex(i).stringValue);

        EditorGUI.indentLevel++;
        foreach (var childName in childNames)
        {
            bool wasSelected = selected.Contains(childName);
            bool isSelected = EditorGUILayout.ToggleLeft(childName, wasSelected);

            if (isSelected != wasSelected)
            {
                if (isSelected)
                {
                    activeChildNamesProp.InsertArrayElementAtIndex(activeChildNamesProp.arraySize);
                    activeChildNamesProp.GetArrayElementAtIndex(activeChildNamesProp.arraySize - 1).stringValue = childName;
                }
                else
                {
                    for (int i = 0; i < activeChildNamesProp.arraySize; i++)
                    {
                        if (activeChildNamesProp.GetArrayElementAtIndex(i).stringValue == childName)
                        {
                            activeChildNamesProp.DeleteArrayElementAtIndex(i);
                            break;
                        }
                    }
                }
            }
        }
        EditorGUI.indentLevel--;
    }

    private void DrawCompositionDropdown(SerializedProperty compositionProp, SerializedProperty animCtrlProp, SerializedProperty playAnimationProp)
    {
        var ctrl = animCtrlProp.objectReferenceValue as AnimationController;

        if (ctrl == null || ctrl.Config == null)
        {
            EditorGUILayout.PropertyField(compositionProp, new GUIContent("Composition"));
            return;
        }

        var available = ctrl.GetCompositionNames();
        if (available == null || available.Count == 0)
        {
            EditorGUILayout.PropertyField(compositionProp, new GUIContent("Composition"));
            return;
        }

        var names = new List<string>();
        var values = new List<int>();

        names.Add("(不播放)");
        values.Add(-1);

        for (int i = 0; i < available.Count; i++)
        {
            names.Add(available[i].ToString());
            values.Add((int)available[i]);
        }

        int currentVal = (int)compositionProp.enumValueIndex;
        int selected = values.IndexOf(currentVal);
        if (selected < 0) selected = 0;

        int newSelected = EditorGUILayout.Popup("Composition", selected, names.ToArray());
        if (newSelected != selected)
        {
            if (values[newSelected] < 0)
            {
                compositionProp.enumValueIndex = 0;
                playAnimationProp.boolValue = false;
            }
            else
            {
                compositionProp.enumValueIndex = values[newSelected];
            }
        }
    }
}
