using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

public enum MapRootId
{
    PunkCity,
}

public class MapPanel : UIWindow
{
    private const string AddressPrefix = "Assets/Prefabs/UI/map/MapRoot_";

    [Header("UI")]
    [SerializeField] private RectTransform mapRootContainer;
    [SerializeField] private RectTransform head;
    [SerializeField] private Animator headAnimator;
    [SerializeField] private Button btnClose;

    private MapRoot _currentMapRoot;
    private GameObject _currentMapRootInstance;
    private AsyncOperationHandle<GameObject> _loadHandle;
    private bool _isTransitioning;
    private string _pendingAnimState;
    private SceneMapId _pendingTargetMap;
    private SceneMapId _pendingCurrentMap;
    private bool _waitingForAnim;
    private AnimationClip _pendingClip;
    private float _animDeadline;
    public override UILayer Layer => UILayer.Popup;

    private void Update()
    {
        if (!_waitingForAnim || headAnimator == null) return;

        var stateInfo = headAnimator.GetCurrentAnimatorStateInfo(0);
        bool clipPlaying = stateInfo.IsName(_pendingAnimState);
        if (!clipPlaying && _pendingClip != null)
        {
            // 兼容 state 名与 clip 名不一致的情况：按当前正在播放的 clip 判定
            var infos = headAnimator.GetCurrentAnimatorClipInfo(0);
            if (infos != null && infos.Length > 0 && infos[0].clip == _pendingClip)
                clipPlaying = true;
        }

        bool finished = clipPlaying && stateInfo.normalizedTime >= 1f;
        if (!finished && _animDeadline > 0f && Time.unscaledTime >= _animDeadline)
        {
            Debug.LogWarning($"[MapPanel] 过场动画未在预期时间内结束（clip={_pendingAnimState}），直接传送以免面板卡住");
            finished = true;
        }

        if (finished)
        {
            _animDeadline = 0f;
            headAnimator.enabled = false;
            _waitingForAnim = false;
            TeleportToMap(_pendingTargetMap, _pendingCurrentMap);
        }
    }

    public override void OnCreate(object args)
    {
        if (btnClose != null)
            btnClose.onClick.AddListener(OnCloseClicked);
    }

    public override void OnOpen(object args)
    {
        MapRootId id = MapRootId.PunkCity;
        if (args is MapRootId rootId)
            id = rootId;

        LoadMapRoot(id);
    }

    public override void OnClose()
    {
        ClearMapRoot();
    }

    private void OnCloseClicked()
    {
        ManagerRegistry.Get<UIManager>()?.Close<MapPanel>();
    }

    private string GetAddress(MapRootId id)
    {
        return AddressPrefix + id;
    }

    private void LoadMapRoot(MapRootId id)
    {
        ClearMapRoot();
        StartCoroutine(LoadMapRootCoroutine(id));
    }

    private System.Collections.IEnumerator LoadMapRootCoroutine(MapRootId id)
    {
        string address = GetAddress(id);
        _loadHandle = Addressables.LoadAssetAsync<GameObject>(address);
        yield return _loadHandle;

        if (_loadHandle.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError("[MapPanel] failed to load MapRoot: " + address);
            yield break;
        }

        _currentMapRootInstance = Instantiate(_loadHandle.Result, mapRootContainer, false);
        _currentMapRoot = _currentMapRootInstance.GetComponent<MapRoot>();

        if (_currentMapRoot != null)
        {
            SceneMapId currentMap = GetCurrentSceneMapId();
            _currentMapRoot.Initialize(OnNodeClicked);
            PositionHead(currentMap);
        }
    }

    private void PositionHead(SceneMapId currentMap)
    {
        Debug.Log($"[MapPanel] PositionHead called: head={head != null}, mapRoot={_currentMapRoot != null}, animator={headAnimator != null}");
        if (head == null || _currentMapRoot == null) return;

        if (headAnimator != null)
        {
            Debug.Log($"[MapPanel] animator was enabled={headAnimator.enabled}");
            headAnimator.enabled = false;
            Debug.Log($"[MapPanel] animator now enabled={headAnimator.enabled}");
        }

        var btn = _currentMapRoot.GetButtonByMap(currentMap);
        Debug.Log($"[MapPanel] btn found={btn != null}, currentMap={currentMap}");
        if (btn != null)
        {
            var btnPos = btn.GetComponent<RectTransform>().position;
            Debug.Log($"[MapPanel] btn world pos={btnPos}, head world pos before={head.position}");
            head.position = btnPos;
            Debug.Log($"[MapPanel] head world pos after={head.position}, head anchored={head.anchoredPosition}");
            head.gameObject.SetActive(true);
        }
        else
        {
            head.gameObject.SetActive(false);
        }
    }

