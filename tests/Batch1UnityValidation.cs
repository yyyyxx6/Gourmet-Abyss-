#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Copy this, Batch2UnityValidation (shared scene assertions), and the pure tests into an isolated project's Assets/Editor.
[InitializeOnLoad]
public static class Batch1UnityValidation
{
    private const string PhaseKey = "ChefDungeon.Batch1.ValidationPhase";
    private const string ResultKey = "ChefDungeon.Batch1.ValidationResult";
    private static double readyAt;

    static Batch1UnityValidation()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static void Run()
    {
        InventoryPackingTests.Run();
        RunSessionDataTests.Run();
        RunResourceTests.Run();
        Debug.Log("BATCH1_PURE_TESTS_PASS");
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "ChefDungeonBatch1";
        EditorSceneManager.OpenScene("Assets/Scenes/UpGround.unity", OpenSceneMode.Single);
        SessionState.SetString(PhaseKey, "enter");
        SessionState.SetInt(ResultKey, 1);
        EditorApplication.EnterPlaymode();
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetString(PhaseKey, "") == "enter")
        {
            readyAt = EditorApplication.timeSinceStartup + 3;
            SessionState.SetString(PhaseKey, "test");
        }
    }

    private static void Tick()
    {
        string phase = SessionState.GetString(PhaseKey, "");
        if (phase == "exit" && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            SessionState.EraseString(PhaseKey);
            EditorApplication.Exit(SessionState.GetInt(ResultKey, 1));
            return;
        }
        if (phase != "test" || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup < readyAt) return;
        SessionState.SetString(PhaseKey, "running");
        try
        {
            ValidateComponents();
            SessionState.SetInt(ResultKey, 0);
            Debug.Log("BATCH1_UNITY_COMPONENT_TESTS_PASS");
        }
        catch (Exception error)
        {
            Debug.LogError("BATCH1_UNITY_COMPONENT_TESTS_FAIL: " + error);
            SessionState.SetInt(ResultKey, 1);
        }
        finally
        {
            SessionState.SetString(PhaseKey, "exit");
            EditorApplication.ExitPlaymode();
        }
    }

    private static void ValidateComponents()
    {
        InventoryManager inventory = InventoryManager.instance;
        Check(inventory != null && GameValManager.Instance != null && RunSessionManager.Instance != null,
            "The real scene must initialize inventory, resource and run managers.");
        GameplaySceneValidation.CaptureHomeState();
        InventorySnapshot original = inventory.CaptureInventory();
        Check(original.Slots.Count == inventory.GetSlotCount() && original.Slots.Count > 0,
            "Snapshot must contain all unlocked slots and exclude the lock preview.");
        Check(inventory.GetSlot(original.Slots.Count) == null, "Lock preview must not be an accessible inventory slot.");

        var empty = new List<InventorySlotSnapshot>();
        int capacity = 0;
        foreach (InventorySlotSnapshot slot in original.Slots)
        {
            empty.Add(new InventorySlotSnapshot(slot.Index, ResourceType.None, 0, slot.Capacity));
            capacity += slot.Capacity;
        }
        Check(capacity >= 2 && inventory.ApplyInventorySnapshot(new InventorySnapshot(empty)), "Clear live slots via snapshot.");
        Check(inventory.AddItemPartial(ResourceType.LootMushroom, capacity - 1) == capacity - 1, "Fill all but one unit.");
        InventorySnapshot beforeInvalid = inventory.CaptureInventory();
        var invalid = new List<InventorySlotSnapshot>(beforeInvalid.Slots);
        InventorySlotSnapshot first = invalid[0];
        invalid[0] = new InventorySlotSnapshot(first.Index, first.ItemType, first.Count, first.Capacity + 1);
        Check(!inventory.ApplyInventorySnapshot(new InventorySnapshot(invalid)), "Mismatched capacities must be rejected.");
        Check(inventory.GetItemCount(ResourceType.LootMushroom) == capacity - 1, "Rejected snapshot must not modify inventory.");

        var pickupPlayer = new GameObject("Batch1PickupPlayer");
        var pickupObject = new GameObject("Batch1PartialPickup", typeof(BoxCollider));
        LootCollector pickup = pickupObject.AddComponent<LootCollector>();
        pickup.SetResourceInfo(ResourceType.LootMushroom, 3);
        SetPrivate(pickup, "player", pickupPlayer.transform);
        InvokePrivate(pickup, "Collect");
        Check(inventory.GetItemCount(ResourceType.LootMushroom) == capacity, "Pickup must fill the remaining one unit.");
        Check((int)GetPrivate(pickup, "resourceAmount") == 2, "Unaccepted pickup units must stay on the object.");
        InvokePrivate(pickup, "Collect");
        Check(inventory.GetItemCount(ResourceType.LootMushroom) == capacity &&
            (int)GetPrivate(pickup, "resourceAmount") == 2, "Retry while full must not duplicate or discard units.");
        UnityEngine.Object.Destroy(pickupObject);
        UnityEngine.Object.Destroy(pickupPlayer);

        inventory.ApplyInventorySnapshot(new InventorySnapshot(empty));
        Check(inventory.AddRunIngredient(ResourceType.LootMushroom, 1), "Legacy food entry must route into slots.");
        Check(inventory.GetItemCount(ResourceType.LootMushroom) == 1 && inventory.GetRunGatheredCount(ResourceType.LootMushroom) == 0,
            "Food cannot leak into the no-slot material bag.");
        inventory.ClearRunGathered();
        RunSessionManager.Instance.BeginRun("Layer1");
        Check(inventory.AddRunGathered(ResourceType.LootPumkin, 5) == 5, "Wood must be collected without a food slot.");
        Check(inventory.GetItemCount(ResourceType.LootMushroom) == 1, "Gathering wood must not alter food slots.");
        RunResultSnapshot result = RunSessionManager.Instance.CompleteRun();
        Check(result.GatheredCounts[ResourceType.LootPumkin] == 5 && result.IngredientDelta == 0,
            "The live bridge must record actual collection and the carried-in baseline.");
        Check(ReferenceEquals(result, RunSessionManager.Instance.CompleteRun()), "The live bridge must complete once.");

        ResourceItem wood = GameValManager.Instance.GetResourceInfo(ResourceType.LootPumkin);
        int oldWood = wood.count;
        int oldCapacity = wood.maxCapacity;
        wood.count = 100;
        wood.maxCapacity = 103;
        inventory.CommitRunGatheredToPermanent();
        Check(wood.count == 103 && inventory.GetRunGatheredCount(ResourceType.LootPumkin) == 2,
            "Capacity-limited commit must retain two pending units.");
        inventory.CommitRunGatheredToPermanent();
        Check(wood.count == 103 && inventory.GetRunGatheredCount(ResourceType.LootPumkin) == 2, "Second commit must not pay twice.");
        Check(!LevelManager.instance.TryEnterLevel("Layer1"), "Entry must report failure when pending materials cannot commit.");
        Check(!LevelManager.instance.IsTransitioning() && inventory.GetRunGatheredCount(ResourceType.LootPumkin) == 2,
            "Starting another level must not clear uncommitted materials.");
        wood.maxCapacity = 105;
        inventory.CommitRunGatheredToPermanent();
        Check(wood.count == 105 && inventory.GetRunGatheredCounts().Count == 0, "Freed permanent capacity allows the remainder to commit.");
        wood.count = oldWood;
        wood.maxCapacity = oldCapacity;

        RunSessionManager.Instance.BeginRun("Layer2");
        RunResultSnapshot next = RunSessionManager.Instance.CaptureCurrentResult();
        Check(next.IngredientDelta == 0 && next.GatheredCounts.Count == 0 && next.LevelId == "Layer2",
            "Next run must retain food and reset collection statistics.");
        var plantObject = new GameObject("Batch1Gatherable");
        EnemyHealth plant = plantObject.AddComponent<EnemyHealth>();
        SetPrivate(plant, "healthBarType", EnemyHealth.HealthBarType.Gatherable);
        InvokePrivate(plant, "Die");
        Check(RunSessionManager.Instance.CaptureCurrentResult().KillCount == 0, "Gatherable destruction must not count as a monster kill.");
        var monsterObject = new GameObject("Batch1Monster");
        EnemyHealth monster = monsterObject.AddComponent<EnemyHealth>();
        InvokePrivate(monster, "Die");
        InvokePrivate(monster, "Die");
        Check(RunSessionManager.Instance.CaptureCurrentResult().KillCount == 1, "A real monster death must count exactly once.");
        UnityEngine.Object.Destroy(plantObject);
        UnityEngine.Object.Destroy(monsterObject);
        RunSessionManager.Instance.CompleteRun();
        Check(inventory.ApplyInventorySnapshot(original), "Restore original live inventory after validation.");
    }

    private static object GetPrivate(object target, string name)
    {
        return target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
    }

    private static void SetPrivate(object target, string name, object value)
    {
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    private static void InvokePrivate(object target, string name)
    {
        target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
