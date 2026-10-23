using Game.Modules;
using UnityEngine;
using UnityEngine.Events;

// Only this adapter knows restaurant services. Module prefabs have no scene/service references.
public sealed class RestaurantModuleAdapter : MonoBehaviour, IQueueIngredientPresentation
{
    public ModulePair pair;
    public RestaurantEntryPoint entry;
    public RestaurantPanel restaurant;
    public GameObject legacyHUD;
    public ShopInteraction shop;
    public RestaurantDecorationPanelUI decoration;
    public ModuleLegacyPopup recipesPopup;
    public ModuleLegacyPopup decorationPopup;
    private bool wasOpen;
    private RunIngredientDebugUI runtimeDebug;
    private IQueueIngredientPresentation previousFlightPresentation;

    private void OnEnable()
    {
        if (restaurant == null) return;
        previousFlightPresentation = restaurant.QueueIngredientPresentation;
        restaurant.QueueIngredientPresentation = this;
    }

    public bool CanPresentQueueIngredients => isActiveAndEnabled && entry != null && entry.IsEntered;

    public System.Collections.IEnumerator PlayQueueIngredients(
        System.Collections.Generic.List<InventoryManager.IngredientFlySource> sources, DishQueueSlot target,
        float duration = .6f, float spawnInterval = 0f, float landingDuration = .2f)
    {
        // Keep coroutine ownership with the existing business host: closing HUD must not cancel an order.
        yield return RestaurantIngredientTransferPresentation.PlayQueue(sources, target.transform, duration, spawnInterval);
        yield return new WaitForSecondsRealtime(Mathf.Max(.01f, landingDuration));
    }

    private void Start()
    {
        Connect("exit", Exit);
        Connect("decoration", Decoration);
        Connect("recipes", Recipes);
        Connect("cooking", OpenRecipes);
        if(decorationPopup!=null)decorationPopup.Closed+=OnDecorationClosed;
        var management = pair.hud.GetAction("management");
        if (management != null)
        {
            var colors=management.colors;colors.disabledColor=colors.normalColor;management.colors=colors;
            management.interactable = false;
        }
        pair.SetOpen(false);
    }
    private void Connect(string id, UnityAction callback)
    {
        var button = pair.hud.GetAction(id);
        if (button != null) button.onClick.AddListener(callback);
    }
    private void Disconnect(string id, UnityAction callback)
    {
        if (pair == null || pair.hud == null) return;
        var button = pair.hud.GetAction(id);
        if (button != null) button.onClick.RemoveListener(callback);
    }
    private void LateUpdate()
    {
        bool open = entry != null && entry.IsEntered;
        if (open != wasOpen)
        {
            if (!open && recipesPopup != null) recipesPopup.Close();
            if (!open && decorationPopup != null) decorationPopup.Close();
            pair.SetOpen(open); wasOpen = open;
        }
        if (!open) return;
        // The decorative module bag also follows the entry transfer lifecycle.
        if (pair != null && pair.hud != null)
        {
            foreach (Transform candidate in pair.hud.GetComponentsInChildren<Transform>(true))
            {
                if (candidate.name != "Bag") continue;
                bool showBag = entry.IsTransferringIngredients;
                if (candidate.gameObject.activeSelf != showBag) candidate.gameObject.SetActive(showBag);
                break;
            }
        }
        if(runtimeDebug==null)runtimeDebug=FindObjectOfType<RunIngredientDebugUI>(true);
        if(runtimeDebug!=null&&pair.presentation!=null)pair.presentation.RegisterSuspended(runtimeDebug.GetComponent<Canvas>());
        var money=pair.hud.GetText("money");var progress=pair.hud.GetImage("progress");
        if (money != null && GameValManager.Instance != null)
            money.text = GameValManager.Instance.GetResourceCount(ResourceType.Money).ToString();
        if (progress != null && restaurant != null && restaurant.cookingProgressImage != null)
            progress.fillAmount = restaurant.cookingProgressImage.fillAmount;
    }
    private void Exit() { entry.LeaveRestaurant(); }
    private void Decoration()
    {
        if(decorationPopup==null||decoration==null)return;
        if(decorationPopup.IsOpen)decorationPopup.Close();
        else {if(recipesPopup!=null)recipesPopup.Close();decoration.ShowPanel();decorationPopup.Open();}
    }
    private void OnDecorationClosed(){if(decoration!=null)decoration.HidePanel();}
    private void Recipes() { if(decorationPopup!=null)decorationPopup.Close();if (recipesPopup != null) recipesPopup.Toggle(); }
    private void OpenRecipes()
    {
        if (decorationPopup != null) decorationPopup.Close();
        if (restaurant != null) restaurant.RefreshOnOpen();
        if (recipesPopup != null && !recipesPopup.IsOpen) recipesPopup.Open();
    }
    private void OnDisable()
    {
        if (restaurant != null && ReferenceEquals(restaurant.QueueIngredientPresentation, this))
            restaurant.QueueIngredientPresentation = previousFlightPresentation;
        if (pair != null) pair.SetOpen(false);
        if (recipesPopup != null) recipesPopup.Close();
        if (decorationPopup != null) decorationPopup.Close();
        if (entry != null && entry.IsEntered) entry.LeaveRestaurant();
        wasOpen = false;
    }
    private void OnDestroy()
    {
        Disconnect("exit", Exit); Disconnect("decoration", Decoration);
        Disconnect("recipes", Recipes);
        Disconnect("cooking", OpenRecipes);
        if(decorationPopup!=null)decorationPopup.Closed-=OnDecorationClosed;
    }
}
