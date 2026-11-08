using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 战斗日志 cell 类型：左（敌方/普通记录）、中（回合/结果提示）、右（玩家行动）
/// </summary>
public enum BattleLogCellType
{
    Left,
    Middle,
    Right,
}

/// <summary>
/// 战斗记录面板：ScrollView 显示，保留全部历史，红色高亮伤害数值，最新日志在底部并自动滚动可见。
/// 结构优先使用场景中静态搭好的 ScrollRect（LogPanel 下已挂 ScrollRect 并指向 Viewport/Content），缺失时动态创建兜底。
/// 日志条目支持左/中/右三种气泡 cell（样式同对话面板 duihuaSV），背景随文本高度自适应。
/// </summary>
public class BattleLogPanel : MonoBehaviour
{
    [Header("配置")]
    [Tooltip("左侧气泡模板（敌方/普通记录，箭头指向左）")]
    [SerializeField] private GameObject logLeftTemplate;

    [Tooltip("中间气泡模板（回合/结果提示，圆形气泡）")]
    [SerializeField] private GameObject logMiddleTemplate;

    [Tooltip("右侧气泡模板（玩家行动，箭头指向右）")]
    [SerializeField] private GameObject logRightTemplate;

    [Tooltip("日志容器（留空则自动查找/创建 ScrollRect 的 Content）")]
    [SerializeField] private Transform logContainer;

    [Header("颜色")]
    [SerializeField] private Color damageColor = Color.red;
    [SerializeField] private Color healColor = Color.green;
    [SerializeField] private Color normalColor = Color.black;

    private readonly List<TextMeshProUGUI> _logEntries = new List<TextMeshProUGUI>();
    private ScrollRect _scrollRect;

    private void Awake()
    {
        SetupScrollView();
        if (logLeftTemplate != null) logLeftTemplate.SetActive(false);
        if (logMiddleTemplate != null) logMiddleTemplate.SetActive(false);
        if (logRightTemplate != null) logRightTemplate.SetActive(false);
    }

    /// <summary>
    /// 初始化 ScrollRect：优先使用场景中静态搭好的 Viewport/Content，缺失时动态创建兜底
    /// </summary>
    private void SetupScrollView()
    {
        _scrollRect = GetComponent<ScrollRect>();
        if (_scrollRect == null) _scrollRect = gameObject.AddComponent<ScrollRect>();

        // 静态结构已就绪（LogPanel 下已搭好 Viewport + Content）
        if (_scrollRect.viewport != null && _scrollRect.content != null)
        {
            logContainer = _scrollRect.content;
            _scrollRect.horizontal = false;
            _scrollRect.vertical = true;
            _scrollRect.movementType = ScrollRect.MovementType.Clamped;
            _scrollRect.scrollSensitivity = 20f;
            return;
        }

        // 兜底：动态创建 ScrollRect 结构（兼容未搭建静态结构的场景）
        var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
        viewportGo.transform.SetParent(transform, false);
        var viewportRect = viewportGo.GetComponent<RectTransform>();
        viewportRect.anchorMin = Vector2.zero;
        viewportRect.anchorMax = Vector2.one;
        viewportRect.offsetMin = Vector2.zero;
        viewportRect.offsetMax = Vector2.zero;

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        var contentRect = contentGo.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0, 1);
        contentRect.anchorMax = new Vector2(1, 1);
        contentRect.pivot = new Vector2(0.5f, 1);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = new Vector2(0, 0);

        var layout = contentGo.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.spacing = 4f;

        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _scrollRect.viewport = viewportRect;
        _scrollRect.content = contentRect;
        _scrollRect.horizontal = false;
        _scrollRect.vertical = true;
        _scrollRect.movementType = ScrollRect.MovementType.Clamped;
        _scrollRect.scrollSensitivity = 20f;

