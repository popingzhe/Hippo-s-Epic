using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// 物品获得提示框（SceneTip）—— 按物品 ID 从物品表取数据填充：
///   HintText    &lt;- item.Gethint     （获取方式）
///   tip_content &lt;- item.Tip         （物品详情，已有字段）
///   ButtonText  &lt;- item.Getexclaim  （获取感叹，按钮上的文字）
///   icon        &lt;- item.Icon        （IconLoader.LoadItemIcon）
///   btn_1       =  确认按钮
/// 用法：实例化后调用 Show(itemId[, onConfirm])；确认按钮点击后自动隐藏并回调。
/// </summary>
public class SceneTip : MonoBehaviour
{
    [Header("物品信息")]
    [SerializeField] private Image itemIcon;
    [SerializeField] private TextMeshProUGUI hintText;
    [SerializeField] private TextMeshProUGUI tipText;
    [SerializeField] private TextMeshProUGUI exclaimText;

    [Header("操作按钮")]
    [SerializeField] private Button confirmButton;

    [Header("框体（需带 VerticalLayoutGroup + ContentSizeFitter）")]
    [SerializeField] private GameObject boxRoot;

    [Header("悬浮才显示的详情")]
    [SerializeField] private RectTransform hoverArea;   // icon 节点
    [SerializeField] private GameObject detailBox;      // left_detail 节点

    [Header("内容变体（长期 Buff 提示）")]
    [Tooltip("默认内容节点（物品提示用）。")]
    [SerializeField] private GameObject contentDefault;
    [Tooltip("长期 Buff 提示内容节点（Content_2y）。ShowBuff 时显示它、隐藏默认内容。")]
    [SerializeField] private GameObject contentBuff;

    private bool _buffCached;
    private GameObject _buffBox;
    private ContentSizeFitter _buffFitter;
    private VerticalLayoutGroup _buffLayout;
    private TextMeshProUGUI _buffHint;
    private TextMeshProUGUI _buffTip;
    private TextMeshProUGUI _buffExclaim;
    private Image _buffIcon;
    private Button _buffConfirm;

    private Action _onConfirm;
    private ContentSizeFitter _boxFitter;
    private VerticalLayoutGroup _boxLayout;
    private bool _cached;
    private InputAction _confirmAction;
    private PointerEventData _pointerData;
    private int _pendingRebuildFrames;

    private void Awake()
    {
        CacheLayout();
        Hide();
    }

    private void CacheLayout()
    {
        Transform t = boxRoot != null ? boxRoot.transform : transform;
        _boxFitter = t.GetComponent<ContentSizeFitter>();
        _boxLayout = t.GetComponent<VerticalLayoutGroup>();
        _cached = true;
    }

    /// <summary>按物品 ID 显示提示。onConfirm 为确认按钮回调，可为 null。</summary>
    public bool Show(int itemId, Action onConfirm = null)
    {
        cfg.Tables tables = GetTables();
        if (tables == null)
        {
            Debug.LogWarning("[SceneTip] Tables 未就绪，无法显示");
            return false;
        }

        cfg.cfg.item.Item itemCfg = tables.TbItem.GetOrDefault(itemId);
        if (itemCfg == null)
        {
            Debug.LogWarning("[SceneTip] 物品表中不存在 id=" + itemId);
            return false;
        }

        if (!_cached) CacheLayout();
        ApplyContentVariant(false);   // 物品提示用默认内容

        _onConfirm = onConfirm;
        EnsureConfirmAction();

        if (hintText != null) hintText.text = itemCfg.Gethint ?? string.Empty;
        if (tipText != null) tipText.text = itemCfg.Tip ?? string.Empty;
        if (exclaimText != null) exclaimText.text = itemCfg.Getexclaim ?? string.Empty;

        if (itemIcon != null)
        {
            Sprite sp = IconLoader.LoadItemIcon(itemCfg.Icon);
            itemIcon.sprite = sp;
            itemIcon.gameObject.SetActive(sp != null);
        }

        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(OnConfirmClicked);
        }

