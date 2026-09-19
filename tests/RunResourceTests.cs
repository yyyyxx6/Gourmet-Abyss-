using System;
using System.Collections.Generic;

public static class RunResourceTests
{
    public static void Run()
    {
        Check((int)ResourceType.LootPumkin == 20 && (int)ResourceType.Loot_RatMeat == 24 &&
            (int)ResourceType.Loot_Paste == 25, "Existing resource IDs must not move.");
        Check(ResourceStorageRules.IsGathered(ResourceType.LootPumkin), "The live resource ID 20 is wood.");
        Check(!ResourceStorageRules.UsesSlots(ResourceType.LootPumkin), "Wood must not consume food slots.");
        Check(ResourceStorageRules.IsIngredient(ResourceType.LootMushroom) &&
            ResourceStorageRules.IsIngredient(ResourceType.Loot_RatMeat) &&
            ResourceStorageRules.IsIngredient(ResourceType.Loot_Paste), "Active recipe ingredients must use food slots.");
        Check(ResourceStorageRules.IsIngredient(ResourceType.LootEggBig) &&
            ResourceStorageRules.IsIngredient(ResourceType.LootFish), "Configured monster ingredients must also use slots.");
        Check(ResourceStorageRules.GetCategory(ResourceType.Money) == RunResourceCategory.Permanent,
            "Money must keep its wallet route.");
        Check(ResourceStorageRules.GetCategory(ResourceType.Furniture_Chair) == RunResourceCategory.Permanent,
            "Furniture must keep its permanent route.");
        Check(ResourceStorageRules.GetCategory(ResourceType.None) == RunResourceCategory.Invalid &&
            ResourceStorageRules.GetCategory((ResourceType)999) == RunResourceCategory.Invalid,
            "Empty and unknown resource IDs must be rejected.");

        RunIngredientStore store = new RunIngredientStore();
        Check(!store.Add(ResourceType.None, 1) && !store.Add(ResourceType.LootPumkin, -1), "Invalid additions must fail.");
        store.Add(ResourceType.LootPumkin, 8);
        var snapshot = store.GetSnapshot();
        snapshot[ResourceType.LootPumkin] = 99;
        Check(store.GetCount(ResourceType.LootPumkin) == 8, "Store snapshot must be detached.");
        Check(store.Remove(ResourceType.LootPumkin, 3) == 3, "A partial commit removes only what arrived.");
        Check(store.GetCount(ResourceType.LootPumkin) == 5, "Uncommitted materials must remain.");
        Check(store.Remove(ResourceType.LootPumkin, 20) == 5 && store.Count == 0, "Removal cannot overdraw.");
        Check(store.Remove(ResourceType.LootPumkin, 20) == 0, "Repeated commit must not remove twice.");
        store.Add(ResourceType.LootPumkin, int.MaxValue - 1);
        store.Add(ResourceType.LootPumkin, 10);
        Check(store.GetCount(ResourceType.LootPumkin) == int.MaxValue, "Gathered counts must not overflow.");
        Check(!store.Add(ResourceType.LootPumkin, 1), "Saturated counters must report no addition.");
        store.Clear();
        Check(store.Count == 0, "A cleared run has no material types.");

        TestSilentReplacementNotifications();
        TestInvalidReplacementPreservesCounts();
    }

    private static void TestSilentReplacementNotifications()
    {
        var store = new RunIngredientStore();
        store.Add(ResourceType.LootPumkin, 8);
        store.Add(ResourceType.LootOnion, 2);
        store.Add(ResourceType.LootPear, 3);
        var expectedError = new InvalidOperationException("A broken resource display.");
        var observed = new Dictionary<ResourceType, string>();
        int firstCalls = 0;
        int secondCalls = 0;
        store.Changed += (type, before, after) =>
        {
            firstCalls++;
            AssertCompleteReplacement(store);
            if (type == ResourceType.LootOnion) throw expectedError;
        };
        store.Changed += (type, before, after) =>
        {
            secondCalls++;
            AssertCompleteReplacement(store);
            observed.Add(type, before + ":" + after);
        };

        var replacement = new Dictionary<ResourceType, int>
        {
            { ResourceType.LootPumkin, 4 },
            { ResourceType.LootPear, 3 },
            { ResourceType.LootRadish, 7 }
        };
        Dictionary<ResourceType, int> previous = store.SilentReplace(replacement);
        Check(firstCalls == 0 && secondCalls == 0, "Silent replacement cannot notify before the transaction consumes its crate.");
        Check(previous[ResourceType.LootPumkin] == 8 && previous[ResourceType.LootOnion] == 2,
            "Silent replacement returns the prior counts for later notification.");
        replacement[ResourceType.LootPumkin] = 99;
        AssertCompleteReplacement(store);

        List<Exception> errors = store.Notify(previous);
        Check(errors.Count == 1 && ReferenceEquals(errors[0], expectedError),
            "Observer failures must be visible to the caller without escaping notification.");
        Check(firstCalls == 3 && secondCalls == 3,
            "A failed observer must not prevent other observers or later resource changes from being notified.");
        Check(observed.Count == 3 && observed[ResourceType.LootOnion] == "2:0" &&
            observed[ResourceType.LootPumkin] == "8:4" && observed[ResourceType.LootRadish] == "0:7",
            "Notifications describe removals, changes and additions, excluding unchanged resources.");
        Check(!observed.ContainsKey(ResourceType.LootPear), "Unchanged resource counts must not generate events.");
        Check(store.Notify(store.GetSnapshot()).Count == 0 && firstCalls == 3 && secondCalls == 3,
            "Notifying an identical snapshot must not invoke observers.");
        previous[ResourceType.LootPumkin] = 1000;
        AssertCompleteReplacement(store);
    }

    private static void AssertCompleteReplacement(RunIngredientStore store)
    {
        Check(store.Count == 3 && store.GetCount(ResourceType.LootPumkin) == 4 &&
            store.GetCount(ResourceType.LootOnion) == 0 && store.GetCount(ResourceType.LootPear) == 3 &&
            store.GetCount(ResourceType.LootRadish) == 7,
            "Every observer must see all new resource counts already in place.");
    }

    private static void TestInvalidReplacementPreservesCounts()
    {
        var store = new RunIngredientStore();
        store.Add(ResourceType.LootPumkin, 8);
        bool rejected = false;
        try
        {
            store.SilentReplace(new Dictionary<ResourceType, int>
            {
                { ResourceType.LootRadish, 7 },
                { ResourceType.LootOnion, -1 }
            });
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        Check(rejected && store.Count == 1 && store.GetCount(ResourceType.LootPumkin) == 8,
            "An invalid replacement must preserve the complete previous store.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
