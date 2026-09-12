using System;
using System.Collections.Generic;

public static class DeathDropCalculatorTests
{
    private const ResourceType FoodA = ResourceType.LootMushroom;
    private const ResourceType FoodB = ResourceType.LootFish;
    private const ResourceType Wood = ResourceType.LootPumkin;
    private const ResourceType GatheredB = ResourceType.LootPear;
    private const ResourceType Legacy = ResourceType.watermelonJ;

    public static void Run()
    {
        RoundsTenPercentPerResourceType();
        SupportsZeroFullAndQuarterRetention();
        AggregatesStacksBeforeRounding();
        KeepsOriginalSlotsAndNonIngredients();
        ProtectsInputAndResultCollections();
        ConservesVariedResourceAmounts();
        HandlesMaximumIntegerCounts();
        RejectsInvalidInput();
    }

    private static void RoundsTenPercentPerResourceType()
    {
        int[] totals = { 0, 1, 9, 10, 11 };
        int[] expected = { 0, 1, 1, 1, 2 };
        for (int index = 0; index < totals.Length; index++)
        {
            int total = totals[index];
            InventorySnapshot inventory = Snapshot(Slot(0, total == 0 ? ResourceType.None : FoodA, total, Math.Max(1, total)));
            DeathDropResult result = DeathDropCalculator.Calculate(inventory, Counts(Wood, total), 0.1m);
            Equal(expected[index], result.RetainedInventory.GetItemCount(FoodA), "Food retention rounds upward once per type.");
            Equal(total - expected[index], Count(result.DroppedIngredients, FoodA), "All unretained food is dropped.");
            Equal(expected[index], Count(result.RetainedGathered, Wood), "Gathered retention uses the same rounding rule.");
            Equal(total - expected[index], Count(result.DroppedGathered, Wood), "All unretained gathered resources are dropped.");
        }

        DeathDropResult empty = DeathDropCalculator.Calculate(Snapshot(), new Dictionary<ResourceType, int>(), 0.1m);
        Equal(0, empty.RetainedInventory.Slots.Count, "An inventory with no unlocked slots stays empty.");
        Equal(0, empty.DroppedIngredients.Count, "Empty inventory creates no ingredient entry.");
        Equal(0, empty.RetainedGathered.Count, "Empty gathered resources create no retained entry.");
        Equal(0, empty.DroppedGathered.Count, "Empty gathered resources create no dropped entry.");
    }

    private static void SupportsZeroFullAndQuarterRetention()
    {
        InventorySnapshot inventory = Snapshot(Slot(0, FoodA, 10, 12), Slot(1, FoodB, 1, 4));
        var gathered = Counts(Wood, 11);
        gathered.Add(GatheredB, 1);
        DeathDropResult zero = DeathDropCalculator.Calculate(inventory, gathered, 0m);
        Equal(0, zero.RetainedInventory.GetCounts().Count, "A zero rate retains no food.");
        Equal(10, Count(zero.DroppedIngredients, FoodA), "A zero rate drops the whole food stack.");
        Equal(0, zero.RetainedGathered.Count, "A zero rate retains no gathered resources.");
        Equal(11, Count(zero.DroppedGathered, Wood), "A zero rate drops all wood.");

        DeathDropResult full = DeathDropCalculator.Calculate(inventory, gathered, 1m);
        Equal(10, full.RetainedInventory.GetItemCount(FoodA), "A full rate retains every food unit.");
        Equal(1, full.RetainedInventory.GetItemCount(FoodB), "Each food type is independent.");
        Equal(0, full.DroppedIngredients.Count, "Full retention drops no food.");
        Equal(11, Count(full.RetainedGathered, Wood), "Full retention keeps all wood.");
        Equal(0, full.DroppedGathered.Count, "Full retention drops no gathered resources.");

        DeathDropResult quarter = DeathDropCalculator.Calculate(inventory, gathered, 0.25m);
        Equal(3, quarter.RetainedInventory.GetItemCount(FoodA), "Ten food units retain three at one quarter.");
        Equal(1, quarter.RetainedInventory.GetItemCount(FoodB), "A nonempty second type gets its own rounding.");
        Equal(3, Count(quarter.RetainedGathered, Wood), "Eleven wood units retain three at one quarter.");
        Equal(1, Count(quarter.RetainedGathered, GatheredB), "Gathered types also round independently.");
    }

