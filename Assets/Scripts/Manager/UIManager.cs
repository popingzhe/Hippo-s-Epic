using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

public class UIManager : MonoBehaviour
{
    [SerializeField] private Canvas rootCanvas;

    private static readonly Dictionary<string, string> addressMap = new Dictionary<string, string>();

    private readonly Dictionary<string, UIWindow> windowMap = new Dictionary<string, UIWindow>();
    private readonly HashSet<string> createdSet = new HashSet<string>();
    private readonly Dictionary<string, AsyncOperationHandle<GameObject>> loadHandles = new Dictionary<string, AsyncOperationHandle<GameObject>>();
    private readonly HashSet<string> pendingClose = new HashSet<string>();
    public static void RegisterAddress(Type windowType, string address)
    {
        addressMap[windowType.FullName] = address;
    }

    private void Awake()
    {
        if (rootCanvas == null)
            rootCanvas = GetComponentInChildren<Canvas>(true);
    }

    private void OnEnable()
    {
        // 注册放 OnEnable: domain reload 后 DontDestroyOnLoad 对象 Awake 不重跑,
        // 但 OnEnable 会重跑 —— 保证战斗重入时 Get<UIManager>() 不失效。
        // 条件注册: 场景副本(Scene_Battle 等)不得顶掉 DDO 实例的注册
        var cur = ManagerRegistry.Get<UIManager>();
        if (cur == null || cur == this)
            ManagerRegistry.Register(this);
    }

    public T Open<T>(object args = null, Action<T> onReady = null) where T : UIWindow
    {
        string key = typeof(T).FullName;
        pendingClose.Remove(key);

        if (windowMap.TryGetValue(key, out var window))
        {
            PlaceWindow(window);
            window.gameObject.SetActive(true);

            if (!createdSet.Contains(key))
            {
                window.OnCreate(args);
                createdSet.Add(key);
            }

            window.OnOpen(args);
            onReady?.Invoke((T)window);
            return (T)window;
        }

        StartCoroutine(LoadAndOpenCoroutine<T>(args, onReady));
        return null;
    }

    private IEnumerator LoadAndOpenCoroutine<T>(object args, Action<T> onReady) where T : UIWindow
    {
        string key = typeof(T).FullName;

        if (!addressMap.TryGetValue(key, out var address))
        {
            Debug.LogError($"[UIManager] 未注册 Addressable 地址: {typeof(T).Name}");
            yield break;
        }

        if (loadHandles.ContainsKey(key))
        {
            yield return loadHandles[key];
        }
        else
        {
            var handle = Addressables.LoadAssetAsync<GameObject>(address);
            loadHandles[key] = handle;
            yield return handle;
        }

        if (loadHandles[key].Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError($"[UIManager] 加载失败: {address}");
            ReleaseHandle(key);
            yield break;
        }

        var prefab = loadHandles[key].Result;
        ReleaseHandle(key);

        var instance = Instantiate(prefab, rootCanvas.transform, false);
        var window = instance.GetComponent<T>();

        if (window == null)
        {
            Debug.LogError($"[UIManager] 预制体上找不到 {typeof(T).Name} 组件: {address}");
            Destroy(instance);
            yield break;
        }

        windowMap[key] = window;
        PlaceWindow(window);

        if (pendingClose.Contains(key))
        {
            pendingClose.Remove(key);
            window.gameObject.SetActive(false);
            yield break;
        }

        window.gameObject.SetActive(true);

        if (!createdSet.Contains(key))
        {
            window.OnCreate(args);
            createdSet.Add(key);
        }

        window.OnOpen(args);
        onReady?.Invoke(window);
    }

    public void Close<T>() where T : UIWindow
    {
        string key = typeof(T).FullName;

        if (!windowMap.TryGetValue(key, out var window))
        {
            pendingClose.Add(key);
            return;
        }

        window.OnClose();
        Destroy(window.gameObject);
        windowMap.Remove(key);
        createdSet.Remove(key);
        ReleaseHandle(key);
    }

    public bool IsOpen<T>() where T : UIWindow
    {
        string key = typeof(T).FullName;
        return windowMap.TryGetValue(key, out var window) && window.gameObject.activeSelf;
    }

