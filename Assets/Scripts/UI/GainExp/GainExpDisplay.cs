using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 获得经验提示（Gain_exp）—— 经验变动（CharacterStatsManager.AddExp）时弹出：
///   process      &lt;- 当前等级经验进度（fillAmount = 本等级内进度）
///   level        &lt;- "LEVEL:x"   当前等级
///   exp          &lt;- "EXP:+n"    本次获得经验
///   process_text &lt;- "cur/max"   当前等级经验进度数字（满级显示 MAX）
/// 数据来源：CharacterStatsManager（角色属性）+ TbCharacterlevel（等级表：Id=累计经验门槛, Level=对应等级）。
/// 随角色预制体常驻；运行时默认隐藏，获得经验后显示 showDuration 秒。
/// 战斗结算在战斗场景发放经验、再切回关卡（角色由场景重新加载）——组件生成时若发现有
/// 未展示过的经验（HasPendingExpGain），补弹一次，保证战斗奖励回到关卡也能看到。
/// </summary>
public class GainExpDisplay : MonoBehaviour
{
    [Header("显示节点")]
    [SerializeField] private Image process;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private TextMeshProUGUI expText;
    [SerializeField] private TextMeshProUGUI processText;

    [Header("显示时长（秒）")]
    [SerializeField] private float showDuration = 2.5f;

    private Canvas _canvas;
    private float _hideAt;
    private bool _subscribed;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        SetVisible(false);   // 运行时默认隐藏（编辑模式不受影响，方便美术看摆位）
    }

    private void Start()
    {
        // 补显示：发放经验时角色不在场（例：战斗结算在战斗场景发放），回到关卡后再弹
        CharacterStatsManager mgr = CharacterStatsManager.Instance;
        if (mgr.HasPendingExpGain && mgr.LastExpGain > 0)
            ShowGain(mgr.LastExpGain);
    }

    private void OnEnable()
    {
        if (_subscribed) return;
        CharacterStatsManager.Instance.OnExpGained += OnExpGained;
        _subscribed = true;
    }

    private void OnDisable()
    {
        if (!_subscribed) return;
        CharacterStatsManager.Instance.OnExpGained -= OnExpGained;
        _subscribed = false;
    }

    private void Update()
    {
        // 显示超时后自动隐藏（Update 驱动，不用协程）
        if (_canvas != null && _canvas.enabled && Time.unscaledTime >= _hideAt)
            SetVisible(false);
    }

    private void OnExpGained(int amount)
    {
        ShowGain(amount);
    }

    private void ShowGain(int amount)
    {
        Refresh(amount);
        SetVisible(true);
        _hideAt = Time.unscaledTime + showDuration;
        CharacterStatsManager.Instance.MarkExpGainShown();
    }

    /// <summary>按当前角色属性刷新界面。gained 仅用于 "EXP:+n" 的 n。</summary>
    public void Refresh(int gained)
    {
        CharacterStats stats = CharacterStatsManager.Instance.PlayerStats;
        if (stats == null)
        {
            Debug.LogWarning("[GainExpDisplay] 角色属性未就绪，无法刷新");
            return;
        }

        int cur = 0;
        int max = 0;
        bool maxed = false;

        cfg.Tables tables = GetTables();
        if (tables != null && tables.TbCharacterlevel != null)
        {
            // 等级表按"每档固定经验"设计，等级是跳档的（例：20/25/30…）——不能在表里找"等级+1"，
            // 要取：不高于当前等级的最大档（当前档位）与高于当前等级的最小档（下一档）。
            int prevThreshold = 0;    // 达到当前档位所需的累计经验
            int nextThreshold = -1;   // 达到下一档所需的累计经验（-1 = 已到表内最高档）
            int prevLevel = int.MinValue;
            int nextLevel = int.MaxValue;
            foreach (var row in tables.TbCharacterlevel.DataList)
            {
                if (row.Level <= stats.Level)
                {
                    if (row.Level >= prevLevel) { prevLevel = row.Level; prevThreshold = row.Id; }
                }
                else if (row.Level < nextLevel)
                {
                    nextLevel = row.Level; nextThreshold = row.Id;
                }
            }

            cur = Mathf.Max(0, stats.Exp - prevThreshold);
            if (nextThreshold >= 0) max = Mathf.Max(1, nextThreshold - prevThreshold);
            else maxed = true;
        }
        else
        {
            Debug.LogWarning("[GainExpDisplay] 等级表未就绪，进度按满格显示");
            maxed = true;
        }

        if (levelText != null) levelText.text = "LEVEL:" + stats.Level;
        if (expText != null) expText.text = "EXP:+" + gained;
        if (processText != null) processText.text = maxed ? "MAX" : cur + "/" + max;
        if (process != null) process.fillAmount = maxed ? 1f : Mathf.Clamp01(max > 0 ? (float)cur / max : 0f);
    }

    /// <summary>编辑器里在该组件上右键可快速预览（模拟一次 +8 经验）。</summary>
    [ContextMenu("预览：模拟获得经验 +8")]
    private void PreviewOnce()
    {
        Refresh(8);
        SetVisible(true);
        _hideAt = Time.unscaledTime + showDuration;
    }

    private void SetVisible(bool visible)
    {
        if (_canvas == null) _canvas = GetComponent<Canvas>();
        if (_canvas != null) _canvas.enabled = visible;
        else Debug.LogWarning("[GainExpDisplay] 未找到 Canvas（组件应挂在 Gain_exp 根节点上）");
    }

    private static cfg.Tables GetTables()
    {
        if (ManagerRegistry.TryGet<DataTableManager>(out DataTableManager dtm) && dtm.Tables != null)
            return dtm.Tables;
        return null;
    }
}
