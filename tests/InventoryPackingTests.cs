using System;
using System.Collections.Generic;

public static class InventoryPackingTests
{
    private const ResourceType FoodA = ResourceType.LootMushroom;
    private const ResourceType FoodB = ResourceType.LootTomato;
    private const ResourceType FoodC = ResourceType.LootFish;

    public static void Run()
    {
        StacksAllTypesBeforeAllocatingEmptySlots();
        StacksFullerSlotsThenLowerIndices();
        RespectsPriorityAndStableTypeOrder();
        ReturnsPartialAcceptedAmounts();
        PreservesEmptySlotsAndHandlesZeroCapacity();
        ProtectsSnapshotsAndResultCollections();
        RejectsInvalidInputAndOverflow();
        ConservesAmountsAcrossVariedInventories();
    }

    private static void StacksAllTypesBeforeAllocatingEmptySlots()
    {
        var current = Snapshot(
            Slot(0, FoodA, 3, 4), Slot(1, FoodB, 1, 4), Slot(2, ResourceType.None, 0, 4));
        var requested = Counts(FoodA, 5, FoodB, 5);
        InventoryPackResult result = InventoryPacking.Add(current, requested, type => type == FoodB ? 10 : 0);

        Equal(1, Count(result.Accepted, FoodA), "The existing Food A stack is filled first.");
        Equal(5, Count(result.Accepted, FoodB), "Food B uses its existing stack and then the empty slot.");
        Equal(4, Count(result.Remaining, FoodA), "Lower priority Food A remains outside the bag.");
        Equal(FoodB, result.Snapshot.Slots[2].ItemType, "Priority is applied only after stacking.");
        Equal(2, result.Snapshot.Slots[2].Count, "The final slot contains only the unstacked Food B.");
        Equal(3, current.GetItemCount(FoodA), "The original inventory is unchanged.");
        Equal(5, requested[FoodA], "The request is unchanged.");
    }

    private static void StacksFullerSlotsThenLowerIndices()
    {
        var current = Snapshot(Slot(8, FoodA, 2, 4), Slot(3, FoodA, 3, 4), Slot(1, FoodA, 2, 4));
        InventoryPackResult result = InventoryPacking.Add(current, Counts(FoodA, 2));

        Equal(2, result.Snapshot.Slots[0].Count, "Equal-sized stacks use the lower slot index first.");
        Equal(4, result.Snapshot.Slots[1].Count, "The fullest stack is filled before smaller stacks.");
        Equal(3, result.Snapshot.Slots[2].Count, "Remaining items use the lowest matching index.");
        Equal(8, result.Snapshot.Slots[0].Index, "Snapshot order is preserved independently of packing order.");
    }

    private static void RespectsPriorityAndStableTypeOrder()
    {
        var current = Snapshot(Slot(7, ResourceType.None, 0, 1), Slot(2, ResourceType.None, 0, 1));
        var requested = Counts(FoodC, 1, FoodB, 1, FoodA, 1);
        InventoryPackResult result = InventoryPacking.Add(current, requested, type => type == FoodC ? int.MaxValue : int.MinValue);

        Equal(FoodC, result.Snapshot.Slots[1].ItemType, "Highest priority uses the lowest-index empty slot.");
        Equal(FoodA, result.Snapshot.Slots[0].ItemType, "Equal priorities use ResourceType numeric order.");
        Equal(1, Count(result.Remaining, FoodB), "Lower-priority remaining resource is reported.");

        result = InventoryPacking.Add(current, requested);
        Equal(FoodA, result.Snapshot.Slots[1].ItemType, "Missing priorities use stable type order.");
        Equal(FoodB, result.Snapshot.Slots[0].ItemType, "Default order is independent of dictionary insertion order.");
    }

    private static void ReturnsPartialAcceptedAmounts()
    {
        var current = Snapshot(Slot(0, FoodA, 2, 4), Slot(1, ResourceType.None, 0, 3));
        InventoryPackResult result = InventoryPacking.Add(current, Counts(FoodA, 8));

        Equal(5, Count(result.Accepted, FoodA), "Partial capacity is accepted.");
        Equal(3, Count(result.Remaining, FoodA), "Unaccepted items remain available to the caller.");
        Equal(7, result.Snapshot.GetItemCount(FoodA), "Final count includes old and accepted inventory.");

        result = InventoryPacking.Add(Snapshot(Slot(0, FoodB, 1, 1)), Counts(FoodA, 4));
        Equal(0, result.Accepted.Count, "A full bag accepts no items.");
        Equal(4, Count(result.Remaining, FoodA), "A full bag reports the entire request.");
    }

