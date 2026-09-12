#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Copy into the isolated project's Assets/Editor and run DeathCrateVisualValidation.Run.
[InitializeOnLoad]
public static class DeathCrateVisualValidation
{
    private const string PhaseKey = "ChefDungeon.DeathCrateVisual.Phase";
    private const string ResultKey = "ChefDungeon.DeathCrateVisual.Result";
    private const string DeadlineKey = "ChefDungeon.DeathCrateVisual.Deadline";
    private const string OutputDirectory = "L:/\u6599\u7406\u5730\u7262/.validation/Batch1/Screenshots";
    private const int CaptureLayer = 31;
    private const int Width = 960;
    private const int Height = 640;
    private static double readyAt;
    private static GameObject previewRoot;
    private static GameObject crate;
    private static Camera captureCamera;
    private static RenderTexture target;
    private static Bounds modelBounds;
    private static VisualReport report;

    static DeathCrateVisualValidation()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static void Run()
    {
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "ChefDungeonDeathCrateVisual";
        Directory.CreateDirectory(OutputDirectory);
        try
        {
            DeathLootCrateBuilder.Build();
            SessionState.SetInt(ResultKey, 1);
            SessionState.SetFloat(DeadlineKey, (float)EditorApplication.timeSinceStartup + 180f);
            SessionState.SetString(PhaseKey, "enter");
            EditorSceneManager.OpenScene("Assets/Scenes/UpGround.unity", OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }
        catch (Exception error)
        {
            Debug.LogError("DEATH_CRATE_VISUAL_VALIDATION_FAIL: " + error);
            EditorApplication.Exit(1);
        }
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetString(PhaseKey, "") == "enter")
        {
            readyAt = EditorApplication.timeSinceStartup + 3.0;
            SessionState.SetString(PhaseKey, "warmup");
        }
    }

