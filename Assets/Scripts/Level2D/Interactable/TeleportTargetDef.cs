// ============================================================
// 自动生成文件 - 勿手动修改
// 生成时间: 2026-10-25 18:18:16
// 通过 Tools/Map/收集所有场景数据 重新生成
// ============================================================

public static class TeleportTargetDef
{
    public struct SceneEntry
    {
        public string sceneName;
        public string[] mapNames;
        public InteractableEntry[] interactables;
    }

    public struct InteractableEntry
    {
        public string entityId;
        public string mapName;
    }

    public static readonly SceneEntry[] Scenes = new SceneEntry[]
    {
    };

    public static string[] AllSceneNames
    {
        get
        {
            var names = new string[Scenes.Length];
            for (int i = 0; i < Scenes.Length; i++)
                names[i] = Scenes[i].sceneName;
            return names;
        }
    }

    public static string[] GetMapNames(string sceneName)
    {
        foreach (var s in Scenes)
            if (s.sceneName == sceneName) return s.mapNames;
        return System.Array.Empty<string>();
    }

    public static InteractableEntry[] GetInteractables(string sceneName)
    {
        foreach (var s in Scenes)
            if (s.sceneName == sceneName) return s.interactables;
        return System.Array.Empty<InteractableEntry>();
    }

    public static InteractableEntry[] GetInteractablesInCurrentScene()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        return GetInteractables(scene.name);
    }
}