    private static void AggregatesStacksBeforeRounding()
    {
        InventorySnapshot split = Snapshot(Slot(8, FoodA, 1, 4), Slot(2, FoodA, 1, 4),
            Slot(5, FoodA, 1, 4), Slot(1, FoodA, 1, 4));
        InventorySnapshot combined = Snapshot(Slot(0, FoodA, 4, 4));
        DeathDropResult result = DeathDropCalculator.Calculate(split, Counts(Wood, 10), 0.1m);
        DeathDropResult comparison = DeathDropCalculator.Calculate(combined, Counts(Wood, 10), 0.1m);
        Equal(1, result.RetainedInventory.GetItemCount(FoodA), "Four separate stacks do not retain four units.");
        Equal(comparison.RetainedInventory.GetItemCount(FoodA), result.RetainedInventory.GetItemCount(FoodA),
            "Splitting stacks cannot improve retention.");
        Equal(3, Count(result.DroppedIngredients, FoodA), "Every remaining food unit belongs to the drop.");
        Equal(1, result.RetainedInventory.Slots[3].Count, "The lowest original slot index keeps the unit.");
        Equal(0, result.RetainedInventory.Slots[0].Count, "List order does not override original slot indices.");
        Equal(8, result.RetainedInventory.Slots[0].Index, "The returned snapshot keeps its original list order.");
        Equal(1, Count(result.RetainedGathered, Wood), "Exact ten-percent decimal multiplication retains one of ten.");
    }

    private static void KeepsOriginalSlotsAndNonIngredients()
    {
        InventorySnapshot inventory = Snapshot(Slot(4, FoodA, 4, 4), Slot(0, FoodA, 1, 4),
            Slot(1, Legacy, 3, 8), Slot(2, ResourceType.None, 0, 0), Slot(3, ResourceType.Money, 2, 6));
        DeathDropResult result = DeathDropCalculator.Calculate(inventory, Counts(Wood, 0), 0.8m);
        Equal(3, result.RetainedInventory.Slots[0].Count, "Later food slots keep only the remaining quota.");
        Equal(1, result.RetainedInventory.Slots[1].Count, "Surviving food is not moved into an earlier stack.");
        Equal(3, result.RetainedInventory.GetItemCount(Legacy), "Legacy slot resources are not subject to food loss.");
        Equal(2, result.RetainedInventory.GetItemCount(ResourceType.Money), "Other non-ingredient slots are also preserved.");
        True(result.RetainedInventory.Slots[3].IsEmpty && result.RetainedInventory.Slots[3].Capacity == 0,
            "Existing empty zero-capacity slots remain unchanged.");
        Equal(0, Count(result.DroppedIngredients, Legacy), "Legacy resources never enter the ingredient drop dictionary.");
        Equal(0, result.RetainedGathered.Count, "Zero gathered amounts are omitted.");
        Equal(0, result.DroppedGathered.Count, "Zero gathered amounts cannot create a drop.");

        DeathDropResult full = DeathDropCalculator.Calculate(inventory, new Dictionary<ResourceType, int>(), 1m);
        for (int index = 0; index < inventory.Slots.Count; index++)
        {
            InventorySlotSnapshot before = inventory.Slots[index];
            InventorySlotSnapshot after = full.RetainedInventory.Slots[index];
            Equal(before.Count, after.Count, "Full retention leaves each original stack count unchanged.");
            Equal(before.ItemType, after.ItemType, "Full retention leaves every slot's resource type unchanged.");
            Equal(before.Index, after.Index, "Slot indices are preserved.");
            Equal(before.Capacity, after.Capacity, "Slot capacities are preserved.");
        }
    }

