using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Modules.Editor
{
    public static class DecorationBrushAcceptance
    {
        const string ChairPath = "Assets/Modules/PlacementSamples/Chair.prefab";
        const string TablePath = "Assets/Modules/PlacementSamples/Table.prefab";
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static DecorationBrushSettings Settings() => new DecorationBrushSettings
        {
            radius = 2, minSpacing = .7f, density = 2,
            entries = new List<DecorationBrushEntry> { new DecorationBrushEntry { prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ChairPath) } }
        };

        sealed class Fixture : IDisposable
        {
            public Scene scene;
            public DecorationBrushGroup group;
            readonly int undo;
            readonly Scene originalScene;
            public Fixture(DecorationBrushGroup.GroundPlane plane = DecorationBrushGroup.GroundPlane.XZ)
            {
                Undo.IncrementCurrentGroup(); undo = Undo.GetCurrentGroup();
                originalScene = SceneManager.GetActiveScene();
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                group = AddGroup(plane);
            }
            public DecorationBrushGroup AddGroup(DecorationBrushGroup.GroundPlane plane)
            {
                var go = new GameObject("Brush acceptance");
                SceneManager.MoveGameObjectToScene(go, scene);
                var g = go.AddComponent<DecorationBrushGroup>(); g.plane = plane;
                return g;
            }
            public void Dispose()
            {
                Undo.RevertAllDownToGroup(undo);
                if (scene.IsValid()) EditorSceneManager.CloseScene(scene, true);
                if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
            }
        }

        public static void PlacementKeepsPrefabAndArtwork()
        {
            foreach (var plane in new[] { DecorationBrushGroup.GroundPlane.XY, DecorationBrushGroup.GroundPlane.XZ })
            using (var f = new Fixture(plane))
            {
                f.group.transform.position = new Vector3(4, 5, 6);
                var options = Settings();
                using (var stroke = new DecorationBrushStroke(f.group, options, false, 21))
                {
                    stroke.Stamp(new Vector2(2, 3), true);
                    Require(stroke.Changed == 1, "Single stamp did not place exactly one object.");
                }
                var placed = f.group.paintedObjects.Single();
                Require(PrefabUtility.GetCorrespondingObjectFromSource(placed) == options.entries[0].prefab, "Prefab link lost.");
                Require(Vector3.Distance(DecorationBrushCore.Contact(placed), f.group.ToWorld(new Vector2(2, 3))) < .0001f, "Contact missed ground.");
                var item = placed.GetComponent<PlacementItem>();
                var source = options.entries[0].prefab.GetComponent<PlacementItem>();
                Require(item.art.transform.localScale == source.art.transform.localScale && item.art.sprite == source.art.sprite, "Artwork changed.");
                Require(item.xzGround == (plane == DecorationBrushGroup.GroundPlane.XZ), "Wrong ground adapter.");
                Require(item.physicsRoot.localPosition == source.physicsRoot.localPosition, "Physics moved.");
            }
        }

        public static void WholeStrokeUndoRedo()
        {
            using (var f = new Fixture())
            {
                using (var stroke = new DecorationBrushStroke(f.group, Settings(), false, 2))
                    for (int i = 0; i < 3; i++) stroke.Stamp(new Vector2(i * 3, 0), true);
                Require(f.group.transform.childCount == 3, "Expected three instances.");
                Undo.PerformUndo();
                Require(f.group.transform.childCount == 0 && f.group.paintedObjects.Count == 0, "Undo must remove whole stroke and ownership.");
                Undo.PerformRedo();
                Require(f.group.transform.childCount == 3 && f.group.paintedObjects.Count == 3 && f.group.paintedObjects.All(f.group.Owns),
                    $"Redo must restore instances and ownership. Children={f.group.transform.childCount}; records={f.group.paintedObjects.Count}; live={f.group.paintedObjects.Count(o => o != null)}");
            }
        }

        public static void EraseIsScopedAndUndoable()
        {
            using (var f = new Fixture())
            {
                var other = f.AddGroup(DecorationBrushGroup.GroundPlane.XZ);
                using (var stroke = new DecorationBrushStroke(f.group, Settings(), false, 1))
                { stroke.Stamp(Vector2.zero, true); stroke.Stamp(new Vector2(1, 0), true); stroke.Stamp(new Vector2(-1, 0), true); }
                var moved = f.group.paintedObjects[1]; Undo.SetTransformParent(moved.transform, other.transform, "Move test instance");
                var manual = new GameObject("Manual decoration"); manual.transform.SetParent(f.group.transform, false);
                using (var stroke = new DecorationBrushStroke(other, Settings(), false, 2)) stroke.Stamp(Vector2.zero, true);
                var eraseOptions = Settings(); eraseOptions.radius = .55f;
                using (var stroke = new DecorationBrushStroke(f.group, eraseOptions, true, 3))
                { stroke.Stamp(Vector2.zero); stroke.Stamp(new Vector2(-1, 0)); Require(stroke.Changed == 2, "Erase crossed ownership boundary."); }
                Require(manual != null && moved != null && other.paintedObjects[0] != null, "Unrelated object erased.");
                Undo.PerformUndo();
                Require(f.group.paintedObjects.Count == 3 && f.group.paintedObjects.All(o => o != null), "Erase undo lost registry.");
                Undo.PerformRedo();
                Require(f.group.paintedObjects.Count == 1 && moved != null, $"Erase redo failed: records={f.group.paintedObjects.Count}; moved={moved != null}.");
            }
        }

        public static void SpacingAcrossStrokes()
        {
            using (var f = new Fixture())
            {
                for (int pass = 0; pass < 2; pass++)
                    using (var stroke = new DecorationBrushStroke(f.group, Settings(), false, 10 + pass))
                        for (int i = 0; i < 20; i++) stroke.Stamp(new Vector2(i * .5f, 0));
                var points = f.group.paintedObjects.Select(o => f.group.ToPlane(DecorationBrushCore.Contact(o))).ToArray();
                Require(points.Length > 15, "Scatter did not produce enough candidates.");
                for (int i = 0; i < points.Length; i++) for (int j = i + 1; j < points.Length; j++)
                    Require(Vector2.Distance(points[i], points[j]) >= .699f, "Minimum spacing violated across strokes.");
            }
        }

        public static void SamplingIndependentOfEvents()
        {
            var slow = new List<Vector2>(); var fast = new List<Vector2>();
            var a = new DecorationBrushPath(Vector2.zero, .4f);
            var b = new DecorationBrushPath(Vector2.zero, .4f);
            a.Move(new Vector2(10, 0), slow.Add);
            for (int i = 1; i <= 100; i++) b.Move(new Vector2(i * .1f, 0), fast.Add);
            Require(Math.Abs(slow.Count - fast.Count) <= 1, "Event rate altered stroke sampling.");
            for (int i = 0; i < Math.Min(slow.Count, fast.Count); i++) Require(Vector2.Distance(slow[i], fast[i]) < .0001f, "Stroke gaps depend on event rate.");
        }

        public static void PaletteAndValidation()
        {
            using (var f = new Fixture())
            {
                var options = Settings(); options.entries[0].enabled = false;
                using (var stroke = new DecorationBrushStroke(f.group, options, false, 1)) stroke.Stamp(Vector2.zero, true);
                Require(f.group.paintedObjects.Count == 0, "Disabled resource was painted.");
                options.entries[0].enabled = true; options.entries[0].weight = 1;
                options.entries.Add(new DecorationBrushEntry { prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TablePath), weight = 20 });
                using (var stroke = new DecorationBrushStroke(f.group, options, false, 42))
                    for (int i = 0; i < 80; i++) stroke.Stamp(new Vector2(i * 3, 0), true);
                Require(f.group.paintedObjects.Count(o => PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(o) == TablePath) > 65, "Weights ignored.");
                Require(DecorationBrushCore.PrefabError(f.group.gameObject) != null, "Scene object accepted as prefab.");
                f.group.transform.localScale = Vector3.one * 2;
                Require(DecorationBrushCore.TargetError(f.group) != null, "Scaled group not rejected.");
                options.radius = float.NaN; options.density = float.PositiveInfinity; options.minSpacing = -1; options.Sanitize();
                Require(options.radius > 0 && !float.IsInfinity(options.density) && options.minSpacing > 0, "Invalid settings not clamped.");
            }
        }

        public static void ModuleCoordinatesAndPresetCopy()
        {
            using (var f = new Fixture())
            {
                f.group.transform.rotation = Quaternion.Euler(0, 0, 15);
                f.group.groundFrame = f.group.transform;
                var options = Settings(); var copy = options.Copy();
                copy.entries[0].weight = 10;
                Require(options.entries[0].weight == 1 && copy.entries[0].prefab == options.entries[0].prefab, "Preset copy shares mutable data or loses asset.");
                using (var stroke = new DecorationBrushStroke(f.group, options, false, 3)) stroke.Stamp(new Vector2(2, 4), true);
                var placed = f.group.paintedObjects[0];
                Require(Vector3.Distance(DecorationBrushCore.Contact(placed), f.group.ToWorld(new Vector2(2, 4))) < .0001f, "Local module plane missed contact.");
                Require(placed.GetComponent<PlacementItem>().sorter.frame == f.group.transform, "Module sorting frame missing.");
            }
        }

        public static void SaveReopenRetainsOwnership()
        {
            DecorationBrushWorkspace.EnsureFolder(DecorationBrushWorkspace.LocalTests);
            string file = DecorationBrushWorkspace.LocalTests + "/__Acceptance_" + Guid.NewGuid().ToString("N") + ".unity";
            Scene reopened = default;
            Scene activeScene = SceneManager.GetActiveScene();
            Undo.IncrementCurrentGroup(); int reopenUndo = Undo.GetCurrentGroup();
            try
            {
                using (var f = new Fixture())
                {
                    using (var stroke = new DecorationBrushStroke(f.group, Settings(), false, 7)) stroke.Stamp(new Vector2(5, 2), true);
                    Require(EditorSceneManager.SaveScene(f.scene, file, true), "Could not save temporary test scene.");
                }
                reopened = EditorSceneManager.OpenScene(file, OpenSceneMode.Additive);
                var group = reopened.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<DecorationBrushGroup>()).Single();
                Require(group.paintedObjects.Count == 1 && group.Owns(group.paintedObjects[0]), "Saved ownership missing.");
                Require(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(group.paintedObjects[0]) == ChairPath, "Saved prefab link missing.");
                using (var stroke = new DecorationBrushStroke(group, Settings(), true, 8)) stroke.Stamp(new Vector2(5, 2));
                Require(group.paintedObjects.Count == 0, "Reopened object cannot be erased.");
            }
            finally
            {
                Undo.RevertAllDownToGroup(reopenUndo);
                if (reopened.IsValid()) EditorSceneManager.CloseScene(reopened, true);
                if (activeScene.IsValid() && activeScene.isLoaded) SceneManager.SetActiveScene(activeScene);
                AssetDatabase.DeleteAsset(file);
            }
        }

        public static void WorkspaceBuildIsolation()
        {
            DecorationBrushWorkspace.EnsureFolder(DecorationBrushWorkspace.LocalTests);
            string id = Guid.NewGuid().ToString("N");
            string localScene = DecorationBrushWorkspace.LocalTests + "/__Acceptance_" + id + ".unity";
            string materialPath = DecorationBrushWorkspace.LocalTests + "/__Acceptance_" + id + ".mat";
            string dependentScene = "Assets/__BrushDependency_" + id + ".unity";
            Action<string> mustReject = file =>
            {
                bool rejected = false;
                try { DecorationBrushWorkspaceBuildGuard.ValidateScenes(new[] { new EditorBuildSettingsScene(file, true) }); }
                catch (UnityEditor.Build.BuildFailedException) { rejected = true; }
                Require(rejected, "Build accepted workspace content: " + file);
            };
            try
            {
                // An actual scene outside the workspace must also be rejected if it references local assets.
                using (var f = new Fixture())
                {
                    Require(EditorSceneManager.SaveScene(f.scene, localScene, true), "Could not save local test scene.");
                    mustReject(localScene);
                    DecorationBrushWorkspaceBuildGuard.ValidateScenes(new[] { new EditorBuildSettingsScene(localScene, false) });
                    var source = AssetDatabase.LoadAssetAtPath<GameObject>(ChairPath).GetComponentInChildren<Renderer>().sharedMaterial;
                    var material = new Material(source);
                    AssetDatabase.CreateAsset(material, materialPath);
                    f.group.gameObject.AddComponent<SpriteRenderer>().sharedMaterial = material;
                    Require(EditorSceneManager.SaveScene(f.scene, dependentScene, true), "Could not save dependency test scene.");
                    mustReject(dependentScene);
                }
                DecorationBrushWorkspace.ValidateBuildPaths(new[] { "Assets/ArtWorkspaceArchive/Released.unity", "Assets/Scenes/Layer1.unity" });
                DecorationBrushWorkspaceBuildGuard.ValidateScenes(EditorBuildSettings.scenes);
            }
            finally
            {
                AssetDatabase.DeleteAsset(dependentScene);
                AssetDatabase.DeleteAsset(localScene);
                AssetDatabase.DeleteAsset(materialPath);
            }
        }

        [MenuItem("Tools/Modules/Decoration Brush/Run Acceptance")]
        public static void RunAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || PrefabStageUtility.GetCurrentPrefabStage() != null)
                throw new InvalidOperationException("Stop Play and return to the scene before acceptance.");
            var checks = new Action[] { PlacementKeepsPrefabAndArtwork, WholeStrokeUndoRedo, EraseIsScopedAndUndoable,
                SpacingAcrossStrokes, SamplingIndependentOfEvents, PaletteAndValidation, ModuleCoordinatesAndPresetCopy, SaveReopenRetainsOwnership, WorkspaceBuildIsolation };
            var lines = new List<string>(); int failed = 0;
            foreach (var check in checks)
            {
                var errors = new List<string>();
                Application.LogCallback capture = (message, stack, kind) =>
                { if (kind == LogType.Error || kind == LogType.Exception || kind == LogType.Assert) errors.Add(message + "\n" + stack); };
                Application.logMessageReceived += capture;
                try
                {
                    check();
                    Require(errors.Count == 0, string.Join("\n", errors));
                    lines.Add("PASS " + check.Method.Name);
                }
                catch (Exception ex) { failed++; lines.Add("FAIL " + check.Method.Name + "\n" + ex); }
                finally { Application.logMessageReceived -= capture; }
            }
            Directory.CreateDirectory("Library/DecorationBrushAcceptance");
            File.WriteAllLines("Library/DecorationBrushAcceptance/results.txt", lines);
            Debug.Log($"[Decoration Brush] {checks.Length - failed}/{checks.Length} passed. Library/DecorationBrushAcceptance/results.txt");
        }
    }
}
