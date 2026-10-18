using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum ConditionChangeType
{
    Item = 0,
    Task = 1,
    Perk = 2,
    Battle = 3,
    Dialog = 4
}

public class ConditionSystem
{
    private static ConditionSystem _instance;
    public static ConditionSystem Instance => _instance ??= new ConditionSystem();
    public static bool HasInstance => _instance != null;

    private readonly Dictionary<int, bool> _cache = new Dictionary<int, bool>();

    // 覆盖层：由交互(SetCondition)等强制设置。优先级最高——存在覆盖时一律以覆盖值为准，
    // 数据变化触发的 Notify 重算对该条件直接跳过。
    private readonly Dictionary<int, bool> _overrides = new Dictionary<int, bool>();

    private readonly Dictionary<ConditionChangeType, HashSet<int>> _fieldIndex
        = new Dictionary<ConditionChangeType, HashSet<int>>();

    public List<int> BuildMetConditionIds()
    {
        var result = new List<int>();
        foreach (var kv in _cache)
        {
            if (kv.Value) result.Add(kv.Key);
        }
        return result;
    }

    public void RestoreMetConditions(List<int> metIds)
    {
        if (metIds == null || metIds.Count == 0) return;
        foreach (var id in metIds)
            _cache[id] = true;
        Debug.Log($"[ConditionSystem] 恢复条件状态: {metIds.Count} 条");
    }

    public event Action<int, bool> OnConditionChanged;

    public void Initialize()
    {
        var tables = ManagerRegistry.GetTables<cfg.Tables>();
        if (tables == null)
        {
            Debug.LogError("[ConditionSystem] Tables 未初始化");
            return;
        }

        BuildIndex(tables);

        _cache.Clear();
        _overrides.Clear();
        foreach (var cond in tables.TbCondition.DataList)
        {
            _cache[cond.Id] = EvaluateCondition(cond);
        }

        SubscribeEvents();
    }

    private void BuildIndex(cfg.Tables tables)
    {
        _fieldIndex.Clear();

        foreach (ConditionChangeType ct in Enum.GetValues(typeof(ConditionChangeType)))
            _fieldIndex[ct] = new HashSet<int>();

        foreach (var cond in tables.TbCondition.DataList)
        {
            for (int i = 0; i < cond.ComposeType.Count && i <= (int)ConditionChangeType.Dialog; i++)
            {
                var ops = cond.ComposeType[i];
                if (ops != null && ops.Count > 0 && ops[0] != 0)
                    _fieldIndex[(ConditionChangeType)i].Add(cond.Id);
            }
        }
    }

    private void SubscribeEvents()
    {
        var dm = ManagerRegistry.Get<DialogManager>();
        if (dm != null)
            dm.OnDialogEnded += () => Notify(ConditionChangeType.Dialog);
    }

    public bool IsConditionMet(int conditionId)
    {
        // 覆盖层优先：被强制设置过的条件不再按条件表推算
        if (_overrides.TryGetValue(conditionId, out bool forced))
            return forced;

        if (_cache.TryGetValue(conditionId, out bool met))
            return met;

        var tables = ManagerRegistry.GetTables<cfg.Tables>();
        var cond = tables?.TbCondition.GetOrDefault(conditionId);
        if (cond == null)
            return false;

        bool result = EvaluateCondition(cond);
        _cache[conditionId] = result;
        return result;
    }

    /// <summary>强制设置条件并**保留覆盖**（优先级高于条件表推算，Notify 重算不会冲掉它）。</summary>
    public void ForceSetCondition(int conditionId, bool met)
    {
        _overrides[conditionId] = met;
        _cache[conditionId] = met;
        OnConditionChanged?.Invoke(conditionId, met);
        Debug.Log($"[ConditionSystem] 强制设置条件 {conditionId} = {met}（覆盖，优先于条件表）");
    }

    /// <summary>清除某条件的覆盖，恢复按条件表推算（并立即重算一次）。</summary>
    public void ClearOverride(int conditionId)
    {
        if (!_overrides.Remove(conditionId)) return;

        var tables = ManagerRegistry.GetTables<cfg.Tables>();
        var cond = tables?.TbCondition.GetOrDefault(conditionId);
        bool re = cond != null ? EvaluateCondition(cond) : false;
        bool old = _cache.TryGetValue(conditionId, out bool c) && c;
        _cache[conditionId] = re;

        Debug.Log($"[ConditionSystem] 清除条件 {conditionId} 的覆盖，重算为 {re}");
        if (old != re)
            OnConditionChanged?.Invoke(conditionId, re);
    }

