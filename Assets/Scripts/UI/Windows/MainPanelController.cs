using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MainPanelController : UIWindow
{
    public override UILayer Layer => UILayer.Hud;   // HUD 常驻底栏：永远在所有面板之下

    [SerializeField] private Button _btnRole;
    [SerializeField] private Button _btnBag;
    [SerializeField] private Button _btnMap;
    [SerializeField] private Button _btnSetting;
    [SerializeField] private Button _btnIn;
    [SerializeField] private Button _btnOut;
    [SerializeField] private RectTransform _dian;
    [SerializeField] private TextMeshProUGUI _goldText;

    private const int GoldItemId = 1;
    private const float SlideDuration = 0.3f;

    private bool _isExpanded;
    private Tweener _slideTween;

    public override void OnCreate(object args)
    {
        _btnRole?.onClick.AddListener(OnClickRole);
        _btnBag?.onClick.AddListener(OnClickBag);
        _btnMap?.onClick.AddListener(OnClickMap);
        _btnSetting?.onClick.AddListener(OnClickSetting);
        _btnIn?.onClick.AddListener(OnClickIn);
        _btnOut?.onClick.AddListener(OnClickOut);

        _isExpanded = false;
        SetDianPosition(false);
        UpdateToggleButtons();
        RefreshGold();

        BagManager.Instance.OnItemChanged += OnItemChanged;
    }

    public override void OnOpen(object args)
    {
        RefreshGold();
    }

    public override void OnClose()
    {
        _btnRole?.onClick.RemoveAllListeners();
        _btnBag?.onClick.RemoveAllListeners();
        _btnMap?.onClick.RemoveAllListeners();
        _btnSetting?.onClick.RemoveAllListeners();
        _btnIn?.onClick.RemoveAllListeners();
        _btnOut?.onClick.RemoveAllListeners();

        _slideTween?.Kill();
        BagManager.Instance.OnItemChanged -= OnItemChanged;
    }

    private void OnItemChanged(BagManager.BagChangeType changeType, int itemId)
    {
        if (itemId == GoldItemId)
            RefreshGold();
    }

    private void RefreshGold()
    {
        if (_goldText == null) return;
        int count = BagManager.Instance.GetItemCount(GoldItemId);
        _goldText.text = count.ToString();
    }

    private void OnClickRole()
    {
    }

    private void OnClickBag()
    {
        var app = GameApp.Instance;
        if (app != null && app.StateMachine.Current is GameplayState gs)
            gs.OpenBagPublic();
    }

    private void OnClickMap()
    {
        var app = GameApp.Instance;
        if (app != null && app.StateMachine.Current is GameplayState gs)
            gs.OpenTaskPublic();
    }

    private void OnClickSetting()
    {
    }

    private void OnClickIn()
    {
        _isExpanded = true;
        SetDianPosition(true);
        UpdateToggleButtons();
    }

    private void OnClickOut()
    {
        _isExpanded = false;
        SetDianPosition(false);
        UpdateToggleButtons();
    }

    private void SetDianPosition(bool expanded)
    {
        if (_dian == null) return;
        _slideTween?.Kill();
        float targetX = expanded ? _dian.rect.width - 40f : 0f;
        _slideTween = _dian.DOAnchorPosX(targetX, SlideDuration).SetEase(Ease.OutCubic);
    }

    private void UpdateToggleButtons()
    {
        if (_btnIn != null) _btnIn.gameObject.SetActive(!_isExpanded);
        if (_btnOut != null) _btnOut.gameObject.SetActive(_isExpanded);
    }
}