    public bool HasPopupOpen()
    {
        foreach (var kvp in windowMap)
        {
            var w = kvp.Value;
            if (w != null && w.gameObject.activeSelf && (int)w.Layer >= (int)UILayer.Popup)
                return true;
        }
        return false;
    }

    public void CloseTopmost()
    {
        UIWindow topmost = null;
        int topLayer = -1;

        foreach (var kvp in windowMap)
        {
            var w = kvp.Value;
            if (w != null && w.gameObject.activeSelf && (int)w.Layer > topLayer)
            {
                topmost = w;
                topLayer = (int)w.Layer;
            }
        }

        if (topmost != null)
        {
            topmost.OnClose();
            Destroy(topmost.gameObject);
            foreach (var kvp in new List<KeyValuePair<string, UIWindow>>(windowMap))
            {
                if (kvp.Value == topmost)
                {
                    windowMap.Remove(kvp.Key);
                    createdSet.Remove(kvp.Key);
                    ReleaseHandle(kvp.Key);
                }
            }
        }
    }

    public T Get<T>() where T : UIWindow
    {
        string key = typeof(T).FullName;

        if (windowMap.TryGetValue(key, out var window))
            return (T)window;
        return null;
    }

    /// <summary>
    /// 释放指定 key 的 prefab 加载 handle，避免缓存旧资源
    /// </summary>
    private void ReleaseHandle(string key)
    {
        if (!loadHandles.TryGetValue(key, out var handle))
            return;

        if (handle.IsValid())
            Addressables.Release(handle);
        loadHandles.Remove(key);
    }

    private void OnDestroy()
    {
        foreach (var kvp in windowMap)
        {
            if (kvp.Value != null)
            {
                kvp.Value.OnClose();
                Destroy(kvp.Value.gameObject);
            }
        }
        windowMap.Clear();
        createdSet.Clear();

        foreach (var kvp in loadHandles)
        {
            if (kvp.Value.IsValid())
                Addressables.Release(kvp.Value);
        }
        loadHandles.Clear();
        pendingClose.Clear();

        // 条件注销: 只有注册的是自己才删键（场景副本销毁不得删 DDO 实例的注册）
        if (ManagerRegistry.Get<UIManager>() == this)
            ManagerRegistry.Unregister<UIManager>();
    }

    private void PlaceWindow(UIWindow window)
    {
        if (rootCanvas == null) return;

        window.transform.SetParent(rootCanvas.transform, false);

        var canvas = window.GetComponent<Canvas>();
        if (canvas == null)
        {
            // 窗口根节点没有 Canvas 时补一个：UILayer 分级要靠每个窗口自己的 sortingOrder 落地，
            // 否则 sortingOrder 根本不会被设置，窗口前后关系全部由兄弟顺序（打开顺序）决定。
            // 子 Canvas 不接收鼠标事件、不需要 Scaler / Raycaster，缩放等一律继承外层根画布；
            // sortingLayer 必须跟根画布保持一致，否则排序会落到别的 sortingLayer 桶里。
            canvas = window.gameObject.AddComponent<Canvas>();
            canvas.renderMode = rootCanvas.renderMode;
            canvas.sortingLayerID = rootCanvas.sortingLayerID;
        }

        // 关键：子 Canvas 里的图形只归它自己的 GraphicRaycaster 管，父级 raycaster 看不到。
        // 不补这个，窗口内所有按钮都会失去点击（"开始游戏"点不动就是这个原因）。
        if (window.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null)
            window.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        canvas.overrideSorting = true;
        canvas.sortingOrder = (int)window.Layer;

        // UI 窗口内的 Animator 统一用独立时间：开背包/任务面板时 timeScale=0，
        // 默认 updateMode=Normal 会让 UI 动画冻住（MapPanel 的 head 也做了同样处理）
        var animators = window.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            if (animators[i] != null && animators[i].updateMode != AnimatorUpdateMode.UnscaledTime)
                animators[i].updateMode = AnimatorUpdateMode.UnscaledTime;
        }

        var rect = window.transform as RectTransform;
        if (rect != null)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }
    }
}
