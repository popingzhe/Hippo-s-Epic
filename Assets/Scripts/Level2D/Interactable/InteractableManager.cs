using UnityEngine;

public static class InteractableManager
{
    public static void InitializeMap(Map map)
    {
        if (map == null)
            return;

        var interactables = map.InteractableList;
        int matchCount = 0;

        foreach (var interactable in interactables)
        {
            if (interactable == null)
                continue;

            var firstPhase = interactable.GetPhase(0);
            if (firstPhase == null)
            {
                Debug.LogWarning($"[InteractableManager] {interactable.name} 缺少 state=0 的交互阶段 ⇒ 该交互体被跳过初始化（提示框不会创建）。请给它补一个 state=0 的阶段");
                continue;
            }

            string eid = interactable.EntityId;
            int state;

            if (SaveManager.Instance.HasEntityState(eid))
            {
                state = SaveManager.Instance.GetEntityState(eid);
            }
            else
            {
                state = firstPhase.state;
            }

            interactable.ApplyState(state);
            if (interactable.CurrentPhase == null)
                Debug.LogWarning($"[InteractableManager] {interactable.name} 存档状态 state={state} 与阶段配置不一致 ⇒ 已中断、未执行（提示框将是空白）");
            interactable.InitializeHint();
            matchCount++;
        }

        Debug.Log($"[InteractableManager] 地图 {map.name} 初始化完成，匹配 {matchCount} 个交互实体");
    }

    public static void InitializeScene(Map[] sceneMaps)
    {
        if (sceneMaps == null || sceneMaps.Length == 0)
            return;

        int totalCount = 0;
        foreach (var map in sceneMaps)
        {
            if (map == null) continue;
            InitializeMap(map);
            totalCount += map.InteractableList.Count;
        }

        Debug.Log($"[InteractableManager] 场景初始化完成，共 {sceneMaps.Length} 个地图，{totalCount} 个交互实体");
    }

    public static void InitializeScene()
    {
        var maps = Object.FindObjectsOfType<Map>(true);
        InitializeScene(maps);
    }
}
