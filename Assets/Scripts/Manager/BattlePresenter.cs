using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 战斗表现层总控（P0 技能释放流程）
/// BattleManager 技能结算完成后调用 PlaySkillSequence 播放演出序列，回调后再推进回合。
/// 演出完全由角色自身 Spine 动画驱动（通过 AnimationController 的组合动画播放）：
///   走路 → Walk 组合 + 位移，攻击 → Attack 组合，受击 → Hurt 组合，死亡 → Die/Dead 组合。
/// 技能由 skill 表 movekind 字段决定演出类型：
///   1 = 走到选中目标中心后播攻击；0 = 原地播攻击（如增益/AOE）。
/// 配置了对应组合才播放；未配置则跳过该段（不播额外动作），保证无该动画的角色也能正常演出。
/// </summary>
public class BattlePresenter : MonoBehaviour
{
    public static BattlePresenter Instance { get; private set; }

    /// <summary> 演出播放中（输入抑制用） </summary>
    public bool IsPlaying { get; private set; }

    /// <summary> 死亡演出是否进行中（结算返回场景前需等待） </summary>
    public bool IsDeathPlaying { get; private set; }
    private int _deathCount;

    // 本次攻击动画总时长 / 打击时刻（用于收招补齐）
    private float _lastAttackDur;
    private float _hitMomentTime;

    /// <summary> 序列开始/结束（UI 订阅做输入抑制） </summary>
    public event Action OnSequenceBegin;
    public event Action OnSequenceEnd;

    // 演出时长（P0 先常量，后续可配表驱动）
    private const int BattlePrefabType = 2; // 对应 animationstate 表 prefabtype：2=战斗预制体
    private const float StartPause = 0.10f;          // 锁定反馈后停顿
    private const float WalkDuration = 0.25f;        // 走向目标时长（未取到走路动画时长时的兜底）
    private const float ReturnDuration = 0.25f;      // 归位时长（未取到走路动画时长时的兜底）
    private const float ApproachDistance = 0.6f;     // 走向目标时停在目标前方（靠近行动者一侧）的间距
    private const float HitInterval = 0.08f;         // 多目标受击反馈间隔
    private const float ComedyDelay = 0.15f;         // 喜剧延迟：受击后停顿再飘字
    private const float FloatTextStagger = 0.05f;    // 多目标飘字间隔
    private const float AttackFallbackDuration = 0.5f; // 未配置攻击组合时的兜底等待
    /// <summary> 打击时刻占攻击动画的比例：在此刻结算命中并播受击（半区前=抬手，之后=收招） </summary>
    [SerializeField] private float attackHitRatio = 0.5f;
    private const float KnockbackDistance = 0.30f;     // 无受击动画时的后仰位移兜底
    private const float KnockbackDuration = 0.14f;     // 后仰位移时长

    private BattleManager _battleManager;
    private BattleStageManager _stage;

    /// <summary> 战斗已登记动画（person, prefabtype=2, statename），懒构建自 cfg.animationstate 表 </summary>
    private readonly HashSet<(int, int, string)> _battleAnimSet = new HashSet<(int, int, string)>();
    private bool _animSetBuilt;

    /// <summary> 二倍速开关（战斗面板按钮切换，加速动画/等待/位移） </summary>
    public bool DoubleSpeed { get; private set; }

    /// <summary> 行动者原始缩放（归位时恢复朝向） </summary>
    private Vector3 _actorOrigScale;

    /// <summary> 当前演出速度倍率（正常 1 / 二倍速 2） </summary>
    private float SpeedScale => DoubleSpeed ? 2f : 1f;

    private void Awake()
    {
        Instance = this;
        _stage = BattleStageManager.Instance;
        _battleManager = ManagerRegistry.Get<BattleManager>();
        if (_battleManager != null)
            _battleManager.OnUnitDeath += OnUnitDeath;
    }

    private void OnDestroy()
    {
        if (_battleManager != null)
            _battleManager.OnUnitDeath -= OnUnitDeath;
        if (Instance == this) Instance = null;
    }

