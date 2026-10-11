using System;
using System.Collections.Generic;
using UnityEngine;

public class CharacterStatsManager
{
    private static CharacterStatsManager _instance;
    public static CharacterStatsManager Instance => _instance ??= new CharacterStatsManager();

    private const int PlayerPersonId = 1;

    private CharacterStats _playerStats;
    public CharacterStats PlayerStats => _playerStats;

    public event Action OnStatsChanged;

    /// <summary>经验获得（参数 = 本次实际获得的经验，含加成）。</summary>
    public event Action<int> OnExpGained;

    /// <summary>经验发放版本号：每次发放 +1。用于跨场景补显示"发过但还没展示"的经验提示。</summary>
    public int ExpGainVersion { get; private set; }

    /// <summary>已被界面展示过的版本号（由显示组件展示后回写）。</summary>
    public int LastShownExpVersion { get; private set; }

    /// <summary>最近一次实际获得的经验（含加成）。</summary>
    public int LastExpGain { get; private set; }

    /// <summary>是否有"已发放但还没给玩家看过"的经验提示。</summary>
    public bool HasPendingExpGain => ExpGainVersion != LastShownExpVersion;

    /// <summary>标记当前版本的经验提示已展示。</summary>
    public void MarkExpGainShown()
    {
        LastShownExpVersion = ExpGainVersion;
    }

    public void InitFromConfig()
    {
        _playerStats = new CharacterStats();

        var tables = GetTables();
        if (tables == null)
        {
            Debug.LogError("[CharacterStatsManager] Tables 未加载");
            return;
        }

        var person = tables.TbPerson.GetOrDefault(PlayerPersonId);
        if (person == null)
        {
            Debug.LogError("[CharacterStatsManager] 玩家角色配置不存在: " + PlayerPersonId);
            return;
        }

        _playerStats.Destroy = person.Destroy;
        _playerStats.Selfmatain = person.Selfmatain;
        _playerStats.Creatorwilling = person.Creatorwilling;
        _playerStats.DestroyPlus = person.DestroyPlus;
        _playerStats.SelfmatainPlus = person.SelfmatainPlus;
        _playerStats.CreatorwillingPlus = person.CreatorwillingPlus;
        _playerStats.DestroyPer = person.DestroyPer;
        _playerStats.SelfmatainPer = person.SelfmatainPer;
        _playerStats.CreatorwillingPer = person.CreatorwillingPer;
        _playerStats.SpeedBase = person.Speed;

        // 从 person 表读取默认技能
        _playerStats.UnlockedSkills.Clear();
        _playerStats.SkillLevels.Clear();
        if (person.Skills != null)
        {
            foreach (int skillId in person.Skills)
            {
                if (skillId > 0 && !_playerStats.UnlockedSkills.Contains(skillId))
                {
                    _playerStats.UnlockedSkills.Add(skillId);
                    _playerStats.SkillLevels[skillId] = 1;

                    // 仅 BuffOnly 技能在初始化时挂永久 buff
                    var skillCfg = tables.TbSkill.GetOrDefault(skillId);
                    if (skillCfg != null && (SkillType)skillCfg.Skilltype == SkillType.BuffOnly && skillCfg.Buffid > 0)
                        BuffManager.Instance.ApplyBuff(skillCfg.Buffid, fromSkill: true);
                }
            }
        }

        InitBuildValues(tables);

        _playerStats.Hp = _playerStats.FinalHpMax;

        Debug.Log($"[CharacterStatsManager] 从配置初始化: " +
            $"Destroy={_playerStats.FinalDestroy}, " +
            $"Selfmatain={_playerStats.FinalSelfmatain}, " +
            $"Creatorwilling={_playerStats.FinalCreatorwilling}, " +
            $"HpMax={_playerStats.FinalHpMax}, " +
            $"Level={_playerStats.Level}, Exp={_playerStats.Exp}, SkillPoint={_playerStats.SkillPoint}");
    }

