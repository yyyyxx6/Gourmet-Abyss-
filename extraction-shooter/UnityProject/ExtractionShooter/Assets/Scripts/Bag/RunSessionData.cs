using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

public sealed class RunSessionData
{
    private Dictionary<ResourceType, int> initialIngredients = new Dictionary<ResourceType, int>();
    private readonly Dictionary<ResourceType, int> gatheredCounts = new Dictionary<ResourceType, int>();
    private readonly HashSet<int> knownRecipeIds = new HashSet<int>();
    private readonly HashSet<PetType> knownPetTypes = new HashSet<PetType>();
    private readonly HashSet<int> newRecipeIds = new HashSet<int>();
    private readonly HashSet<PetType> newPetTypes = new HashSet<PetType>();
    private RunResultSnapshot completedResult;

    public bool IsActive { get; private set; }
    public string LevelId { get; private set; } = string.Empty;
    public double ElapsedSeconds { get; private set; }
    public int KillCount { get; private set; }

    public void Begin(
        string levelId,
        IReadOnlyDictionary<ResourceType, int> initialIngredients,
        IEnumerable<int> knownRecipeIds = null,
        IEnumerable<PetType> knownPetTypes = null)
    {
        if (string.IsNullOrWhiteSpace(levelId))
            throw new ArgumentException("A level ID is required.", nameof(levelId));

        Dictionary<ResourceType, int> baseline = CopyCounts(initialIngredients, nameof(initialIngredients));
        HashSet<int> recipes = knownRecipeIds == null ? new HashSet<int>() : new HashSet<int>(knownRecipeIds);
        HashSet<PetType> pets = knownPetTypes == null ? new HashSet<PetType>() : new HashSet<PetType>(knownPetTypes);

        LevelId = levelId;
        this.initialIngredients = baseline;
        this.knownRecipeIds.Clear();
        this.knownRecipeIds.UnionWith(recipes);
        this.knownPetTypes.Clear();
        this.knownPetTypes.UnionWith(pets);
        gatheredCounts.Clear();
        newRecipeIds.Clear();
        newPetTypes.Clear();
        ElapsedSeconds = 0;
        KillCount = 0;
        completedResult = null;
        IsActive = true;
    }

    public void AdvanceTime(double seconds)
    {
        if (!IsActive || seconds <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds)) return;
        ElapsedSeconds = seconds > double.MaxValue - ElapsedSeconds ? double.MaxValue : ElapsedSeconds + seconds;
    }

    public void RecordKill()
    {
        if (IsActive && KillCount < int.MaxValue)
            KillCount++;
    }

    public void RecordGathered(ResourceType type, int acceptedAmount)
    {
        if (!IsActive || acceptedAmount <= 0 || type == ResourceType.None) return;
        int previous;
        gatheredCounts.TryGetValue(type, out previous);
        gatheredCounts[type] = (int)Math.Min(int.MaxValue, (long)previous + acceptedAmount);
    }

    public void RecordRecipeUnlocked(int id)
    {
        if (IsActive && id >= 0 && !knownRecipeIds.Contains(id))
            newRecipeIds.Add(id);
    }

    public void RecordPetUnlocked(PetType type)
    {
        if (IsActive && type != PetType.None && !knownPetTypes.Contains(type))
            newPetTypes.Add(type);
    }

    public RunResultSnapshot Capture(InventorySnapshot inventory, IReadOnlyDictionary<ResourceType, int> currentGathered)
    {
        if (!IsActive)
            throw new InvalidOperationException("Only an active run can be captured.");
        if (inventory == null)
            throw new ArgumentNullException(nameof(inventory));

        Dictionary<ResourceType, int> retained = CopyCounts(currentGathered, nameof(currentGathered));
        long ingredientDelta = 0;
        foreach (KeyValuePair<ResourceType, int> entry in inventory.GetCounts())
            ingredientDelta += entry.Value;
        foreach (KeyValuePair<ResourceType, int> entry in initialIngredients)
            ingredientDelta -= entry.Value;

        List<int> recipes = new List<int>(newRecipeIds);
        recipes.Sort();
        List<PetType> pets = new List<PetType>(newPetTypes);
        pets.Sort();

        return new RunResultSnapshot(LevelId, ElapsedSeconds, KillCount, inventory,
            ingredientDelta, gatheredCounts, retained, recipes, pets);
    }

    public RunResultSnapshot Complete(InventorySnapshot inventory, IReadOnlyDictionary<ResourceType, int> currentGathered)
    {
        if (completedResult != null) return completedResult;

        completedResult = Capture(inventory, currentGathered);
        IsActive = false;
        return completedResult;
    }

    private static Dictionary<ResourceType, int> CopyCounts(IReadOnlyDictionary<ResourceType, int> source, string parameterName)
    {
        if (source == null)
            throw new ArgumentNullException(parameterName);

        Dictionary<ResourceType, int> copy = new Dictionary<ResourceType, int>();
        foreach (KeyValuePair<ResourceType, int> entry in source)
        {
            if (entry.Value < 0)
                throw new ArgumentOutOfRangeException(parameterName, "Resource counts cannot be negative.");
            if (entry.Value > 0 && entry.Key != ResourceType.None)
                copy.Add(entry.Key, entry.Value);
        }
        return copy;
    }
}

public sealed class RunResultSnapshot
{
    public string LevelId { get; }
    public double ElapsedSeconds { get; }
    public int KillCount { get; }
    public InventorySnapshot Inventory { get; }
    public long IngredientDelta { get; }
    public IReadOnlyDictionary<ResourceType, int> GatheredCounts { get; }
    public IReadOnlyDictionary<ResourceType, int> RetainedGatheredCounts { get; }
    public IReadOnlyList<int> NewRecipeIds { get; }
    public IReadOnlyList<PetType> NewPetTypes { get; }

    internal RunResultSnapshot(
        string levelId,
        double elapsedSeconds,
        int killCount,
        InventorySnapshot inventory,
        long ingredientDelta,
        Dictionary<ResourceType, int> gatheredCounts,
        Dictionary<ResourceType, int> retainedGatheredCounts,
        List<int> newRecipeIds,
        List<PetType> newPetTypes)
    {
        LevelId = levelId;
        ElapsedSeconds = elapsedSeconds;
        KillCount = killCount;
        Inventory = inventory;
        IngredientDelta = ingredientDelta;
        GatheredCounts = new ReadOnlyDictionary<ResourceType, int>(new Dictionary<ResourceType, int>(gatheredCounts));
        RetainedGatheredCounts = new ReadOnlyDictionary<ResourceType, int>(new Dictionary<ResourceType, int>(retainedGatheredCounts));
        NewRecipeIds = new ReadOnlyCollection<int>(new List<int>(newRecipeIds));
        NewPetTypes = new ReadOnlyCollection<PetType>(new List<PetType>(newPetTypes));
    }
}
