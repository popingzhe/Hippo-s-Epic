using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

[AddComponentMenu("Level 2D/Interactable")]
public class Interactable : MonoBehaviour
{
    private const string HintPrefabAddress = "Assets/Prefabs/UI/Interact/InteractHint";
    private const string TipPrefabAddress = "Assets/Prefabs/UI/Interact/SceneTip";

    [Header("Phases")]
    [SerializeField] private List<InteractionPhase> phases = new List<InteractionPhase>();

    private static GameObject _hintPrefab;
    private static AsyncOperationHandle<GameObject> _hintPrefabHandle;
    private static bool _hintLoading;
    private readonly List<InteractHint> _pendingHints = new List<InteractHint>();
    private InteractHint _hint;

    private static GameObject _tipPrefab;
    private static AsyncOperationHandle<GameObject> _tipPrefabHandle;
    private static bool _tipLoading;
    private static readonly List<Action> _tipPendingRequests = new List<Action>();
    private SceneTip _tip;
    private bool _itemTipShowing;
    private int _confirmSuppressFrame = -1;
    private Action _pendingItemTipConfirm;

    private int _currentState;
    private InteractionPhase _currentPhase;
    private Coroutine _phaseAnimRoutine;
    private bool _playerInside;
    private bool _hasExecuted;
    private int _playerInsideCount;

    public string EntityId => $"{transform.parent.name}_{name}";
    public int CurrentState => _currentState;
    public InteractionPhase CurrentPhase => _currentPhase;

    public InteractionPhase GetPhase(int state)
    {
        return phases.Find(p => p.state == state);
    }

    public void ApplyState(int state)
    {
        _currentState = state;
        _hasExecuted = false;

        _currentPhase = GetPhase(state);

        if (_currentPhase == null)
        {
            string configured = "";
            if (phases != null)
            {
                for (int i = 0; i < phases.Count; i++)
                {
                    if (i > 0) configured += ",";
                    configured += phases[i].state;
                }
            }
            Debug.LogWarning($"[Interactable] {name}({EntityId}) 交互阶段状态不一致：当前 state={state}，但只配置了 state=[{configured}] 的阶段 ⇒ 提示框内容不会显示。请补一个 state={state} 的阶段，或检查存档里的实体状态");
            return;
        }

        _currentPhase.MigrateIfNeeded();

        if (_currentPhase.buttons == null || _currentPhase.buttons.Count == 0)
            Debug.LogWarning($"[Interactable] {name}({EntityId}) 阶段 state={state} 没有配置任何按钮 ⇒ 交互不可执行（提示框会是空白）");

        if (_currentPhase.deactivateSelf)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);

