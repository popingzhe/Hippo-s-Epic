using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

public class DialogWindow : UIWindow
{
    public override UILayer Layer => UILayer.Popup;

    [Header("预制体引用")]
    [SerializeField] private GameObject dialoguePanelPrefab;
    [SerializeField] private GameObject bubbleLeftPrefab;
    [SerializeField] private GameObject bubbleMiddlePrefab;
    [SerializeField] private GameObject bubbleRightPrefab;
    [SerializeField] private GameObject bubbleOptionPrefab;
    [SerializeField] private GameObject dialogCameraRootPrefab;

    private DialogManagerUI _dialogUI;
    private DialogManager _dialogManager;
    private DialogCharacterManager _charManager;
    private DialogCharacterRenderer _renderer;
    private RightSideSlotManager _rightSlotManager;
    private BackgroundView _backgroundView;
    private Transform _sceneRuleRoot;
    private RawImage _characterImage;
    private Image _speakerAvatar;
    private readonly Dictionary<string, Sprite> _avatarCache = new Dictionary<string, Sprite>();
    private readonly Dictionary<string, AsyncOperationHandle<Texture2D>> _avatarHandles = new Dictionary<string, AsyncOperationHandle<Texture2D>>();
    private int _avatarSeq;
    private GameObject _cameraRootInstance;
    private bool _initialized;

    public override void OnCreate(object args)
    {
        _dialogManager = ManagerRegistry.Get<DialogManager>();
        _charManager = ManagerRegistry.Get<DialogCharacterManager>();

        _dialogManager.OnBackgroundChange += OnBackgroundChange;
        _dialogManager.OnSpeakerAvatar += OnSpeakerAvatar;
    }

    public override void OnOpen(object args)
    {
        if (!_initialized)
        {
            Initialize(args);
            _initialized = true;
        }

        if (_cameraRootInstance == null)
            InstantiateCameraRoot();

        var app = ManagerRegistry.Get<GameApp>();
        if (app != null)
            app.EnterDialog();
        else
            ManagerRegistry.Get<BootstrapQuickStart>()?.EnterDialogMode();

        if (args is int dialogId)
            StartDialog(dialogId);
    }

    private void InstantiateCameraRoot()
    {
        if (dialogCameraRootPrefab == null)
        {
            Debug.LogError("[DialogWindow] DialogCameraRootPrefab 未设置");
            return;
        }

        _cameraRootInstance = Instantiate(dialogCameraRootPrefab);
        _cameraRootInstance.name = "DialogCameraRoot";
        _renderer = _cameraRootInstance.GetComponent<DialogCharacterRenderer>();
        ManagerRegistry.Register(_renderer);
        _renderer.SetCharacterImage(_characterImage);
        if (_characterImage != null && _characterImage.texture != null)
            _characterImage.SetNativeSize();

        _rightSlotManager = _cameraRootInstance.GetComponent<RightSideSlotManager>();
        if (_rightSlotManager == null)
            _rightSlotManager = _cameraRootInstance.AddComponent<RightSideSlotManager>();
        _rightSlotManager.Initialize();
    }

    private void DestroyCameraRoot()
    {
        if (_rightSlotManager != null)
        {
            _rightSlotManager.Cleanup();
            _rightSlotManager = null;
        }

        if (_renderer != null)
        {
            ManagerRegistry.Unregister<DialogCharacterRenderer>();
            _renderer = null;
        }

        if (_cameraRootInstance != null)
        {
            Destroy(_cameraRootInstance);
            _cameraRootInstance = null;
        }
    }

    private void Initialize(object args)
    {
        if (dialoguePanelPrefab == null)
        {
            Debug.LogError("[DialogWindow] DialoguePanelPrefab 未设置");
            return;
        }

        var panel = Instantiate(dialoguePanelPrefab, transform, false);
        panel.name = "DialoguePanel";

        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        var content = panel.transform.Find("duihuaSV/Viewport/Content");
        if (content == null)
        {
            Debug.LogError("[DialogWindow] DialoguePanel 中未找到 Scroll View/Viewport/Content");
            return;
        }

        if (bubbleLeftPrefab == null || bubbleMiddlePrefab == null || bubbleRightPrefab == null || bubbleOptionPrefab == null)
        {
            Debug.LogError("[DialogWindow] 气泡预制体引用未设置");
            return;
        }

        _backgroundView = panel.GetComponentInChildren<BackgroundView>();
        _sceneRuleRoot = panel.transform.Find("sceneRuleRoot");

        CreateCharacterLayer();
        CreateSpeakerAvatar();

        _dialogUI = panel.AddComponent<DialogManagerUI>();
        _dialogUI.Initialize(
            content as RectTransform,
            bubbleLeftPrefab.GetComponent<ChatBubbleLeftView>(),
            bubbleMiddlePrefab.GetComponent<ChatBubbleMiddleView>(),
            bubbleRightPrefab.GetComponent<ChatBubbleRightView>(),
            bubbleOptionPrefab.GetComponent<ChatBubbleOptionView>()
        );

        _initialized = true;
    }

