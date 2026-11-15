#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public class InteractableOpenUITests
{
    private const BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

    // 与 HEAD 逐项一致的 11 条注册映射：锁住类型、地址和顺序
    private static readonly (Type type, string address, UIPanelArgs kind)[] ExpectedWindows =
    {
        (typeof(DialogWindow), "Assets/Prefabs/UI/Dialog/DialogWindow", UIPanelArgs.Int),
        (typeof(MainMenuPanel), "Assets/Prefabs/UI/Global/MainMenuPanel", UIPanelArgs.None),
        (typeof(LoadingPanel), "Assets/Prefabs/UI/Global/LoadingPanel", UIPanelArgs.None),
        (typeof(BagPanel), "Assets/Prefabs/UI/Bag/BagPanel", UIPanelArgs.None),
        (typeof(UseItemTip), "Assets/Prefabs/UI/Bag/UseItemTip", UIPanelArgs.Int),
        (typeof(CommonTipPanel), "Assets/Prefabs/UI/common/CommonTipPanel", UIPanelArgs.TipArgs),
        (typeof(TaskPanel), "Assets/Prefabs/UI/task/TaskPanel", UIPanelArgs.None),
        (typeof(MapPanel), "Assets/Prefabs/UI/map/MapPanel", UIPanelArgs.MapRootId),
        (typeof(MainPanelController), "Assets/Prefabs/UI/Global/MainPanel", UIPanelArgs.None),
        (typeof(BattleWindow), "Assets/Prefabs/UI/Battle/BattleWindow", UIPanelArgs.None),
        (typeof(TestUIPanel), "Assets/Prefabs/UI/Test/TestUIPanel", UIPanelArgs.None),
    };

    [Test]
    public void OpenUI_IsAppendedWithoutChangingExistingInteractionValues()
    {
        string[] original = { "HintOnly", "Dialogue", "Pickup", "Teleport", "Consume",
            "SetCondition", "GainBuff", "LoseBuff", "EnterBattle" };
        for (int i = 0; i < original.Length; i++)
            Assert.AreEqual(i, Convert.ToInt32(Enum.Parse(typeof(InteractionType), original[i])));
        Assert.IsTrue(Enum.IsDefined(typeof(InteractionType), "OpenUI"),
            "Buttons need an OpenUI interaction type.");
        Assert.AreEqual(9, Convert.ToInt32(Enum.Parse(typeof(InteractionType), "OpenUI")));
    }

    [Test]
    public void WindowChoices_ComeFromTheSharedRegistry()
    {
        string[] keys = UIWindowRegistry.GetWindowKeys();
        CollectionAssert.Contains(keys, typeof(BagPanel).FullName);
        CollectionAssert.Contains(keys, typeof(TaskPanel).FullName);
        CollectionAssert.AllItemsAreUnique(keys);
        CollectionAssert.IsOrdered(keys, StringComparer.Ordinal);
        // TipArgs 面板不能由交互按钮打开，不能出现在下拉里
        CollectionAssert.DoesNotContain(keys, typeof(CommonTipPanel).FullName);
    }

    [Test]
    public void Registry_KeepsAllElevenHeadMappings()
    {
        Assert.AreEqual(11, ExpectedWindows.Length);
        foreach (var (type, address, kind) in ExpectedWindows)
        {
            string key = type.FullName;
            Assert.AreEqual(kind, UIWindowRegistry.GetArgsKind(key), "args kind changed: " + key);
            Assert.AreEqual(address, UIWindowRegistry.GetAddress(key), "address changed: " + key);
        }
        Assert.AreEqual(10, UIWindowRegistry.GetWindowKeys().Length, "only the TipArgs panel is hidden");
    }

    [Test]
    public void Args_AreBuiltPerPanelKind()
    {
        Assert.IsFalse(UIWindowRegistry.TryBuildArgs(typeof(DialogWindow).FullName, 0, out _, out var err),
            "0 must be rejected for int-arg panels");
        Assert.IsNotNull(err);
        Assert.IsTrue(UIWindowRegistry.TryBuildArgs(typeof(DialogWindow).FullName, 1001001, out var dialogArgs, out _));
        Assert.AreEqual(1001001, (int)dialogArgs);

        Assert.IsTrue(UIWindowRegistry.TryBuildArgs(typeof(BagPanel).FullName, 7, out var bagArgs, out _));
        Assert.IsNull(bagArgs, "arg-less panels must receive null");

        Assert.IsTrue(UIWindowRegistry.TryBuildArgs(typeof(MapPanel).FullName, 0, out var mapArgs, out _));
        Assert.AreEqual(MapRootId.PunkCity, (MapRootId)mapArgs);

        Assert.IsFalse(UIWindowRegistry.TryBuildArgs(typeof(CommonTipPanel).FullName, 1, out _, out _),
            "TipArgs panels are unsupported from interaction buttons");
    }

    [Test]
    public void TryOpen_RejectsMissingManagerOrUnknownWindow()
    {
        Assert.IsFalse(UIWindowRegistry.TryOpen(null, typeof(BagPanel).FullName, 0, out var err));
        Assert.IsNotNull(err);
        Assert.IsFalse(UIWindowRegistry.TryOpen(null, "__unknown_window__", 0, out _));
        Assert.IsFalse(UIWindowRegistry.TryOpen(null, "", 0, out _));
    }

    [Test]
    public void InvalidWindow_DoesNotConsumeOneShotOrChangePhase()
    {
        var originalScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var testScene = UnityEditor.SceneManagement.EditorSceneManager.NewScene(
            UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
            UnityEditor.SceneManagement.NewSceneMode.Additive);
        UnityEngine.SceneManagement.SceneManager.SetActiveScene(testScene);
        var root = new GameObject("OpenUIRegressionRoot");
        try
        {
            var child = new GameObject("Interactable");
            child.transform.SetParent(root.transform);
            var interactable = child.AddComponent<Interactable>();
            typeof(Interactable).GetField("_playerInside", PrivateInstance).SetValue(interactable, true);
            foreach (string key in new[] { "", "__unknown_window__" })
            {
                var phase = new InteractionPhase
                {
                    state = 0,
                    canRepeat = false,
                    buttons = new List<ButtonOption>
                    {
                        new ButtonOption { type = (InteractionType)9, param1 = key }
                    }
                };
                typeof(Interactable).GetField("_currentPhase", PrivateInstance).SetValue(interactable, phase);
                typeof(Interactable).GetField("_hasExecuted", PrivateInstance).SetValue(interactable, false);
                interactable.OnPlayerExecute(0);
                Assert.IsFalse((bool)typeof(Interactable).GetField("_hasExecuted", PrivateInstance).GetValue(interactable),
                    "A rejected UI request must remain retryable: " + key);
                Assert.AreEqual(0, interactable.CurrentState);
            }
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(originalScene);
            UnityEditor.SceneManagement.EditorSceneManager.CloseScene(testScene, true);
        }
    }
}
#endif