        ApplyChildNodes(_currentPhase);
        ApplyAnimation(_currentPhase);
    }

    private void ApplyAnimation(InteractionPhase phase)
    {
        StopPhaseAnimation();

        if (!phase.playAnimation) return;
        if (phase.animationController == null) return;

        var ctrl = phase.animationController;
        if (!ctrl.IsInitialized) return;
        if (ctrl.Config == null) return;

        // 收集本阶段要播的动画：优先用列表，列表为空则兼容旧的单个 composition
        var wanted = new List<CompositionName>();
        if (phase.compositions != null)
        {
            foreach (var c in phase.compositions)
                if (!wanted.Contains(c)) wanted.Add(c);
        }
        if (wanted.Count == 0)
            wanted.Add(phase.composition);

        // 过滤掉配置里不存在的组合，并给出告警（方便美术排查配漏）
        var valid = new List<CompositionName>();
        foreach (var c in wanted)
        {
            if (ctrl.Config.HasComposition(c)) valid.Add(c);
            else Debug.LogWarning($"[Interactable] {name} 阶段 state={phase.state} 的组合动画 {c} 在 {ctrl.Config.name} 里不存在 ⇒ 已跳过");
        }
        if (valid.Count == 0) return;

        // 只有一个：直接循环播（保持旧行为）
        if (valid.Count == 1)
        {
            ctrl.PlayComposition(valid[0]);
            return;
        }

        // 多个：起协程按 顺序/随机 轮播
        _phaseAnimRoutine = StartCoroutine(PlayPhaseAnimationListRoutine(ctrl, valid, phase.animationOrder));
    }

    /// <summary> 停掉上一次阶段的轮播协程（不主动停已有动画，保持原行为） </summary>
    private void StopPhaseAnimation()
    {
        if (_phaseAnimRoutine != null)
        {
            StopCoroutine(_phaseAnimRoutine);
            _phaseAnimRoutine = null;
        }
    }

    /// <summary>
    /// 列表轮播：每个动画循环播一个完整时长后切下一个。
    /// Sequential = 按列表顺序逐个播；Random = 每次随机挑（避免连续重复）。
    /// </summary>
    private System.Collections.IEnumerator PlayPhaseAnimationListRoutine(AnimationController ctrl,
        List<CompositionName> list, PhaseAnimOrder order)
    {
        int index = 0;
        int last = -1;

        while (true)
        {
            int pick;
            if (order == PhaseAnimOrder.Random)
            {
                if (list.Count <= 1)
                {
                    pick = 0;
                }
                else
                {
                    // 从"除上一次以外的 N-1 项"里等概率取，避免连续重复且分布不偏向下一项
                    pick = UnityEngine.Random.Range(0, list.Count - 1);
                    if (last >= 0 && pick >= last) pick++;
                }
            }
            else
            {
                pick = index % list.Count;
            }

            var name = list[pick];
            ctrl.PlayComposition(name);

            float dur = GetCompositionDuration(ctrl, name);
            if (dur <= 0.05f) dur = 2f;   // 拿不到时长时的兜底，避免疯狂切换
            yield return new WaitForSeconds(dur);

            last = pick;
            index++;
        }
    }

    /// <summary> 组合动画的时长 = 它各层级时长里最长的那个 </summary>
    private float GetCompositionDuration(AnimationController ctrl, CompositionName name)
    {
        var comp = ctrl.Config != null ? ctrl.Config.GetComposition(name) : null;
        if (comp == null || comp.layers == null || comp.layers.Count == 0) return 0f;

        float max = 0f;
        foreach (var layer in comp.layers)
        {
            float d = ctrl.GetAnimationDuration(layer.layerName);
            if (d > max) max = d;
        }
        return max;
    }

    private void ApplyChildNodes(InteractionPhase phase)
    {
        var styleRoot = transform.Find("Style");
        if (styleRoot == null) return;
        if (phase.activeChildNames == null || phase.activeChildNames.Count == 0) return;

        var activeSet = new HashSet<string>(phase.activeChildNames);

        for (int i = 0; i < styleRoot.childCount; i++)
        {
            Transform child = styleRoot.GetChild(i);
            bool shouldBeActive = activeSet.Contains(child.name);

            if (child.gameObject.activeSelf != shouldBeActive)
                child.gameObject.SetActive(shouldBeActive);
        }
    }

    public void InitializeHint()
    {
        if (_hint != null) return;
        if (phases == null || phases.Count == 0) return;

        Transform uiChild = transform.Find("UI");
        if (uiChild == null) return;

        if (_hintPrefab != null)
        {
            CreateHintInstance(uiChild);
            return;
        }

        if (!_hintLoading)
            StartCoroutine(LoadHintPrefabAsync(uiChild));
        else
            _pendingHints.Add(null);
    }

    private IEnumerator LoadHintPrefabAsync(Transform uiChild)
    {
        _hintLoading = true;
        _hintPrefabHandle = Addressables.LoadAssetAsync<GameObject>(HintPrefabAddress);
        yield return _hintPrefabHandle;

        if (_hintPrefabHandle.Status == AsyncOperationStatus.Succeeded)
        {
            _hintPrefab = _hintPrefabHandle.Result;
            CreateHintInstance(uiChild);
        }
        else
        {
            Debug.LogWarning($"[Interactable] 加载 Hint 预制体失败: {HintPrefabAddress}");
        }
        _hintLoading = false;
    }

    private void CreateHintInstance(Transform parent)
    {
        if (_hintPrefab == null) return;
        if (_hint != null) return;
        GameObject instance = Instantiate(_hintPrefab, parent);
        instance.name = "InteractHint";
        _hint = instance.GetComponent<InteractHint>();

        if (_playerInside)
            RefreshHint();
    }

    public static void ReleaseHintPrefab()
    {
        if (_hintPrefabHandle.IsValid())
            Addressables.Release(_hintPrefabHandle);
        _hintPrefab = null;
        _hintPrefabHandle = default;
    }

    #region Player Detection

    private float _checkInterval = 0.5f;
    private float _lastCheckTime;
    private float _triggerCheckInterval = 1f;
    private float _lastTriggerCheckTime;
    private Collider2D _triggerCollider;

    private void OnTriggerEnter2D(Collider2D other)
    {
        var player = other.GetComponentInParent<PlayerCharacter>();
        if (player == null) return;

        _playerInsideCount++;
        if (_playerInsideCount != 1) return;

        _triggerCollider = other;
        _playerInside = true;
        _lastCheckTime = Time.time;

        if (player.CurrentInteractable != null && player.CurrentInteractable != this)
            player.CurrentInteractable.OnPlayerExit();

        player.SetCurrentInteractable(this);

        if (_hint == null)
            InitializeHint();

        RefreshHint();

        // 本阶段勾选了"自动触发第一个按钮"：进入范围就直接执行，不等玩家按键
        TryAutoTriggerFirstButton();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        var player = other.GetComponentInParent<PlayerCharacter>();
        if (player == null) return;

        _playerInsideCount--;
        if (_playerInsideCount > 0) return;
        _playerInsideCount = 0;

        _playerInside = false;
        _triggerCollider = null;
        if (_hint != null)
            _hint.Hide();
        ForceFinishItemTip();

        if (player.CurrentInteractable == this)
            player.ClearCurrentInteractable();
    }

    public void OnPlayerEnter()
    {
        _playerInside = true;
        RefreshHint();
    }

    private void Update()
    {
        CheckConditionTriggers();

        if (!_playerInside || _triggerCollider == null)
            return;

        if (Time.time - _lastCheckTime < _checkInterval)
            return;

        _lastCheckTime = Time.time;

        if (!IsStillOverlapping())
        {
            _playerInside = false;
            _playerInsideCount = 0;
            _triggerCollider = null;
            if (_hint != null)
                _hint.Hide();
            ForceFinishItemTip();

            var player = FindObjectOfType<PlayerCharacter>();
            if (player != null && player.CurrentInteractable == this)
                player.ClearCurrentInteractable();
        }
    }

    private void CheckConditionTriggers()
    {
        if (_currentPhase == null) return;
        if (_currentPhase.conditionTriggers == null || _currentPhase.conditionTriggers.Count == 0) return;

        if (Time.time - _lastTriggerCheckTime < _triggerCheckInterval) return;
        _lastTriggerCheckTime = Time.time;

        var condSys = ConditionSystem.HasInstance ? ConditionSystem.Instance : null;
        if (condSys == null) return;

        foreach (var trigger in _currentPhase.conditionTriggers)
        {
            if (trigger.transitionToState < 0) continue;
            if (trigger.conditionId != 0 && condSys.IsConditionMet(trigger.conditionId))
            {
                int newState = trigger.transitionToState;
                SaveManager.Instance.SetEntityState(EntityId, newState);
                ApplyState(newState);
                return;
            }
        }
    }

    private bool IsStillOverlapping()
    {
        if (_triggerCollider == null) return false;

        var myCollider = GetComponent<Collider2D>();
        if (myCollider == null) return false;

        return myCollider.OverlapPoint(_triggerCollider.bounds.center)
            || _triggerCollider.OverlapPoint(myCollider.bounds.center);
    }

    public void OnPlayerExit()
    {
        _playerInside = false;
        if (_hint != null)
            _hint.Hide();
        ForceFinishItemTip();
    }

    public void OnPlayerExecute(int buttonIndex = 0)
    {
        if (!_playerInside)
            return;
        if (_currentPhase == null)
            return;
        if (_itemTipShowing)
            return;   // 物品获得提示未确认前冻结交互，避免重复触发
        if (Time.frameCount == _confirmSuppressFrame)
            return;   // 确认那一帧交互键还按着：别顺手把下一阶段也执行了

        var buttons = _currentPhase.buttons;
        if (buttons == null || buttonIndex < 0 || buttonIndex >= buttons.Count)
        {
            Debug.LogWarning($"[Interactable] {EntityId} OnPlayerExecute({buttonIndex}) 无效，buttons.Count={buttons?.Count ?? 0}");
            return;
        }

        var button = buttons[buttonIndex];

        if (button.conditionId != 0)
        {
            var condSys = ConditionSystem.HasInstance ? ConditionSystem.Instance : null;
            if (condSys != null && !condSys.IsConditionMet(button.conditionId))
                return;
        }

        if (_hasExecuted && !_currentPhase.canRepeat)
            return;

        int added = ExecuteButtonAction(button);

        _hasExecuted = true;

        var phase = _currentPhase;

        // —— 获取物品劫持：真的拿到物品时，先隐藏原交互 UI，在原位显示物品获得提示（SceneTip）；
        //    点确认后才继续走原流程的收尾（切阶段 / 销毁 / 隐藏 / 刷新）
        //    虚拟物品（item.type=1，例『经验值+1』）：获取即消化，不弹物品获得框，直接推进流程
        if (button.type == InteractionType.Pickup && added > 0 && !IsVirtualItem(button.dataId))
        {
            HijackWithItemTip(button, phase);
            return;
        }

        CompleteExecute(button, phase);
    }

    /// <summary>虚拟物品（item.type=1）：不在背包显示、获取即消化——获取时不弹物品获得提示框。</summary>
    private static bool IsVirtualItem(int itemId)
    {
        var itemCfg = BagManager.Instance.GetItemConfig(itemId);
        return itemCfg != null && itemCfg.Type == 1;
    }

    private void CompleteExecute(ButtonOption button, InteractionPhase phase)
    {
        if (phase == null) return;

        bool hideAfterExecute = phase.hideAfterExecute;
        bool destroySelf = phase.destroySelf;

        if (button.transitionToState >= 0)
        {
            int newState = button.transitionToState;
            SaveManager.Instance.SetEntityState(EntityId, newState);
            ApplyState(newState);
        }

        if (destroySelf)
        {
            if (_hint != null)
                _hint.Hide();
            Destroy(gameObject);
        }
        else if (hideAfterExecute)
        {
            if (_hint != null)
                _hint.Hide();
        }
        else
        {
            RefreshHint();
        }
    }

    /// <summary>
    /// 获得 Buff：dataId=Buff表ID；param1=1/空 时用本交互体的提示框弹"获得提示"
    /// （父节点 = 本交互体的 UI 子节点，位置与物品/经验提示一致）。
    /// </summary>
    private void ExecuteGainBuff(ButtonOption button)
    {
        if (button.dataId <= 0)
        {
            Debug.LogWarning("[Interactable] GainBuff 未配置 Buff ID");
            return;
        }

        var buffMgr = BuffManager.Instance;
        if (buffMgr != null) buffMgr.ApplyBuff(button.dataId);
        else Debug.LogWarning("[Interactable] BuffManager 不可用，无法发放 buff " + button.dataId);

        string p1 = (button.param1 ?? string.Empty).Trim().ToLower();
        bool showTip = string.IsNullOrEmpty(p1) || p1 == "1" || p1 == "true";
        if (!showTip) return;

        // 和物品获得一致：先隐藏交互提示框，提示确认后再刷新回来
        if (_hint != null) _hint.Hide();

        Transform parent = transform.Find("UI");
        if (parent == null) parent = transform;

        SceneTip.ShowBuffGlobal(button.dataId, () => { if (this != null) RefreshHint(); }, parent);
        Debug.Log($"[Interactable] 获得 Buff {button.dataId}（已隐藏交互提示，提示父节点={parent.name}）");
    }

    /// <summary>失去 Buff：dataId=Buff表ID；只做移除（不弹提示）。</summary>
    private void ExecuteLoseBuff(ButtonOption button)
    {
        if (button.dataId <= 0)
        {
            Debug.LogWarning("[Interactable] LoseBuff 未配置 Buff ID");
            return;
        }

        var buffMgr = BuffManager.Instance;
        if (buffMgr == null)
        {
            Debug.LogWarning("[Interactable] BuffManager 不可用，无法移除 buff " + button.dataId);
            return;
        }

        bool had = buffMgr.HasBuff(button.dataId);
        buffMgr.RemoveBuff(button.dataId);
        Debug.Log($"[Interactable] 失去 Buff {button.dataId}（移除前{(had ? "持有" : "未持有")}）");
    }

    /// <summary>进入战斗：dataId=battle 表ID，走 GameApp.EnterBattle（加载战斗场景 → 初始化 → 开打）。</summary>
    private void ExecuteEnterBattle(ButtonOption button)
    {
        if (button.dataId <= 0)
        {
            Debug.LogWarning("[Interactable] EnterBattle 未配置 battle ID");
            return;
        }

        var app = GameApp.Instance;
        if (app == null)
        {
            Debug.LogWarning("[Interactable] GameApp 未就绪，无法进入战斗 " + button.dataId);
            return;
        }

        Debug.Log($"[Interactable] 进入战斗 battleId={button.dataId}");
        app.EnterBattle(button.dataId);
    }

    private void HijackWithItemTip(ButtonOption button, InteractionPhase phase)
    {
        _itemTipShowing = true;

        if (_hint != null)
            _hint.Hide();

        Transform parent = transform.Find("UI");
        if (parent == null) parent = transform;

        var tipParent = parent;
        Action onConfirmed = () =>
        {
            _itemTipShowing = false;
            _tip = null;
            _pendingItemTipConfirm = null;
            _confirmSuppressFrame = Time.frameCount;   // 确认发生在哪一帧
            CompleteExecute(button, phase);
        };
        _pendingItemTipConfirm = onConfirmed;

        if (_tipPrefab != null)
        {
            CreateTipInstance(tipParent, button.dataId, onConfirmed);
            return;
        }

        _tipPendingRequests.Add(() =>
        {
            if (!_itemTipShowing) return;   // 玩家已走开/流程已结束，作废
            CreateTipInstance(tipParent, button.dataId, onConfirmed);
        });
        if (!_tipLoading)
            StartCoroutine(LoadTipPrefabAsync());
    }

    private IEnumerator LoadTipPrefabAsync()
    {
        _tipLoading = true;
        _tipPrefabHandle = Addressables.LoadAssetAsync<GameObject>(TipPrefabAddress);
        yield return _tipPrefabHandle;

        if (_tipPrefabHandle.Status == AsyncOperationStatus.Succeeded)
        {
            _tipPrefab = _tipPrefabHandle.Result;
        }
        else
        {
            Debug.LogWarning($"[Interactable] 加载 SceneTip 预制体失败: {TipPrefabAddress}");
        }
        _tipLoading = false;

        var pending = _tipPendingRequests.ToArray();
        _tipPendingRequests.Clear();
        for (int i = 0; i < pending.Length; i++)
        {
            if (pending[i] != null) pending[i]();
        }
    }

    private void CreateTipInstance(Transform parent, int itemId, Action onConfirmed)
    {
        // 流程已经结束了（比如预载期间玩家走开触发了强制收尾）：作废这次创建，
        // 也**不要**再调 onConfirmed —— 它已经在强制收尾时执行过，再调会重复推进
        if (!_itemTipShowing) return;

        if (this == null || parent == null || _tipPrefab == null)
        {
            Debug.LogWarning($"[Interactable] SceneTip 预制体未就绪，跳过物品获得提示");
            if (onConfirmed != null) onConfirmed();
            return;
        }

        GameObject instance = Instantiate(_tipPrefab, parent);
        instance.name = "SceneTip";

        // 对齐到原交互 UI 的位置
        if (_hint != null)
        {
            instance.transform.localPosition = _hint.transform.localPosition;
            instance.transform.localRotation = _hint.transform.localRotation;
        }

        _tip = instance.GetComponent<SceneTip>();
        if (_tip == null)
        {
            Debug.LogWarning("[Interactable] SceneTip 预制体上缺少 SceneTip 组件");
            Destroy(instance);
            if (onConfirmed != null) onConfirmed();
            return;
        }

        if (!_tip.Show(itemId, onConfirmed))
        {
            Debug.LogWarning("[Interactable] SceneTip.Show 失败（表未就绪?），直接继续流程");
            if (onConfirmed != null) onConfirmed();
        }
    }

    /// <summary>玩家没点确认就走开：收起提示框，并按"点了确认"一样继续流程</summary>
    private void ForceFinishItemTip()
    {
        if (!_itemTipShowing) return;

        if (_tip != null)
        {
            _tip.gameObject.SetActive(false);
            Destroy(_tip.gameObject);
            _tip = null;
        }

        _tipPendingRequests.Clear();   // 预载中的创建请求一并作废，别等加载完再冒出来

        Action pending = _pendingItemTipConfirm;
        _pendingItemTipConfirm = null;
        _itemTipShowing = false;

        if (pending != null) pending();

        // 阶段虽然推进了，但人已经走了：新的提示 UI 不要露出来
        if (_hint != null)
            _hint.Hide();
    }

    public static void ReleaseTipPrefab()
    {
        if (_tipPrefabHandle.IsValid())
            Addressables.Release(_tipPrefabHandle);
        _tipPrefab = null;
        _tipPrefabHandle = default;
        _tipPendingRequests.Clear();
    }

    #endregion

    private int ExecuteButtonAction(ButtonOption button)
    {
        Debug.Log($"[Interactable] {EntityId} 执行 {button.type} (buttonText={button.buttonText}, param1={button.param1}, param2={button.param2})");

        switch (button.type)
        {
            case InteractionType.HintOnly:
                break;

            case InteractionType.Dialogue:
                ExecuteDialogue(button);
                break;

            case InteractionType.Pickup:
                return ExecutePickup(button);

            case InteractionType.Consume:
                ExecuteConsume(button);
                break;

            case InteractionType.Teleport:
                ExecuteTeleport(button);
                break;

            case InteractionType.GainBuff:
                ExecuteGainBuff(button);
                break;
            case InteractionType.LoseBuff:
                ExecuteLoseBuff(button);
                break;
            case InteractionType.EnterBattle:
                ExecuteEnterBattle(button);
                break;
            case InteractionType.SetCondition:
                ExecuteSetCondition(button);
                break;
        }

        return 0;
    }

    /// <summary>
    /// 修改条件满足情况：dataId=条件ID，param1=目标状态。
    ///   1/true/空  = 覆盖为"已满足"
    ///   0/false    = 覆盖为"未满足"
    ///   clear/2    = 清除覆盖，恢复按条件表判定
    /// 覆盖优先级最高：写入后不会再被数据变化触发的 Notify 重算冲掉，重启游戏(Initialize)才清空。
    /// </summary>
    private void ExecuteSetCondition(ButtonOption button)
    {
        int condId = button.dataId;
        if (condId == 0)
        {
            Debug.LogWarning($"[Interactable] {EntityId} SetCondition 未指定条件ID（dataId=0）");
            return;
        }

        var condSys = ConditionSystem.HasInstance ? ConditionSystem.Instance : null;
        if (condSys == null)
        {
            Debug.LogWarning($"[Interactable] {EntityId} ConditionSystem 不存在，条件 {condId} 未修改");
            return;
        }

        string v = string.IsNullOrEmpty(button.param1) ? "" : button.param1.Trim().ToLowerInvariant();

        // param1 = clear / release / 2 ⇒ 清除覆盖，恢复按条件表判定
        if (v == "clear" || v == "release" || v == "2")
        {
            condSys.ClearOverride(condId);
            Debug.Log($"[Interactable] {EntityId} 触发：清除条件 {condId} 的覆盖");
            return;
        }

        bool target = !(v == "0" || v == "false");
        condSys.ForceSetCondition(condId, target);
        Debug.Log($"[Interactable] {EntityId} 触发：条件 {condId} 覆盖为{(target ? "已满足" : "未满足")}（优先于条件表）");
    }

    private void ExecuteDialogue(ButtonOption button)
    {
        if (_hint != null)
            _hint.Hide();

        var uiManager = ManagerRegistry.Get<UIManager>();
        if (uiManager != null)
        {
            uiManager.Open<DialogWindow>(button.dataId);
        }
    }

    private int ExecutePickup(ButtonOption button)
    {
        int itemId = button.dataId;
        int count = 1;
        if (!string.IsNullOrEmpty(button.param1) && int.TryParse(button.param1, out int parsed))
            count = parsed;
        int added = BagManager.Instance.AddItem(itemId, count);
        if (added > 0)
        {
            var itemCfg = BagManager.Instance.GetItemConfig(itemId);
            Debug.Log($"获得了道具: {itemCfg?.Name ?? itemId.ToString()} x{added}");
        }
        return added;
    }

    private void ExecuteConsume(ButtonOption button)
    {
        int itemId = button.dataId;
        int count = 1;
        if (!string.IsNullOrEmpty(button.param1) && int.TryParse(button.param1, out int parsed))
            count = parsed;

        if (!BagManager.Instance.HasItem(itemId, count))
        {
            var itemCfg = BagManager.Instance.GetItemConfig(itemId);
            Debug.LogWarning($"[Interactable] {EntityId} 物品不足: {itemCfg?.Name ?? itemId.ToString()} 需要{count}个");
            return;
        }

        bool removed = BagManager.Instance.RemoveItem(itemId, count);
        if (removed)
        {
            var itemCfg = BagManager.Instance.GetItemConfig(itemId);
            Debug.Log($"消耗了道具: {itemCfg?.Name ?? itemId.ToString()} x{count}");
        }
    }

    private void ExecuteTeleport(ButtonOption button)
    {
        if (button.teleportMode == 1)
        {
            string targetScene = button.param2;
            if (!string.IsNullOrEmpty(targetScene) && GameApp.Instance != null)
            {
                SaveManager.Instance.SetEntityState(EntityId, _currentState);
                GameApp.Instance.StartGame(targetScene);
            }
            return;
        }

        string target = button.param1;
        if (string.IsNullOrEmpty(target)) return;

        var all = FindObjectsOfType<Interactable>(true);
        foreach (var ia in all)
        {
            if (ia.EntityId != target) continue;

            var mapManager = LevelController.Instance?.MapManager;
            Map targetMap = FindParentMap(ia.transform);
            if (mapManager != null && targetMap != null)
                mapManager.SwitchMap(targetMap.name, false);

            var player = FindObjectOfType<PlayerCharacter>();
            if (player != null)
            {
                Vector3 pos = ia.transform.position;
                player.transform.position = new Vector3(pos.x, pos.y, pos.y);
                Debug.Log($"[Interactable] {EntityId} 传送到交互点 {target}");
            }
            return;
        }

        Debug.LogWarning($"[Interactable] {EntityId} 未找到目标交互点: {target}");
    }

    private static Map FindParentMap(Transform t)
    {
        Transform cur = t.parent;
        while (cur != null)
        {
            var map = cur.GetComponent<Map>();
            if (map != null) return map;
            cur = cur.parent;
        }
        return null;
    }

    private void OnEnable()
    {
        if (ConditionSystem.HasInstance)
            ConditionSystem.Instance.OnConditionChanged += OnConditionChanged;
    }

    private void OnDisable()
    {
        StopPhaseAnimation();

        if (ConditionSystem.HasInstance)
            ConditionSystem.Instance.OnConditionChanged -= OnConditionChanged;
    }

    /// <summary>
    /// 进入交互范围时：本阶段勾选了自动触发就直接执行第一个可用按钮（等同玩家点了它）。
    /// "可用" = 按钮无条件，或条件已满足（与提示 UI 的显示规则一致）。
    /// </summary>
    private void TryAutoTriggerFirstButton()
    {
        if (_currentPhase == null || !_currentPhase.autoTriggerFirstButton) return;

        var buttons = _currentPhase.buttons;
        if (buttons == null || buttons.Count == 0) return;

        var condSys = ConditionSystem.HasInstance ? ConditionSystem.Instance : null;

        for (int i = 0; i < buttons.Count; i++)
        {
            int condId = buttons[i].conditionId;
            if (condId != 0 && (condSys == null || !condSys.IsConditionMet(condId)))
                continue;

            Debug.Log($"[Interactable] {EntityId} 进入范围自动触发按钮{i}（{buttons[i].type}）");
            OnPlayerExecute(i);
            return;
        }

        Debug.LogWarning($"[Interactable] {EntityId} 勾选了自动触发，但当前没有可用按钮（条件都不满足），已跳过");
    }

    private void OnConditionChanged(int conditionId, bool isMet)
    {
        if (_currentPhase == null) return;

        if (isMet && _currentPhase.conditionTriggers != null)
        {
            foreach (var trigger in _currentPhase.conditionTriggers)
            {
                if (trigger.conditionId == conditionId && trigger.transitionToState >= 0)
                {
                    SaveManager.Instance.SetEntityState(EntityId, trigger.transitionToState);
                    ApplyState(trigger.transitionToState);
                    if (_playerInside)
                        RefreshHint();
                    return;
                }
            }
        }

        if (!_playerInside || _hint == null)
            return;

        var buttons = _currentPhase.buttons;
        if (buttons == null) return;

        for (int i = 0; i < buttons.Count; i++)
        {
            if (buttons[i].conditionId == conditionId)
            {
                RefreshHint();
                return;
            }
        }

        if (_currentPhase.conditionTriggers != null)
        {
            foreach (var trigger in _currentPhase.conditionTriggers)
            {
                if (trigger.conditionId == conditionId)
                {
                    RefreshHint();
                    return;
                }
            }
        }
    }

    private void RefreshHint()
    {
        if (_itemTipShowing)
            return;   // 物品获得提示显示期间，原交互 UI 保持隐藏

        if (!_playerInside)
        {
            // 玩家已经离开：哪怕刚切到新阶段，也别把提示 UI 弹出来
            if (_hint != null)
                _hint.Hide();
            return;
        }

        if (_hint == null || _currentPhase == null)
            return;

        if (_hasExecuted && !_currentPhase.canRepeat)
        {
            _hint.Hide();
            return;
        }

        _currentPhase.MigrateIfNeeded();

        var buttons = _currentPhase.buttons;
        if (buttons == null || buttons.Count == 0)
        {
            _hint.Show(_currentPhase.hintText, Array.Empty<string>(), Array.Empty<bool>(), null);
            return;
        }

        var condSys = ConditionSystem.HasInstance ? ConditionSystem.Instance : null;
        var visibleTexts = new List<string>();
        var visibleInteractables = new List<bool>();
        var visibleIndices = new List<int>();

        for (int i = 0; i < buttons.Count; i++)
        {
            var btn = buttons[i];
            visibleTexts.Add(btn.buttonText);
            visibleIndices.Add(i);

            bool conditionMet = true;
            if (btn.conditionId != 0 && condSys != null && !condSys.IsConditionMet(btn.conditionId))
                conditionMet = false;
            visibleInteractables.Add(conditionMet);
        }

        _hint.Show(_currentPhase.hintText, visibleTexts.ToArray(), visibleInteractables.ToArray(), OnHintButtonClicked);
        _visibleButtonIndices = visibleIndices;
        _visibleButtonInteractables = visibleInteractables;
    }

    private List<int> _visibleButtonIndices = new List<int>();
    private List<bool> _visibleButtonInteractables = new List<bool>();

    private void OnHintButtonClicked(int visibleIndex)
    {
        if (visibleIndex < 0 || visibleIndex >= _visibleButtonIndices.Count)
            return;
        if (!_visibleButtonInteractables[visibleIndex])
            return;
        int actualIndex = _visibleButtonIndices[visibleIndex];
        OnPlayerExecute(actualIndex);
    }
}
