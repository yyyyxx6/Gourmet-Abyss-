using System;
using System.Collections.Generic;

public static class RunSessionDataTests
{
    public static void Run()
    {
        RecordsPositiveAndNegativeNetChanges();
        UsesLongForTotalIngredientDelta();
        DeduplicatesNewUnlocksAgainstTheBaseline();
        CompletesOnceAndIgnoresLateEvents();
        IsolatesInputAndResultSnapshots();
        StartsTheNextRunWithAFreshBaseline();
        IgnoresInvalidTimeAndAcceptedAmounts();
        ValidatesLifecycleAndCounts();
    }

    private static void RecordsPositiveAndNegativeNetChanges()
    {
        RunSessionData run = new RunSessionData();
        run.Begin("Layer1", Counts(ResourceType.LootMushroom, 8));
        run.RecordGathered(ResourceType.Money, 5);
        run.RecordGathered(ResourceType.Money, 2);
        run.RecordKill();
        run.AdvanceTime(12.5);
        RunResultSnapshot result = run.Complete(Bag(ResourceType.LootMushroom, 3), Counts(ResourceType.Money, 1));
        Equal(-5L, result.IngredientDelta, "Lost carried ingredients must produce a negative delta.");
        Equal(7, result.GatheredCounts[ResourceType.Money], "Collected count must use accepted amounts.");
        Equal(1, result.RetainedGatheredCounts[ResourceType.Money], "Retained amount must remain separate from collected amount.");
        Equal(1, result.KillCount, "Kill count.");
        Equal(12.5, result.ElapsedSeconds, "Elapsed time.");
        Equal("Layer1", result.LevelId, "Result level.");

        run.Begin("Layer1", Counts(ResourceType.LootMushroom, 3));
        Equal(4L, run.Complete(Bag(ResourceType.LootMushroom, 7), EmptyCounts()).IngredientDelta,
            "Net gain must subtract the carried baseline.");
    }

    private static void UsesLongForTotalIngredientDelta()
    {
        InventorySnapshot full = new InventorySnapshot(new[]
        {
            new InventorySlotSnapshot(0, ResourceType.LootMushroom, int.MaxValue, int.MaxValue),
            new InventorySlotSnapshot(1, ResourceType.LootEggSmall, int.MaxValue, int.MaxValue)
        });
        RunSessionData run = new RunSessionData();
        run.Begin("Layer1", EmptyCounts());
        Equal(2L * int.MaxValue, run.Complete(full, EmptyCounts()).IngredientDelta,
            "A total across types must not overflow int.");
    }

    private static void DeduplicatesNewUnlocksAgainstTheBaseline()
    {
        RunSessionData run = new RunSessionData();
        run.Begin("Layer1", EmptyCounts(), new[] { 1, 1 }, new[] { PetType.FlyingCompanion });
        run.RecordRecipeUnlocked(1);
        run.RecordRecipeUnlocked(3);
        run.RecordRecipeUnlocked(3);
        run.RecordRecipeUnlocked(2);
        run.RecordRecipeUnlocked(-1);
        run.RecordPetUnlocked(PetType.FlyingCompanion);
        run.RecordPetUnlocked(PetType.None);
        RunResultSnapshot result = run.Complete(EmptyBag(), EmptyCounts());
        Equal(2, result.NewRecipeIds.Count, "Only new, unique recipes are reported.");
        Equal(2, result.NewRecipeIds[0], "Recipe result order must be deterministic.");
        Equal(3, result.NewRecipeIds[1], "Recipe result order must be deterministic.");
        Equal(0, result.NewPetTypes.Count, "An existing pet must not be marked new.");

        run.Begin("Layer1", EmptyCounts());
        run.RecordPetUnlocked(PetType.FlyingCompanion);
        run.RecordPetUnlocked(PetType.FlyingCompanion);
        Equal(1, run.Complete(EmptyBag(), EmptyCounts()).NewPetTypes.Count, "A new pet is recorded once.");
    }

