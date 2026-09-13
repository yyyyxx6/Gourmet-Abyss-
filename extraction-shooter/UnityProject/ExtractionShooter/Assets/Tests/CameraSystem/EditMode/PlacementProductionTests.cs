using System;
using System.Reflection;
using NUnit.Framework;

namespace GourmetAbyss.CameraSystem.Tests
{
    public sealed class PlacementProductionTests
    {
        // Production editor tools live in Assembly-CSharp-Editor; this test assembly references camera runtime only.
        [TestCase("SharedCameraStandard")]
        [TestCase("PortableSampleLibrary")]
        [TestCase("ContactAndLogicIsolationXY")]
        [TestCase("ContactAndLogicIsolationXZ")]
        [TestCase("InvalidAuthoringIsRejected")]
        [TestCase("RestaurantAnchorOwnership")]
        [TestCase("ProjectionAtEdgesAndDepth")]
        [TestCase("TemplateInstancesAreIndependent")]
        [TestCase("PreviewDoesNotCopyGameplayOrChangeSource")]
        [TestCase("StandaloneDepthBeyondRestaurantRange")]
        public void ProductionPlacementContract(string check)
        {
            var type=Type.GetType("Game.Modules.Editor.PlacementAcceptanceChecks, Assembly-CSharp-Editor",true);
            try {type.GetMethod(check,BindingFlags.Public|BindingFlags.Static).Invoke(null,null);}
            catch(TargetInvocationException e) {throw e.InnerException??e;}
        }
    }
}
