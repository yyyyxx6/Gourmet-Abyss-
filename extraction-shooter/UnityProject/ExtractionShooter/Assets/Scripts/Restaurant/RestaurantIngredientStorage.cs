using System;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;

/// <summary>Session-persistent restaurant inventory. Restaurant UI and orders use this service.</summary>
public sealed class RestaurantIngredientStorage : PersistentMonoSingleton<RestaurantIngredientStorage>
{
    public RestaurantIngredientStore Store { get; } = new RestaurantIngredientStore();
    public event Action Changed;
    private bool isTransferring;

    public static RestaurantIngredientStorage EnsureExists()
    {
        if (Instance != null) return Instance;
        return new GameObject("RestaurantIngredientStorage").AddComponent<RestaurantIngredientStorage>();
    }

    public long GetCount(ResourceType type) => Store.GetCount(type);

    public static string GetDisplayName(ResourceType type)
    {
        ResourceItem info = GameValManager.Instance != null ? GameValManager.Instance.GetResourceInfo(type) : null;
        if (info != null && !string.IsNullOrWhiteSpace(info.name)) return info.name;
        switch (type)
        {
            case ResourceType.LootMushroom: return "\u8611\u83c7";
            case ResourceType.LootChickenLeg: return "\u9e21\u817f";
            case ResourceType.LootEggSmall: return "\u5c0f\u86cb";
            case ResourceType.LootEggBig: return "\u5927\u86cb";
            case ResourceType.LootTomato: return "\u756a\u8304";
            case ResourceType.LootFish: return "\u9c7c";
            case ResourceType.LootCrabStick: return "\u87f9\u68d2";
            case ResourceType.LootChicken: return "\u9e21\u8089";
            case ResourceType.LootSnailMeat: return "\u8717\u725b\u8089";
            case ResourceType.Loot_RatMeat: return "\u8089";
            case ResourceType.Loot_Paste: return "\u9762\u56e2";
            default: return "\u98df\u6750";
        }
    }

    public bool TryDeposit(IReadOnlyDictionary<ResourceType, long> quantities)
    {
        if (!Store.TryDeposit(quantities)) return false;
        NotifyChanged();
        return true;
    }

    public bool CanAffordRecipe(DishRecipe recipe)
    {
        Dictionary<ResourceType, long> costs;
        return TryGetRecipeCosts(recipe, out costs) && Store.CanAfford(costs);
    }

    public bool TryReserveRecipe(DishRecipe recipe, out RestaurantIngredientStore.Reservation reservation)
    {
        reservation = null;
        Dictionary<ResourceType, long> costs;
        if (!TryGetRecipeCosts(recipe, out costs) || !Store.TryReserve(costs, out reservation)) return false;
        NotifyChanged();
        return true;
    }

    public bool Commit(RestaurantIngredientStore.Reservation reservation) => Store.Commit(reservation);

    public bool Refund(RestaurantIngredientStore.Reservation reservation)
    {
        if (!Store.Refund(reservation)) return false;
        NotifyChanged();
        return true;
    }

    public bool TryConsumeRecipe(DishRecipe recipe)
    {
        RestaurantIngredientStore.Reservation reservation;
        return TryReserveRecipe(recipe, out reservation) && Commit(reservation);
    }

    public bool TryTransferFromBackpack(InventoryManager inventory,
        out List<InventoryManager.RestaurantTransferSource> visuals)
    {
        visuals = new List<InventoryManager.RestaurantTransferSource>();
        if (isTransferring || inventory == null) return false;
        isTransferring = true;
        try
        {
            if (!inventory.TryTransferIngredientsToRestaurant(Store, out visuals)) return false;
            NotifyChanged();
            return true;
        }
        finally { isTransferring = false; }
    }

    public static bool TryGetRecipeCosts(DishRecipe recipe, out Dictionary<ResourceType, long> costs)
    {
        costs = new Dictionary<ResourceType, long>();
        if (recipe == null || recipe.ingredients == null) return false;
        foreach (DishIngredient ingredient in recipe.ingredients)
        {
            if (ingredient == null || ingredient.requiredCount < 0) return false;
            if (ingredient.requiredCount == 0) continue;
            if (!ResourceStorageRules.IsIngredient(ingredient.resourceType)) return false;
            long previous;
            costs.TryGetValue(ingredient.resourceType, out previous);
            if (previous > long.MaxValue - ingredient.requiredCount) return false;
            costs[ingredient.resourceType] = previous + ingredient.requiredCount;
        }
        return true;
    }

    private void NotifyChanged()
    {
        Action listeners = Changed;
        if (listeners == null) return;
        foreach (Action listener in listeners.GetInvocationList())
        {
            try { listener(); }
            catch (Exception error) { Debug.LogException(error, this); }
        }
    }
}
