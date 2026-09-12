using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public sealed class InventorySlotSnapshot
{
    public int Index { get; }
    public ResourceType ItemType { get; }
    public int Count { get; }
    public int Capacity { get; }
    public bool IsEmpty { get { return Count == 0; } }

    public InventorySlotSnapshot(int index, ResourceType itemType, int count, int capacity)
    {
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (capacity < 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (count > capacity)
            throw new ArgumentException("The item count cannot exceed slot capacity.", nameof(count));
        ValidateItemType(itemType, count);

        Index = index;
        ItemType = itemType;
        Count = count;
        Capacity = capacity;
    }

    internal static void ValidateItemType(ResourceType itemType, int count)
    {
        if (!Enum.IsDefined(typeof(ResourceType), itemType))
            throw new ArgumentOutOfRangeException(nameof(itemType));
        if (count > 0 && itemType == ResourceType.None)
            throw new ArgumentException("An occupied slot or request must specify a resource type.", nameof(itemType));
    }
}

public sealed class InventorySnapshot
{
    private readonly ReadOnlyCollection<InventorySlotSnapshot> slots;
    private readonly Dictionary<ResourceType, int> counts;

    public IReadOnlyList<InventorySlotSnapshot> Slots { get { return slots; } }

    public InventorySnapshot(IEnumerable<InventorySlotSnapshot> slots)
    {
        if (slots == null) throw new ArgumentNullException(nameof(slots));

        var copiedSlots = new List<InventorySlotSnapshot>();
        var indices = new HashSet<int>();
        counts = new Dictionary<ResourceType, int>();

        foreach (InventorySlotSnapshot slot in slots)
        {
            if (slot == null)
                throw new ArgumentException("Slots cannot contain null entries.", nameof(slots));
            if (!indices.Add(slot.Index))
                throw new ArgumentException("Slot indices must be unique.", nameof(slots));

            copiedSlots.Add(slot);
            if (slot.IsEmpty) continue;

            int previousCount;
            counts.TryGetValue(slot.ItemType, out previousCount);
            counts[slot.ItemType] = checked(previousCount + slot.Count);
        }

        this.slots = copiedSlots.AsReadOnly();
    }

    public int GetItemCount(ResourceType itemType)
    {
        int count;
        return counts.TryGetValue(itemType, out count) ? count : 0;
    }

    public Dictionary<ResourceType, int> GetCounts()
    {
        return new Dictionary<ResourceType, int>(counts);
    }
}
