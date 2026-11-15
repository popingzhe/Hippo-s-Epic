using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>面板 OnOpen 需要的参数形态：交互按钮按此转换参数，避免传错类型静默失效。</summary>
public enum UIPanelArgs
{
    None = 0,      // 不需要参数（传 null）
    Int = 1,       // 需要整数（对话ID / 物品ID），0 视为未配置
    MapRootId = 2, // 地图编号（0 有效）
    TipArgs = 3    // 需要 TipArgs 结构：交互按钮不支持
}

public static class UIWindowRegistry
{
    private static readonly Dictionary<string, Action<UIManager, object>> openers =
        new Dictionary<string, Action<UIManager, object>>();

    private static readonly Dictionary<string, UIPanelArgs> argKinds =
        new Dictionary<string, UIPanelArgs>();

    private static readonly Dictionary<string, string> addresses =
        new Dictionary<string, string>();

    // 有专属打开流程的面板（要同步 GameplayState 的暂停/输入/互斥标记），否则面板自己的关闭按钮关不掉
    private static readonly Dictionary<string, Func<UIManager, object, bool>> specialOpeners =
        new Dictionary<string, Func<UIManager, object, bool>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterAll()
    {
        openers.Clear();
        argKinds.Clear();
        addresses.Clear();
        specialOpeners.Clear();
        Register<DialogWindow>("Assets/Prefabs/UI/Dialog/DialogWindow", UIPanelArgs.Int);
        Register<MainMenuPanel>("Assets/Prefabs/UI/Global/MainMenuPanel");
        Register<LoadingPanel>("Assets/Prefabs/UI/Global/LoadingPanel");
        Register<BagPanel>("Assets/Prefabs/UI/Bag/BagPanel", UIPanelArgs.None, OpenBagThroughState);
        Register<UseItemTip>("Assets/Prefabs/UI/Bag/UseItemTip", UIPanelArgs.Int);
        Register<CommonTipPanel>("Assets/Prefabs/UI/common/CommonTipPanel", UIPanelArgs.TipArgs);
        Register<TaskPanel>("Assets/Prefabs/UI/task/TaskPanel", UIPanelArgs.None, OpenTaskThroughState);
        Register<MapPanel>("Assets/Prefabs/UI/map/MapPanel", UIPanelArgs.MapRootId);
        Register<MainPanelController>("Assets/Prefabs/UI/Global/MainPanel");
        Register<BattleWindow>("Assets/Prefabs/UI/Battle/BattleWindow");
        Register<TestUIPanel>("Assets/Prefabs/UI/Test/TestUIPanel");
    }

    private static void Register<T>(string address, UIPanelArgs args = UIPanelArgs.None,
        Func<UIManager, object, bool> special = null) where T : UIWindow
    {
        UIManager.RegisterAddress(typeof(T), address);
        string key = typeof(T).FullName;
        openers[key] = (manager, argsObj) => manager.Open<T>(argsObj);
        argKinds[key] = args;
        addresses[key] = address;
        if (special != null)
            specialOpeners[key] = special;
    }

    /// <summary>背包要走 GameplayState 的打开流程，才能同步 _bagOpen / timeScale / 输入模式。</summary>
    private static bool OpenBagThroughState(UIManager manager, object args)
    {
        var app = GameApp.Instance;
        if (app != null && app.StateMachine.Current is GameplayState gameplay)
        {
            gameplay.OpenBagPublic();
            return true;
        }
        manager.Open<BagPanel>(args);
        return true;
    }

    /// <summary>任务面板同理：CloseTaskIfOpen 依赖 _taskOpen。</summary>
    private static bool OpenTaskThroughState(UIManager manager, object args)
    {
        var app = GameApp.Instance;
        if (app != null && app.StateMachine.Current is GameplayState gameplay)
        {
            gameplay.OpenTaskPublic();
            return true;
        }
        manager.Open<TaskPanel>(args);
        return true;
    }

    /// <summary>注册的 Addressables 地址（配置/回归测试用）。</summary>
    public static string GetAddress(string key)
    {
        EnsureRegistered();
        return key != null && addresses.TryGetValue(key.Trim(), out var address) ? address : null;
    }

    /// <summary>配置下拉和运行时打开使用同一份注册；编辑模式也可查询。只列交互按钮能打开的面板。</summary>
    public static string[] GetWindowKeys()
    {
        EnsureRegistered();
        var keys = new List<string>();
        foreach (var kvp in argKinds)
            if (kvp.Value != UIPanelArgs.TipArgs)
                keys.Add(kvp.Key);
        keys.Sort(StringComparer.Ordinal);
        return keys.ToArray();
    }

    /// <summary>面板需要的参数形态；未注册返回 None。</summary>
    public static UIPanelArgs GetArgsKind(string key)
    {
        EnsureRegistered();
        return key != null && argKinds.TryGetValue(key.Trim(), out var kind) ? kind : UIPanelArgs.None;
    }

    /// <summary>按面板参数形态把配置的整数转成 OnOpen 参数；失败给出原因。</summary>
    public static bool TryBuildArgs(string key, int param, out object args, out string error)
    {
        args = null;
        error = null;
        switch (GetArgsKind(key))
        {
            case UIPanelArgs.None:
                return true;
            case UIPanelArgs.Int:
                if (param <= 0)
                {
                    error = "该面板需要参数ID（对话ID/物品ID），当前配置为 " + param;
                    return false;
                }
                args = param;
                return true;
            case UIPanelArgs.MapRootId:
                args = (MapRootId)param;
                return true;
            default:
                error = "该面板需要 TipArgs 结构，交互按钮不支持";
                return false;
        }
    }

    /// <summary>按注册 key 请求打开窗口，仍通过 UIManager 处理加载、层级、互斥和生命周期。</summary>
    public static bool TryOpen(UIManager manager, string key, int param, out string error)
    {
        EnsureRegistered();
        error = null;
        if (string.IsNullOrWhiteSpace(key))
        {
            error = "未配置目标UI面板";
            return false;
        }
        key = key.Trim();
        if (!openers.ContainsKey(key))
        {
            error = "面板未注册: " + key;
            return false;
        }
        if (!TryBuildArgs(key, param, out var args, out error))
            return false;
        if (manager == null || !manager.isActiveAndEnabled)
        {
            error = "UIManager 不可用";
            return false;
        }
        if (specialOpeners.TryGetValue(key, out var special))
            return special(manager, args);

        openers[key](manager, args);
        return true;
    }

    private static void EnsureRegistered()
    {
        if (openers.Count == 0)
            RegisterAll();
    }
}
