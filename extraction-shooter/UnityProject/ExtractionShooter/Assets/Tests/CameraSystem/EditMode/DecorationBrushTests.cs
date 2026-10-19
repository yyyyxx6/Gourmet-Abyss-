using System;
using System.Reflection;
using NUnit.Framework;

namespace GourmetAbyss.CameraSystem.Tests
{
    public sealed class DecorationBrushTests
    {
        [TestCase("PlacementKeepsPrefabAndArtwork")]
        [TestCase("WholeStrokeUndoRedo")]
        [TestCase("EraseIsScopedAndUndoable")]
        [TestCase("SpacingAcrossStrokes")]
        [TestCase("SamplingIndependentOfEvents")]
        [TestCase("PaletteAndValidation")]
        [TestCase("ModuleCoordinatesAndPresetCopy")]
        [TestCase("SaveReopenRetainsOwnership")]
        [TestCase("WorkspaceBuildIsolation")]
        public void ArtistWorkflow(string check)
        {
            var type = Type.GetType("Game.Modules.Editor.DecorationBrushAcceptance, Assembly-CSharp-Editor", true);
            try { type.GetMethod(check, BindingFlags.Public | BindingFlags.Static).Invoke(null, null); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
        }
    }
}
