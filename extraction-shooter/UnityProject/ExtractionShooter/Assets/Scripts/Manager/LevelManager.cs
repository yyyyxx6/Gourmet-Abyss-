using System.Collections;
using System.Collections.Generic;
using Game.Core;
using Game.Core.SceneFlow;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DefaultExecutionOrder(-60)] // 在 Audio/UI/GameVal 之后，大部分游戏逻辑之前
public class LevelManager : PersistentMonoSingleton<LevelManager>
{
    public GameObject mainUI;
    public Text TitleText;

    /// <summary>兼容旧调用点的小写别名，等价于 <see cref="Instance"/>。</summary>
    public static LevelManager instance => Instance;

    [Header("场景对象")]
    public GameObject homeSceneObject;
    public GameObject restaurantObject;
    public GameObject postProcessObject;

    [Header("过渡系统")]
    public EmissionTransition emissionTransition;
    public SaturationTransition saturationTransition;
    public Animator transitionUIAnimator;

    [Header("过渡设置")]
    [SerializeField] private float transitionDuration = 1.0f;
    [SerializeField] private float uiAnimationDelay = 0.5f;

    [Header("场景设置")]
    [SerializeField] private string levelSceneName = "Layer1";

    // 私有变量
    private bool isTransitioning = false;
    private List<string> loadedLevels = new List<string>();
    private Vector3 restaurantInitialPosition;
    private bool hasCachedRestaurantInitialPosition = false;
    private bool movedForSkillTree = false;

    public string CurrentLevelId => loadedLevels.Count > 0 ? loadedLevels[loadedLevels.Count - 1] : null;

    protected override void OnAwake()
    {
        base.OnAwake();
        if (GetComponent<RunSessionManager>() == null)
            gameObject.AddComponent<RunSessionManager>();
    }

    private void Start()
    {
        ApplySceneTitle(SceneManager.GetActiveScene().name);
        UIManager.instance?.SetBattleUIActive(false);
        if (transitionUIAnimator != null && transitionUIAnimator.enabled)
            transitionUIAnimator.Play("DefaultState", 0, 0f);
        CacheRestaurantInitialPosition();
    }

    #region Public transition entry points

    public void EnterLevel(string sceneName = null)
    {
        TryEnterLevel(sceneName);
    }

    public bool TryEnterLevel(string sceneName = null)
    {
        RunSessionManager runs = RunSessionManager.Instance;
        string target = sceneName ?? levelSceneName;
        if (isTransitioning || runs == null || runs.IsEndingRun || runs.IsActive ||
            !string.IsNullOrEmpty(CurrentLevelId) || !CanLoadDungeon(target) ||
            GetLoadedScene(target).IsValid() || !HaveRunManagers())
            return false;

        InventoryManager.instance.CommitRunGatheredToPermanent();
        if (InventoryManager.instance.GetRunGatheredCounts().Count > 0)
        {
            GlobalMessageUI.Show("采集物仓库空间不足，请先整理资源");
            return false;
        }

        StartTransition(TransitionPresets.EnterLevel(target));
        return true;
    }

    public void ExitLevel()
    {
        if (!isTransitioning)
            RunSessionManager.Instance?.TryEndRun(RunEndReason.Extracted);
    }

    public void FromLevelToHome(string sceneName = null)
    {
        if (!isTransitioning)
            RunSessionManager.Instance?.TryEndRun(RunEndReason.Extracted);
    }

    public void SwitchLevel(string fromLevel, string toLevel)
    {
        RunSessionManager runs = RunSessionManager.Instance;
        if (isTransitioning || runs == null || runs.IsEndingRun || !runs.IsActive ||
            CurrentLevelId != fromLevel || !GetLoadedScene(fromLevel).IsValid() ||
            !CanLoadDungeon(toLevel) || !HaveRunManagers())
            return;
        if (fromLevel != toLevel && GetLoadedScene(toLevel).IsValid()) return;
        if (!CanCommitGatheredMaterials())
        {
            GlobalMessageUI.Show("采集物仓库空间不足，请先返回小镇整理资源");
            return;
        }
        // Snapshot validation must finish before stopping the source run or starting any scene operation.
        try
        {
            if (runs.CompleteRun() == null) return;
        }
        catch (System.Exception error)
        {
            Debug.LogException(error, this);
            GlobalMessageUI.Show("背包暂时不可用，未切换关卡");
            return;
        }
        StartTransition(TransitionPresets.SwitchLevel(fromLevel, toLevel));
    }