    /// <summary> 切换正常/二倍速（战斗面板按钮调用），并把速度应用到场上所有角色动画 </summary>
    public void SetDoubleSpeed(bool on)
    {
        if (DoubleSpeed == on) return;
        DoubleSpeed = on;
        ApplySpeedScaleToUnits();
    }

    private void ApplySpeedScaleToUnits()
    {
        if (_battleManager == null) return;
        float speed = SpeedScale;
        foreach (var unit in _battleManager.AllUnits)
        {
            var go = GetUnitGo(unit);
            var ctrl = GetAnimCtrl(go);
            if (ctrl == null || !ctrl.IsInitialized) continue;
            foreach (var layerName in ctrl.GetLayerNames())
                ctrl.SetAnimationSpeed(layerName, speed);
        }
    }

    /// <summary>
    /// 播放技能释放序列（BattleManager 在技能结算后、回合推进前调用）
    /// </summary>
    public void PlaySkillSequence(BattleUnit actor, int skillId, List<BattleUnit> targets,
        Dictionary<BattleUnit, int> beforeHp, Action onComplete)
    {
        if (actor == null)
        {
            onComplete?.Invoke();
            return;
        }
        StartCoroutine(SkillSequenceRoutine(actor, skillId, targets, beforeHp, onComplete));
    }

