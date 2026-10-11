using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>通用提示框：显示一段文字，并让预制体里的锚点节点 point 落在鼠标悬浮点上。
/// 用法：
///   CommonTipPanel.Show("文本");                        // 贴着当前鼠标弹
///   CommonTipPanel.ShowAt("文本", screenPoint);          // 贴在指定屏幕坐标弹（悬浮事件里传 eventData.position）
///   CommonTipPanel.Hide();                              // 关掉
/// 也可以走 UIManager：ui.Open&lt;CommonTipPanel&gt;(new CommonTipPanel.TipArgs { Text = "…", ScreenPosition = pos });
/// 预制体里 point 节点摆在气泡的哪个位置，那个位置就贴在鼠标上（可自由挪）。</summary>
public class CommonTipPanel : UIWindow
{
    /// <summary>打开参数：文本 + 可选屏幕坐标（不传就用当前鼠标位置）。</summary>
    public struct TipArgs
    {
        public string Text;
        public Vector2? ScreenPosition;
    }

    public override UILayer Layer => UILayer.Top;   // 盖在背包等 Popup 之上

    [Header("结构（预制体节点）")]
    [SerializeField] private RectTransform point;          // 锚点：这个节点会落在鼠标悬浮点上
    [SerializeField] private RectTransform leftDetail;     // 内容容器（带自适应大小）
    [SerializeField] private TextMeshProUGUI tipContent;   // 文本内容
    [SerializeField] private Image background;             // 根上的底图（会被强制关掉 raycast）

    [Header("跟随")]
    [SerializeField] private bool followMouse = true;      // 显示期间持续贴着鼠标
    [SerializeField] private Vector2 screenOffset = Vector2.zero;   // 锚点额外的屏幕偏移（像素）

    private RectTransform _root;
    private Canvas _canvas;
    private bool _positioned;
    private Vector2 _screenPosition;

    public override void OnCreate(object args)
    {
        if (background != null)
            background.raycastTarget = false;   // 提示框绝不能吃鼠标事件，否则整个 UI 会失灵
    }

    public override void OnOpen(object args)
    {
        _root = transform as RectTransform;
        // 坐标一律相对"最外层画布"算：预制体自带 Canvas 时，GetComponentInParent 会拿到它自己（零尺寸）
        var selfCanvas = GetComponent<Canvas>();
        _canvas = selfCanvas != null ? selfCanvas.rootCanvas : GetComponentInParent<Canvas>();

        string text = null;
        _positioned = false;

        if (args is string s)
        {
            text = s;
        }
        else if (args is TipArgs t)
        {
            text = t.Text;
            if (t.ScreenPosition.HasValue)
            {
                _screenPosition = t.ScreenPosition.Value;
                _positioned = true;
            }
        }

        if (text != null && tipContent != null)
        {
            tipContent.text = text;
            Rebuild();
        }

        NormalizeRoot();    // UIManager.PlaceWindow 会把窗口拉满画布，这里改回可自由摆位
        transform.SetAsLastSibling();   // 同级置顶：保证压在背包等界面之上

        // 提示框整体不吃鼠标事件：它贴着鼠标显示，鼠标必然落在它上面，
        // 若任一 Image/文本 raycastTarget=true，会把"移出"事件从原本的悬浮行上抢走 ⇒ 提示框闪烁
        foreach (var g in GetComponentsInChildren<Graphic>(true))
            g.raycastTarget = false;

        if (!_positioned)
            _screenPosition = MouseScreenPosition();

        MoveTo(_screenPosition);
    }

    public override void OnClose()
    {
        _positioned = false;
    }

    private void Update()
    {
        if (!followMouse) return;
        if (!gameObject.activeInHierarchy) return;
        MoveTo(MouseScreenPosition());
    }

    /// <summary>设置文本（改完会自动重新布局）。</summary>
    public void SetText(string text)
    {
        if (tipContent == null) return;
        tipContent.text = text ?? string.Empty;
        Rebuild();
    }

    /// <summary>让锚点 point 落在给定屏幕坐标上（0,0 = 屏幕左下）。</summary>
    public void MoveTo(Vector2 screenPosition)
    {
        if (_root == null) _root = transform as RectTransform;
        if (_root == null) return;

        if (_canvas == null)
        {
            var selfCanvas = GetComponent<Canvas>();
            _canvas = selfCanvas != null ? selfCanvas.rootCanvas : GetComponentInParent<Canvas>();
        }

        RectTransform canvasRect = _canvas != null ? _canvas.rootCanvas.transform as RectTransform : null;
        if (canvasRect == null) return;

        Camera cam = null;
        Canvas root = _canvas != null ? _canvas.rootCanvas : null;
        if (root != null && root.renderMode != RenderMode.ScreenSpaceOverlay)
            cam = root.worldCamera;

        Vector2 target = screenPosition + screenOffset;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, target, cam, out Vector2 local))
            return;

        // 按"世界坐标差值"一步到位：已对准时差值≈0，重复调用不会累加
        Vector3 worldTarget = canvasRect.TransformPoint(local);
        Vector3 pointWorld = point != null ? point.position : _root.position;
        _root.position += worldTarget - pointWorld;
        _screenPosition = screenPosition;
    }

    /// <summary>当前鼠标屏幕坐标（新输入系统；没有鼠标设备返回 0）。</summary>
    public static Vector2 MouseScreenPosition()
    {
        if (Mouse.current != null) return Mouse.current.position.ReadValue();
        if (Pointer.current != null) return Pointer.current.position.ReadValue();
        return Vector2.zero;
    }

    /// <summary>把窗口从"拉满画布"改回"中心锚点 + 自由摆位"。</summary>
    private void NormalizeRoot()
    {
        if (_root == null) return;
        _root.anchorMin = new Vector2(0.5f, 0.5f);
        _root.anchorMax = new Vector2(0.5f, 0.5f);
        _root.pivot = new Vector2(0.5f, 0.5f);
        _root.sizeDelta = Vector2.zero;
    }

    private void Rebuild()
    {
        if (leftDetail != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(leftDetail);
        Canvas.ForceUpdateCanvases();
    }

    // ---- 便捷入口 ----

    /// <summary>贴在指定屏幕坐标弹出。</summary>
    public static void ShowAt(string text, Vector2 screenPosition)
    {
        var ui = ManagerRegistry.Get<UIManager>();
        if (ui == null) return;
        ui.Open<CommonTipPanel>(new TipArgs { Text = text, ScreenPosition = screenPosition });
    }

    /// <summary>贴着当前鼠标弹出。</summary>
    public static void Show(string text)
    {
        var ui = ManagerRegistry.Get<UIManager>();
        if (ui == null) return;
        ui.Open<CommonTipPanel>(new TipArgs { Text = text, ScreenPosition = MouseScreenPosition() });
    }

    /// <summary>关掉提示框。</summary>
    public static void Hide()
    {
        var ui = ManagerRegistry.Get<UIManager>();
        if (ui != null)
            ui.Close<CommonTipPanel>();
    }
}
