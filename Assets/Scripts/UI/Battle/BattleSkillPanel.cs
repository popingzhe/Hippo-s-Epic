using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

/// <summary>
/// 战斗子面板基类
/// </summary>
public abstract class BattleSubPanel : MonoBehaviour
{
    [SerializeField] protected GameObject panelRoot;
    [SerializeField] protected Button btnClose;

    protected BattleManager _battleManager;

    /// <summary>
    /// 子面板初始化：绑定关闭按钮；panelRoot 为空时回退到自身
    /// </summary>
    protected virtual void Awake()
    {
        if (btnClose != null)
            btnClose.onClick.AddListener(Close);
        if (panelRoot == null) panelRoot = gameObject;
    }

    /// <summary>
    /// 打开面板：记录 BattleManager 引用并显示面板根节点
    /// </summary>
    public virtual void Open(BattleManager battleManager)
    {
        _battleManager = battleManager;
        if (panelRoot != null) panelRoot.SetActive(true);
    }

    /// <summary>
    /// 关闭面板：隐藏面板根节点
    /// </summary>
    public virtual void Close()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }
}

/// <summary>
/// 技能子面板（暴力行为）
/// 显示当前单位可用技能列表
/// </summary>
public class BattleSkillPanel : BattleSubPanel
{
    [Header("技能列表")]
    [SerializeField] private Transform skillContainer;
    [SerializeField] private GameObject skillItemPrefab;

    [Header("技能图标")]
    [Tooltip("技能图标加载地址格式，{0} 为技能表 Icon 字段（对应 skill_{0}.png）")]
    [SerializeField] private string skillIconPathFormat = "Assets/Art/Sprites/UI/battle/skill_icon/skill_{0}";

    [Tooltip("缺省图标 ID：表里 Icon<=0 或对应图标不存在时，回退到该图标；<=0 表示保留模板自带图标")]
    [SerializeField] private int defaultSkillIconId = 1;

    private Action<int> _onSkillSelected;
    private Func<int, bool> _canUseOnTarget;
    private BattleUnit _unit;  // 打开面板时的当前单位（由上层事件缓存传入，不主动读取）

    /// <summary> 已创建的技能项（非冷却项，供目标有效性重算时刷新置灰与 interactable） </summary>
    private readonly List<(int skillId, Button btn, Image bgImg, TextMeshProUGUI nameTmp, TextMeshProUGUI detailTmp)> _items =
        new List<(int, Button, Image, TextMeshProUGUI, TextMeshProUGUI)>();

    /// <summary>
    /// 初始化兜底：skillItemPrefab 未拖引用时，取容器内第一个子对象作为模板；
    /// 若模板是场景实例则隐藏，避免其残留在列表里（Project 资产不隐藏）
    /// </summary>
    protected override void Awake()
    {
        base.Awake();
        if (skillItemPrefab == null && skillContainer != null && skillContainer.childCount > 0)
            skillItemPrefab = skillContainer.GetChild(0).gameObject;

        if (skillItemPrefab != null && skillItemPrefab.scene.IsValid())
            skillItemPrefab.SetActive(false);
    }

    /// <summary>战斗动作分类过滤：null=不过滤 0=暴力行为 1=咄咄逼人（对应 skill 表 actiontype）</summary>
    private int? _actionFilter;

    public void Open(BattleManager battleManager, BattleUnit unit, Action<int> onSkillSelected,
                     Func<int, bool> canUseOnTarget = null, int? actionFilter = null)
    {
        _actionFilter = actionFilter;
        Debug.Log($"[BattleSkillPanel] Open: unit={unit} 面板activeSelf={panelRoot != null && panelRoot.activeSelf} " +
                  $"canUseOnTarget={(canUseOnTarget != null ? "有" : "无")}");
        base.Open(battleManager);
        _unit = unit;
        _onSkillSelected = onSkillSelected;
        _canUseOnTarget = canUseOnTarget;
        RefreshSkillList();
    }

    public override void Close()
    {
        Debug.Log($"[BattleSkillPanel] Close: 面板activeSelf={panelRoot != null && panelRoot.activeSelf} " +
                  $"container={skillContainer} childCount={skillContainer?.childCount}");
        base.Close();
    }

