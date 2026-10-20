using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GourmetAbyss.CameraSystem.Tests
{
    public sealed class PlanarSpriteSortingTests
    {
        static readonly Type SorterType = Type.GetType("Game.Modules.PlanarSprite, Assembly-CSharp", true);
        static string GroundLayer => (string)SorterType.GetField("GroundSortingLayer").GetRawConstantValue();
        static void Set(Component sorter, string field, object value) => SorterType.GetField(field).SetValue(sorter, value);
        static void Refresh(Component sorter) => SorterType.GetMethod("Refresh").Invoke(sorter, null);

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void MovingArtworkAcrossMap_StaysAboveGround_AndKeepsDepthOrder(bool xz, bool bound)
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Sorting regression");
            SceneManager.MoveGameObjectToScene(root, scene);
            try
            {
                var frame = new GameObject("Frame").transform;
                frame.SetParent(root.transform, false);
                frame.rotation = xz ? Quaternion.Euler(90, 0, 0) : Quaternion.identity;
                var floor = new GameObject("Ground").AddComponent<SpriteRenderer>();
                floor.transform.SetParent(root.transform, false);
                var floorSorter = floor.gameObject.AddComponent(SorterType);
                Set(floorSorter, "visual", floor);
                Set(floorSorter, "ground", true);
                Set(floorSorter, "useWorldAxesWhenUnbound", true);
                Set(floorSorter, "frame", bound ? frame : null);
                Refresh(floorSorter);
                // Layer priority must hold even at the numeric extremes.
                floor.sortingOrder = 32767;

                var art = new GameObject("Artwork").AddComponent<SpriteRenderer>();
                art.transform.SetParent(root.transform, false);
                var sorter = art.gameObject.AddComponent(SorterType);
                Set(sorter, "visual", art);
                Set(sorter, "contactPoint", art.transform);
                Set(sorter, "xzGround", xz);
                Set(sorter, "useWorldAxesWhenUnbound", true);
                Set(sorter, "frame", bound ? frame : null);
                int previousOrder = int.MaxValue;
                foreach (float depth in new[] { -400f, -40f, 0f, 5f, 20f, 300f, 400f })
                {
                    art.transform.position = (xz ? Vector3.forward : Vector3.up) * depth;
                    var position = art.transform.position;
                    Refresh(sorter);
                    Assert.AreEqual(position, art.transform.position, "Sorting moved the artwork.");
                    Assert.Less(SortingLayer.GetLayerValueFromID(floor.sortingLayerID),
                        SortingLayer.GetLayerValueFromID(art.sortingLayerID), "Ground can cover moved artwork.");
                    Assert.LessOrEqual(art.sortingOrder, previousOrder, "Depth order reversed.");
                    previousOrder = art.sortingOrder;
                }
                // Nearby objects still separate; the ground fix must not collapse the object's depth range.
                foreach (float depth in bound ? new[] { 1f, 2f } : new[] { 20f, 21f })
                {
                    art.transform.position = frame.up * depth;
                    Refresh(sorter);
                    Assert.AreEqual(Mathf.RoundToInt(-depth * 100), art.sortingOrder);
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void WindArtwork_RendersAboveFloorAfterMovingAcrossOldBoundary()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("Rendered sorting regression");
            SceneManager.MoveGameObjectToScene(root, scene);
            var texture = new Texture2D(2, 2);
            texture.SetPixels(new[] { Color.white, Color.white, Color.white, Color.white });
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.one * .5f, 2);
            var material = new Material(Shader.Find("GourmetAbyss/Effects/Wind Sway"));
            material.SetFloat("_WindStrength", 0);
            var target = new RenderTexture(64, 64, 24);
            var readback = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var previousTarget = RenderTexture.active;
            try
            {
                var camera = new GameObject("Camera").AddComponent<Camera>();
                camera.transform.SetParent(root.transform, false);
                camera.scene = scene;
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = 2;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.allowHDR = camera.allowMSAA = false;
                camera.targetTexture = target;
                var floor = new GameObject("Floor").AddComponent<SpriteRenderer>();
                floor.transform.SetParent(root.transform, false);
                floor.sprite = sprite;
                floor.color = Color.green;
                floor.transform.rotation = Quaternion.Euler(90, 0, 0);
                floor.transform.localScale = Vector3.one * 100;
                floor.sortingOrder = -1000;
                var art = new GameObject("Tree").AddComponent<SpriteRenderer>();
                art.transform.SetParent(root.transform, false);
                art.sprite = sprite;
                art.color = Color.red;
                art.sharedMaterial = material;
                var sorter = art.gameObject.AddComponent(SorterType);
                Set(sorter, "visual", art);
                Set(sorter, "xzGround", true);
                Set(sorter, "useWorldAxesWhenUnbound", true);
                foreach (float depth in new[] { 0f, 20f, 400f })
                {
                    art.transform.position = new Vector3(0, 1, depth);
                    floor.transform.position = new Vector3(0, -.02f, depth);
                    camera.transform.rotation = Quaternion.Euler(45, 0, 0);
                    camera.transform.position = art.transform.position - camera.transform.forward * 10;
                    Refresh(sorter);
                    if (depth == 20)
                    {
                        floor.sortingLayerID = 0;
                        var covered = CaptureCenter();
                        Assert.Greater(covered.g, covered.r + .2f, "Control did not reproduce the original occlusion.");
                    }
                    floor.sortingLayerName = GroundLayer;
                    var visible = CaptureCenter();
                    Assert.Greater(visible.r, visible.g + .2f, "Ground covered the tree at Z=" + depth);
                }

                Color CaptureCenter()
                {
                    camera.Render();
                    RenderTexture.active = target;
                    readback.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
                    readback.Apply();
                    return readback.GetPixel(32, 32);
                }
            }
            finally
            {
                RenderTexture.active = previousTarget;
                EditorSceneManager.ClosePreviewScene(scene);
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(material);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [Test]
        public void GroundRoleWithoutFrame_ChangesLayer_AndCanReturnToArtwork()
        {
            var go = new GameObject("Ground role regression");
            try
            {
                var renderer = go.AddComponent<SpriteRenderer>();
                var sorter = go.AddComponent(SorterType);
                Set(sorter, "visual", renderer);
                Set(sorter, "ground", true);
                Refresh(sorter);
                Assert.AreEqual(GroundLayer, renderer.sortingLayerName);
                Set(sorter, "ground", false);
                Refresh(sorter);
                Assert.AreEqual(0, renderer.sortingLayerID);
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void RestaurantGround_StaysBelowFurnitureWithoutMovingLayout()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Modules/Restaurant/RestaurantWorld.prefab");
                var root = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                var sorters = root.GetComponentsInChildren(SorterType, true);
                var grounds = sorters.Where(s => (bool)SorterType.GetField("ground").GetValue(s)).ToArray();
                var furniture = sorters.Except(grounds).ToArray();
                Assert.IsNotEmpty(grounds);
                Assert.IsNotEmpty(furniture);
                foreach (Component sorter in sorters)
                {
                    var position = sorter.transform.position;
                    var rotation = sorter.transform.rotation;
                    var scale = sorter.transform.localScale;
                    Refresh(sorter);
                    Assert.AreEqual(position, sorter.transform.position);
                    Assert.AreEqual(rotation, sorter.transform.rotation);
                    Assert.AreEqual(scale, sorter.transform.localScale);
                }
                foreach (var ground in grounds)
                foreach (var item in furniture)
                {
                    var floor = (SpriteRenderer)SorterType.GetField("visual").GetValue(ground);
                    var art = (SpriteRenderer)SorterType.GetField("visual").GetValue(item);
                    Assert.Less(SortingLayer.GetLayerValueFromID(floor.sortingLayerID),
                        SortingLayer.GetLayerValueFromID(art.sortingLayerID));
                }
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        [Test]
        public void GroundAssets_UseDedicatedLayer()
        {
            int layer = SortingLayer.NameToID(GroundLayer);
            Assert.AreNotEqual(0, layer, "The ground sorting layer is missing.");
            Assert.Less(SortingLayer.GetLayerValueFromID(layer), SortingLayer.GetLayerValueFromID(0));
            foreach (string name in new[] { "Ground", "GroundMat" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Modules/PlacementSamples/" + name + ".prefab");
                var sorter = prefab.GetComponentsInChildren(SorterType, true).Single();
                var renderer = (SpriteRenderer)SorterType.GetField("visual").GetValue(sorter);
                Assert.AreEqual(layer, renderer.sortingLayerID, name + " was not migrated.");
            }
        }
    }
}