    private static void CompletesOnceAndIgnoresLateEvents()
    {
        RunSessionData run = new RunSessionData();
        run.Begin("Layer1", EmptyCounts());
        run.RecordGathered(ResourceType.Money, 2);
        run.RecordRecipeUnlocked(1);
        run.RecordPetUnlocked(PetType.FlyingCompanion);
        run.AdvanceTime(1);
        run.RecordKill();
        RunResultSnapshot result = run.Complete(EmptyBag(), Counts(ResourceType.Money, 2));
        run.AdvanceTime(20);
        run.RecordKill();
        run.RecordGathered(ResourceType.Money, 8);
        run.RecordRecipeUnlocked(2);
        run.RecordPetUnlocked((PetType)2);
        True(!run.IsActive, "Complete must stop the run.");
        True(ReferenceEquals(result, run.Complete(null, null)), "Repeated completion must return the original result.");
        Equal(1.0, run.ElapsedSeconds, "Late time events must be ignored.");
        Equal(1, run.KillCount, "Late kill events must be ignored.");
        Equal(2, result.GatheredCounts[ResourceType.Money], "Late gathered events must be ignored.");
        Equal(1, result.NewRecipeIds.Count, "Late recipe events must be ignored.");
        Equal(1, result.NewPetTypes.Count, "Late pet events must be ignored.");
    }

    private static void IsolatesInputAndResultSnapshots()
    {
        Dictionary<ResourceType, int> initial = Counts(ResourceType.LootMushroom, 3);
        Dictionary<ResourceType, int> retained = Counts(ResourceType.Money, 4);
        List<int> knownRecipes = new List<int> { 10 };
        List<PetType> knownPets = new List<PetType> { PetType.FlyingCompanion };
        RunSessionData run = new RunSessionData();
        run.Begin("Layer1", initial, knownRecipes, knownPets);
        initial[ResourceType.LootMushroom] = 99;
        knownRecipes.Clear();
        knownPets.Clear();
        run.RecordRecipeUnlocked(10);
        run.RecordPetUnlocked(PetType.FlyingCompanion);
        run.RecordGathered(ResourceType.Money, 4);
        RunResultSnapshot preview = run.Capture(Bag(ResourceType.LootMushroom, 5), retained);
        retained[ResourceType.Money] = 99;
        run.RecordGathered(ResourceType.Money, 2);
        run.RecordRecipeUnlocked(11);
        Equal(2L, preview.IngredientDelta, "Begin must copy the ingredient baseline.");
        Equal(0, preview.NewRecipeIds.Count, "Begin must copy known recipes.");
        Equal(0, preview.NewPetTypes.Count, "Begin must copy known pets.");
        Equal(4, preview.RetainedGatheredCounts[ResourceType.Money], "Capture must copy retained counts.");
        Equal(4, preview.GatheredCounts[ResourceType.Money], "Capture must isolate cumulative counts.");
        True(run.IsActive, "Preview must leave the run active.");
        Throws<NotSupportedException>(() => ((IDictionary<ResourceType, int>)preview.GatheredCounts).Add(ResourceType.mushroom, 1));
        Throws<NotSupportedException>(() => ((IDictionary<ResourceType, int>)preview.RetainedGatheredCounts)[ResourceType.Money] = 1);
        Throws<NotSupportedException>(() => ((IList<int>)preview.NewRecipeIds).Add(20));
        Throws<NotSupportedException>(() => ((IList<PetType>)preview.NewPetTypes).Add(PetType.FlyingCompanion));
        RunResultSnapshot result = run.Complete(Bag(ResourceType.LootMushroom, 5), Counts(ResourceType.Money, 6));
        run.Begin("Layer2", EmptyCounts());
        Equal(6, result.GatheredCounts[ResourceType.Money], "A new run must not clear a completed result.");
        Equal(1, result.NewRecipeIds.Count, "A new run must not clear completed unlocks.");
        Equal(5, result.Inventory.GetItemCount(ResourceType.LootMushroom), "Inventory snapshot must remain accessible.");
    }

