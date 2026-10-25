using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class DialogManagerUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private RectTransform chatContent;
    [SerializeField] private ChatBubbleLeftView leftBubblePrefab;
    [SerializeField] private ChatBubbleMiddleView middleBubblePrefab;
    [SerializeField] private ChatBubbleRightView rightBubblePrefab;
    [SerializeField] private ChatBubbleOptionView optionBubblePrefab;

    [Header("跳字")]
    [SerializeField] private bool enableTypewriter = true;
    [SerializeField] private float typewriterSpeed = 30f;

    private readonly List<BubbleTintController> _bubbleTints = new List<BubbleTintController>();
    private readonly List<RaycastResult> _raycastResults = new List<RaycastResult>();
    private PointerEventData _pointerEventData;
    private ScrollRect _scrollRect;
    private RectTransform _scrollViewRect;
    private bool _blockAdvance;
    private bool _optionChosen;
    private RectTransform _lastRightBubbleBeforeOption;
    private bool _pendingCollapse;
    private TypewriterEffect _currentTypewriter;
    private ChatBubbleOptionView _currentOptionBubble;
    private List<OptionInfo> _currentOptions;
    private bool[] _currentInteractables;

    public void Initialize(
        RectTransform contentRect,
        ChatBubbleLeftView leftPrefab,
        ChatBubbleMiddleView middlePrefab,
        ChatBubbleRightView rightPrefab,
        ChatBubbleOptionView optionPrefab)
    {
        chatContent = contentRect;
        leftBubblePrefab = leftPrefab;
        middleBubblePrefab = middlePrefab;
        rightBubblePrefab = rightPrefab;
        optionBubblePrefab = optionPrefab;

        _scrollRect = contentRect.GetComponentInParent<ScrollRect>();
        if (_scrollRect != null)
            _scrollViewRect = _scrollRect.GetComponent<RectTransform>();
    }

    private DialogManager _dialogManager;

    private void OnEnable()
    {
        _dialogManager = ManagerRegistry.Get<DialogManager>();
        if (_dialogManager == null) return;

        _dialogManager.OnContent += OnContent;
        _dialogManager.OnOptions += OnOptions;
        _dialogManager.OnDialogBuff += OnDialogBuff;
        _dialogManager.OnDialogBattle += OnDialogBattle;
        _dialogManager.OnDialogEnded += OnDialogEnded;

        if (ConditionSystem.HasInstance)
            ConditionSystem.Instance.OnConditionChanged += OnConditionChanged;
    }

    private void OnDisable()
    {
        if (_dialogManager == null) return;

        _dialogManager.OnContent -= OnContent;
        _dialogManager.OnOptions -= OnOptions;
        _dialogManager.OnDialogEnded -= OnDialogEnded;

        if (ConditionSystem.HasInstance)
            ConditionSystem.Instance.OnConditionChanged -= OnConditionChanged;

        _currentOptionBubble = null;
        _currentOptions = null;
        _currentInteractables = null;
    }

    private void Update()
    {
        if (_dialogManager == null) return;
        if (_dialogManager.State != DialogState.Playing) return;

        if (_blockAdvance)
        {
            _blockAdvance = false;
            return;
        }

        if (!IsClickOrTapBegan()) return;
        if (IsPointerOverButtonOrSelectable()) return;
        if (!IsPointerOverScrollView()) return;

        if (enableTypewriter && _currentTypewriter != null && _currentTypewriter.IsTyping)
        {
            _currentTypewriter.Skip();
            return;
        }

        _dialogManager.Advance();
    }

    /// <summary>对话内容携带长期 buff：发放到玩家 + 弹出获得提示（SceneTip 的 Content_2y 变体）。</summary>
    private void OnDialogBuff(int buffId)
    {
        var buffMgr = BuffManager.Instance;
        if (buffMgr != null) buffMgr.ApplyBuff(buffId);
        SceneTip.ShowBuffGlobal(buffId);
        Debug.Log($"[DialogManagerUI] 对话发放长期 buff: {buffId}");
    }

    /// <summary>type=3 的对话：关闭对话后进入指定战斗。</summary>
    private void OnDialogBattle(int battleId)
    {
        if (battleId <= 0)
        {
            Debug.LogWarning("[DialogManagerUI] 对话要求进入战斗但 battleId 无效");
            return;
        }

        var app = GameApp.Instance;
        if (app == null)
        {
            Debug.LogWarning("[DialogManagerUI] GameApp 未就绪，无法进入战斗 " + battleId);
            return;
        }

        Debug.Log($"[DialogManagerUI] 对话结束 → 进入战斗 {battleId}");
        app.EnterBattle(battleId);
    }

    private void OnOptions(List<OptionInfo> options)
    {
        if (_currentTypewriter != null && _currentTypewriter.IsTyping)
            _currentTypewriter.Skip();
        _currentTypewriter = null;

        if (_pendingCollapse)
        {
            _pendingCollapse = false;
            _lastRightBubbleBeforeOption = null;
        }

        _optionChosen = false;
        _currentOptions = options;
        _currentOptionBubble = null;

        if (_bubbleTints.Count > 0)
        {
            var latest = _bubbleTints[_bubbleTints.Count - 1];
            if (latest != null && latest.GetComponent<ChatBubbleRightView>() != null)
                _lastRightBubbleBeforeOption = latest.GetComponent<RectTransform>();
        }

        var texts = new string[options.Count];
        var extends = new string[options.Count];
        var interactables = new bool[options.Count];
        var condSys = ConditionSystem.HasInstance ? ConditionSystem.Instance : null;

        for (int i = 0; i < options.Count; i++)
        {
            texts[i] = options[i].Text;
            extends[i] = options[i].FirstContentType == 0 ? "narrator" : "";
            int condId = options[i].ConditionId;
            interactables[i] = condId == 0 || (condSys != null && condSys.IsConditionMet(condId));
        }

        var opBubble = Instantiate(optionBubblePrefab, chatContent);
        _currentOptionBubble = opBubble;
        _currentInteractables = interactables;

        opBubble.Bind(texts, extends, interactables, index =>
        {
            if (_optionChosen) return;
            if (!interactables[index]) return;
            _optionChosen = true;
            _blockAdvance = true;

            if (_lastRightBubbleBeforeOption != null && !IsOptionAction(index))
                _pendingCollapse = true;

            RemoveFromTints(opBubble.gameObject);
            DestroyImmediate(opBubble.gameObject);
            _currentOptionBubble = null;
            _currentOptions = null;
            _currentInteractables = null;

            LayoutRebuilder.ForceRebuildLayoutImmediate(chatContent);
            ScrollToBottom();

            _dialogManager.ChooseOption(index);
        });

        LayoutRebuilder.ForceRebuildLayoutImmediate(chatContent);
        ScrollToBottom();
    }

    private void OnContent(SpeakerSide side, string text, string speakerName, string gainItemText)
    {
        if (_pendingCollapse && _lastRightBubbleBeforeOption != null)
        {
            _pendingCollapse = false;
            CollapseBubble(_lastRightBubbleBeforeOption);
            _lastRightBubbleBeforeOption = null;
        }

        TMP_Text mainText = null;
        GameObject bubbleRoot;
        switch (side)
        {
            case SpeakerSide.Left:
                var leftBubble = Instantiate(leftBubblePrefab, chatContent);
                leftBubble.SetText(text, gainItemText);
                mainText = leftBubble.MainText;
                bubbleRoot = leftBubble.gameObject;
                break;
            case SpeakerSide.Right:
                var rightBubble = Instantiate(rightBubblePrefab, chatContent);
                rightBubble.SetText(text, gainItemText);
                mainText = rightBubble.MainText;
                bubbleRoot = rightBubble.gameObject;
                break;
            default:
                var middleBubble = Instantiate(middleBubblePrefab, chatContent);
                middleBubble.SetText(text);
                mainText = middleBubble.MainText;
                bubbleRoot = middleBubble.gameObject;
                break;
        }

        if (enableTypewriter && mainText != null)
        {
            var tw = bubbleRoot.AddComponent<TypewriterEffect>();
            tw.Play(mainText, typewriterSpeed);
            _currentTypewriter = tw;
        }

        MarkAsLatest(bubbleRoot);
        LayoutRebuilder.ForceRebuildLayoutImmediate(bubbleRoot.GetComponent<RectTransform>());
        LayoutRebuilder.ForceRebuildLayoutImmediate(chatContent);
        ScrollToBottom();
    }

    private void OnDialogEnded()
    {
        ClearAllBubbles();
        _currentOptionBubble = null;
        _currentOptions = null;
        _currentInteractables = null;

        var uiManager = ManagerRegistry.Get<UIManager>();
        if (uiManager != null && uiManager.IsOpen<DialogWindow>())
            uiManager.Close<DialogWindow>();
    }

    private void OnConditionChanged(int conditionId, bool isMet)
    {
        if (_optionChosen) return;
        if (_currentOptionBubble == null || _currentOptions == null) return;

        bool needsRefresh = false;
        for (int i = 0; i < _currentOptions.Count; i++)
        {
            if (_currentOptions[i].ConditionId == conditionId)
            {
                needsRefresh = true;
                break;
            }
        }

        if (!needsRefresh) return;

        var condSys = ConditionSystem.HasInstance ? ConditionSystem.Instance : null;
        for (int i = 0; i < _currentOptions.Count; i++)
        {
            int condId = _currentOptions[i].ConditionId;
            _currentInteractables[i] = condId == 0 || (condSys != null && condSys.IsConditionMet(condId));
        }

        var texts = new string[_currentOptions.Count];
        var extends = new string[_currentOptions.Count];
        for (int i = 0; i < _currentOptions.Count; i++)
        {
            texts[i] = _currentOptions[i].Text;
            extends[i] = _currentOptions[i].FirstContentType == 0 ? "narrator" : "";
        }

        _currentOptionBubble.Bind(texts, extends, _currentInteractables, index =>
        {
            if (_optionChosen) return;
            if (!_currentInteractables[index]) return;
            _optionChosen = true;
            _blockAdvance = true;

            if (_lastRightBubbleBeforeOption != null && !IsOptionAction(index))
                _pendingCollapse = true;

            RemoveFromTints(_currentOptionBubble.gameObject);
            DestroyImmediate(_currentOptionBubble.gameObject);
            _currentOptionBubble = null;
            _currentOptions = null;
            _currentInteractables = null;

            LayoutRebuilder.ForceRebuildLayoutImmediate(chatContent);
            ScrollToBottom();

            _dialogManager.ChooseOption(index);
        });

        LayoutRebuilder.ForceRebuildLayoutImmediate(chatContent);
        ScrollToBottom();
    }

    private void MarkAsLatest(GameObject bubbleRoot)
    {
        for (int i = _bubbleTints.Count - 1; i >= 0; i--)
        {
            if (_bubbleTints[i] == null)
                _bubbleTints.RemoveAt(i);
        }

        var tint = bubbleRoot.GetComponent<BubbleTintController>();
        if (tint == null)
            tint = bubbleRoot.AddComponent<BubbleTintController>();

        for (int i = 0; i < _bubbleTints.Count; i++)
            _bubbleTints[i].SetDimmed(true);

        tint.SetDimmed(false);

        if (!_bubbleTints.Contains(tint))
            _bubbleTints.Add(tint);
    }

    private void ClearAllBubbles()
    {
        _currentTypewriter = null;
        _currentOptionBubble = null;
        _currentOptions = null;
        _currentInteractables = null;
        for (int i = chatContent.childCount - 1; i >= 0; i--)
            Destroy(chatContent.GetChild(i).gameObject);
        _bubbleTints.Clear();
        _lastRightBubbleBeforeOption = null;
        _pendingCollapse = false;
    }

    private bool IsOptionAction(int optionIndex)
    {
        var current = _dialogManager?.CurrentDialog;
        if (current == null || string.IsNullOrEmpty(current.Param2)) return false;
        var parts = current.Param2.Split('|');
        return optionIndex < parts.Length && parts[optionIndex].Trim() == "2";
    }

    private void CollapseBubble(RectTransform bubble)
    {
        if (bubble == null) return;

        var csf = bubble.GetComponent<ContentSizeFitter>();
        if (csf != null) csf.enabled = false;

        float currentHeight = bubble.rect.height;
        float newHeight = Mathf.Max(currentHeight - 70f, 0f);
        bubble.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, newHeight);

        var le = bubble.GetComponent<LayoutElement>();
        if (le == null) le = bubble.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = newHeight;

        LayoutRebuilder.ForceRebuildLayoutImmediate(chatContent);
    }

    private void RemoveFromTints(GameObject go)
    {
        for (int i = _bubbleTints.Count - 1; i >= 0; i--)
        {
            if (_bubbleTints[i] == null || _bubbleTints[i].gameObject == go)
                _bubbleTints.RemoveAt(i);
        }
    }

    private void ScrollToBottom()
    {
        if (_scrollRect != null)
            _scrollRect.verticalNormalizedPosition = 0f;
    }

    private bool IsClickOrTapBegan()
    {
        var inputManager = ManagerRegistry.Get<InputManager>();
        if (inputManager != null && inputManager.GameInput.Dialog.enabled)
            return inputManager.GameInput.Dialog.Advance.WasPressedThisFrame();

        return false;
    }

    private Vector2 GetPointerPosition()
    {
#if ENABLE_INPUT_SYSTEM
        if (Mouse.current != null)
            return Mouse.current.position.ReadValue();
#endif
        return Input.mousePosition;
    }

    private bool IsPointerOverButtonOrSelectable()
    {
        if (EventSystem.current == null) return false;

        if (_pointerEventData == null)
            _pointerEventData = new PointerEventData(EventSystem.current);

        _pointerEventData.position = GetPointerPosition();

        _raycastResults.Clear();
        EventSystem.current.RaycastAll(_pointerEventData, _raycastResults);

        for (int i = 0; i < _raycastResults.Count; i++)
        {
            var go = _raycastResults[i].gameObject;
            if (go == null) continue;
            if (go.GetComponentInParent<Button>() != null) return true;
            if (go.GetComponentInParent<Selectable>() != null) return true;
        }
        return false;
    }

    private bool IsPointerOverScrollView()
    {
        if (_scrollViewRect == null) return false;
        return RectTransformUtility.RectangleContainsScreenPoint(
            _scrollViewRect, GetPointerPosition(), null);
    }
}
