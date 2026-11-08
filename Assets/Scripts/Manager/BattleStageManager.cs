using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 战斗场景管理器：管理站位点、生成Spine角色、血条绑定
/// 挂在战斗场景的根节点上
/// </summary>
public class BattleStageManager : MonoBehaviour
{
    #region 单例

    private static BattleStageManager _instance;
    public static BattleStageManager Instance => _instance;

    #endregion

    #region 序列化字段

    [Header("站位点")]
    [Tooltip("玩家方站位点（最多9个）")]
    [SerializeField] private Transform[] playerSlots;

    [Tooltip("敌方站位点（最多9个）")]
    [SerializeField] private Transform[] enemySlots;

    [Header("血条")]
    [Tooltip("血条预制体")]
    [SerializeField] private GameObject healthBarPrefab;

    [Tooltip("血条在角色头顶的偏移")]
    [SerializeField] private Vector3 healthBarOffset = new Vector3(0, 2.2f, 0);

    [Header("目标选中标签")]
    [Tooltip("角色下方的选中标签节点（不拖则自动查找子节点 TargetSelectionIndicator）")]
    [SerializeField] private GameObject targetIndicator;

    [Tooltip("目标选中标签的显示缩放倍数")]
    [SerializeField] private float targetIndicatorScale = 4f;

    [Header("点击检测")]
    [Tooltip("角色点击检测预制体（含 Collider2D），不拖则运行时动态创建 HitArea 子节点")]
    [SerializeField] private GameObject hitAreaPrefab;

    [Header("背景")]
    [Tooltip("战斗背景渲染器（不拖则自动查找子节点 bg）")]
    [SerializeField] private SpriteRenderer backgroundRenderer;

    [Tooltip("战斗背景图 Addressable 根目录")]
    [SerializeField] private string backgroundRoot = "Assets/Art/Sprites/Battle/bg";

    #endregion

    #region 运行时数据

    /// <summary> 已生成的角色GameObject（key=槽位索引） </summary>
    private readonly Dictionary<int, GameObject> _playerUnits = new Dictionary<int, GameObject>();
    private readonly Dictionary<int, GameObject> _enemyUnits = new Dictionary<int, GameObject>();

    /// <summary> 血条组件（key=槽位索引） </summary>
    private readonly Dictionary<int, BattleHealthBar> _playerHealthBars = new Dictionary<int, BattleHealthBar>();
    private readonly Dictionary<int, BattleHealthBar> _enemyHealthBars = new Dictionary<int, BattleHealthBar>();

    /// <summary> 当前行动单位指示箭头 </summary>
    private GameObject _currentIndicator;

    /// <summary> 目标选中标签（场景预置节点，运行时移动/显示/隐藏） </summary>
    private GameObject _targetIndicator;

    /// <summary> 资源加载句柄 </summary>
    private readonly List<AsyncOperationHandle> _loadHandles = new List<AsyncOperationHandle>();

    #endregion

    #region 生命周期