    private void InitBuildValues(cfg.Tables tables)
    {
        foreach (var build in tables.TbBuild.DataList)
        {
            switch (build.Buildtype)
            {
                case 1:
                    if (int.TryParse(build.Buildvalue, out int expVal))
                        _playerStats.Exp = expVal;
                    break;
                case 2:
                    if (int.TryParse(build.Buildvalue, out int spVal))
                        _playerStats.SkillPoint = spVal;
                    break;
                case 3:
                    if (int.TryParse(build.Buildvalue, out int lvVal))
                        _playerStats.Level = lvVal;
                    break;
                case 1001:
                    if (float.TryParse(build.Buildvalue, out float expBonus))
                        _playerStats.ExpBonus = expBonus;
                    break;
                case 4001:
                    if (float.TryParse(build.Buildvalue, out float curBonus))
                        _playerStats.CurrencyBonus = curBonus;
                    break;
            }
        }
    }

    public void AddExp(int amount)
    {
        if (amount <= 0 || _playerStats == null) return;

        int actual = Mathf.RoundToInt(amount * _playerStats.ExpBonus);
        _playerStats.Exp += actual;
        LastExpGain = actual;
        ExpGainVersion++;

        CheckLevelUp();
        _playerStats.NotifyChanged();
        OnStatsChanged?.Invoke();
        OnExpGained?.Invoke(actual);

        Debug.Log($"[CharacterStatsManager] 获得经验: {actual}(x{_playerStats.ExpBonus}), 当前档经验={_playerStats.Exp}, 等级={_playerStats.Level}");
    }

    /// <summary>
    /// 按等级表逐档结算。等级表一行 = 一个等级：
    ///   Id         = 等级
    ///   Level      = 升到下一级所需经验
    ///   Skillpoint = 升这一级给的点数（写几给几）
    /// 经验够就扣掉该级所需经验、等级 +1，多余的结转到下一级；每级只结算一次（等级本身就是位置）。
    /// </summary>
    private void CheckLevelUp()
    {
        var tables = GetTables();
        if (tables == null || tables.TbCharacterlevel == null) return;

        bool leveledUp = false;

        while (true)
        {
            // 当前等级对应表里 Id == 等级 的那一行
            int needExp = -1;
            int skillPoint = 0;
            foreach (var row in tables.TbCharacterlevel.DataList)
            {
                if (row.Id != _playerStats.Level) continue;
                needExp = row.Level;      // 该级升级所需经验
                skillPoint = row.Skillpoint;
                break;
            }

            if (needExp < 0) break;                    // 表里没有当前等级（超出表范围）
            if (_playerStats.Exp < needExp) break;     // 经验不够，先攒着

            _playerStats.Exp -= needExp;               // 消耗该级所需经验（多余结转）
            _playerStats.Level += 1;
            _playerStats.SkillPoint += skillPoint;
            leveledUp = true;

            Debug.Log($"[CharacterStatsManager] 升级! 消耗经验{needExp} → Level={_playerStats.Level}, 技能点+{skillPoint}, 剩余经验={_playerStats.Exp}");
        }

        if (leveledUp)
        {
            _playerStats.ClampHp();
        }
    }

    public bool UnlockSkill(int skillId)
    {
        if (_playerStats == null) return false;

        var tables = GetTables();
        if (tables == null) return false;

        var skillCfg = tables.TbSkill.GetOrDefault(skillId);
        if (skillCfg == null)
        {
            Debug.LogWarning($"[CharacterStatsManager] 技能配置不存在: {skillId}");
            return false;
        }

        int currentLevel = _playerStats.SkillLevels.TryGetValue(skillId, out int lv) ? lv : 0;
        if (currentLevel > 0 || _playerStats.UnlockedSkills.Contains(skillId))
        {
            Debug.LogWarning($"[CharacterStatsManager] 技能 {skillId} 已解锁");
            return false;
        }

        if (_playerStats.SkillPoint < skillCfg.Cost)
        {
            Debug.LogWarning($"[CharacterStatsManager] 技能点不足: 需要{skillCfg.Cost}, 当前{_playerStats.SkillPoint}");
            return false;
        }

        _playerStats.SkillPoint -= skillCfg.Cost;
        _playerStats.SkillLevels[skillId] = 1;

        if (!_playerStats.UnlockedSkills.Contains(skillId))
            _playerStats.UnlockedSkills.Add(skillId);

        // 仅永久 Buff 类技能在解锁时挂到角色；战斗即时技能不在此施加
        if ((SkillType)skillCfg.Skilltype == SkillType.BuffOnly && skillCfg.Buffid > 0)
            BuffManager.Instance.ApplyBuff(skillCfg.Buffid, fromSkill: true);

        _playerStats.NotifyChanged();
        OnStatsChanged?.Invoke();

        Debug.Log($"[CharacterStatsManager] 解锁技能: {skillCfg.Name}(id={skillId}), 消耗技能点{skillCfg.Cost}");

        ConditionSystem.Instance.Notify(ConditionChangeType.Perk);
        return true;
    }

