using UnityEngine;

public enum UILayer
{
    /// <summary>HUD 常驻底栏（主界面 MainPanel 等）：永远在所有窗口之下。</summary>
    Hud = -100,
    Normal = 0,
    Popup = 100,
    Top = 200
}

public abstract class UIWindow : MonoBehaviour
{
    public virtual UILayer Layer => UILayer.Normal;

    /// <summary>
    /// 互斥组：同一非空组内同时只允许存在一个窗口，后开的会自动关掉先开的。
    /// 例如背包与任务面板都属于 "MainPanels"。
    /// </summary>
    public virtual string ExclusiveGroup => null;

    public virtual void OnCreate(object args) { }
    public virtual void OnOpen(object args) { }
    public virtual void OnClose() { }
}