    private static void PreservesEmptySlotsAndHandlesZeroCapacity()
    {
        var current = Snapshot(Slot(0, ResourceType.None, 0, 0), Slot(1, ResourceType.None, 0, 4), Slot(2, FoodA, 0, 4));
        InventoryPackResult result = InventoryPacking.Add(current, Counts(FoodA, 1, FoodB, 0, ResourceType.None, 0));

        Equal(3, result.Snapshot.Slots.Count, "Empty and zero-capacity slots remain in the snapshot.");
        Equal(0, result.Snapshot.Slots[0].Count, "A zero-capacity slot receives nothing.");
        Equal(1, result.Snapshot.Slots[1].Count, "An available empty slot receives the request.");
        True(result.Snapshot.Slots[2].IsEmpty, "Unused empty slots remain empty.");
        Equal(1, result.Accepted.Count, "Zero requests are omitted from results.");
        Equal(0, result.Remaining.Count, "Fully accepted requests are omitted from remaining amounts.");

        result = InventoryPacking.Add(Snapshot(), Counts(FoodA, 1));
        Equal(1, Count(result.Remaining, FoodA), "An empty inventory reports unaccepted items.");
        result = InventoryPacking.Add(current, new Dictionary<ResourceType, int>());
        Equal(3, result.Snapshot.Slots.Count, "An empty request preserves every slot.");
    }

    private static void ProtectsSnapshotsAndResultCollections()
    {
        var source = new List<InventorySlotSnapshot> { Slot(0, FoodA, 1, 4) };
        var current = new InventorySnapshot(source);
        source.Clear();
        Equal(1, current.Slots.Count, "The snapshot copies the source collection.");

        Dictionary<ResourceType, int> counts = current.GetCounts();
        counts[FoodA] = 100;
        Equal(1, current.GetItemCount(FoodA), "Returned counts cannot mutate the snapshot.");
        Throws<NotSupportedException>(() => ((IList<InventorySlotSnapshot>)current.Slots)[0] = Slot(0, FoodB, 1, 4));

        var request = Counts(FoodA, 8);
        InventoryPackResult result = InventoryPacking.Add(current, request);
        request[FoodA] = 0;
        Equal(3, Count(result.Accepted, FoodA), "Result amounts are independent of the request.");
        Equal(5, Count(result.Remaining, FoodA), "Remaining amounts are independent of the request.");
        Throws<NotSupportedException>(() => ((IDictionary<ResourceType, int>)result.Accepted)[FoodA] = 100);
        Throws<NotSupportedException>(() => ((IDictionary<ResourceType, int>)result.Remaining)[FoodA] = 100);
        Throws<NotSupportedException>(() => ((IList<InventorySlotSnapshot>)result.Snapshot.Slots).Clear());
    }

    private static void RejectsInvalidInputAndOverflow()
    {
        Throws<ArgumentOutOfRangeException>(() => Slot(-1, FoodA, 0, 1));
        Throws<ArgumentOutOfRangeException>(() => Slot(0, FoodA, -1, 1));
        Throws<ArgumentOutOfRangeException>(() => Slot(0, FoodA, 0, -1));
        Throws<ArgumentException>(() => Slot(0, FoodA, 2, 1));
        Throws<ArgumentException>(() => Slot(0, ResourceType.None, 1, 1));
        Throws<ArgumentOutOfRangeException>(() => Slot(0, (ResourceType)(-1), 0, 1));
        Throws<ArgumentNullException>(() => new InventorySnapshot(null));
        Throws<ArgumentException>(() => Snapshot((InventorySlotSnapshot)null));
        Throws<ArgumentException>(() => Snapshot(Slot(1, FoodA, 0, 1), Slot(1, FoodB, 0, 1)));
        Throws<OverflowException>(() => Snapshot(Slot(0, FoodA, int.MaxValue, int.MaxValue), Slot(1, FoodA, 1, 1)));

        var current = Snapshot(Slot(0, ResourceType.None, 0, 1));
        Throws<ArgumentNullException>(() => InventoryPacking.Add(null, Counts(FoodA, 1)));
        Throws<ArgumentNullException>(() => InventoryPacking.Add(current, null));
        Throws<ArgumentOutOfRangeException>(() => InventoryPacking.Add(current, Counts(FoodA, -1)));
        Throws<ArgumentException>(() => InventoryPacking.Add(current, Counts(ResourceType.None, 1)));
        Throws<ArgumentOutOfRangeException>(() => InventoryPacking.Add(current, Counts((ResourceType)(-1), 1)));
        Throws<InvalidOperationException>(() => InventoryPacking.Add(current, Counts(FoodA, 1), type => { throw new InvalidOperationException(); }));
        Equal(0, current.GetItemCount(FoodA), "Failed priority evaluation leaves the input unchanged.");

        var large = Snapshot(Slot(0, FoodA, int.MaxValue, int.MaxValue), Slot(1, ResourceType.None, 0, 1));
        Throws<OverflowException>(() => InventoryPacking.Add(large, Counts(FoodA, 1)));
        Equal(int.MaxValue, large.GetItemCount(FoodA), "An overflowing result does not mutate its input.");

        InventoryPackResult result = InventoryPacking.Add(current, Counts(FoodA, int.MaxValue));
        Equal(1, Count(result.Accepted, FoodA), "A maximum-sized request can be partially accepted.");
        Equal(int.MaxValue - 1, Count(result.Remaining, FoodA), "Large remainders do not wrap.");
    }

