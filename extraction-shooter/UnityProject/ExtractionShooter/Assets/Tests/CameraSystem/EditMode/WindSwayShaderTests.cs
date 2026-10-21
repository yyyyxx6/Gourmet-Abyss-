using System;
using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GourmetAbyss.CameraSystem.Tests
{
    public sealed class WindSwayShaderTests
    {
        private const string ShaderName = "GourmetAbyss/Effects/Wind Sway";
        private const int Resolution = 256;
        private Scene scene;
        private GameObject cameraObject, plantObject;
        private Camera camera;
        private SpriteRenderer renderer;
        private Texture2D texture;
        private Sprite sprite;
        private Material material;
        private RenderTexture target;

        [SetUp]
        public void SetUp()
        {
            var shader = Shader.Find(ShaderName);
            Assert.IsNotNull(shader);
            scene = EditorSceneManager.NewPreviewScene();
            cameraObject = new GameObject("Wind test camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            camera = cameraObject.AddComponent<Camera>();
            camera.scene = scene;
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 1;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.clear;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            target = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            target.Create();
            camera.targetTexture = target;
            texture = new Texture2D(64, 64, TextureFormat.RGBA32, false, true);
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            var colors = new Color[64 * 64];
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                if (x >= 28 && x <= 35) colors[y * 64 + x] = y < 2 ? new Color(1, 0.75f, 0.1f, 1) : Color.green;
                if (x >= 36 && x <= 44 && y >= 45 && y <= 55) colors[y * 64 + x] = Color.green;
            }
            texture.SetPixels(colors);
            texture.Apply();
            sprite = Sprite.Create(texture, new Rect(0, 0, 64, 64), new Vector2(0.5f, 0.5f), 64, 0, SpriteMeshType.FullRect);
            plantObject = new GameObject("Wind test sprite");
            SceneManager.MoveGameObjectToScene(plantObject, scene);
            renderer = plantObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            material = new Material(shader);
            material.SetFloat("_WindStrength", 0.2f);
            material.SetFloat("_WindSpeed", 1.5f);
            material.SetFloat("_WindSpatialScale", 0);
            material.SetFloat("_GustStrength", 0);
            renderer.sharedMaterial = material;
        }

        [TearDown]
        public void TearDown()
        {
            if (camera != null) camera.targetTexture = null;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            if (scene.IsValid()) EditorSceneManager.ClosePreviewScene(scene);
            Object.DestroyImmediate(material);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }

        private Color32[] Capture(float time)
        {
            material.SetFloat("_WindTimeOverride", time);
            camera.Render();
            var previous = RenderTexture.active;
            var readback = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, true);
            try
            {
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
                readback.Apply();
                return readback.GetPixels32();
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(readback); }
        }

        private static float Centroid(Color32[] pixels, bool root)
        {
            float sum = 0;
            int count = 0;
            for (int y = 0; y < Resolution; y++)
            for (int x = 0; x < Resolution; x++)
            {
                var c = pixels[y * Resolution + x];
                bool match = root ? c.r > 100 && c.g > 100 && c.b < 60 : c.g > 180 && c.r < 60 && y > Resolution / 2 + 20;
                if (match) { sum += x; count++; }
            }
            Assert.Greater(count, 0, "Expected sprite pixels were not rendered.");
            return sum / count;
        }

        private static int ChangedPixels(Color32[] a, Color32[] b)
        {
            int changed = 0;
            for (int i = 0; i < a.Length; i++)
                if (Math.Abs(a[i].r - b[i].r) + Math.Abs(a[i].g - b[i].g) + Math.Abs(a[i].b - b[i].b) > 20) changed++;
            return changed;
        }

        [Test]
        public void ShaderAndExampleMaterial_ImportWithoutShaderErrors()
        {
            Capture(1);
            Assert.IsTrue(material.shader.isSupported);
            Assert.IsEmpty(ShaderUtil.GetShaderMessages(material.shader).Where(m => m.severity.ToString() == "Error"));
            var example = AssetDatabase.LoadAssetAtPath<Material>("Assets/Shaders/Effects/WindSway/M_WindSway.mat");
            Assert.IsNotNull(example);
            Assert.AreEqual(ShaderName, example.shader.name);
            Assert.AreEqual(-1, example.GetFloat("_WindTimeOverride"));
        }

        [Test]
        public void SourceMaterial_CanSwitchShaderAndKeepTextureAndTint()
        {
            var source = new Material(Shader.Find("Sprites/Default"));
            try
            {
                var tint = new Color(0.8f, 0.6f, 0.4f, 0.75f);
                source.SetTexture("_MainTex", texture);
                source.SetColor("_Color", tint);
                source.shader = material.shader;
                Assert.AreSame(texture, source.GetTexture("_MainTex"));
                Assert.Less(Vector4.Distance((Vector4)tint, (Vector4)source.GetColor("_Color")), 0.00001f);
                source.SetFloat("_WindStrength", 0);
                renderer.sharedMaterial = source;
                var pixels = Capture(1);
                Assert.Greater(pixels.Count(c => c.g > 10 && c.a > 10), 100);
                Assert.AreEqual(0, pixels[0].a);
            }
            finally { renderer.sharedMaterial = material; Object.DestroyImmediate(source); }
        }

        [Test]
        public void Wind_MovesTopButKeepsRootAndTransformStable()
        {
            var position = plantObject.transform.position;
            var rotation = plantObject.transform.rotation;
            var scale = plantObject.transform.localScale;
            var first = Capture(1);
            var second = Capture(3);
            Assert.LessOrEqual(Mathf.Abs(Centroid(first, true) - Centroid(second, true)), 1.1f);
            Assert.Greater(Mathf.Abs(Centroid(first, false) - Centroid(second, false)), 5);
            Assert.AreEqual(position, plantObject.transform.position);
            Assert.AreEqual(rotation, plantObject.transform.rotation);
            Assert.AreEqual(scale, plantObject.transform.localScale);
            Assert.AreEqual(0, renderer.sortingOrder);
            Directory.CreateDirectory("Library/VegetationWind");
            SavePixels(first, "Library/VegetationWind/test-wind-a.png");
            SavePixels(second, "Library/VegetationWind/test-wind-b.png");
        }

        [Test]
        public void ZeroAmplitude_IsStationaryAndRetainsTransparentMargins()
        {
            material.SetFloat("_WindStrength", 0);
            var first = Capture(1);
            var second = Capture(3);
            Assert.AreEqual(0, ChangedPixels(first, second));
            Assert.AreEqual(0, first[0].a);
            Assert.Greater(first.Count(c => c.g > 180), 100);
        }

        [Test]
        public void ZeroSpeed_FreezesSwayAndGust()
        {
            material.SetFloat("_WindSpeed", 0);
            material.SetFloat("_GustStrength", 1);
            Assert.AreEqual(0, ChangedPixels(Capture(1), Capture(8)));
        }

        [UnityTest]
        public IEnumerator DefaultMaterial_AnimatesUsingUnityTimeWithoutScripts()
        {
            material.SetFloat("_WindSpeed", 5);
            var first = Capture(-1);
            int maximumChange = 0;
            for (int sample = 0; sample < 3; sample++)
            {
                float start = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - start < 0.25f) yield return null;
                maximumChange = Math.Max(maximumChange, ChangedPixels(first, Capture(-1)));
            }
            Assert.Greater(maximumChange, 20, "The material should animate from Unity's clock without a controller or binding script.");
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public void SpriteFlip_MirrorsArtworkWithoutLosingAlpha(bool flipX, bool flipY)
        {
            material.SetFloat("_WindStrength", 0);
            var original = Capture(1);
            renderer.flipX = flipX;
            renderer.flipY = flipY;
            var flipped = Capture(1);
            var expected = new Color32[original.Length];
            for (int y = 0; y < Resolution; y++)
            for (int x = 0; x < Resolution; x++)
                expected[y * Resolution + x] = original[(flipY ? Resolution - 1 - y : y) * Resolution + (flipX ? Resolution - 1 - x : x)];
            Assert.LessOrEqual(ChangedPixels(expected, flipped), 20);
            Assert.AreEqual(0, flipped[0].a);
        }

        [Test]
        public void SpriteRendererTint_IsApplied()
        {
            material.SetFloat("_WindStrength", 0);
            renderer.color = new Color(1, 0.5f, 1, 0.5f);
            var tinted = Capture(1);
            var greenPixels = tinted.Where(c => c.g > 10 && c.r < 5 && c.a > 10).ToArray();
            Assert.Greater(greenPixels.Length, 100);
            Assert.That(greenPixels.Average(c => c.a), Is.InRange(115, 140));
            // SpriteRenderer converts its tint to linear space in a linear project;
            // blending onto transparent black also multiplies RGB by alpha.
            float green = QualitySettings.activeColorSpace == ColorSpace.Linear ? renderer.color.linear.g : renderer.color.g;
            float expected = green * renderer.color.a * 255;
            Assert.That(greenPixels.Average(c => c.g), Is.InRange(expected - 5, expected + 5));
        }

        [Test]
        public void CameraFacingRotation_StillRendersAndAnimates()
        {
            var rotation = Quaternion.Euler(20, 35, 0);
            plantObject.transform.rotation = rotation;
            camera.transform.rotation = rotation;
            camera.transform.position = -(rotation * Vector3.forward) * 10;
            var first = Capture(1);
            var second = Capture(3);
            Assert.Greater(ChangedPixels(first, second), 50);
            Assert.LessOrEqual(Mathf.Abs(Centroid(first, true) - Centroid(second, true)), 1.1f);
        }

        [Test]
        public void SpriteSheetUVRect_ProducesSameRootAndTipMotion()
        {
            var fullA = Capture(1);
            var fullB = Capture(3);
            var atlas = new Texture2D(128, 128, TextureFormat.RGBA32, false, true);
            atlas.filterMode = FilterMode.Point;
            atlas.SetPixels(new Color[128 * 128]);
            atlas.SetPixels(32, 32, 64, 64, texture.GetPixels());
            atlas.Apply();
            var sheetSprite = Sprite.Create(atlas, new Rect(32, 32, 64, 64), new Vector2(0.5f, 0.5f), 64, 0, SpriteMeshType.FullRect);
            try
            {
                renderer.sprite = sheetSprite;
                material.SetVector("_SpriteUVRect", new Vector4(0.25f, 0.25f, 0.5f, 0.5f));
                Assert.LessOrEqual(ChangedPixels(fullA, Capture(1)), 20);
                Assert.LessOrEqual(ChangedPixels(fullB, Capture(3)), 20);
            }
            finally { renderer.sprite = sprite; Object.DestroyImmediate(sheetSprite); Object.DestroyImmediate(atlas); }
        }

        private static void SavePixels(Color32[] pixels, string path)
        {
            var image = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false, true);
            try { image.SetPixels32(pixels); image.Apply(); File.WriteAllBytes(path, image.EncodeToPNG()); }
            finally { Object.DestroyImmediate(image); }
        }
    }
}
