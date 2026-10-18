using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 让某个子节点单独按 UILayer 排序（内部自动挂嵌套 Canvas + GraphicRaycaster）。
///
/// 用途举例：TaskPanel 的 bgTask 需要盖在 HUD 之上，但整个 TaskPanel 仍保持 Normal。
///
/// 注意两点：
/// 1) 会把这个节点**及其所有子节点**一起提升到该层；
/// 2) 子 Canvas 必须有自己的 GraphicRaycaster，否则里面的按钮/滚动条会点不动
///    （GraphicRaycaster 只检测注册在自己 canvas 下的图形）；
/// 3) 同面板内原本留在低层的按钮会被这块内容盖住 —— 需要把可交互元素一起放进这个子树，
///    或者给它们各自挂本组件。
/// </summary>
[RequireComponent(typeof(RectTransform))]
[DisallowMultipleComponent]
public class UILayerOverride : MonoBehaviour
{
    [SerializeField] private UILayer layer = UILayer.Popup;

    private void OnEnable()
    {
        Apply();
    }

    private void Apply()
    {
        var canvas = GetComponent<Canvas>();
        if (canvas == null)
        {
            canvas = gameObject.AddComponent<Canvas>();
            // 子 Canvas 不补 raycaster 会失去点击（踩过的坑）
            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
        }

        canvas.overrideSorting = true;
        canvas.sortingOrder = (int)layer;
    }
}