    private IEnumerator SkillSequenceRoutine(BattleUnit actor, int skillId, List<BattleUnit> targets,
        Dictionary<BattleUnit, int> beforeHp, Action onComplete)
    {
        IsPlaying = true;
        OnSequenceBegin?.Invoke();
        try
        {
            // 技能信息（判断治疗/飘字 + 演出方式）
            bool isHeal = false;
            int moveKind = 0;
            if (skillId > 0 && _battleManager != null)
            {
                var cfg = _battleManager.GetTables()?.TbSkill.GetOrDefault(skillId);
                if (cfg != null)
                {
                    if ((SkillType)cfg.Skilltype == SkillType.Heal)
                        isHeal = true;
                    moveKind = cfg.Movekind;
                }
            }
            else if (skillId <= 0)
            {
                // 普攻（基础功能，不在技能表）：默认走到目标中心
                moveKind = 1;
            }

            var actorGo = GetUnitGo(actor);
            var actorAnim = GetAnimCtrl(actorGo);

            var validTargets = new List<BattleUnit>();
            if (targets != null)
            {
                foreach (var t in targets)
                {
                    if (t != null) validTargets.Add(t);
                }
            }
            // 多目标按格子顺序（左上→右下，槽位升序）依次反馈
            validTargets.Sort((a, b) => a.SlotIndex.CompareTo(b.SlotIndex));

            // ── [0.00s] 锁定反馈后停顿 0.1s ──
            yield return W(StartPause);

            // ── [0.10s] 转身：朝向目标（翻转 Sprite X）──
            if (actorGo != null && validTargets.Count > 0)
            {
                var firstGo = GetUnitGo(validTargets[0]);
                if (firstGo != null)
                {
                    var visualT = GetVisualTransform(actorGo);
                    _actorOrigScale = visualT != null ? visualT.localScale : Vector3.one;
                    FaceTarget(actorGo, firstGo.transform.position);
                }
            }

            // ── [0.15s] 走向选中目标前方（movekind=1：走路动画 + 位移；0：原地）──
            if (moveKind == 1 && actorGo != null && validTargets.Count > 0)
            {
                var firstGo = GetUnitGo(validTargets[0]);
                if (firstGo != null)
                {
                    Vector3 approachPos = ComputeApproachPos(actorGo.transform.position, firstGo.transform.position);
                    float walkTime = HasBattleAnim(actor, CompositionName.Walk)
                        ? PlayLoop(actorAnim, CompositionName.Walk) : 0f;
                    if (walkTime <= 0f) walkTime = WalkDuration;
                    yield return MoveRoutine(actorGo, approachPos, walkTime);
                    yield return W(0.05f);
                    PlayIdle(actor, actorAnim);
                }
            }

            // ── 攻击：按技能配置的动画组合播放一次（animname；未配置回退默认 attack）──
            if (actorGo != null)
            {
                var attackComp = ResolveAttackAnimation(skillId, actor.PersonId, actorAnim);
                _lastAttackDur = attackComp.HasValue ? PlayOnceNonLoop(actorAnim, attackComp.Value) : 0f;
                if (_lastAttackDur <= 0f) _lastAttackDur = AttackFallbackDuration;
                // 只播到"打击时刻"，之后立刻结算命中 + 播受击（不再等整段动画播完）
                float ration = Mathf.Clamp(attackHitRatio, 0.05f, 0.95f);
                _hitMomentTime = _lastAttackDur * ration;
                yield return W(_hitMomentTime);
            }

            // ── 命中结算：打击时刻已过，真正扣血/回血（支持 skill.Hitdelay 延迟出伤）──
            if (_battleManager != null)
            {
                float hitDelay = 0f;
                if (skillId > 0)
                {
                    var cfg = _battleManager.GetTables()?.TbSkill.GetOrDefault(skillId);
                    if (cfg != null)
                        hitDelay = cfg.Hitdelay;
                }

                if (hitDelay > 0f)
                    yield return W(hitDelay);

                _battleManager.ResolvePendingHits();
            }

            // ── 受击反馈：所有目标【同时】播（多目标不再串行拖延），只等最长的一个 ──
            float hurtMax = 0f;
            foreach (var target in validTargets)
            {
                var targetGo = GetUnitGo(target);
                if (targetGo == null) continue;
                var targetAnim = GetAnimCtrl(targetGo);

                if (targetAnim != null && HasBattleAnim(target, CompositionName.Hurt))
                {
                    float hurtDur = PlayOnceNonLoop(targetAnim, CompositionName.Hurt);
                    if (hurtDur > hurtMax) hurtMax = hurtDur;
                }
                else
                {
                    // 无受击动画：位移后仰兜底
                    StartCoroutine(KnockbackRoutine(targetGo, actorGo, KnockbackDistance, KnockbackDuration));
                    if (KnockbackDuration > hurtMax) hurtMax = KnockbackDuration;
                }
            }
            if (hurtMax > 0f)
            {
                yield return W(hurtMax);
                foreach (var target in validTargets)
                {
                    var g = GetUnitGo(target);
                    if (g != null) PlayIdle(target, GetAnimCtrl(g));
                }
            }

            // ── 收招：把攻击动画剩下的部分播完（保持出招姿势自然衔接） ──
            float restTime = _lastAttackDur - _hitMomentTime;
            if (restTime > 0f)
                yield return W(restTime);

            // ── [1.02s] 喜剧延迟：受击演完停顿一拍，再飘字（WoL 滑稽感关键）──
            yield return W(ComedyDelay);

            // ── 伤害飘字 ──
            foreach (var target in validTargets)
            {
                var targetGo = GetUnitGo(target);
                if (targetGo == null) continue;

                int before = beforeHp != null && beforeHp.TryGetValue(target, out int b) ? b : target.Stats.Hp;
                int delta = target.Stats.Hp - before;

                if (delta < 0)
                {
                    DamageFx.SpawnFloatText(targetGo.transform, delta.ToString(), FloatTextType.Damage);
                }
                else if (delta > 0)
                {
                    DamageFx.SpawnFloatText(targetGo.transform, "+" + delta, FloatTextType.Heal);
                }
                else if (isHeal)
                {
                    DamageFx.SpawnFloatText(targetGo.transform, "恢复", FloatTextType.Heal);
                }

                yield return W(FloatTextStagger);
            }

            // ── 归位：走回站位（movekind=1）+ 恢复朝向 ──
            if (actorGo != null)
            {
                if (moveKind == 1)
                {
                    var slot = _stage != null ? _stage.GetSlot(actor.IsPlayerSide, actor.SlotIndex) : null;
                    if (slot != null)
                    {
                        // 转身朝回程方向，再走回站位
                        FaceTarget(actorGo, slot.position);
                        float walkTime = HasBattleAnim(actor, CompositionName.Walk)
                            ? PlayLoop(actorAnim, CompositionName.Walk) : 0f;
                        if (walkTime <= 0f) walkTime = ReturnDuration;
                        yield return MoveRoutine(actorGo, slot.position, walkTime);
                    }
                }

                var visualT = GetVisualTransform(actorGo);
                if (visualT != null) visualT.localScale = _actorOrigScale;
                PlayIdle(actor, actorAnim);
            }
        }
        finally
        {
            IsPlaying = false;
            OnSequenceEnd?.Invoke();
            // 回调必须保证执行：序列中途异常时也要走阵亡结算/回合推进，否则战斗会永久卡死
            onComplete?.Invoke();
        }
    }

