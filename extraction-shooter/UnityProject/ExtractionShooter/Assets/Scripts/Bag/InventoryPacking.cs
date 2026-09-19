using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public sealed class InventoryPackResult
{
    public InventorySnapshot Snapshot { get; }
    public IReadOnlyDictionary<ResourceType, int> Accepted { get; }
    public IReadOnlyDictionary<ResourceType, int> Remaining { get; }

    internal InventoryPackResult(
        InventorySnapshot snapshot,
        IDictionary<ResourceType, int> accepted,
        IDictionary<ResourceType, int> remaining)
    {
        Snapshot = snapshot;
        Accepted = new ReadOnlyDictionary<ResourceType, int>(
            new Dictionary<ResourceType, int>(accepted));
        Remaining = new ReadOnlyDictionary<ResourceType, int>(
            new Dictionary<ResourceType, int>(remaining));
    }
}

public static class InventoryPacking
{
    private sealed class WorkingSlot
    {
        public int Index;
        public ResourceType ItemType;
        public int Count;
        public int Capacity;
    }

    public static InventoryPackResult Add(
        InventorySnapshot current,
        IReadOnlyDictionary<ResourceType, int> requested,
        Func<ResourceType, int> priority = null)
    {
        if (current == null) throw new ArgumentNullException(nameof(current));
        if (requested == null) throw new ArgumentNullException(nameof(requested));

        var remaining = new Dictionary<ResourceType, int>();
        foreach (KeyValuePair<ResourceType, int> entry in requested)
        {
            if (entry.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(requested), "Requested amounts cannot be negative.");
            InventorySlotSnapshot.ValidateItemType(entry.Key, entry.Value);
            if (entry.Value > 0)
                remaining.Add(entry.Key, entry.Value);
        }

        var slots = new List<WorkingSlot>();
        foreach (InventorySlotSnapshot slot in current.Slots)
        {
            slots.Add(new WorkingSlot
            {
                Index = slot.Index,
                ItemType = slot.ItemType,
                Count = slot.Count,
                Capacity = slot.Capacity
            });
        }

        var accepted = new Dictionary<ResourceType, int>();
        var types = new List<ResourceType>(remaining.Keys);
        types.Sort();

        // Finish stacking every resource before any request can occupy an empty slot.
        foreach (ResourceType type in types)
        {
            var matchingSlots = slots.FindAll(slot =>
                slot.Count > 0 && slot.Count < slot.Capacity && slot.ItemType == type);
            matchingSlots.Sort((left, right) =>
            {
                int byCount = right.Count.CompareTo(left.Count);
                return byCount != 0 ? byCount : left.Index.CompareTo(right.Index);
            });

            foreach (WorkingSlot slot in matchingSlots)
            {
                if (remaining[type] == 0) break;
                FillSlot(slot, type, remaining, accepted);
            }
        }

        var priorities = new Dictionary<ResourceType, int>();
        foreach (ResourceType type in types)
        {
            if (remaining[type] > 0)
                priorities.Add(type, priority == null ? 0 : priority(type));
        }

        types.RemoveAll(type => remaining[type] == 0);
        types.Sort((left, right) =>
        {
            int byPriority = priorities[right].CompareTo(priorities[left]);
            return byPriority != 0 ? byPriority : left.CompareTo(right);
        });

        var emptySlots = slots.FindAll(slot => slot.Count == 0 && slot.Capacity > 0);
        emptySlots.Sort((left, right) => left.Index.CompareTo(right.Index));
        int nextEmptySlot = 0;
        foreach (ResourceType type in types)
        {
            while (remaining[type] > 0 && nextEmptySlot < emptySlots.Count)
            {
                FillSlot(emptySlots[nextEmptySlot], type, remaining, accepted);
                nextEmptySlot++;
            }
        }

        var resultSlots = new List<InventorySlotSnapshot>();
        foreach (WorkingSlot slot in slots)
        {
            resultSlots.Add(new InventorySlotSnapshot(slot.Index, slot.ItemType, slot.Count, slot.Capacity));
        }

        foreach (ResourceType type in new List<ResourceType>(remaining.Keys))
        {
            if (remaining[type] == 0)
                remaining.Remove(type);
        }

        return new InventoryPackResult(new InventorySnapshot(resultSlots), accepted, remaining);
    }

    private static void FillSlot(
        WorkingSlot slot,
        ResourceType type,
        Dictionary<ResourceType, int> remaining,
        Dictionary<ResourceType, int> accepted)
    {
        int amount = Math.Min(remaining[type], slot.Capacity - slot.Count);
        if (amount <= 0) return;

        slot.ItemType = type;
        slot.Count = checked(slot.Count + amount);
        remaining[type] -= amount;
        int previousAccepted;
        accepted.TryGetValue(type, out previousAccepted);
        accepted[type] = checked(previousAccepted + amount);
    }
}
