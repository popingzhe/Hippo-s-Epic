using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

// 只转发悬浮，不注册拖拽事件，ScrollRect 仍能收到拖动。
public sealed class ChatBubbleOptionHover : MonoBehaviour, IPointerEnterHandler
{
    private ChatBubbleOptionView _owner;
    private Button _button;

    internal void Initialize(ChatBubbleOptionView owner, Button button)
    {
        _owner = owner;
        _button = button;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_owner != null) _owner.EnterOption(_button);
    }

    private void OnDisable()
    {
        if (_owner != null) _owner.ForgetOption(_button);
    }
}
