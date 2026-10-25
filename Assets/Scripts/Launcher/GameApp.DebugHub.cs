using UnityEngine;

public partial class GameApp
{
    #region Debug Hub Inspector

    [Header("调试中枢")]
    [Tooltip("总开关：开启后显示统一调试窗口")]
    [SerializeField] private bool debugEnableHub = true;
    [Tooltip("F1 切换显示/隐藏")]
    [SerializeField] private bool debugHubHotkey = true;
    [Tooltip("条件调试 Tab（实时状态 + 覆盖标记 + 覆盖操作）")]
    [SerializeField] private bool debugEnableConditionTest = true;

    [Tooltip("Buff 调试 Tab（列出 buff 表，添加/移除 + 弹获得提示）")]
    [SerializeField] private bool debugEnableBuffTest = true;

    #endregion

    #region Debug Hub State

    private enum DebugHubTab
    {
        Dialog = 0,
        Animation = 1,
        Bag = 2,
        Battle = 3,
        Condition = 4,
        Buff = 5
    }

    private static readonly string[] HubTabNames = { "对话", "动画", "背包", "战斗", "条件", "Buff" };

    private DebugHubTab _hubTab = DebugHubTab.Dialog;
    private bool _hubMinimized;
    private Rect _hubRect = new Rect(-1, -1, 560, 520);
    private readonly Vector2 _hubDefaultSize = new Vector2(560, 520);
    private bool _hubResizing;
    private Vector2 _hubResizeStart;
    private Rect _hubResizeStartRect;
    private Texture2D _hubBgTex;
    private Texture2D _hubBtnTex;
    private Texture2D _hubBtnHoverTex;
    private Texture2D _hubTabOnTex;
    private Texture2D _hubTabOffTex;
    private Vector2 _hubCondScroll;
    private string _hubCondSearch = "";

    #endregion

    private void DrawDebugHub()
    {
        if (!debugEnableHub) return;
        if (!debugEnableDialogTest && !debugEnableAnimationTest
            && !debugEnableBagTest && !debugEnableBattleTest && !debugEnableConditionTest && !debugEnableBuffTest)
            return;

        if (debugHubHotkey && Event.current.type == EventType.KeyDown
            && Event.current.keyCode == KeyCode.F1)
        {
            _hubMinimized = !_hubMinimized;
            Event.current.Use();
        }

        EnsureHubStyles();

        if (_hubMinimized)
        {
            if (GUI.Button(new Rect(10, 10, 140, 40), "调试中枢 [F1]", HubBtnStyle(20)))
                _hubMinimized = false;
            return;
        }

        if (_hubRect.x < 0)
            _hubRect = new Rect(10, 10, _hubDefaultSize.x, _hubDefaultSize.y);

        _hubRect.width = Mathf.Clamp(_hubRect.width, 420, Screen.width);
        _hubRect.height = Mathf.Clamp(_hubRect.height, 320, Screen.height);
        _hubRect = GUI.Window(90001, _hubRect, DrawHubWindow, "", GUIStyle.none);
        HandleHubResize();
    }

