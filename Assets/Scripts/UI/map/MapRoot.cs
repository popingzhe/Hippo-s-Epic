using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class MapRoot : MonoBehaviour
{
    [System.Serializable]
    public class MapNodeEntry
    {
        [Tooltip("节点枚举标识")]
        public MapNodeId nodeId;

        [Tooltip("Button reference")]
        public Button button;

        [Tooltip("Target map to switch to")]
        public SceneMapId targetMap;

        [Tooltip("传送条件ID，0=无条件，其他=条件表ID")]
        public int conditionId;

        public Vector2 HeadPosition => button != null ? button.GetComponent<RectTransform>().anchoredPosition : Vector2.zero;
    }

    [System.Serializable]
    public class MapTransition
    {
        [Tooltip("起始节点")]
        public MapNodeId fromNode;

        [Tooltip("目标节点")]
        public MapNodeId toNode;

        [Tooltip("正向动画（from→to）")]
        public AnimationClip forwardClip;

        [Tooltip("反向动画（to→from），由工具自动生成，无需手动配置")]
        public AnimationClip reverseClip;
    }

    [SerializeField] private MapNodeEntry[] nodes;
    [SerializeField] private MapTransition[] transitions;

    private Dictionary<MapNodeId, MapNodeEntry> _nodeDict;

    public IReadOnlyList<MapNodeEntry> Nodes => nodes;

    private void BuildDict()
    {
        if (_nodeDict != null) return;
        _nodeDict = new Dictionary<MapNodeId, MapNodeEntry>();
        if (nodes == null) return;
        foreach (var entry in nodes)
            _nodeDict[entry.nodeId] = entry;
    }

    public void Initialize(System.Action<SceneMapId> onNodeClicked)
    {
        if (nodes == null) return;

        foreach (var entry in nodes)
        {
            if (entry.button == null) continue;

            entry.button.interactable = true;
            entry.button.onClick.RemoveAllListeners();

            var targetMap = entry.targetMap;
            entry.button.onClick.AddListener(() => onNodeClicked?.Invoke(targetMap));
        }
    }

    /// <summary>
    /// 按条件刷新节点可见性：conditionId == 0 视为不限制；条件不满足的节点直接隐藏（SetActive(false)）。
    /// 每次打开地图面板都会调一次（条件会随任务/道具/对话进度变化）。
    /// </summary>
    public void ApplyConditionVisibility()
    {
        if (nodes == null) return;

        var condSys = ConditionSystem.HasInstance ? ConditionSystem.Instance : null;

        foreach (var entry in nodes)
        {
            if (entry == null || entry.button == null) continue;

            bool visible = entry.conditionId == 0
                           || (condSys != null && condSys.IsConditionMet(entry.conditionId));

            entry.button.gameObject.SetActive(visible);
        }
    }

    public int GetConditionId(SceneMapId mapId)
    {
        if (nodes == null) return 0;

        foreach (var entry in nodes)
        {
            if (entry.targetMap == mapId)
                return entry.conditionId;
        }

        return 0;
    }

    public AnimationClip GetTransitionClip(MapNodeId from, MapNodeId to)
    {
        if (transitions == null) return null;

        foreach (var t in transitions)
        {
            if (t.fromNode == from && t.toNode == to)
                return t.forwardClip;
            if (t.fromNode == to && t.toNode == from)
                return t.reverseClip;
        }

        return null;
    }

    public MapNodeId? GetNodeIdByMap(SceneMapId mapId)
    {
        BuildDict();
        if (nodes == null) return null;

        foreach (var entry in nodes)
        {
            if (entry.targetMap == mapId)
                return entry.nodeId;
        }

        return null;
    }

    public Button GetButtonByMap(SceneMapId mapId)
    {
        if (nodes == null) return null;

        foreach (var entry in nodes)
        {
            if (entry.targetMap == mapId)
                return entry.button;
        }

        return null;
    }

    public Vector2? GetHeadPosition(SceneMapId currentMap)
    {
        if (nodes == null) return null;

        foreach (var entry in nodes)
        {
            if (entry.targetMap == currentMap)
                return entry.HeadPosition;
        }

        return null;
    }

    public void SetNodeInteractable(SceneMapId mapId, bool interactable)
    {
        if (nodes == null) return;

        foreach (var entry in nodes)
        {
            if (entry.targetMap == mapId && entry.button != null)
            {
                entry.button.interactable = interactable;
            }
        }
    }
}
