using System;
public static class RestaurantIngredientStoreRunner
{
    public static int Main()
    {
        try { RestaurantIngredientStoreTests.Run(); Console.WriteLine("PASS RestaurantIngredientStoreTests (including 128 conservation cases)"); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
