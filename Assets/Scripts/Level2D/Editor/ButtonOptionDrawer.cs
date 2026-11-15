using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomPropertyDrawer(typeof(ButtonOption))]
public class ButtonOptionDrawer : PropertyDrawer
{
    private enum TeleportMode
    {
        ToInteractable = 0,
        CrossScene = 1
    }

    private static readonly GUIContent[] TeleportModeLabels =
    {
        new GUIContent("传送到交互点"),
        new GUIContent("跨场景")
    };

    private static readonly GUIContent[] SetStateLabels =
    {
        new GUIContent("覆盖为已满足"),
        new GUIContent("覆盖为未满足"),
        new GUIContent("清除覆盖(恢复条件表)")
    };

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var typeProp = property.FindPropertyRelative("type");
        var param1Prop = property.FindPropertyRelative("param1");
        var param2Prop = property.FindPropertyRelative("param2");
        var dataIdProp = property.FindPropertyRelative("dataId");
        var buttonTextProp = property.FindPropertyRelative("buttonText");
        var transProp = property.FindPropertyRelative("transitionToState");
        var tpModeProp = property.FindPropertyRelative("teleportMode");
        var condProp = property.FindPropertyRelative("conditionId");

        float y = position.y;
        float w = position.width;
        float lineH = EditorGUIUtility.singleLineHeight;
        float sp = EditorGUIUtility.standardVerticalSpacing;

        var tt = (InteractionType)typeProp.enumValueIndex;
        var tpMode = (TeleportMode)tpModeProp.intValue;

        buttonTextProp.stringValue = EditorGUI.TextField(
            new Rect(position.x, y, w, lineH), "按钮文字", buttonTextProp.stringValue);
        y += lineH + sp;

        EditorGUI.BeginChangeCheck();
        tt = (InteractionType)EditorGUI.EnumPopup(
            new Rect(position.x, y, w, lineH), "交互类型", tt);
        bool typeChanged = EditorGUI.EndChangeCheck();
        if (typeChanged)
        {
            typeProp.enumValueIndex = (int)tt;
            param1Prop.stringValue = "";
            param2Prop.stringValue = "";
            tpModeProp.intValue = (int)TeleportMode.ToInteractable;
            if (tt == InteractionType.OpenUI)
                dataIdProp.intValue = 0;
            property.serializedObject.ApplyModifiedProperties();
            return;
        }
        y += lineH + sp;