    /// <summary>
    /// 目标切换/目标状态变化后刷新置灰（事件驱动，由上层订阅触发；只重算 alpha，不重建列表）
    /// </summary>
    public void RefreshInvalidState()
    {
        if (_battleManager == null || _unit == null) return;
        if (panelRoot != null && !panelRoot.activeSelf) return;

        int remainingAp = _unit.CurrentActionPoints;

        foreach (var (skillId, btn, bgImg, nameTmp, detailTmp) in _items)
        {
            bool invalid = IsInvalidForTarget(skillId);

            // 行动点不足同样置灰（技能 cost / 普攻 1 点）
            if (!invalid)
            {
                if (skillId == 0)
                    invalid = remainingAp < 1;
                else
                {
                    var cfg = _battleManager.GetTables()?.TbSkill.GetOrDefault(skillId);
                    invalid = cfg != null && remainingAp < cfg.Cost;
                }
            }

            float a = invalid ? 0.45f : 1.00f;
            if (bgImg != null) bgImg.color = new Color(bgImg.color.r, bgImg.color.g, bgImg.color.b, a);
            if (nameTmp != null) nameTmp.color = new Color(nameTmp.color.r, nameTmp.color.g, nameTmp.color.b, a);
            if (detailTmp != null) detailTmp.color = new Color(detailTmp.color.r, detailTmp.color.g, detailTmp.color.b, a);
        }
    }

    private void RefreshSkillList()
    {
        bool prefabIsAsset = skillItemPrefab != null && !skillItemPrefab.gameObject.scene.IsValid();
        Debug.Log($"[BattleSkillPanel] RefreshSkillList: " +
                  $"container={skillContainer} " +
                  $"prefab={skillItemPrefab} " +
                  $"prefabIsAsset={prefabIsAsset} (false=引用的是场景实例) " +
                  $"manager={_battleManager} unit={_unit}");
        if (skillContainer == null || skillItemPrefab == null) return;
        if (_battleManager == null || _unit == null) return;

        // 清空旧列表（保留模板本身，模板可能是容器内被隐藏的场景对象）
        Debug.Log($"[BattleSkillPanel] 清空前: container={skillContainer.name} childCount={skillContainer.childCount} " +
                  $"模板引用有效={skillItemPrefab != null} 模板transform={skillItemPrefab?.transform}");
        foreach (Transform child in skillContainer)
        {
            if (skillItemPrefab != null && child == skillItemPrefab.transform)
            {
                Debug.Log($"[BattleSkillPanel] 清理跳过模板: {child.name}");
                continue;
            }
            Debug.Log($"[BattleSkillPanel] 清理销毁: {child.name} activeInHierarchy={child.gameObject.activeInHierarchy}");
            child.gameObject.SetActive(false);  // 先隐藏，避免销毁延迟期间遮挡新项
            Destroy(child.gameObject);
        }
        _items.Clear();

        var unit = _unit;
        var tables = _battleManager.GetTables();

        // 行动点信息（置灰判断依据：cost > 剩余行动点）
        int remainingAp = unit.CurrentActionPoints;

        // 添加普通攻击（固定消耗 1 行动点）；咄咄逼人（actiontype=1）不显示普通攻击
        if (!_actionFilter.HasValue || _actionFilter.Value == 0)
            AddSkillItem(0, "普通攻击", false, IsInvalidForTarget(0) || remainingAp < 1,
                "普通攻击：基础打击，消耗 1 行动点");

        // 添加技能
        foreach (int skillId in unit.AvailableSkills)
        {
            bool onCooldown = unit.IsSkillOnCooldown(skillId);
            var skillCfg = tables?.TbSkill.GetOrDefault(skillId);
            if (_actionFilter.HasValue && skillCfg != null && skillCfg.Actiontype != _actionFilter.Value)
                continue;
            string name = skillCfg != null ? skillCfg.Name : $"技能{skillId}";

            if (onCooldown)
            {
                unit.SkillCooldowns.TryGetValue(skillId, out int cd);
                name += $" (冷却{cd})";
            }

            string intro = skillCfg != null && !string.IsNullOrEmpty(skillCfg.Comment)
                ? skillCfg.Comment
                : $"技能：{name}";

            bool apNotEnough = skillCfg != null && remainingAp < skillCfg.Cost;
            AddSkillItem(skillId, name, onCooldown,
                IsInvalidForTarget(skillId) || apNotEnough, intro, skillCfg?.Icon ?? 0);
        }

        // 重置滚动位置到顶部，避免关闭再打开时 Content 残留上次滚动位置导致技能项错位/点不到
        var scrollRect = skillContainer.GetComponentInParent<ScrollRect>();
        if (scrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 1f;
            scrollRect.horizontalNormalizedPosition = 0f;
        }
    }

    /// <summary> 当前选中目标对该技能是否无效（用于置灰提示，点击时由上层给出提示） </summary>
    private bool IsInvalidForTarget(int skillId)
    {
        return _canUseOnTarget != null && !_canUseOnTarget(skillId);
    }