    private static void ConservesAmountsAcrossVariedInventories()
    {
        var random = new Random(81027);
        var types = new[] { FoodA, FoodB, FoodC };
        for (int iteration = 0; iteration < 128; iteration++)
        {
            var slots = new List<InventorySlotSnapshot>();
            for (int index = 0; index < random.Next(1, 9); index++)
            {
                int capacity = random.Next(0, 7);
                int count = random.Next(0, capacity + 1);
                ResourceType type = count == 0 ? ResourceType.None : types[random.Next(types.Length)];
                slots.Add(Slot(index, type, count, capacity));
            }

            var current = new InventorySnapshot(slots);
            var requested = new Dictionary<ResourceType, int>();
            foreach (ResourceType type in types)
                requested[type] = random.Next(0, 20);

            InventoryPackResult result = InventoryPacking.Add(current, requested, type => type == FoodB ? 2 : 1);
            foreach (ResourceType type in types)
            {
                Equal(requested[type], Count(result.Accepted, type) + Count(result.Remaining, type), "Requested amounts are conserved.");
                Equal(current.GetItemCount(type) + Count(result.Accepted, type), result.Snapshot.GetItemCount(type), "Inventory changes equal accepted amounts.");
            }

            for (int index = 0; index < current.Slots.Count; index++)
            {
                InventorySlotSnapshot before = current.Slots[index];
                InventorySlotSnapshot after = result.Snapshot.Slots[index];
                Equal(before.Index, after.Index, "Packing preserves slot indices.");
                Equal(before.Capacity, after.Capacity, "Packing preserves slot capacities.");
                True(after.Count >= before.Count && after.Count <= after.Capacity, "Packing does not remove items or overfill slots.");
                if (!before.IsEmpty)
                    Equal(before.ItemType, after.ItemType, "Occupied slots keep their resource type.");
            }
        }
    }

    private static InventorySlotSnapshot Slot(int index, ResourceType type, int count, int capacity)
    {
        return new InventorySlotSnapshot(index, type, count, capacity);
    }

    private static InventorySnapshot Snapshot(params InventorySlotSnapshot[] slots)
    {
        return new InventorySnapshot(slots);
    }

    private static Dictionary<ResourceType, int> Counts(ResourceType firstType, int firstCount)
    {
        return new Dictionary<ResourceType, int> { { firstType, firstCount } };
    }

    private static Dictionary<ResourceType, int> Counts(ResourceType firstType, int firstCount, ResourceType secondType, int secondCount)
    {
        var counts = Counts(firstType, firstCount);
        counts.Add(secondType, secondCount);
        return counts;
    }

    private static Dictionary<ResourceType, int> Counts(ResourceType firstType, int firstCount, ResourceType secondType, int secondCount, ResourceType thirdType, int thirdCount)
    {
        var counts = Counts(firstType, firstCount, secondType, secondCount);
        counts.Add(thirdType, thirdCount);
        return counts;
    }

    private static int Count(IReadOnlyDictionary<ResourceType, int> counts, ResourceType type)
    {
        int count;
        return counts.TryGetValue(type, out count) ? count : 0;
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(message + " Expected: " + expected + "; actual: " + actual + ".");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        throw new Exception("Expected " + typeof(TException).Name + ".");
    }
}