    public override void OnClose()
    {
        var app = ManagerRegistry.Get<GameApp>();
        if (app != null)
            app.ExitDialog();
        else
            ManagerRegistry.Get<BootstrapQuickStart>()?.ExitDialogMode();

        if (_dialogManager != null)
        {
            _dialogManager.OnBackgroundChange -= OnBackgroundChange;
            _dialogManager.OnSpeakerAvatar -= OnSpeakerAvatar;
            if (_dialogManager.State == DialogState.Playing)
                _dialogManager.ForceEndDialog();
        }

        _charManager?.CleanupDynamicCharacters();
        ReleaseAvatarHandles();
        DestroyCameraRoot();
    }

    private void CreateCharacterLayer()
    {
        var parent = _sceneRuleRoot != null ? _sceneRuleRoot : transform;
        var go = new GameObject("CharacterLayer");
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        // 居中显示相机实际渲染尺寸，不随父节点拉伸。
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;

        _characterImage = go.AddComponent<RawImage>();
        _characterImage.raycastTarget = false;
    }

    private void CreateSpeakerAvatar()
    {
        var parent = _sceneRuleRoot != null ? _sceneRuleRoot : transform;
        var go = new GameObject("SpeakerAvatar");
        var rect = go.AddComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0, 0.5f);
        rect.anchorMax = new Vector2(0, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(120, 0);
        rect.sizeDelta = new Vector2(200, 200);

        _speakerAvatar = go.AddComponent<Image>();
        _speakerAvatar.raycastTarget = false;
        _speakerAvatar.enabled = false;
    }

    private static Texture2D _whiteTexture;
    private static Sprite _whiteSprite;

    private static Sprite GetWhiteSprite()
    {
        if (_whiteSprite != null) return _whiteSprite;
        if (_whiteTexture == null)
        {
            _whiteTexture = new Texture2D(1, 1);
            _whiteTexture.SetPixel(0, 0, Color.white);
            _whiteTexture.Apply();
        }
        _whiteSprite = Sprite.Create(_whiteTexture, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 100f);
        return _whiteSprite;
    }

    private async void OnSpeakerAvatar(string avatarName)
    {
        if (_speakerAvatar == null || string.IsNullOrEmpty(avatarName)) return;

        int seq = ++_avatarSeq;

        if (_avatarCache.TryGetValue(avatarName, out var sp) && sp != null)
        {
            _speakerAvatar.sprite = sp;
            _speakerAvatar.enabled = true;
            return;
        }

        if (_avatarHandles.TryGetValue(avatarName, out var existing))
        {
            if (existing.IsDone && existing.Result != null)
            {
                sp = Sprite.Create(existing.Result,
                    new Rect(0, 0, existing.Result.width, existing.Result.height),
                    new Vector2(0.5f, 0.5f), 100f);
                _avatarCache[avatarName] = sp;
                if (seq == _avatarSeq)
                {
                    _speakerAvatar.sprite = sp;
                    _speakerAvatar.enabled = true;
                }
            }
            return;
        }

        var address = $"Assets/Art/Sprites/Head/{avatarName}";
        AsyncOperationHandle<Texture2D> handle = default;

        try
        {
            handle = Addressables.LoadAssetAsync<Texture2D>(address);
            _avatarHandles[avatarName] = handle;
            await handle.Task;
        }
        catch (Exception)
        {
            Debug.LogWarning($"[DialogWindow] 头像不存在: {address}，使用白图替代");
            if (seq == _avatarSeq)
            {
                var ws = GetWhiteSprite();
                _avatarCache[avatarName] = ws;
                _speakerAvatar.sprite = ws;
                _speakerAvatar.enabled = true;
            }
            return;
        }

        if (seq != _avatarSeq) return;

        if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
        {
            sp = Sprite.Create(handle.Result,
                new Rect(0, 0, handle.Result.width, handle.Result.height),
                new Vector2(0.5f, 0.5f), 100f);
            _avatarCache[avatarName] = sp;
            _speakerAvatar.sprite = sp;
            _speakerAvatar.enabled = true;
        }
        else
        {
            var ws = GetWhiteSprite();
            _avatarCache[avatarName] = ws;
            _speakerAvatar.sprite = ws;
            _speakerAvatar.enabled = true;
        }
    }

    private void ReleaseAvatarHandles()
    {
        foreach (var kvp in _avatarCache)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value);
        }
        _avatarCache.Clear();

        foreach (var kvp in _avatarHandles)
        {
            if (kvp.Value.IsValid())
                Addressables.Release(kvp.Value);
        }
        _avatarHandles.Clear();
    }

    private void OnBackgroundChange(string backgroundName)
    {
        if (_backgroundView != null)
            _backgroundView.Apply(backgroundName);
    }

    private void StartDialog(int dialogId)
    {
        if (_dialogManager == null)
        {
            Debug.LogError("[DialogWindow] DialogManager 未初始化");
            return;
        }

        if (_renderer != null)
            _renderer.SetVisible(true);

        _dialogManager.StartDialog(dialogId);
    }
}