    #region 死亡演出

    private void OnUnitDeath(BattleUnit killer, BattleUnit unit)
    {
        StartCoroutine(DeathRoutine(unit));
    }

    private IEnumerator DeathRoutine(BattleUnit unit)
    {
        var go = GetUnitGo(unit);
        if (go == null) yield break;

        _deathCount++;
        IsDeathPlaying = true;
        try
        {
            // 等当前演出序列结束（攻击方飘字/归位完成）再倒地，避免死亡演出抢跑
            // 加超时保护：自己打自己致死时，行动者的死亡会打断序列，IsPlaying 可能永远为 true
            float seqGuard = 0f;
            while (IsPlaying && seqGuard < 3f)
            {
                seqGuard += Time.deltaTime;
                yield return null;
            }
            if (IsPlaying)
                Debug.LogWarning("[BattlePresenter] 死亡演出等待序列结束超时(3s)，强制继续");

            // 再等一小拍让飘字亮稳
            yield return new WaitForSeconds(0.2f);

            var anim = GetAnimCtrl(go);
            bool died = false;
            if (anim != null && HasBattleAnim(unit, CompositionName.Die))
            {
                // 死亡动画循环播放：尸首留在场上，不再播一次就隐藏
                float dieDur = PlayLoop(anim, CompositionName.Die);
                if (dieDur > 0f)
                    died = true;
            }
            else if (anim != null && HasBattleAnim(unit, CompositionName.Dead))
            {
                float dieDur = PlayLoop(anim, CompositionName.Dead);
                if (dieDur > 0f)
                    died = true;
            }

            if (!died)
            {
                // 兜底：无可播放死亡动画时使用旧旋转 + 下坠
                float dur = 0.45f;
                float t = 0f;
                var startRot = go.transform.rotation;
                var startPos = go.transform.position;
                float dir = unit.IsPlayerSide ? -1f : 1f;

                while (t < dur)
                {
                    t += Time.deltaTime;
                    float k = t / dur;
                    go.transform.rotation = startRot * Quaternion.Euler(0f, 0f, dir * 85f * k);
                    go.transform.position = startPos + Vector3.down * (0.25f * k);
                    yield return null;
                }
            }
            // 不隐藏：死亡后尸体保留在场上（有死亡动画时循环播放）
        }
        finally
        {
            _deathCount--;
            if (_deathCount <= 0)
            {
                _deathCount = 0;
                IsDeathPlaying = false;
            }
        }
    }

    #endregion

    #region 工具

    /// <summary> 加速感知的等待（二倍速时所有停顿/动画等待同步缩短） </summary>
    private WaitForSeconds W(float seconds)
    {
        return new WaitForSeconds(seconds / SpeedScale);
    }

    private GameObject GetUnitGo(BattleUnit unit)
    {
        if (unit == null || _stage == null) return null;
        return _stage.GetUnitObject(unit.IsPlayerSide, unit.SlotIndex);
    }

    private static AnimationController GetAnimCtrl(GameObject go)
    {
        if (go == null) return null;
        return go.GetComponentInChildren<AnimationController>(true);
    }