    private void DrawHubWindow(int id)
    {
        var a = new Rect(0, 0, _hubRect.width, _hubRect.height);
        GUI.DrawTexture(a, _hubBgTex);

        float sw = _hubRect.width / _hubDefaultSize.x;
        float sh = _hubRect.height / _hubDefaultSize.y;
        float s = Mathf.Clamp(Mathf.Min(sw, sh), 0.55f, 2.5f);

        int fs = Mathf.RoundToInt(18 * s);
        int fsBtn = Mathf.RoundToInt(16 * s);
        int fsTitle = Mathf.RoundToInt(20 * s);

        var lbl = new GUIStyle(GUI.skin.label)
        {
            richText = true,
            fontSize = fs,
            wordWrap = true,
            normal = { textColor = Color.white }
        };
        var btn = HubBtnStyle(fsBtn);
        var fld = new GUIStyle(GUI.skin.textField) { fontSize = fs };
        var title = new GUIStyle(lbl) { fontSize = fsTitle, fontStyle = FontStyle.Bold };

        float pad = 8 * s;
        float titleH = 28 * s;
        float tabH = 32 * s;

        // 标题栏
        GUILayout.BeginArea(new Rect(pad, pad * 0.5f, a.width - pad * 2, titleH));
        GUILayout.BeginHorizontal();
        GUILayout.Label("调试中枢", title);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("收起", btn, GUILayout.Width(70 * s), GUILayout.Height(titleH - 4)))
            _hubMinimized = true;
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        // Tab 栏
        GUILayout.BeginArea(new Rect(pad, titleH + pad, a.width - pad * 2, tabH));
        GUILayout.BeginHorizontal();
        DrawHubTab(DebugHubTab.Dialog, debugEnableDialogTest, btn, s);
        DrawHubTab(DebugHubTab.Animation, debugEnableAnimationTest, btn, s);
        DrawHubTab(DebugHubTab.Bag, debugEnableBagTest, btn, s);
        DrawHubTab(DebugHubTab.Battle, debugEnableBattleTest, btn, s);
        DrawHubTab(DebugHubTab.Condition, debugEnableConditionTest, btn, s);
        DrawHubTab(DebugHubTab.Buff, debugEnableBuffTest, btn, s);
        GUILayout.EndHorizontal();
        GUILayout.EndArea();

        // 内容区
        float contentY = titleH + tabH + pad * 2;
        float contentH = a.height - contentY - pad - 16 * s;
        GUILayout.BeginArea(new Rect(pad, contentY, a.width - pad * 2, contentH));
        switch (_hubTab)
        {
            case DebugHubTab.Dialog:
                if (debugEnableDialogTest) DrawDialogTestContent(lbl, btn, fld, s);
                else GUILayout.Label("对话调试未启用（Inspector 勾选）", lbl);
                break;
            case DebugHubTab.Animation:
                if (debugEnableAnimationTest) DrawAnimationTestContent(lbl, btn, fld, s);
                else GUILayout.Label("动画调试未启用（Inspector 勾选）", lbl);
                break;
            case DebugHubTab.Bag:
                if (debugEnableBagTest) DrawBagTestContent(lbl, btn, fld, s);
                else GUILayout.Label("背包调试未启用（Inspector 勾选）", lbl);
                break;
            case DebugHubTab.Battle:
                if (debugEnableBattleTest) DrawBattleTestContent(lbl, btn, fld, s);
                else GUILayout.Label("战斗调试未启用（Inspector 勾选）", lbl);
                break;
            case DebugHubTab.Condition:
                if (debugEnableConditionTest) DrawConditionTestContent(lbl, btn, fld, s);
                else GUILayout.Label("条件调试未启用（Inspector 勾选）", lbl);
                break;
            case DebugHubTab.Buff:
                if (debugEnableBuffTest) DrawBuffTestContent(lbl, btn, fld, s);
                else GUILayout.Label("Buff 调试未启用（Inspector 勾选）", lbl);
                break;
        }
        GUILayout.EndArea();

