using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class ChatBubbleOptionView : MonoBehaviour
{
    // 一行的宽度预算 = 9 个全角字的实测宽度；窄字符（数字/字母/半角标点）自然能多排几个。
    private const int LineBudgetCharacters = 9;
    private const int MaxLineLength = 18;
    private const int MaxOptionSlots = 6;
    // 近的浅、远的深：中心保持原色，紧邻项 190，再远 153。
    private const float NearHoverGray = 190f / 255f;
    private const float FarHoverGray = 153f / 255f;
    private static readonly Regex ExplicitLineBreak = new Regex(@"\r\n|\r|\n|<br\s*/?>", RegexOptions.IgnoreCase);

    [Header("UI")]
    [SerializeField] private GameObject op1;
    [SerializeField] private Button op1Button;
    [SerializeField] private TMP_Text op1Text;

    [SerializeField] private Color disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);
    private Color _normalColor = Color.white;
    private bool _normalColorCaptured;
    private readonly List<HoverBackground> _hoverBackgrounds = new List<HoverBackground>(MaxOptionSlots);
    private Button _lastHoveredButton;
    private Transform[] _originalDrawOrder;

    private struct HoverBackground
    {
        public Button Button;
        public Image Image;
        public Color OriginalColor;
        public float CenterY;
    }

    public void Bind(string[] texts, string[] extends, bool[] interactables, Action<int> onChoose)
    {
        var lastHovered = _lastHoveredButton;
        RestoreHoverPresentation();
        _hoverBackgrounds.Clear();

        for (int index = 0; index < MaxOptionSlots; index++)
            SetupSlot(index, texts, extends, interactables, onChoose);

        ApplyHalfHeight();
        for (int i = 0; i < _hoverBackgrounds.Count; i++)
        {
            var entry = _hoverBackgrounds[i];
            var rect = entry.Image.rectTransform;
            entry.CenterY = transform.InverseTransformPoint(rect.TransformPoint(rect.rect.center)).y;
            _hoverBackgrounds[i] = entry;
        }

        // 按画面实际的上下顺序，不按 Op 编号或层级顺序。
        _hoverBackgrounds.Sort((a, b) => b.CenterY.CompareTo(a.CenterY));
        if (lastHovered != null)
            EnterOption(lastHovered);
        else if (_originalDrawOrder != null && gameObject.activeInHierarchy)
        {
            // 停用期间不能改同级顺序，回到激活状态后再归位。
            ApplySiblingOrder(_originalDrawOrder);
            _originalDrawOrder = null;
        }
    }

    internal void EnterOption(Button button)
    {
        if (!isActiveAndEnabled || button == null || button == _lastHoveredButton) return;

        int center = _hoverBackgrounds.FindIndex(entry => entry.Button == button);
        if (center < 0) return;
        // 鼠标移开时保留这个配色中心，直到悬浮到另一个选项。
        _lastHoveredButton = button;

        for (int i = 0; i < _hoverBackgrounds.Count; i++)
        {
            var entry = _hoverBackgrounds[i];
            if (i == center)
            {
                // 悬浮项继续用 Button 本来的高亮和按下状态。
                entry.Image.color = entry.OriginalColor;
                continue;
            }

            float gray = Mathf.Abs(i - center) == 1 ? NearHoverGray : FarHoverGray;
            entry.Image.color = new Color(gray, gray, gray, entry.OriginalColor.a);
        }

        ApplyHoverDrawOrder(center);
    }

    private void ApplyHoverDrawOrder(int center)
    {
        // 只有自由摆放的同级节点才能用 sibling 顺序叠放，不介入 LayoutGroup 的排版。
        if (GetComponent<ILayoutGroup>() != null) return;

        if (_originalDrawOrder == null)
        {
            _originalDrawOrder = new Transform[transform.childCount];
            for (int i = 0; i < _originalDrawOrder.Length; i++)
                _originalDrawOrder[i] = transform.GetChild(i);
        }

        var visible = new List<int>(_hoverBackgrounds.Count);
        var visibleSlots = new HashSet<int>();
        for (int i = 0; i < _hoverBackgrounds.Count; i++)
        {
            var image = _hoverBackgrounds[i].Image;
            if (image == null || !image.gameObject.activeInHierarchy) continue;
            int slot = Array.IndexOf(_originalDrawOrder, image.transform);
            if (slot < 0) continue;
            visible.Add(i);
            visibleSlots.Add(slot);
        }

        // 离悬浮中心越近越靠上；同距离保持原本的上下顺序（下方先画）。
        visible.Sort((a, b) =>
        {
            int distanceA = Mathf.Abs(a - center);
            int distanceB = Mathf.Abs(b - center);
            return distanceA != distanceB ? distanceB.CompareTo(distanceA) : b.CompareTo(a);
        });

        // 隐藏槽位和装饰节点留在原位，只把显示中的选项按上面的次序填回它们的槽位。
        var order = (Transform[])_originalDrawOrder.Clone();
        int destination = 0;
        foreach (int entry in visible)
        {
            int source = Array.IndexOf(_originalDrawOrder, _hoverBackgrounds[entry].Image.transform);
            while (destination < order.Length && !visibleSlots.Contains(destination)) destination++;
            if (destination >= order.Length) break;
            order[destination++] = _originalDrawOrder[source];
        }

        ApplySiblingOrder(order);
    }

    private void ApplySiblingOrder(Transform[] order)
    {
        for (int i = 0; i < order.Length; i++)
        {
            var child = order[i];
            if (child != null && child.parent == transform && child.GetSiblingIndex() != i)
                child.SetSiblingIndex(i);
        }
    }

    internal void ForgetOption(Button button)
    {
        if (_lastHoveredButton == button)
            RestoreHoverPresentation();
    }

    private void OnDisable()
    {
        RestoreHoverPresentation();
    }

    private void RestoreHoverPresentation()
    {
        if (_lastHoveredButton == null && _originalDrawOrder == null) return;
        _lastHoveredButton = null;
        foreach (var entry in _hoverBackgrounds)
        {
            if (entry.Image != null)
                entry.Image.color = entry.OriginalColor;
        }
        // Unity 禁止在父物体激活/停用的调用栈里改同级顺序（OnDisable 期间就会报错），
        // 那时保留缓存，等重新激活后的下一次 Bind 再归位。
        if (_originalDrawOrder != null && gameObject.activeInHierarchy)
        {
            ApplySiblingOrder(_originalDrawOrder);
            _originalDrawOrder = null;
        }
    }

    private void ApplyHalfHeight()
    {
        var rectTransform = (RectTransform)transform;
        LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);

        var fitter = GetComponent<ContentSizeFitter>();
        if (fitter != null && fitter.enabled)
        {
            float fullHeight = rectTransform.rect.height;
            fitter.enabled = false;

            float halfHeight = fullHeight / 2f;
            rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, halfHeight);

            var layoutElement = GetComponent<LayoutElement>();
            if (layoutElement == null)
                layoutElement = gameObject.AddComponent<LayoutElement>();
            layoutElement.preferredHeight = halfHeight;
        }
    }

    private void SetupSlot(int index, string[] texts, string[] extends, bool[] interactables, Action<int> onChoose)
    {
        string slotName = "Op" + (index + 1);
        GameObject normalObj = index == 0 ? op1 : null;
        Button normalBtn = index == 0 ? op1Button : null;
        TMP_Text normalTxt = index == 0 ? op1Text : null;
        GameObject extendObj = null;
        Button extendBtn = null;
        TMP_Text extendTxt = null;
        ResolveSlot(slotName, ref normalObj, ref normalBtn, ref normalTxt);
        ResolveSlot(slotName + "b", ref extendObj, ref extendBtn, ref extendTxt);

        if (!_normalColorCaptured && index == 0 && normalTxt != null)
        {
            _normalColor = normalTxt.color;
            _normalColorCaptured = true;
        }

        bool hasOption = index < texts.Length && !string.IsNullOrEmpty(texts[index]);
        bool wantsExtended = index < extends.Length && !string.IsNullOrEmpty(extends[index]);
        bool showNormal = hasOption && !wantsExtended && normalObj != null && normalBtn != null && normalTxt != null;
        bool showExtended = hasOption && wantsExtended && extendObj != null && extendBtn != null && extendTxt != null;
        string label = hasOption ? texts[index] : string.Empty;
        bool interactable = index < interactables.Length && interactables[index];

        SetSlot(normalObj, normalBtn, normalTxt, showNormal, label, interactable, index, onChoose);
        SetSlot(extendObj, extendBtn, extendTxt, showExtended, label, interactable, index, onChoose);
    }

    private void ResolveSlot(string name, ref GameObject slot, ref Button button, ref TMP_Text text)
    {
        if (slot == null)
        {
            var child = transform.Find(name);
            if (child == null) return;
            slot = child.gameObject;
        }

        if (button == null) button = slot.GetComponentInChildren<Button>(true);
        if (text == null && button != null) text = button.GetComponentInChildren<TMP_Text>(true);
    }

    private void SetSlot(GameObject slot, Button button, TMP_Text text, bool show, string label,
        bool interactable, int index, Action<int> onChoose)
    {
        if (button != null) button.onClick.RemoveAllListeners();
        if (slot == null) return;
        slot.SetActive(show);
        if (!show) return;

        text.text = FormatOptionText(label, text);
        text.color = interactable ? _normalColor : disabledColor;
        button.interactable = interactable;
        button.onClick.AddListener(() => onChoose?.Invoke(index));

        // 背景的 LayoutGroup 读取子节点实际尺寸，先让文字的 Fitter 完成，再重算背景。
        LayoutRebuilder.ForceRebuildLayoutImmediate(text.rectTransform);
        if (slot.transform is RectTransform rect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);

        if (!slot.TryGetComponent<Image>(out var background)) return;
        _hoverBackgrounds.Add(new HoverBackground
        {
            Button = button,
            Image = background,
            OriginalColor = background.color
        });

        if (!button.TryGetComponent<ChatBubbleOptionHover>(out var hover))
            hover = button.gameObject.AddComponent<ChatBubbleOptionHover>();
        hover.Initialize(this, button);
    }

    private static string FormatOptionText(string text, TMP_Text probe)
    {
        // 保留配表中已有的分行（包括 TMP 的 <br>）；每个手动分段各自按宽度排。
        string[] paragraphs = ExplicitLineBreak.Split(text);
        for (int i = 0; i < paragraphs.Length; i++)
            paragraphs[i] = WrapOptionLine(paragraphs[i], probe);
        return string.Join("\n", paragraphs);
    }

    // 按实测字宽贪心排行：预算 = 9 个全角字，窄字符能多排，不再按字符个数一刀切。
    private static string WrapOptionLine(string text, TMP_Text probe)
    {
        if (probe == null) return WrapOptionLineByCount(text, LineBudgetCharacters, MaxLineLength);

        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int count = starts.Length;
        if (count <= 1) return text;

        float budget = MeasureWidth(probe, new string('中', LineBudgetCharacters));
        if (budget <= 0f) return WrapOptionLineByCount(text, LineBudgetCharacters, MaxLineLength);

        // 前缀宽度：prefix[i] = 前 i 个文本元素的实测宽度（含字距），比逐字累加准。
        var prefix = new float[count + 1];
        for (int i = 1; i <= count; i++)
            prefix[i] = MeasureWidth(probe, text.Substring(0, ElementStart(text, starts, i)));

        if (prefix[count] <= budget) return text;

        var result = new StringBuilder(text.Length + 8);
        int lineStart = 0;
        while (lineStart < count)
        {
            int lineEnd = lineStart + 1;
            while (lineEnd < count && prefix[lineEnd + 1] - prefix[lineStart] <= budget) lineEnd++;

            if (lineEnd < count)
            {
                int breakAt = PreferSpaceBreak(text, starts, lineStart, lineEnd);
                if (breakAt > lineStart) lineEnd = breakAt;
                // 单个文本元素超过预算时也要前进，避免死循环。
                if (lineEnd <= lineStart) lineEnd = lineStart + 1;
            }

            result.Append(text, starts[lineStart], ElementStart(text, starts, lineEnd) - starts[lineStart]).Append('\n');
            lineStart = lineEnd;
            while (lineStart < count && char.IsWhiteSpace(text[starts[lineStart]])) lineStart++;
        }
        return result.ToString(0, result.Length - 1);
    }

    private static float MeasureWidth(TMP_Text probe, string value)
    {
        if (string.IsNullOrEmpty(value)) return 0f;
        return probe.GetPreferredValues(value).x;
    }

    private static int PreferSpaceBreak(string text, int[] starts, int lineStart, int overflow)
    {
        // 只有会把英文/数字单词劈成两半时才回退到空格，中文按宽度直接断。
        if (overflow >= starts.Length || !IsAsciiWordChar(text[starts[overflow]])) return overflow;
        for (int i = overflow - 1; i > lineStart; i--)
            if (char.IsWhiteSpace(text[starts[i]])) return i;
        return overflow;
    }

    private static bool IsAsciiWordChar(char c)
    {
        return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
    }

    private static int ElementStart(string text, int[] starts, int index)
    {
        return index < starts.Length ? starts[index] : text.Length;
    }

    // 没有 TMP 探针时的兜底：仍按字符数（全角=1，半角≈0.5）估算。
    private static string WrapOptionLineByCount(string text, int lineCharacters, int maxCharacters)
    {
        int[] starts = StringInfo.ParseCombiningCharacters(text);
        int count = starts.Length;
        if (count <= lineCharacters) return text;

        if (count <= 2 * maxCharacters)
        {
            int split = count <= maxCharacters ? lineCharacters : (count + 1) / 2;
            return text.Insert(starts[split], "\n");
        }

        var result = new StringBuilder(text.Length);
        int start = 0;
        for (int i = maxCharacters; i < count; i += maxCharacters)
        {
            int end = starts[i];
            result.Append(text, start, end - start).Append('\n');
            start = end;
        }
        return result.Append(text, start, text.Length - start).ToString();
    }
}
