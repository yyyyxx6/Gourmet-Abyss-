#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Copy this, Batch2UnityValidation (shared scene assertions), and the pure tests into an isolated project's Assets/Editor.
[InitializeOnLoad]
public static class Batch3UnityValidation
{
    private const string Prefix = "ChefDungeon.Batch3.";
    private const string PhaseKey = Prefix + "Phase";
    private const string ResultKey = Prefix + "Result";
    private const string DeadlineKey = Prefix + "Deadline";
    private const string ReadyKey = Prefix + "ReadyAt";
    private const string UnexpectedErrorKey = Prefix + "UnexpectedError";
    private const string ExpectedGatheredListenerFailure = "BATCH3_EXPECTED_GATHERED_LISTENER_FAILURE";
    private const string Level = "Layer1";
    private const string OtherLevel = "Layer2";
    private const ResourceType Food = ResourceType.LootMushroom;
    private const ResourceType Wood = ResourceType.LootPumkin;
    private const ResourceType Egg = ResourceType.LootEggSmall;
    private const ResourceType Paste = ResourceType.Loot_Paste;
    private const ResourceType Meat = ResourceType.Loot_RatMeat;
    private static int woodBaseline;
    private static int expectedWoodGain;
    private static int deathNumber;
    private static Vector3 originalSpawn;
    private static DeathLootRecord expectedCrate;
    private static DeathLootCrate collectedCrate;
    private static string collectedId;

