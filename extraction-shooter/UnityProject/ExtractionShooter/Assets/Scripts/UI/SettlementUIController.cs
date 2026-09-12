using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class SettlementUIController : MonoBehaviour
{
    public CanvasGroup canvasGroup;
    public EventSystem eventSystem;
    public SettlementPresentationCatalog presentationCatalog;
    public Text titleText;
    public Text inventorySummaryText;
    public RectTransform inventoryContent;
    public RectTransform inventorySlotTemplate;
    public ScrollRect inventoryScroll;
    public GameObject petSection;
    public RectTransform petContent;
    public GameObject recipeSection;
    public RectTransform recipeContent;
    public GameObject gatheredSection;
    public RectTransform gatheredContent;
    public RectTransform rewardRowTemplate;
    public RectTransform detailsContent;
    public ScrollRect detailsScroll;
    public Text ingredientDeltaText;
    public Text durationText;
    public Text killsText;
    public Text statusText;
    public Button retryButton;
    public Button homeButton;

    private Action retryAction;
    private Action homeAction;
    private Coroutine fadeRoutine;
    private bool isBusy;
    private readonly List<GameObject> generatedEntries = new List<GameObject>();
    private readonly List<EventSystem> suspendedEventSystems = new List<EventSystem>();
    private readonly List<Transform> newMarkers = new List<Transform>();

    public bool IsVisible { get; private set; }

    public static SettlementUIController EnsureExists(Transform parent)
    {
        SettlementUIController existing = FindObjectOfType<SettlementUIController>(true);
        if (existing != null) return existing;

        GameObject prefab = Resources.Load<GameObject>("UI/SettlementPanel");
        if (prefab == null)
        {
            Debug.LogError("SettlementPanel prefab is missing from Resources/UI.");
            return null;
        }
        GameObject instance = Instantiate(prefab, parent, false);
        instance.name = "SettlementPanel";
        SettlementUIController controller = instance.GetComponent<SettlementUIController>();
        if (controller == null)
        {
            Debug.LogError("SettlementPanel prefab has no SettlementUIController.");
            Destroy(instance);
            return null;
        }
        instance.SetActive(false);
        return controller;
    }

    private void Awake()
    {
        retryButton.onClick.AddListener(OnRetryClicked);
        homeButton.onClick.AddListener(OnHomeClicked);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        if (IsVisible)
        {
            if (canvasGroup != null) canvasGroup.alpha = 1f;
            ClaimInput();
        }
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        fadeRoutine = null;
        ReleaseInput();
    }

    private void Update()
    {
        if (!IsVisible) return;
        float scale = 1f + Mathf.Sin(Time.unscaledTime * 3.2f) * 0.035f;
        foreach (Transform marker in newMarkers)
            if (marker != null && marker.gameObject.activeInHierarchy) marker.localScale = Vector3.one * scale;
    }

    public void Show(RunResultSnapshot result, RunEndReason reason,
        IReadOnlyDictionary<ResourceType, int> ownedGathered, Action onRetry, Action onHome)
    {
        Dictionary<ResourceType, long> totals = null;
        if (ownedGathered != null)
        {
            totals = new Dictionary<ResourceType, long>();
            foreach (KeyValuePair<ResourceType, int> entry in ownedGathered)
                totals.Add(entry.Key, entry.Value);
        }
        Show(result, reason, totals, onRetry, onHome);
    }

    public void Show(RunResultSnapshot result, RunEndReason reason,
        IReadOnlyDictionary<ResourceType, long> ownedGathered, Action onRetry, Action onHome)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        if (presentationCatalog == null)
            presentationCatalog = Resources.Load<SettlementPresentationCatalog>("UI/SettlementPresentationCatalog");
        ClearEntries();
        retryAction = onRetry;
        homeAction = onHome;
        SetStatusMessage(string.Empty);
        titleText.text = reason == RunEndReason.Death ? "\u5192\u9669\u7ed3\u675f" : "\u6210\u529f\u64a4\u79bb";
        titleText.color = reason == RunEndReason.Death
            ? new Color32(135, 49, 49, 255) : new Color32(39, 77, 58, 255);

        ConfigureInventoryLayout(result.Inventory.Slots.Count);
        int occupied = 0;
        foreach (InventorySlotSnapshot slot in result.Inventory.Slots)
        {
            RectTransform entry = CloneEntry(inventorySlotTemplate, inventoryContent);
            Image icon = entry.Find("Icon").GetComponent<Image>();
            Text count = entry.Find("Count").GetComponent<Text>();
            Text fallbackName = entry.Find("Name").GetComponent<Text>();
            icon.gameObject.SetActive(false);
            count.gameObject.SetActive(false);
            fallbackName.gameObject.SetActive(false);
            if (slot.IsEmpty) continue;
            occupied++;
            ResourceItem item = GetResourceInfo(slot.ItemType);
            Sprite sprite = GetResourceIcon(slot.ItemType, item);
            if (sprite != null)
            {
                icon.sprite = sprite;
                icon.gameObject.SetActive(true);
            }
            else
            {
                fallbackName.text = GetResourceName(slot.ItemType, item);
                fallbackName.gameObject.SetActive(true);
            }
            count.text = slot.Count.ToString(CultureInfo.InvariantCulture);
            count.gameObject.SetActive(true);
        }
        inventorySummaryText.text = occupied + " / " + result.Inventory.Slots.Count;

        petSection.SetActive(result.NewPetTypes.Count > 0);
        foreach (PetType pet in result.NewPetTypes)
        {
            string name = pet == PetType.FlyingCompanion ? "\u98de\u884c\u968f\u4ece" : "\u65b0\u5ba0\u7269";
            Sprite icon = null;
            SettlementPresentationCatalog.PetPresentation entry;
            if (presentationCatalog != null && presentationCatalog.TryGetPet(pet, out entry))
            {
                if (!string.IsNullOrWhiteSpace(entry.displayName)) name = entry.displayName;
                icon = entry.icon;
            }
            CreateRewardRow(petContent, name, icon, string.Empty, string.Empty, true);
        }

        recipeSection.SetActive(result.NewRecipeIds.Count > 0);
        foreach (int recipeId in result.NewRecipeIds)
        {
            DishRecipe recipe = FindRecipe(recipeId);
            string name = recipe != null && !string.IsNullOrWhiteSpace(recipe.dishName)
                ? recipe.dishName : "\u65b0\u83dc\u8c31";
            CreateRewardRow(recipeContent, name, recipe == null ? null : recipe.dishIcon,
                string.Empty, string.Empty, true);
        }

        List<ResourceType> gatheredTypes = new List<ResourceType>();
        foreach (KeyValuePair<ResourceType, int> entry in result.GatheredCounts)
            if (entry.Value > 0) gatheredTypes.Add(entry.Key);
        gatheredTypes.Sort();
        gatheredSection.SetActive(gatheredTypes.Count > 0);
        foreach (ResourceType type in gatheredTypes)
        {
            ResourceItem item = GetResourceInfo(type);
            long owned = 0;
            if (ownedGathered != null) ownedGathered.TryGetValue(type, out owned);
            string outcome = string.Empty;
            if (reason == RunEndReason.Death)
            {
                int retained = 0;
                result.RetainedGatheredCounts.TryGetValue(type, out retained);
                long lost = Math.Max(0L, (long)result.GatheredCounts[type] - retained);
                outcome = "\u5e26\u51fa " + retained.ToString(CultureInfo.InvariantCulture) +
                    "  \u00b7  \u9057\u5931 " + lost.ToString(CultureInfo.InvariantCulture);
            }
            CreateRewardRow(gatheredContent, GetResourceName(type, item), GetResourceIcon(type, item),
                "\u672c\u5c40 +" + result.GatheredCounts[type].ToString(CultureInfo.InvariantCulture),
                "\u62e5\u6709 " + owned.ToString(CultureInfo.InvariantCulture), false, outcome);
        }

        ingredientDeltaText.text = result.IngredientDelta > 0
            ? "+" + result.IngredientDelta.ToString(CultureInfo.InvariantCulture)
            : result.IngredientDelta.ToString(CultureInfo.InvariantCulture);
        ingredientDeltaText.color = result.IngredientDelta < 0
            ? new Color32(171, 46, 46, 255) : new Color32(39, 77, 58, 255);
        durationText.text = FormatDuration(result.ElapsedSeconds);
        killsText.text = result.KillCount.ToString(CultureInfo.InvariantCulture);

        ClaimInput();
        gameObject.SetActive(true);
        IsVisible = true;
        if (eventSystem != null) EventSystem.current = eventSystem;
        SetBusy(false);
        canvasGroup.blocksRaycasts = true;
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(detailsContent);
        LayoutRebuilder.ForceRebuildLayoutImmediate(inventoryContent);
        inventoryScroll.verticalNormalizedPosition = 1f;
        detailsScroll.verticalNormalizedPosition = 1f;
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeIn());
        if (eventSystem != null)
            eventSystem.SetSelectedGameObject(retryButton.interactable ? retryButton.gameObject : homeButton.gameObject);
    }

    public void SetBusy(bool busy)
    {
        isBusy = busy;
        retryButton.interactable = !busy && retryAction != null;
        homeButton.interactable = !busy && homeAction != null;
        canvasGroup.interactable = !busy;
    }

    public void SetStatusMessage(string message)
    {
        if (statusText == null) return;
        statusText.text = message ?? string.Empty;
        statusText.gameObject.SetActive(!string.IsNullOrWhiteSpace(message));
    }

    public void Hide()
    {
        IsVisible = false;
        retryAction = null;
        homeAction = null;
        if (fadeRoutine != null)
        {
            StopCoroutine(fadeRoutine);
            fadeRoutine = null;
        }
        if (eventSystem != null) eventSystem.SetSelectedGameObject(null);
        canvasGroup.blocksRaycasts = false;
        ReleaseInput();
        gameObject.SetActive(false);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (IsVisible) ClaimInput();
    }

    private void ClaimInput()
    {
        if (eventSystem == null) return;
        foreach (EventSystem other in FindObjectsOfType<EventSystem>(true))
        {
            if (other == eventSystem || !other.isActiveAndEnabled) continue;
            // Only enabled systems are suspended; disabled scene systems stay disabled.
            if (!suspendedEventSystems.Contains(other)) suspendedEventSystems.Add(other);
            other.enabled = false;
        }
        eventSystem.enabled = true;
        if (eventSystem.isActiveAndEnabled) EventSystem.current = eventSystem;
    }

    private void ReleaseInput()
    {
        if (eventSystem != null) eventSystem.enabled = false;
        foreach (EventSystem other in suspendedEventSystems)
            if (other != null) other.enabled = true;
        suspendedEventSystems.Clear();
    }

    private void OnRetryClicked() { InvokeAction(retryAction); }
    private void OnHomeClicked() { InvokeAction(homeAction); }

    private void InvokeAction(Action action)
    {
        if (!IsVisible || isBusy || action == null) return;
        SetBusy(true);
        try { action(); }
        catch
        {
            SetBusy(false);
            throw;
        }
    }

    private IEnumerator FadeIn()
    {
        canvasGroup.alpha = 0f;
        float elapsed = 0f;
        while (elapsed < 0.16f)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Clamp01(elapsed / 0.16f);
            yield return null;
        }
        canvasGroup.alpha = 1f;
        fadeRoutine = null;
    }

    private RectTransform CloneEntry(RectTransform template, Transform parent)
    {
        RectTransform entry = Instantiate(template, parent, false);
        entry.gameObject.SetActive(true);
        generatedEntries.Add(entry.gameObject);
        return entry;
    }

    private void ClearEntries()
    {
        newMarkers.Clear();
        foreach (GameObject entry in generatedEntries)
        {
            if (entry == null) continue;
            entry.SetActive(false);
            Destroy(entry);
        }
        generatedEntries.Clear();
    }

    private void CreateRewardRow(Transform parent, string name, Sprite sprite, string gained, string total, bool isNew,
        string outcome = "")
    {
        RectTransform row = CloneEntry(rewardRowTemplate, parent);
        bool hasOutcome = !string.IsNullOrEmpty(outcome);
        float height = isNew ? 64f : hasOutcome ? 104f : 76f;
        LayoutElement layout = row.GetComponent<LayoutElement>();
        layout.minHeight = height;
        layout.preferredHeight = height;
        Image icon = row.Find("Icon").GetComponent<Image>();
        icon.sprite = sprite;
        icon.color = sprite != null ? Color.white : new Color32(132, 111, 76, 60);
        icon.rectTransform.anchoredPosition = new Vector2(34f, hasOutcome ? 10f : 0f);
        Text placeholder = icon.transform.Find("Placeholder").GetComponent<Text>();
        placeholder.text = string.IsNullOrEmpty(name) ? "\u7269" : name.Substring(0, 1);
        placeholder.gameObject.SetActive(sprite == null);
        Text nameText = row.Find("Name").GetComponent<Text>();
        nameText.text = name;
        nameText.rectTransform.offsetMin = new Vector2(80f, hasOutcome ? 32f : 8f);
        nameText.rectTransform.offsetMax = new Vector2(isNew ? -12f : -218f, -8f);
        row.Find("Gain").GetComponent<Text>().text = gained;
        row.Find("Total").GetComponent<Text>().text = total;
        Text outcomeText = row.Find("Outcome").GetComponent<Text>();
        outcomeText.text = outcome;
        outcomeText.gameObject.SetActive(hasOutcome);
        Transform marker = icon.transform.Find("New");
        marker.gameObject.SetActive(isNew);
        if (isNew) newMarkers.Add(marker);
    }

    private void ConfigureInventoryLayout(int slotCount)
    {
        GridLayoutGroup grid = inventoryContent.GetComponent<GridLayoutGroup>();
        if (grid == null) return;
        int columns = slotCount <= 1 ? 1 : slotCount <= 4 ? 2 : slotCount <= 9 ? 3 : 4;
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = columns;
        grid.childAlignment = TextAnchor.UpperCenter;
        grid.cellSize = slotCount <= 4 ? new Vector2(160f, 164f)
            : slotCount <= 6 ? new Vector2(144f, 148f) : new Vector2(124f, 128f);
    }

    private static ResourceItem GetResourceInfo(ResourceType type)
    {
        return GameValManager.Instance == null ? null : GameValManager.Instance.GetResourceInfo(type);
    }

    private Sprite GetResourceIcon(ResourceType type, ResourceItem item)
    {
        SettlementPresentationCatalog.ResourcePresentation entry;
        if (presentationCatalog != null && presentationCatalog.TryGetResource(type, out entry) && entry.icon != null)
            return entry.icon;
        return item == null ? null : item.Icon;
    }

    private string GetResourceName(ResourceType type, ResourceItem item)
    {
        SettlementPresentationCatalog.ResourcePresentation entry;
        if (presentationCatalog != null && presentationCatalog.TryGetResource(type, out entry) &&
            !string.IsNullOrWhiteSpace(entry.displayName)) return entry.displayName;
        if (item != null && !string.IsNullOrWhiteSpace(item.name)) return item.name;
        switch (type)
        {
            case ResourceType.LootMushroom: return "\u8611\u83c7";
            case ResourceType.LootEggSmall:
            case ResourceType.LootEggBig: return "\u86cb";
            case ResourceType.Loot_RatMeat: return "\u8089";
            case ResourceType.Loot_Paste: return "\u9762\u7cca";
            case ResourceType.LootPumkin: return "\u6728\u6750";
            case ResourceType.Money: return "\u91d1\u5e01";
            default: return ResourceStorageRules.IsIngredient(type) ? "\u98df\u6750" : "\u91c7\u96c6\u7269";
        }
    }

    private static DishRecipe FindRecipe(int id)
    {
        if (RestaurantPanel.instance == null || RestaurantPanel.instance.dishRecipes == null) return null;
        foreach (DishRecipe recipe in RestaurantPanel.instance.dishRecipes)
            if (recipe != null && recipe.dishID == id) return recipe;
        return null;
    }

    private static string FormatDuration(double elapsed)
    {
        long seconds = elapsed >= long.MaxValue ? long.MaxValue : (long)Math.Max(0d, elapsed);
        long hours = seconds / 3600;
        long minutes = seconds / 60 % 60;
        long remainder = seconds % 60;
        return hours > 0 ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", hours, minutes, remainder)
            : string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}", minutes, remainder);
    }
}