    public void ModifyBase(AttrType type, int delta)
    {
        if (_playerStats == null) return;

        int current = _playerStats.GetBase(type);
        _playerStats.SetBase(type, current + delta);

        _playerStats.NotifyChanged();
        OnStatsChanged?.Invoke();
    }

    public void ModifyPlus(AttrType type, int delta)
    {
        if (_playerStats == null) return;

        int current = _playerStats.GetPlus(type);
        _playerStats.SetPlus(type, current + delta);

        _playerStats.NotifyChanged();
        OnStatsChanged?.Invoke();
    }

    public void ModifyPer(AttrType type, int delta)
    {
        if (_playerStats == null) return;

        int current = _playerStats.GetPer(type);
        _playerStats.SetPer(type, current + delta);

        _playerStats.NotifyChanged();
        OnStatsChanged?.Invoke();
    }

    public void TakeDamage(int damage)
    {
        if (_playerStats == null || damage <= 0) return;

        _playerStats.Hp -= damage;
        _playerStats.ClampHp();
        _playerStats.NotifyChanged();
        OnStatsChanged?.Invoke();

        Debug.Log($"[CharacterStatsManager] 受到伤害: {damage}, 剩余HP={_playerStats.Hp}/{_playerStats.FinalHpMax}");
    }

    public void Heal(int amount)
    {
        if (_playerStats == null || amount <= 0) return;

        _playerStats.Hp += amount;
        _playerStats.ClampHp();
        _playerStats.NotifyChanged();
        OnStatsChanged?.Invoke();

        Debug.Log($"[CharacterStatsManager] 治疗: {amount}, 当前HP={_playerStats.Hp}/{_playerStats.FinalHpMax}");
    }

    public void ResetHp()
    {
        if (_playerStats == null) return;

        _playerStats.Hp = _playerStats.FinalHpMax;
        _playerStats.NotifyChanged();
        OnStatsChanged?.Invoke();
    }

    public CharacterStats CreateTempStats(int personId)
    {
        var tables = GetTables();
        if (tables == null) return null;

        var person = tables.TbPerson.GetOrDefault(personId);
        if (person == null) return null;

        var stats = new CharacterStats
        {
            Destroy = person.Destroy,
            Selfmatain = person.Selfmatain,
            Creatorwilling = person.Creatorwilling,
            DestroyPlus = person.DestroyPlus,
            SelfmatainPlus = person.SelfmatainPlus,
            CreatorwillingPlus = person.CreatorwillingPlus,
            DestroyPer = person.DestroyPer,
            SelfmatainPer = person.SelfmatainPer,
            CreatorwillingPer = person.CreatorwillingPer,
            SpeedBase = person.Speed
        };

        stats.Hp = stats.FinalHpMax;

        return stats;
    }

    public CharacterStats.StatsSaveData BuildSaveData()
    {
        return _playerStats?.ToSaveData();
    }

    public void RestoreFromSaveData(CharacterStats.StatsSaveData data)
    {
        if (data == null)
        {
            InitFromConfig();
            return;
        }

        if (_playerStats == null)
            _playerStats = new CharacterStats();

        _playerStats.FromSaveData(data);
        _playerStats.ClampHp();

        Debug.Log($"[CharacterStatsManager] 从存档恢复: " +
            $"Destroy={_playerStats.FinalDestroy}, " +
            $"Selfmatain={_playerStats.FinalSelfmatain}, " +
            $"Creatorwilling={_playerStats.FinalCreatorwilling}, " +
            $"Hp={_playerStats.Hp}/{_playerStats.FinalHpMax}, " +
            $"Level={_playerStats.Level}, Exp={_playerStats.Exp}");
    }

    public void Clear()
    {
        _playerStats = null;
    }

    private cfg.Tables GetTables()
    {
        if (ManagerRegistry.TryGet<DataTableManager>(out var dtm) && dtm.Tables != null)
            return dtm.Tables;
        return null;
    }
}
