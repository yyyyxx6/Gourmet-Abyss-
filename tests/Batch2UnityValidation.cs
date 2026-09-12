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
using GourmetAbyss.CameraSystem;
using Game.Modules;

// Run only in an isolated project; copy this and the four pure test files into Assets/Editor.
[InitializeOnLoad]
public static class Batch2UnityValidation
{
    private const string Prefix = "ChefDungeon.Batch2.";
    private const string PhaseKey = Prefix + "Phase";
    private const string ResultKey = Prefix + "Result";
    private const string DeadlineKey = Prefix + "Deadline";
    private const string ReadyKey = Prefix + "ReadyAt";
    private const string WoodBaselineKey = Prefix + "WoodBaseline";
    private const string FirstSceneKey = Prefix + "FirstSceneHandle";
    private const string FirstPlayerKey = Prefix + "FirstPlayerId";
    private const string SecondSceneKey = Prefix + "SecondSceneHandle";
    private const string LevelId = "Layer1";
    private const string UnexpectedErrorKey = Prefix + "UnexpectedError";
    private const ResourceType Food = ResourceType.LootMushroom;
    private const ResourceType Wood = ResourceType.LootPumkin;
    private static List<InventoryItemUI> injectedSlots;
    private static InventoryItemUI replacedSlot;
    private static RunResultSnapshot previousResult;

