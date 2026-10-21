using System;
using System.Reflection;
using NUnit.Framework;

namespace GourmetAbyss.CameraSystem.Tests
{
    public sealed class RestaurantPrefabLinkTests
    {
        [TestCase("RestaurantHasCompleteNestedLinks")]
        [TestCase("NoVisualOverridesBlockPropagation")]
        [TestCase("StoveEditsReachMap")]
        [TestCase("TableEditsReachAllSixAssemblies")]
        [TestCase("ChairEditsReachAllTwentyFourChairs")]
        [TestCase("GroundEditsReachActualRestaurant")]
        [TestCase("UnpackedMapItemIsRejected")]
        [TestCase("NewItemCanBeSavedAndConnected")]
        public void PrefabProductionChain(string name)
        {
            var type=Type.GetType("Game.Modules.Editor.PrefabLinkAcceptanceChecks, Assembly-CSharp-Editor",true);
            try {type.GetMethod(name,BindingFlags.Public|BindingFlags.Static).Invoke(null,null);}
            catch(TargetInvocationException e){throw e.InnerException??e;}
        }
    }
}