    /// <summary>
    /// 解析技能释放时使用的攻击动画组合：
    /// 1. skill 表 animname 配置的组合名（且该角色 AnimationConfig 中存在）→ 用之（技能粒度配置）；
    /// 2. 为空/不可解析/角色无该组合 → 回退默认 Attack（仍需角色在 animationstate 表登记）。
    /// 返回 null 表示无可用攻击动画（不播）。
    /// </summary>
    private CompositionName? ResolveAttackAnimation(int skillId, int personId, AnimationController ctrl)
    {
        // 普攻（skillId<=0，基础功能，不在技能表）：直接播放默认 Attack 组合
        if (skillId <= 0)
        {
            if (ctrl != null && ctrl.Config != null && ctrl.Config.HasComposition(CompositionName.Attack))
                return CompositionName.Attack;
            return null;
        }

        string configured = null;
        if (_battleManager != null)
        {
            var cfg = _battleManager.GetTables()?.TbSkill.GetOrDefault(skillId);
            if (cfg != null)
                configured = cfg.Animname;
        }

        if (!string.IsNullOrEmpty(configured)
            && Enum.TryParse<CompositionName>(configured, true, out var cn)
            && ctrl != null && ctrl.Config != null && ctrl.Config.HasComposition(cn))
        {
            return cn;
        }

        if (HasBattleAnim(personId, CompositionName.Attack))
            return CompositionName.Attack;

        return null;
    }

    /// <summary>
    /// 该角色在战斗（prefabtype=2）中是否登记了该动画组合（来自 cfg.animationstate 表）。
    /// 未登记则不播放该流程段动画（可视为"没配就什么都不播"）。
    /// </summary>
    private bool HasBattleAnim(BattleUnit unit, CompositionName composition)
    {
        if (unit == null) return false;
        return HasBattleAnim(unit.PersonId, composition);
    }

    private bool HasBattleAnim(int personId, CompositionName composition)
    {
        EnsureAnimSet();
        return _battleAnimSet.Contains((personId, BattlePrefabType, composition.ToString().ToLowerInvariant()));
    }

    /// <summary> 懒构建战斗动画登记集合（读 cfg.animationstate 表 prefabtype=2 的记录，状态名统一小写） </summary>
    private void EnsureAnimSet()
    {
        if (_animSetBuilt) return;

        var tables = _battleManager != null ? _battleManager.GetTables() : null;
        if (tables == null) return; // 表未加载，不置位以便后续重试

        _animSetBuilt = true;

        foreach (var item in tables.TbAnimationstate.DataList)
        {
            if (item.Prefabtype != BattlePrefabType) continue;
            if (string.IsNullOrEmpty(item.Statename)) continue;
            _battleAnimSet.Add((item.Personid, item.Prefabtype, item.Statename.ToLowerInvariant()));
        }
    }

    /// <summary> 循环播放组合（用于走路等持续动作），返回时长 </summary>
    private static float PlayLoop(AnimationController ctrl, CompositionName composition)
    {
        return PlayInternal(ctrl, composition, false);
    }

    /// <summary> 一次性播放组合（非循环，用于攻击/受击/死亡），返回时长 </summary>
    private static float PlayOnceNonLoop(AnimationController ctrl, CompositionName composition)
    {
        return PlayInternal(ctrl, composition, true);
    }

    private static float PlayInternal(AnimationController ctrl, CompositionName composition, bool nonLoop)
    {
        if (ctrl == null || ctrl.Config == null) return 0f;
        if (!ctrl.Config.HasComposition(composition)) return 0f;

        if (nonLoop)
            ctrl.PlayCompositionOnce(composition);
        else
            ctrl.PlayComposition(composition);

        return GetCompositionDuration(ctrl, composition);
    }

    private static float GetCompositionDuration(AnimationController ctrl, CompositionName composition)
    {
        if (ctrl == null || ctrl.Config == null) return 0f;
        var comp = ctrl.Config.GetComposition(composition);
        float dur = 0f;
        if (comp != null && comp.layers != null)
        {
            foreach (var cl in comp.layers)
            {
                if (cl == null) continue;
                var clip = ctrl.Config.GetClip(cl.clipName);
                if (clip != null && clip.animation != null && clip.animation.Animation != null
                    && clip.animation.Animation.Duration > dur)
                {
                    dur = clip.animation.Animation.Duration;
                }
            }
        }
        return dur;
    }

