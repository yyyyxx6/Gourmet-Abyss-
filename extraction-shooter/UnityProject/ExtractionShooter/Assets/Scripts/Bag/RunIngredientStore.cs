using System;
using System.Collections.Generic;

/// <summary>No-slot resource counts. The legacy class name is retained for compatibility.</summary>
public sealed class RunIngredientStore
{
    private Dictionary<ResourceType, int> counts = new Dictionary<ResourceType, int>();

    public event Action<ResourceType, int, int> Changed;

    public int Count { get { return counts.Count; } }

    public bool Add(ResourceType type, int amount)
    {
        if (amount <= 0 || type == ResourceType.None) return false;

        int oldCount = GetCount(type);
        int newCount = (int)Math.Min(int.MaxValue, (long)oldCount + amount);
        if (newCount == oldCount) return false;

        counts[type] = newCount;
        if (Changed != null)
            Changed(type, oldCount, newCount);
        return true;
    }

    public int GetCount(ResourceType type)
    {
        int count;
        return counts.TryGetValue(type, out count) ? count : 0;
    }

    public Dictionary<ResourceType, int> GetSnapshot()
    {
        return new Dictionary<ResourceType, int>(counts);
    }

    /// <summary>Validates and publishes a complete replacement without notifying observers.</summary>
    public Dictionary<ResourceType, int> SilentReplace(IReadOnlyDictionary<ResourceType, int> replacement)
    {
        if (replacement == null) throw new ArgumentNullException(nameof(replacement));
        var validated = new Dictionary<ResourceType, int>();
        foreach (KeyValuePair<ResourceType, int> entry in replacement)
        {
            if (entry.Value < 0 || entry.Key == ResourceType.None || !Enum.IsDefined(typeof(ResourceType), entry.Key))
                throw new ArgumentException("Invalid resource counts.", nameof(replacement));
            if (entry.Value > 0) validated.Add(entry.Key, entry.Value);
        }

        Dictionary<ResourceType, int> previous = counts;
        counts = validated;
        return previous;
    }

    /// <summary>Notifies each observer of actual differences; observer failures are returned to the caller.</summary>
    public List<Exception> Notify(IReadOnlyDictionary<ResourceType, int> previous)
    {
        if (previous == null) throw new ArgumentNullException(nameof(previous));
        var errors = new List<Exception>();
        Action<ResourceType, int, int> listeners = Changed;
        if (listeners == null) return errors;

        Dictionary<ResourceType, int> current = GetSnapshot();
        var changedTypes = new HashSet<ResourceType>(previous.Keys);
        changedTypes.UnionWith(current.Keys);
        var orderedTypes = new List<ResourceType>(changedTypes);
        orderedTypes.Sort();
        Delegate[] observers = listeners.GetInvocationList();
        foreach (ResourceType type in orderedTypes)
        {
            int before;
            int after;
            previous.TryGetValue(type, out before);
            current.TryGetValue(type, out after);
            if (before == after) continue;

            foreach (Action<ResourceType, int, int> observer in observers)
            {
                try
                {
                    observer(type, before, after);
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }
        }
        return errors;
    }

    public int Remove(ResourceType type, int amount)
    {
        if (amount <= 0) return 0;
        int oldCount = GetCount(type);
        int removed = Math.Min(oldCount, amount);
        if (removed == 0) return 0;
        int newCount = oldCount - removed;
        if (newCount == 0) counts.Remove(type);
        else counts[type] = newCount;
        Changed?.Invoke(type, oldCount, newCount);
        return removed;
    }

    public void Clear()
    {
        if (counts.Count == 0) return;

        Dictionary<ResourceType, int> snapshot = GetSnapshot();
        counts.Clear();
        foreach (KeyValuePair<ResourceType, int> entry in snapshot)
        {
            if (Changed != null)
                Changed(entry.Key, entry.Value, 0);
        }
    }
}
