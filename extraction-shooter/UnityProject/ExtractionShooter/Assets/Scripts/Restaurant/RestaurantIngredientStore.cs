using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

/// <summary>Restaurant food stock, independent of backpack slots and permanent material caps.</summary>
public sealed class RestaurantIngredientStore
{
    private readonly Dictionary<ResourceType, long> available = new Dictionary<ResourceType, long>();
    private readonly Dictionary<ResourceType, long> reserved = new Dictionary<ResourceType, long>();

    public long GetCount(ResourceType type) => Count(available, type);
    public IReadOnlyDictionary<ResourceType, long> Capture() =>
        new ReadOnlyDictionary<ResourceType, long>(new Dictionary<ResourceType, long>(available));

    public bool CanDeposit(IReadOnlyDictionary<ResourceType, long> quantities)
    {
        if (!Valid(quantities)) return false;
        foreach (var entry in quantities)
            if (entry.Value > long.MaxValue - GetCount(entry.Key) - Count(reserved, entry.Key)) return false;
        return true;
    }

    public bool TryDeposit(IReadOnlyDictionary<ResourceType, long> quantities)
    {
        if (!CanDeposit(quantities)) return false;
        foreach (var entry in quantities)
            if (entry.Value > 0) available[entry.Key] = GetCount(entry.Key) + entry.Value;
        return true;
    }

    public bool CanAfford(IReadOnlyDictionary<ResourceType, long> quantities)
    {
        if (!Valid(quantities)) return false;
        foreach (var entry in quantities)
            if (GetCount(entry.Key) < entry.Value) return false;
        return true;
    }

    public bool TryReserve(IReadOnlyDictionary<ResourceType, long> quantities, out Reservation reservation)
    {
        reservation = null;
        if (!CanAfford(quantities)) return false;
        var copy = new Dictionary<ResourceType, long>();
        foreach (var entry in quantities)
        {
            if (entry.Value == 0) continue;
            copy.Add(entry.Key, entry.Value);
            available[entry.Key] = GetCount(entry.Key) - entry.Value;
            reserved[entry.Key] = Count(reserved, entry.Key) + entry.Value;
        }
        reservation = new Reservation(this, copy);
        return true;
    }

    public bool Commit(Reservation reservation)
    {
        if (!Owns(reservation)) return false;
        foreach (var entry in reservation.Quantities)
            reserved[entry.Key] = Count(reserved, entry.Key) - entry.Value;
        reservation.IsPending = false;
        return true;
    }

    public bool Refund(Reservation reservation)
    {
        if (!Owns(reservation)) return false;
        foreach (var entry in reservation.Quantities)
        {
            reserved[entry.Key] = Count(reserved, entry.Key) - entry.Value;
            available[entry.Key] = GetCount(entry.Key) + entry.Value;
        }
        reservation.IsPending = false;
        return true;
    }

    public bool TryConsume(IReadOnlyDictionary<ResourceType, long> quantities)
    {
        Reservation reservation;
        return TryReserve(quantities, out reservation) && Commit(reservation);
    }

    private bool Owns(Reservation reservation) => reservation != null && reservation.IsPending &&
        ReferenceEquals(reservation.Owner, this);

    private static bool Valid(IReadOnlyDictionary<ResourceType, long> quantities)
    {
        if (quantities == null) return false;
        foreach (var entry in quantities)
            if (!ResourceStorageRules.IsIngredient(entry.Key) || entry.Value < 0) return false;
        return true;
    }

    private static long Count(Dictionary<ResourceType, long> values, ResourceType type)
    {
        long count;
        return values.TryGetValue(type, out count) ? count : 0L;
    }

    public sealed class Reservation
    {
        internal RestaurantIngredientStore Owner { get; }
        public bool IsPending { get; internal set; } = true;
        public IReadOnlyDictionary<ResourceType, long> Quantities { get; }
        internal Reservation(RestaurantIngredientStore owner, Dictionary<ResourceType, long> quantities)
        {
            Owner = owner;
            Quantities = new ReadOnlyDictionary<ResourceType, long>(quantities);
        }
    }
}
