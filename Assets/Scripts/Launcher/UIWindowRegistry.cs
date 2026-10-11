using UnityEngine;

public static class UIWindowRegistry
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterAll()
    {
        UIManager.RegisterAddress(typeof(DialogWindow), "Assets/Prefabs/UI/Dialog/DialogWindow");
        UIManager.RegisterAddress(typeof(MainMenuPanel), "Assets/Prefabs/UI/Global/MainMenuPanel");
        UIManager.RegisterAddress(typeof(LoadingPanel), "Assets/Prefabs/UI/Global/LoadingPanel");
        UIManager.RegisterAddress(typeof(BagPanel), "Assets/Prefabs/UI/Bag/BagPanel");
        UIManager.RegisterAddress(typeof(UseItemTip), "Assets/Prefabs/UI/Bag/UseItemTip");
        UIManager.RegisterAddress(typeof(CommonTipPanel), "Assets/Prefabs/UI/common/CommonTipPanel");
        UIManager.RegisterAddress(typeof(TaskPanel), "Assets/Prefabs/UI/task/TaskPanel");
        UIManager.RegisterAddress(typeof(MapPanel), "Assets/Prefabs/UI/map/MapPanel");
        UIManager.RegisterAddress(typeof(MainPanelController), "Assets/Prefabs/UI/Global/MainPanel");
        UIManager.RegisterAddress(typeof(BattleWindow), "Assets/Prefabs/UI/Battle/BattleWindow");
        UIManager.RegisterAddress(typeof(TestUIPanel), "Assets/Prefabs/UI/Test/TestUIPanel");
    }
}
