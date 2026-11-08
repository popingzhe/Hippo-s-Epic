using TMPro;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class BagItem : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private GameObject selectedBg;
    [SerializeField] private TextMeshProUGUI num;
    [SerializeField] private Image icon;
    [SerializeField] private GameObject bg;

    public int InstanceId { get; private set; }
    public int ItemId { get; private set; }
    public int ItemCount { get; private set; }
    public Sprite IconSprite => icon != null ? icon.sprite : null;
    public Vector2 IconSize => icon != null ? icon.rectTransform.rect.size : Vector2.zero;

    public System.Action<BagItem> onClick;
    public System.Action<BagItem> onRightClick;
    public System.Action<BagItem, PointerEventData> onBeginDrag;
    public System.Action<PointerEventData> onDrag;
    public System.Action<PointerEventData> onEndDrag;

    private RectTransform _rt;
    private Canvas _canvas;
    private CanvasGroup _canvasGroup;

    private void Awake()
    {
        _rt = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();
    }

    public void Setup(int instanceId, int itemId, int count, cfg.cfg.item.Item itemCfg)
    {
        InstanceId = instanceId;
        ItemId = itemId;
        ItemCount = count;

        if (icon != null)
        {
            if (itemCfg != null && itemCfg.Icon > 0)
            {
                icon.gameObject.SetActive(true);
                icon.sprite = LoadIcon(itemCfg.Icon);
            }
            else
            {
                icon.gameObject.SetActive(true);
                icon.sprite = null;
            }
        }

        if (num != null)
        {
            if (count > 1)
            {
                num.gameObject.SetActive(true);
                num.text = count.ToString();
            }
            else
            {
                num.gameObject.SetActive(false);
            }
        }

        if (_rt != null)
        {
            _rt.anchorMin = Vector2.zero;
            _rt.anchorMax = Vector2.zero;
            _rt.pivot = new Vector2(0.5f, 0.5f);
        }

        SetSelected(false);
    }

    public void SetSelected(bool selected)
    {
        if (selectedBg != null)
            selectedBg.SetActive(selected);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Right)
        {
            onRightClick?.Invoke(this);
            return;
        }

        PlayClickShake();
        onClick?.Invoke(this);
    }

    /// <summary>
    /// 点击物品时的图标抖动（DOTween）：位置抖 + 缩放脉冲。
    /// 用 SetUpdate(true) 走 unscaled 时间，背包在暂停(timeScale=0)下也能抖。
    /// </summary>
    private void PlayClickShake()
    {
        if (icon == null) return;
        var rt = icon.rectTransform;
        if (rt == null) return;

        // 打断上一次未播完的抖动，避免连点时动画叠加偏移
        rt.DOKill();
        rt.localScale = Vector3.one;

        rt.DOShakeAnchorPos(duration: 0.25f,
                            strength: new Vector2(7f, 5f),
                            vibrato: 20,
                            randomness: 40f,
                            snapping: false,
                            fadeOut: true)
          .SetUpdate(true)
          .SetTarget(rt);

        rt.DOPunchScale(Vector3.one * 0.12f, 0.22f, vibrato: 8, elasticity: 0.7f)
          .SetUpdate(true)
          .SetTarget(rt)
          .OnComplete(() => { if (rt != null) rt.localScale = Vector3.one; });
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Left)
            return;

        _canvasGroup = GetComponent<CanvasGroup>();
        if (_canvasGroup == null)
            _canvasGroup = gameObject.AddComponent<CanvasGroup>();

        _canvasGroup.alpha = 0.5f;
        _canvasGroup.blocksRaycasts = false;

        onBeginDrag?.Invoke(this, eventData);
    }

    public void OnDrag(PointerEventData eventData)
    {
        onDrag?.Invoke(eventData);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        if (_canvasGroup != null)
        {
            _canvasGroup.alpha = 1f;
            _canvasGroup.blocksRaycasts = true;
        }

        onEndDrag?.Invoke(eventData);
    }

    private Sprite LoadIcon(int iconId)
    {
        return IconLoader.LoadItemIcon(iconId);
    }
}
