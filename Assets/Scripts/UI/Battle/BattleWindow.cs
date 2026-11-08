using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 战斗窗口：下半部分UI面板
/// 左侧4个操作按钮 + 中间画框 + 右侧战斗记录/子面板 + 右上EXIT
/// </summary>
public class BattleWindow : UIWindow
{
    public override UILayer Layer => UILayer.Top;

    #region 序列化字段

    [Header("底部面板")]
    [SerializeField] private RectTransform bottomPanel;

    [Header("左侧操作按钮")]
    [SerializeField] private Toggle btnViolence;    // 暴力行为 → 技能列表
    [SerializeField] private Toggle btnTalk;        // 咄咄逼人 → 对话选择
    [SerializeField] private Toggle btnItem;        // 翻找工具 → 物品列表
    [SerializeField] private Toggle btnIdle;        // 正在发呆 → 跳过回合

    [Header("右上角按钮")]
    [SerializeField] private Button btnRun;         // common btn：逃跑按钮 → 战斗按失败结算
    [SerializeField] private Button btnSpeed;       // 二倍速切换按钮
    [SerializeField] private TextMeshProUGUI btnSpeedText; // 显示当前速度 1x/2x

    [Header("右侧内容区")]
    [SerializeField] private BattleLogPanel logPanel;          // 战斗记录
    [SerializeField] private GameObject subPanelContainer;      // 子面板容器（覆盖在记录上）

    [Header("子面板")]
    [SerializeField] private BattleSkillPanel skillPanel;
    [SerializeField] private BattleItemPanel itemPanel;
    [SerializeField] private BattleTalkPanel talkPanel;

    [Header("按钮/技能描述")]
    [Tooltip("底部按钮或技能悬浮时显示描述的文字")]
    [SerializeField] private TextMeshProUGUI btnDetailText;

    [Header("技能点指示")]
    [Tooltip("技能点根容器（子节点 movepoint1~4，每个含 icon=可用图标 / cha=已使用遮罩）")]
    [SerializeField] private RectTransform movePointRoot;

    #endregion

    #region 按钮描述常量

    private const string DetailViolence = "暴力行为：打！往死里整，别停！";
    private const string DetailTalk = "咄咄逼人：操你妈，快！呈口舌之快！";
    private const string DetailItem = "使用物品：又嗑又砸！血条涨，对面躺！";
    private const string DetailIdle = "原地发呆：老子不动，看谁敢先动手！";
    private const string DetailRun = "逃跑：打不过就跑，认怂保命！";

    #endregion

    #region 运行时

    private BattleManager _battleManager;
    private BattleStageManager _stageManager;
    private bool _isSelectingTarget;
    private int _selectedSkillId;
    private int _selectedItemId;
    private Camera _mainCamera;
    private BattleUnit _currentCursorTarget;

    /// <summary> 本次行动使用的技能 id（0=普通攻击），供伤害日志取 skill.skilllog </summary>
    private int _currentSkillId;

    // ── 一次行动内的日志合并：多个受击者/多个阵亡合成一句，避免刷屏 ──
    private readonly List<string> _pendingTargetNames = new List<string>();
    private readonly List<string> _pendingDeathNames = new List<string>();
    private BattleUnit _pendingActor;
    private int _pendingDamageTotal;
    private BattleLogCellType _pendingDamageCell = BattleLogCellType.Left;
    private Coroutine _pendingLogCoroutine;

    /// <summary> 目标选择开始时刻（用于忽略选技能那一下的点击，避免误释放） </summary>
    private float _targetSelectStartTime;

    // 提示弹窗（技能无法使用时居中弹出，自动消失）
    private GameObject _toastGo;
    private TextMeshProUGUI _toastText;
    private Coroutine _toastCoroutine;

    // 当前打开子面板的功能描述（btnDetail 常驻显示；悬浮其它功能临时覆盖，移出后恢复）
    private string _currentPanelDetail;

    // ---- 事件推送缓存（严格 MVC：只由事件更新，不主动读取战斗状态）----
    private BattleUnit _currentUnit;
    private bool _isWaitingForPlayerAction;
    private List<BattleUnit> _playerUnits = new List<BattleUnit>();
    private List<BattleUnit> _enemyUnits = new List<BattleUnit>();

    /// <summary> 技能点槽缓存：movepoint1~4 的槽位节点 + 各自的 cha(已消耗遮罩) </summary>
    private readonly List<GameObject> _movePointSlots = new List<GameObject>();
    private readonly List<GameObject> _movePointChas = new List<GameObject>();
    private bool _movePointCached;

    #endregion

    #region 生命周期

    /// <summary>
    /// 窗口创建：绑定四个操作按钮（Toggle 点击触发对应操作，执行后自动复位）
    /// </summary>
    public override void OnCreate(object args)
    {
        // 四个操作按钮：onValueChanged=true 时执行操作，并复位为未选中
        BindActionToggle(btnViolence, DetailViolence, OnViolenceClicked);
        BindActionToggle(btnTalk, DetailTalk, OnTalkClicked);
        BindActionToggle(btnItem, DetailItem, OnItemClicked);
        BindActionToggle(btnIdle, DetailIdle, OnIdleClicked);

        // 右上角逃跑按钮（点击即逃跑，战斗按失败结算）
        if (btnRun != null) btnRun.onClick.AddListener(OnRunClicked);
        BindHoverDetail(btnRun, DetailRun);

        // 二倍速切换按钮
        if (btnSpeed != null) btnSpeed.onClick.AddListener(OnSpeedClicked);

        CloseAllSubPanels();

        _mainCamera = Camera.main;
    }

    /// <summary>
    /// 窗口打开：获取 BattleManager/StageManager、订阅事件、刷新UI；若已在等待玩家操作则立即设置默认选中目标
    /// </summary>
    public override void OnOpen(object args)
    {
        _battleManager = GameApp.Instance.BattleManager;
        _stageManager = BattleStageManager.Instance;

        if (_battleManager == null)
        {
            Debug.LogError("[BattleWindow] BattleManager 为空");
            return;
        }

        SubscribeEvents();
        SetActionButtonsVisible(false);
        CloseAllSubPanels();

        if (logPanel != null) logPanel.ClearLogs();

        RefreshSpeedButton();

        // 严格 MVC：请求管理器推送当前状态快照（由事件回调更新缓存，不主动读取）
        // 若战斗已处于玩家等待，OnPlayerActionWaitChanged(true) 回调会自动显示按钮并选中目标
        _battleManager.PushStateToUI();
    }

    /// <summary>
    /// 窗口关闭：退订事件、清空管理器引用
    /// </summary>
    public override void OnClose()
    {
        UnsubscribeEvents();
        _battleManager = null;
        _stageManager = null;
    }