    static Batch3UnityValidation()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Application.logMessageReceived += OnRuntimeLog;
    }

    public static void Run()
    {
        try
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Validation must start in Edit Mode.");
            SettlementPanelBuilder.Build();
            DeathLootCrateBuilder.Build();
            InventoryPackingTests.Run();
            RunSessionDataTests.Run();
            RunResourceTests.Run();
            RunEndFlowTests.Run();
            DeathDropCalculatorTests.Run();
            Debug.Log("BATCH3_PURE_TESTS_PASS");
            PlayerSettings.companyName = "CodexValidation";
            PlayerSettings.productName = "ChefDungeonBatch3";
            EditorSceneManager.OpenScene("Assets/Scenes/UpGround.unity", OpenSceneMode.Single);
            SessionState.SetInt(ResultKey, 1);
            SessionState.EraseString(UnexpectedErrorKey);
            SetPhase("enter-play", 60);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception error)
        {
            Finish(false, error);
        }
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        string phase = SessionState.GetString(PhaseKey, "");
        if (state == PlayModeStateChange.EnteredPlayMode && phase == "enter-play") SetPhase("prepare", 30, 3);
        else if (state == PlayModeStateChange.EnteredEditMode && phase == "exit") EditorApplication.delayCall += ExitEditor;
        else if (state == PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(phase))
            Finish(false, new InvalidOperationException("Play Mode ended during " + phase));
    }

    private static void Tick()
    {
        string phase = SessionState.GetString(PhaseKey, "");
        if (string.IsNullOrEmpty(phase)) return;
        if (phase == "exit")
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) ExitEditor();
            return;
        }
        try
        {
            string runtimeError = SessionState.GetString(UnexpectedErrorKey, "");
            Check(string.IsNullOrEmpty(runtimeError), "Unexpected runtime error: " + runtimeError);
            Check(EditorApplication.timeSinceStartup <= GetTime(DeadlineKey), "Timed out during " + phase + ". " + DescribeRuntime());
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < GetTime(ReadyKey)) return;
            switch (phase)
            {
                case "prepare": Prepare(); break;
                case "first-level": if (IsLevelReady(Level)) FirstDeath(); break;
                case "recover-first": if (IsLevelReady(Level)) CollectFirstCrate(); break;
                case "after-first-collection": DieAfterCollection(); break;
                case "replace-unlooted": if (IsLevelReady(Level)) DieWithoutCollection(); break;
                case "clear-with-full-retention": if (IsLevelReady(Level)) ClearOldCrateWithNoDrop(); break;
                case "create-town-record": if (IsLevelReady(Level)) CreateTownRecord(); break;
                case "town-record": if (IsHomeReady()) VerifyTownRecordAndChangeMap(); break;
                case "other-level": if (IsLevelReady(OtherLevel)) VerifyOtherMap(); break;
                case "town-after-other": if (IsHomeReady()) EnterLevel(Level, "return-original"); break;
                case "return-original": if (IsLevelReady(Level)) PreparePackingCrate(); break;
                case "packing-home": if (IsHomeReady()) PreparePackingInventory(); break;
                case "packing-collect": if (IsLevelReady(Level)) CollectIntoLimitedInventory(); break;
                case "after-packing-collection": FinishPackingCollection(); break;
                case "final-home": if (IsHomeReady()) VerifyFinalHome(); break;
            }
        }
        catch (Exception error)
        {
            Finish(false, new InvalidOperationException("Phase " + phase + ": " + DescribeRuntime(), error));
        }
    }

    private static void Prepare()
    {
        Check(InventoryManager.instance != null && RunSessionManager.Instance != null && GameValManager.Instance != null &&
            WeaponStatsManager.Instance != null && LevelManager.instance != null && ShopManager.Instance != null,
            "The real UpGround scene must initialize all inventory, run and price dependencies.");
        GameplaySceneValidation.CaptureHomeState();
        ClearInventory();
        InventoryManager.instance.ClearRunGathered();
        Check(InventoryManager.instance.GetAvailableCapacity(Food) >= 4, "The initial fixture needs four units in its existing inventory.");
        Check(RunSessionManager.Instance.PendingDeathCrate == null, "A new isolated session starts without a death record.");
        ValidateRetentionSkill();
        WeaponStatsManager.Instance.deathRetentionRate = 0.1f;
        woodBaseline = GameValManager.Instance.GetResourceCount(Wood);
        Check(woodBaseline <= int.MaxValue - 100, "The isolated permanent wood counter must leave room for test receipts.");
        GameValManager.Instance.GetResourceInfo(Wood).maxCapacity = Math.Max(
            GameValManager.Instance.GetResourceInfo(Wood).maxCapacity, woodBaseline + 100);
        expectedWoodGain = 0;
        deathNumber = 0;
        expectedCrate = null;
        collectedCrate = null;
        EnterLevel(Level, "first-level");
    }

    private static void ValidateRetentionSkill()
    {
        SkillTreeInitializer initializer = UnityEngine.Object.FindObjectOfType<SkillTreeInitializer>(true);
        Check(initializer != null, "The real scene must contain its skill-tree initializer.");
        MethodInfo apply = typeof(SkillTreeInitializer).GetMethod("ApplyStatEffect", BindingFlags.Instance | BindingFlags.NonPublic,
            null, new[] { typeof(int), typeof(float), typeof(WeaponStatsManager), typeof(int) }, null);
        Check(apply != null, "The skill test must invoke the actual stat-effect method.");
        WeaponStatsManager stats = WeaponStatsManager.Instance;
        float before = stats.deathRetentionRate;
        try
        {
            stats.deathRetentionRate = 0.1f;
            apply.Invoke(initializer, new object[] { 71, 0.05f, stats, 2 });
            Check(Mathf.Abs(stats.deathRetentionRate - 0.2f) < 0.00001f, "Stat 71 adds two five-percentage-point levels to ten percent.");
            apply.Invoke(initializer, new object[] { 71, 2f, stats, 2 });
            Check(stats.deathRetentionRate == 1f, "Stat 71 must clamp its upper boundary to one.");
            apply.Invoke(initializer, new object[] { 71, -2f, stats, 2 });
            Check(stats.deathRetentionRate == 0f, "Stat 71 must clamp its lower boundary to zero.");
        }
        finally
        {
            stats.deathRetentionRate = before;
        }
        Debug.Log("BATCH3_RETENTION_SKILL_PASS");
    }

    private static void FirstDeath()
    {
        TopDownController player = FindPlayer(Level);
        originalSpawn = player.transform.position;
        AddFood(Food, 4);
        AddWood(11);
        DieAwayFromSpawn(player);
        Check(InventoryManager.instance.GetItemCount(Food) == 1, "Four food units retain one at the default rate.");
        Check(Count(RunSessionManager.Instance.LastResult.RetainedGatheredCounts, Wood) == 2,
            "Eleven gathered wood units retain two at the default rate.");
        CheckSimpleCrate(3, 9);
        AddExpectedPermanentWood(2);
        Check(InventoryManager.instance.GetRunGatheredCounts().Count == 0, "Retained wood is committed once at settlement.");
        Check(!RunSessionManager.Instance.TryEndRun(RunEndReason.Death), "Repeated death cannot allocate a second crate.");
        expectedCrate = RunSessionManager.Instance.PendingDeathCrate;
        Debug.Log("BATCH3_DEFAULT_DEATH_ALLOCATION_PASS");
        Retry("recover-first");
    }

    private static void CollectFirstCrate()
    {
        CheckRestoredCrate();
        TopDownController player = FindPlayer(Level);
        DeathLootCrate crate = RunSessionManager.Instance.ActiveDeathCrate;
        Check(Vector3.Distance(player.transform.position, crate.transform.position) > 6f,
            "The new spawn must be outside the saved crate's pickup radius.");
        Check(!crate.TryCollect(player) && !RunSessionManager.Instance.TryCollectDeathCrate(expectedCrate.Id),
            "Both the scene interaction and backend must reject remote collection.");
        collectedId = expectedCrate.Id;
        collectedCrate = crate;
        InventoryManager inventory = InventoryManager.instance;
        bool listenerThrew = false;
        int normalNotifications = 0;
        int notifiedPrevious = -1;
        int notifiedCurrent = -1;
        Action<ResourceType, int, int> throwingListener = (type, previous, current) =>
        {
            if (type != Wood || previous == current || listenerThrew) return;
            listenerThrew = true;
            throw new InvalidOperationException(ExpectedGatheredListenerFailure);
        };
        Action<ResourceType, int, int> normalListener = (type, previous, current) =>
        {
            if (type != Wood || previous == current) return;
            normalNotifications++;
            notifiedPrevious = previous;
            notifiedCurrent = current;
        };
        inventory.OnRunGatheredChanged += throwingListener;
        inventory.OnRunGatheredChanged += normalListener;
        try
        {
            MovePlayer(player, crate.transform.position);
            Check(crate.TryCollect(player), "Approaching the actual crate must succeed even when a gathered-change listener throws.");
        }
        finally
        {
            inventory.OnRunGatheredChanged -= throwingListener;
            inventory.OnRunGatheredChanged -= normalListener;
        }
        Check(listenerThrew, "The collection fixture must execute its intentionally failing listener.");
        Check(normalNotifications == 1 && notifiedPrevious == 0 && notifiedCurrent == 9,
            "A failing listener must not block or duplicate the later listener's actual gathered-change notification.");
        Check(InventoryManager.instance.GetItemCount(Food) == 4 && InventoryManager.instance.GetRunGatheredCount(Wood) == 9,
            "Recovered resources join the new run's existing food and gathered stores.");
        DeathLootReceipt receipt = RunSessionManager.Instance.LastDeathLootReceipt;
        Check(receipt != null && Count(receipt.RecoveredIngredients, Food) == 3 &&
            Count(receipt.RecoveredGathered, Wood) == 9 && receipt.DiscardedIngredients.Count == 0,
            "The collection receipt must contain the actual recovered quantities.");
        CheckCrateCleared();
        Check(!crate.TryCollect(player) && !RunSessionManager.Instance.TryCollectDeathCrate(collectedId),
            "A consumed crate cannot be collected twice.");
        CheckPermanentWood();
        Debug.Log("BATCH3_GATHERED_LISTENER_FAILURE_ISOLATION_PASS");
        SetPhase("after-first-collection", 10, 0.1);
    }

    private static void DieAfterCollection()
    {
        Check(collectedCrate == null, "The collected scene object must be destroyed after the collection frame.");
        Check(InventoryManager.instance.GetRunGatheredCount(Wood) == 9, "Waiting after collection must not credit the crate twice.");
        AddWood(2);
        DieAwayFromSpawn(FindPlayer(Level));
        CheckSimpleCrate(3, 9);
        Check(RunSessionManager.Instance.PendingDeathCrate.Id != collectedId, "Death after collection creates a new crate identity.");
        Check(Count(RunSessionManager.Instance.LastResult.GatheredCounts, Wood) == 11,
            "Recovered nine wood plus two new wood must be recorded as this run's eleven collected units.");
        AddExpectedPermanentWood(2);
        expectedCrate = RunSessionManager.Instance.PendingDeathCrate;
        Debug.Log("BATCH3_RECOVERY_AND_REDEATH_PASS");
        Retry("replace-unlooted");
    }

    private static void DieWithoutCollection()
    {
        CheckRestoredCrate();
        string oldId = expectedCrate.Id;
        Check(!RunSessionManager.Instance.ActiveDeathCrate.TryCollect(FindPlayer(Level)), "The unlooted crate remains out of reach.");
        AddFood(Food, 3);
        AddWood(5);
        DieAwayFromSpawn(FindPlayer(Level));
        CheckSimpleCrate(3, 4);
        Check(RunSessionManager.Instance.PendingDeathCrate.Id != oldId,
            "A new death must replace an unlooted crate instead of merging the previous contents.");
        AddExpectedPermanentWood(1);
        expectedCrate = RunSessionManager.Instance.PendingDeathCrate;
        Debug.Log("BATCH3_UNLOOTED_CRATE_REPLACEMENT_PASS");
        Retry("clear-with-full-retention");
    }

    private static void ClearOldCrateWithNoDrop()
    {
        CheckRestoredCrate();
        WeaponStatsManager.Instance.deathRetentionRate = 1f;
        DieAwayFromSpawn(FindPlayer(Level));
        Check(InventoryManager.instance.GetItemCount(Food) == 1, "Full retention keeps the existing food.");
        CheckCrateCleared();
        CheckPermanentWood();
        WeaponStatsManager.Instance.deathRetentionRate = 0.1f;
        Debug.Log("BATCH3_EMPTY_DROP_REPLACES_OLD_CRATE_PASS");
        Retry("create-town-record");
    }

    private static void CreateTownRecord()
    {
        CheckCrateCleared();
        AddFood(Food, 3);
        AddWood(5);
        DieAwayFromSpawn(FindPlayer(Level));
        CheckSimpleCrate(3, 4);
        AddExpectedPermanentWood(1);
        expectedCrate = RunSessionManager.Instance.PendingDeathCrate;
        ReturnHome("town-record");
    }

    private static void VerifyTownRecordAndChangeMap()
    {
        CheckSameRecord(expectedCrate, RunSessionManager.Instance.PendingDeathCrate);
        Check(RunSessionManager.Instance.ActiveDeathCrate == null, "Returning home unloads the crate scene object while preserving its record.");
        CheckPermanentWood();
        Debug.Log("BATCH3_HOME_PRESERVES_DEATH_RECORD_PASS");

        Scene unloadedLevel = SceneManager.GetSceneByName(Level);
        Check(!unloadedLevel.IsValid() || !unloadedLevel.isLoaded, "The failure fixture must use a genuinely unloaded level.");
        RunSessionManager runs = RunSessionManager.Instance;
        RunEndPhase previousPhase = runs.Phase;
        RunResultSnapshot previousResult = runs.LastResult;
        string previousCrateId = runs.PendingDeathCrate.Id;
        Check(previousPhase == RunEndPhase.Inactive && !runs.IsActive && previousResult != null,
            "The unloaded-level failure fixture starts from the real completed home state.");
        bool beginThrew = false;
        try
        {
            runs.BeginRun(Level);
        }
        catch (InvalidOperationException)
        {
            beginThrew = true;
        }
        Check(beginThrew, "Beginning a run whose saved crate scene is unloaded must fail explicitly.");
        Check(runs.Phase == previousPhase && runs.Phase == RunEndPhase.Inactive && !runs.IsActive &&
            ReferenceEquals(previousResult, runs.LastResult) && runs.PendingDeathCrate != null &&
            runs.PendingDeathCrate.Id == previousCrateId,
            "Failed crate restoration must preserve the inactive gate, completed result and pending crate identity.");
        CheckSameRecord(expectedCrate, runs.PendingDeathCrate);
        Debug.Log("BATCH3_UNLOADED_LEVEL_BEGIN_PRESERVES_RESULT_PASS");
        EnterLevel(OtherLevel, "other-level");
    }

    private static void VerifyOtherMap()
    {
        CheckCrateCleared();
        CheckPermanentWood();
        Check(RunSessionManager.Instance.TryEndRun(RunEndReason.Extracted), "The other real level must support extraction.");
        ReturnHome("town-after-other");
    }

    private static void PreparePackingCrate()
    {
        CheckCrateCleared();
        CheckPermanentWood();
        Debug.Log("BATCH3_CHANGED_MAP_DISCARDS_OLD_RECORD_PASS");
        WeaponStatsManager.Instance.inventorySlotCount = 3;
        WeaponStatsManager.Instance.inventorySlotCapacity = 4;
        MethodInfo update = typeof(InventoryManager).GetMethod("OnInventoryStatsUpdated", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(update != null, "The packing fixture must use the real inventory stats refresh.");
        update.Invoke(InventoryManager.instance, null);
        Check(InventoryManager.instance.GetSlotCount() == 3, "The packing fixture has three real unlocked slots.");
        ClearInventory();
        AddFood(Food, 4);
        AddFood(Egg, 4);
        AddFood(Paste, 4);
        AddWood(11);
        DieAwayFromSpawn(FindPlayer(Level));
        DeathLootRecord record = RunSessionManager.Instance.PendingDeathCrate;
        Check(record != null && record.Ingredients.Count == 3 && Count(record.Ingredients, Food) == 3 &&
            Count(record.Ingredients, Egg) == 3 && Count(record.Ingredients, Paste) == 3 && Count(record.Gathered, Wood) == 9,
            "The packing fixture's death must produce the real three-type crate.");
        AddExpectedPermanentWood(2);
        expectedCrate = record;
        ReturnHome("packing-home");
    }

    private static void PreparePackingInventory()
    {
        CheckSameRecord(expectedCrate, RunSessionManager.Instance.PendingDeathCrate);
        ClearInventory();
        AddFood(Food, 3);
        AddFood(Meat, 4);
        Check(InventoryManager.instance.GetSlot(2).IsEmpty(), "The constrained collection fixture leaves one empty slot.");
        SetRuntimePrice(Food, 10);
        SetRuntimePrice(Egg, 100);
        SetRuntimePrice(Paste, 1);
        EnterLevel(Level, "packing-collect");
    }

    private static void CollectIntoLimitedInventory()
    {
        CheckRestoredCrate();
        TopDownController player = FindPlayer(Level);
        DeathLootCrate crate = RunSessionManager.Instance.ActiveDeathCrate;
        Check(!crate.TryCollect(player), "Packing collection cannot happen remotely.");
        collectedCrate = crate;
        collectedId = expectedCrate.Id;
        MovePlayer(player, crate.transform.position);
        Check(crate.TryCollect(player), "The actual scene crate must resolve against the constrained live inventory.");
        DeathLootReceipt receipt = RunSessionManager.Instance.LastDeathLootReceipt;
        Check(receipt != null && Count(receipt.RecoveredIngredients, Food) == 1 && Count(receipt.RecoveredIngredients, Egg) == 3 &&
            Count(receipt.RecoveredIngredients, Paste) == 0 && Count(receipt.DiscardedIngredients, Food) == 2 &&
            Count(receipt.DiscardedIngredients, Paste) == 3 && Count(receipt.DiscardedIngredients, Egg) == 0,
            "Collection must fill the existing mushroom stack, then place the higher-priced eggs and report all discarded food.");
        Check(Count(receipt.RecoveredGathered, Wood) == 9 && InventoryManager.instance.GetRunGatheredCount(Wood) == 9,
            "All gathered wood is recovered even when food capacity is exhausted.");
        Check(InventoryManager.instance.GetItemCount(Food) == 4 && InventoryManager.instance.GetItemCount(Meat) == 4 &&
            InventoryManager.instance.GetItemCount(Egg) == 3 && InventoryManager.instance.GetItemCount(Paste) == 0,
            "The final bag preserves existing meat and contains exactly the accepted food.");
        CheckCrateCleared();
        Check(!RunSessionManager.Instance.TryCollectDeathCrate(collectedId), "The resolved packing crate cannot credit a second receipt.");
        CheckPermanentWood();
        SetPhase("after-packing-collection", 10, 0.1);
    }

    private static void FinishPackingCollection()
    {
        Check(collectedCrate == null, "The resolved packing crate's scene object must be destroyed.");
        Check(RunSessionManager.Instance.TryEndRun(RunEndReason.Extracted), "Recovered packing materials must extract through the real settlement path.");
        Check(Count(RunSessionManager.Instance.LastResult.GatheredCounts, Wood) == 9, "The final result records the recovered wood in the new run.");
        AddExpectedPermanentWood(9);
        Debug.Log("BATCH3_FULL_BAG_PRIORITY_AND_GATHERED_RECOVERY_PASS");
        ReturnHome("final-home");
    }

    private static void VerifyFinalHome()
    {
        CheckCrateCleared();
        CheckPermanentWood();
        Check(expectedWoodGain == 17, "All retained and recovered wood receipts must total seventeen new permanent units.");
        Check(InventoryManager.instance.GetItemCount(Food) == 4 && InventoryManager.instance.GetItemCount(Meat) == 4 &&
            InventoryManager.instance.GetItemCount(Egg) == 3, "Final extraction and return home retain the packed inventory.");
        Check(Time.timeScale > 0f && PlayerStateManager.instance.currentState == PlayerState.UpGround &&
            !BattleValManager.Instance.IsActive && !RunSessionManager.Instance.IsActive,
            "The final home destination must restore time and finish the run.");
        Finish(true, null);
    }

    private static void DieAwayFromSpawn(TopDownController player)
    {
        Vector3[] directions = { Vector3.right, Vector3.forward, Vector3.left, Vector3.back };
        Vector3 destination = originalSpawn + directions[deathNumber++ % directions.Length] * 10f;
        Check(Vector3.Distance(destination, originalSpawn) > 6f, "The death fixture must remain outside the next spawn's pickup range.");
        MovePlayer(player, destination);
        player.Die();
        Check(RunSessionManager.Instance.Phase == RunEndPhase.ShowingResult && !RunSessionManager.Instance.IsActive &&
            Time.timeScale == 0f && !player.enabled && player.isDead, "Real player death must complete and pause settlement.");
        Check(RunSessionManager.Instance.LastResult != null, "Death must publish its result snapshot.");
        DeathLootRecord record = RunSessionManager.Instance.PendingDeathCrate;
        if (record != null)
        {
            Check(Vector3.Distance(record.Position, destination) < 0.0001f, "The saved death position must match the actual player position.");
            DeathLootCrate crate = RunSessionManager.Instance.ActiveDeathCrate;
            Check(crate != null && crate.gameObject.scene.handle == player.gameObject.scene.handle,
                "A death crate must belong to the dead player's real level scene.");
            Check(Vector3.Distance(crate.transform.position, destination) < 0.0001f, "The crate scene object must spawn at the saved death position.");
        }
    }

    private static void CheckSimpleCrate(int food, int wood)
    {
        DeathLootRecord crate = RunSessionManager.Instance.PendingDeathCrate;
        Check(crate != null && crate.LevelId == Level && crate.Ingredients.Count == 1 && crate.Gathered.Count == 1 &&
            Count(crate.Ingredients, Food) == food && Count(crate.Gathered, Wood) == wood,
            "Death crate contents must match the resource allocation for this death only.");
    }

    private static void CheckRestoredCrate()
    {
        CheckSameRecord(expectedCrate, RunSessionManager.Instance.PendingDeathCrate);
        DeathLootCrate crate = RunSessionManager.Instance.ActiveDeathCrate;
        Check(crate != null && crate.CrateId == expectedCrate.Id && crate.gameObject.scene.handle == SceneManager.GetSceneByName(Level).handle &&
            Vector3.Distance(crate.transform.position, expectedCrate.Position) < 0.0001f,
            "Same-map entry must rebuild the saved crate in the newly loaded scene at its original position.");
    }

    private static void CheckSameRecord(DeathLootRecord expected, DeathLootRecord actual)
    {
        Check(expected != null && actual != null && actual.Id == expected.Id && actual.LevelId == expected.LevelId &&
            Vector3.Distance(actual.Position, expected.Position) < 0.0001f, "The retained crate must keep its identity, level and position.");
        CheckCounts(expected.Ingredients, actual.Ingredients);
        CheckCounts(expected.Gathered, actual.Gathered);
    }

    private static void CheckCounts(IReadOnlyDictionary<ResourceType, int> expected, IReadOnlyDictionary<ResourceType, int> actual)
    {
        Check(expected.Count == actual.Count, "Saved resource type counts must remain unchanged.");
        foreach (KeyValuePair<ResourceType, int> entry in expected)
            Check(Count(actual, entry.Key) == entry.Value, "Saved crate resource quantities must remain unchanged.");
    }

    private static void CheckCrateCleared()
    {
        Check(RunSessionManager.Instance.PendingDeathCrate == null && RunSessionManager.Instance.ActiveDeathCrate == null,
            "Both the pending record and active crate reference must be cleared.");
    }

    private static void MovePlayer(TopDownController player, Vector3 position)
    {
        Check(player != null, "The real level must contain an active player object.");
        Rigidbody body = player.GetComponent<Rigidbody>();
        if (body != null) { body.velocity = Vector3.zero; body.position = position; }
        player.transform.position = position;
        Physics.SyncTransforms();
    }

    private static void ClearInventory()
    {
        var empty = new List<InventorySlotSnapshot>();
        foreach (InventorySlotSnapshot slot in InventoryManager.instance.CaptureInventory().Slots)
            empty.Add(new InventorySlotSnapshot(slot.Index, ResourceType.None, 0, slot.Capacity));
        Check(InventoryManager.instance.ApplyInventorySnapshot(new InventorySnapshot(empty)), "The fixture must clear the real bag through its snapshot API.");
    }

    private static void AddFood(ResourceType type, int amount)
    {
        Check(InventoryManager.instance.AddItemPartial(type, amount) == amount, "The food fixture must fit completely: " + type);
    }

    private static void AddWood(int amount)
    {
        Check(InventoryManager.instance.AddRunGathered(Wood, amount) == amount, "The gathered fixture must accept its wood completely.");
    }

    private static void SetRuntimePrice(ResourceType type, int price)
    {
        FieldInfo field = typeof(ShopManager).GetField("resourcePrices", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(field != null, "The price fixture must access the actual shop price table.");
        var prices = field.GetValue(ShopManager.Instance) as List<ShopManager.ResourcePrice>;
        Check(prices != null, "The actual runtime price table must exist.");
        ShopManager.ResourcePrice entry = prices.Find(item => item.type == type);
        if (entry == null) { entry = new ShopManager.ResourcePrice { type = type }; prices.Add(entry); }
        entry.pricePerUnit = price;
        Check(ShopManager.Instance.GetResourcePrice(type) == price, "The collection service must observe the fixture's real shop price.");
    }

    private static bool IsLevelReady(string level)
    {
        Scene scene = SceneManager.GetSceneByName(level);
        bool ready = scene.IsValid() && scene.isLoaded && LevelManager.instance != null && !LevelManager.instance.IsTransitioning() &&
            LevelManager.instance.CurrentLevelId == level && RunSessionManager.Instance != null && RunSessionManager.Instance.IsActive &&
            RunSessionManager.Instance.Phase == RunEndPhase.Exploring && FindPlayer(level) != null && Time.timeScale > 0f;
        if (!ready || !GameplaySceneValidation.IsCameraFrameReady(scene)) return false;
        GameplaySceneValidation.CheckSceneBindings(scene);
        return true;
    }

    private static bool IsHomeReady()
    {
        Scene home = SceneManager.GetSceneByName("UpGround");
        bool ready = LevelManager.instance != null && !LevelManager.instance.IsTransitioning() && string.IsNullOrEmpty(LevelManager.instance.CurrentLevelId) &&
            RunSessionManager.Instance != null && !RunSessionManager.Instance.IsActive && RunSessionManager.Instance.Phase == RunEndPhase.Inactive;
        if (!ready || !GameplaySceneValidation.IsCameraFrameReady(home)) return false;
        GameplaySceneValidation.CheckSceneBindings(home);
        return true;
    }

    private static TopDownController FindPlayer(string level)
    {
        Scene scene = SceneManager.GetSceneByName(level);
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (TopDownController player in root.GetComponentsInChildren<TopDownController>(true))
                if (player.gameObject.activeInHierarchy && player.CompareTag("Player")) return player;
        return null;
    }

    private static void EnterLevel(string level, string nextPhase)
    {
        Check(LevelManager.instance.TryEnterLevel(level), "Entry to the real scene must be accepted: " + level);
        SetPhase(nextPhase, 60);
    }

    private static void Retry(string nextPhase)
    {
        SettlementUIController ui = UnityEngine.Object.FindObjectOfType<SettlementUIController>(true);
        Check(ui != null && ui.IsVisible && ui.retryButton.interactable, "The real retry button must be available.");
        ui.retryButton.onClick.Invoke();
        Check(RunSessionManager.Instance.Phase == RunEndPhase.Transitioning, "Retry must start an actual scene transition.");
        SetPhase(nextPhase, 60);
    }

    private static void ReturnHome(string nextPhase)
    {
        SettlementUIController ui = UnityEngine.Object.FindObjectOfType<SettlementUIController>(true);
        Check(ui != null && ui.IsVisible && ui.homeButton.interactable, "The real home button must be available.");
        ui.homeButton.onClick.Invoke();
        Check(RunSessionManager.Instance.Phase == RunEndPhase.Transitioning, "Returning home must start an actual scene transition.");
        SetPhase(nextPhase, 60);
    }

    private static void AddExpectedPermanentWood(int amount)
    {
        expectedWoodGain += amount;
        CheckPermanentWood();
    }

    private static void CheckPermanentWood()
    {
        Check(GameValManager.Instance.GetResourceCount(Wood) == woodBaseline + expectedWoodGain,
            "Historical permanent wood must remain untouched; only this run's retained or extracted receipts may be credited.");
    }

    private static int Count(IReadOnlyDictionary<ResourceType, int> values, ResourceType type)
    {
        int count;
        return values.TryGetValue(type, out count) ? count : 0;
    }

    private static void SetPhase(string phase, double timeout, double wait = 0)
    {
        SessionState.SetString(PhaseKey, phase);
        SessionState.SetString(DeadlineKey, (EditorApplication.timeSinceStartup + timeout).ToString("R", CultureInfo.InvariantCulture));
        SessionState.SetString(ReadyKey, (EditorApplication.timeSinceStartup + wait).ToString("R", CultureInfo.InvariantCulture));
        Debug.Log("BATCH3_PHASE " + phase);
    }

    private static double GetTime(string key)
    {
        double value;
        return double.TryParse(SessionState.GetString(key, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0;
    }

    private static string DescribeRuntime()
    {
        LevelManager levels = LevelManager.instance;
        RunSessionManager runs = RunSessionManager.Instance;
        return "level=" + (levels == null ? "missing" : levels.CurrentLevelId) +
            ", transition=" + (levels != null && levels.IsTransitioning()) +
            ", phase=" + (runs == null ? "missing" : runs.Phase.ToString()) +
            ", crate=" + (runs == null || runs.PendingDeathCrate == null ? "none" : runs.PendingDeathCrate.Id);
    }

    private static void OnRuntimeLog(string message, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        string phase = SessionState.GetString(PhaseKey, "");
        if (!EditorApplication.isPlaying || string.IsNullOrEmpty(phase) || phase == "exit") return;
        if (phase == "recover-first" && message.Contains(ExpectedGatheredListenerFailure)) return;
        if (string.IsNullOrEmpty(SessionState.GetString(UnexpectedErrorKey, "")))
            SessionState.SetString(UnexpectedErrorKey, message + "\n" + stackTrace);
    }

    private static void Finish(bool success, Exception error)
    {
        SessionState.SetInt(ResultKey, success ? 0 : 1);
        SessionState.SetString(PhaseKey, "exit");
        if (success) Debug.Log("BATCH3_UNITY_SCENE_TESTS_PASS");
        else Debug.LogError("BATCH3_UNITY_SCENE_TESTS_FAIL: " + error);
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
        else EditorApplication.delayCall += ExitEditor;
    }

    private static void ExitEditor()
    {
        if (SessionState.GetString(PhaseKey, "") != "exit") return;
        int result = SessionState.GetInt(ResultKey, 1);
        SessionState.EraseString(PhaseKey);
        EditorApplication.Exit(result);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
