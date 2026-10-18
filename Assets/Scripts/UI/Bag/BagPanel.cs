using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class BagPanel : UIWindow
{
    public override string ExclusiveGroup => "MainPanels";   // 背包与任务面板互斥，同时只显示一个

    [Header("穿戴展示")]
    [SerializeField] private Image wearLeftHand;
    [SerializeField] private Image wearRightHand;
    [SerializeField] private Image wearHead;
    [SerializeField] private Image wearFace;
    [SerializeField] private Image wearLeftFoot;
    [SerializeField] private Image wearRightFoot;
    [SerializeField] private Image wearBody;

    [Header("装备槽")]
    [SerializeField] private BagEquip equipHand;
    [SerializeField] private BagEquip equipBody;
    [SerializeField] private BagEquip equipFoot;
    [SerializeField] private BagEquip equipHead;

    [Header("物品详情")]
    [SerializeField] private Image itemIcon;
    [SerializeField] private TextMeshProUGUI itemName;
    [SerializeField] private TextMeshProUGUI itemTip;
    [SerializeField] private TextMeshProUGUI typeName;
    [SerializeField] private Button btnUseItem;
    [SerializeField] private GameObject itemTipRoot;

    [Header("列表")]
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private Transform content;

    [Header("分类筛选")]
    [SerializeField] private ToggleGroup toggleGroup;
    [SerializeField] private Toggle btnEquip;
    [SerializeField] private Toggle btnTrash;
    [SerializeField] private Toggle btnUse;
    [SerializeField] private Toggle btnKey;

    [Header("预制体")]
    [SerializeField] private BagItem bagItemPrefab;
    
    [SerializeField] private Button btnClose;

    [Header("顶部信息")]
    [SerializeField] private TextMeshProUGUI topLevelText;
    [SerializeField] private Image topExpFill;
    [SerializeField] private TextMeshProUGUI topExpText;
    [SerializeField] private Transform attrContent;
    [SerializeField] private Transform perkContent;
    [SerializeField] private TextMeshProUGUI infoRowTemplate;

    private readonly List<BagItem> _bagItems = new List<BagItem>();
    private readonly Dictionary<EquipSlot, BagEquip> _equipSlots = new Dictionary<EquipSlot, BagEquip>();

    private int _selectedItemId;
    private int _selectedInstanceId;
    private EquipSlot? _selectedEquipSlot;
    private int _currentFilterType = -1;
    private int _currentFilterChildtype = -1;

    private GameObject _dragIcon;
    private BagItem _dragBagItem;
    private readonly HashSet<int> _traitIds = new HashSet<int>();
    private bool _traitTipShowing;

    private const int PlayerPersonId = 1;   // 与 BagManager.PlayerPersonId 一致
#pragma warning disable CS0414
    private bool _isDragging;
#pragma warning restore CS0414

    private void OnDestroy()
    {
        // 面板销毁时退订，否则 BagManager 的事件里会一直留着已销毁面板的引用
        if (BagManager.Instance != null)
            BagManager.Instance.OnItemChanged -= OnItemChanged;
        if (EquipManager.Instance != null)
            EquipManager.Instance.OnEquipChanged -= OnEquipChanged;
        if (CharacterStatsManager.Instance != null)
            CharacterStatsManager.Instance.OnStatsChanged -= OnStatsChanged;
        if (BuffManager.Instance != null)
            BuffManager.Instance.OnBuffChanged -= OnBuffChanged;
    }

    public override void OnCreate(object args)
    {
        _equipSlots[EquipSlot.Hand] = equipHand;
        _equipSlots[EquipSlot.Body] = equipBody;
        _equipSlots[EquipSlot.Foot] = equipFoot;
        _equipSlots[EquipSlot.Head] = equipHead;

        foreach (var kvp in _equipSlots)
        {
            if (kvp.Value != null)
            {
                EquipSlot slot = kvp.Key;
                kvp.Value.onClick = OnEquipSlotClicked;
                kvp.Value.onDropItem = OnEquipSlotDropItem;
                kvp.Value.onBtnEquipClicked = OnEquipSlotBtnEquipClicked;
                kvp.Value.onBtnUnequipClicked = OnEquipSlotBtnUnequipClicked;
            }
        }

        // 先退订再订阅：OnCreate 可能被重复调用，避免重复订阅
        BagManager.Instance.OnItemChanged -= OnItemChanged;
        BagManager.Instance.OnItemChanged += OnItemChanged;
        EquipManager.Instance.OnEquipChanged -= OnEquipChanged;
        EquipManager.Instance.OnEquipChanged += OnEquipChanged;

        CharacterStatsManager.Instance.OnStatsChanged -= OnStatsChanged;
        CharacterStatsManager.Instance.OnStatsChanged += OnStatsChanged;

        // 特质（长期 buff）变化时刷新右列
        BuffManager.Instance.OnBuffChanged -= OnBuffChanged;
        BuffManager.Instance.OnBuffChanged += OnBuffChanged;

        // 行模板只用于克隆，运行时隐藏
        if (infoRowTemplate != null)
            infoRowTemplate.gameObject.SetActive(false);

        if (toggleGroup != null)
            toggleGroup.allowSwitchOff = true;

        btnEquip.onValueChanged.AddListener(isOn => { if (isOn) SetFilter(4); else if (_currentFilterType == 4) SetFilter(-1); });
        btnTrash.onValueChanged.AddListener(isOn => { if (isOn) SetFilter(5); else if (_currentFilterType == 5) SetFilter(-1); });
        btnUse.onValueChanged.AddListener(isOn => { if (isOn) SetFilter(3); else if (_currentFilterType == 3) SetFilter(-1); });
        btnKey.onValueChanged.AddListener(isOn => { if (isOn) SetFilter(2); else if (_currentFilterType == 2) SetFilter(-1); });

        if (btnUseItem != null)
            btnUseItem.onClick.AddListener(OnBtnUseItemClicked);

        if (btnClose != null)
            btnClose.onClick.AddListener(OnBtnCloseClicked);

        _currentFilterType = -1;
        btnEquip.isOn = false;
        btnTrash.isOn = false;
        btnUse.isOn = false;
        btnKey.isOn = false;
    }

    public override void OnOpen(object args)
    {
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);

        RefreshList();
        RefreshEquips();
        ClearSelection();
        RefreshTopInfo();
    }

    public override void OnClose()
    {
        // 面板被关掉时收不到行的"移出"事件 ⇒ 主动收掉通用提示框，避免留一个飘在外面的提示
        HideTraitTip();
    }

    private void SetFilter(int type, int childtype = -1)
    {
        _currentFilterType = type;
        _currentFilterChildtype = childtype;
        RefreshList();
        ClearSelection();
    }

    private void RefreshList()
    {
        ClearList();

        var items = BagManager.Instance.AllItems;
        var tables = GetTables();
        if (tables == null) return;

        foreach (var entry in items)
        {
            int itemId = entry.itemId;

            var itemCfg = tables.TbItem.GetOrDefault(itemId);
            if (itemCfg == null) continue;
            if (itemCfg.Type == 1) continue;
            if (_currentFilterType > 0 && itemCfg.Type != _currentFilterType) continue;
            if (_currentFilterChildtype > 0 && itemCfg.Childtype != _currentFilterChildtype) continue;

            EquipSlot equipSlot = EquipManager.Instance.GetEquipSlotByInstance(entry.instanceId);
            if (equipSlot != (EquipSlot)(-1)) continue;

            BagItem bagItem = Instantiate(bagItemPrefab, content, false);
            bagItem.Setup(entry.instanceId, itemId, entry.count, itemCfg);
            bagItem.onClick = OnBagItemLeftClick;
            bagItem.onRightClick = OnBagItemRightClick;
            bagItem.onBeginDrag = OnBagItemBeginDrag;
            bagItem.onDrag = OnBagItemDrag;
            bagItem.onEndDrag = OnBagItemEndDrag;
            _bagItems.Add(bagItem);
        }
    }

    private void ClearList()
    {
        foreach (var item in _bagItems)
        {
            if (item != null)
                Destroy(item.gameObject);
        }
        _bagItems.Clear();
    }

    private void RefreshEquips()
    {
        foreach (var kvp in _equipSlots)
        {
            if (kvp.Value == null) continue;
            int equippedId = EquipManager.Instance.GetEquippedItemId(kvp.Key);
            kvp.Value.Setup(kvp.Key, equippedId > 0 ? equippedId : (int?)null);
        }
    }

    private void OnBagItemLeftClick(BagItem item)
    {
        _selectedEquipSlot = null;
        _selectedItemId = item.ItemId;
        _selectedInstanceId = item.InstanceId;
        HighlightBagItem(item);
        ShowItemDetail(item.ItemId);
    }

    private void OnBagItemRightClick(BagItem item)
    {
        _selectedEquipSlot = null;
        _selectedItemId = item.ItemId;
        _selectedInstanceId = item.InstanceId;
        HighlightBagItem(item);
        ShowItemDetail(item.ItemId);

        UseSelectedItem();
    }

    private void OnBtnUseItemClicked()
    {
        if (_selectedItemId <= 0) return;
        UseSelectedItem();
    }

    private void OnBtnCloseClicked()
    {
        GameplayState.CloseBagIfOpen();
    }

    private void UseSelectedItem()
    {
        if (_selectedItemId <= 0) return;

        var tables = GetTables();
        if (tables == null) return;

        var itemCfg = tables.TbItem.GetOrDefault(_selectedItemId);
        if (itemCfg == null) return;

        if (itemCfg.Type == 4)
        {
            EquipManager.Instance.EquipByInstance(_selectedInstanceId);
        }
        else if (itemCfg.Type == 3)
        {
            BagManager.Instance.RemoveItem(_selectedItemId, 1);
            Debug.Log($"[BagPanel] {UIStrings.Bag.Use}{UIStrings.Common.Confirm}: {_selectedItemId}");
        }
    }

    private void OnEquipSlotClicked(BagEquip equip)
    {
        _selectedItemId = 0;
        _selectedEquipSlot = equip.Slot;

        btnEquip.isOn = true;
        btnTrash.isOn = false;
        btnUse.isOn = false;
        btnKey.isOn = false;

        _currentFilterType = 4;
        _currentFilterChildtype = (int)equip.Slot;

        HighlightEquipSlot(equip);
        RefreshList();

        int equippedId = EquipManager.Instance.GetEquippedItemId(equip.Slot);
        if (equippedId > 0)
            ShowItemDetail(equippedId);
        else
            ClearDetail();
    }

    private void OnEquipSlotBtnEquipClicked(BagEquip equip)
    {
        int childtype = (int)equip.Slot;
        btnEquip.isOn = true;
        btnTrash.isOn = false;
        btnUse.isOn = false;
        btnKey.isOn = false;

        _currentFilterType = 4;
        _currentFilterChildtype = childtype;

        RefreshList();

        _selectedItemId = 0;
        _selectedEquipSlot = equip.Slot;
        HighlightEquipSlot(equip);

        int equippedId = EquipManager.Instance.GetEquippedItemId(equip.Slot);
        if (equippedId > 0)
            ShowItemDetail(equippedId);
        else
            ClearDetail();
    }

    private void OnEquipSlotBtnUnequipClicked(BagEquip equip)
    {
        if (!EquipManager.Instance.IsEquipped(equip.Slot)) return;
        EquipManager.Instance.Unequip(equip.Slot);
    }

    private void OnEquipSlotDropItem(BagEquip equip, BagItem bagItem)
    {
        var tables = GetTables();
        if (tables == null) return;

        var itemCfg = tables.TbItem.GetOrDefault(bagItem.ItemId);
        if (itemCfg == null) return;

        if (itemCfg.Type != 4)
        {
            Debug.Log($"[BagPanel] 物品 {itemCfg.Name}{UIStrings.Bag.NotEquipable}");
            return;
        }

        EquipSlot targetSlot = (EquipSlot)itemCfg.Childtype;
        if (targetSlot != equip.Slot)
        {
            Debug.Log($"[BagPanel] 物品 {itemCfg.Name}{UIStrings.Bag.SlotMismatch}");
            return;
        }

        EquipManager.Instance.EquipByInstance(bagItem.InstanceId);
        CleanupDragIcon();
    }

    private void OnBagItemBeginDrag(BagItem item, PointerEventData eventData)
    {
        if (_dragIcon != null)
            Destroy(_dragIcon);

        _dragBagItem = item;
        _isDragging = true;

        var canvas = GetComponentInParent<Canvas>();
        _dragIcon = new GameObject("DragIcon");
        _dragIcon.transform.SetParent(canvas != null ? canvas.transform : transform.root, false);
        _dragIcon.layer = LayerMask.NameToLayer("UI");

        var img = _dragIcon.AddComponent<Image>();
        img.sprite = item.IconSprite;
        img.raycastTarget = false;
        img.color = new Color(1, 1, 1, 0.9f);

        var rt = _dragIcon.GetComponent<RectTransform>();
        rt.sizeDelta = item.IconSize;
        rt.position = item.transform.position;

        var cg = _dragIcon.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;
        cg.alpha = 0.8f;

        var tables = GetTables();
        var itemCfg = tables != null ? tables.TbItem.GetOrDefault(item.ItemId) : null;
        bool isEquip = itemCfg != null && itemCfg.Type == 4;
        EquipSlot dragSlot = isEquip ? (EquipSlot)itemCfg.Childtype : (EquipSlot)(-1);

        foreach (var kvp in _equipSlots)
        {
            if (kvp.Value != null)
                kvp.Value.SetHighlight(isEquip && kvp.Key == dragSlot);
        }
    }

    private void OnBagItemDrag(PointerEventData eventData)
    {
        if (_dragIcon == null) return;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _dragIcon.transform.parent as RectTransform,
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 localPos);

        _dragIcon.GetComponent<RectTransform>().localPosition = localPos;
    }

    private void OnBagItemEndDrag(PointerEventData eventData)
    {
        CleanupDragIcon();
    }

    private void CleanupDragIcon()
    {
        if (_dragIcon != null)
        {
            Destroy(_dragIcon);
            _dragIcon = null;
        }

        _isDragging = false;
        _dragBagItem = null;

        foreach (var kvp in _equipSlots)
        {
            if (kvp.Value != null)
                kvp.Value.SetHighlight(false);
        }
    }

    private void ShowItemDetail(int itemId)
    {
        var tables = GetTables();
        if (tables == null) return;

        var itemCfg = tables.TbItem.GetOrDefault(itemId);
        if (itemCfg == null) return;

        if (itemTipRoot != null)
            itemTipRoot.SetActive(true);

        if (itemIcon != null)
        {
            itemIcon.gameObject.SetActive(true);
            itemIcon.sprite = LoadIcon(itemCfg.Icon);
        }

        if (itemName != null)
            itemName.text = itemCfg.Name;

        if (itemTip != null)
            itemTip.text = itemCfg.Tip;

        bool showBtn = false;
        string actionLabel = "";

        if (itemCfg.Type == 4)
        {
            bool isThisEquipped = EquipManager.Instance.IsInstanceEquipped(_selectedInstanceId);
            if (isThisEquipped)
            {
                actionLabel = UIStrings.Bag.Equipped;
                showBtn = false;
            }
            else
            {
                actionLabel = UIStrings.Bag.Equip;
                showBtn = true;
            }
        }
        else if (itemCfg.Type == 3)
        {
            bool hasFunc = itemCfg.Func != null && itemCfg.Func.Count > 0;
            if (hasFunc)
            {
                actionLabel = UIStrings.Bag.Use;
                showBtn = true;
            }
            else
            {
                actionLabel = "";
                showBtn = false;
            }
        }

        if (typeName != null)
        {
            typeName.gameObject.SetActive(!string.IsNullOrEmpty(actionLabel));
            typeName.text = actionLabel;
        }

        if (btnUseItem != null)
            btnUseItem.gameObject.SetActive(showBtn);
    }

    private void ClearDetail()
    {
        if (itemTipRoot != null)
            itemTipRoot.SetActive(false);

        if (itemIcon != null)
        {
            itemIcon.sprite = null;
            itemIcon.gameObject.SetActive(false);
        }
        if (itemName != null) itemName.text = "";
        if (itemTip != null) itemTip.text = "";
        if (typeName != null)
        {
            typeName.text = "";
            typeName.gameObject.SetActive(false);
        }
        if (btnUseItem != null)
            btnUseItem.gameObject.SetActive(false);
    }

    private void ClearSelection()
    {
        _selectedItemId = 0;
        _selectedInstanceId = 0;
        _selectedEquipSlot = null;

        foreach (var item in _bagItems)
            item.SetSelected(false);
        foreach (var kvp in _equipSlots)
        {
            if (kvp.Value != null)
                kvp.Value.SetSelected(false);
        }
        ClearDetail();
    }

    private void HighlightBagItem(BagItem selected)
    {
        foreach (var item in _bagItems)
            item.SetSelected(item == selected);
        foreach (var kvp in _equipSlots)
        {
            if (kvp.Value != null)
                kvp.Value.SetSelected(false);
        }
    }

    private void HighlightEquipSlot(BagEquip selected)
    {
        foreach (var item in _bagItems)
            item.SetSelected(false);
        foreach (var kvp in _equipSlots)
        {
            if (kvp.Value != null)
                kvp.Value.SetSelected(kvp.Value == selected);
        }
    }

    private void OnItemChanged(BagManager.BagChangeType changeType, int itemId)
    {
        if (this == null) return;   // 面板已销毁：事件里可能残留已销毁引用，别碰 gameObject
        if (!gameObject.activeInHierarchy) return;
        RefreshList();
        if (_selectedItemId == itemId)
        {
            int remaining = BagManager.Instance.GetItemCount(itemId);
            if (remaining <= 0)
                ClearSelection();
            else
                ShowItemDetail(itemId);
        }
    }

    private void OnEquipChanged(EquipSlot slot, int itemId)
    {
        if (!gameObject.activeInHierarchy) return;
        RefreshEquips();
        RefreshList();
        if (_selectedEquipSlot.HasValue && _selectedEquipSlot.Value == slot)
        {
            if (itemId > 0)
                ShowItemDetail(itemId);
            else
                ClearSelection();
        }
    }

    // —— 顶部信息：等级 / 经验进度 / 左列属性 / 右列技能 ——

    private void RefreshTopInfo()
    {
        CharacterStatsManager mgr = CharacterStatsManager.Instance;
        CharacterStats stats = mgr != null ? mgr.PlayerStats : null;
        if (stats == null) return;

        if (topLevelText != null)
            topLevelText.text = "LEVEL:" + stats.Level;

        RefreshTopExp(stats);
        RefreshTopAttrs(stats);
        RefreshTopTraits();
    }

    /// <summary>经验进度：等级表一行 = 一个等级（Id = 等级），该行 Level 列 = 升下一级所需经验。
    /// cur = 本级已攒经验，max = 本级升级所需经验；表里没有该等级时显示 MAX。（与 GainExpDisplay 同一套算法）</summary>
    private void RefreshTopExp(CharacterStats stats)
    {
        int cur = Mathf.Max(0, stats.Exp);
        int max = 0;
        bool maxed = false;

        var tables = GetTables();
        if (tables != null && tables.TbCharacterlevel != null)
        {
            int needExp = -1;   // 本级升级所需经验
            foreach (var row in tables.TbCharacterlevel.DataList)
            {
                if (row.Id != stats.Level) continue;
                needExp = row.Level;
                break;
            }

            if (needExp < 0) maxed = true;            // 表里没有该等级
            else max = Mathf.Max(1, needExp);
        }
        else
        {
            maxed = true;   // 等级表未就绪：按满格处理
        }

        if (topExpFill != null)
            topExpFill.fillAmount = maxed ? 1f : Mathf.Clamp01(max > 0 ? (float)cur / max : 0f);

        if (topExpText != null)
            topExpText.text = maxed ? "MAX" : (cur + "/" + max);
    }

    /// <summary>左列：力量 / 体质 / 理智（取含加成的最终值）。</summary>
    private void RefreshTopAttrs(CharacterStats stats)
    {
        ClearInfoRows(attrContent);
        AddInfoRow(attrContent, "力量：" + stats.FinalDestroy);
        AddInfoRow(attrContent, "体质：" + stats.FinalSelfmatain);
        AddInfoRow(attrContent, "理智：" + stats.FinalCreatorwilling);
    }

    /// <summary>右列：玩家的特质 = 长期 buff（buff 表 showtype==2「背包显示」的那些）。</summary>
    private void RefreshTopTraits()
    {
        ClearInfoRows(perkContent);

        var tables = GetTables();
        if (tables == null || tables.TbBuff == null) return;

        var ids = CollectTraitIds(tables);
        foreach (int buffId in ids)
        {
            var buffCfg = tables.TbBuff.GetOrDefault(buffId);
            if (buffCfg == null) continue;

            TextMeshProUGUI row = AddInfoRow(perkContent, buffCfg.Name);
            AttachHover(row, buffId);
        }
    }

    /// <summary>特质 id 来源：person 表 buffIds（角色持有 buff）+ 运行时 BuffManager 活跃 buff，
    /// 过滤条件 = buff 表 showtype==2（背包显示）。</summary>
    private List<int> CollectTraitIds(cfg.Tables tables)
    {
        _traitIds.Clear();

        var person = tables.TbPerson != null ? tables.TbPerson.GetOrDefault(PlayerPersonId) : null;
        if (person != null && person.BuffIds != null)
        {
            foreach (int id in person.BuffIds)
                if (id > 0) _traitIds.Add(id);
        }

        var buffMgr = BuffManager.Instance;
        foreach (var active in buffMgr.ActiveBuffs)
            if (active != null && active.BuffId > 0) _traitIds.Add(active.BuffId);

        var result = new List<int>();
        foreach (int id in _traitIds)
        {
            var buffCfg = tables.TbBuff.GetOrDefault(id);
            if (buffCfg == null || buffCfg.Showtype != 2) continue;
            result.Add(id);
        }
        result.Sort();
        return result;
    }

    private void AttachHover(TextMeshProUGUI row, int buffId)
    {
        if (row == null) return;

        row.raycastTarget = true;   // 行要能收到鼠标事件
        var hover = row.gameObject.GetComponent<BagInfoRowHover>();
        if (hover == null) hover = row.gameObject.AddComponent<BagInfoRowHover>();
        hover.Bind(this, buffId);
    }

    private void OnBuffChanged(int buffId, bool added)
    {
        if (this == null) return;
        if (!gameObject.activeInHierarchy) return;
        RefreshTopTraits();
    }

    /// <summary>悬浮特质行：用通用提示框 CommonTipPanel 贴着鼠标显示这条特质（buff）的详情。</summary>
    public void ShowTraitTip(int buffId, Vector2 screenPosition)
    {
        var tables = GetTables();
        if (tables == null || tables.TbBuff == null) return;

        var buffCfg = tables.TbBuff.GetOrDefault(buffId);
        if (buffCfg == null) return;

        // 拼文本：名字 / 文本描述 / 功能说明（缺项自动跳过）
        string desc = buffCfg.Descrption;
        string func = buffCfg.Tip;
        string body;
        if (string.IsNullOrEmpty(func)) body = desc;
        else if (string.IsNullOrEmpty(desc)) body = func;
        else body = desc + "\n" + func;

        string text = buffCfg.Name;
        if (!string.IsNullOrEmpty(body))
            text = string.IsNullOrEmpty(text) ? body : text + "\n" + body;
        if (string.IsNullOrEmpty(text)) return;

        _traitTipShowing = true;
        CommonTipPanel.ShowAt(text, screenPosition);
    }

    /// <summary>鼠标移出特质行：关掉通用提示框（右下角物品详情本来就没被动过）。</summary>
    public void HideTraitTip()
    {
        if (!_traitTipShowing) return;
        _traitTipShowing = false;
        CommonTipPanel.Hide();
    }

    private void ClearInfoRows(Transform content)
    {
        if (content == null) return;

        var removing = new List<GameObject>();
        for (int i = 0; i < content.childCount; i++)
        {
            Transform child = content.GetChild(i);
            if (infoRowTemplate != null && child == infoRowTemplate.transform) continue;
            removing.Add(child.gameObject);
        }
        foreach (var go in removing)
            Destroy(go);
    }

    private TextMeshProUGUI AddInfoRow(Transform content, string text)
    {
        if (content == null || infoRowTemplate == null) return null;

        TextMeshProUGUI row = Instantiate(infoRowTemplate, content, false);
        row.gameObject.SetActive(true);
        row.text = text;
        return row;
    }

    private void OnStatsChanged()
    {
        if (this == null) return;   // 面板已销毁时的残留引用
        if (!gameObject.activeInHierarchy) return;
        RefreshTopInfo();
    }

    private cfg.Tables GetTables()
    {
        if (ManagerRegistry.TryGet<DataTableManager>(out var dtm) && dtm.Tables != null)
            return dtm.Tables;
        return null;
    }

    private Sprite LoadIcon(int iconId)
    {
        return IconLoader.LoadItemIcon(iconId);
    }
}

/// <summary>顶部信息行的鼠标悬浮处理：进入显示提示框，移出恢复。</summary>
public class BagInfoRowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private BagPanel _panel;
    private int _buffId;

    public void Bind(BagPanel panel, int buffId)
    {
        _panel = panel;
        _buffId = buffId;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_panel != null) _panel.ShowTraitTip(_buffId, eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (_panel != null) _panel.HideTraitTip();
    }
}