        SetDetailVisible(false);
        if (boxRoot != null) boxRoot.SetActive(true);
        gameObject.SetActive(true);
        ForceRebuildLayout();
        _pendingRebuildFrames = 1;   // 下一帧（激活后）再重建一次
        return true;
    }

    public void Hide()
    {
        if (confirmButton != null) confirmButton.onClick.RemoveAllListeners();
        if (_buffConfirm != null) _buffConfirm.onClick.RemoveAllListeners();
        _onConfirm = null;
        if (boxRoot != null) boxRoot.SetActive(false);
        ApplyContentVariant(false);   // 恢复到默认内容，下次直接复用
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (boxRoot != null ? !boxRoot.activeSelf : !gameObject.activeSelf)
        {
            SetDetailVisible(false);
            return;
        }

        if (_pendingRebuildFrames > 0)
        {
            _pendingRebuildFrames--;
            if (_pendingRebuildFrames == 0) ForceRebuildLayout();
        }

        UpdateDetailHover();

        if (_confirmAction != null)
        {
            if (_confirmAction.WasPressedThisFrame())
                OnConfirmClicked();
            return;
        }

        // 兜底：取不到游戏交互动作时直接看键盘（E / 回车 / 空格）
        Keyboard kb = Keyboard.current;
        if (kb != null && (kb.eKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame))
            OnConfirmClicked();
    }

    private void UpdateDetailHover()
    {
        if (detailBox == null || hoverArea == null) return;
        SetDetailVisible(IsPointerOverIcon());
    }

    private void SetDetailVisible(bool visible)
    {
        if (detailBox == null) return;
        if (detailBox.activeSelf == visible) return;

        detailBox.SetActive(visible);

        // 强制刷新它自己 + 它后面（下面）节点的布局，别等下一帧
        var dRect = detailBox.transform as RectTransform;
        if (dRect != null)
        {
            LayoutRebuilder.MarkLayoutForRebuild(dRect);
            LayoutRebuilder.ForceRebuildLayoutImmediate(dRect);
        }

        ForceRebuildLayout();
        _pendingRebuildFrames = 1;   // 下一帧（激活后）再重建一次
    }

    private bool IsPointerOverIcon()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || hoverArea == null) return false;

        Canvas cv = GetComponent<Canvas>();
        Camera cam = cv != null && cv.worldCamera != null ? cv.worldCamera : Camera.main;
        if (cam == null) return false;

        // 屏幕点 → 射线 → 与 icon 所在平面求交 → 换算到 icon 本地判断是否在矩形内
        // （正交/透视相机、有无缩放旋转都成立；不走 EventSystem，所以不受全屏 UI 遮挡影响）
        Vector2 screen = mouse.position.ReadValue();
        Ray ray = cam.ScreenPointToRay(new Vector3(screen.x, screen.y, 0f));
        Plane plane = new Plane(hoverArea.rotation * Vector3.forward, hoverArea.position);
        float dist;
        if (!plane.Raycast(ray, out dist)) return false;

        Vector3 local = hoverArea.InverseTransformPoint(ray.GetPoint(dist));
        return hoverArea.rect.Contains(new Vector2(local.x, local.y));
    }

    private void EnsureConfirmAction()
    {
        if (_confirmAction != null) return;

        InputManager inputManager = ManagerRegistry.Get<InputManager>();
        if (inputManager != null && inputManager.GameInput != null)
            _confirmAction = inputManager.GameInput.Player.Interact;
    }

    private void OnConfirmClicked()
    {
        Action cb = _onConfirm;
        Hide();
        if (cb != null) cb();
    }

    private void ForceRebuildLayout()
    {
        RefreshTextMetrics(hintText);
        RefreshTextMetrics(tipText);
        RefreshTextMetrics(exclaimText);
        Canvas.ForceUpdateCanvases();

        RebuildFitter(hintText);
        RebuildFitter(tipText);
        RebuildFitter(exclaimText);

        if (_boxLayout != null)
        {
            _boxLayout.CalculateLayoutInputHorizontal();
            _boxLayout.CalculateLayoutInputVertical();
            _boxLayout.SetLayoutHorizontal();
            _boxLayout.SetLayoutVertical();
        }

        if (_boxFitter != null)
        {
            _boxFitter.SetLayoutHorizontal();
            _boxFitter.SetLayoutVertical();
        }

        RectTransform rect = boxRoot != null ? boxRoot.GetComponent<RectTransform>() : GetComponent<RectTransform>();
        if (rect != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
    }

    private static void RefreshTextMetrics(TextMeshProUGUI label)
    {
        if (label == null) return;
        label.ForceMeshUpdate();
        label.SetLayoutDirty();
    }

    private static void RebuildFitter(TextMeshProUGUI label)
    {
        if (label == null) return;
        ContentSizeFitter fitter = label.GetComponent<ContentSizeFitter>();
        if (fitter == null) return;
        fitter.SetLayoutHorizontal();
        fitter.SetLayoutVertical();
    }

    /// <summary>
    /// 显示"获得长期 Buff"提示：使用 Content_2y 变体（contentBuff），
    /// HintText ← buff.Name，tip_content ← buff.Descrption(+Tip)，ButtonText ← "确定"，
    /// icon ← buff.Icon（IconLoader）。onConfirm 为确认回调，可为 null。
    /// </summary>
    public bool ShowBuff(int buffId, Action onConfirm = null)
    {
        cfg.Tables tables = GetTables();
        if (tables == null)
        {
            Debug.LogWarning("[SceneTip] Tables 未就绪，无法显示 Buff 提示");
            return false;
        }

        cfg.cfg.buff.Buff buffCfg = tables.TbBuff.GetOrDefault(buffId);
        if (buffCfg == null)
        {
            Debug.LogWarning("[SceneTip] Buff 表中不存在 id=" + buffId);
            return false;
        }

        ResolveContentVariants();
        if (contentBuff == null)
        {
            Debug.LogWarning("[SceneTip] 未配置 Content_2y（contentBuff），无法显示 Buff 提示");
            return false;
        }

        if (!_cached) CacheLayout();
        CacheBuffRefs();

        _onConfirm = onConfirm;
        EnsureConfirmAction();
        ApplyContentVariant(true);

        if (_buffHint != null) _buffHint.text = buffCfg.Name ?? string.Empty;

        if (_buffTip != null)
        {
            string desc = buffCfg.Descrption ?? string.Empty;
            string tip = buffCfg.Tip ?? string.Empty;
            _buffTip.text = string.IsNullOrEmpty(tip) ? desc : desc + "\n" + tip;
        }

        if (_buffExclaim != null) _buffExclaim.text = "确定";

        if (_buffIcon != null)
        {
            Sprite sp = IconLoader.LoadItemIcon(buffCfg.Icon);
            _buffIcon.sprite = sp;
            _buffIcon.gameObject.SetActive(sp != null);
        }

        if (_buffConfirm != null)
        {
            _buffConfirm.onClick.RemoveAllListeners();
            _buffConfirm.onClick.AddListener(OnConfirmClicked);
        }

        if (_buffBox != null) _buffBox.SetActive(true);
        gameObject.SetActive(true);
        RebuildBuffLayout();
        _pendingRebuildFrames = 1;
        return true;
    }

    /// <summary>字段没在预制体上连好时，按节点名兜底解析。</summary>
    private void ResolveContentVariants()
    {
        if (contentDefault == null)
        {
            Transform t = transform.Find("Content");
            if (t != null) contentDefault = t.gameObject;
        }
        if (contentBuff == null)
        {
            Transform t = transform.Find("Content_2y");
            if (t != null)
            {
                contentBuff = t.gameObject;
                _buffCached = false;   // 兜底拿到后要重新缓存内部引用
            }
        }
    }

    /// <summary>切换内容变体：false=默认(Content)，true=长期Buff(Content_2y)。</summary>
    private void ApplyContentVariant(bool buff)
    {
        ResolveContentVariants();
        if (contentDefault != null) contentDefault.SetActive(!buff);
        if (contentBuff != null) contentBuff.SetActive(buff);
        else if (buff) Debug.LogWarning("[SceneTip] 找不到 Content_2y 节点，退化为默认样式");
    }

    private void CacheBuffRefs()
    {
        if (_buffCached || contentBuff == null) return;
        _buffCached = true;

        Transform root = contentBuff.transform;
        Transform border = root.Find("Border");
        _buffBox = (border != null ? border : root).gameObject;
        _buffFitter = _buffBox.GetComponent<ContentSizeFitter>();
        _buffLayout = _buffBox.GetComponent<VerticalLayoutGroup>();

        _buffHint = FindText(root, "HintText");
        _buffTip = FindText(root, "tip_content");
        _buffExclaim = FindText(root, "ButtonText");
        _buffIcon = FindImage(root, "icon");

        Transform btn = root.Find("Border/btn_1");
        _buffConfirm = btn != null ? btn.GetComponent<Button>() : null;
    }

    private static TextMeshProUGUI FindText(Transform root, string nodeName)
    {
        var all = root.GetComponentsInChildren<TextMeshProUGUI>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i].name == nodeName) return all[i];
        return null;
    }

    private static Image FindImage(Transform root, string nodeName)
    {
        var all = root.GetComponentsInChildren<Image>(true);
        for (int i = 0; i < all.Length; i++)
            if (all[i].name == nodeName) return all[i];
        return null;
    }

    private void RebuildBuffLayout()
    {
        RefreshTextMetrics(_buffHint);
        RefreshTextMetrics(_buffTip);
        RefreshTextMetrics(_buffExclaim);
        Canvas.ForceUpdateCanvases();

        RebuildFitter(_buffHint);
        RebuildFitter(_buffTip);
        RebuildFitter(_buffExclaim);

        if (_buffLayout != null)
        {
            _buffLayout.CalculateLayoutInputHorizontal();
            _buffLayout.CalculateLayoutInputVertical();
            _buffLayout.SetLayoutHorizontal();
            _buffLayout.SetLayoutVertical();
        }

        if (_buffFitter != null)
        {
            _buffFitter.SetLayoutHorizontal();
            _buffFitter.SetLayoutVertical();
        }

        if (_buffBox != null)
        {
            var rt = _buffBox.transform as RectTransform;
            if (rt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }
    }

    private const string PrefabAddress = "Assets/Prefabs/UI/Interact/SceneTip";
    private static GameObject _sharedPrefab;
    // 必须把 handle 存成静态字段，否则 Addressables 的操作会被回收，Completed 回调永远不触发
    private static AsyncOperationHandle<GameObject> _sharedHandle;
    private static bool _loading;
    private static Vector3 _tipLocalPos;   // 提示相对父节点的位置（对齐 Gain_exp）

    /// <summary>全局显示"获得长期 Buff"提示（对话等非交互系统用）。</summary>
    public static void ShowBuffGlobal(int buffId, Action onConfirm = null, Transform parent = null)
    {
        // ① 优先用玩家节点下自建的 Gain_buff（不再实例化 SceneTip 预制体）
        PlayerCharacter pc = UnityEngine.Object.FindObjectOfType<PlayerCharacter>();
        Transform gainBuff = pc != null ? pc.transform.Find("Gain_buff") : null;
        if (gainBuff != null)
        {
            ShowOnGainBuffNode(gainBuff.gameObject, buffId);
            return;
        }

        if (_sharedPrefab != null)
        {
            CreateAndShowBuff(buffId, onConfirm, parent);
            return;
        }
        if (_loading) return;

        _loading = true;
        _sharedHandle = Addressables.LoadAssetAsync<GameObject>(PrefabAddress);
        _sharedHandle.Completed += h =>
        {
            _loading = false;
            if (h.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogWarning("[SceneTip] 加载 " + PrefabAddress + " 失败，无法弹 Buff 提示");
                return;
            }
            _sharedPrefab = h.Result;
            CreateAndShowBuff(buffId, onConfirm, parent);
        };
    }

    /// <summary>
    /// 用玩家节点下自建的 Gain_buff 节点显示获得 Buff 提示：
    /// 激活节点 → 按顺序填第一/第二个 TMP（名称、描述）→ 2 秒后自动收起。
    /// 节点上没有 GainBuffTipAutoHide 组件时自动补一个。
    /// </summary>
    private static void ShowOnGainBuffNode(GameObject node, int buffId)
    {
        if (node == null) return;

        cfg.Tables tables = GetTables();
        var buffCfg = tables != null && tables.TbBuff != null ? tables.TbBuff.GetOrDefault(buffId) : null;
        if (buffCfg != null)
        {
            var texts = node.GetComponentsInChildren<TMPro.TextMeshProUGUI>(true);
            if (texts != null && texts.Length > 0)
            {
                texts[0].text = buffCfg.Name ?? string.Empty;
                if (texts.Length > 1) texts[1].text = buffCfg.Descrption ?? string.Empty;
                for (int i = 2; i < texts.Length; i++)
                    if (texts[i].name.ToLower().Contains("tip")) texts[i].text = buffCfg.Tip ?? string.Empty;
            }

        }

        var autoHide = node.GetComponent<GainBuffTipAutoHide>();
        if (autoHide == null) autoHide = node.AddComponent<GainBuffTipAutoHide>();
        node.SetActive(true);
        autoHide.Restart();
        Debug.Log("[SceneTip] 用 Gain_buff 显示获得 Buff 提示: " + buffId);
    }

    /// <summary>提示的父节点：和物品/经验提示一致，挂在玩家节点下（找不到玩家则退回场景根）。</summary>
    private static Transform ResolveTipParent()
    {
        PlayerCharacter player = UnityEngine.Object.FindObjectOfType<PlayerCharacter>();
        if (player == null) return null;
        Transform ui = player.transform.Find("UI");
        if (ui != null) return ui;

        // 与"经验获取"面板对齐：挂在玩家节点下，位置取 Gain_exp 的 localPosition
        Transform gainExp = player.transform.Find("Gain_exp");
        _tipLocalPos = gainExp != null ? gainExp.localPosition : Vector3.zero;
        return player.transform;
    }

    private static void CreateAndShowBuff(int buffId, Action onConfirm, Transform parent = null)
    {
        if (parent == null)
        {
            _tipLocalPos = Vector3.zero;
            parent = ResolveTipParent();
        }
        GameObject instance = parent != null ? Instantiate(_sharedPrefab, parent) : Instantiate(_sharedPrefab);
        if (parent != null) instance.transform.localPosition = _tipLocalPos;
        instance.name = "SceneTip";
        var tip = instance.GetComponent<SceneTip>();
        if (tip == null)
        {
            Debug.LogWarning("[SceneTip] 预制体上缺少 SceneTip 组件");
            return;
        }
        if (!tip.ShowBuff(buffId, onConfirm))
            tip.Hide();
    }

    private static cfg.Tables GetTables()
    {
        if (ManagerRegistry.TryGet<DataTableManager>(out DataTableManager dtm) && dtm.Tables != null)
            return dtm.Tables;
        return null;
    }
}

/// <summary>Gain_buff 提示：激活后 N 秒自动收起。</summary>
public class GainBuffTipAutoHide : MonoBehaviour
{
    public float seconds = 2f;

    public void Restart()
    {
        CancelInvoke();
        Invoke(nameof(Close), seconds);
    }

    private void Close()
    {
        gameObject.SetActive(false);
    }
}
