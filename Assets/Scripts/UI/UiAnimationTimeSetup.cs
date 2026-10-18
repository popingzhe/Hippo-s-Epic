using DG.Tweening;
using UnityEngine;

/// <summary>
/// UI 动画统一走独立时间：开背包/任务面板时 GameplayState 会把 Time.timeScale 设为 0，
/// DOTween 默认吃缩放时间，UI 动画会原地冻住（表现为"展开/收起点了没反应"）。
/// DOTweenSettings.asset 里已把 defaultTimeScaleIndependent 置 1，但该资源只在 DOTween
/// 初始化时读一次、改完要重启编辑器才生效；这里每次进 Play 再强制一次，保证一定生效。
/// 注意：本文件不能放进 Assets/Scripts/Manager（Manager.asmdef 不引用 DOTween），
/// 必须留在默认的 Assembly-CSharp 里。
/// </summary>
public static class UiAnimationTimeSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        DOTween.defaultTimeScaleIndependent = true;
    }
}