    private static void Tick()
    {
        string phase = SessionState.GetString(PhaseKey, "");
        if (string.IsNullOrEmpty(phase)) return;
        if (phase == "exit")
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
            {
                SessionState.EraseString(PhaseKey);
                EditorApplication.Exit(SessionState.GetInt(ResultKey, 1));
            }
            return;
        }
        if (EditorApplication.timeSinceStartup > SessionState.GetFloat(DeadlineKey, float.MaxValue))
        {
            Finish(new TimeoutException("Death crate visual validation timed out in " + phase));
            return;
        }
        if (!EditorApplication.isPlaying) return;
        EditorApplication.QueuePlayerLoopUpdate();
        if (EditorApplication.timeSinceStartup < readyAt) return;
        try
        {
            if (phase == "warmup")
            {
                Prepare();
                readyAt = EditorApplication.timeSinceStartup + 0.75;
                SessionState.SetString(PhaseKey, "capture");
                return;
            }
            if (phase == "capture")
            {
                Capture();
                Finish(null);
            }
        }
        catch (Exception error)
        {
            WriteReport();
            Finish(error);
        }
    }

    private static void Prepare()
    {
        Check(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset,
            "The real UpGround Universal Render Pipeline must be active.");
        GameObject prefab = Resources.Load<GameObject>("Loot/DeathLootCrate");
        Check(prefab != null, "The built death crate prefab must load from Resources/Loot.");
        previewRoot = new GameObject("DeathCrateVisualCapture");
        previewRoot.transform.position = new Vector3(1000, 1000, 1000);
        crate = UnityEngine.Object.Instantiate(prefab, previewRoot.transform, false);
        crate.name = "DeathLootCratePreview";
        foreach (Transform child in crate.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = CaptureLayer;

        Collider[] colliders = crate.GetComponentsInChildren<Collider>(true);
        Check(colliders.Length == 1 && colliders[0] is SphereCollider && colliders[0].isTrigger,
            "The crate must have exactly one nonblocking sphere trigger.");
        SphereCollider trigger = (SphereCollider)colliders[0];
        Check(Mathf.Abs(trigger.radius - 1.8f) < 0.001f && Vector3.Distance(trigger.center, new Vector3(0, 0.55f, 0)) < 0.001f,
            "The generated collection trigger must use the reviewed radius and center.");
        MonoBehaviour[] behaviours = crate.GetComponentsInChildren<MonoBehaviour>(true);
        Check(behaviours.Length == 1 && behaviours[0] is DeathLootCrate,
            "Only DeathLootCrate may remain; ordinary loot, expiry and legacy chest behaviours must be absent.");
        Check(crate.GetComponentsInChildren<LootCollector>(true).Length == 0,
            "Death crates must not use ordinary timed loot collection.");
        Renderer[] renderers = crate.GetComponentsInChildren<Renderer>(true);
        Check(renderers.Length > 0, "The crate must retain its model renderers.");
        HashSet<string> shaders = new HashSet<string>();
        bool initializedBounds = false;
        foreach (Renderer renderer in renderers)
        {
            Check(renderer.enabled && renderer.gameObject.activeInHierarchy, "All retained model renderers must be visible.");
            if (!initializedBounds)
            {
                modelBounds = renderer.bounds;
                initializedBounds = true;
            }
            else modelBounds.Encapsulate(renderer.bounds);
            foreach (Material material in renderer.sharedMaterials)
            {
                Check(material != null && material.shader != null, "The existing model must retain valid original materials.");
                shaders.Add(material.shader.name);
            }
        }
        Check(modelBounds.size.sqrMagnitude > 0.01f, "The existing model must have nonzero visible bounds.");

        captureCamera = new GameObject("DeathCrateCaptureCamera", typeof(Camera), typeof(UniversalAdditionalCameraData)).GetComponent<Camera>();
        captureCamera.transform.SetParent(previewRoot.transform, true);
        captureCamera.enabled = false;
        captureCamera.orthographic = true;
        captureCamera.nearClipPlane = 0.05f;
        captureCamera.farClipPlane = 100f;
        captureCamera.clearFlags = CameraClearFlags.SolidColor;
        captureCamera.backgroundColor = new Color32(29, 39, 43, 255);
        captureCamera.cullingMask = 1 << CaptureLayer;
        captureCamera.allowHDR = false;
        captureCamera.allowMSAA = false;
        captureCamera.aspect = (float)Width / Height;
        UniversalAdditionalCameraData cameraData = captureCamera.GetComponent<UniversalAdditionalCameraData>();
        cameraData.SetRenderer(-1);
        cameraData.renderPostProcessing = false;
        cameraData.requiresColorTexture = true;
        cameraData.requiresDepthTexture = true;

        Vector3 offset = new Vector3(1.6f, 1.35f, -2.2f).normalized;
        float distance = Mathf.Max(4f, modelBounds.extents.magnitude * 5f);
        captureCamera.transform.position = modelBounds.center + offset * distance;
        captureCamera.transform.rotation = Quaternion.LookRotation(-offset, Vector3.up);
        float halfWidth = 0;
        float halfHeight = 0;
        foreach (Vector3 corner in BoundsCorners(modelBounds))
        {
            Vector3 relative = corner - modelBounds.center;
            halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(relative, captureCamera.transform.right)));
            halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(relative, captureCamera.transform.up)));
        }
        captureCamera.orthographicSize = Mathf.Max(halfHeight, halfWidth / captureCamera.aspect) * 1.28f;
        target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32)
        {
            name = "DeathCrateCapture", antiAliasing = 1
        };
        target.Create();
        captureCamera.targetTexture = target;
        captureCamera.pixelRect = new Rect(0, 0, Width, Height);

        // Existing ProPixelizer renderer features handle this ordinary URP camera.
        // CameraSnapSRP is unnecessary for a static shot and would alter scene transforms.
        MakePreviewLight("CrateKey", modelBounds.center + new Vector3(-2, 3, -3), new Color(1f, 0.88f, 0.72f), 22f);
        MakePreviewLight("CrateFill", modelBounds.center + new Vector3(2, 1.5f, 1), new Color(0.76f, 0.88f, 1f), 10f);
        report = new VisualReport
        {
            width = Width, height = Height,
            rendererCount = renderers.Length, colliderCount = colliders.Length,
            triggerRadius = trigger.radius, triggerCenter = trigger.center,
            boundsCenter = modelBounds.center, boundsSize = modelBounds.size,
            shaderNames = new List<string>(shaders),
            pipeline = GraphicsSettings.currentRenderPipeline.name,
            orthographicSize = captureCamera.orthographicSize
        };
        Application.runInBackground = true;
        Time.timeScale = 0f;
    }

    private static void MakePreviewLight(string name, Vector3 position, Color color, float intensity)
    {
        Light light = new GameObject(name, typeof(Light)).GetComponent<Light>();
        light.transform.SetParent(previewRoot.transform, true);
        light.transform.position = position;
        light.type = LightType.Point;
        light.cullingMask = 1 << CaptureLayer;
        light.color = color;
        light.intensity = intensity;
        light.range = 12f;
        light.shadows = LightShadows.None;
    }

    private static void Capture()
    {
        RenderPipeline.SubmitRenderRequest(captureCamera,
            new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        Texture2D image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0, false);
            image.Apply(false, false);
            byte[] png = image.EncodeToPNG();
            File.WriteAllBytes(Path.Combine(OutputDirectory, "death-loot-crate.png"), png);
            report.pngBytes = png.Length;

            Color32[] pixels = image.GetPixels32();
            Color32 background = pixels[0];
            HashSet<int> uniqueColors = new HashSet<int>();
            int foreground = 0;
            int magenta = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                Color32 pixel = pixels[index];
                if (Math.Abs(pixel.r - background.r) + Math.Abs(pixel.g - background.g) + Math.Abs(pixel.b - background.b) > 40)
                    foreground++;
                if (pixel.r > 180 && pixel.b > 180 && pixel.g < 100 && Math.Abs(pixel.r - pixel.b) < 70)
                    magenta++;
                uniqueColors.Add((pixel.r >> 3) << 10 | (pixel.g >> 3) << 5 | pixel.b >> 3);
            }
            report.foregroundFraction = (float)foreground / pixels.Length;
            report.magentaFraction = (float)magenta / pixels.Length;
            report.quantizedColors = uniqueColors.Count;
            Rect bounds = GetScreenBounds(modelBounds);
            report.projectedBounds = new Vector4(bounds.x, bounds.y, bounds.width, bounds.height);
            WriteReport();
            Check(bounds.xMin >= 2 && bounds.yMin >= 2 && bounds.xMax <= Width - 2 && bounds.yMax <= Height - 2,
                "The complete chest model must fit inside the frame.");
            Check(bounds.width > Width * 0.35f && bounds.height > Height * 0.35f,
                "The model must be large enough to inspect its shape and material.");
            Check(report.foregroundFraction > 0.05f && report.foregroundFraction < 0.85f,
                "The screenshot must show a visible model against a surrounding background.");
            Check(report.magentaFraction < 0.01f, "The original shader must not render as a large error-magenta surface.");
            Check(uniqueColors.Count > 24 && png.Length > 5000, "The model must render textured detail rather than a flat fill.");
            Debug.Log("DEATH_CRATE_VISUAL_SHOT_PASS " + Path.Combine(OutputDirectory, "death-loot-crate.png") + " " + JsonUtility.ToJson(report));
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.Destroy(image);
        }
    }

    private static Vector3[] BoundsCorners(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        return new[]
        {
            new Vector3(min.x, min.y, min.z), new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z), new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, min.y, min.z), new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, min.z), new Vector3(max.x, max.y, max.z)
        };
    }

    private static Rect GetScreenBounds(Bounds bounds)
    {
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 corner in BoundsCorners(bounds))
        {
            Vector3 point = captureCamera.WorldToScreenPoint(corner);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static void WriteReport()
    {
        if (report == null) return;
        Directory.CreateDirectory(OutputDirectory);
        File.WriteAllText(Path.Combine(OutputDirectory, "death-loot-crate-report.json"), JsonUtility.ToJson(report, true));
    }

    private static void Finish(Exception error)
    {
        SessionState.SetInt(ResultKey, error == null ? 0 : 1);
        if (error == null) Debug.Log("DEATH_CRATE_VISUAL_VALIDATION_PASS");
        else Debug.LogError("DEATH_CRATE_VISUAL_VALIDATION_FAIL: " + error);
        Time.timeScale = 1f;
        if (captureCamera != null) captureCamera.targetTexture = null;
        if (target != null)
        {
            target.Release();
            UnityEngine.Object.Destroy(target);
        }
        if (previewRoot != null) UnityEngine.Object.Destroy(previewRoot);
        SessionState.SetString(PhaseKey, "exit");
        EditorApplication.ExitPlaymode();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [Serializable]
    private sealed class VisualReport
    {
        public int width;
        public int height;
        public int rendererCount;
        public int colliderCount;
        public float triggerRadius;
        public Vector3 triggerCenter;
        public Vector3 boundsCenter;
        public Vector3 boundsSize;
        public Vector4 projectedBounds;
        public List<string> shaderNames;
        public string pipeline;
        public float orthographicSize;
        public int pngBytes;
        public float foregroundFraction;
        public float magentaFraction;
        public int quantizedColors;
    }
}
#endif
