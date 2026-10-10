using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class BagManager
{
    private static BagManager _instance;
    public static BagManager Instance => _instance ??= new BagManager();

    private const int PlayerPersonId = 1;

    private int _nextInstanceId = 1;

    private readonly List<BagItemEntry> _items = new List<BagItemEntry>();

    public IReadOnlyList<BagItemEntry> AllItems => _items;

    public event Action<BagChangeType, int> OnItemChanged;

    public int GetItemCount(int itemId)
    {
        return _items.Where(x => x.itemId == itemId).Sum(x => x.count);
    }

    public bool HasItem(int itemId, int count = 1)
    {
        return GetItemCount(itemId) >= count;
    }

    /// <summary>虚拟经验物品 → 每单位经验值（item 表『经验值+N』系列，id=10『经验值+1』）。
    /// 获取时直接消化成经验：不进背包列表、不进存档；将来新增经验物品在这里登记一行即可。</summary>
    private static readonly Dictionary<int, int> ExpItemValues = new Dictionary<int, int>
    {
        { 10, 1 },   // 『经验值+1』
    };

    public int AddItem(int itemId, int count = 1)
    {
        if (count <= 0) return 0;

        var itemCfg = GetItemConfig(itemId);
        if (itemCfg == null)
        {
            Debug.LogWarning($"[BagManager] 物品配置不存在: {itemId}");
            return 0;
        }

        // —— 虚拟经验物品：获取即转化，不进背包、不进存档。
        //    仍返回 count（added>0）：调用方流程照走——拾取分支会正常弹 SceneTip 物品提示。
        if (ExpItemValues.TryGetValue(itemId, out int expPerUnit))
        {
            int expAmount = expPerUnit * count;
            CharacterStatsManager.Instance.AddExp(expAmount);
            Debug.Log($"[BagManager] 经验物品转化: {itemCfg.Name} x{count} → AddExp({expAmount})");
            return count;
        }

        int added = 0;

        if (itemCfg.Stackable)
        {
            var existing = _items.FirstOrDefault(x => x.itemId == itemId);
            if (existing != null)
            {
                existing.count += count;
                added = count;
            }
            else
            {
                _items.Add(new BagItemEntry
                {
                    instanceId = _nextInstanceId++,
                    itemId = itemId,
                    count = count
                });
                added = count;
            }
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                _items.Add(new BagItemEntry
                {
                    instanceId = _nextInstanceId++,
                    itemId = itemId,
                    count = 1
                });
            }
            added = count;
        }

        if (added > 0)
        {
            OnItemChanged?.Invoke(BagChangeType.Added, itemId);
            ConditionSystem.Instance.Notify(ConditionChangeType.Item);
        }

        return added;
    }

    public bool RemoveItem(int itemId, int count = 1)
    {
        if (count <= 0) return true;

        int available = GetItemCount(itemId);
        if (available < count) return false;

        if (IsStackable(itemId))
        {
            var entry = _items.FirstOrDefault(x => x.itemId == itemId);
            if (entry == null) return false;

            entry.count -= count;
            if (entry.count <= 0)
                _items.Remove(entry);
        }
        else
        {
            for (int i = 0; i < count; i++)
            {
                int idx = _items.FindLastIndex(x => x.itemId == itemId);
                if (idx >= 0)
                    _items.RemoveAt(idx);
            }
        }

        OnItemChanged?.Invoke(BagChangeType.Removed, itemId);
        ConditionSystem.Instance.Notify(ConditionChangeType.Item);
        return true;
    }

    public bool RemoveByInstanceId(int instanceId)
    {
        int idx = _items.FindIndex(x => x.instanceId == instanceId);
        if (idx < 0) return false;

        int itemId = _items[idx].itemId;
        _items.RemoveAt(idx);
        OnItemChanged?.Invoke(BagChangeType.Removed, itemId);
        return true;
    }

    public BagItemEntry GetByInstanceId(int instanceId)
    {
        return _items.FirstOrDefault(x => x.instanceId == instanceId);
    }

    public List<BagItemEntry> GetItemsOfType(int itemId)
    {
        return _items.Where(x => x.itemId == itemId).ToList();
    }

    public void Clear()
    {
        _items.Clear();
    }

    public List<BagSaveEntry> BuildSaveData()
    {
        var result = new List<BagSaveEntry>(_items.Count);
        foreach (var entry in _items)
        {
            result.Add(new BagSaveEntry
            {
                instanceId = entry.instanceId,
                itemId = entry.itemId,
                count = entry.count
            });
        }
        return result;
    }

    public void RestoreFromSaveData(List<BagSaveEntry> data)
    {
        _items.Clear();
        if (data == null) return;

        foreach (var entry in data)
        {
            if (entry.count > 0)
            {
                int iid = entry.instanceId > 0 ? entry.instanceId : _nextInstanceId++;
                _items.Add(new BagItemEntry
                {
                    instanceId = iid,
                    itemId = entry.itemId,
                    count = entry.count
                });
                if (iid >= _nextInstanceId)
                    _nextInstanceId = iid + 1;
            }
        }
    }

    public void InitFromConfig()
    {
        _items.Clear();

        if (!ManagerRegistry.TryGet<DataTableManager>(out var dtm) || dtm.Tables == null)
        {
            Debug.LogWarning("[BagManager] DataTableManager 未加载，无法初始化背包");
            return;
        }

        var tbBag = dtm.Tables.TbBag;
        foreach (var bagEntry in tbBag.DataList)
        {
            if (bagEntry.Ownerid == PlayerPersonId && bagEntry.Num > 0)
            {
                AddItem(bagEntry.Itemid, bagEntry.Num);
            }
        }

        Debug.Log($"[BagManager] 从配置初始化背包: {_items.Count} 个物品实例");
    }

    private bool IsStackable(int itemId)
    {
        var cfg = GetItemConfig(itemId);
        return cfg != null && cfg.Stackable;
    }

    public cfg.cfg.item.Item GetItemConfig(int itemId)
    {
        if (ManagerRegistry.TryGet<DataTableManager>(out var dtm) && dtm.Tables != null)
        {
            return dtm.Tables.TbItem.GetOrDefault(itemId);
        }
        return null;
    }

    public enum BagChangeType
    {
        Added,
        Removed
    }

    public class BagItemEntry
    {
        public int instanceId;
        public int itemId;
        public int count;
    }

    [Serializable]
    public class BagSaveEntry
    {
        public int instanceId;
        public int itemId;
        public int count;
    }
}