    public bool TryRestartFromSettlement(string levelName, System.Action<bool> onCompleted)
    {
        if (!CanLeaveSettlement() || !CanLoadDungeon(levelName) ||
            !GetLoadedScene(TransitionPresets.HomeSceneName).IsValid() || InventoryManager.instance == null ||
            InventoryManager.instance.GetRunGatheredCounts().Count > 0)
            return false;

        // A failed load may already have removed the previous instance.
        StartTransition(TransitionPresets.RestartLevel(levelName), onCompleted);
        return true;
    }

    public bool TryReturnHomeFromSettlement(string levelName, System.Action<bool> onCompleted)
    {
        if (!CanLeaveSettlement() || !GetLoadedScene(TransitionPresets.HomeSceneName).IsValid())
            return false;
        string source = string.IsNullOrEmpty(levelName) ? CurrentLevelId : levelName;
        if (source == TransitionPresets.HomeSceneName) return false;

        TransitionRequest request = TransitionPresets.LevelToHome(source);
        request.FromSettlement = true;
        StartTransition(request, onCompleted);
        return true;
    }

    public bool IsTransitioning()
    {
        return isTransitioning;
    }

    private bool CanLeaveSettlement()
    {
        return !isTransitioning && RunSessionManager.Instance != null &&
            RunSessionManager.Instance.Phase == RunEndPhase.Transitioning;
    }

    private static bool CanLoadDungeon(string sceneName)
    {
        return !string.IsNullOrWhiteSpace(sceneName) && sceneName != TransitionPresets.HomeSceneName &&
            Application.CanStreamedLevelBeLoaded(sceneName);
    }

    private static bool HaveRunManagers()
    {
        return InventoryManager.instance != null && BattleValManager.Instance != null &&
            PlayerStateManager.instance != null && RunSessionManager.Instance != null;
    }

    #endregion

    #region Unified transition pipeline

    private void StartTransition(TransitionRequest request, System.Action<bool> onCompleted = null)
    {
        isTransitioning = true;
        StartCoroutine(RunTransition(request, onCompleted));
    }

    private IEnumerator RunTransition(TransitionRequest request, System.Action<bool> onCompleted)
    {
        bool succeeded = false;
        bool recoveredHome = false;
        System.Exception failure = null;
        try
        {
            yield return DriveSafely(ExecuteTransition(request), error => failure = error);
            succeeded = failure == null;
            if (!succeeded && !request.FromSettlement)
            {
                System.Exception recoveryFailure = null;
                yield return DriveSafely(RecoverHomeAfterFailure(request), error => recoveryFailure = error);
                recoveredHome = recoveryFailure == null;
            }
        }
        finally
        {
            try
            {
                if (!succeeded && !recoveredHome)
                    FreezeFailedTransition(request);
            }
            catch (System.Exception error)
            {
                succeeded = false;
                Debug.LogException(error, this);
            }
            finally
            {
                isTransitioning = false;
                try { onCompleted?.Invoke(succeeded); }
                catch (System.Exception error) { Debug.LogException(error, this); }
            }
        }
    }

    // Catch failures when the iterator resumes after a scene operation, not only at its creation.
    private IEnumerator DriveSafely(IEnumerator process, System.Action<System.Exception> onFailure)
    {
        try
        {
            while (true)
            {
                bool hasNext = false;
                object current = null;
                System.Exception failure = null;
                try
                {
                    hasNext = process.MoveNext();
                    if (hasNext) current = process.Current;
                }
                catch (System.Exception error) { failure = error; }

                if (failure != null)
                {
                    Debug.LogException(failure, this);
                    onFailure(failure);
                    break;
                }
                if (!hasNext) break;
                yield return current;
            }
        }
        finally
        {
            try { (process as System.IDisposable)?.Dispose(); }
            catch (System.Exception error)
            {
                Debug.LogException(error, this);
                onFailure(error);
            }
        }
    }

