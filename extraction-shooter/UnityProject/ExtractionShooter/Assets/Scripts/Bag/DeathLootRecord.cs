using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

/// <summary>One recoverable crate, independent of the lifetime of its scene object.</summary>
public sealed class DeathLootRecord
{
    public string Id { get; }
    public string LevelId { get; }
    public Vector3 Position { get; }
    public IReadOnlyDictionary<ResourceType, int> Ingredients { get; }
    public IReadOnlyDictionary<ResourceType, int> Gathered { get; }

    public DeathLootRecord(string id, string levelId, Vector3 position,
        IReadOnlyDictionary<ResourceType, int> ingredients, IReadOnlyDictionary<ResourceType, int> gathered)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A crate ID is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(levelId)) throw new ArgumentException("A level ID is required.", nameof(levelId));
        if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z) ||
            float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsInfinity(position.z))
            throw new ArgumentOutOfRangeException(nameof(position));
        Id = id;
        LevelId = levelId;
        Position = position;
        Ingredients = Copy(ingredients, true);
        Gathered = Copy(gathered, false);
    }

    private static IReadOnlyDictionary<ResourceType, int> Copy(IReadOnlyDictionary<ResourceType, int> source, bool ingredients)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        var copy = new Dictionary<ResourceType, int>();
        foreach (KeyValuePair<ResourceType, int> entry in source)
        {
            if (entry.Value < 0 || (ingredients ? !ResourceStorageRules.IsIngredient(entry.Key) : !ResourceStorageRules.IsGathered(entry.Key)))
                throw new ArgumentException("Invalid death-crate contents.", nameof(source));
            if (entry.Value > 0) copy.Add(entry.Key, entry.Value);
        }
        return new ReadOnlyDictionary<ResourceType, int>(copy);
    }
}

public sealed class DeathLootReceipt
{
    public IReadOnlyDictionary<ResourceType, int> RecoveredIngredients { get; }
    public IReadOnlyDictionary<ResourceType, int> RecoveredGathered { get; }
    public IReadOnlyDictionary<ResourceType, int> DiscardedIngredients { get; }

    public DeathLootReceipt(InventoryPackResult packing, IReadOnlyDictionary<ResourceType, int> gathered)
    {
        RecoveredIngredients = packing.Accepted;
        DiscardedIngredients = packing.Remaining;
        var copy = new Dictionary<ResourceType, int>();
        foreach (KeyValuePair<ResourceType, int> entry in gathered) copy.Add(entry.Key, entry.Value);
        RecoveredGathered = new ReadOnlyDictionary<ResourceType, int>(copy);
    }
}