    private void Update()
    {
        // 目标选择模式：先选技能后，点击/方向键选目标，回车/空格确认，Esc 取消
        if (_isSelectingTarget)
        {
            // 目标选择刚开始的一小段时间内忽略点击（避免"点击选中技能"这一下的 up 被误判为选目标释放）
            bool clickAllowed = Time.unscaledTime - _targetSelectStartTime >= 0.1f;

            // 鼠标抬起选择（抬起时 EventSystem 的 hover 状态已稳定，能准确区分点 UI vs 点场景）
            if (clickAllowed && Input.GetMouseButtonUp(0) && !IsPointerOverUI())
            {
                TrySelectTargetByClick();
            }
            // 键盘游标切换目标（方向键/WASD：在有效目标间循环切换）
            else if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D))
            {
                CycleTarget(1, 0);
            }
            else if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A))
            {
                CycleTarget(-1, 0);
            }
            else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
            {
                CycleTarget(0, -1);
            }
            else if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
            {
                CycleTarget(0, 1);
            }
            // 回车/空格：确认当前选中目标
            else if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
            {
                ConfirmCurrentTarget();
            }
            // Esc/右键：取消，返回技能面板
            else if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                CancelTargetSelection();
                if (logPanel != null)
                    logPanel.AddLog("已取消目标选择");
                OnViolenceClicked();
            }
            return;
        }
    }

    #endregion

    #region 事件订阅

    private void SubscribeEvents()
    {
        if (_battleManager == null) return;
        _battleManager.OnBattleStart += OnBattleStart;
        _battleManager.OnRoundStart += OnRoundStart;
        _battleManager.OnUnitTurnStart += OnUnitTurnStart;
        _battleManager.OnUnitPhaseChange += OnUnitPhaseChange;
        _battleManager.OnDamageTaken += OnDamageTaken;
        _battleManager.OnHealed += OnHealed;
        _battleManager.OnUnitDeath += OnUnitDeath;
        _battleManager.OnUnitTurnEnd += OnUnitTurnEnd;
        _battleManager.OnRoundEnd += OnRoundEnd;
        _battleManager.OnBattleEnd += OnBattleEnd;
        _battleManager.OnBuffApplied += OnBuffApplied;
        _battleManager.OnSkillExecuted += OnSkillExecuted;
        _battleManager.OnCurrentUnitChanged += OnCurrentUnitChanged;
        _battleManager.OnPlayerActionWaitChanged += OnPlayerActionWaitChanged;
        _battleManager.OnUnitListsChanged += OnUnitListsChanged;

        var presenter = BattlePresenter.Instance;
        if (presenter != null)
        {
            presenter.OnSequenceBegin += OnSequenceBegin;
            presenter.OnSequenceEnd += OnSequenceEnd;
        }
    }

    private void UnsubscribeEvents()
    {
        if (_battleManager == null) return;
        _battleManager.OnBattleStart -= OnBattleStart;
        _battleManager.OnRoundStart -= OnRoundStart;
        _battleManager.OnUnitTurnStart -= OnUnitTurnStart;
        _battleManager.OnUnitPhaseChange -= OnUnitPhaseChange;
        _battleManager.OnDamageTaken -= OnDamageTaken;
        _battleManager.OnHealed -= OnHealed;
        _battleManager.OnUnitDeath -= OnUnitDeath;
        _battleManager.OnUnitTurnEnd -= OnUnitTurnEnd;
        _battleManager.OnRoundEnd -= OnRoundEnd;
        _battleManager.OnBattleEnd -= OnBattleEnd;
        _battleManager.OnBuffApplied -= OnBuffApplied;
        _battleManager.OnSkillExecuted -= OnSkillExecuted;
        _battleManager.OnCurrentUnitChanged -= OnCurrentUnitChanged;
        _battleManager.OnPlayerActionWaitChanged -= OnPlayerActionWaitChanged;
        _battleManager.OnUnitListsChanged -= OnUnitListsChanged;

        var presenter = BattlePresenter.Instance;
        if (presenter != null)
        {
            presenter.OnSequenceBegin -= OnSequenceBegin;
            presenter.OnSequenceEnd -= OnSequenceEnd;
        }
    }

    #endregion

    #region 事件回调

    /// <summary> 当前行动单位变化：仅缓存（由管理器推送，UI 不主动读取） </summary>
    private void OnCurrentUnitChanged(BattleUnit unit)
    {
        _currentUnit = unit;
    }

    /// <summary>
    /// 玩家操作等待状态变化：进入等待 → 显示操作按钮并在当前角色显示行动指示器；结束 → 隐藏按钮并清理
    /// </summary>
    private void OnPlayerActionWaitChanged(bool waiting)
    {
        // 缓存等待状态（严格 MVC：CanOperate/Update 门控读取此字段）
        _isWaitingForPlayerAction = waiting;
        if (waiting)
        {
            if (_currentUnit == null) return;
            SetActionButtonsVisible(true);
            RefreshMovePointSlots(_currentUnit);
            // 新流程：进入玩家回合不预选目标，等玩家先选技能再选目标
            _selectedSkillId = 0;
            _currentCursorTarget = null;
            _isSelectingTarget = false;
            if (_stageManager != null)
            {
                _stageManager.ClearTargetSelection();
                // 仅玩家可操作时在当前角色显示行动指示器
                _stageManager.HighlightCurrentUnit(_currentUnit);
            }
        }
        else
        {
            SetActionButtonsVisible(false);
            CloseAllSubPanels();
            CancelTargetSelection();
            HideMovePoints();
            // 执行/不可操作时不显示行动指示器
            if (_stageManager != null)
                _stageManager.ClearHighlight();
        }
    }

    /// <summary> 单位列表变化（增援/死亡）：缓存快照 </summary>
    private void OnUnitListsChanged(List<BattleUnit> players, List<BattleUnit> enemies)
    {
        _playerUnits = new List<BattleUnit>(players);
        _enemyUnits = new List<BattleUnit>(enemies);
    }

    private void OnBattleStart()
    {
        if (logPanel != null) logPanel.AddLog("战斗开始！");
    }

    private void OnRoundStart(int round)
    {
        if (logPanel != null) logPanel.AddRoundLog(round);
    }

    private void OnUnitTurnStart(BattleUnit unit)
    {
        if (_stageManager != null)
        {
            // 清除上一位被选中的目标标签
            // 行动指示器不在此显示：改为仅玩家可操作时（OnPlayerActionWaitChanged(true)）显示
            _stageManager.ClearTargetSelection();
        }
    }

    private void OnUnitPhaseChange(BattleUnit unit, UnitTurnPhase phase)
    {
        if (phase == UnitTurnPhase.Action && unit.IsPlayerControlled)
        {
            SetActionButtonsVisible(true);
            // 新流程：进入玩家回合不预选目标，等玩家先选技能再选目标
            _selectedSkillId = 0;
            _currentCursorTarget = null;
            _isSelectingTarget = false;
            if (_stageManager != null)
                _stageManager.ClearTargetSelection();
        }
    }

    private void OnDamageTaken(BattleUnit actor, BattleUnit unit, int damage)
    {
        // 更新场景血条
        if (_stageManager != null)
            _stageManager.UpdateHealthBar(unit);

        // 不逐条打日志：累计到本次行动结算完，合并成一条（见 FlushActionLog）
        if (actor != null)
        {
            _pendingActor = actor;
            _pendingDamageCell = actor.IsPlayerSide ? BattleLogCellType.Left : BattleLogCellType.Right;
        }
        string targetName = GetUnitName(unit);
        if (!_pendingTargetNames.Contains(targetName)) _pendingTargetNames.Add(targetName);
        _pendingDamageTotal += damage;
        ScheduleActionLogFlush();
    }

    private void OnHealed(BattleUnit actor, BattleUnit unit, int amount)
    {
        if (_stageManager != null)
            _stageManager.UpdateHealthBar(unit);
        if (logPanel != null)
        {
            var cell = actor != null && !actor.IsPlayerSide ? BattleLogCellType.Right : BattleLogCellType.Left;
            if (actor != null && actor != unit)
                logPanel.AddLog($"{GetUnitName(actor)} 为 {GetUnitName(unit)} 恢复 {amount} 点生命", cell);
            else
                logPanel.AddLog($"{GetUnitName(unit)} 恢复 {amount} 点生命", cell);
        }
    }

    /// <summary> 行动记录：玩家/友方行动在左，敌方行动在右；目标具体到每个人 </summary>
    private void OnSkillExecuted(BattleUnit actor, int skillId, List<BattleUnit> targets)
    {
        _currentSkillId = skillId;

        // 新行动开始：清空上一次行动的累计
        _pendingTargetNames.Clear();
        _pendingDeathNames.Clear();
        _pendingDamageTotal = 0;
        _pendingActor = actor;

        if (logPanel == null || actor == null) return;

        // 伤害类技能不在这里播报，等结算完合并成一条（避免"使用XX攻击YY"+"伤害"两条）
        var dmgCfg = skillId > 0 && _battleManager != null ? _battleManager.GetTables().TbSkill.GetOrDefault(skillId) : null;
        if (dmgCfg != null && (dmgCfg.Skilltype == 2 || dmgCfg.Skilltype == 3)) return;

        string actorName = GetUnitName(actor);
        string skillName = "普通攻击";
        if (skillId > 0)
        {
            var skillCfg = _battleManager?.GetTables()?.TbSkill.GetOrDefault(skillId);
            if (skillCfg != null) skillName = skillCfg.Name;
        }

        var cell = actor.IsPlayerSide ? BattleLogCellType.Left : BattleLogCellType.Right;
        if (targets != null && targets.Count > 0)
        {
            var names = new List<string>();
            foreach (var t in targets)
            {
                if (t != null && t.IsAlive)
                    names.Add(GetUnitName(t));
            }
            string targetStr = names.Count > 0 ? string.Join("、", names) : "";
            if (!string.IsNullOrEmpty(targetStr))
                logPanel.AddLog($"{actorName} 使用 {skillName} 攻击 {targetStr}", cell);
            else
                logPanel.AddLog($"{actorName} 使用 {skillName}", cell);
        }
        else
        {
            logPanel.AddLog($"{actorName} 使用 {skillName}", cell);
        }
    }

    private void OnBuffApplied(BattleUnit unit, int buffId, int param)
    {
        // Buff/场地效果提示居中
        if (logPanel != null)
            logPanel.AddLog($"{GetUnitName(unit)} 获得Buff({buffId})", BattleLogCellType.Middle);
    }

    private void OnUnitDeath(BattleUnit killer, BattleUnit unit)
    {
        if (_stageManager != null)
            _stageManager.UpdateHealthBar(unit);
        // 阵亡提示合并成一句（同名只提示一次）
        string deadName = GetUnitName(unit);
        if (!_pendingDeathNames.Contains(deadName)) _pendingDeathNames.Add(deadName);
        ScheduleActionLogFlush();
    }

    /// <summary> 拖延一小会儿再合并输出，等这次行动的所有伤害结算完 </summary>
    private void ScheduleActionLogFlush()
    {
        if (_pendingLogCoroutine != null) StopCoroutine(_pendingLogCoroutine);
        _pendingLogCoroutine = StartCoroutine(FlushActionLogRoutine());
    }

    private System.Collections.IEnumerator FlushActionLogRoutine()
    {
        yield return new WaitForSeconds(0.3f);
        FlushActionLog();
    }

    /// <summary>
    /// 把一次行动内的所有伤害/阵亡合并成 1~2 条日志：
    /// 受击者用"、"连起来放在同一句池文案里，伤害取合计值。
    /// </summary>
    private void FlushActionLog()
    {
        _pendingLogCoroutine = null;
        if (logPanel == null) { ClearPendingLog(); return; }

        var tables = _battleManager != null ? _battleManager.GetTables() : null;

        // ① 伤害：所有受击者合成一句
        if (_pendingTargetNames.Count > 0 && _pendingActor != null)
        {
            string targetStr = string.Join("、", _pendingTargetNames);
            var skillCfg = _currentSkillId > 0 && tables != null ? tables.TbSkill.GetOrDefault(_currentSkillId) : null;
            string effect = BattleLogText.BuildEffect(skillCfg != null ? skillCfg.Skilllog : null, _pendingDamageTotal);

            string pooled = null;
            var actorCfg = tables != null ? tables.TbPerson.GetOrDefault(_pendingActor.PersonId) : null;
            int poolId = actorCfg != null ? actorCfg.BattlepoolId : 0;
            if (tables != null && poolId > 0)
            {
                int total = 0;
                foreach (var r in tables.TbBattlelog.DataList)
                    if (r.LogpoolId == poolId) total++;
                if (total > 0)
                {
                    int pick = Random.Range(0, total);
                    int idx = 0;
                    foreach (var r in tables.TbBattlelog.DataList)
                    {
                        if (r.LogpoolId != poolId) continue;
                        if (idx++ == pick)
                        {
                            pooled = BattleLogText.Format(r.Logcontext, r.LogcontextType,
                                GetUnitName(_pendingActor), targetStr, effect);
                            break;
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(pooled))
                logPanel.AddLog(pooled, _pendingDamageCell);
            else
                logPanel.AddLog($"{GetUnitName(_pendingActor)} 对 {targetStr} 造成 {_pendingDamageTotal} 点伤害", _pendingDamageCell);
        }

        // ② 阵亡：合成一句（中间气泡）
        if (_pendingDeathNames.Count > 0)
            logPanel.AddLog($"<color=#FF0000>{string.Join("、", _pendingDeathNames)} 倒下了！</color>", BattleLogCellType.Middle);

        ClearPendingLog();
    }

    private void ClearPendingLog()
    {
        _pendingTargetNames.Clear();
        _pendingDeathNames.Clear();
        _pendingDamageTotal = 0;
        _pendingActor = null;
    }

    private void OnUnitTurnEnd(BattleUnit unit)
    {
        if (_stageManager != null)
            _stageManager.ClearHighlight();
        SetActionButtonsVisible(false);
        CloseAllSubPanels();
        CancelTargetSelection();
    }

    private void OnRoundEnd(int round) { }

    /// <summary> 演出序列开始：抑制输入（隐藏操作按钮/关闭子面板/取消目标选择） </summary>
    private void OnSequenceBegin()
    {
        SetActionButtonsVisible(false);
        CloseAllSubPanels();
        CancelTargetSelection();
    }

    /// <summary> 演出序列结束（回合推进由 BattleManager 负责） </summary>
    private void OnSequenceEnd() { }

    private void OnBattleEnd(BattleResult result)
    {
        SetActionButtonsVisible(false);
        CloseAllSubPanels();
        CancelTargetSelection();

        if (_stageManager != null)
            _stageManager.ClearHighlight();
        if (_stageManager != null)
            _stageManager.ClearTargetSelection();

        if (logPanel != null)
        {
            switch (result)
            {
                case BattleResult.Win: logPanel.AddLog("战斗胜利！", BattleLogCellType.Middle); break;
                case BattleResult.Lose: logPanel.AddLog("战斗失败...", BattleLogCellType.Middle); break;
                case BattleResult.Flee: logPanel.AddLog("成功逃跑！", BattleLogCellType.Middle); break;
            }
        }

        // 战斗结束：等演出全部结束（技能序列+死亡倒地）再返回原场景，避免截断死亡演出
        StartCoroutine(WaitPerformancesThenReturn());
    }

    /// <summary>
    /// 等待演出收尾：技能序列结束 → 死亡倒地播完 → 飘字淡出，再返回原场景
    /// </summary>
    private System.Collections.IEnumerator WaitPerformancesThenReturn()
    {
        var presenter = BattlePresenter.Instance;
        if (presenter != null)
        {
            // 等待演出收尾，但都要有上限：演出被打断时不能卡死在结算界面
            float guard = 0f;
            while (presenter.IsPlaying && guard < 8f)
            {
                guard += Time.unscaledDeltaTime;
                yield return null;
            }
            if (presenter.IsPlaying)
                Debug.LogWarning("[BattleWindow] 等待技能演出结束超时(8s)，强制进入结算");

            guard = 0f;
            while (presenter.IsDeathPlaying && guard < 5f)
            {
                guard += Time.unscaledDeltaTime;
                yield return null;
            }
            if (presenter.IsDeathPlaying)
                Debug.LogWarning("[BattleWindow] 等待死亡演出结束超时(5s)，强制返回");
        }
        // 再等半拍让飘字自然淡出
        yield return new WaitForSeconds(0.5f);

        var gameApp = GameApp.Instance;
        if (gameApp != null)
            gameApp.OnBattleResultConfirmed();
    }

    #endregion

    #region 画框立绘

    /// <summary>
    /// 通过 Person 表获取单位名称
    /// </summary>
    private string GetUnitName(BattleUnit unit)
    {
        if (unit == null) return "?";
        var tables = _battleManager?.GetTables();
        var person = tables?.TbPerson.GetOrDefault(unit.PersonId);
        return person != null ? person.Name : $"P{unit.PersonId}";
    }

    #endregion

    #region 技能点指示（movepoint）

    /// <summary> 技能点槽最多四个 </summary>
    private const int MovePointCount = 4;

    /// <summary> 懒缓存 movepoint1~4 槽位节点 + 各自 cha 子节点 </summary>
    private void CacheMovePoints()
    {
        if (_movePointCached) return;
        _movePointCached = true;

        _movePointSlots.Clear();
        _movePointChas.Clear();

        Transform root = movePointRoot != null ? movePointRoot : transform;
        for (int i = 1; i <= MovePointCount; i++)
        {
            var t = root.Find($"movepoint{i}");
            if (t == null)
            {
                _movePointSlots.Add(null);
                _movePointChas.Add(null);
                continue;
            }

            var cha = t.Find("cha");
            _movePointSlots.Add(t.gameObject);
            _movePointChas.Add(cha != null ? cha.gameObject : null);
        }
    }

    /// <summary>
    /// 刷新技能点槽位：上限 ActionPoint 个，显示序号大的槽位；行动点扣除后对应槽位整体隐藏。
    /// cha 仅作"选中技能消耗预览"用，此方法只处理槽位显隐，不驱动 cha。
    /// </summary>
    private void RefreshMovePointSlots(BattleUnit unit)
    {
        if (unit == null) return;
        CacheMovePoints();

        int max = unit.Stats != null ? unit.Stats.ActionPoint : 0;
        if (max > MovePointCount) max = MovePointCount;

        // 剩余行动点决定还有几个槽位存活：剩余 remaining 个 → 显示序号最小的 remaining 个（序号大的先被消耗并隐藏）
        int remaining = unit.CurrentActionPoints;
        if (remaining < 0) remaining = 0;
        if (remaining > max) remaining = max;

        for (int i = 0; i < MovePointCount; i++)
        {
            // 有效槽位区间：上限 max 显示序号大的 max 个（max=4 → 0..3；max=3 → 1..3）
            bool withinMax = i >= (MovePointCount - max);

            // 已消耗（释放后隐藏）：序号大的先耗掉（4 → 3 → 2 → 1）
            bool alive = withinMax && (MovePointCount - 1 - i) < remaining;

            var slot = i < _movePointSlots.Count ? _movePointSlots[i] : null;
            if (slot != null) slot.SetActive(alive);

            // cha 预览由 PreviewSkillCost/ClearCostPreview 单独控制，此处重置
            var cha = i < _movePointChas.Count ? _movePointChas[i] : null;
            if (cha != null) cha.SetActive(false);
        }
    }

    /// <summary>
    /// 选中技能时预览将消耗的技能点：从有效槽位的最后（序号大，即从下）往上点亮 cost 个 cha。
    /// </summary>
    private void PreviewSkillCost(int skillId)
    {
        CacheMovePoints();
        if (_currentUnit == null) return;

        int cost = 1;
        if (skillId > 0)
        {
            var cfg = _battleManager?.GetTables()?.TbSkill.GetOrDefault(skillId);
            if (cfg != null) cost = cfg.Cost;
        }

        int max = _currentUnit.Stats != null ? _currentUnit.Stats.ActionPoint : 0;
        if (max > MovePointCount) max = MovePointCount;
        int remaining = _currentUnit.CurrentActionPoints;
        if (remaining > max) remaining = max;

        // 有效存活槽位的 index 集合：上限范围内且未被消耗（序号大的一侧先被消耗隐藏）
        var aliveIndices = new List<int>();
        for (int i = MovePointCount - 1; i >= 0; i--)
        {
            bool withinMax = i >= (MovePointCount - max);
            bool alive = withinMax && (MovePointCount - 1 - i) < remaining;
            if (alive) aliveIndices.Add(i);
        }
        // aliveIndices 已按序号从大到小排列（4 → 1），从最末（序号大）开始点亮 cost 个
        var toLight = new HashSet<int>();
        for (int k = 0; k < cost && k < aliveIndices.Count; k++)
            toLight.Add(aliveIndices[k]);

        for (int i = 0; i < MovePointCount; i++)
        {
            var cha = i < _movePointChas.Count ? _movePointChas[i] : null;
            if (cha != null) cha.SetActive(toLight.Contains(i));
        }
    }

    /// <summary> 清除消耗预览（取消选中/确认释放后） </summary>
    private void ClearCostPreview()
    {
        CacheMovePoints();
        for (int i = 0; i < MovePointCount; i++)
        {
            var cha = i < _movePointChas.Count ? _movePointChas[i] : null;
            if (cha != null) cha.SetActive(false);
        }
    }

    /// <summary> 隐藏全部技能点槽位 + 清预览（非玩家操作阶段） </summary>
    private void HideMovePoints()
    {
        CacheMovePoints();
        for (int i = 0; i < MovePointCount; i++)
        {
            var slot = i < _movePointSlots.Count ? _movePointSlots[i] : null;
            var cha = i < _movePointChas.Count ? _movePointChas[i] : null;
            if (slot != null) slot.SetActive(false);
            if (cha != null) cha.SetActive(false);
        }
    }

    #endregion

    #region 玩家操作

    /// <summary> 暴力行为 → 打开技能列表 </summary>
    private void OnViolenceClicked()
    {
        if (!CanOperate()) return;
        CloseAllSubPanels();
        if (skillPanel != null)
        {
            _currentPanelDetail = DetailViolence;
            skillPanel.Open(_battleManager, _currentUnit, OnSkillSelected, null, 0);   // 暴力行为：actiontype=0
            ShowDetail(_currentPanelDetail);
            if (subPanelContainer != null) subPanelContainer.SetActive(true);
            if (btnViolence != null) btnViolence.SetIsOnWithoutNotify(true);
            ShowActionHighlight(btnViolence);
        }
    }

    /// <summary> 咄咄逼人 → 打开对话选择 </summary>
    private void OnTalkClicked()
    {
        if (!CanOperate()) return;
        CloseAllSubPanels();
        if (skillPanel != null)
        {
            _currentPanelDetail = DetailTalk;
            skillPanel.Open(_battleManager, _currentUnit, OnSkillSelected, null, 1);   // 咄咄逼人：actiontype=1
            ShowDetail(_currentPanelDetail);
            if (subPanelContainer != null) subPanelContainer.SetActive(true);
            if (btnTalk != null) btnTalk.SetIsOnWithoutNotify(true);
            ShowActionHighlight(btnTalk);
        }
    }

    /// <summary> 翻找工具 → 打开物品列表；没有可用战斗道具时直接提示，不打开面板 </summary>
    private void OnItemClicked()
    {
        if (!CanOperate()) return;
        CloseAllSubPanels();

        if (itemPanel != null && !itemPanel.HasUsableItems(_battleManager))
        {
            if (logPanel != null) logPanel.AddLog("没有可用物品");
            ShowToast("没有可用物品");
            // 复位按钮为未选中，不点亮高亮
            if (btnItem != null) btnItem.SetIsOnWithoutNotify(false);
            SetActionHighlight(btnItem, false);
            return;
        }

        if (itemPanel != null)
        {
            _currentPanelDetail = DetailItem;
            itemPanel.Open(_battleManager, OnItemSelected);
            ShowDetail(_currentPanelDetail);
            if (subPanelContainer != null) subPanelContainer.SetActive(true);
            if (btnItem != null) btnItem.SetIsOnWithoutNotify(true);
            ShowActionHighlight(btnItem);
        }
    }

    /// <summary> 正在发呆 → 后移回合（排到队尾，下一位先行）；直接行动，不保持常亮 </summary>
    private void OnIdleClicked()
    {
        if (!CanOperate())
        {
            if (btnIdle != null) btnIdle.SetIsOnWithoutNotify(false);
            return;
        }
        CloseAllSubPanels();
        _battleManager.PlayerDeferTurn();
        if (btnIdle != null) btnIdle.SetIsOnWithoutNotify(false);
    }

    /// <summary> 右上角逃跑 → 战斗按失败结算（能否逃跑由战斗配置 Canflee 决定） </summary>
    private void OnRunClicked()
    {
        if (!CanOperate()) return;

        // 战斗配置不可逃跑时给出提示，不执行逃跑
        var cfg = _battleManager?.GetTables()?.TbBattle.GetOrDefault(_battleManager.BattleId);
        if (cfg != null && !cfg.Canflee)
        {
            if (logPanel != null)
                logPanel.AddLog("此战斗无法逃跑！", BattleLogCellType.Middle);
            ShowToast("此战斗无法逃跑！");
            return;
        }

        _battleManager.PlayerFlee();
    }

    /// <summary> 切换正常/二倍速播放（动画、等待、位移同步加速） </summary>
    private void OnSpeedClicked()
    {
        var presenter = BattlePresenter.Instance;
        if (presenter == null) return;

        presenter.SetDoubleSpeed(!presenter.DoubleSpeed);
        RefreshSpeedButton();
    }

    private void RefreshSpeedButton()
    {
        if (btnSpeedText == null) return;
        var presenter = BattlePresenter.Instance;
        btnSpeedText.text = presenter != null && presenter.DoubleSpeed ? "2x" : "1x";
    }

    private bool CanOperate()
    {
        if (_battleManager == null || !_isWaitingForPlayerAction) return false;
        var presenter = BattlePresenter.Instance;
        return presenter == null || !presenter.IsPlaying;
    }

    #endregion

    #region 子面板回调

    private void OnSkillSelected(int skillId)
    {
        _selectedSkillId = skillId;
        _selectedItemId = 0;  // 选技能即取消之前的物品选择，走技能释放路径
        Debug.Log($"[BattleWindow] OnSkillSelected: skillId={skillId} _currentUnit={_currentUnit} " +
                  $"剩余AP={_currentUnit?.CurrentActionPoints}");
        if (skillId > 0 && _currentUnit != null && _currentUnit.IsSkillOnCooldown(skillId))
        {
            _currentUnit.SkillCooldowns.TryGetValue(skillId, out int cd);
            Debug.Log($"[BattleWindow] 冷却中分支: cd={cd}");
            if (logPanel != null)
                logPanel.AddLog($"技能冷却中，还需{cd}回合");
            ShowToast($"技能冷却中，还需{cd}回合");
            return;
        }

        // 行动点不足：给出提示，面板保持打开
        if (_currentUnit != null)
        {
            int apCost = 1;
            if (skillId > 0)
            {
                var costCfg = _battleManager?.GetTables()?.TbSkill.GetOrDefault(skillId);
                if (costCfg != null) apCost = costCfg.Cost;
            }
            if (_currentUnit.CurrentActionPoints < apCost)
            {
                Debug.Log($"[BattleWindow] 行动点不足分支: 需要{apCost} 剩余{_currentUnit.CurrentActionPoints}");
                if (logPanel != null)
                    logPanel.AddLog($"行动点不足，无法使用（需要{apCost}点）");
                ShowToast($"行动点不足，无法使用（需要{apCost}点）");
                return; // 面板保持打开
            }
        }

        // targettype=Self 无需选目标，直接对自身释放
        if (skillId > 0 && _battleManager != null)
        {
            var skillCfg = _battleManager.GetTables()?.TbSkill.GetOrDefault(skillId);
            if (skillCfg != null && (TargetType)skillCfg.Targettype == TargetType.Self
                && _currentUnit != null)
            {
                Debug.Log($"[BattleWindow] Self技能分支: skillId={skillId} 直接释放");
                CloseAllSubPanels();
                _battleManager.PlayerUseSkill(skillId, _currentUnit.SlotIndex);
                return;
            }
        }

        Debug.Log($"[BattleWindow] 进入目标选择");
        // 选中技能后：点亮 cha 预览将消耗的技能点
        PreviewSkillCost(skillId);
        // 先选技能 → 进入目标选择模式（再选目标）
        StartTargetSelection();
    }

    private void OnTalkSelected(int choiceIndex)
    {
        CloseAllSubPanels();
        // TODO: 咄咄逼人对话效果
        // 目前先作为普通攻击处理
        _selectedSkillId = 0;
        _selectedItemId = 0;
        PreviewSkillCost(0); // 普通攻击消耗 1 点
        StartTargetSelection();
    }

    private void OnItemSelected(int itemId)
    {
        CloseAllSubPanels();
        _selectedSkillId = 0;
        _selectedItemId = itemId;
        PreviewSkillCost(0); // 物品使用消耗 1 点
        StartTargetSelection();
    }

    #endregion

    #region 目标选择

    private void StartTargetSelection()
    {
        // 先检查有无有效目标；无则提示并保持面板打开（可反复点击提示原因）
        var defaultTarget = FindDefaultTarget();
        Debug.Log($"[BattleWindow] StartTargetSelection: _selectedSkillId={_selectedSkillId} " +
                  $"默认目标={(defaultTarget != null ? GetUnitName(defaultTarget) : "NULL")} " +
                  $"有效目标数={GetValidTargets().Count}");
        if (defaultTarget == null)
        {
            if (logPanel != null)
                logPanel.AddLog("无有效目标，无法释放该技能");
            ShowToast("无有效目标，无法释放该技能");
            return;
        }

        // 进入目标选择模式（不关闭技能面板：技能面板只在左侧功能切换时更换/关闭）
        _isSelectingTarget = true;
        _currentCursorTarget = defaultTarget;
        _targetSelectStartTime = Time.unscaledTime;
        if (logPanel != null)
            logPanel.AddLog("请选择目标：点击 / 方向键切换 / 回车确认（Esc 取消）");
        if (_stageManager != null)
            _stageManager.ShowTargetSelected(defaultTarget);
    }

    /// <summary>
    /// 目标选择模式的默认目标：当前技能的第一个有效目标
    /// </summary>
    private BattleUnit FindDefaultTarget()
    {
        var validTargets = GetValidTargets();
        return validTargets.Count > 0 ? validTargets[0] : null;
    }

    /// <summary>
    /// 当前技能的有效目标列表（普通攻击→存活敌人；技能→按 selectable + range 筛选的可落点单位）
    /// </summary>
    private List<BattleUnit> GetValidTargets()
    {
        if (_battleManager == null || _currentUnit == null) return new List<BattleUnit>();

        if (_selectedItemId > 0)
            return _battleManager.GetAvailableTargetsForItem(_currentUnit, _selectedItemId);

        if (_selectedSkillId == 0)
            return _enemyUnits.FindAll(u => u.IsAlive);

        return _battleManager.GetAvailableTargetsForSkill(_currentUnit, _selectedSkillId);
    }

    private void CancelTargetSelection()
    {
        _isSelectingTarget = false;
        _currentCursorTarget = null;
        ClearCostPreview();
    }

    /// <summary> 鼠标是否悬停在 UI 上（避免点击角色时误触 UI 按钮） </summary>
    private bool IsPointerOverUI()
    {
        if (EventSystem.current == null) return false;
        var ped = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        var results = new List<RaycastResult>();
        EventSystem.current.RaycastAll(ped, results);
        return results.Count > 0;
    }

    /// <summary>
    /// 点击场景中的角色来选择目标
    /// </summary>
    private void TrySelectTargetByClick()
    {
        if (_mainCamera == null) _mainCamera = Camera.main;
        if (_mainCamera == null) return;

        Vector2 worldPos = _mainCamera.ScreenToWorldPoint(Input.mousePosition);
        var hit = Physics2D.OverlapPoint(worldPos);
        if (hit == null) return;

        // 查找被点击的单位
        var unit = FindUnitByGameObject(hit.gameObject);
        if (unit == null || !unit.IsAlive) return;

        // 先判断是不是有效落点（物品→func 阵营+param2 范围；技能→九宫格 selectable/range）
        bool valid = _selectedItemId > 0
            ? IsTargetValidForItem(unit, _selectedItemId)
            : IsTargetValidForSkill(unit, _selectedSkillId);

        if (!valid)
        {
            // 不是有效落点：只把"选中目标"重置到点到的这个人身上（选中与有效分离），不释放技能
            _currentCursorTarget = unit;
            if (_stageManager != null)
                _stageManager.ShowTargetSelected(unit);
            if (logPanel != null)
                logPanel.AddLog($"选中 {GetUnitName(unit)}：该位置不能作为落点", BattleLogCellType.Middle);
            return;
        }

        // 有效落点：直接释放
        if (_selectedItemId > 0)
            _battleManager.PlayerUseItem(_selectedItemId, unit.SlotIndex);
        else
            _battleManager.PlayerUseSkill(_selectedSkillId, unit.SlotIndex);

        // 显示目标选中标签
        if (_stageManager != null)
            _stageManager.ShowTargetSelected(unit);

        CancelTargetSelection();
    }

    private BattleUnit FindUnitByGameObject(GameObject go)
    {
        if (_battleManager == null || _stageManager == null) return null;

        // 通过槽位唯一定位被点击的单位（支持同一 personId 的多个单位）
        if (!_stageManager.TryResolveUnitSlot(go, out bool isPlayerSide, out int slotIndex))
            return null;

        var list = isPlayerSide ? _playerUnits : _enemyUnits;
        foreach (var unit in list)
        {
            if (unit.SlotIndex == slotIndex && unit.IsAlive) return unit;
        }
        return null;
    }

    #region 键盘游标切换目标（3.2 动态选择轻量版）

    /// <summary>
    /// 当前选中目标对该技能是否为有效目标：由技能九宫格配置（阵营 targettype + 可选落点 selectable + 范围 range）决定。
    /// 选中目标与有效目标分离——选中目标无限制（鼠标所指任意角色），只有落在技能九宫格有效范围内才算有效目标，
    /// 技能仅在有有效目标时才允许释放。
    /// </summary>
    private bool IsTargetValidForSkill(BattleUnit target, int skillId)
    {
        if (target == null || !target.IsAlive) return false;
        if (_battleManager == null || _currentUnit == null) return false;

        // 普通攻击：只对敌方单位有效（单格命中）
        if (skillId == 0)
            return target.IsPlayerSide != _currentUnit.IsPlayerSide;

        var skillCfg = _battleManager.GetTables()?.TbSkill.GetOrDefault(skillId);
        if (skillCfg == null) return false;

        var tt = (TargetType)skillCfg.Targettype;
        // Self 技能无需选目标，始终有效（可用性由行动点/冷却决定，不受选中目标影响）
        if (tt == TargetType.Self)
            return true;

        // 阵营校验：targettype 决定有效阵营（Ally→友方，Enemy→敌方）
        bool expectedSide = tt == TargetType.Ally ? _currentUnit.IsPlayerSide : !_currentUnit.IsPlayerSide;
        if (target.IsPlayerSide != expectedSide) return false;

        // 九宫格校验：GetSkillTargets 内部统一校验 selectable（落点可选）与 range（范围内命中），
        // 以选中目标所在槽位为落点中心，范围展开后至少命中一个存活单位才算有效
        return _battleManager.GetSkillTargets(_currentUnit, skillCfg, target.SlotIndex).Count > 0;
    }

    /// <summary>
    /// 当前选中目标对该物品是否为有效目标：由物品 func（阵营）与 param2（范围）决定。
    /// func 1=对己方 / 2=对敌方；以选中目标所在槽位为落点中心，范围展开后至少命中一个存活单位才算有效。
    /// </summary>
    private bool IsTargetValidForItem(BattleUnit target, int itemId)
    {
        if (target == null || !target.IsAlive) return false;
        if (_battleManager == null || _currentUnit == null) return false;

        var itemCfg = _battleManager.GetTables()?.TbItem.GetOrDefault(itemId);
        if (itemCfg == null) return false;

        int func = itemCfg.Func != null && itemCfg.Func.Count > 0 ? itemCfg.Func[0] : 0;
        if (func != 1 && func != 2) return false;

        // 阵营校验：func 1=己方，2=敌方
        bool expectedSide = func == 1 ? _currentUnit.IsPlayerSide : !_currentUnit.IsPlayerSide;
        if (target.IsPlayerSide != expectedSide) return false;

        // 范围校验：以选中目标为落点中心，param2 展开后至少命中一个存活目标
        var targetType = func == 1 ? TargetType.Ally : TargetType.Enemy;
        var hitSlots = SkillCombatUtil.ExpandRangeSlots(target.SlotIndex, itemCfg.Param2);
        return _battleManager.GetTargetsBySide(targetType, _currentUnit, hitSlots).Count > 0;
    }

    /// <summary> 键盘游标在当前有效目标基础上按行列偏移切换（左右循环，上下换行） </summary>
    private void CycleTarget(int colDelta, int rowDelta)
    {
        var targets = GetValidTargets();
        if (targets.Count == 0) return;

        var current = _currentCursorTarget;
        if (current == null || !current.IsAlive)
        {
            current = targets[0];
        }
        else
        {
            int idx = targets.IndexOf(current);
            if (idx < 0)
            {
                current = targets[0];
            }
            else
            {
                int next = idx + colDelta + rowDelta * 3;
                // 循环（含上下换行越界回绕）
                next = (next % targets.Count + targets.Count) % targets.Count;
                current = targets[next];
            }
        }

        _currentCursorTarget = current;
        if (_stageManager != null)
            _stageManager.ShowTargetSelected(current);
    }

    /// <summary> 回车/空格：确认当前游标目标并释放技能 </summary>
    private void ConfirmCurrentTarget()
    {
        if (_currentCursorTarget == null) return;
        var unit = _currentCursorTarget;
        if (!unit.IsAlive) return;

        if (_selectedItemId > 0)
        {
            if (!IsTargetValidForItem(unit, _selectedItemId)) return;
            _battleManager.PlayerUseItem(_selectedItemId, unit.SlotIndex);
        }
        else
        {
            if (!IsTargetValidForSkill(unit, _selectedSkillId)) return;
            _battleManager.PlayerUseSkill(_selectedSkillId, unit.SlotIndex);
        }

        if (_stageManager != null)
            _stageManager.ShowTargetSelected(unit);

        CancelTargetSelection();
    }

    #endregion

    #endregion

    #region UI控制

    /// <summary>
    /// 给目标按钮绑定悬浮显示描述（PointerEnter 显示，PointerExit 恢复当前面板描述）
    /// </summary>
    private void BindHoverDetail(Component target, string enterText)
    {
        if (target == null) return;
        var trigger = target.GetComponent<EventTrigger>() ?? target.gameObject.AddComponent<EventTrigger>();
        trigger.triggers.Clear();

        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        string text = enterText;
        enter.callback.AddListener(_ => ShowDetail(text));
        trigger.triggers.Add(enter);

        // 移出时恢复当前打开子面板的功能描述（无面板打开则清空）
        var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
        exit.callback.AddListener(_ => ShowDetail(_currentPanelDetail));
        trigger.triggers.Add(exit);
    }

    /// <summary>
    /// 绑定操作按钮（Toggle）：选中时打开对应功能并保持常亮；再次点击取消选中关闭面板。
    /// 解除 ToggleGroup 互斥，由代码统一管理唯一常亮，避免组内切换时序冲突。
    /// </summary>
    private void BindActionToggle(Toggle toggle, string detail, System.Action onClick)
    {
        if (toggle == null) return;

        // 解除 ToggleGroup 互斥，由代码统一管理唯一常亮，避免组内切换时序冲突
        toggle.group = null;

        // 选中态改为明显高亮（原 SelectedColor 与 NormalColor 几乎相同，无法辨别常亮）
        var colors = toggle.colors;
        colors.selectedColor = new Color(1f, 0.85f, 0.5f);
        toggle.colors = colors;

        toggle.onValueChanged.AddListener(isOn =>
        {
            if (isOn)
                onClick();          // 打开对应功能，按钮保持选中常亮
            else
                CloseAllSubPanels(); // 取消选中：关闭子面板
        });

        BindHoverDetail(toggle, detail);
    }

    /// <summary>
    /// 显示按钮/技能描述到 btnDetail（null 或空串则清空）
    /// </summary>
    public void ShowDetail(string text)
    {
        if (btnDetailText != null)
            btnDetailText.text = string.IsNullOrEmpty(text) ? "" : text;
    }

    /// <summary>
    /// 清空按钮/技能描述
    /// </summary>
    public void ClearDetail()
    {
        if (btnDetailText != null)
            btnDetailText.text = "";
    }

    private void SetActionButtonsVisible(bool visible)
    {
        if (btnViolence != null)
        {
            btnViolence.gameObject.SetActive(visible);
            if (!visible) btnViolence.SetIsOnWithoutNotify(false);
        }
        if (btnTalk != null)
        {
            btnTalk.gameObject.SetActive(visible);
            if (!visible) btnTalk.SetIsOnWithoutNotify(false);
        }
        if (btnItem != null)
        {
            btnItem.gameObject.SetActive(visible);
            if (!visible) btnItem.SetIsOnWithoutNotify(false);
        }
        if (btnIdle != null)
        {
            btnIdle.gameObject.SetActive(visible);
            if (!visible) btnIdle.SetIsOnWithoutNotify(false);
        }
    }

    private void CloseAllSubPanels()
    {
        _currentPanelDetail = null;
        ResetActionToggles();
        if (skillPanel != null) skillPanel.Close();
        if (itemPanel != null) itemPanel.Close();
        if (talkPanel != null) talkPanel.Close();
        if (subPanelContainer != null) subPanelContainer.SetActive(false);
        ClearDetail();
    }

    /// <summary> 复位四个操作按钮为未选中（关闭面板时熄灭常亮）并隐藏高亮图片 </summary>
    private void ResetActionToggles()
    {
        if (btnViolence != null) btnViolence.SetIsOnWithoutNotify(false);
        if (btnTalk != null) btnTalk.SetIsOnWithoutNotify(false);
        if (btnItem != null) btnItem.SetIsOnWithoutNotify(false);
        if (btnIdle != null) btnIdle.SetIsOnWithoutNotify(false);
        SetActionHighlight(btnViolence, false);
        SetActionHighlight(btnTalk, false);
        SetActionHighlight(btnItem, false);
        SetActionHighlight(btnIdle, false);
    }

    /// <summary> 打开功能时：显示对应按钮下的高亮图片（hl），隐藏其它 </summary>
    private void ShowActionHighlight(Toggle activeToggle)
    {
        SetActionHighlight(btnViolence, activeToggle == btnViolence);
        SetActionHighlight(btnTalk, activeToggle == btnTalk);
        SetActionHighlight(btnItem, activeToggle == btnItem);
        SetActionHighlight(btnIdle, activeToggle == btnIdle);
    }

    /// <summary> 设置按钮下名为 "hl" 的高亮图片显隐 </summary>
    private void SetActionHighlight(Toggle btn, bool show)
    {
        if (btn == null) return;
        var hl = btn.transform.Find("hl");
        if (hl == null)
        {
            // hl 可能是按钮下的深层子节点（中间还隔着容器），逐层兜底查找
            foreach (var t in btn.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "hl") { hl = t; break; }
            }
        }
        if (hl != null) hl.gameObject.SetActive(show);
    }

    /// <summary>
    /// 弹出提示弹窗（技能无法使用等），居中显示，约 1.5 秒后自动消失
    /// </summary>
    private void ShowToast(string message)
    {
        if (_toastGo == null)
            CreateToast();
        if (_toastGo == null) return;

        _toastGo.SetActive(true);
        if (_toastText != null)
            _toastText.text = message;

        if (_toastCoroutine != null)
            StopCoroutine(_toastCoroutine);
        _toastCoroutine = StartCoroutine(HideToastRoutine());
    }

    /// <summary>
    /// 动态创建提示弹窗（挂到 Canvas 下，屏幕居中偏上）
    /// </summary>
    private void CreateToast()
    {
        var canvas = GetComponentInParent<Canvas>();
        var parent = canvas != null ? canvas.transform : transform;

        var go = new GameObject("BattleToast", typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(460f, 130f);
        rect.anchoredPosition = new Vector2(0f, 240f);

        var bg = go.GetComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.82f);
        bg.raycastTarget = false;  // 提示弹窗不拦截点击，避免挡住技能/按钮操作

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        textGo.transform.SetParent(go.transform, false);
        var textRect = textGo.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(16f, 12f);
        textRect.offsetMax = new Vector2(-16f, -12f);

        _toastText = textGo.GetComponent<TextMeshProUGUI>();
        _toastText.font = TMP_Settings.defaultFontAsset;
        _toastText.fontSize = 26f;
        _toastText.alignment = TextAlignmentOptions.Center;
        _toastText.raycastTarget = false;
        _toastText.color = Color.white;

        _toastGo = go;
        _toastGo.SetActive(false);
    }

    private System.Collections.IEnumerator HideToastRoutine()
    {
        yield return new WaitForSeconds(1.5f);
        if (_toastGo != null)
            _toastGo.SetActive(false);
    }

    #endregion
}