        if (tt == InteractionType.Teleport)
        {
            EditorGUI.BeginChangeCheck();
            tpMode = (TeleportMode)EditorGUI.Popup(
                new Rect(position.x, y, w, lineH),
                new GUIContent("传送模式"), (int)tpMode, TeleportModeLabels);
            bool modeChanged = EditorGUI.EndChangeCheck();
            if (modeChanged)
            {
                tpModeProp.intValue = (int)tpMode;
                param1Prop.stringValue = "";
                param2Prop.stringValue = "";
                property.serializedObject.ApplyModifiedProperties();
                return;
            }
            y += lineH + sp;

            switch (tpMode)
            {
                case TeleportMode.ToInteractable:
                    DrawToInteractable(position.x, y, w, lineH, sp, param1Prop);
                    y += (lineH + sp) * 2;
                    break;
                case TeleportMode.CrossScene:
                    DrawCrossScene(position.x, y, w, lineH, sp, param1Prop, param2Prop);
                    y += (lineH + sp) * 2;
                    break;
            }
        }
        else if (tt == InteractionType.OpenUI)
        {
            var keys = UIWindowRegistry.GetWindowKeys();
            string current = param1Prop.stringValue ?? "";
            bool unknown = !string.IsNullOrEmpty(current) && System.Array.IndexOf(keys, current) < 0;

            // 未注册值单独追加一项：既不占用“(无)”槽位，也不会被误点清空
            var options = new GUIContent[keys.Length + 1 + (unknown ? 1 : 0)];
            options[0] = new GUIContent("(无)");
            for (int i = 0; i < keys.Length; i++)
                options[i + 1] = new GUIContent(keys[i]);
            if (unknown)
                options[keys.Length + 1] = new GUIContent("（未注册）" + current);

            int selected = unknown ? keys.Length + 1 : System.Array.IndexOf(keys, current) + 1;
            EditorGUI.BeginChangeCheck();
            int next = EditorGUI.Popup(new Rect(position.x, y, w, lineH),
                new GUIContent("目标UI面板"), selected, options);
            if (EditorGUI.EndChangeCheck())
            {
                if (next == 0)
                    param1Prop.stringValue = "";
                else if (next <= keys.Length)
                    param1Prop.stringValue = keys[next - 1];
                // next == keys.Length + 1（未注册项）：保持原值，不清空
            }
            y += lineH + sp;

            var kind = UIWindowRegistry.GetArgsKind(param1Prop.stringValue);
            string idLabel = kind == UIPanelArgs.Int ? "面板参数ID（必填）"
                : kind == UIPanelArgs.MapRootId ? "面板参数ID（地图编号）"
                : "面板参数ID";
            string idTip = kind == UIPanelArgs.Int ? "该面板需要参数ID（对话ID/物品ID），填 0 会被拒绝并保持可重试"
                : kind == UIPanelArgs.MapRootId ? "参数ID 会转成地图编号，0 = 朋克城"
                : "普通面板填 0：运行时传 null";
            dataIdProp.intValue = EditorGUI.IntField(new Rect(position.x, y, w, lineH),
                new GUIContent(idLabel, idTip), dataIdProp.intValue);
            y += lineH + sp;
        }
        else
        {
            bool isConsume = tt == InteractionType.Consume;
            bool isSetCond = tt == InteractionType.SetCondition;
            bool isGainBuff = tt == InteractionType.GainBuff;
            bool isEnterBattle = tt == InteractionType.EnterBattle;
            string idLabel = isConsume ? "物品ID" : (isSetCond ? "目标条件ID" : "dataId");

            dataIdProp.intValue = EditorGUI.IntField(
                new Rect(position.x, y, w, lineH), idLabel, dataIdProp.intValue);
            y += lineH + sp;

            if (isSetCond)
            {
                string v = param1Prop.stringValue == null ? "" : param1Prop.stringValue.Trim().ToLowerInvariant();
                int sel = (v == "0" || v == "false") ? 1 : (v == "clear" || v == "release" || v == "2") ? 2 : 0;
                EditorGUI.BeginChangeCheck();
                sel = EditorGUI.Popup(
                    new Rect(position.x, y, w, lineH), new GUIContent("目标状态"), sel, SetStateLabels);
                if (EditorGUI.EndChangeCheck())
                    param1Prop.stringValue = sel == 1 ? "0" : (sel == 2 ? "clear" : "1");
                y += lineH + sp;
            }
            else
            {
                param1Prop.stringValue = EditorGUI.TextField(
                    new Rect(position.x, y, w, lineH), isConsume ? "扣除数量" : "param1", param1Prop.stringValue);
                y += lineH + sp;
            }

            if (!isConsume && !isSetCond)
            {
                param2Prop.stringValue = EditorGUI.TextField(
                    new Rect(position.x, y, w, lineH), "param2", param2Prop.stringValue);
                y += lineH + sp;
            }
        }

        transProp.intValue = EditorGUI.IntField(
            new Rect(position.x, y, w, lineH), "transitionToState", transProp.intValue);
        y += lineH + sp;

        condProp.intValue = EditorGUI.IntField(
            new Rect(position.x, y, w, lineH), "条件ID", condProp.intValue);

