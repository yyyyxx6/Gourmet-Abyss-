#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Additional regressions only; also copy Batch2UnityValidation for the shared upstream scene/camera assertions.
[InitializeOnLoad]
public static class FinalGameplayValidation
{
    private const string Prefix = "ChefDungeon.FinalValidation.";
    private const string PhaseKey = Prefix + "Phase";
    private const string DeadlineKey = Prefix + "Deadline";
    private const string ReadyKey = Prefix + "ReadyAt";
    private const string ResultKey = Prefix + "Result";
    private const string ErrorKey = Prefix + "UnexpectedError";
    private const string Level = "Layer3";
    private const string SwitchLevelName = "Layer2";
    private const string SwitchSnapshotFailure = "An unlocked inventory slot is missing or locked.";
    private const ResourceType Food = ResourceType.LootMushroom;
    private const ResourceType Wood = ResourceType.LootPumkin;
    private static readonly int[] CapacitySteps = { 1, 2, 4, 6, 9, 12 };
    private static int capacityIndex;
    private static int woodBaseline;
    private static DishRecipe unlockRecipe;
    private static InventorySnapshot beforePresentation;
    private static DeathLootRecord deathRecord;
    private static InventorySnapshot beforeNormalSwitch;
    private static int firstLayer3Handle;
    private static bool injectingSwitchSnapshotFailure;
    private static bool observedSwitchSnapshotFailure;

