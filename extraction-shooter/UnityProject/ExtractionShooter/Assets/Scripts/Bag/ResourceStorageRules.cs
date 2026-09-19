public enum RunResourceCategory
{
    Invalid,
    Ingredient,
    Gathered,
    Permanent,
    LegacySlot
}

public static class ResourceStorageRules
{
    public static RunResourceCategory GetCategory(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.LootMushroom:
            case ResourceType.LootChickenLeg:
            case ResourceType.LootEggSmall:
            case ResourceType.LootEggBig:
            case ResourceType.LootTomato:
            case ResourceType.LootFish:
            case ResourceType.LootCrabStick:
            case ResourceType.LootChicken:
            case ResourceType.LootSnailMeat:
            case ResourceType.Loot_RatMeat:
            case ResourceType.Loot_Paste:
                return RunResourceCategory.Ingredient;
            // These legacy plant types retain their no-slot collection behavior.
            case ResourceType.LootOnion:
            case ResourceType.LootPear:
            case ResourceType.LootPineapple:
            case ResourceType.LootRadish:
            case ResourceType.LootSweetPepper:
            case ResourceType.LootWatermelon:
            // The live UpGround resource table uses ID 20 for wood, not food.
            case ResourceType.LootPumkin:
                return RunResourceCategory.Gathered;
            case ResourceType.Money:
            case ResourceType.Furniture_Clock:
            case ResourceType.Furniture_Chair:
                return RunResourceCategory.Permanent;
            case ResourceType.watermelonJ:
            case ResourceType.orangeJ:
            case ResourceType.tomatoJ:
            case ResourceType.mushroom:
                return RunResourceCategory.LegacySlot;
            default:
                return RunResourceCategory.Invalid;
        }
    }

    public static bool IsIngredient(ResourceType type)
    {
        return GetCategory(type) == RunResourceCategory.Ingredient;
    }

    public static bool IsGathered(ResourceType type)
    {
        return GetCategory(type) == RunResourceCategory.Gathered;
    }

    public static bool UsesSlots(ResourceType type)
    {
        RunResourceCategory category = GetCategory(type);
        return category == RunResourceCategory.Ingredient || category == RunResourceCategory.LegacySlot;
    }
}