    private static void ProtectsInputAndResultCollections()
    {
        InventorySnapshot inventory = Snapshot(Slot(0, FoodA, 10, 12), Slot(1, ResourceType.None, 0, 4));
        var gathered = Counts(Wood, 11);
        DeathDropResult result = DeathDropCalculator.Calculate(inventory, gathered, 0.1m);
        Equal(10, inventory.GetItemCount(FoodA), "Calculating death loss never mutates the input inventory.");
        Equal(11, gathered[Wood], "Calculating death loss never mutates gathered input.");
        gathered[Wood] = 900;
        Equal(2, Count(result.RetainedGathered, Wood), "Retained gathered values are detached from the input dictionary.");
        Equal(9, Count(result.DroppedGathered, Wood), "Dropped gathered values are detached from the input dictionary.");
        result.RetainedInventory.GetCounts()[FoodA] = 100;
        Equal(1, result.RetainedInventory.GetItemCount(FoodA), "Returned inventory count copies cannot mutate retention.");
        Throws<NotSupportedException>(() => ((IDictionary<ResourceType, int>)result.DroppedIngredients)[FoodA] = 50);
        Throws<NotSupportedException>(() => ((IDictionary<ResourceType, int>)result.RetainedGathered)[Wood] = 50);
        Throws<NotSupportedException>(() => ((IDictionary<ResourceType, int>)result.DroppedGathered)[Wood] = 50);
        Throws<NotSupportedException>(() => ((IList<InventorySlotSnapshot>)result.RetainedInventory.Slots).Clear());
    }

    private static void ConservesVariedResourceAmounts()
    {
        var random = new Random(90132);
        ResourceType[] types = { FoodA, FoodB, Legacy };
        int[] ratePercents = { 0, 10, 25, 50, 100 };
        for (int iteration = 0; iteration < 128; iteration++)
        {
            var slots = new List<InventorySlotSnapshot>();
            int slotCount = random.Next(0, 9);
            for (int index = slotCount - 1; index >= 0; index--)
            {
                int capacity = random.Next(0, 10);
                int count = random.Next(0, capacity + 1);
                ResourceType type = count == 0 ? ResourceType.None : types[random.Next(types.Length)];
                slots.Add(Slot(index, type, count, capacity));
            }
            InventorySnapshot inventory = new InventorySnapshot(slots);
            var gathered = Counts(Wood, random.Next(0, 50));
            gathered.Add(GatheredB, random.Next(0, 50));
            int percent = ratePercents[iteration % ratePercents.Length];
            DeathDropResult result = DeathDropCalculator.Calculate(inventory, gathered, percent / 100m);

            foreach (ResourceType type in types)
            {
                int original = inventory.GetItemCount(type);
                int kept = result.RetainedInventory.GetItemCount(type);
                Equal(original, kept + Count(result.DroppedIngredients, type), "Food and legacy quantities are conserved.");
                int expected = type == Legacy ? original : (int)(((long)original * percent + 99) / 100);
                Equal(expected, kept, "Retained counts match independent integer percentage arithmetic.");
            }
            foreach (KeyValuePair<ResourceType, int> entry in gathered)
            {
                Equal(entry.Value, Count(result.RetainedGathered, entry.Key) + Count(result.DroppedGathered, entry.Key),
                    "Gathered quantities are conserved.");
                Equal((int)(((long)entry.Value * percent + 99) / 100), Count(result.RetainedGathered, entry.Key),
                    "Gathered retention matches independent integer percentage arithmetic.");
            }
            Equal(inventory.Slots.Count, result.RetainedInventory.Slots.Count, "Death allocation preserves the number of unlocked slots.");
            for (int index = 0; index < slots.Count; index++)
            {
                InventorySlotSnapshot before = inventory.Slots[index];
                InventorySlotSnapshot after = result.RetainedInventory.Slots[index];
                Equal(before.Index, after.Index, "Allocation preserves original slot order.");
                Equal(before.Capacity, after.Capacity, "Allocation never changes capacity.");
                True(after.Count >= 0 && after.Count <= before.Count, "No surviving slot can gain items.");
                if (!after.IsEmpty) Equal(before.ItemType, after.ItemType, "Allocation cannot replace an occupied slot's resource type.");
            }
        }
    }

