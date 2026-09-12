using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public sealed class DeathDropResult
{
    public InventorySnapshot RetainedInventory { get; }
    public IReadOnlyDictionary<ResourceType, int> DroppedIngredients { get; }
    public IReadOnlyDictionary<ResourceType, int> RetainedGathered { get; }
    public IReadOnlyDictionary<ResourceType, int> DroppedGathered { get; }

    internal DeathDropResult(
        InventorySnapshot retainedInventory,
        IDictionary<ResourceType, int> droppedIngredients,
        IDictionary<ResourceType, int> retainedGathered,
        IDictionary<ResourceType, int> droppedGathered)
    {
        RetainedInventory = retainedInventory;
        DroppedIngredients = CopyReadOnly(droppedIngredients);
        RetainedGathered = CopyReadOnly(retainedGathered);
        DroppedGathered = CopyReadOnly(droppedGathered);
    }

    private static IReadOnlyDictionary<ResourceType, int> CopyReadOnly(IDictionary<ResourceType, int> counts)
    {
        return new ReadOnlyDictionary<ResourceType, int>(new Dictionary<ResourceType, int>(counts));
    }
}

public static class DeathDropCalculator
{
    public static DeathDropResult Calculate(
        InventorySnapshot inventory,
        IReadOnlyDictionary<ResourceType, int> gathered,
        decimal retentionRate)
    {
        if (inventory == null) throw new ArgumentNullException(nameof(inventory));
        if (gathered == null) throw new ArgumentNullException(nameof(gathered));
        if (retentionRate < 0m || retentionRate > 1m)
            throw new ArgumentOutOfRangeException(nameof(retentionRate), "Retention must be between zero and one.");

        var retainedGathered = new Dictionary<ResourceType, int>();
        var droppedGathered = new Dictionary<ResourceType, int>();
        foreach (KeyValuePair<ResourceType, int> entry in gathered)
        {
            if (!ResourceStorageRules.IsGathered(entry.Key))
                throw new ArgumentException("Only gathered resource types are allowed in gathered counts.", nameof(gathered));
            if (entry.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(gathered), "Gathered counts cannot be negative.");

            int retained = RetainCount(entry.Value, retentionRate);
            AddPositive(retainedGathered, entry.Key, retained);
            AddPositive(droppedGathered, entry.Key, entry.Value - retained);
        }

        var retentionRemaining = new Dictionary<ResourceType, int>();
        var droppedIngredients = new Dictionary<ResourceType, int>();
        foreach (KeyValuePair<ResourceType, int> entry in inventory.GetCounts())
        {
            if (!ResourceStorageRules.IsIngredient(entry.Key)) continue;
            int retained = RetainCount(entry.Value, retentionRate);
            retentionRemaining.Add(entry.Key, retained);
            AddPositive(droppedIngredients, entry.Key, entry.Value - retained);
        }

        // Allocate each type's single retention quota in original slot-index order.
        // A surviving stack never grows beyond its previous count or moves to an empty slot.
        var orderedSlots = new List<InventorySlotSnapshot>(inventory.Slots);
        orderedSlots.Sort((left, right) => left.Index.CompareTo(right.Index));
        var retainedByIndex = new Dictionary<int, InventorySlotSnapshot>();
        foreach (InventorySlotSnapshot slot in orderedSlots)
        {
            if (slot.IsEmpty || !ResourceStorageRules.IsIngredient(slot.ItemType))
            {
                retainedByIndex.Add(slot.Index, slot);
                continue;
            }

            int retained = Math.Min(slot.Count, retentionRemaining[slot.ItemType]);
            retentionRemaining[slot.ItemType] -= retained;
            retainedByIndex.Add(slot.Index, new InventorySlotSnapshot(slot.Index,
                retained == 0 ? ResourceType.None : slot.ItemType, retained, slot.Capacity));
        }

        var resultSlots = new List<InventorySlotSnapshot>();
        foreach (InventorySlotSnapshot slot in inventory.Slots)
            resultSlots.Add(retainedByIndex[slot.Index]);

        return new DeathDropResult(new InventorySnapshot(resultSlots), droppedIngredients,
            retainedGathered, droppedGathered);
    }

    private static int RetainCount(int count, decimal rate)
    {
        return checked((int)decimal.Ceiling(count * rate));
    }

    private static void AddPositive(Dictionary<ResourceType, int> counts, ResourceType type, int count)
    {
        if (count > 0) counts.Add(type, count);
    }
}
