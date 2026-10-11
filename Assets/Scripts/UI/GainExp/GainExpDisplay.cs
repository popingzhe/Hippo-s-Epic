using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 获得经验提示（Gain_exp）—— 经验变动（CharacterStatsManager.AddExp）时弹出：
///   process      &lt;- 本级经验进度（fillAmount）
///   level        &lt;- "LEVEL:x"   当前等级
///   exp          &lt;- "EXP:+n"    本次获得经验
///   process_text &lt;- "cur/max"   本级经验进度数字（表里没有该等级时显示 MAX）
/// 数据来源：CharacterStatsManager（角色属性）+ TbCharacterlevel（等级表：一行一个等级，Id=等级，Level 列=该级升级所需经验）。
/// 弹出表现：**从下往上**——下方渐入升到原位 → 停留 showDuration 秒 → 继续上飘渐出（Update 驱动，不用协程）。
/// 随角色预制体常驻；运行时默认隐藏。战斗结算在战斗场景发放经验、再切回关卡（角色由场景重新加载）——
/// 组件生成时若发现有未展示过的经验（HasPendingExpGain），补弹一次，保证战斗奖励回到关卡也能看到。
/// </summary>
public class GainExpDisplay : MonoBehaviour
{
    [Header("显示节点")]
    [SerializeField] private Image process;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private TextMeshProUGUI expText;
    [SerializeField] private TextMeshProUGUI processText;

    [Header("停留时长（秒）")]
    [SerializeField] private float showDuration = 2.5f;

    [Header("弹出表现（从下到上：渐入 → 停留 → 上飘渐出）")]
    [SerializeField] private CanvasGroup canvasGroup;      // 留空则运行时自动添加
    [SerializeField] private float fadeInTime = 0.22f;     // 渐入时长
    [SerializeField] private float fadeOutTime = 0.4f;     // 渐出（上飘）时长
    [SerializeField] private float moveDistance = 0.5f;   // 上移距离（本地单位，玩家根空间）

    private enum Phase { None, FadeIn, Hold, FadeOut }

    private Canvas _canvas;
    private Vector3 _baseLocalPos;
    private Phase _phase = Phase.None;
    private float _phaseTime;
    private bool _subscribed;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

        _baseLocalPos = transform.localPosition;
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
        if (_phase == Phase.None) return;

        // 掉帧/卡顿（场景切换、编辑器停帧等）时钳一下，否则一整段动画会被一帧跳完
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

        switch (_phase)
        {
            case Phase.FadeIn:
            {
                _phaseTime += dt;
                float p = fadeInTime > 0f ? Mathf.Clamp01(_phaseTime / fadeInTime) : 1f;
                float e = 1f - (1f - p) * (1f - p);      // easeOutQuad：起步快、收尾稳

                if (canvasGroup != null) canvasGroup.alpha = e;
                transform.localPosition = Vector3.Lerp(_baseLocalPos + Vector3.down * moveDistance, _baseLocalPos, e);

                if (p >= 1f) { _phase = Phase.Hold; _phaseTime = 0f; }
                break;
            }

            case Phase.Hold:
            {
                _phaseTime += dt;
                if (_phaseTime >= showDuration) { _phase = Phase.FadeOut; _phaseTime = 0f; }
                break;
            }

            case Phase.FadeOut:
            {
                _phaseTime += dt;
                float p = fadeOutTime > 0f ? Mathf.Clamp01(_phaseTime / fadeOutTime) : 1f;
                float e = p * p;                          // easeInQuad：越飘越快

                if (canvasGroup != null) canvasGroup.alpha = 1f - e;
                transform.localPosition = Vector3.Lerp(_baseLocalPos, _baseLocalPos + Vector3.up * moveDistance, e);

                if (p >= 1f) Finish();
                break;
            }
        }
    }

    private void OnExpGained(int amount)
    {
        ShowGain(amount);
    }

    private void ShowGain(int amount)
    {
        Refresh(amount);

        // 起点：下方 + 全透明，然后渐入升上来
        transform.localPosition = _baseLocalPos + Vector3.down * moveDistance;
        if (canvasGroup != null) canvasGroup.alpha = 0f;

        SetVisible(true);
        _phase = Phase.FadeIn;
        _phaseTime = 0f;

        CharacterStatsManager.Instance.MarkExpGainShown();
    }

    private void Finish()
    {
        _phase = Phase.None;
        _phaseTime = 0f;

        SetVisible(false);
        transform.localPosition = _baseLocalPos;
        if (canvasGroup != null) canvasGroup.alpha = 1f;   // 复位，免得下次渐入前闪一下
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

        // 等级表一行 = 一个等级（Id = 等级），该行 Level 列 = 升下一级所需经验
        // cur = 本级已攒经验，max = 本级升级所需经验；表里没有该等级时显示 MAX
        int cur = Mathf.Max(0, stats.Exp);
        int max = 0;
        bool maxed = false;

        cfg.Tables tables = GetTables();
        if (tables != null && tables.TbCharacterlevel != null)
        {
            int needExp = -1;   // 本级升级所需经验
            foreach (var row in tables.TbCharacterlevel.DataList)
            {
                if (row.Id != stats.Level) continue;
                needExp = row.Level;
                break;
            }

            if (needExp < 0) maxed = true;            // 表里没有该等级
            else max = Mathf.Max(1, needExp);
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
        // 编辑器里点菜单看不到 Update 过程，直接把"停留"中间态摆出来
        SetVisible(true);
        if (canvasGroup != null) canvasGroup.alpha = 1f;
        transform.localPosition = _baseLocalPos;
        _phase = Phase.Hold;
        _phaseTime = 0f;
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