    private void OnNodeClicked(SceneMapId targetMap)
    {
        if (_isTransitioning) return;

        if (_currentMapRoot != null)
        {
            int condId = _currentMapRoot.GetConditionId(targetMap);
            if (condId != 0 && (!ConditionSystem.HasInstance || !ConditionSystem.Instance.IsConditionMet(condId)))
                return;
        }

        SceneMapId currentMap = GetCurrentSceneMapId();
        Debug.Log($"[MapPanel] OnNodeClicked: targetMap={targetMap}, currentMap={currentMap}");

        if (_currentMapRoot != null)
        {
            var fromNodeId = _currentMapRoot.GetNodeIdByMap(currentMap);
            var toNodeId = _currentMapRoot.GetNodeIdByMap(targetMap);
            Debug.Log($"[MapPanel] fromNodeId={fromNodeId}, toNodeId={toNodeId}");
            var clip = (fromNodeId.HasValue && toNodeId.HasValue)
                ? _currentMapRoot.GetTransitionClip(fromNodeId.Value, toNodeId.Value)
                : null;
            Debug.Log($"[MapPanel] clip={clip?.name ?? "null"}");
            var fromBtn = _currentMapRoot.GetButtonByMap(currentMap);
            if (clip != null)
            {
                PlayTransitionAndTeleport(clip, targetMap, currentMap, fromBtn);
                return;
            }
        }

        TeleportToMap(targetMap, currentMap);
    }

    private void PlayTransitionAndTeleport(AnimationClip clip, SceneMapId targetMap, SceneMapId currentMap, Button fromBtn)
    {
        Debug.Log($"[MapPanel] PlayTransition: clip={clip?.name}, animator={headAnimator != null}");
        _isTransitioning = true;
        SetButtonsInteractable(false);

        if (headAnimator != null)
            headAnimator.enabled = false;

        if (fromBtn != null)
        {
            head.position = fromBtn.GetComponent<RectTransform>().position;
            Debug.Log($"[MapPanel] head moved to fromBtn pos={head.position}");
            head.gameObject.SetActive(true);
        }

        if (headAnimator != null)
        {
            headAnimator.enabled = true;
            headAnimator.Rebind();
            headAnimator.updateMode = AnimatorUpdateMode.UnscaledTime;
            headAnimator.Play(clip.name, 0, 0f);
            _pendingAnimState = clip.name;
            _pendingClip = clip;
            _animDeadline = Time.unscaledTime + clip.length + 1f;
            _pendingTargetMap = targetMap;
            _pendingCurrentMap = currentMap;
            _waitingForAnim = true;
        }
        else
        {
            TeleportToMap(targetMap, currentMap);
        }
    }

    private void TeleportToMap(SceneMapId targetMap, SceneMapId currentMap)
    {
        _isTransitioning = false;
        ManagerRegistry.Get<UIManager>()?.Close<MapPanel>();

        if (targetMap == currentMap)
            GameplayState.CloseTaskIfOpen();

        GameApp.Instance?.GoToMap(targetMap);
    }

    private void SetButtonsInteractable(bool interactable)
    {
        if (_currentMapRoot == null) return;
        foreach (var entry in _currentMapRoot.Nodes)
        {
            if (entry.button != null)
                entry.button.interactable = interactable;
        }
    }

    private SceneMapId GetCurrentSceneMapId()
    {
        string mapName = GetCurrentMapName();
        // Debug.Log($"[MapPanel] GetCurrentSceneMapId: mapName='{mapName}'");
        if (string.IsNullOrEmpty(mapName)) return SceneMapId.StaffLounge;
        var result = SceneMapIdExtensions.GetSceneMapIdByMapName(mapName);
        // Debug.Log($"[MapPanel] GetCurrentSceneMapId: result={result}, MapNameToId keys=[{string.Join(", ", SceneMapIdExtensions.GetAllMapNames())}]");
        return result;
    }

    private string GetCurrentMapName()
    {
        var levelController = LevelController.Instance;
        if (levelController != null && levelController.MapManager != null && levelController.MapManager.CurrentMap != null)
        {
            return levelController.MapManager.CurrentMap.name;
        }
        return null;
    }

    public void SwitchToMapRoot(MapRootId id)
    {
        LoadMapRoot(id);
    }

    private void ClearMapRoot()
    {
        _isTransitioning = false;
        _waitingForAnim = false;

        if (_currentMapRootInstance != null)
        {
            Destroy(_currentMapRootInstance);
            _currentMapRootInstance = null;
        }

        if (_loadHandle.IsValid())
        {
            Addressables.Release(_loadHandle);
        }

        _currentMapRoot = null;
    }
}