    private void AddSkillItem(int skillId, string name, bool disabled, bool invalidForTarget = false, string intro = null, int iconId = 0)
    {
        var go = Instantiate(skillItemPrefab, skillContainer);
        go.SetActive(true);

        // 模板组件可能是 disabled，克隆后需显式启用，否则 SetActive 不会激活 disable 的组件
        var bg = go.transform.Find("bg");
        var icon = go.transform.Find("icon");
        var nameGo = go.transform.Find("skill_name");
        var detailGo = go.transform.Find("skill_detail");

        foreach (var g in new[] { bg, icon, nameGo, detailGo })
        {
            if (g == null) continue;
            foreach (var c in g.GetComponents<Behaviour>())
                c.enabled = true;
        }

        var bgImg = bg != null ? bg.GetComponent<Image>() : null;
        var btn = bg != null ? bg.GetComponent<Button>() : null;
        var nameTmp = nameGo != null ? nameGo.GetComponent<TextMeshProUGUI>() : null;
        var detailTmp = detailGo != null ? detailGo.GetComponent<TextMeshProUGUI>() : null;
        var iconImg = icon != null ? icon.GetComponent<Image>() : null;

        if (nameTmp != null) nameTmp.text = name;
        if (detailTmp != null) detailTmp.text = intro ?? name;
        if (iconImg != null)
        {
            // 图标始终显示：无 Icon 配置时保留模板自带图标，有配置时动态覆盖
            iconImg.gameObject.SetActive(true);
            if (iconId > 0)
                LoadIcon(iconImg, iconId);
        }

        // 文本/图标设为不拦截点击，确保点击穿透到 bg 的 Button（否则命中文字区域时 onClick 不触发）
        if (iconImg != null) iconImg.raycastTarget = false;
        if (nameTmp != null) nameTmp.raycastTarget = false;
        if (detailTmp != null) detailTmp.raycastTarget = false;

        if (btn != null)
        {
            // 所有技能项均可点击（含冷却/置灰），点击后由 OnSkillSelected 判断并提示不可用原因
            btn.interactable = true;
            int capturedId = skillId;
            btn.onClick.AddListener(() =>
            {
                Debug.Log($"[BattleSkillPanel] 点击技能项: skillId={capturedId} " +
                          $"按钮activeInHierarchy={btn.gameObject.activeInHierarchy} " +
                          $"_onSkillSelected={( _onSkillSelected != null ? "已绑定" : "为空")}");
                _onSkillSelected?.Invoke(capturedId);
            });
        }

        // 置灰视觉：冷却 或 目标无效/行动点不足 → alpha 0.45；有效恢复全色（不依赖模板原色）
        float a = (disabled || invalidForTarget) ? 0.45f : 1.00f;
        if (bgImg != null)
            bgImg.color = new Color(bgImg.color.r, bgImg.color.g, bgImg.color.b, a);
        if (nameTmp != null)
            nameTmp.color = new Color(nameTmp.color.r, nameTmp.color.g, nameTmp.color.b, a);
        if (detailTmp != null)
            detailTmp.color = new Color(detailTmp.color.r, detailTmp.color.g, detailTmp.color.b, a);

        // 非冷却项记录，供目标切换后 RefreshInvalidState 重算置灰与 interactable
        if (!disabled)
        {
            _items.Add((skillId, btn, bgImg, nameTmp, detailTmp));
        }

        Debug.Log($"[BattleSkillPanel][{name}] 已创建: " +
                  $"btn={btn} btnEnabled={btn?.enabled} btnInteractable={btn?.interactable} " +
                  $"nameTmp={nameTmp} detailTmp={detailTmp} bgImg={bgImg}");
    }

    /// <summary> 检查 Addressable 是否存在该地址（不存在则静默跳过，避免 LoadAsset 报错） </summary>
    private static bool KeyExists(object key)
    {
        foreach (IResourceLocator locator in Addressables.ResourceLocators)
        {
            if (locator.Locate(key, typeof(UnityEngine.Object), out _))
                return true;
        }
        return false;
    }

    /// <summary>
    /// 异步加载技能图标到 icon；配置的图标不存在时回退默认图标，仍失败则保留模板自带图标
    /// </summary>
    private void LoadIcon(Image target, int iconId)
    {
        string address = string.Format(skillIconPathFormat, iconId);
        if (KeyExists(address))
        {
            LoadSprite(target, address);
            return;
        }

        // 该 id 没有对应图标 → 回退默认图标（skill_1）；默认图标本身缺失时保留模板图标
        if (defaultSkillIconId > 0 && defaultSkillIconId != iconId)
        {
            string fallback = string.Format(skillIconPathFormat, defaultSkillIconId);
            if (KeyExists(fallback))
                LoadSprite(target, fallback);
        }
    }

    /// <summary> 从 Addressables 异步加载 Sprite 并赋给 target；失败时保持 target 现有图标 </summary>
    private void LoadSprite(Image target, string address)
    {
        var handle = Addressables.LoadAssetAsync<Sprite>(address);
        handle.Completed += op =>
        {
            if (op.Status == AsyncOperationStatus.Succeeded && op.Result != null)
            {
                if (target != null)
                {
                    target.sprite = op.Result;
                    target.gameObject.SetActive(true);
                }
            }
            else
            {
                if (op.IsValid())
                    Addressables.Release(op);
            }
        };
    }
}