    /// <summary>该条件当前是否处于覆盖状态（调试/工具用）。</summary>
    public bool HasOverride(int conditionId) => _overrides.ContainsKey(conditionId);

    /// <summary>调试用：一句话说明该条件的条件表数据（物品/任务/特质/战斗/对话）。</summary>
    public string GetConditionBrief(int conditionId)
    {
        var tables = ManagerRegistry.GetTables<cfg.Tables>();
        var cond = tables?.TbCondition.GetOrDefault(conditionId);
        if (cond == null) return "(条件表无此 id)";

        var sb = new System.Text.StringBuilder();
        if (cond.HasItem != null && cond.HasItem.Count > 0)
        {
            sb.Append("物品");
            foreach (var p in cond.HasItem) sb.Append($"[{p[0]}×{p[1]}]");
            sb.Append(' ');
        }
        if (cond.TaskState != null && cond.TaskState.Count > 0)
        {
            sb.Append("任务");
            foreach (var p in cond.TaskState) sb.Append($"[{p[0]}→{p[1]}]");
            sb.Append(' ');
        }
        if (cond.HasPerkId != null && cond.HasPerkId.Count > 0)
        {
            sb.Append("特质");
            foreach (var p in cond.HasPerkId) sb.Append($"[{p}]");
            sb.Append(' ');
        }
        if (cond.BattleId != null && cond.BattleId.Count > 0)
        {
            sb.Append("战斗");
            foreach (var p in cond.BattleId) sb.Append($"[{p[0]}→{p[1]}]");
            sb.Append(' ');
        }
        if (cond.DialogFinishId != null && cond.DialogFinishId.Count > 0)
        {
            sb.Append("对话");
            foreach (var p in cond.DialogFinishId) sb.Append($"[{p}]");
            sb.Append(' ');
        }
        if (sb.Length == 0) sb.Append("(无数据限制)");
        return sb.ToString();
    }

    public List<int> GetAllConditionIds()
    {
        var tables = ManagerRegistry.GetTables<cfg.Tables>();
        return tables?.TbCondition.DataList.Select(c => c.Id).ToList() ?? new List<int>();
    }

    public string GetConditionInfo(int conditionId)
    {
        var tables = ManagerRegistry.GetTables<cfg.Tables>();
        var cond = tables?.TbCondition.GetOrDefault(conditionId);
        if (cond == null) return $"条件 {conditionId} 不存在";

        bool met = IsConditionMet(conditionId);
        string tag = _overrides.ContainsKey(conditionId) ? " <color=orange>[已覆盖]</color>" : "";
        return $"ID:{conditionId} | {(met ? "<color=green>已满足</color>" : "<color=red>未满足</color>")}{tag}";
    }

    public void Notify(ConditionChangeType changeType)
    {
        if (!_fieldIndex.TryGetValue(changeType, out var conditionIds)) return;

        var tables = ManagerRegistry.GetTables<cfg.Tables>();
        if (tables == null) return;

        foreach (int condId in conditionIds)
        {
            if (_overrides.ContainsKey(condId)) continue;   // 被覆盖的条件不重算（覆盖优先）

            var cond = tables.TbCondition.GetOrDefault(condId);
            if (cond == null) continue;

            bool oldResult = _cache.TryGetValue(condId, out bool cached) && cached;
            bool newResult = EvaluateCondition(cond);

            if (oldResult != newResult)
            {
                _cache[condId] = newResult;
                OnConditionChanged?.Invoke(condId, newResult);
            }
        }
    }

    #region 评估入口

    private bool EvaluateCondition(cfg.cfg.condition.Condition cond)
    {
        if (!EvaluateHasItemField(cond)) return false;
        if (!EvaluateTaskStateField(cond)) return false;
        if (!EvaluatePerkField(cond)) return false;
        if (!EvaluateBattleField(cond)) return false;
        if (!EvaluateDialogField(cond)) return false;
        return true;
    }

    #endregion

    #region HasItem

    private bool EvaluateHasItemField(cfg.cfg.condition.Condition cond)
    {
        var ops = GetOps(cond, 0);
        if (ops == null) return true;
        if (cond.HasItem == null || cond.HasItem.Count == 0) return true;

        foreach (var pair in cond.HasItem)
        {
            if (pair == null || pair.Count < 2) continue;
            int actual = GetItemCount(pair[0]);
            if (!CompareQuantity(actual, pair[1], ops))
                return false;
        }
        return true;
    }

    #endregion

    #region TaskState