    private void Awake()
    {
        _instance = this;
        AutoFindSlots();
        AutoFindBackground();

        // 表现层总控（技能释放演出序列）
        if (GetComponent<BattlePresenter>() == null)
            gameObject.AddComponent<BattlePresenter>();

        // 目标选中提示（TargetSelectionIndicator）默认隐藏：
        // 场景里这个节点是激活状态，不主动关会一直露在角色脚下
        EnsureTargetIndicator();
        if (_targetIndicator != null)
            _targetIndicator.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_instance == this) _instance = null;
        ReleaseAll();
    }

    #endregion

    #region 初始化

    /// <summary>
    /// 自动查找站位点（如果Inspector未手动赋值）
    /// </summary>
    private void AutoFindSlots()
    {
        if (playerSlots == null || playerSlots.Length == 0)
        {
            var root = transform.Find("PlayerSlots");
            if (root != null)
            {
                playerSlots = new Transform[root.childCount];
                for (int i = 0; i < root.childCount; i++)
                    playerSlots[i] = root.GetChild(i);
            }
        }

        if (enemySlots == null || enemySlots.Length == 0)
        {
            var root = transform.Find("EnemySlots");
            if (root != null)
            {
                enemySlots = new Transform[root.childCount];
                for (int i = 0; i < root.childCount; i++)
                    enemySlots[i] = root.GetChild(i);
            }
        }
    }

    /// <summary>
    /// 自动查找背景节点（场景子节点 bg 上的 SpriteRenderer，Inspector 未手动赋值时调用）
    /// </summary>
    private void AutoFindBackground()
    {
        if (backgroundRenderer != null) return;
        var bgTransform = transform.Find("bg");
        if (bgTransform != null)
            backgroundRenderer = bgTransform.GetComponent<SpriteRenderer>();
    }

    /// <summary>
    /// 根据战斗配置动态加载背景图并设置到 bg 节点（Addressables）
    /// </summary>
    public void ApplyBackground(int battleId)
    {
        var tables = ManagerRegistry.GetTables<cfg.Tables>();
        if (tables == null)
        {
            Debug.LogWarning("[BattleStageManager] Tables 未加载，无法应用战斗背景");
            return;
        }

        var battleCfg = tables.TbBattle.GetOrDefault(battleId);
        if (battleCfg == null || string.IsNullOrWhiteSpace(battleCfg.Bg))
        {
            Debug.LogWarning($"[BattleStageManager] 战斗配置无背景图: battleId={battleId}");
            return;
        }

        AutoFindBackground();
        StartCoroutine(LoadBackgroundCoroutine($"{backgroundRoot}/{battleCfg.Bg}"));
    }

    /// <summary>
    /// 通过 Addressables 加载背景 Sprite 并赋值给背景节点
    /// </summary>
    private IEnumerator LoadBackgroundCoroutine(string address)
    {
        var handle = Addressables.LoadAssetAsync<Sprite>(address);
        _loadHandles.Add(handle);
        yield return handle;

        if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
        {
            if (backgroundRenderer == null) AutoFindBackground();
            if (backgroundRenderer != null)
            {
                backgroundRenderer.sprite = handle.Result;
                Debug.Log($"[BattleStageManager] 战斗背景加载完成: {address}");
            }
            else
            {
                Debug.LogWarning("[BattleStageManager] 未找到背景节点 bg，无法显示背景图");
            }
        }
        else
        {
            Debug.LogWarning($"[BattleStageManager] 无法加载战斗背景图: {address}");
        }
    }

    /// <summary>
    /// 根据战斗单位列表生成角色（清空后重建）
    /// </summary>
    public void SpawnUnits(List<BattleUnit> units, System.Action onComplete = null)
    {
        ClearAllUnits();
        SpawnUnitsInternal(units, onComplete);
    }

    /// <summary>
    /// 新增角色（不清空现有角色，用于增援）
    /// </summary>
    public void SpawnNewUnits(List<BattleUnit> units, System.Action onComplete = null)
    {
        SpawnUnitsInternal(units, onComplete);
    }

    private void SpawnUnitsInternal(List<BattleUnit> units, System.Action onComplete)
    {
        if (units.Count == 0)
        {
            onComplete?.Invoke();
            return;
        }

        StartCoroutine(SpawnAllUnitsCoroutine(units, onComplete));
    }

    private IEnumerator SpawnAllUnitsCoroutine(List<BattleUnit> units, System.Action onComplete)
    {
        int remaining = units.Count;
        foreach (var unit in units)
        {
            if (unit == null || unit.PersonId <= 0)
            {
                remaining--;
                continue;
            }
            StartCoroutine(SpawnUnitCoroutine(unit, () =>
            {
                remaining--;
            }));
        }
        // 等待所有协程完成
        while (remaining > 0)
            yield return null;

        Debug.Log($"[BattleStageManager] 所有角色生成完成");
        onComplete?.Invoke();
    }

    private IEnumerator SpawnUnitCoroutine(BattleUnit unit, System.Action onComplete = null)
    {
        var tables = ManagerRegistry.GetTables<cfg.Tables>();
        if (tables == null)
        {
            Debug.LogWarning("[BattleStageManager] Tables 未加载");
            onComplete?.Invoke();
            yield break;
        }
        var person = tables.TbPerson.Get(unit.PersonId);
        if (person == null)
        {
            Debug.LogWarning($"[BattleStageManager] Person不存在: {unit.PersonId}");
            onComplete?.Invoke();
            yield break;
        }

        // 获取站位点
        Transform slot = GetSlot(unit.IsPlayerSide, unit.SlotIndex);
        if (slot == null)
        {
            Debug.LogWarning($"[BattleStageManager] 站位点不存在: side={unit.IsPlayerSide}, slot={unit.SlotIndex}");
            onComplete?.Invoke();
            yield break;
        }

        // 加载Spine预制体
        string prefabPath = person.Prefab2;
        if (string.IsNullOrEmpty(prefabPath))
        {
            Debug.LogWarning($"[BattleStageManager] Person {person.Name} 没有战斗预制体(Prefab2)");
            onComplete?.Invoke();
            yield break;
        }

        GameObject prefab = null;
        bool loaded = false;

        // 尝试Addressables加载
        var handle = Addressables.LoadAssetAsync<GameObject>(prefabPath);
        _loadHandles.Add(handle);
        yield return handle;

        if (handle.Status == AsyncOperationStatus.Succeeded)
        {
            prefab = handle.Result;
            loaded = true;
        }
        else
        {
#if UNITY_EDITOR
            string assetPath = prefabPath.StartsWith("Assets/") ? prefabPath : "Assets/" + prefabPath;
            prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab != null) loaded = true;
#endif
        }

        if (!loaded || prefab == null)
        {
            Debug.LogWarning($"[BattleStageManager] 无法加载预制体: {prefabPath}");
            onComplete?.Invoke();
            yield break;
        }

        // 实例化角色
        var go = Instantiate(prefab, slot.position, Quaternion.identity, slot);
        go.name = $"Unit_{unit.PersonId}_{person.Name}";

        // 归零Z坐标，确保在2D平面上
        var pos = go.transform.position;
        pos.z = 0;
        go.transform.position = pos;

        // 统一缩放为合理大小（可根据预制体调整）
        // go.transform.localScale = Vector3.one;

        // 敌方面向左边（仅翻转视觉层 Spine，避免镜像血条/指示器等 UI 子节点）
        var visual = FindChildRecursive(go.transform, "Visual");
        if (visual != null)
        {
            var scale = visual.localScale;
            scale.x = unit.IsPlayerSide ? Mathf.Abs(scale.x) : -Mathf.Abs(scale.x);
            visual.localScale = scale;
        }

        // 预制体已内置 HitArea / 血条 / 指示器，运行时只查找并管理

        // 查找预制体中的血条并 Setup（血条是角色子节点，LateUpdate 保持本地位置）
        var healthBar = go.GetComponentInChildren<BattleHealthBar>();
        if (healthBar != null)
        {
            healthBar.Setup(unit, go.transform, healthBarOffset);
            var healthBarDict = unit.IsPlayerSide ? _playerHealthBars : _enemyHealthBars;
            healthBarDict[unit.SlotIndex] = healthBar;
        }

        // 查找当前行动指示箭头（默认隐藏，高亮时显示）
        var indicator = FindChildRecursive(go.transform, "CurrentIndicator");
        if (indicator != null)
            indicator.gameObject.SetActive(false);

        // 记录
        var dict = unit.IsPlayerSide ? _playerUnits : _enemyUnits;
        dict[unit.SlotIndex] = go;

        Debug.Log($"[BattleStageManager] 生成角色: {person.Name} at slot {unit.SlotIndex} ({(unit.IsPlayerSide ? "Player" : "Enemy")})");
        onComplete?.Invoke();
    }

    #endregion

    #region 血条

    /// <summary>
    /// 更新指定单位的血条
    /// </summary>
    public void UpdateHealthBar(BattleUnit unit)
    {
        var dict = unit.IsPlayerSide ? _playerHealthBars : _enemyHealthBars;
        if (dict.TryGetValue(unit.SlotIndex, out var bar))
            bar.UpdateHealth();
    }

    /// <summary>
    /// 更新所有血条
    /// </summary>
    public void UpdateAllHealthBars()
    {
        foreach (var bar in _playerHealthBars.Values) bar?.UpdateHealth();
        foreach (var bar in _enemyHealthBars.Values) bar?.UpdateHealth();
    }

    #endregion

    #region 指示器

    /// <summary>
    /// 高亮当前行动单位
    /// </summary>
    public void HighlightCurrentUnit(BattleUnit unit)
    {
        ClearHighlight();
        if (unit == null) return;

        var dict = unit.IsPlayerSide ? _playerUnits : _enemyUnits;
        if (!dict.TryGetValue(unit.SlotIndex, out var go)) return;

        // 显示预制体中的当前行动指示箭头
        var indicator = FindChildRecursive(go.transform, "CurrentIndicator");
        if (indicator == null) return;
        indicator.gameObject.SetActive(true);
        _currentIndicator = indicator.gameObject;
    }

    /// <summary>
    /// 清除高亮
    /// </summary>
    public void ClearHighlight()
    {
        if (_currentIndicator != null)
        {
            _currentIndicator.SetActive(false);
            _currentIndicator = null;
        }
    }

    /// <summary>
    /// 显示目标选中标签（角色下方绿色标记），用于目标选择反馈
    /// </summary>
    public void ShowTargetSelected(BattleUnit unit)
    {
        ClearTargetSelection();
        if (unit == null) return;

        var dict = unit.IsPlayerSide ? _playerUnits : _enemyUnits;
        if (!dict.TryGetValue(unit.SlotIndex, out var go)) return;

        EnsureTargetIndicator();
        if (_targetIndicator == null) return;

        // 选中点显示在角色位置（角色根节点即 slot 位置）
        // 注意: 不能用 go.transform.position + 固定偏移(如 -2.4), 那会偏离 slot
        _targetIndicator.transform.position = go.transform.position;

        // 显式重置缩放: 场景文件里该节点 localScale=1, 但内存场景可能被放大(如10倍),
        // 每次显示前重置, 保证选中点大小稳定; 按配置倍数放大显示
        _targetIndicator.transform.localScale = Vector3.one * targetIndicatorScale;

        var sr = _targetIndicator.GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            if (sr.sprite == null)
                sr.sprite = CreateTriangleSprite();
            sr.color = Color.green;
        }

        _targetIndicator.SetActive(true);
    }

    /// <summary>
    /// 清除目标选中标签（仅隐藏，保留场景节点）
    /// </summary>
    public void ClearTargetSelection()
    {
        if (_targetIndicator != null)
            _targetIndicator.SetActive(false);
    }

    /// <summary>
    /// 获取目标选中标签节点：优先序列化字段，其次场景子节点 TargetSelectionIndicator
    /// </summary>
    private void EnsureTargetIndicator()
    {
        if (_targetIndicator != null) return;

        if (targetIndicator != null)
        {
            _targetIndicator = targetIndicator;
            return;
        }

        var found = transform.Find("TargetSelectionIndicator");
        if (found != null)
            _targetIndicator = found.gameObject;
    }

    private Sprite CreateTriangleSprite()
    {
        // 创建简单的三角形纹理
        int size = 32;
        var tex = new Texture2D(size, size);
        var pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // 倒三角形
                float halfWidth = (float)(size - y) / size * size / 2;
                float center = size / 2f;
                if (x >= center - halfWidth && x <= center + halfWidth)
                    pixels[y * size + x] = Color.white;
                else
                    pixels[y * size + x] = Color.clear;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0f));
    }

    #endregion

    #region 查询

    public Transform GetSlot(bool isPlayerSide, int slotIndex)
    {
        var slots = isPlayerSide ? playerSlots : enemySlots;
        if (slots == null || slotIndex < 0 || slotIndex >= slots.Length) return null;
        return slots[slotIndex];
    }

    /// <summary>
    /// 递归查找子节点（兼容嵌套预制体，节点不在根的直接子节点下）
    /// </summary>
    private Transform FindChildRecursive(Transform parent, string name)
    {
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            var found = FindChildRecursive(parent.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    public GameObject GetUnitObject(bool isPlayerSide, int slotIndex)
    {
        var dict = isPlayerSide ? _playerUnits : _enemyUnits;
        return dict.TryGetValue(slotIndex, out var go) ? go : null;
    }

    /// <summary>
    /// 通过被点击的 GameObject（角色根节点或其任意子节点，如 HitArea）反查所属单位槽位。
    /// 支持同一 personId 的多个单位（用槽位唯一定位，而非 personId）。
    /// </summary>
    public bool TryResolveUnitSlot(GameObject obj, out bool isPlayerSide, out int slotIndex)
    {
        isPlayerSide = false;
        slotIndex = -1;
        if (obj == null) return false;

        Transform t = obj.transform;
        while (t != null)
        {
            foreach (var kv in _playerUnits)
            {
                if (kv.Value == t.gameObject)
                {
                    isPlayerSide = true;
                    slotIndex = kv.Key;
                    return true;
                }
            }
            foreach (var kv in _enemyUnits)
            {
                if (kv.Value == t.gameObject)
                {
                    isPlayerSide = false;
                    slotIndex = kv.Key;
                    return true;
                }
            }
            t = t.parent;
        }
        return false;
    }

    #endregion

    #region 清理

    public void ClearAllUnits()
    {
        foreach (var go in _playerUnits.Values) if (go != null) Destroy(go);
        foreach (var go in _enemyUnits.Values) if (go != null) Destroy(go);
        _playerUnits.Clear();
        _enemyUnits.Clear();
        _playerHealthBars.Clear();
        _enemyHealthBars.Clear();
        ClearHighlight();
        ClearTargetSelection();
    }

    private void ReleaseAll()
    {
        foreach (var handle in _loadHandles)
        {
            if (handle.IsValid()) Addressables.Release(handle);
        }
        _loadHandles.Clear();
    }

    #endregion
}

/// <summary>
/// 指示器浮动动画
/// </summary>
public class BattleIndicatorFloat : MonoBehaviour
{
    private float _timer;
    private Vector3 _startPos;

    private void Start()
    {
        _startPos = transform.localPosition;
    }

    private void Update()
    {
        _timer += Time.deltaTime * 3f;
        float offset = Mathf.Sin(_timer) * 0.15f;
        transform.localPosition = _startPos + new Vector3(0, offset, 0);
    }
}