    static Batch2UnityValidation()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Application.logMessageReceived += OnRuntimeLog;
    }

    public static void Run()
    {
        try
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Validation must start outside Play Mode.");
            SettlementPanelBuilder.Build();
            DeathLootCrateBuilder.Build();
            InventoryPackingTests.Run();
            RunSessionDataTests.Run();
            RunResourceTests.Run();
            RunEndFlowTests.Run();
            Debug.Log("BATCH2_PURE_TESTS_PASS");

            PlayerSettings.companyName = "CodexValidation";
            PlayerSettings.productName = "ChefDungeonBatch2";
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
        if (state == PlayModeStateChange.EnteredPlayMode && phase == "enter-play")
            SetPhase("prepare", 30, 3);
        else if (state == PlayModeStateChange.EnteredEditMode && phase == "exit")
            EditorApplication.delayCall += ExitEditor;
        else if (state == PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(phase))
            Finish(false, new InvalidOperationException("Play Mode exited before validation completed: " + phase));
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
            string unexpectedError = SessionState.GetString(UnexpectedErrorKey, "");
            Check(string.IsNullOrEmpty(unexpectedError), "Unexpected runtime error: " + unexpectedError);
            double now = EditorApplication.timeSinceStartup;
            Check(now <= GetTime(DeadlineKey), "Timed out in phase " + phase + ". " + DescribeRuntime());
            if (!EditorApplication.isPlaying || now < GetTime(ReadyKey)) return;

            switch (phase)
            {
                case "prepare":
                    PrepareAndEnter();
                    break;
                case "wait-first-level":
                    if (IsLevelReady()) ValidateExtractionAndRetry();
                    break;
                case "wait-extraction-visible":
                    if (FindSettlementUI().canvasGroup.alpha >= 0.99f) ValidateExtractionVisibleAndRetry();
                    break;
                case "wait-failed-retry":
                    if (LevelManager.instance != null && !LevelManager.instance.IsTransitioning() &&
                        RunSessionManager.Instance != null && RunSessionManager.Instance.Phase == RunEndPhase.ShowingResult)
                        ValidateFailureRecoveryAndRetry();
                    break;
                case "wait-retry-level":
                    if (IsLevelReady()) ValidateRetryAndDie();
                    break;
                case "wait-after-death":
                    ValidateDeathWaitAndReturn();
                    break;
                case "wait-home":
                    if (IsHomeReady()) ValidateHomeAndFinish();
                    break;
            }
        }
        catch (Exception error)
        {
            Finish(false, new InvalidOperationException("Phase " + phase + ": " + DescribeRuntime(), error));
        }
    }

    private static void PrepareAndEnter()
    {
        InventoryManager inventory = InventoryManager.instance;
        Check(inventory != null && GameValManager.Instance != null && RunSessionManager.Instance != null &&
            LevelManager.instance != null && BattleValManager.Instance != null && PlayerStateManager.instance != null,
            "The real UpGround scene must initialize all run dependencies.");
        GameplaySceneValidation.CaptureHomeState();
        var emptySlots = new List<InventorySlotSnapshot>();
        int capacity = 0;
        foreach (InventorySlotSnapshot slot in inventory.CaptureInventory().Slots)
        {
            emptySlots.Add(new InventorySlotSnapshot(slot.Index, ResourceType.None, 0, slot.Capacity));
            capacity += slot.Capacity;
        }
        Check(capacity >= 3 && inventory.ApplyInventorySnapshot(new InventorySnapshot(emptySlots)),
            "The fixture needs at least three units of real food capacity.");
        inventory.ClearRunGathered();
        Check(inventory.AddItemPartial(Food, 1) == 1, "Carry one food unit into the first exploration.");

        int baseline = GameValManager.Instance.GetResourceCount(Wood);
        ResourceItem wood = GameValManager.Instance.GetResourceInfo(Wood);
        Check(wood != null && baseline <= int.MaxValue - 7, "Wood must have a usable permanent-storage record.");
        wood.maxCapacity = Math.Max(wood.maxCapacity, baseline + 7);
        SessionState.SetInt(WoodBaselineKey, baseline);
        Check(LevelManager.instance.TryEnterLevel(LevelId), "The real level-entry request must be accepted.");
        SetPhase("wait-first-level", 60);
    }

    private static bool IsLevelReady()
    {
        LevelManager levels = LevelManager.instance;
        RunSessionManager runs = RunSessionManager.Instance;
        Scene scene = SceneManager.GetSceneByName(LevelId);
        return levels != null && runs != null && scene.IsValid() && scene.isLoaded &&
            levels.CurrentLevelId == LevelId && !levels.IsTransitioning() && runs.IsActive &&
            runs.Phase == RunEndPhase.Exploring && FindPlayer(scene) != null && Time.timeScale > 0f &&
            GameplaySceneValidation.IsCameraFrameReady(scene);
    }

    private static void ValidateExtractionAndRetry()
    {
        InventoryManager inventory = InventoryManager.instance;
        RunSessionManager runs = RunSessionManager.Instance;
        Scene scene = SceneManager.GetSceneByName(LevelId);
        TopDownController player = FindPlayer(scene);
        CheckSceneBindings(scene);
        Check(player != null && player.enabled && !player.isDead, "The first level has an active player.");
        Check(inventory.GetItemCount(Food) == 1, "Entering a real level preserves carried food.");
        SessionState.SetInt(FirstSceneKey, scene.handle);
        SessionState.SetInt(FirstPlayerKey, player.GetInstanceID());

        Check(inventory.AddItemPartial(Food, 2) == 2, "Collect two food units in the first run.");
        Check(inventory.AddRunGathered(Wood, 7) == 7, "Collect seven gathered wood units in the first run.");
        Check(runs.TryEndRun(RunEndReason.Extracted), "Extraction must open the real settlement panel.");
        CheckSettlement(player, RunEndReason.Extracted);
        RunResultSnapshot result = runs.LastResult;
        Check(result != null && result.IngredientDelta == 2 && Count(result.GatheredCounts, Wood) == 7,
            "The extraction result must report net food +2 and gathered wood +7.");
        CheckWoodPaidOnce();
        Check(inventory.GetRunGatheredCounts().Count == 0, "Successful extraction commits pending gathered resources.");
        Check(!runs.TryEndRun(RunEndReason.Extracted) && !runs.TryEndRun(RunEndReason.Death),
            "Repeated extraction and death requests must be refused.");
        player.Die();
        Check(ReferenceEquals(result, runs.LastResult) && inventory.GetItemCount(Food) == 3,
            "Calling player death after extraction cannot replace or reapply the result.");
        CheckWoodPaidOnce();
        previousResult = result;
        SetPhase("wait-extraction-visible", 10, 0.3);
    }

    private static void ValidateExtractionVisibleAndRetry()
    {
        InventoryManager inventory = InventoryManager.instance;
        CheckSettlement(FindPlayer(SceneManager.GetSceneByName(LevelId)), RunEndReason.Extracted, true);
        Check(previousResult != null && ReferenceEquals(previousResult, RunSessionManager.Instance.LastResult),
            "Waiting for the visible result must not replace the completed result.");
        FieldInfo slotsField = typeof(InventoryManager).GetField("slots", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(slotsField != null, "The fault fixture must find the live inventory slot collection.");
        injectedSlots = slotsField.GetValue(inventory) as List<InventoryItemUI>;
        Check(injectedSlots != null && injectedSlots.Count > 0 && injectedSlots[0] != null,
            "The fault fixture needs an existing first slot to restore.");
        replacedSlot = injectedSlots[0];
        injectedSlots[0] = null;
        Debug.Log("BATCH2_EXPECTED_BEGIN_FAILURE: the first slot reference is temporarily missing; BeginRun must fail before releasing the transition gate.");
        StartRetryFromButton("wait-failed-retry", 15);
        Debug.Log("BATCH2_EXTRACTION_AND_BUTTON_GATE_PASS");
    }

    private static void ValidateFailureRecoveryAndRetry()
    {
        RunSessionManager runs = RunSessionManager.Instance;
        Scene scene = SceneManager.GetSceneByName(LevelId);
        TopDownController player = FindPlayer(scene);
        Check(scene.IsValid() && scene.isLoaded && scene.handle != SessionState.GetInt(FirstSceneKey, 0),
            "The injected failure must occur after a real replacement scene has loaded.");
        CheckSettlement(player, RunEndReason.Extracted, true);
        Check(previousResult != null && ReferenceEquals(previousResult, runs.LastResult) &&
            runs.LastResult.IngredientDelta == 2 && Count(runs.LastResult.GatheredCounts, Wood) == 7,
            "Failed BeginRun must preserve the original completed result.");
        SettlementUIController ui = FindSettlementUI();
        Check(ui.retryButton.interactable && ui.homeButton.interactable && ui.canvasGroup.interactable,
            "Both destinations must become actionable after a failed retry.");
        Check(ui.statusText != null && ui.statusText.gameObject.activeInHierarchy &&
            !string.IsNullOrWhiteSpace(ui.statusText.text), "Failed loading must leave a visible status message.");
        CheckWoodPaidOnce();
        Check(injectedSlots != null && injectedSlots.Count > 0 && injectedSlots[0] == null && replacedSlot != null,
            "The injected missing reference must remain in place until recovery has been verified.");

        RestoreInjectedSlot();
        Check(InventoryManager.instance.GetItemCount(Food) == 3,
            "Restoring the original slot reference must retain all three carried food units.");
        SessionState.SetInt(FirstSceneKey, scene.handle);
        SessionState.SetInt(FirstPlayerKey, player.GetInstanceID());
        previousResult = null;
        Debug.Log("BATCH2_FAILURE_RECOVERY_PASS");
        StartRetryFromButton("wait-retry-level", 60);
    }

    private static void StartRetryFromButton(string nextPhase, double timeout)
    {
        RunSessionManager runs = RunSessionManager.Instance;
        SettlementUIController ui = FindSettlementUI();
        Check(ui.retryButton != null && ui.retryButton.interactable, "The actual retry button must be available.");
        ui.retryButton.onClick.Invoke();
        Check(runs.Phase == RunEndPhase.Transitioning, "The actual retry button must acquire the transition gate.");
        Check(!runs.ReturnToTown(), "The other destination cannot start while retry is transitioning.");
        Check(!ui.retryButton.interactable && !ui.homeButton.interactable, "Both result buttons lock during a transition.");
        SetPhase(nextPhase, timeout);
    }

    private static void ValidateRetryAndDie()
    {
        RunSessionManager runs = RunSessionManager.Instance;
        InventoryManager inventory = InventoryManager.instance;
        Scene scene = SceneManager.GetSceneByName(LevelId);
        TopDownController player = FindPlayer(scene);
        Check(scene.handle != SessionState.GetInt(FirstSceneKey, 0), "Retry must load a new scene instance.");
        Check(player != null && player.GetInstanceID() != SessionState.GetInt(FirstPlayerKey, 0),
            "Retry must create a new player instance.");
        Check(player.enabled && !player.isDead && Time.timeScale > 0f && BattleValManager.Instance.IsActive,
            "Retry restores player control, time and battle consumption.");
        Check(inventory.GetItemCount(Food) == 3, "Retry preserves all three carried food units.");
        CheckWoodPaidOnce();
        Check(runs.LastResult == null, "A new exploration clears the previous result.");
        RunResultSnapshot next = runs.CaptureCurrentResult();
        Check(next != null && next.LevelId == LevelId && next.IngredientDelta == 0 && next.KillCount == 0 &&
            next.GatheredCounts.Count == 0 && next.NewPetTypes.Count == 0 && next.NewRecipeIds.Count == 0 &&
            next.ElapsedSeconds >= 0 && next.ElapsedSeconds < 1,
            "Retry resets run statistics and captures the retained food as the new baseline.");
        Check(!FindSettlementUI().IsVisible, "The result panel hides after a successful retry.");
        CheckSceneBindings(scene);
        SessionState.SetInt(SecondSceneKey, scene.handle);

        player.Die();
        CheckSettlement(player, RunEndReason.Death);
        Check(inventory.GetItemCount(Food) == 1 && runs.LastResult.IngredientDelta == -2,
            "Default ten-percent retention keeps one of three carried food units and reports the loss as -2.");
        CheckWoodPaidOnce();
        Debug.Log("BATCH2_RETRY_REFRESH_AND_DEATH_RESULT_PASS");
        SetPhase("wait-after-death", 15, 1.5);
    }

    private static void ValidateDeathWaitAndReturn()
    {
        RunSessionManager runs = RunSessionManager.Instance;
        Scene scene = SceneManager.GetSceneByName(LevelId);
        Check(scene.IsValid() && scene.isLoaded && scene.handle == SessionState.GetInt(SecondSceneKey, 0),
            "Death must not automatically unload the level after the old one-second delay.");
        Check(LevelManager.instance.CurrentLevelId == LevelId && !LevelManager.instance.IsTransitioning(),
            "Death waits for a destination selection.");
        CheckSettlement(FindPlayer(scene), RunEndReason.Death, true);
        SettlementUIController ui = FindSettlementUI();
        Check(ui.homeButton != null && ui.homeButton.interactable, "The actual return-home button is available.");
        ui.homeButton.onClick.Invoke();
        Check(runs.Phase == RunEndPhase.Transitioning, "The home button must acquire the transition gate.");
        Check(!runs.RetryExploration(), "Retry is rejected while the return-home transition owns the gate.");
        SetPhase("wait-home", 60);
    }

    private static bool IsHomeReady()
    {
        LevelManager levels = LevelManager.instance;
        RunSessionManager runs = RunSessionManager.Instance;
        Scene home = SceneManager.GetSceneByName("UpGround");
        return levels != null && runs != null && home.IsValid() && home.isLoaded &&
            string.IsNullOrEmpty(levels.CurrentLevelId) && !levels.IsTransitioning() &&
            runs.Phase == RunEndPhase.Inactive && !runs.IsActive && GameplaySceneValidation.IsCameraFrameReady(home);
    }

    private static void ValidateHomeAndFinish()
    {
        Scene home = SceneManager.GetSceneByName("UpGround");
        TopDownController player = FindPlayer(home);
        Check(player != null && player.enabled && !player.isDead && player.canPlayerMove,
            "Returning home restores the home player's controller.");
        Check(Time.timeScale > 0f && PlayerStateManager.instance.currentState == PlayerState.UpGround &&
            !BattleValManager.Instance.IsActive, "Home restores time and the non-battle player state.");
        Check(!FindSettlementUI().IsVisible && !RunSessionManager.Instance.IsEndingRun,
            "The result panel and its run-ending gate close on return home.");
        Scene oldLevel = SceneManager.GetSceneByName(LevelId);
        Check(!oldLevel.IsValid() || !oldLevel.isLoaded, "Returning home unloads the old level.");
        CheckSceneBindings(home);
        CheckWoodPaidOnce();
        Check(!RunSessionManager.Instance.TryEndRun(RunEndReason.Death), "An inactive home run cannot settle again.");
        Debug.Log("BATCH2_RETURN_HOME_BINDINGS_PASS");
        Finish(true, null);
    }

    private static void CheckSettlement(TopDownController player, RunEndReason reason, bool checkPointer = false)
    {
        RunSessionManager runs = RunSessionManager.Instance;
        Check(runs.Phase == RunEndPhase.ShowingResult && !runs.IsActive && runs.IsEndingRun,
            "The completed run must be showing its result.");
        Check(Time.timeScale == 0f && PlayerStateManager.instance.currentState == PlayerState.Settlement,
            "Settlement pauses game time and enters the settlement player state.");
        Check(!BattleValManager.Instance.IsActive && player != null && !player.enabled,
            "Settlement stops battle consumption and player control.");
        FieldInfo field = typeof(RunSessionManager).GetField("endFlow", BindingFlags.Instance | BindingFlags.NonPublic);
        Check(field != null && ((RunEndFlow)field.GetValue(runs)).EndReason == reason,
            "The result must preserve the accepted end reason.");
        SettlementUIController ui = FindSettlementUI();
        Check(ui.IsVisible, "The instantiated result panel must be visible.");
        Canvas canvas = ui.GetComponent<Canvas>();
        Check(canvas != null && canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay,
            "Settlement must use an independent root overlay canvas that survives scene-camera unloading.");
        EventSystem current = EventSystem.current;
        Check(current != null && current.isActiveAndEnabled && current.transform.IsChildOf(ui.transform),
            "Settlement must own active input independently of the unloaded scene.");
        StandaloneInputModule input = current.GetComponent<StandaloneInputModule>();
        Check(input != null && input.isActiveAndEnabled, "Settlement pointer and keyboard input must remain available.");
        if (checkPointer)
        {
            Canvas.ForceUpdateCanvases();
            RectTransform retryRect = ui.retryButton.GetComponent<RectTransform>();
            var pointer = new PointerEventData(current)
            {
                position = RectTransformUtility.WorldToScreenPoint(null, retryRect.TransformPoint(retryRect.rect.center))
            };
            var hits = new List<RaycastResult>();
            current.RaycastAll(pointer, hits);
            UnityEngine.UI.Graphic graphic = ui.retryButton.targetGraphic;
            Check(hits.Count > 0 && hits[0].gameObject.transform.IsChildOf(ui.retryButton.transform),
                "A real UI raycast at Retry must hit the settlement button ahead of the world and HUD. First hit: " +
                (hits.Count == 0 ? "none" : hits[0].gameObject.name) + ", hits=" + hits.Count +
                ", pointer=" + pointer.position + ", graphicDepth=" + (graphic == null ? "missing" : graphic.depth.ToString()) +
                ", canvasPixelRect=" + canvas.pixelRect);
        }
    }

    private static void CheckSceneBindings(Scene expected)
    {
        GameplaySceneValidation.CheckSceneBindings(expected);
    }

    private static TopDownController FindPlayer(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (TopDownController controller in root.GetComponentsInChildren<TopDownController>(true))
                if (controller.gameObject.activeInHierarchy) return controller;
        }
        return null;
    }

    private static SettlementUIController FindSettlementUI()
    {
        SettlementUIController ui = UnityEngine.Object.FindObjectOfType<SettlementUIController>(true);
        Check(ui != null, "SettlementUIController must be instantiated from the built prefab.");
        return ui;
    }

    private static void CheckWoodPaidOnce()
    {
        Check(GameValManager.Instance.GetResourceCount(Wood) == SessionState.GetInt(WoodBaselineKey, 0) + 7,
            "The seven gathered wood units must be credited exactly once and retained across later runs.");
    }

    private static int Count(IReadOnlyDictionary<ResourceType, int> values, ResourceType type)
    {
        int count;
        return values.TryGetValue(type, out count) ? count : 0;
    }

    private static void SetPhase(string phase, double timeout, double wait = 0)
    {
        SessionState.SetString(PhaseKey, phase);
        SetTime(DeadlineKey, EditorApplication.timeSinceStartup + timeout);
        SetTime(ReadyKey, EditorApplication.timeSinceStartup + wait);
        Debug.Log("BATCH2_PHASE " + phase);
    }

    private static void SetTime(string key, double value)
    {
        SessionState.SetString(key, value.ToString("R", CultureInfo.InvariantCulture));
    }

    private static double GetTime(string key)
    {
        double value;
        return double.TryParse(SessionState.GetString(key, "0"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out value) ? value : 0;
    }

    private static string DescribeRuntime()
    {
        LevelManager levels = LevelManager.instance;
        RunSessionManager runs = RunSessionManager.Instance;
        return "level=" + (levels == null ? "missing" : levels.CurrentLevelId) +
            ", transition=" + (levels != null && levels.IsTransitioning()) +
            ", runPhase=" + (runs == null ? "missing" : runs.Phase.ToString()) +
            ", active=" + (runs != null && runs.IsActive) + ", timeScale=" + Time.timeScale;
    }

    private static void Finish(bool success, Exception error)
    {
        RestoreInjectedSlot();
        previousResult = null;
        SessionState.SetInt(ResultKey, success ? 0 : 1);
        SessionState.SetString(PhaseKey, "exit");
        if (success) Debug.Log("BATCH2_UNITY_SCENE_TESTS_PASS");
        else Debug.LogError("BATCH2_UNITY_SCENE_TESTS_FAIL: " + error);
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode();
        else EditorApplication.delayCall += ExitEditor;
    }

    private static void RestoreInjectedSlot()
    {
        if (injectedSlots != null && injectedSlots.Count > 0 && replacedSlot != null)
            injectedSlots[0] = replacedSlot;
        injectedSlots = null;
        replacedSlot = null;
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

    private static void OnRuntimeLog(string message, string stackTrace, LogType type)
    {
        if (type != LogType.Error && type != LogType.Exception && type != LogType.Assert) return;
        string phase = SessionState.GetString(PhaseKey, "");
        if (!EditorApplication.isPlaying || string.IsNullOrEmpty(phase) || phase == "exit") return;
        if (phase == "wait-failed-retry" && message.Contains("An unlocked inventory slot is missing or locked.")) return;
        if (string.IsNullOrEmpty(SessionState.GetString(UnexpectedErrorKey, "")))
            SessionState.SetString(UnexpectedErrorKey, message + "\n" + stackTrace);
    }
}

// Shared by Batch1/3 and FinalGameplayValidation; include this file when copying those runners.
public static class GameplaySceneValidation
{
    private static int inventoryId;
    private static int levelManagerId;
    private static int gameValuesId;
    private static int weaponStatsId;
    private static int restaurantId;
    private static int homeDirectorId;
    private static int observedSceneHandle;
    private static int observedFrame;

    public static void CaptureHomeState()
    {
        Check(InventoryManager.Instance != null && LevelManager.Instance != null && GameValManager.Instance != null &&
            WeaponStatsManager.Instance != null && RestaurantPanel.Instance != null,
            "The upstream singleton framework must initialize the real persistent gameplay services.");
        inventoryId = InventoryManager.Instance.GetInstanceID();
        levelManagerId = LevelManager.Instance.GetInstanceID();
        gameValuesId = GameValManager.Instance.GetInstanceID();
        weaponStatsId = WeaponStatsManager.Instance.GetInstanceID();
        restaurantId = RestaurantPanel.Instance.GetInstanceID();
        Camera camera = Camera.main;
        CameraDirector director = camera == null ? null : camera.GetComponent<CameraDirector>();
        Check(director != null && camera.gameObject.scene.name == "UpGround", "The real home scene must provide its authored camera director.");
        homeDirectorId = director.GetInstanceID();
        observedSceneHandle = 0;
        observedFrame = -1;
        CheckSceneBindings(SceneManager.GetSceneByName("UpGround"));
    }

    public static bool IsCameraFrameReady(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return false;
        if (observedSceneHandle != scene.handle)
        {
            observedSceneHandle = scene.handle;
            observedFrame = Time.frameCount;
            return false;
        }
        // The scene-ready callback may precede CameraDirector.LateUpdate; observe a completed frame first.
        return Time.frameCount > observedFrame + 1;
    }

    public static void CheckSceneBindings(Scene expected)
    {
        CheckCoreSingletons();
        Check(expected.IsValid() && expected.isLoaded && SceneManager.GetActiveScene().handle == expected.handle,
            "The selected destination must be the active loaded scene.");
        Camera camera = Camera.main;
        Check(camera != null && camera.isActiveAndEnabled && camera.gameObject.scene.handle == expected.handle,
            "Camera.main must belong to the current destination scene.");
        SceneTitle title = SceneTitle.Resolve(expected.name);
        Check(title != null && title.gameObject.scene.handle == expected.handle && SceneTitle.Current == title && SceneTitle.instance == title,
            "SceneTitle resolution and its legacy alias must select the destination, including home fallback after unloading.");
        Check(LevelManager.Instance.TitleText != null && LevelManager.Instance.TitleText.text == SceneTitle.ResolveName(expected.name),
            "The visible level title must match the destination scene.");

        CameraDirector director = camera.GetComponent<CameraDirector>();
        CameraFollow follow = camera.GetComponent<CameraFollow>();
        CameraSceneContext context = camera.GetComponent<CameraSceneContext>();
        Check(director != null && director.isActiveAndEnabled && director.Camera == camera && CameraService.Active == director,
            "CameraService must select the live destination's sole camera writer.");
        Check(follow != null && follow.isActiveAndEnabled && follow.Director == director && context != null && context.Director == director,
            "The legacy follow adapter and scene context must bind to that same destination director.");
        Transform target = follow.DefaultTarget;
        Check(target != null && target.gameObject.scene.handle == expected.handle && context.DefaultTarget == target &&
            target.GetComponentInParent<TopDownController>() != null,
            "The camera's base target must be the new scene's player, never the unloaded player.");
        Check(director.RequestCount > 0 && director.ActiveRequestId >= 0,
            "A completed destination frame must have a valid active camera request.");
        Check(camera.orthographic == !director.CurrentPose.Perspective &&
            Mathf.Abs(camera.fieldOfView - director.CurrentPose.FieldOfView) < 0.01f,
            "The rendered projection must agree with the director's current logical pose.");
        CheckNoExpiredShotOwners(director);

        if (expected.name == "UpGround")
        {
            Check(director.GetInstanceID() == homeDirectorId,
                "Returning home must reactivate the original home director rather than rebuilding or retaining a dungeon director.");
        }
        else
        {
            FieldInfo profileField = typeof(CameraFollow).GetField("dungeonProfile", BindingFlags.Instance | BindingFlags.NonPublic);
            DungeonPerspectiveProfile profile = profileField == null ? null : profileField.GetValue(follow) as DungeonPerspectiveProfile;
            Check(profile != null, "The authored dungeon must retain its upstream perspective profile.");
            Check(!camera.orthographic && director.CurrentPose.Perspective && camera.nearClipPlane > 0f &&
                Mathf.Abs(camera.fieldOfView - profile.fieldOfView) < 0.01f,
                "Dungeon entry and retry must preserve the configured perspective lens and a positive near clip plane.");
            CheckProjectionFollowers(camera, expected);
        }

        KeepMainCamera binding = KeepMainCamera.Instance;
        Check(binding != null && KeepMainCamera.instance == binding && binding.mainUICanvas != null && binding.mainUICanvas.worldCamera == camera,
            "The persistent main UI must use the destination camera through the upstream singleton alias.");
        Check(binding.transitionAnimator != null && binding.transitionAnimator.mainCamera == camera,
            "The transition effect must use the destination camera.");
    }

    private static void CheckCoreSingletons()
    {
        Check(InventoryManager.Instance != null && InventoryManager.instance == InventoryManager.Instance &&
            InventoryManager.Instance.GetInstanceID() == inventoryId, "Scene transitions must retain the same inventory singleton and legacy alias.");
        Check(LevelManager.Instance != null && LevelManager.instance == LevelManager.Instance && LevelManager.Instance.GetInstanceID() == levelManagerId,
            "Scene transitions must retain the same level manager singleton and legacy alias.");
        Check(GameValManager.Instance != null && GameValManager.Instance.GetInstanceID() == gameValuesId &&
            WeaponStatsManager.Instance != null && WeaponStatsManager.Instance.GetInstanceID() == weaponStatsId,
            "Scene transitions must preserve permanent resources and weapon/skill state managers.");
        Check(RestaurantPanel.Instance != null && RestaurantPanel.instance == RestaurantPanel.Instance && RestaurantPanel.Instance.GetInstanceID() == restaurantId,
            "Scene transitions must retain the home restaurant singleton and its existing inventory integration.");
    }

    private static void CheckProjectionFollowers(Camera camera, Scene expected)
    {
        CameraStackPresentation stack = camera.GetComponent<CameraStackPresentation>();
        Check(stack != null && stack.source == camera, "Dungeon rendering must keep its camera-stack presentation bound to the base camera.");
        foreach (Camera overlay in stack.overlays)
        {
            Check(overlay != null && overlay.gameObject.scene.handle == expected.handle,
                "Every configured overlay camera must survive in the destination scene.");
            Check(overlay.orthographic == camera.orthographic && Mathf.Abs(overlay.fieldOfView - camera.fieldOfView) < 0.01f &&
                Mathf.Abs(overlay.nearClipPlane - camera.nearClipPlane) < 0.001f && Mathf.Abs(overlay.farClipPlane - camera.farClipPlane) < 0.001f &&
                overlay.rect == camera.rect && Vector3.Distance(overlay.transform.position, camera.transform.position) < 0.001f &&
                Quaternion.Angle(overlay.transform.rotation, camera.transform.rotation) < 0.01f,
                "Overlay cameras must use the same destination pose, perspective lens and viewport as the gameplay camera.");
        }
        foreach (Behaviour component in stack.orthographicOnly)
            if (component != null) Check(!component.enabled, "Orthographic-only camera processors must stay suspended during perspective gameplay.");
    }

    private static void CheckNoExpiredShotOwners(CameraDirector director)
    {
        FieldInfo requestsField = typeof(CameraDirector).GetField("_shots", BindingFlags.Instance | BindingFlags.NonPublic);
        var requests = requestsField == null ? null : requestsField.GetValue(director) as System.Collections.IEnumerable;
        Check(requests != null, "Camera regression must inspect the upstream request collection for stale owners.");
        foreach (object request in requests)
        {
            FieldInfo ownerField = request.GetType().GetField("Owner", BindingFlags.Instance | BindingFlags.Public);
            UnityEngine.Object owner = ownerField == null ? null : ownerField.GetValue(request) as UnityEngine.Object;
            Check(owner != null, "The destination director must not retain a request owned by a destroyed scene object.");
            Behaviour behaviour = owner as Behaviour;
            if (behaviour != null) Check(behaviour.isActiveAndEnabled, "Disabled camera request owners must be pruned after LateUpdate.");
            Component component = owner as Component;
            if (component != null)
                Check(component.gameObject.scene.IsValid() && component.gameObject.scene.isLoaded,
                    "Camera requests cannot keep referencing an unloaded scene.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
#endif
