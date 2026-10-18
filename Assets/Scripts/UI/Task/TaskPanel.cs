using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public class TaskPanel : UIWindow
{
    public override string ExclusiveGroup => "MainPanels";   // 背包与任务面板互斥，同时只显示一个

    [SerializeField] private GameObject itemCell;
    [SerializeField] private GameObject cellContent;
    [SerializeField] private ScrollRect sv;

    [Header("地图节点")]
    [SerializeField] private Button btnMidMap1;
    [SerializeField] private Button btnMidMap2;
    [SerializeField] private Button btnMidMap3;

    [SerializeField] private Button btnClose;

    private readonly List<GameObject> _itemInstances = new List<GameObject>();

    public override void OnCreate(object args)
    {
        btnClose.onClick.AddListener(Close);

        if (btnMidMap3 != null)
            btnMidMap3.onClick.AddListener(() => OpenMapPanel(MapRootId.PunkCity));
    }

    private void OpenMapPanel(MapRootId id)
    {
        ManagerRegistry.Get<UIManager>()?.Open<MapPanel>(id);
    }

    public override void OnOpen(object args)
    {
        RebuildItems();
        QuestManager.Instance.OnQuestNodeChanged += OnQuestChanged;
        QuestManager.Instance.OnQuestCompleted += OnQuestChanged;
        QuestManager.Instance.OnQuestFailed += OnQuestChanged;
    }

    public override void OnClose()
    {
        QuestManager.Instance.OnQuestNodeChanged -= OnQuestChanged;
        QuestManager.Instance.OnQuestCompleted -= OnQuestChanged;
        QuestManager.Instance.OnQuestFailed -= OnQuestChanged;
        ClearItems();
    }

    private void OnQuestChanged(QuestInstance _)
    {
        RebuildItems();
    }

    private void RebuildItems()
    {
        ClearItems();

        var quests = QuestManager.Instance.AllQuests.Values
            .OrderBy(q => q.State == QuestState.Completed || q.State == QuestState.Failed)
            .ThenByDescending(q => q.State == QuestState.InProgress)
            .ThenByDescending(q => q.State == QuestState.NotAccepted)
            .ToList();

        int index = 1;
        foreach (var quest in quests)
        {
            var go = Instantiate(itemCell, sv.content, false);
            go.SetActive(true);
            var item = go.GetComponent<TaskItem>();
            if (item != null)
                item.Setup(quest, index++, cellContent);
            _itemInstances.Add(go);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(sv.content);
        sv.verticalNormalizedPosition = 1f;
    }

    private void ClearItems()
    {
        foreach (var go in _itemInstances)
        {
            if (go != null) Destroy(go);
        }
        _itemInstances.Clear();
    }

    private void Close()
    {
        GameplayState.CloseTaskIfOpen();
    }
}