    private static void StartsTheNextRunWithAFreshBaseline()
    {
        RunSessionData run = new RunSessionData();
        run.Begin("Layer1", Counts(ResourceType.LootMushroom, 2));
        run.RecordGathered(ResourceType.Money, 4);
        run.RecordRecipeUnlocked(8);
        run.RecordPetUnlocked(PetType.FlyingCompanion);
        run.RecordKill();
        run.AdvanceTime(8);
        RunResultSnapshot first = run.Complete(Bag(ResourceType.LootMushroom, 5), EmptyCounts());
        run.Begin("Layer2", Counts(ResourceType.LootMushroom, 5), new[] { 8 }, new[] { PetType.FlyingCompanion });
        Equal("Layer2", run.LevelId, "Begin must replace the level.");
        Equal(0, run.KillCount, "Begin must reset kills.");
        Equal(0.0, run.ElapsedSeconds, "Begin must reset the clock.");
        run.RecordRecipeUnlocked(8);
        run.RecordPetUnlocked(PetType.FlyingCompanion);
        RunResultSnapshot second = run.Complete(Bag(ResourceType.LootMushroom, 5), EmptyCounts());
        True(!ReferenceEquals(first, second), "Each run needs its own completed result.");
        Equal(0L, second.IngredientDelta, "The next run must use its own carried baseline.");
        Equal(0, second.GatheredCounts.Count, "Begin must reset gathered counts.");
        Equal(0, second.NewRecipeIds.Count, "Begin must reset new recipes.");
        Equal(0, second.NewPetTypes.Count, "Begin must reset new pets.");
    }

    private static void IgnoresInvalidTimeAndAcceptedAmounts()
    {
        RunSessionData run = new RunSessionData();
        run.AdvanceTime(5);
        run.RecordKill();
        run.RecordGathered(ResourceType.Money, 1);
        run.Begin("Layer1", EmptyCounts());
        run.AdvanceTime(0);
        run.AdvanceTime(-1);
        run.AdvanceTime(double.NaN);
        run.AdvanceTime(double.PositiveInfinity);
        run.AdvanceTime(double.NegativeInfinity);
        run.AdvanceTime(0.25);
        run.RecordGathered(ResourceType.Money, 0);
        run.RecordGathered(ResourceType.Money, -1);
        run.RecordGathered(ResourceType.None, 1);
        RunResultSnapshot preview = run.Capture(EmptyBag(), EmptyCounts());
        Equal(0.25, preview.ElapsedSeconds, "Only finite positive active time is counted.");
        Equal(0, preview.GatheredCounts.Count, "Only positive accepted resources are counted.");
        Equal(0, preview.KillCount, "Pre-run events must be ignored.");
        run.AdvanceTime(double.MaxValue);
        run.AdvanceTime(double.MaxValue);
        run.RecordGathered(ResourceType.Money, int.MaxValue);
        run.RecordGathered(ResourceType.Money, 1);
        RunResultSnapshot result = run.Complete(EmptyBag(), EmptyCounts());
        Equal(double.MaxValue, result.ElapsedSeconds, "Time overflow must stay finite.");
        Equal(int.MaxValue, result.GatheredCounts[ResourceType.Money], "Collected counts must not wrap on overflow.");
    }

    private static void ValidatesLifecycleAndCounts()
    {
        RunSessionData run = new RunSessionData();
        Throws<InvalidOperationException>(() => run.Complete(EmptyBag(), EmptyCounts()));
        Throws<ArgumentException>(() => run.Begin(" ", EmptyCounts()));
        Throws<ArgumentNullException>(() => run.Begin("Layer1", null));
        Throws<ArgumentOutOfRangeException>(() => run.Begin("Layer1", Counts(ResourceType.LootMushroom, -1)));
        run.Begin("Layer1", EmptyCounts());
        Throws<ArgumentNullException>(() => run.Complete(null, EmptyCounts()));
        Throws<ArgumentOutOfRangeException>(() => run.Complete(EmptyBag(), Counts(ResourceType.Money, -1)));
        True(run.IsActive, "A failed completion must leave the run active.");
        run.Complete(EmptyBag(), EmptyCounts());
        Throws<InvalidOperationException>(() => run.Capture(EmptyBag(), EmptyCounts()));
    }

    private static Dictionary<ResourceType, int> EmptyCounts()
    {
        return new Dictionary<ResourceType, int>();
    }

    private static Dictionary<ResourceType, int> Counts(ResourceType type, int count)
    {
        return new Dictionary<ResourceType, int> { { type, count } };
    }

    private static InventorySnapshot EmptyBag()
    {
        return new InventorySnapshot(new InventorySlotSnapshot[0]);
    }

    private static InventorySnapshot Bag(ResourceType type, int count)
    {
        return new InventorySnapshot(new[] { new InventorySlotSnapshot(0, type, count, Math.Max(1, count)) });
    }

    private static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception(message + " Expected " + expected + ", got " + actual + ".");
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }
        throw new Exception("Expected " + typeof(T).Name + ".");
    }
}
