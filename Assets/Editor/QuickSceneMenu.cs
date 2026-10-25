using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// 顶部菜单快捷入口：一键打开 Scene_Init（编辑器菜单栏「场景」页签）。
/// </summary>
public static class QuickSceneMenu
{
    private const string SceneInitPath = "Assets/Scenes/Scene_Init.unity";

    [MenuItem("场景/打开 Scene_Init %#1", false, 1)]
    public static void OpenSceneInit()
    {
        if (EditorApplication.isPlaying)
        {
            if (!EditorUtility.DisplayDialog(
                    "打开 Scene_Init",
                    "当前正在 Play 模式。要先退出 Play 再打开 Scene_Init 吗？",
                    "退出并打开", "取消"))
                return;

            EditorApplication.isPlaying = false;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (System.IO.File.Exists(SceneInitPath) == false)
        {
            Debug.LogError("[QuickSceneMenu] 场景不存在: " + SceneInitPath);
            return;
        }

        EditorSceneManager.OpenScene(SceneInitPath, OpenSceneMode.Single);
        Debug.Log("[QuickSceneMenu] 已打开 " + SceneInitPath);
    }

    [MenuItem("场景/打开 Scene_Init %#1", true)]
    public static bool OpenSceneInitValidate()
    {
        return true;
    }
}
