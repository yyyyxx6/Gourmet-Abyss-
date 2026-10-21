#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class RestaurantStage1UnityValidation
{
    private const string Key = "ChefDungeon.RestaurantStage1.";
    private static readonly ResourceType Food = ResourceType.LootMushroom;
    private static readonly ResourceType Egg = ResourceType.LootEggSmall;
    private static RestaurantEntryPoint entry;
    private static RestaurantPanel panel;
    private static RestaurantIngredientStorage stock;
    private static InventoryManager bag;
    private static TopDownController player;
    private static RestaurantModuleAdapter adapter;
    private static Vector3 originalPosition;
    private static int foodBank;
    private static int moneyBank;
    private static DishRecipe orderRecipe;
    private static List<Pot> originalPots;
    private static GameplayDeathPresentationProbe death;

    static RestaurantStage1UnityValidation()
    {
        EditorApplication.update += Tick;
        Application.logMessageReceived += OnLog;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && Phase == "enter") SetPhase("prepare", 60, 3);
            if (state == PlayModeStateChange.EnteredEditMode && Phase == "exit") EditorApplication.delayCall += Exit;
        };
    }
    private static string Phase => SessionState.GetString(Key + "phase", "");
    public static void Run()
    {
        try
        {
            RestaurantIngredientStoreTests.Run();
            Debug.Log("RESTAURANT_STORE_PURE_PASS");
            PlayerSettings.companyName = "CodexValidation";
            PlayerSettings.productName = "ChefDungeonRestaurantStage1";
            EditorSceneManager.OpenScene("Assets/Scenes/UpGround.unity", OpenSceneMode.Single);
            SessionState.EraseString(Key + "error");
            SessionState.SetInt(Key + "result", 1);
            SetPhase("enter", 90);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception error) { Finish(error); }
    }
    private static void Tick()
    {
        if (string.IsNullOrEmpty(Phase)) return;
        if (Phase == "exit") { if (!EditorApplication.isPlayingOrWillChangePlaymode) Exit(); return; }
        try
        {
            Check(string.IsNullOrEmpty(SessionState.GetString(Key + "error", "")), "Unexpected error: " + SessionState.GetString(Key + "error", ""));
            Check(EditorApplication.timeSinceStartup < ReadTime("deadline"), "Timeout in " + Phase);
            if (!EditorApplication.isPlaying) return;
            EditorApplication.QueuePlayerLoopUpdate();
            if (EditorApplication.timeSinceStartup < ReadTime("ready")) return;
            switch (Phase)
            {
                case "prepare": Prepare(); break;
                case "flight": ObserveFlight(); break;
                case "deposited": ValidateDepositAndStock(); break;
                case "queued": ValidateSuccessfulOrder(); break;
                case "failed-queue": ValidateFailedQueue(); break;
                case "disabled-order": ValidateDisabledOrder(); break;
                case "early-exit": ValidateEarlyExitAndEnterDungeon(); break;
                case "dungeon": ValidateDungeon(); break;
                case "death": if (death != null) { death.Observe(); if (death.ReadyForButtons) ReturnFromDungeon(); } break;
                case "home": if (RunSessionManager.Instance.Phase == RunEndPhase.Inactive && !LevelManager.Instance.IsTransitioning()) Finish(null); break;
            }
        }
        catch (Exception error) { Finish(new InvalidOperationException("Phase " + Phase, error)); }
    }
    private static void Prepare()
    {
        Check(GameValManager.Instance != null && InventoryManager.Instance != null && RestaurantPanel.Instance != null && RestaurantEntryPoint.Instance != null,
            "The real home scene must initialize all restaurant dependencies.");
        entry = RestaurantEntryPoint.Instance; panel = RestaurantPanel.Instance; bag = InventoryManager.Instance;
        player = FindPlayer(SceneManager.GetSceneByName("UpGround"));
        adapter = UnityEngine.Object.FindObjectOfType<RestaurantModuleAdapter>(true);
        stock = RestaurantIngredientStorage.EnsureExists();
        Check(stock.GetCount(Food) == 0, "The isolated session starts without restaurant food.");
        bag.ClearAllItems();
        WeaponStatsManager.Instance.SetInventorySlotCount(3);
        WeaponStatsManager.Instance.SetInventorySlotCapacity(16);
        Check(bag.AddItemPartial(Food, 10) == 10 && bag.AddItemPartial(Egg, 4) == 4 && bag.AddItemPartial(ResourceType.watermelonJ, 2) == 2,
            "The actual bag must contain two food types and a legacy non-food slot.");
        originalPosition = player.transform.position;
        foodBank = GameValManager.Instance.GetResourceCount(Food); moneyBank = GameValManager.Instance.GetResourceCount(ResourceType.Money);
        Check(entry.TryEnterRestaurant(player) && entry.IsEntered && entry.IsTransferringIngredients, "Actual restaurant entry starts its deposit presentation.");
        Check(bag.GetItemCount(Food) == 0 && bag.GetItemCount(Egg) == 0 && bag.GetItemCount(ResourceType.watermelonJ) == 2,
            "Entry atomically empties food slots but preserves non-food items.");
        Check(stock.GetCount(Food) == 10 && stock.GetCount(Egg) == 4, "Exact deposited amounts become warehouse stock immediately.");
        Check(!entry.TryEnterRestaurant(player) && stock.GetCount(Food) == 10, "Repeated entry cannot replay deposits.");
        Check(!player.canPlayerMove && entry.IngredientStorageOrigin != null && entry.IngredientStorageOrigin.name == "Cabinet",
            "Entry retains movement locking and the actual cabinet animation destination.");
        SetPhase("flight", 10, 0.18);
    }
    private static void ObserveFlight()
    {
        GameObject visual = GameObject.Find("RestaurantIngredientTransfer");
        Check(visual != null && visual.transform.childCount == 2, "The real deposit must display one moving icon per occupied food stack.");
        foreach (Image icon in visual.GetComponentsInChildren<Image>())
        {
            Text label = icon.GetComponentInChildren<Text>();
            Check(icon.rectTransform.anchoredPosition != Vector2.zero &&
                ((icon.sprite != null && icon.enabled) || (label != null && label.text == RestaurantIngredientStorage.GetDisplayName(Egg) + " 4")),
                "Food must have a visible icon or an identifiable missing-art label at its moving position: " + icon.name);
        }
        Check(entry.IsTransferringIngredients, "The presentation should still be in flight before its completion.");
        Debug.Log("RESTAURANT_DEPOSIT_ANIMATION_PASS");
        SetPhase("deposited", 10, 0.7);
    }
    private static void ValidateDepositAndStock()
    {
        Check(!entry.IsTransferringIngredients && GameObject.Find("RestaurantIngredientTransfer") == null,
            "Presentation must finish and remove temporary graphics.");
        Check(!bag.InventoryPresentationRoot.activeSelf, "The real backpack hides after deposit.");
        Transform moduleBag = null;
        foreach (Transform candidate in adapter.pair.hud.GetComponentsInChildren<Transform>(true)) if (candidate.name == "Bag") moduleBag = candidate;
        Check(moduleBag != null && !moduleBag.gameObject.activeSelf, "The decorative module bag also hides.");
        entry.LeaveRestaurant();
        Check(player.canPlayerMove && bag.InventoryPresentationRoot.activeSelf && Vector3.Distance(player.transform.position, originalPosition) < 0.01f,
            "Leaving restores the previous movement, position and backpack visibility.");
        Check(entry.TryEnterRestaurant(player) && !entry.IsTransferringIngredients && stock.GetCount(Food) == 10,
            "Empty re-entry cannot duplicate food.");
        Check(stock.TryDeposit(Counts(Food, 10000)) && panel.GetIngredientCount(Food) == 10010, "Warehouse and menu amounts must exceed 9999 without clamping.");
        panel.GenerateFoodItems();
        bool found = false;
        foreach (foodItemPrefabs widget in panel.foodItemParent.GetComponentsInChildren<foodItemPrefabs>(true))
            if (widget.gameObject.activeSelf && widget.resourceType == Food) { Check(widget.foodAmount.text == "10010", "The actual food UI reads warehouse stock: " + widget.foodAmount.text); found = true; }
        Check(found, "The real menu must generate its mushroom quantity widget.");
        var duplicate = Recipe(2); duplicate.ingredients.Add(new DishIngredient { resourceType = Food, requiredCount = 3 });
        Check(stock.TryConsumeRecipe(duplicate) && stock.GetCount(Food) == 10005, "Duplicate recipe ingredient rows must aggregate before debit.");
        duplicate.ingredients.Add(new DishIngredient { resourceType = Egg, requiredCount = 99 });
        Check(!stock.TryConsumeRecipe(duplicate) && stock.GetCount(Food) == 10005 && stock.GetCount(Egg) == 4,
            "Insufficient mixed recipes do not partially debit affordable food.");
        CheckPermanentAndLegacy();
        Debug.Log("RESTAURANT_TRANSFER_REENTRY_AND_MENU_PASS");
        stock.Store.TryConsume(Counts(Food, stock.GetCount(Food) - 2));
        originalPots = new List<Pot>(panel.potsList); panel.potsList.Clear();
        ClearQueue();
        orderRecipe = Recipe(2);
        Check(panel.TryEnqueueDishForCooking(orderRecipe) && stock.GetCount(Food) == 0, "Accepted cooking reserves its food immediately.");
        Check(!panel.TryEnqueueDishForCooking(orderRecipe) && stock.GetCount(Food) == 0, "Repeated click cannot spend already reserved food.");
        SetPhase("queued", 15, 1.2);
    }
    private static void ValidateSuccessfulOrder()
    {
        Check(QueueCount(orderRecipe) == 1 && stock.GetCount(Food) == 0, "Successful cooking queue commits one meal for one debit.");
        ClearQueue(); stock.TryDeposit(Counts(Food, 2));
        Check(panel.TryEnqueueDishForCooking(orderRecipe), "Start a failed queue fixture.");
        FillQueue();
        orderRecipe.ingredients[0].requiredCount = 9;
        SetPhase("failed-queue", 15, 1.2);
    }
    private static void ValidateFailedQueue()
    {
        Check(stock.GetCount(Food) == 2 && QueueCount(orderRecipe) == 0, "Failed queue restores original two-unit reservation even if recipe cost changes.");
        ClearQueue(); orderRecipe.ingredients[0].requiredCount = 2;
        Check(panel.TryEnqueueDishForCooking(orderRecipe) && stock.GetCount(Food) == 0, "Start an interrupted order.");
        panel.enabled = false;
        Check(stock.GetCount(Food) == 2, "Disabling the business owner refunds pending orders immediately.");
        panel.enabled = true;
        SetPhase("disabled-order", 15, 1.2);
    }
    private static void ValidateDisabledOrder()
    {
        Check(stock.GetCount(Food) == 2 && QueueCount(orderRecipe) == 0, "An old coroutine cannot commit or refund again after re-enable.");
        CheckPermanentAndLegacy();
        Debug.Log("RESTAURANT_ORDER_RESERVATION_AND_REFUND_PASS");
        entry.LeaveRestaurant();
        Check(bag.AddItemPartial(Food, 5) == 5 && entry.TryEnterRestaurant(player), "New carried food can be deposited.");
        entry.LeaveRestaurant();
        SetPhase("early-exit", 10, 0.8);
    }
    private static void ValidateEarlyExitAndEnterDungeon()
    {
        Check(stock.GetCount(Food) == 7 && bag.GetItemCount(Food) == 0 && GameObject.Find("RestaurantIngredientTransfer") == null,
            "Exiting during the deposit animation keeps the committed food and cleans the presentation.");
        Check(entry.TryEnterRestaurant(player) && stock.GetCount(Food) == 7, "Re-entering after an interrupted presentation cannot replay food.");
        entry.LeaveRestaurant();
        var boundary = new RestaurantIngredientStore(); boundary.TryDeposit(Counts(Food, long.MaxValue));
        bag.AddItemPartial(Food, 1); bag.AddItemPartial(Egg, 1);
        List<InventoryManager.RestaurantTransferSource> visuals;
        Check(!bag.TryTransferIngredientsToRestaurant(boundary, out visuals) && visuals.Count == 0 && bag.GetItemCount(Food) == 1 &&
            bag.GetItemCount(Egg) == 1 && boundary.GetCount(Egg) == 0, "Rejected transfers leave every backpack and stock quantity untouched.");
        CheckPermanentAndLegacy();
        panel.potsList.Clear(); panel.potsList.AddRange(originalPots);
        Check(LevelManager.Instance.TryEnterLevel("Layer1"), "The existing exploration flow must still accept entry after restaurant use.");
        SetPhase("dungeon", 60);
    }
    private static void ValidateDungeon()
    {
        if (LevelManager.Instance.IsTransitioning() || !RunSessionManager.Instance.IsActive) return;
        Check(stock.GetCount(Food) == 7 && stock.GetCount(Egg) == 4, "Restaurant stock persists across scene loads.");
        Check(bag.GetItemCount(Food) == 1 && bag.GetItemCount(Egg) == 1, "Only actually carried food remains in the exploration bag.");
        death = GameplayDeathPresentationProbe.Begin(FindPlayer(SceneManager.GetSceneByName("Layer1")));
        SetPhase("death", 15);
    }
    private static void ReturnFromDungeon()
    {
        Check(stock.GetCount(Food) == 7 && stock.GetCount(Egg) == 4, "Dungeon death cannot reduce restaurant food.");
        Check(RunSessionManager.Instance.ReturnToTown(), "The existing death settlement must return to town.");
        SetPhase("home", 60);
        Debug.Log("RESTAURANT_STOCK_EXPLORATION_ISOLATION_PASS");
    }
    private static void CheckPermanentAndLegacy()
    {
        Check(GameValManager.Instance.GetResourceCount(Food) == foodBank && GameValManager.Instance.GetResourceCount(ResourceType.Money) == moneyBank &&
            bag.GetItemCount(ResourceType.watermelonJ) == 2, "Restaurant operations cannot debit permanent materials/currency or legacy slot items.");
    }
    private static DishRecipe Recipe(int amount) => new DishRecipe
    {
        dishID = 90201, dishName = "Validation meal", locked = false,
        dishIcon = GameValManager.Instance.GetResourceInfo(Food).Icon,
        ingredients = new List<DishIngredient> { new DishIngredient { resourceType = Food, requiredCount = amount } }
    };
    private static IList Queue() => (IList)typeof(RestaurantPanel).GetField("_cookQueueData", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
    private static void ClearQueue() { foreach (object row in Queue()) row.GetType().GetMethod("Clear").Invoke(row, null); }
    private static void FillQueue()
    {
        foreach (object row in Queue())
        {
            row.GetType().GetField("recipe").SetValue(row, new DishRecipe { dishID = 99901, dishName = "Other meal" });
            row.GetType().GetField("count").SetValue(row, int.MaxValue);
        }
    }
    private static int QueueCount(DishRecipe recipe)
    {
        int count = 0;
        foreach (object row in Queue())
            if (ReferenceEquals(row.GetType().GetField("recipe").GetValue(row), recipe)) count += (int)row.GetType().GetField("count").GetValue(row);
        return count;
    }
    private static TopDownController FindPlayer(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (TopDownController candidate in root.GetComponentsInChildren<TopDownController>(true))
                if (candidate.isActiveAndEnabled && candidate.CompareTag("Player")) return candidate;
        return null;
    }
    private static Dictionary<ResourceType, long> Counts(ResourceType type, long count) => new Dictionary<ResourceType, long> { { type, count } };
    private static void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
    private static void SetPhase(string phase, double timeout, double wait = 0)
    {
        SessionState.SetString(Key + "phase", phase);
        SessionState.SetString(Key + "deadline", (EditorApplication.timeSinceStartup + timeout).ToString("R", CultureInfo.InvariantCulture));
        SessionState.SetString(Key + "ready", (EditorApplication.timeSinceStartup + wait).ToString("R", CultureInfo.InvariantCulture));
        Debug.Log("RESTAURANT_STAGE1_PHASE " + phase);
    }
    private static double ReadTime(string name) => double.Parse(SessionState.GetString(Key + name, "0"), CultureInfo.InvariantCulture);
    private static void OnLog(string message, string stack, LogType type)
    {
        if (EditorApplication.isPlaying && Phase != "exit" && !string.IsNullOrEmpty(Phase) &&
            (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)) SessionState.SetString(Key + "error", message + "\n" + stack);
    }
    private static void Finish(Exception error)
    {
        SessionState.SetInt(Key + "result", error == null ? 0 : 1);
        Debug.Log(error == null ? "RESTAURANT_STAGE1_UNITY_PASS" : "RESTAURANT_STAGE1_UNITY_FAIL " + error);
        SessionState.SetString(Key + "phase", "exit");
        if (EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.ExitPlaymode(); else Exit();
    }
    private static void Exit() { if (Phase != "exit") return; SessionState.EraseString(Key + "phase"); EditorApplication.Exit(SessionState.GetInt(Key + "result", 1)); }
}
#endif