    private static void HandlesMaximumIntegerCounts()
    {
        InventorySnapshot inventory = Snapshot(Slot(0, FoodA, int.MaxValue - 1, int.MaxValue),
            Slot(1, FoodA, 1, 4), Slot(2, FoodB, int.MaxValue, int.MaxValue));
        var gathered = Counts(Wood, int.MaxValue);
        gathered.Add(GatheredB, int.MaxValue);
        DeathDropResult tenth = DeathDropCalculator.Calculate(inventory, gathered, 0.1m);
        Equal(214748365, tenth.RetainedInventory.GetItemCount(FoodA), "Maximum food totals retain the exact decimal ceiling.");
        Equal(1932735282, Count(tenth.DroppedIngredients, FoodA), "Maximum food remainders do not overflow.");
        Equal(214748365, Count(tenth.RetainedGathered, Wood), "Maximum gathered counts retain the exact decimal ceiling.");
        Equal(1932735282, Count(tenth.DroppedGathered, Wood), "Maximum gathered remainders do not overflow.");
        Equal(214748365, tenth.RetainedInventory.GetItemCount(FoodB), "Separate resource totals are not combined into an overflowing grand total.");
        DeathDropResult quarter = DeathDropCalculator.Calculate(inventory, gathered, 0.25m);
        Equal(536870912, quarter.RetainedInventory.GetItemCount(FoodA), "Quarter retention stays exact at int.MaxValue.");
        DeathDropResult full = DeathDropCalculator.Calculate(inventory, gathered, 1m);
        Equal(int.MaxValue, full.RetainedInventory.GetItemCount(FoodA), "Full retention safely preserves the maximum total.");
        Equal(int.MaxValue, Count(full.RetainedGathered, Wood), "Full gathered retention safely preserves int.MaxValue.");
        DeathDropResult tiny = DeathDropCalculator.Calculate(inventory, gathered, 0.0000000000000000000000000001m);
        Equal(1, tiny.RetainedInventory.GetItemCount(FoodA), "A positive decimal fraction retains one unit instead of rounding down to zero.");
        Equal(1, Count(tiny.RetainedGathered, Wood), "Tiny positive gathered retention also rounds upward.");
    }

    private static void RejectsInvalidInput()
    {
        InventorySnapshot inventory = Snapshot(Slot(0, FoodA, 1, 4));
        var gathered = Counts(Wood, 1);
        Throws<ArgumentNullException>(() => DeathDropCalculator.Calculate(null, gathered, 0.1m));
        Throws<ArgumentNullException>(() => DeathDropCalculator.Calculate(inventory, null, 0.1m));
        Throws<ArgumentOutOfRangeException>(() => DeathDropCalculator.Calculate(inventory, gathered, -0.01m));
        Throws<ArgumentOutOfRangeException>(() => DeathDropCalculator.Calculate(inventory, gathered, 1.01m));
        Throws<ArgumentOutOfRangeException>(() => DeathDropCalculator.Calculate(inventory, gathered, decimal.MaxValue));
        Throws<ArgumentOutOfRangeException>(() => DeathDropCalculator.Calculate(inventory, Counts(Wood, -1), 0.1m));
        Throws<ArgumentException>(() => DeathDropCalculator.Calculate(inventory, Counts(ResourceType.None, 1), 0.1m));
        Throws<ArgumentException>(() => DeathDropCalculator.Calculate(inventory, Counts(ResourceType.None, 0), 0.1m));
        Throws<ArgumentException>(() => DeathDropCalculator.Calculate(inventory, Counts(FoodA, 1), 0.1m));
        Throws<ArgumentException>(() => DeathDropCalculator.Calculate(inventory, Counts(Legacy, 1), 0.1m));
        Throws<ArgumentException>(() => DeathDropCalculator.Calculate(inventory, Counts(ResourceType.Money, 1), 0.1m));
        Throws<ArgumentException>(() => DeathDropCalculator.Calculate(inventory, Counts((ResourceType)999, 1), 0.1m));
        Equal(1, inventory.GetItemCount(FoodA), "Rejected calculations leave the original inventory intact.");
        Equal(1, gathered[Wood], "Rejected calculations leave original gathered counts intact.");
    }

    private static InventorySlotSnapshot Slot(int index, ResourceType type, int count, int capacity)
    {
        return new InventorySlotSnapshot(index, type, count, capacity);
    }

    private static InventorySnapshot Snapshot(params InventorySlotSnapshot[] slots)
    {
        return new InventorySnapshot(slots);
    }

    private static Dictionary<ResourceType, int> Counts(ResourceType type, int count)
    {
        return new Dictionary<ResourceType, int> { { type, count } };
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