    static FinalGameplayValidation()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Application.logMessageReceived += OnRuntimeLog;
    }

    public static void Run()
    {
        try
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Final regressions must start in Edit Mode.");
            SettlementPanelBuilder.Build();
            DeathLootCrateBuilder.Build();
            PlayerSettings.companyName = "CodexValidation";
            PlayerSettings.productName = "ChefDungeonFinalRegression";
            EditorSceneManager.OpenScene("Assets/Scenes/UpGround.unity", OpenSceneMode.Single);
            SessionState.SetInt(ResultKey, 1);
            SessionState.EraseString(ErrorKey);
            SetPhase("enter-play", 60);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Finish(false, error); }
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
            Check(string.IsNullOrEmpty(SessionState.GetString(ErrorKey, "")), "Unexpected runtime error: " + SessionState.GetString(ErrorKey, ""));
            Check(EditorApplication.timeSinceStartup <= ReadTime(DeadlineKey), "Timed out during " + phase);
            if (!EditorApplication.isPlaying || EditorApplication.timeSinceStartup < ReadTime(ReadyKey)) return;
            switch (phase)
            {
                case "prepare": Prepare(); break;
                case "capacity-visible": if (View().canvasGroup.alpha >= 0.99f) ValidateCapacityPresentation(); break;
                case "layer3-first": if (IsLevelReady()) ValidateSwitchFailureAndStart(); break;
                case "switch-layer2": if (IsLevelReady(SwitchLevelName)) ValidateLayer2AndSwitchBack(); break;
                case "switch-layer3": if (IsLevelReady()) ValidateSwitchReturnAndContinue(); break;
                case "death-visible": if (View().canvasGroup.alpha >= 0.99f) ValidateDeathPresentationAndRetry(); break;
                case "layer3-retry": if (IsLevelReady()) RecoverAndExtract(); break;
                case "extraction-visible": if (View().canvasGroup.alpha >= 0.99f) ValidateSecondResultAndReturn(); break;
                case "home": if (IsHomeReady()) ValidateHome(); break;
            }
        }
        catch (Exception error) { Finish(false, new InvalidOperationException("Phase " + phase, error)); }
    }

    private static void Prepare()
    {
        Check(InventoryManager.instance != null && WeaponStatsManager.Instance != null && RestaurantPanel.instance != null &&
            GameValManager.Instance != null && RunSessionManager.Instance != null, "The real home scene must provide all regression dependencies.");
        GameplaySceneValidation.CaptureHomeState();
        ClearInventory();
        InventoryManager.instance.ClearRunGathered();
        WeaponStatsManager.Instance.SetInventorySlotCount(1);
        WeaponStatsManager.Instance.SetInventorySlotCapacity(4);
        Check(InventoryManager.instance.AddItemPartial(Food, 3) == 3, "The initial one-slot fixture contains three food units.");
        capacityIndex = 0;
        ShowCapacityPresentation();
    }

    private static void ShowCapacityPresentation()
    {
        int unlocked = CapacitySteps[capacityIndex];
        WeaponStatsManager.Instance.SetInventorySlotCount(unlocked);
        InventoryManager inventory = InventoryManager.instance;
        beforePresentation = inventory.CaptureInventory();
        Check(beforePresentation.Slots.Count == unlocked && inventory.GetSlotCount() == unlocked && inventory.GetSlot(unlocked) == null,
            "Every expansion snapshot must contain all unlocked slots and exclude its lock preview.");
        FieldInfo slotsField = typeof(InventoryManager).GetField("slots", BindingFlags.Instance | BindingFlags.NonPublic);
        var liveSlots = slotsField == null ? null : slotsField.GetValue(inventory) as List<InventoryItemUI>;
        Check(liveSlots != null && liveSlots.Count == unlocked + 1 && liveSlots[unlocked].IsLockedPreviewSlot(),
            "Expansion must keep exactly one separate locked preview.");
        Check(inventory.GetItemCount(Food) == 3, "Increasing unlocked slots must not drop or duplicate existing ingredients.");

        var preview = new RunSessionData();
        preview.Begin("CapacityPresentation", beforePresentation.GetCounts());
        RunResultSnapshot result = preview.Capture(beforePresentation, new Dictionary<ResourceType, int>());
        SettlementUIController view = View();
        view.Show(result, RunEndReason.Extracted, new Dictionary<ResourceType, int>(), () => { }, () => { });
        view.Show(result, RunEndReason.Extracted, new Dictionary<ResourceType, int>(), () => { }, () => { });
        SetPhase("capacity-visible", 10, 0.3);
    }

    private static void ValidateCapacityPresentation()
    {
        SettlementUIController view = View();
        CheckSameInventory(beforePresentation, InventoryManager.instance.CaptureInventory());
        List<RectTransform> entries = ActiveEntries(view.inventoryContent);
        Check(entries.Count == CapacitySteps[capacityIndex], "Repeated presentation must produce exactly one visual entry per unlocked slot, including empty slots.");
        Canvas.ForceUpdateCanvases();
        for (int index = 0; index < entries.Count; index++)
        {
            Rect bounds = WorldBounds(entries[index]);
            Check(bounds.width > 0f && bounds.height > 0f, "Every inventory presentation cell must have a nonzero layout.");
            for (int previous = 0; previous < index; previous++)
            {
                Rect other = WorldBounds(entries[previous]);
                float overlapWidth = Mathf.Min(bounds.xMax, other.xMax) - Mathf.Max(bounds.xMin, other.xMin);
                float overlapHeight = Mathf.Min(bounds.yMax, other.yMax) - Mathf.Max(bounds.yMin, other.yMin);
                Check(overlapWidth <= 0.5f || overlapHeight <= 0.5f, "Expanded inventory cells must not overlap.");
            }
        }
        Check(!view.petSection.activeSelf && !view.recipeSection.activeSelf && !view.gatheredSection.activeSelf,
            "Empty reward categories must remain hidden in every inventory layout.");
        CheckIndependentInput(view);
        view.Hide();
        Debug.Log("FINAL_CAPACITY_LAYOUT_PASS " + CapacitySteps[capacityIndex]);
        if (++capacityIndex < CapacitySteps.Length) { ShowCapacityPresentation(); return; }
        ValidateRestaurantConsumption();
        PrepareRealLayer3();
    }

    private static void ValidateRestaurantConsumption()
    {
        DishRecipe selected = null;
        Dictionary<ResourceType, int> costs = null;
        foreach (DishRecipe candidate in RestaurantPanel.instance.dishRecipes)
        {
            if (candidate == null || candidate.ingredients == null) continue;
            var candidateCosts = new Dictionary<ResourceType, int>();
            bool valid = true;
            foreach (DishIngredient ingredient in candidate.ingredients)
            {
                if (ingredient == null || ingredient.requiredCount <= 0 || !ResourceStorageRules.IsIngredient(ingredient.resourceType)) { valid = false; break; }
                int old;
                candidateCosts.TryGetValue(ingredient.resourceType, out old);
                candidateCosts[ingredient.resourceType] = checked(old + ingredient.requiredCount);
            }
            long neededSlots = 0;
            foreach (int amount in candidateCosts.Values) neededSlots += ((long)amount + 4) / 4;
            if (valid && candidateCosts.Count > 0 && neededSlots <= InventoryManager.instance.GetSlotCount())
            { selected = candidate; costs = candidateCosts; break; }
        }
        Check(selected != null, "The real restaurant must contain an affordable ingredient recipe for the regression.");
        ClearInventory();
        var permanent = new Dictionary<ResourceType, int>();
        foreach (KeyValuePair<ResourceType, int> entry in costs)
        {
            Check(InventoryManager.instance.AddItemPartial(entry.Key, entry.Value + 1) == entry.Value + 1,
                "The configured recipe plus one spare unit of each ingredient must fit.");
            permanent[entry.Key] = GameValManager.Instance.GetResourceCount(entry.Key);
        }
        MethodInfo consume = typeof(RestaurantPanel).GetMethod("ConsumeIngredientsForRecipe", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(consume != null && (bool)consume.Invoke(RestaurantPanel.instance, new object[] { selected }),
            "The restaurant's actual cooking consumption entry must accept the configured recipe.");
        foreach (KeyValuePair<ResourceType, int> entry in costs)
        {
            Check(InventoryManager.instance.GetItemCount(entry.Key) == 1, "Cooking must deduct the exact recipe quantity from the live inventory.");
            Check(GameValManager.Instance.GetResourceCount(entry.Key) == permanent[entry.Key], "Cooking from the carried bag must not also consume permanent resources.");
        }
        Debug.Log("FINAL_RESTAURANT_CONSUMPTION_PASS");
    }

    private static void PrepareRealLayer3()
    {
        ClearInventory();
        Check(InventoryManager.instance.AddItemPartial(Food, 4) == 4, "Carry four food units into Layer3.");
        unlockRecipe = RestaurantPanel.instance.dishRecipes.Find(recipe => recipe != null && recipe.locked && recipe.dishIcon != null);
        if (unlockRecipe == null) unlockRecipe = RestaurantPanel.instance.dishRecipes.Find(recipe => recipe != null && recipe.locked);
        Check(unlockRecipe != null, "The configured restaurant must provide a genuinely locked recipe for the first-unlock regression.");
        WeaponStatsManager.Instance.SetPetEnabled(PetType.FlyingCompanion, false);
        WeaponStatsManager.Instance.deathRetentionRate = 0.1f;
        woodBaseline = GameValManager.Instance.GetResourceCount(Wood);
        Check(woodBaseline <= int.MaxValue - 11, "The isolated permanent wood counter must have space.");
        ResourceItem wood = GameValManager.Instance.GetResourceInfo(Wood);
        wood.maxCapacity = Math.Max(wood.maxCapacity, woodBaseline + 11);
        Check(LevelManager.instance.TryEnterLevel(Level), "Layer3 must be entered through the actual scene lifecycle.");
        SetPhase("layer3-first", 60);
    }

    private static void ValidateSwitchFailureAndStart()
    {
        InventoryManager inventory = InventoryManager.instance;
        LevelManager levels = LevelManager.instance;
        RunSessionManager runs = RunSessionManager.Instance;
        Scene originalScene = SceneManager.GetSceneByName(Level);
        TopDownController player = FindPlayer();
        firstLayer3Handle = originalScene.handle;
        beforeNormalSwitch = inventory.CaptureInventory();
        runs.RecordKill();
        Check(runs.CaptureCurrentResult().KillCount == 1, "The switch fixture must give the source run a nonzero statistic to reset.");
        RunResultSnapshot previousResult = runs.LastResult;
        float previousTimeScale = Time.timeScale;
        FieldInfo field = typeof(InventoryManager).GetField("slots", BindingFlags.Instance | BindingFlags.NonPublic);
        var slots = field == null ? null : field.GetValue(inventory) as List<InventoryItemUI>;
        Check(slots != null && slots.Count > 0 && slots[0] != null, "The switch failure fixture needs the real first inventory slot.");
        InventoryItemUI firstSlot = slots[0];
        observedSwitchSnapshotFailure = false;
        injectingSwitchSnapshotFailure = true;
        try
        {
            slots[0] = null;
            Debug.Log("FINAL_EXPECTED_SWITCH_SNAPSHOT_FAILURE");
            levels.SwitchLevel(Level, SwitchLevelName);
        }
        finally
        {
            slots[0] = firstSlot;
            injectingSwitchSnapshotFailure = false;
        }
        Check(observedSwitchSnapshotFailure, "The injected switch must reach and report the snapshot-validation failure.");
        Scene unchanged = SceneManager.GetSceneByName(Level);
        Scene untouchedDestination = SceneManager.GetSceneByName(SwitchLevelName);
        Check(unchanged.IsValid() && unchanged.isLoaded && unchanged.handle == originalScene.handle &&
            (!untouchedDestination.IsValid() || !untouchedDestination.isLoaded) && levels.CurrentLevelId == Level && !levels.IsTransitioning(),
            "A failed switch snapshot must neither unload the source nor start loading the destination.");
        Check(runs.IsActive && runs.Phase == RunEndPhase.Exploring && ReferenceEquals(previousResult, runs.LastResult) &&
            runs.CaptureCurrentResult().KillCount == 1 && Time.timeScale == previousTimeScale && BattleValManager.Instance.IsActive &&
            PlayerStateManager.instance.currentState == PlayerState.Battle && player.enabled && !player.isDead,
            "Snapshot failure must leave the current run, result, battle consumption, input and time unchanged.");
        CheckSameInventory(beforeNormalSwitch, inventory.CaptureInventory());
        GameplaySceneValidation.CheckSceneBindings(unchanged);
        Debug.Log("FINAL_NORMAL_SWITCH_SNAPSHOT_FAILURE_PASS");
        levels.SwitchLevel(Level, SwitchLevelName);
        Check(levels.IsTransitioning(), "The same source run must still accept a normal switch after the failed snapshot.");
        Check(runs.LastResult != null && runs.LastResult.LevelId == Level && runs.LastResult.KillCount == 1,
            "The accepted switch must complete the original run exactly once.");
        SetPhase("switch-layer2", 60);
    }

    private static void ValidateLayer2AndSwitchBack()
    {
        CheckNormalSwitchDestination(SwitchLevelName, Level);
        RunSessionManager.Instance.RecordKill();
        Check(RunSessionManager.Instance.CaptureCurrentResult().KillCount == 1,
            "Layer2 must record its own statistic independently of the completed Layer3 run.");
        LevelManager.instance.SwitchLevel(SwitchLevelName, Level);
        Check(LevelManager.instance.IsTransitioning(), "The real Layer2 run must accept switching back to Layer3.");
        SetPhase("switch-layer3", 60);
    }

    private static void ValidateSwitchReturnAndContinue()
    {
        CheckNormalSwitchDestination(Level, SwitchLevelName);
        Check(SceneManager.GetSceneByName(Level).handle != firstLayer3Handle, "Switching back must create a fresh Layer3 scene instance.");
        Debug.Log("FINAL_NORMAL_SWITCH_ROUND_TRIP_PASS");
        UnlockAndDie();
    }

    private static void CheckNormalSwitchDestination(string destination, string unloadedSource)
    {
        Scene source = SceneManager.GetSceneByName(unloadedSource);
        Check(!source.IsValid() || !source.isLoaded, "A normal switch must unload its source scene before continuing.");
        RunResultSnapshot current = RunSessionManager.Instance.CaptureCurrentResult();
        Check(LevelManager.instance.CurrentLevelId == destination && RunSessionManager.Instance.IsActive &&
            RunSessionManager.Instance.Phase == RunEndPhase.Exploring && RunSessionManager.Instance.LastResult == null &&
            current != null && current.LevelId == destination && current.IngredientDelta == 0 && current.KillCount == 0 &&
            current.GatheredCounts.Count == 0 && current.NewRecipeIds.Count == 0 && current.NewPetTypes.Count == 0,
            "Each normal switch must begin the correct destination run with a fresh baseline and cleared per-run statistics.");
        CheckSameInventory(beforeNormalSwitch, InventoryManager.instance.CaptureInventory());
        Check(GameValManager.Instance.GetResourceCount(Wood) == woodBaseline && unlockRecipe.locked &&
            !WeaponStatsManager.Instance.IsPetEnabled(PetType.FlyingCompanion),
            "The switch regression must preserve resources and leave the later first-unlock fixture untouched.");
    }

    private static void UnlockAndDie()
    {
        Check(InventoryManager.instance.GetItemCount(Food) == 4, "Layer3 entry preserves the carried inventory.");
        RestaurantPanel.instance.UnlockDishByID(unlockRecipe.dishID);
        RestaurantPanel.instance.UnlockDishByID(unlockRecipe.dishID);
        WeaponStatsManager.Instance.SetPetEnabled(PetType.FlyingCompanion, true);
        WeaponStatsManager.Instance.SetPetEnabled(PetType.FlyingCompanion, true);
        RunResultSnapshot current = RunSessionManager.Instance.CaptureCurrentResult();
        Check(CountRecipe(current.NewRecipeIds, unlockRecipe.dishID) == 1 && CountPet(current.NewPetTypes, PetType.FlyingCompanion) == 1,
            "Repeated real unlock calls must record the first new recipe and pet only once.");
        Check(InventoryManager.instance.AddRunGathered(Wood, 11) == 11, "The death presentation has eleven gathered wood units.");
        TopDownController player = FindPlayer();
        MovePlayer(player, player.transform.position + Vector3.right * 10f);
        player.Die();
        CheckSettlementState();
        Check(InventoryManager.instance.GetItemCount(Food) == 1 && RunSessionManager.Instance.LastResult.IngredientDelta == -3,
            "Layer3 death applies ten-percent retention to the four carried food units.");
        deathRecord = RunSessionManager.Instance.PendingDeathCrate;
        Check(deathRecord != null && deathRecord.LevelId == Level && Count(deathRecord.Ingredients, Food) == 3 && Count(deathRecord.Gathered, Wood) == 9,
            "Layer3 death must create its recoverable three-food, nine-wood record.");
        Check(GameValManager.Instance.GetResourceCount(Wood) == woodBaseline + 2, "Only the two retained wood units are permanently credited on death.");
        Check(CountRecipe(RunSessionManager.Instance.LastResult.NewRecipeIds, unlockRecipe.dishID) == 1 &&
            CountPet(RunSessionManager.Instance.LastResult.NewPetTypes, PetType.FlyingCompanion) == 1,
            "Death settlement preserves each new unlock exactly once.");
        beforePresentation = InventoryManager.instance.CaptureInventory();
        SetPhase("death-visible", 10, 0.3);
    }

    private static void ValidateDeathPresentationAndRetry()
    {
        SettlementUIController view = View();
        CheckSettlementState();
        CheckSameInventory(beforePresentation, InventoryManager.instance.CaptureInventory());
        CheckIndependentInput(view);
        FieldInfo catalog = typeof(SettlementUIController).GetField("presentationCatalog", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Check(catalog != null && catalog.GetValue(view) != null, "The real settlement must use its presentation catalog.");
        var presentation = catalog.GetValue(view) as SettlementPresentationCatalog;
        SettlementPresentationCatalog.ResourcePresentation woodPresentation;
        SettlementPresentationCatalog.PetPresentation petPresentation;
        Check(presentation != null && presentation.TryGetResource(Wood, out woodPresentation) && woodPresentation.icon != null,
            "The catalog must provide the dedicated gathered-wood icon.");
        Check(presentation.TryGetPet(PetType.FlyingCompanion, out petPresentation) && petPresentation.icon != null,
            "The catalog must provide the flying companion's icon.");
        Check(view.petSection.activeSelf && view.recipeSection.activeSelf && view.gatheredSection.activeSelf,
            "Death settlement must show the new pet, new recipe and gathered resource categories.");
        ValidateNewRows(view.petContent, RunSessionManager.Instance.LastResult.NewPetTypes.Count);
        ValidateNewRows(view.recipeContent, RunSessionManager.Instance.LastResult.NewRecipeIds.Count);
        List<RectTransform> gathered = ActiveEntries(view.gatheredContent);
        Check(gathered.Count == 1 && HasVisibleIcon(gathered[0]), "The gathered wood row must have its actual icon.");
        presentation.TryGetResource(Wood, out woodPresentation);
        presentation.TryGetPet(PetType.FlyingCompanion, out petPresentation);
        Check(gathered[0].Find("Icon").GetComponent<Image>().sprite == woodPresentation.icon &&
            ActiveEntries(view.petContent)[0].Find("Icon").GetComponent<Image>().sprite == petPresentation.icon,
            "Wood and pet rows must display their dedicated catalog sprites.");
        Text gained = gathered[0].Find("Gain").GetComponent<Text>();
        Text owned = gathered[0].Find("Total").GetComponent<Text>();
        Text outcome = gathered[0].Find("Outcome").GetComponent<Text>();
        Check(gained.text == "\u672c\u5c40 +11" && owned.text == "\u62e5\u6709 " + (woodBaseline + 2).ToString(CultureInfo.InvariantCulture) &&
            outcome.gameObject.activeInHierarchy && outcome.text.Replace(" ", "") == "\u5e26\u51fa2\u00b7\u9057\u59319",
            "Death must distinguish eleven gathered units, two carried out, nine lost and the permanent owned total.");
        Transform statistics = view.transform.Find("Panel/Statistics");
        Check(statistics != null && view.ingredientDeltaText.transform.IsChildOf(statistics) &&
            view.durationText.transform.IsChildOf(statistics) && view.killsText.transform.IsChildOf(statistics),
            "All three summary statistics must belong to the fixed Panel/Statistics area.");
        Check(!view.ingredientDeltaText.transform.IsChildOf(view.detailsContent) &&
            !view.durationText.transform.IsChildOf(view.detailsContent) && !view.killsText.transform.IsChildOf(view.detailsContent),
            "The three summary statistics must stay outside the reward scroll content.");
        Check(view.ingredientDeltaText.text.Contains("-3") && view.ingredientDeltaText.color.r > view.ingredientDeltaText.color.g,
            "The negative carried-food delta must be displayed in red.");
        Debug.Log("FINAL_REAL_UNLOCKS_AND_DEATH_PRESENTATION_PASS");
        view.retryButton.onClick.Invoke();
        Check(RunSessionManager.Instance.Phase == RunEndPhase.Transitioning, "The actual Layer3 retry button must start the transition.");
        SetPhase("layer3-retry", 60);
    }

    private static void RecoverAndExtract()
    {
        RunResultSnapshot fresh = RunSessionManager.Instance.CaptureCurrentResult();
        Check(fresh.NewRecipeIds.Count == 0 && fresh.NewPetTypes.Count == 0 && !unlockRecipe.locked &&
            WeaponStatsManager.Instance.IsPetEnabled(PetType.FlyingCompanion), "The next real run retains unlocks without marking them new again.");
        RestaurantPanel.instance.UnlockDishByID(unlockRecipe.dishID);
        WeaponStatsManager.Instance.SetPetEnabled(PetType.FlyingCompanion, true);
        fresh = RunSessionManager.Instance.CaptureCurrentResult();
        Check(fresh.NewRecipeIds.Count == 0 && fresh.NewPetTypes.Count == 0, "Repeated known unlock events in the next run must not recreate NEW entries.");
        DeathLootCrate crate = RunSessionManager.Instance.ActiveDeathCrate;
        Check(crate != null && RunSessionManager.Instance.PendingDeathCrate.Id == deathRecord.Id && crate.gameObject.scene.name == Level,
            "Retry must restore the Layer3 crate in its own newly loaded scene.");
        TopDownController player = FindPlayer();
        Check(!crate.TryCollect(player), "The restored Layer3 crate remains beyond the fresh spawn's collection range.");
        MovePlayer(player, crate.transform.position);
        Check(crate.TryCollect(player), "Approaching the real Layer3 crate recovers its contents.");
        Check(InventoryManager.instance.GetItemCount(Food) == 4 && InventoryManager.instance.GetRunGatheredCount(Wood) == 9,
            "Layer3 recovery restores food and adds the dropped wood to the current run.");
        Check(RunSessionManager.Instance.TryEndRun(RunEndReason.Extracted), "Layer3 recovery must support a normal extraction.");
        Check(RunSessionManager.Instance.LastResult.NewRecipeIds.Count == 0 && RunSessionManager.Instance.LastResult.NewPetTypes.Count == 0,
            "The second settlement has no repeated NEW rewards.");
        beforePresentation = InventoryManager.instance.CaptureInventory();
        RunResultSnapshot result = RunSessionManager.Instance.LastResult;
        var owned = new Dictionary<ResourceType, int> { { Wood, GameValManager.Instance.GetResourceCount(Wood) } };
        View().Show(result, RunEndReason.Extracted, owned, () => RunSessionManager.Instance.RetryExploration(), () => RunSessionManager.Instance.ReturnToTown());
        View().Show(result, RunEndReason.Extracted, owned, () => RunSessionManager.Instance.RetryExploration(), () => RunSessionManager.Instance.ReturnToTown());
        SetPhase("extraction-visible", 10, 0.3);
    }

    private static void ValidateSecondResultAndReturn()
    {
        CheckSettlementState();
        SettlementUIController view = View();
        CheckSameInventory(beforePresentation, InventoryManager.instance.CaptureInventory());
        Check(ActiveEntries(view.inventoryContent).Count == InventoryManager.instance.GetSlotCount(),
            "Repeated rendering of a real result must not duplicate its inventory cells.");
        Check(!view.petSection.activeSelf && !view.recipeSection.activeSelf, "Known pet and recipe sections must hide in the next result.");
        Check(GameValManager.Instance.GetResourceCount(Wood) == woodBaseline + 11,
            "Re-showing settlement cannot duplicate the retained and recovered wood credit.");
        CheckIndependentInput(view);
        Debug.Log("FINAL_LAYER3_RECOVERY_AND_NEW_BASELINE_PASS");
        view.homeButton.onClick.Invoke();
        Check(RunSessionManager.Instance.Phase == RunEndPhase.Transitioning, "The actual home button must leave Layer3.");
        SetPhase("home", 60);
    }

    private static void ValidateHome()
    {
        Check(InventoryManager.instance.GetItemCount(Food) == 4 && GameValManager.Instance.GetResourceCount(Wood) == woodBaseline + 11,
            "Layer3 return home must retain the recovered resources exactly once.");
        Check(RunSessionManager.Instance.PendingDeathCrate == null && RunSessionManager.Instance.ActiveDeathCrate == null,
            "The consumed Layer3 crate must remain cleared at home.");
        Check(Time.timeScale > 0 && PlayerStateManager.instance.currentState == PlayerState.UpGround && !View().IsVisible,
            "The final home destination must restore gameplay state and close settlement.");
        Finish(true, null);
    }

    private static void ValidateNewRows(Transform parent, int expected)
    {
        List<RectTransform> rows = ActiveEntries(parent);
        Check(rows.Count == expected, "Each newly unlocked reward must have exactly one visible row.");
        foreach (RectTransform row in rows)
        {
            Check(HasVisibleIcon(row), "Each new pet or recipe must have a visible icon.");
            RectTransform marker = row.Find("Icon/New") as RectTransform;
            RectTransform icon = row.Find("Icon") as RectTransform;
            Check(marker != null && marker.gameObject.activeInHierarchy, "Each newly unlocked reward must display its NEW marker.");
            Vector3 markerCenter = marker.TransformPoint(marker.rect.center);
            Vector3 iconCenter = icon.TransformPoint(icon.rect.center);
            Check(markerCenter.x < iconCenter.x && markerCenter.y > iconCenter.y, "NEW must be positioned at the icon's upper-left corner.");
        }
    }

    private static bool HasVisibleIcon(Transform row)
    {
        Transform iconTransform = row.Find("Icon");
        Image icon = iconTransform == null ? null : iconTransform.GetComponent<Image>();
        return icon != null && icon.isActiveAndEnabled && icon.sprite != null;
    }

    private static void CheckIndependentInput(SettlementUIController view)
    {
        Canvas canvas = view.GetComponent<Canvas>();
        Check(canvas != null && canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay,
            "Settlement must retain its independent root overlay canvas.");
        EventSystem input = EventSystem.current;
        Check(input != null && input.isActiveAndEnabled && input.transform.IsChildOf(view.transform), "The visible settlement must own independent input.");
        StandaloneInputModule module = input.GetComponent<StandaloneInputModule>();
        Check(module != null && module.isActiveAndEnabled, "Settlement must retain its actual pointer input module.");
        Canvas.ForceUpdateCanvases();
        RectTransform button = view.retryButton.GetComponent<RectTransform>();
        var pointer = new PointerEventData(input) { position = RectTransformUtility.WorldToScreenPoint(null, button.TransformPoint(button.rect.center)) };
        var hits = new List<RaycastResult>();
        input.RaycastAll(pointer, hits);
        Graphic graphic = view.retryButton.targetGraphic;
        Check(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(view.retryButton.transform),
            "The visible Retry button must win a real UI raycast. hits=" + hits.Count + ", first=" +
            (hits.Count == 0 ? "none" : hits[0].gameObject.name) + ", pointer=" + pointer.position +
            ", graphicDepth=" + (graphic == null ? -1 : graphic.depth) + ", canvasPixelRect=" + canvas.pixelRect);
    }

    private static void CheckSettlementState()
    {
        Check(RunSessionManager.Instance.Phase == RunEndPhase.ShowingResult && !RunSessionManager.Instance.IsActive &&
            Time.timeScale == 0f && PlayerStateManager.instance.currentState == PlayerState.Settlement && View().IsVisible,
            "The real run must be paused in a visible settlement.");
    }

    private static SettlementUIController View()
    {
        SettlementUIController view = UnityEngine.Object.FindObjectOfType<SettlementUIController>(true);
        if (view == null) view = SettlementUIController.EnsureExists(RunSessionManager.Instance.transform);
        Check(view != null, "The built settlement prefab must be available.");
        return view;
    }

    private static List<RectTransform> ActiveEntries(Transform parent)
    {
        var entries = new List<RectTransform>();
        foreach (Transform child in parent)
            if (child.gameObject.activeSelf && child is RectTransform) entries.Add((RectTransform)child);
        return entries;
    }

    private static Rect WorldBounds(RectTransform transform)
    {
        var corners = new Vector3[4];
        transform.GetWorldCorners(corners);
        return Rect.MinMaxRect(corners[0].x, corners[0].y, corners[2].x, corners[2].y);
    }

    private static void CheckSameInventory(InventorySnapshot expected, InventorySnapshot actual)
    {
        Check(expected.Slots.Count == actual.Slots.Count, "Presentation cannot change unlocked slot count.");
        for (int index = 0; index < expected.Slots.Count; index++)
        {
            InventorySlotSnapshot before = expected.Slots[index];
            InventorySlotSnapshot after = actual.Slots[index];
            Check(before.Index == after.Index && before.ItemType == after.ItemType && before.Count == after.Count && before.Capacity == after.Capacity,
                "Presentation cannot change, move or duplicate live inventory data.");
        }
    }

    private static void ClearInventory()
    {
        var empty = new List<InventorySlotSnapshot>();
        foreach (InventorySlotSnapshot slot in InventoryManager.instance.CaptureInventory().Slots)
            empty.Add(new InventorySlotSnapshot(slot.Index, ResourceType.None, 0, slot.Capacity));
        Check(InventoryManager.instance.ApplyInventorySnapshot(new InventorySnapshot(empty)), "The isolated fixture must clear through the inventory snapshot API.");
    }

    private static TopDownController FindPlayer(string level = Level)
    {
        Scene scene = SceneManager.GetSceneByName(level);
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (TopDownController player in root.GetComponentsInChildren<TopDownController>(true))
                if (player.gameObject.activeInHierarchy && player.CompareTag("Player")) return player;
        return null;
    }

    private static void MovePlayer(TopDownController player, Vector3 position)
    {
        Check(player != null, "Layer3 must provide its real player controller.");
        Rigidbody body = player.GetComponent<Rigidbody>();
        if (body != null) { body.velocity = Vector3.zero; body.position = position; }
        player.transform.position = position;
        Physics.SyncTransforms();
    }

    private static bool IsLevelReady(string level = Level)
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

    private static int Count(IReadOnlyDictionary<ResourceType, int> counts, ResourceType type) { int value; return counts.TryGetValue(type, out value) ? value : 0; }
    private static int CountRecipe(IReadOnlyList<int> values, int target) { int count = 0; foreach (int value in values) if (value == target) count++; return count; }
    private static int CountPet(IReadOnlyList<PetType> values, PetType target) { int count = 0; foreach (PetType value in values) if (value == target) count++; return count; }

    private static void SetPhase(string phase, double timeout, double wait = 0)
    {
        SessionState.SetString(PhaseKey, phase);
        SessionState.SetString(DeadlineKey, (EditorApplication.timeSinceStartup + timeout).ToString("R", CultureInfo.InvariantCulture));
        SessionState.SetString(ReadyKey, (EditorApplication.timeSinceStartup + wait).ToString("R", CultureInfo.InvariantCulture));
        Debug.Log("FINAL_GAMEPLAY_PHASE " + phase);
    }

    private static double ReadTime(string key) { double value; return double.TryParse(SessionState.GetString(key, "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : 0; }

    private static void OnRuntimeLog(string message, string stack, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        string phase = SessionState.GetString(PhaseKey, "");
        if (!EditorApplication.isPlaying || string.IsNullOrEmpty(phase) || phase == "exit") return;
        if (phase == "layer3-first" && injectingSwitchSnapshotFailure && type == LogType.Exception &&
            (message == SwitchSnapshotFailure || message == "InvalidOperationException: " + SwitchSnapshotFailure) &&
            stack.Contains("InventoryManager.CaptureInventory"))
        {
            observedSwitchSnapshotFailure = true;
            return;
        }
        if (string.IsNullOrEmpty(SessionState.GetString(ErrorKey, ""))) SessionState.SetString(ErrorKey, message + "\n" + stack);
    }

    private static void Finish(bool success, Exception error)
    {
        SessionState.SetInt(ResultKey, success ? 0 : 1);
        SessionState.SetString(PhaseKey, "exit");
        if (success) Debug.Log("FINAL_GAMEPLAY_REGRESSIONS_PASS");
        else Debug.LogError("FINAL_GAMEPLAY_REGRESSIONS_FAIL: " + error);
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
        else EditorApplication.delayCall += ExitEditor;
    }

    private static void ExitEditor()
    {
        if (SessionState.GetString(PhaseKey, "") != "exit") return;
        int code = SessionState.GetInt(ResultKey, 1);
        SessionState.EraseString(PhaseKey);
        EditorApplication.Exit(code);
    }

    private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
#endif
