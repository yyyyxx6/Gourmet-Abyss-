using System;
using System.Linq;
using GourmetAbyss.CameraSystem;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GourmetAbyss.CameraSystem.Tests
{
    public sealed class PlanarSpritePlacementTests
    {
        [Test]
        public void RestaurantPrefab_PreservesArtworkShapeInFixedPerspectiveView()
        {
            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Modules/Restaurant/RestaurantWorld.prefab");
            Assert.IsNotNull(prefabAsset);
            var previewScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var prefab = (GameObject)PrefabUtility.InstantiatePrefab(prefabAsset, previewScene);
            var type = Type.GetType("Game.Modules.PlanarSprite, Assembly-CSharp", true);
            var artwork = prefab.GetComponentsInChildren(type, true)
                .Where(s=>!(bool)type.GetField("ground").GetValue(s)).ToArray();
            var itemType=Type.GetType("Game.Modules.PlacementItem, Assembly-CSharp",true);
            Assert.AreEqual(6, artwork.Count(s=>((Component)s).GetComponentInParent(itemType).name=="Table"));
            Assert.AreEqual(2, artwork.Count(s=>((Component)s).GetComponentInParent(itemType).name.StartsWith("Stove")));
            var view=prefab.GetComponent<PlanarPerspectiveView>();
            var cameraGO=new GameObject("Artwork projection test");
            try
            {
                var camera=cameraGO.AddComponent<Camera>();camera.orthographic=false;
                camera.pixelRect=new Rect(0,0,1920,1080);camera.aspect=1920f/1080;
                foreach(var pan in new[]{Vector2.zero,Vector2.right*1.5f,Vector2.up*1.5f})
                {
                    var pose=view.Pose(pan);camera.fieldOfView=pose.FieldOfView;
                    camera.transform.SetPositionAndRotation(pose.Position,pose.Rotation);
                    foreach (var facing in prefab.GetComponentsInChildren<CameraFacingVisual>(true))
                    {
                        var serialized = new SerializedObject(facing);
                        serialized.FindProperty("targetCamera").objectReferenceValue = camera;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                        facing.AlignToCamera();
                    }
                    foreach(var item in artwork)
                    {
                        var visual=(SpriteRenderer)type.GetField("visual").GetValue(item);
                        var bounds=visual.sprite.bounds;var t=visual.transform;
                        var bl=camera.WorldToScreenPoint(t.TransformPoint(new Vector3(bounds.min.x,bounds.min.y,0)));
                        var br=camera.WorldToScreenPoint(t.TransformPoint(new Vector3(bounds.max.x,bounds.min.y,0)));
                        var tl=camera.WorldToScreenPoint(t.TransformPoint(new Vector3(bounds.min.x,bounds.max.y,0)));
                        var tr=camera.WorldToScreenPoint(t.TransformPoint(new Vector3(bounds.max.x,bounds.max.y,0)));
                        Assert.That(Mathf.Abs(br.y-bl.y),Is.LessThan(.02f),item.name+" horizontal edge skewed");
                        Assert.That(Mathf.Abs(tl.x-bl.x),Is.LessThan(.02f),item.name+" vertical edge skewed");
                        Assert.That(Mathf.Abs(Vector2.Distance(bl,br)-Vector2.Distance(tl,tr)),Is.LessThan(.02f),item.name+" became trapezoid");
                        float expected=bounds.size.x*t.lossyScale.x/(bounds.size.y*t.lossyScale.y);
                        Assert.That(Vector2.Distance(bl,br)/Vector2.Distance(bl,tl),Is.EqualTo(expected).Within(.001f),item.name+" artwork aspect changed");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraGO);
                UnityEngine.Object.DestroyImmediate(prefab);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        [TestCase(0f, false)] [TestCase(90f, false)]
        [TestCase(0f, true)] [TestCase(90f, true)]
        public void Sorting_PreservesAuthoredTransform_WithOrWithoutCameraProfile(float frameAngle, bool ground)
        {
            var root = new GameObject("Placement frame");
            var spriteGO = new GameObject("Authored sprite");
            var profile = ScriptableObject.CreateInstance<PlanarPerspectiveProfile>();
            try
            {
                root.transform.rotation = Quaternion.Euler(frameAngle, 13, 7);
                spriteGO.transform.SetParent(root.transform, false);
                spriteGO.transform.localPosition = new Vector3(2, 3, .1f);
                spriteGO.transform.localRotation = Quaternion.Euler(12, 23, 34);
                spriteGO.transform.localScale = new Vector3(.7f, 1.2f, 1);
                var renderer = spriteGO.AddComponent<SpriteRenderer>();
                var type = Type.GetType("Game.Modules.PlanarSprite, Assembly-CSharp", true);
                var sorter = spriteGO.AddComponent(type);
                type.GetField("frame").SetValue(sorter, root.transform);
                type.GetField("visual").SetValue(sorter, renderer);
                type.GetField("ground").SetValue(sorter, ground);
                var position = spriteGO.transform.localPosition;
                var rotation = spriteGO.transform.localRotation;
                var scale = spriteGO.transform.localScale;
                for (int i = 0; i < 10; i++)
                {
                    profile.tiltFromNormal = 15 + i * 4;
                    type.GetField("profile").SetValue(sorter, i % 2 == 0 ? profile : null);
                    root.transform.rotation *= Quaternion.Euler(0, 10, 0);
                    type.GetMethod("Refresh").Invoke(sorter, null);
                    Assert.Less(Quaternion.Angle(rotation, spriteGO.transform.localRotation), .001f);
                    Assert.AreEqual(position, spriteGO.transform.localPosition);
                    Assert.AreEqual(scale, spriteGO.transform.localScale);
                    Assert.AreEqual(ground ? -1000 : -300, renderer.sortingOrder);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(spriteGO); UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(profile); }
        }
    }
}