    /// <summary> 计算走向目标时的落点：目标前方留间距（靠近行动者一侧，不贴脸） </summary>
    private static Vector3 ComputeApproachPos(Vector3 from, Vector3 target)
    {
        Vector3 dir = from - target;
        if (dir.sqrMagnitude < 0.0001f)
            dir = Vector3.left;
        return target + dir.normalized * ApproachDistance;
    }

    /// <summary> 播回默认待机（角色登记了战斗 idle 且配置了 Idle 组合才回切） </summary>
    private static void PlayIdle(BattleUnit unit, AnimationController ctrl)
    {
        if (unit == null) return;
        PlayIdle(unit.PersonId, ctrl);
    }

    private static void PlayIdle(int personId, AnimationController ctrl)
    {
        if (ctrl == null || ctrl.Config == null) return;
        if (!ctrl.Config.HasComposition(CompositionName.Idle)) return;
        // 未登记 id 不强制回 idle（保持当前姿态）；登记了才播
        var presenter = Instance;
        if (presenter != null && !presenter.HasBattleAnim(personId, CompositionName.Idle)) return;
        ctrl.PlayComposition(CompositionName.Idle);
    }

    /// <summary> 受击后仰：向远离攻击者的方向推出再弹回（Sin 曲线，一段完成去+回），无受击动画时的兜底 </summary>
    private IEnumerator KnockbackRoutine(GameObject go, GameObject attackerGo, float distance, float duration)
    {
        if (go == null) yield break;

        Vector3 origin = go.transform.position;
        Vector3 dir = Vector3.right;
        if (attackerGo != null)
        {
            Vector3 d = origin - attackerGo.transform.position;
            if (d.sqrMagnitude > 0.0001f) dir = d.normalized;
        }

        Vector3 pushed = origin + dir * distance;
        float dur = duration / SpeedScale;
        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / dur);
            go.transform.position = Vector3.Lerp(origin, pushed, Mathf.Sin(k * Mathf.PI));
            yield return null;
        }
        if (go != null) go.transform.position = origin;
    }

    /// <summary> 朝向目标：翻转视觉层（Visual 子节点）X。敌人 Visual 生成时已镜像 -1，此处叠加翻转保证朝向正确 </summary>
    private static void FaceTarget(GameObject go, Vector3 targetPos)
    {
        if (go == null) return;
        var t = GetVisualTransform(go);
        if (t == null) return;
        Vector3 scale = t.localScale;
        float faceRight = targetPos.x >= go.transform.position.x ? 1f : -1f;
        scale.x = Mathf.Abs(scale.x) * faceRight;
        t.localScale = scale;
    }

    /// <summary> 单位视觉层节点（Spine 镜像翻转在 Visual 子节点上，敌人初始已翻转 -1；无则回退根节点） </summary>
    private static Transform GetVisualTransform(GameObject go)
    {
        if (go == null) return null;
        var visual = FindVisualRecursive(go.transform);
        return visual != null ? visual : go.transform;
    }

    private static Transform FindVisualRecursive(Transform parent)
    {
        if (parent == null) return null;
        if (parent.name == "Visual") return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            var found = FindVisualRecursive(parent.GetChild(i));
            if (found != null) return found;
        }
        return null;
    }

    /// <summary> 平滑位移（SmoothStep 缓动，二倍速时同步加速） </summary>
    private IEnumerator MoveRoutine(GameObject go, Vector3 target, float duration)
    {
        if (go == null) yield break;
        var start = go.transform.position;
        float d = duration / SpeedScale;
        float t = 0f;
        while (t < d)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / d);
            go.transform.position = Vector3.Lerp(start, target, Mathf.SmoothStep(0f, 1f, k));
            yield return null;
        }
        if (go != null) go.transform.position = target;
    }

    #endregion
}