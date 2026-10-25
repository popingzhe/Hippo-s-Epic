using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class InteractionPhase
{
    [Tooltip("状态值，与 SaveManager 存档中的 EntityState 对应\nInteractableManager 初始化时根据存档选择对应阶段")]
    public int state;

    [Tooltip("玩家进入范围时显示的提示文字，如 \"按 E 交谈\"")]
    public string hintText = UIStrings.Interaction.DefaultHint;

    [Tooltip("可选按钮列表，每个按钮可触发不同的交互操作\n玩家可通过键盘按键或点击UI按钮触发")]
    public List<ButtonOption> buttons = new List<ButtonOption>();

    [Tooltip("实时监测条件列表，条件满足时自动切换到目标状态\n无需玩家交互，每帧检测")]
    public List<ConditionTrigger> conditionTriggers = new List<ConditionTrigger>();

    [Tooltip("此状态下激活的 Style 子节点名称列表\nStyle 下的子节点代表不同样式，未在列表中的 Style 子节点将被停用")]
    public List<string> activeChildNames = new List<string>();

    [Tooltip("此状态下是否播放动画")]
    public bool playAnimation;
    [Tooltip("此状态下播放动画的 AnimationController 组件\n留空则不播放动画")]
    public AnimationController animationController;
    [Tooltip("此状态下播放的组合动画名称")]
    public CompositionName composition = CompositionName.Idle;

    [Tooltip("是否可重复交互。false 时首次交互后不再响应")]
    public bool canRepeat = true;
    [Tooltip("交互后隐藏提示UI")]
    public bool hideAfterExecute;
    [Tooltip("交互后销毁此GameObject")]
    public bool destroySelf;
    [Tooltip("交互后禁用此GameObject（SetActive(false)）")]
    public bool deactivateSelf;

    [Tooltip("勾选后：玩家进入交互范围时，直接触发本阶段第一个可用按钮（等同玩家点了它），不再等待按键/点击。\n无可用按钮（条件都不满足）时不触发。")]
    public bool autoTriggerFirstButton;

    [HideInInspector]
    [SerializeField]
    private InteractionType _legacyType;
    [HideInInspector]
    [SerializeField]
    private string _legacyHintButton = "E";
    [HideInInspector]
    [SerializeField]
    private int _legacyDataId;
    [HideInInspector]
    [SerializeField]
    private string _legacyParam1;
    [HideInInspector]
    [SerializeField]
    private string _legacyParam2;
    [HideInInspector]
    [SerializeField]
    private int _legacyTransitionToState = -1;

    public void MigrateIfNeeded()
    {
        if (buttons.Count > 0)
            return;

        if (_legacyType == InteractionType.HintOnly && string.IsNullOrEmpty(_legacyHintButton))
            return;

        buttons.Add(new ButtonOption
        {
            buttonText = !string.IsNullOrEmpty(_legacyHintButton) ? _legacyHintButton : UIStrings.Interaction.DefaultButton,
            type = _legacyType,
            dataId = _legacyDataId,
            param1 = _legacyParam1,
            param2 = _legacyParam2,
            transitionToState = _legacyTransitionToState
        });

        _legacyType = InteractionType.HintOnly;
        _legacyHintButton = "";
        _legacyDataId = 0;
        _legacyParam1 = "";
        _legacyParam2 = "";
        _legacyTransitionToState = -1;
    }
}

[Serializable]
public class ConditionTrigger
{
    [Tooltip("条件表ID，通过 ConditionSystem 检测是否满足")]
    public int conditionId;
    [Tooltip("条件满足后切换到的状态值，-1 表示不切换")]
    public int transitionToState = -1;
}

[Serializable]
public class ButtonOption
{
    [Tooltip("按钮显示文字，如 \"E\"、\"交谈\"、\"拾取\"")]
    public string buttonText = UIStrings.Interaction.DefaultButton;

    [Header("操作")]
    [Tooltip("交互类型：HintOnly=仅提示 | Dialogue=对话 | Pickup=拾取 | Consume=消耗物品 | Teleport=传送 | SetCondition=修改条件满足情况")]
    public InteractionType type;
    [Tooltip("Dialogue → 对话表ID\nPickup/Consume → 物品ID\nSetCondition → 目标条件ID（condition 表 id）")]
    public int dataId;
    [Tooltip("Teleport(场景内) → 目标地图名（Map名称）\nTeleport(跨场景) → 留空，使用 param2\nPickup/Consume → 扣除/添加数量（默认1）\nSetCondition → 目标状态：1/true/留空=设为已满足，0/false=设为未满足")]
    public string param1;
    [Tooltip("Teleport(跨场景) → 目标场景名（如 Scene_Staff_Lounge）\n需要 GameApp 存在才能跨场景传送")]
    public string param2;
    [Tooltip("传送模式：0=传送到交互点，1=跨场景")]
    public int teleportMode;

    [Header("条件")]
    [Tooltip("关联条件表ID，0 表示无条件限制\n条件不满足时此按钮隐藏")]
    public int conditionId;

    [Header("行为")]
    [Tooltip("点击后转换到的状态值，-1 表示不转换状态\n转换后会重新匹配 phases 中对应 state 的阶段")]
    public int transitionToState = -1;
}

public enum InteractionType
{
    HintOnly,
    Dialogue,
    Pickup,
    Teleport,
    Consume,
    /// <summary>修改条件满足情况：dataId=条件ID，param1=目标状态（1/true/空=已满足，0/false=未满足）</summary>
    SetCondition,

    /// <summary>获得Buff（长期/永久都行）：dataId=Buff表ID，param1=是否弹获得提示（1/空=弹，0=false=不弹）</summary>
    GainBuff,

    /// <summary>失去Buff：dataId=Buff表ID（只移除，不弹提示）</summary>
    LoseBuff
}