    private bool EvaluateTaskStateField(cfg.cfg.condition.Condition cond)
    {
        var ops = GetOps(cond, 1);
        if (ops == null) return true;
        if (cond.TaskState == null || cond.TaskState.Count == 0) return true;

        return EvaluateListPairs(
            cond.TaskState,
            pair => pair != null && pair.Count >= 2 && CheckTaskProgress(pair[0], pair[1]),
            ops);
    }

    #endregion

    #region HasPerkId

    private bool EvaluatePerkField(cfg.cfg.condition.Condition cond)
    {
        var ops = GetOps(cond, 2);
        if (ops == null) return true;
        if (cond.HasPerkId == null || cond.HasPerkId.Count == 0) return true;

        return EvaluateIdList(cond.HasPerkId, id => CheckPerk(id), ops);
    }

    #endregion

    #region BattleId

    private bool EvaluateBattleField(cfg.cfg.condition.Condition cond)
    {
        var ops = GetOps(cond, 3);
        if (ops == null) return true;
        if (cond.BattleId == null || cond.BattleId.Count == 0) return true;

        return EvaluateListPairs(
            cond.BattleId,
            pair => pair != null && pair.Count >= 2 && CheckBattleResult(pair[0], pair[1]),
            ops);
    }

    #endregion

    #region DialogFinishId

    private bool EvaluateDialogField(cfg.cfg.condition.Condition cond)
    {
        var ops = GetOps(cond, 4);
        if (ops == null) return true;
        if (cond.DialogFinishId == null || cond.DialogFinishId.Count == 0) return true;

        return EvaluateIdList(cond.DialogFinishId, id => CheckDialogFinished(id), ops);
    }

    #endregion

    #region 通用评估工具

    private List<int> GetOps(cfg.cfg.condition.Condition cond, int index)
    {
        if (index >= cond.ComposeType.Count) return null;
        var ops = cond.ComposeType[index];
        if (ops == null || ops.Count == 0 || ops[0] == 0) return null;
        return ops;
    }

    private bool CompareQuantity(int actual, int threshold, List<int> ops)
    {
        bool greater = ops.Contains(1);
        bool equal = ops.Contains(2);
        bool less = ops.Contains(3);

        if (greater && equal) return actual >= threshold;
        if (less && equal) return actual <= threshold;
        if (greater) return actual > threshold;
        if (equal) return actual == threshold;
        if (less) return actual < threshold;

        return actual >= threshold;
    }

    private bool EvaluateListPairs(List<List<int>> pairs, Func<List<int>, bool> check, List<int> ops)
    {
        if (ops.Contains(5))
        {
            foreach (var pair in pairs)
            {
                if (!check(pair)) return false;
            }
            return true;
        }

        if (ops.Contains(4))
        {
            foreach (var pair in pairs)
            {
                if (check(pair)) return true;
            }
            return false;
        }

        return true;
    }

    private bool EvaluateIdList(List<int> ids, Func<int, bool> check, List<int> ops)
    {
        if (ops.Contains(5))
        {
            foreach (int id in ids)
            {
                if (!check(id)) return false;
            }
            return true;
        }

        if (ops.Contains(4))
        {
            foreach (int id in ids)
            {
                if (check(id)) return true;
            }
            return false;
        }

        return true;
    }

    #endregion

    #region 外部状态查询（可被子系统/测试覆写）

    protected virtual int GetItemCount(int itemId)
    {
        return BagManager.Instance.GetItemCount(itemId);
    }

    protected virtual bool CheckTaskProgress(int taskId, int progress)
    {
        var qm = QuestManager.Instance;
        if (qm == null) return false;

        if (progress == (int)QuestState.Completed)
            return qm.IsQuestCompleted(taskId);

        if (progress == (int)QuestState.InProgress)
            return qm.IsQuestActive(taskId);

        if (progress == (int)QuestState.Failed)
            return qm.IsQuestFailed(taskId);

        return qm.GetQuestState(taskId) == (QuestState)progress;
    }

    protected virtual bool CheckPerk(int perkId)
    {
        var stats = CharacterStatsManager.Instance.PlayerStats;
        if (stats == null) return false;
        return stats.UnlockedSkills.Contains(perkId);
    }

    protected virtual bool CheckBattleResult(int battleId, int result)
    {
        return SaveManager.Instance.IsBattleCompleted(battleId, (BattleResult)result);
    }

    protected virtual bool CheckDialogFinished(int dialogId)
    {
        return DialogManager.IsDialogFinished(dialogId);
    }

    #endregion
}
