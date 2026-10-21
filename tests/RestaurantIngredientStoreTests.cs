using System;
using System.Collections.Generic;

public static class RestaurantIngredientStoreTests
{
    private static readonly ResourceType Food = ResourceType.LootMushroom;
    private static readonly ResourceType Egg = ResourceType.LootEggSmall;
    public static void Run()
    {
        var store = new RestaurantIngredientStore();
        True(store.TryDeposit(Counts(Food, 10020)), "Stock exceeds the old 9999 cap.");
        Equal(10020L, store.GetCount(Food), "No material-store cap or backpack limit applies.");
        var request = Counts(Food, 7);
        request.Add(Egg, 3);
        True(!store.TryConsume(request), "A partially affordable recipe must be rejected atomically.");
        Equal(10020L, store.GetCount(Food), "Rejected recipes cannot debit their affordable components.");
        True(store.TryDeposit(Counts(Egg, 3)), "Second ingredient deposit.");
        RestaurantIngredientStore.Reservation order;
        True(store.TryReserve(request, out order), "Reserve all recipe components exactly once.");
        request[Food] = 500;
        Equal(10013L, store.GetCount(Food), "Reserved stock is unavailable to a repeated click.");
        True(!store.TryConsume(Counts(Egg, 1)), "Reserved eggs cannot be overspent.");
        True(store.Refund(order), "Interrupted order refunds.");
        True(!store.Refund(order) && !store.Commit(order), "A resolved receipt cannot refund or commit twice.");
        Equal(10020L, store.GetCount(Food), "Refund uses frozen quantities, not a mutated recipe/request.");
        Equal(3L, store.GetCount(Egg), "Every ingredient returns.");
        var stranger = new RestaurantIngredientStore();
        True(store.TryReserve(Counts(Food, 2), out order), "New order.");
        True(!stranger.Refund(order) && !stranger.Commit(order), "Another stock cannot resolve this receipt.");
        True(store.Commit(order), "Successful queue commit consumes reserved ingredients.");
        True(!store.Refund(order), "Committed meals cannot be refunded as failed orders.");
        Equal(10018L, store.GetCount(Food), "Successful consumption is exact.");
        var snapshot = store.Capture();
        store.TryDeposit(Counts(Food, 1));
        Equal(10018L, snapshot[Food], "Snapshots do not track later mutations.");
        Throws(() => ((IDictionary<ResourceType, long>)snapshot)[Food] = 0);
        True(!store.TryDeposit(Counts(ResourceType.Money, 1)) && !store.TryDeposit(Counts(Food, -1)),
            "Only nonnegative ingredient quantities are valid.");

        var boundary = new RestaurantIngredientStore();
        True(boundary.TryDeposit(Counts(Food, long.MaxValue - 2)), "Numeric boundary deposit.");
        True(boundary.TryReserve(Counts(Food, 5), out order), "Boundary reservation.");
        True(!boundary.TryDeposit(Counts(Food, 3)), "Deposits must retain enough numeric headroom for a pending refund.");
        True(boundary.TryDeposit(Counts(Food, 2)) && boundary.Refund(order), "A guaranteed refund fits after a later deposit.");
        Equal(long.MaxValue, boundary.GetCount(Food), "Numeric limits must never silently clamp food.");

        for (int index = 0; index < 128; index++)
        {
            var model = new RestaurantIngredientStore();
            long initial = index * 139L + 19;
            model.TryDeposit(Counts(Food, initial));
            long cost = initial / 3;
            model.TryReserve(Counts(Food, cost), out order);
            if (index % 2 == 0) model.Refund(order); else model.Commit(order);
            Equal(initial - (index % 2 == 0 ? 0 : cost), model.GetCount(Food), "Reserve/resolve quantity conservation.");
        }
    }
    private static Dictionary<ResourceType, long> Counts(ResourceType type, long count) =>
        new Dictionary<ResourceType, long> { { type, count } };
    private static void True(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Equal(long expected, long actual, string message) { True(expected == actual, message); }
    private static void Throws(Action action)
    {
        try { action(); } catch (NotSupportedException) { return; }
        throw new Exception("Snapshot must be readonly.");
    }
}