        // 拖动 + 缩放
        GUI.DragWindow(new Rect(0, 0, a.width - 50 * s, titleH + pad));
        var hr = new Rect(a.width - 16 * s, a.height - 16 * s, 16 * s, 16 * s);
        GUI.Box(hr, "◢");
        if (Event.current.type == EventType.MouseDown && hr.Contains(Event.current.mousePosition))
        {
            _hubResizing = true;
            _hubResizeStart = Event.current.mousePosition;
            _hubResizeStartRect = _hubRect;
            Event.current.Use();
        }
        else if (Event.current.type == EventType.MouseUp)
        {
            _hubResizing = false;
        }
    }

    private string _hubBuffSearch = "";
    private Vector2 _hubBuffScroll;

    /// <summary>Buff 调试：列出 buff 表全部条目，可添加/移除，也可"添加并弹获得提示"（顺便验证 SceneTip 的 Content_2y）。</summary>
    private void DrawBuffTestContent(GUIStyle lbl, GUIStyle btn, GUIStyle fld, float s)
    {
        if (!ManagerRegistry.TryGet<DataTableManager>(out DataTableManager dtm) || dtm.Tables == null)
        {
            GUILayout.Label("数据表未就绪", lbl);
            return;
        }

        BuffManager buffMgr = BuffManager.Instance;
        var tb = dtm.Tables.TbBuff;

        GUILayout.BeginHorizontal();
        GUILayout.Label("筛选:", lbl, GUILayout.Width(40 * s));
        _hubBuffSearch = GUILayout.TextField(_hubBuffSearch ?? "", fld, GUILayout.Width(150 * s));
        GUILayout.Label("活跃 " + buffMgr.ActiveBuffs.Count + " 个", lbl);
        if (GUILayout.Button("清空全部", btn, GUILayout.Width(80 * s))) buffMgr.Clear();
        GUILayout.EndHorizontal();

        _hubBuffScroll = GUILayout.BeginScrollView(_hubBuffScroll);
        foreach (var row in tb.DataList)
        {
            string label = "[" + row.Id + "] " + row.Name
                + "   type=" + row.Type + " show=" + row.Showtype
                + " 回合=" + row.Time + " 次数=" + row.Num;
            if (!string.IsNullOrEmpty(_hubBuffSearch)
                && label.IndexOf(_hubBuffSearch, System.StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            bool has = buffMgr.HasBuff(row.Id);

            GUILayout.BeginHorizontal();
            GUILayout.Label((has ? "● " : "○ ") + label, lbl);
            if (has)
            {
                if (GUILayout.Button("移除", btn, GUILayout.Width(56 * s))) buffMgr.RemoveBuff(row.Id);
            }
            else
            {
                if (GUILayout.Button("添加", btn, GUILayout.Width(56 * s))) buffMgr.ApplyBuff(row.Id);
            }
            if (GUILayout.Button("添加+提示", btn, GUILayout.Width(92 * s)))
            {
                buffMgr.ApplyBuff(row.Id);
                SceneTip.ShowBuffGlobal(row.Id);
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();
    }

    /// <summary>条件调试：实时列出全部条件（状态按 IsConditionMet 现算，覆盖用橙色标记），可直接覆盖/清覆盖。</summary>
    private void DrawConditionTestContent(GUIStyle lbl, GUIStyle btn, GUIStyle fld, float s)
    {
        var cs = ConditionSystem.HasInstance ? ConditionSystem.Instance : null;
        if (cs == null)
        {
            GUILayout.Label("ConditionSystem 尚未创建（未开始游戏）", lbl);
            return;
        }

        var ids = cs.GetAllConditionIds();
        int metCount = 0, ovCount = 0;
        for (int i = 0; i < ids.Count; i++)
        {
            if (cs.IsConditionMet(ids[i])) metCount++;
            if (cs.HasOverride(ids[i])) ovCount++;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label($"共 {ids.Count} 条 | <color=#7CFC7C>已满足 {metCount}</color> | <color=#FFD24D>覆盖中 {ovCount}</color>", lbl);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("全部清覆盖", btn, GUILayout.Width(110 * s)))
        {
            for (int i = 0; i < ids.Count; i++)
                if (cs.HasOverride(ids[i])) cs.ClearOverride(ids[i]);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("筛选(ID)", lbl, GUILayout.Width(80 * s));
        _hubCondSearch = GUILayout.TextField(_hubCondSearch, fld, GUILayout.Width(90 * s));
        GUILayout.Label("（状态每帧实时计算，无需刷新）", lbl);
        GUILayout.EndHorizontal();

        _hubCondScroll = GUILayout.BeginScrollView(_hubCondScroll);

        for (int i = 0; i < ids.Count; i++)
        {
            int id = ids[i];
            if (!string.IsNullOrEmpty(_hubCondSearch) && !id.ToString().Contains(_hubCondSearch))
                continue;

            bool met = cs.IsConditionMet(id);
            bool ov = cs.HasOverride(id);

            GUILayout.BeginHorizontal();
            string state = met ? "<color=#7CFC7C><b>已满足</b></color>" : "<color=#FF8080><b>未满足</b></color>";
            string tag = ov ? " <color=#FFD24D>[覆盖]</color>" : "";
            GUILayout.Label($"<b>{id}</b>  {state}{tag}  {cs.GetConditionBrief(id)}", lbl);

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("满足", btn, GUILayout.Width(56 * s)))
                cs.ForceSetCondition(id, true);
            if (GUILayout.Button("未满足", btn, GUILayout.Width(66 * s)))
                cs.ForceSetCondition(id, false);
            if (ov)
            {
                if (GUILayout.Button("清覆盖", btn, GUILayout.Width(76 * s)))
                    cs.ClearOverride(id);
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();
    }

    private void DrawHubTab(DebugHubTab tab, bool enabled, GUIStyle btn, float s)
    {
        string name = HubTabNames[(int)tab];
        if (!enabled) name += "·";
        bool on = _hubTab == tab;
        var style = new GUIStyle(btn)
        {
            fontStyle = on ? FontStyle.Bold : FontStyle.Normal,
            normal =
            {
                textColor = on ? Color.white : new Color(0.7f, 0.7f, 0.75f),
                background = on ? _hubTabOnTex : _hubTabOffTex
            },
            hover =
            {
                textColor = Color.white,
                background = on ? _hubTabOnTex : _hubBtnHoverTex
            }
        };
        if (GUILayout.Button(name, style, GUILayout.Height(28 * s)))
            _hubTab = tab;
    }

    private void HandleHubResize()
    {
        if (!_hubResizing) return;
        if (Event.current.type != EventType.MouseDrag) return;
        var e = Event.current;
        _hubRect.width = _hubResizeStartRect.width + e.mousePosition.x - _hubResizeStart.x;
        _hubRect.height = _hubResizeStartRect.height + e.mousePosition.y - _hubResizeStart.y;
        e.Use();
    }

    private void EnsureHubStyles()
    {
        if (_hubBgTex != null) return;
        _hubBgTex = MakeHubTex(new Color(0.08f, 0.08f, 0.12f, 0.94f));
        _hubBtnTex = MakeHubTex(new Color(0.25f, 0.25f, 0.35f, 0.95f));
        _hubBtnHoverTex = MakeHubTex(new Color(0.35f, 0.35f, 0.45f, 0.95f));
        _hubTabOnTex = MakeHubTex(new Color(0.3f, 0.4f, 0.55f, 0.98f));
        _hubTabOffTex = MakeHubTex(new Color(0.18f, 0.18f, 0.24f, 0.95f));
    }

    private GUIStyle HubBtnStyle(int fontSize)
    {
        return new GUIStyle(GUI.skin.button)
        {
            fontSize = fontSize,
            normal = { textColor = Color.white, background = _hubBtnTex },
            hover = { textColor = Color.white, background = _hubBtnHoverTex },
            active = { textColor = Color.white, background = _hubTabOnTex }
        };
    }

    private static Texture2D MakeHubTex(Color c)
    {
        var t = new Texture2D(1, 1);
        t.SetPixel(0, 0, c);
        t.Apply();
        return t;
    }

    /// <summary>自动切到首个已启用的 Tab（启动时调用可选）</summary>
    private void SelectFirstEnabledHubTab()
    {
        if (debugEnableDialogTest) { _hubTab = DebugHubTab.Dialog; return; }
        if (debugEnableAnimationTest) { _hubTab = DebugHubTab.Animation; return; }
        if (debugEnableBagTest) { _hubTab = DebugHubTab.Bag; return; }
        if (debugEnableBattleTest) { _hubTab = DebugHubTab.Battle; }
    }
}