        logContainer = contentRect;
    }

    /// <summary>
    /// 添加一条战斗记录（默认左侧气泡）
    /// </summary>
    public void AddLog(string message)
    {
        AddLogInternal(message, normalColor, BattleLogCellType.Left);
    }

    /// <summary>
    /// 添加一条战斗记录（指定气泡类型）
    /// </summary>
    public void AddLog(string message, BattleLogCellType cell)
    {
        AddLogInternal(message, normalColor, cell);
    }

    /// <summary>
    /// 添加玩家行动记录（右侧气泡）
    /// </summary>
    public void AddPlayerLog(string message)
    {
        AddLogInternal(message, normalColor, BattleLogCellType.Right);
    }

    /// <summary>
    /// 添加伤害记录（红色高亮数值，左侧气泡）
    /// </summary>
    public void AddDamageLog(string attackerName, string targetName, int damage, string skillName = null)
    {
        string msg;
        if (!string.IsNullOrEmpty(skillName))
            msg = $"{attackerName} 使用 {skillName} 对 {targetName} 造成 <color=#{ColorToHex(damageColor)}>{damage}点伤害</color>";
        else
            msg = $"{attackerName} 对 {targetName} 造成 <color=#{ColorToHex(damageColor)}>{damage}点伤害</color>";
        AddLogInternal(msg, normalColor, BattleLogCellType.Left);
    }

    /// <summary>
    /// 添加治疗记录（左侧气泡）
    /// </summary>
    public void AddHealLog(string healerName, string targetName, int amount)
    {
        string msg = $"{healerName} 治疗 {targetName} <color=#{ColorToHex(healColor)}>{amount}点生命</color>";
        AddLogInternal(msg, normalColor, BattleLogCellType.Left);
    }

    /// <summary>
    /// 添加阵亡记录（左侧气泡）
    /// </summary>
    public void AddDeathLog(string unitName)
    {
        AddLogInternal($"<color=#{ColorToHex(damageColor)}>{unitName} 倒下了！</color>", normalColor, BattleLogCellType.Left);
    }

    /// <summary>
    /// 添加阵亡记录（指定气泡类型）
    /// </summary>
    public void AddDeathLog(string unitName, BattleLogCellType cell)
    {
        AddLogInternal($"<color=#{ColorToHex(damageColor)}>{unitName} 倒下了！</color>", normalColor, cell);
    }

    /// <summary>
    /// 添加回合开始记录（中间气泡）
    /// </summary>
    public void AddRoundLog(int round)
    {
        AddLogInternal($"—— 第{round}轮 ——", new Color(0.5f, 0.5f, 0.5f), BattleLogCellType.Middle);
    }

    private void AddLogInternal(string message, Color color, BattleLogCellType cell)
    {
        if (logContainer == null) return;

        TextMeshProUGUI entry;
        var template = cell == BattleLogCellType.Right ? logRightTemplate
            : cell == BattleLogCellType.Middle ? logMiddleTemplate
            : logLeftTemplate;
        if (template == null) template = logLeftTemplate ?? logMiddleTemplate ?? logRightTemplate;

        // 记录加日志前的滚动位置：只有本来就贴底才自动跟随，否则会把正在翻历史的玩家拽回底部
        bool wasAtBottom = _scrollRect == null || _scrollRect.verticalNormalizedPosition <= 0.05f;

        if (template != null)
        {
            var go = Instantiate(template, logContainer);
            go.SetActive(true);
            entry = go.GetComponentInChildren<TextMeshProUGUI>();
        }
        else
        {
            var go = CreateLogItem($"Log_{_logEntries.Count}");
            entry = go.GetComponentInChildren<TextMeshProUGUI>();
        }
        if (entry == null)
        {
            Debug.LogWarning("[BattleLogPanel] 日志条目模板缺少 TextMeshProUGUI 子节点");
            return;
        }

        entry.text = message;
        entry.color = color;
        _logEntries.Add(entry);

        // 只在原本贴底时跟随到底部；玩家往上翻时不打断
        if (wasAtBottom) ScrollToBottom();
    }

    /// <summary>
    /// 动态创建简单日志条目兜底（无气泡背景的纯文本，高度随文本自适应）
    /// </summary>
    private GameObject CreateLogItem(string name)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        go.transform.SetParent(logContainer, false);

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(1, 1);
        rect.pivot = new Vector2(0.5f, 1);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0, 30);

        var layout = go.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 4, 4);
        layout.spacing = 0f;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);

        var entry = textGo.GetComponent<TextMeshProUGUI>();
        entry.fontSize = 14;
        entry.alignment = TextAlignmentOptions.Left;
        entry.richText = true;
        entry.enableWordWrapping = true;
        entry.raycastTarget = false;

        return go;
    }

    private void ScrollToBottom()
    {
        StartCoroutine(ScrollToBottomRoutine());
    }

    private System.Collections.IEnumerator ScrollToBottomRoutine()
    {
        yield return null; // 等一帧让布局生效
        if (_scrollRect == null) yield break;
        Canvas.ForceUpdateCanvases();
        _scrollRect.verticalNormalizedPosition = 0f; // 0=底部
    }

    /// <summary>
    /// 清空所有记录
    /// </summary>
    public void ClearLogs()
    {
        foreach (var entry in _logEntries)
        {
            if (entry != null) Destroy(entry.gameObject);
        }
        _logEntries.Clear();
    }

    private string ColorToHex(Color color)
    {
        return $"{(int)(color.r * 255):X2}{(int)(color.g * 255):X2}{(int)(color.b * 255):X2}";
    }
}