        EditorGUI.EndProperty();
    }

    private static TeleportMode DetectTeleportMode(
        SerializedProperty p1, SerializedProperty p2)
    {
        if (!string.IsNullOrEmpty(p2.stringValue)) return TeleportMode.CrossScene;
        return TeleportMode.ToInteractable;
    }

    #region Draw Methods

    private static void DrawToInteractable(float x, float y, float w, float lineH, float sp,
        SerializedProperty param1Prop)
    {
        EditorGUI.LabelField(new Rect(x, y, w, lineH), "选择目标交互点（当前场景）");
        y += lineH + sp;

        var (entityIds, displayNames) = CollectFromCurrentScene();

        if (entityIds.Length > 0)
        {
            int eIdx = System.Array.IndexOf(entityIds, param1Prop.stringValue);
            EditorGUI.BeginChangeCheck();
            int newEIdx = EditorGUI.Popup(
                new Rect(x, y, w, lineH),
                new GUIContent("目标"), eIdx + 1, ToGUIContent(displayNames));
            if (EditorGUI.EndChangeCheck())
                param1Prop.stringValue = newEIdx > 0 ? entityIds[newEIdx - 1] : "";
        }
        else
        {
            EditorGUI.LabelField(new Rect(x, y, w, lineH), "（当前场景无交互点）");
        }
    }

    private static void DrawCrossScene(float x, float y, float w, float lineH, float sp,
        SerializedProperty param1Prop, SerializedProperty param2Prop)
    {
        string[] sceneNames = TeleportTargetDef.AllSceneNames;
        int idx = System.Array.IndexOf(sceneNames, param2Prop.stringValue);

        EditorGUI.BeginChangeCheck();
        int newIdx = EditorGUI.Popup(
            new Rect(x, y, w, lineH),
            new GUIContent("目标场景"), idx + 1, ToGUIContent(sceneNames));
        if (EditorGUI.EndChangeCheck())
        {
            param2Prop.stringValue = newIdx > 0 ? sceneNames[newIdx - 1] : "";
            param1Prop.stringValue = "";
        }

        y += lineH + sp;

        string[] mapNames = string.IsNullOrEmpty(param2Prop.stringValue)
            ? System.Array.Empty<string>()
            : TeleportTargetDef.GetMapNames(param2Prop.stringValue);

        if (mapNames.Length > 0)
        {
            int mapIdx = System.Array.IndexOf(mapNames, param1Prop.stringValue);
            EditorGUI.BeginChangeCheck();
            int newMapIdx = EditorGUI.Popup(
                new Rect(x, y, w, lineH),
                new GUIContent("目标地图"), mapIdx + 1, ToGUIContent(mapNames));
            if (EditorGUI.EndChangeCheck())
                param1Prop.stringValue = newMapIdx > 0 ? mapNames[newMapIdx - 1] : "";
        }
        else
        {
            EditorGUI.LabelField(new Rect(x, y, w, lineH), "（该场景无 Map 数据）");
        }
    }

    #endregion

    #region Helpers

    private static (string[] ids, string[] names) CollectFromCurrentScene()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var maps = new List<Map>();
        foreach (var go in scene.GetRootGameObjects())
            maps.AddRange(go.GetComponentsInChildren<Map>(true));

        var idList = new List<string>();
        var nameList = new List<string>();

        foreach (var go in scene.GetRootGameObjects())
        {
            foreach (var ia in go.GetComponentsInChildren<Interactable>(true))
            {
                string eid = ia.EntityId;
                string mapName = FindParentMapName(ia.transform, maps);
                idList.Add(eid);
                nameList.Add(string.IsNullOrEmpty(mapName) ? eid : $"[{mapName}] {eid}");
            }
        }

        return (idList.ToArray(), nameList.ToArray());
    }

    private static string FindParentMapName(Transform t, List<Map> maps)
    {
        Transform cur = t.parent;
        while (cur != null)
        {
            var map = maps.Find(m => m.transform == cur);
            if (map != null) return map.name;
            cur = cur.parent;
        }
        return null;
    }

    private static GUIContent[] ToGUIContent(string[] names)
    {
        var opts = new GUIContent[names.Length + 1];
        opts[0] = new GUIContent("(无)");
        for (int i = 0; i < names.Length; i++)
            opts[i + 1] = new GUIContent(names[i]);
        return opts;
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var tt = (InteractionType)property.FindPropertyRelative("type").enumValueIndex;
        int lines = 4;

        if (tt == InteractionType.Teleport)
        {
            lines += 3;
        }
        else if (tt == InteractionType.Consume || tt == InteractionType.SetCondition || tt == InteractionType.OpenUI)
        {
            lines += 2;
        }
        else
        {
            lines += 3;
        }

        return lines * EditorGUIUtility.singleLineHeight
             + (lines - 1) * EditorGUIUtility.standardVerticalSpacing;
    }

    #endregion
}
