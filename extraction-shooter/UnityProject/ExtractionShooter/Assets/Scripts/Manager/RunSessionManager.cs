using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Bridges scene lifecycle and gameplay events to the run's data record.</summary>
public sealed class RunSessionManager : MonoBehaviour
{
    public static RunSessionManager Instance { get; private set; }

    private readonly RunSessionData data = new RunSessionData();
    private readonly RunEndFlow endFlow = new RunEndFlow();
    private SettlementUIController settlementUI;
    private float resumeTimeScale = 1f;
    private bool ownsPause;
    private DeathLootRecord pendingDeathCrate;
    private DeathLootCrate activeDeathCrate;
    private bool collectingDeathCrate;
    private GameObject deathCratePrefab;
    public bool IsActive => data.IsActive;
    public RunEndPhase Phase => endFlow.Phase;
    public bool IsEndingRun => Phase == RunEndPhase.Settling ||
        Phase == RunEndPhase.ShowingResult || Phase == RunEndPhase.Transitioning;
    public RunResultSnapshot LastResult { get; private set; }
    public DeathLootRecord PendingDeathCrate => pendingDeathCrate;
    public DeathLootCrate ActiveDeathCrate => activeDeathCrate;
    public DeathLootReceipt LastDeathLootReceipt { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        RestoreTimeScale();
        ClearDeathCrate();
        endFlow.Reset();
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!data.IsActive || PlayerStateManager.instance == null) return;
        if (PlayerStateManager.instance.currentState != PlayerState.Battle ||
            PlayerStateManager.instance.isSettingUIActive) return;
        if (BattleValManager.Instance == null || !BattleValManager.Instance.IsActive) return;
        if (LevelManager.instance != null && LevelManager.instance.IsTransitioning()) return;
        data.AdvanceTime(Time.deltaTime);
    }

    public void BeginRun(string levelId)
    {
        InventoryManager inventory = InventoryManager.instance;
        if (inventory == null)
        {
            Debug.LogError("Cannot begin run statistics without InventoryManager.");
            return;
        }

        if (string.IsNullOrWhiteSpace(levelId))
            throw new System.ArgumentException("A loaded level ID is required.", nameof(levelId));
        Dictionary<ResourceType, int> initialIngredients = inventory.CaptureIngredientInventory().GetCounts();
        bool restoreCrate = pendingDeathCrate != null && pendingDeathCrate.LevelId == levelId;
        if (restoreCrate) RequireDeathCratePrefab();
        List<int> knownRecipes = new List<int>();
        if (RestaurantPanel.instance != null && RestaurantPanel.instance.dishRecipes != null)
        {
            foreach (DishRecipe recipe in RestaurantPanel.instance.dishRecipes)
                if (recipe != null && !recipe.locked) knownRecipes.Add(recipe.dishID);
        }

        List<PetType> knownPets = new List<PetType>();
        if (WeaponStatsManager.Instance != null && WeaponStatsManager.Instance.petStateList != null)
        {
            foreach (WeaponStatsManager.PetStateEntry pet in WeaponStatsManager.Instance.petStateList)
                if (pet != null && pet.isEnabled) knownPets.Add(pet.petType);
        }

        // Validate all scene-backed data before releasing the transition gate.
        if (Phase != RunEndPhase.Inactive && Phase != RunEndPhase.Transitioning &&
            !(Phase == RunEndPhase.Exploring && !data.IsActive))
        {
            Debug.LogError("Cannot begin a new run while a run result is still being handled.");
            return;
        }
        if (restoreCrate) SpawnDeathCrate();
        if (!data.IsActive && Phase == RunEndPhase.Exploring) endFlow.Reset();
        if (!endFlow.Begin())
        {
            Debug.LogError("Cannot begin a new run while a run result is still being handled.");
            return;
        }
        data.Begin(levelId, initialIngredients, knownRecipes, knownPets);
        LastResult = null;
        LastDeathLootReceipt = null;
        if (pendingDeathCrate != null && !restoreCrate) ClearDeathCrate();
    }

    public bool TryEndRun(RunEndReason reason)
    {
        if (!data.IsActive || Phase != RunEndPhase.Exploring || collectingDeathCrate) return false;
        LevelManager levels = LevelManager.instance;
        InventoryManager inventory = InventoryManager.instance;
        if (levels == null || levels.IsTransitioning() || string.IsNullOrEmpty(levels.CurrentLevelId) || inventory == null)
            return false;

        // Resolve the view and validate the inventory before changing any resources.
        InventorySnapshot before = inventory.CaptureInventory();
        DeathDropResult deathDrop = null;
        DeathLootRecord replacementCrate = null;
        if (reason == RunEndReason.Death)
        {
            TopDownController player = FindLevelPlayer(levels.CurrentLevelId, false);
            if (player == null) return false;
            decimal retention = GetDeathRetentionRate();
            deathDrop = DeathDropCalculator.Calculate(before, inventory.GetRunGatheredCounts(), retention);
            if (deathDrop.DroppedIngredients.Count > 0 || deathDrop.DroppedGathered.Count > 0)
            {
                RequireDeathCratePrefab();
                replacementCrate = new DeathLootRecord(System.Guid.NewGuid().ToString("N"), levels.CurrentLevelId,
                    player.transform.position, deathDrop.DroppedIngredients, deathDrop.DroppedGathered);
            }
        }
        if (settlementUI == null)
            settlementUI = SettlementUIController.EnsureExists(transform);
        if (settlementUI == null || !endFlow.TryBeginEnd(reason)) return false;

        PauseForSettlement();
        GlobalMessageUI.Clear();
        if (reason == RunEndReason.Death)
            ApplyDeathDrop(inventory, deathDrop, replacementCrate);

        RunResultSnapshot result = CompleteRun();
        inventory.CommitRunGatheredToPermanent();
        var ownedGathered = new Dictionary<ResourceType, long>();
        foreach (ResourceType type in result.GatheredCounts.Keys)
            ownedGathered[type] = GetOwnedGatheredTotal(type, inventory);
        foreach (ResourceType type in result.RetainedGatheredCounts.Keys)
            if (!ownedGathered.ContainsKey(type))
                ownedGathered[type] = GetOwnedGatheredTotal(type, inventory);

        endFlow.ShowResult();
        settlementUI.Show(result, reason, ownedGathered, () => RetryExploration(), () => ReturnToTown());
        return true;
    }

    private void ApplyDeathDrop(InventoryManager inventory, DeathDropResult drop, DeathLootRecord replacement)
    {
        if (!inventory.ApplyInventorySnapshot(drop.RetainedInventory))
            throw new System.InvalidOperationException("The inventory changed during death allocation.");
        inventory.ReplaceRunGathered(drop.RetainedGathered);
        // Every death replaces the old crate, including a death with nothing left to drop.
        ClearDeathCrate();
        pendingDeathCrate = replacement;
        if (pendingDeathCrate != null) SpawnDeathCrate();
    }

    private static decimal GetDeathRetentionRate()
    {
        float configured = WeaponStatsManager.Instance != null ? WeaponStatsManager.Instance.deathRetentionRate : 0.1f;
        if (float.IsNaN(configured) || float.IsInfinity(configured)) configured = 0.1f;
        // Keep quantity arithmetic in decimal, so ten items at 10% retain exactly one.
        return (decimal)Mathf.Clamp01(configured);
    }

    public bool TryCollectDeathCrate(string crateId)
    {
        if (!data.IsActive || Phase != RunEndPhase.Exploring || collectingDeathCrate || pendingDeathCrate == null ||
            pendingDeathCrate.Id != crateId || activeDeathCrate == null || activeDeathCrate.CrateId != crateId)
            return false;
        LevelManager levels = LevelManager.instance;
        InventoryManager inventory = InventoryManager.instance;
        if (levels == null || levels.IsTransitioning() || levels.CurrentLevelId != pendingDeathCrate.LevelId || inventory == null)
            return false;
        if (PlayerStateManager.instance == null || PlayerStateManager.instance.currentState != PlayerState.Battle ||
            PlayerStateManager.instance.isSettingUIActive) return false;
        TopDownController player = FindLevelPlayer(pendingDeathCrate.LevelId, true);
        if (player == null || player.isDead || !activeDeathCrate.IsPlayerInRange(player)) return false;
        if (pendingDeathCrate.Ingredients.Count > 0 && ShopManager.Instance == null) return false;

        // Gathered materials must fit completely in their numeric store; no part is silently lost.
        DeathLootRecord claimedCrate = pendingDeathCrate;
        Dictionary<ResourceType, int> gatheredAfter = inventory.GetRunGatheredCounts();
        foreach (KeyValuePair<ResourceType, int> entry in claimedCrate.Gathered)
        {
            int previous;
            gatheredAfter.TryGetValue(entry.Key, out previous);
            long next = (long)previous + entry.Value;
            if (next > int.MaxValue) return false;
            gatheredAfter[entry.Key] = (int)next;
        }

        InventoryPackResult packing = InventoryPacking.Add(inventory.CaptureInventory(), claimedCrate.Ingredients,
            type => Mathf.Max(0, ShopManager.Instance.GetResourcePrice(type)));
        DeathLootReceipt receipt = new DeathLootReceipt(packing, claimedCrate.Gathered);
        collectingDeathCrate = true;
        try
        {
            if (!inventory.ApplyInventorySnapshot(packing.Snapshot)) return false;
            Dictionary<ResourceType, int> previousGathered = inventory.ReplaceRunGatheredSilently(gatheredAfter);
            LastDeathLootReceipt = receipt;
            ClearDeathCrate();
            foreach (KeyValuePair<ResourceType, int> entry in claimedCrate.Gathered)
                RecordGathered(entry.Key, entry.Value);

            // Observers run only after both inventories and the crate identity are committed.
            collectingDeathCrate = false;
            inventory.NotifyRunGatheredChanges(previousGathered);
            long food = SumCounts(receipt.RecoveredIngredients);
            long materials = SumCounts(receipt.RecoveredGathered);
            long discarded = SumCounts(receipt.DiscardedIngredients);
            string message = $"已找回 {food} 份食材、{materials} 份采集物";
            if (discarded > 0) message += $"；背包已满，丢弃 {discarded} 份食材";
            try { GlobalMessageUI.Show(message); }
            catch (System.Exception error) { Debug.LogException(error, this); }
            return true;
        }
        finally
        {
            collectingDeathCrate = false;
        }
    }

    private void RequireDeathCratePrefab()
    {
        if (deathCratePrefab == null) deathCratePrefab = Resources.Load<GameObject>("Loot/DeathLootCrate");
        if (deathCratePrefab == null || deathCratePrefab.GetComponent<DeathLootCrate>() == null)
            throw new System.InvalidOperationException("Resources/Loot/DeathLootCrate prefab is missing or invalid.");
    }

    private void SpawnDeathCrate()
    {
        if (pendingDeathCrate == null) return;
        Scene scene = SceneManager.GetSceneByName(pendingDeathCrate.LevelId);
        if (!scene.IsValid() || !scene.isLoaded)
            throw new System.InvalidOperationException("Cannot restore a death crate before its level is loaded.");
        if (activeDeathCrate != null && activeDeathCrate.CrateId == pendingDeathCrate.Id &&
            activeDeathCrate.gameObject.scene.handle == scene.handle) return;
        RequireDeathCratePrefab();
        GameObject instance = Instantiate(deathCratePrefab, pendingDeathCrate.Position, Quaternion.identity);
        try
        {
            instance.name = "DeathLootCrate";
            SceneManager.MoveGameObjectToScene(instance, scene);
            DeathLootCrate restored = instance.GetComponent<DeathLootCrate>();
            restored.Initialize(pendingDeathCrate.Id);
            if (activeDeathCrate != null) Destroy(activeDeathCrate.gameObject);
            activeDeathCrate = restored;
        }
        catch
        {
            Destroy(instance);
            throw;
        }
    }

    private void ClearDeathCrate()
    {
        pendingDeathCrate = null;
        if (activeDeathCrate != null) Destroy(activeDeathCrate.gameObject);
        activeDeathCrate = null;
    }

    private static TopDownController FindLevelPlayer(string levelId, bool requireEnabled)
    {
        Scene scene = SceneManager.GetSceneByName(levelId);
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (TopDownController player in root.GetComponentsInChildren<TopDownController>(true))
                if (player.gameObject.activeInHierarchy && (!requireEnabled || player.enabled) && player.CompareTag("Player"))
                    return player;
        return null;
    }

    private static long SumCounts(IReadOnlyDictionary<ResourceType, int> counts)
    {
        long total = 0;
        foreach (int value in counts.Values) total += value;
        return total;
    }

    private static long GetOwnedGatheredTotal(ResourceType type, InventoryManager inventory)
    {
        long permanent = GameValManager.Instance != null ? GameValManager.Instance.GetResourceCount(type) : 0;
        return System.Math.Max(0L, permanent) + System.Math.Max(0L, inventory.GetRunGatheredCount(type));
    }

    public bool RetryExploration()
    {
        if (Phase != RunEndPhase.ShowingResult || LastResult == null) return false;
        InventoryManager.instance?.CommitRunGatheredToPermanent();
        if (InventoryManager.instance != null && InventoryManager.instance.GetRunGatheredCounts().Count > 0)
        {
            RejectDestination("采集物仓库已满，请先返回小镇整理资源");
            return false;
        }
        if (!endFlow.TryBeginTransition()) return false;
        settlementUI?.SetBusy(true);
        RestoreTimeScale();
        bool started = LevelManager.instance != null &&
            LevelManager.instance.TryRestartFromSettlement(LastResult.LevelId, success => OnDestinationReady(success, false));
        if (!started) OnDestinationReady(false, false);
        return started;
    }

    public bool ReturnToTown()
    {
        if (Phase != RunEndPhase.ShowingResult || LastResult == null || !endFlow.TryBeginTransition()) return false;
        settlementUI?.SetBusy(true);
        RestoreTimeScale();
        bool started = LevelManager.instance != null &&
            LevelManager.instance.TryReturnHomeFromSettlement(LastResult.LevelId, success => OnDestinationReady(success, true));
        if (!started) OnDestinationReady(false, true);
        return started;
    }

    private void OnDestinationReady(bool success, bool returningHome)
    {
        if (success)
        {
            if (returningHome) endFlow.FinishToHome();
            settlementUI?.Hide();
            return;
        }

        endFlow.CancelTransition();
        PauseForSettlement();
        RejectDestination("场景切换未完成，请重试或返回小镇");
    }

    private void RejectDestination(string message)
    {
        if (settlementUI == null) return;
        settlementUI.SetBusy(false);
        settlementUI.SetStatusMessage(message);
    }

    private void PauseForSettlement()
    {
        BattleValManager.Instance?.StopConsuming();
        if (PlayerStateManager.instance != null)
        {
            PlayerStateManager.instance.currentState = PlayerState.Settlement;
            PlayerStateManager.instance.isSettingUIActive = false;
            if (PlayerStateManager.instance.SetttingPanel != null)
                PlayerStateManager.instance.SetttingPanel.SetActive(false);
        }
        foreach (TopDownController controller in FindObjectsOfType<TopDownController>())
            controller.StopForRunEnd();
        UIManager.instance?.SetBattleUIActive(false);
        if (!ownsPause)
        {
            resumeTimeScale = Time.timeScale > 0 ? Time.timeScale : 1f;
            ownsPause = true;
        }
        Time.timeScale = 0f;
    }

    private void RestoreTimeScale()
    {
        if (!ownsPause) return;
        Time.timeScale = resumeTimeScale;
        ownsPause = false;
    }

    public RunResultSnapshot CompleteRun()
    {
        if (!data.IsActive) return LastResult;
        InventoryManager inventory = InventoryManager.instance;
        if (inventory == null)
        {
            Debug.LogError("Cannot complete run statistics without InventoryManager.");
            return null;
        }
        LastResult = data.Complete(inventory.CaptureIngredientInventory(), inventory.GetRunGatheredCounts());
        return LastResult;
    }

    public RunResultSnapshot CaptureCurrentResult()
    {
        if (!data.IsActive || InventoryManager.instance == null) return LastResult;
        return data.Capture(InventoryManager.instance.CaptureIngredientInventory(),
            InventoryManager.instance.GetRunGatheredCounts());
    }

    public void RecordGathered(ResourceType type, int acceptedAmount)
    {
        if (ResourceStorageRules.IsGathered(type)) data.RecordGathered(type, acceptedAmount);
    }

    public void RecordKill() => data.RecordKill();
    public void RecordRecipeUnlocked(int recipeId) => data.RecordRecipeUnlocked(recipeId);
    public void RecordPetUnlocked(PetType type) => data.RecordPetUnlocked(type);
}