    private IEnumerator ExecuteTransition(TransitionRequest request)
    {
        string sourceName = string.IsNullOrEmpty(request.SceneToUnload)
            ? TransitionPresets.HomeSceneName : request.SceneToUnload;
        string destinationName = string.IsNullOrEmpty(request.SceneToLoad)
            ? TransitionPresets.HomeSceneName : request.SceneToLoad;
        Scene source = GetLoadedScene(sourceName);

        BattleValManager.Instance?.StopConsuming();
        if (PlayerStateManager.instance != null)
            PlayerStateManager.instance.currentState = PlayerState.Settlement;
        StopScenePlayers(source);
        UIManager.instance?.SetBattleUIActive(false);
        ApplyRunBag(request);
        GlobalMessageUI.Clear();
        AudioManager.Instance?.PlayAudio("3");
        if (transitionUIAnimator != null && !string.IsNullOrEmpty(request.AnimatorTrigger))
            transitionUIAnimator.SetTrigger(request.AnimatorTrigger);

        yield return new WaitForSecondsRealtime(Mathf.Max(0f, uiAnimationDelay));
        VehicleColorTransition fadeOut = FindVehicleInScene(request.VehicleFadeOutScene);
        if (fadeOut != null) fadeOut.TransitionToWhite(transitionDuration);
        ApplySaturation(request.Saturation);
        ApplyEmission(request.Emission);
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, transitionDuration));
        if (fadeOut != null) fadeOut.SetToWhiteImmediate();

        AsyncOperation unload = null;
        AsyncOperation load = null;
        System.Exception activationError = null;
        UnityEngine.Events.UnityAction<Scene, LoadSceneMode> freezeOnLoad = (scene, mode) =>
        {
            if (scene.name != request.SceneToLoad || mode != LoadSceneMode.Additive) return;
            try
            {
                MakeSceneActive(scene);
                StopScenePlayers(scene, true);
            }
            catch (System.Exception error) { activationError = error; }
        };

        if (!string.IsNullOrEmpty(request.SceneToLoad))
            SceneManager.sceneLoaded += freezeOnLoad;
        try
        {
            if (!string.IsNullOrEmpty(request.SceneToUnload) && source.IsValid())
            {
                Scene home = GetLoadedScene(TransitionPresets.HomeSceneName);
                if (home.IsValid()) MakeSceneActive(home);
                unload = SceneManager.UnloadSceneAsync(source);
                if (unload == null)
                    throw new System.InvalidOperationException("Cannot unload level " + sourceName + ".");
            }

            bool sequential = request.WaitForUnloadBeforeLoad ||
                request.SceneToUnload == request.SceneToLoad;
            if (sequential && unload != null)
            {
                if (request.HudTiming == HudRefreshTiming.BeforeSceneOpCompletes)
                    RefreshHudBeforeSceneOperations(request);
                yield return unload;
                loadedLevels.RemoveAll(level => level == sourceName);
            }

            if (!string.IsNullOrEmpty(request.SceneToLoad))
            {
                load = SceneManager.LoadSceneAsync(request.SceneToLoad, LoadSceneMode.Additive);
                if (load == null)
                    throw new System.InvalidOperationException("Cannot load level " + request.SceneToLoad + ".");
            }
            if (!sequential && request.HudTiming == HudRefreshTiming.BeforeSceneOpCompletes)
                RefreshHudBeforeSceneOperations(request);
            while ((unload != null && !unload.isDone) || (load != null && !load.isDone))
                yield return null;
        }
        finally
        {
            SceneManager.sceneLoaded -= freezeOnLoad;
        }

        if (activationError != null) throw activationError;
        Scene destination = GetLoadedScene(destinationName);
        if (!destination.IsValid())
            throw new System.InvalidOperationException("The destination scene is unavailable: " + destinationName + ".");

        if (request.HudTiming == HudRefreshTiming.BeforeSceneOpCompletes)
            ApplySceneTitle(destinationName);
        else if (request.HudTiming == HudRefreshTiming.AfterSceneOpBeforeWorldSetup)
            RefreshHud(request, destinationName);

        ApplyHomeObjects(request.HomeObjects);
        ApplyRestaurantPose(request.Restaurant);
        MakeSceneActive(destination);
        StopScenePlayers(destination, true);

        // Let the scene's own Start methods initialize its camera director, player and title.
        yield return null;
        TopDownController player = BindSceneForPlay(destination);
        if (request.RefreshMainCamera) RefreshMainCamera();
        if (request.HudTiming == HudRefreshTiming.AfterWorldSetup)
            RefreshHud(request, destinationName);
        if (request.ExtraFrameBeforeFadeIn) yield return null;

        VehicleColorTransition fadeIn = FindVehicleInScene(request.VehicleFadeInScene);
        if (fadeIn != null)
        {
            fadeIn.enabled = true;
            fadeIn.SetToWhiteImmediate();
            fadeIn.TransitionToOriginal(transitionDuration);
        }
        else if (request.Kind == TransitionKind.EnterLevel)
            Debug.LogWarning("No VehicleColorTransition in " + request.VehicleFadeInScene + ".");

        if (!HaveRunManagers())
            throw new System.InvalidOperationException("Run managers are unavailable after loading " + destinationName + ".");
        if (request.ResetBattleValues) BattleValManager.Instance.ResetValues();
        if (request.PlaySlotsEntranceAnimation) InventoryManager.instance.PlaySlotsEntranceAnimation();
        loadedLevels.RemoveAll(level => level == request.SceneToUnload || !GetLoadedScene(level).IsValid());
        if (!string.IsNullOrEmpty(request.SceneToLoad))
        {
            loadedLevels.RemoveAll(level => level == request.SceneToLoad);
            loadedLevels.Add(request.SceneToLoad);
        }

        PlayerStateManager.instance.currentState = request.TargetPlayerState;
        UIManager.instance?.SetBattleUIActive(request.BattleUiActive);
        if (request.ReenablePlayerController) player.ResumeAfterRun();
        if (request.TargetPlayerState == PlayerState.Battle)
        {
            if (request.Oxygen == OxygenAction.StartConsuming)
                BattleValManager.Instance.StartConsuming();
            else
                BattleValManager.Instance.ResumeConsuming();
            PetManager.Instance?.SyncPetsForBattleNow();
            // Keep this last: a failure before BeginRun preserves the completed settlement result.
            RunSessionManager.Instance.BeginRun(destinationName);
            if (!RunSessionManager.Instance.IsActive)
                throw new System.InvalidOperationException("Could not begin run statistics for " + destinationName + ".");
        }
        else if (HomeCavecar.homeCavecar != null)
            HomeCavecar.homeCavecar.canUse = true;
    }

    private static void ApplyRunBag(TransitionRequest request)
    {
        // The result transaction already settled resources before either destination was chosen.
        if (request.FromSettlement) return;
        InventoryManager inventory = InventoryManager.instance;
        if (inventory == null) throw new System.InvalidOperationException("InventoryManager is unavailable.");
        if (request.RunBag == RunBagAction.None) return;
        inventory.CommitRunGatheredToPermanent();
        if (request.TargetPlayerState == PlayerState.Battle && inventory.GetRunGatheredCounts().Count > 0)
            throw new System.InvalidOperationException("Gathered materials could not be committed before entering the next run.");
        if (request.RunBag == RunBagAction.Clear)
            inventory.ClearRunGathered();
    }

    private IEnumerator RecoverHomeAfterFailure(TransitionRequest request)
    {
        Scene home = GetLoadedScene(TransitionPresets.HomeSceneName);
        if (!home.IsValid()) throw new System.InvalidOperationException("Home is unavailable for transition recovery.");
        BattleValManager.Instance?.StopConsuming();
        MakeSceneActive(home);
        var cleanup = new HashSet<string>(loadedLevels);
        if (!string.IsNullOrEmpty(request.SceneToLoad)) cleanup.Add(request.SceneToLoad);
        if (!string.IsNullOrEmpty(request.SceneToUnload)) cleanup.Add(request.SceneToUnload);
        foreach (string level in cleanup)
        {
            if (level == TransitionPresets.HomeSceneName) continue;
            Scene scene = GetLoadedScene(level);
            if (!scene.IsValid()) continue;
            StopScenePlayers(scene);
            AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
            if (unload == null) throw new System.InvalidOperationException("Cannot unload failed level " + level + ".");
            yield return unload;
        }
        loadedLevels.Clear();
        ApplyHomeObjects(HomeSceneVisibility.Show);
        ApplyRestaurantPose(RestaurantPose.RestoreToHome);
        ApplySaturation(SaturationDirection.ToSaturated);
        ApplyEmission(EmissionEffect.ExitLevel);
        StopScenePlayers(home, true);
        yield return null;
        TopDownController player = BindSceneForPlay(home);
        RestoreVehicle(home);
        BattleValManager.Instance?.ResetValues();
        if (PlayerStateManager.instance != null)
            PlayerStateManager.instance.currentState = PlayerState.UpGround;
        UIManager.instance?.SetBattleUIActive(false);
        player.ResumeAfterRun();
        if (HomeCavecar.homeCavecar != null) HomeCavecar.homeCavecar.canUse = true;
        GlobalMessageUI.Show("场景加载未完成，已返回小镇，请重新选择关卡");
    }

    private void FreezeFailedTransition(TransitionRequest request)
    {
        BattleValManager.Instance?.StopConsuming();
        if (PlayerStateManager.instance != null)
            PlayerStateManager.instance.currentState = PlayerState.Settlement;
        StopScenePlayers(GetLoadedScene(request.SceneToLoad));
        StopScenePlayers(GetLoadedScene(request.SceneToUnload));
        StopScenePlayers(GetLoadedScene(TransitionPresets.HomeSceneName));
        UIManager.instance?.SetBattleUIActive(false);
        loadedLevels.RemoveAll(level => !GetLoadedScene(level).IsValid());
    }

    private TopDownController BindSceneForPlay(Scene scene)
    {
        TopDownController player = FindSceneComponent<TopDownController>(scene);
        SceneTitle title = SceneTitle.Resolve(scene.name);
        Camera camera = null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Camera candidate in root.GetComponentsInChildren<Camera>(true))
            {
                if (candidate.isActiveAndEnabled && candidate.CompareTag("MainCamera"))
                {
                    camera = candidate;
                    break;
                }
            }
            if (camera != null) break;
        }
        if (player == null || !player.CompareTag("Player") || title == null ||
            title.gameObject.scene.handle != scene.handle || camera == null)
            throw new System.InvalidOperationException("Scene " + scene.name + " is missing its active player, title or main camera.");

        ApplySceneTitle(scene.name);
        KeepMainCamera keeper = KeepMainCamera.instance;
        if (keeper == null) keeper = FindSceneComponent<KeepMainCamera>(scene);
        if (keeper != null)
        {
            if (keeper.transitionAnimator != null) keeper.transitionAnimator.mainCamera = camera;
            if (keeper.mainUICanvas != null) keeper.mainUICanvas.worldCamera = camera;
        }
        if (mainUI != null) mainUI.SetActive(true);
        return player;
    }

    private static Scene GetLoadedScene(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return default;
        Scene scene = SceneManager.GetSceneByName(sceneName);
        return scene.IsValid() && scene.isLoaded ? scene : default;
    }

    private static void MakeSceneActive(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
            throw new System.InvalidOperationException("Cannot activate an unavailable scene.");
        if (SceneManager.GetActiveScene().handle == scene.handle) return;
        SceneManager.SetActiveScene(scene);
        if (SceneManager.GetActiveScene().handle != scene.handle)
            throw new System.InvalidOperationException("Cannot activate scene " + scene.name + ".");
    }

    private static T FindSceneComponent<T>(Scene scene) where T : Component
    {
        if (!scene.IsValid() || !scene.isLoaded) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (T component in root.GetComponentsInChildren<T>(true))
                if (component.gameObject.activeInHierarchy) return component;
        return null;
    }

    private static void StopScenePlayers(Scene scene, bool allowStart = false)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (TopDownController player in root.GetComponentsInChildren<TopDownController>(true))
            {
                if (!player.gameObject.activeInHierarchy) continue;
                player.StopForRunEnd();
                if (allowStart) player.enabled = true;
            }
        }
    }

    private void ApplySaturation(SaturationDirection direction)
    {
        if (saturationTransition == null) return;
        if (direction == SaturationDirection.ToSaturated) saturationTransition.TransitionToSaturated();
        else if (direction == SaturationDirection.ToUnsaturated) saturationTransition.TransitionToUnsaturated();
    }

    private void ApplyEmission(EmissionEffect effect)
    {
        if (emissionTransition == null) return;
        if (effect == EmissionEffect.EnterLevel) emissionTransition.EnterLevelTransition();
        else if (effect == EmissionEffect.ExitLevel) emissionTransition.ExitLevelTransition();
    }

    private void ApplyHomeObjects(HomeSceneVisibility visibility)
    {
        if (visibility == HomeSceneVisibility.Unchanged) return;
        bool active = visibility == HomeSceneVisibility.Show;
        if (homeSceneObject != null) homeSceneObject.SetActive(active);
        if (postProcessObject != null) postProcessObject.SetActive(active);
    }

    private void ApplyRestaurantPose(RestaurantPose pose)
    {
        if (pose == RestaurantPose.MoveAwayForBattle) MoveRestaurantForBattle();
        else if (pose == RestaurantPose.RestoreToHome) RestoreRestaurantToHomePosition();
    }

    private void RefreshHud(TransitionRequest request, string sceneName)
    {
        ResetTapBounce();
        ApplySceneTitle(sceneName);
        BlinkMainUI(request.FromSettlement);
    }

    private void RefreshHudBeforeSceneOperations(TransitionRequest request)
    {
        ResetTapBounce();
        BlinkMainUI(request.FromSettlement);
    }

    private void BlinkMainUI(bool keepActive)
    {
        if (mainUI == null) return;
        // A settlement owns its overlay through the transition; never stop this coroutine's host.
        if (!keepActive && !transform.IsChildOf(mainUI.transform))
            mainUI.SetActive(false);
        mainUI.SetActive(true);
    }

    private void ApplySceneTitle(string sceneName)
    {
        if (TitleText == null) return;
        string title = SceneTitle.ResolveName(sceneName);
        if (!string.IsNullOrEmpty(title)) TitleText.text = title;
    }

    private static void RefreshMainCamera()
    {
        KeepMainCamera.instance?.tKeepMainCamera();
    }

    private static void ResetTapBounce()
    {
        UITapBounce.Instance?.ResetPosition();
    }

    private VehicleColorTransition FindVehicleInScene(string sceneName)
    {
        Scene scene = GetLoadedScene(sceneName);
        if (!scene.IsValid()) return null;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            VehicleColorTransition vehicle = root.GetComponentInChildren<VehicleColorTransition>(true);
            if (vehicle != null) return vehicle;
        }
        return null;
    }

    private void RestoreVehicle(Scene scene)
    {
        VehicleColorTransition vehicle = FindVehicleInScene(scene.name);
        if (vehicle == null) return;
        vehicle.enabled = true;
        vehicle.SetToWhiteImmediate();
        vehicle.TransitionToOriginal(transitionDuration);
    }

    private bool CanCommitGatheredMaterials()
    {
        if (InventoryManager.instance == null) return true;
        foreach (KeyValuePair<ResourceType, int> entry in InventoryManager.instance.GetRunGatheredCounts())
        {
            ResourceItem resource = GameValManager.Instance?.GetResourceInfo(entry.Key);
            if (resource == null || (long)resource.maxCapacity - resource.count < entry.Value) return false;
        }
        return true;
    }

    #endregion

    private void CacheRestaurantInitialPosition()
    {
        if (restaurantObject == null || hasCachedRestaurantInitialPosition) return;
        restaurantInitialPosition = restaurantObject.transform.position;
        hasCachedRestaurantInitialPosition = true;
    }

    private void MoveRestaurantForBattle()
    {
        if (restaurantObject == null) return;
        CacheRestaurantInitialPosition();

        Vector3 targetPos = restaurantInitialPosition;
        targetPos.x += 100f;
        restaurantObject.transform.position = targetPos;
    }

    private void RestoreRestaurantToHomePosition()
    {
        if (restaurantObject == null) return;
        CacheRestaurantInitialPosition();
        restaurantObject.transform.position = restaurantInitialPosition;
    }

    /// <summary>
    /// 打开技能树时把餐厅移开（仅在地面场景使用）
    /// </summary>
    public void MoveRestaurantForSkillTree()
    {
        if (restaurantObject == null) return;
        CacheRestaurantInitialPosition();
        if (movedForSkillTree) return;

        Vector3 targetPos = restaurantInitialPosition;
        targetPos.x += 1000f;
        restaurantObject.transform.position = targetPos;
        movedForSkillTree = true;
    }

    /// <summary>
    /// 关闭技能树时恢复餐厅位置
    /// </summary>
    public void RestoreRestaurantFromSkillTree()
    {
        if (restaurantObject == null) return;
        CacheRestaurantInitialPosition();
        restaurantObject.transform.position = restaurantInitialPosition;
        movedForSkillTree = false;
    }
}
