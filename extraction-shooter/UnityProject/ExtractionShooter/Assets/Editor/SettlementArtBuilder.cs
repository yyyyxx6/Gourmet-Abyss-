using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Renders existing game models into transparent presentation sprites.</summary>
[InitializeOnLoad]
public static class SettlementArtBuilder
{
    private const string PhaseKey = "ChefDungeon.SettlementArt.Phase";
    private const string ResultKey = "ChefDungeon.SettlementArt.Result";
    private const string CompanyKey = "ChefDungeon.SettlementArt.Company";
    private const string ProductKey = "ChefDungeon.SettlementArt.Product";
    private const string IconFolder = "Assets/Resources/UI/SettlementIcons";
    private const string CatalogPath = "Assets/Resources/UI/SettlementPresentationCatalog.asset";
    private const string WoodPrefab = "Assets/ImportAsset/FarmCrops/Prefabs/PlanterModules/Log_1m_01.prefab";
    private const string PetPrefab = "Assets/Suriyun/Monster Pack Forest/Prefab/Planta/Planta_Queen.prefab";
    private const int Layer = 31;
    private const int Size = 256;
    private static double readyAt;

    static SettlementArtBuilder()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetString(PhaseKey, "") == "enter")
            {
                readyAt = EditorApplication.timeSinceStartup + 3;
                SessionState.SetString(PhaseKey, "render");
            }
            else if (state == PlayModeStateChange.EnteredEditMode && !string.IsNullOrEmpty(SessionState.GetString(PhaseKey, "")))
                SessionState.SetString(PhaseKey, "exit");
        };
    }

    [MenuItem("Tools/Chef Dungeon/Render Settlement Icons")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Start icon generation outside Play Mode.");
        SessionState.SetString(CompanyKey, PlayerSettings.companyName);
        SessionState.SetString(ProductKey, PlayerSettings.productName);
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "ChefDungeonSettlementArt";
        EditorSceneManager.OpenScene("Assets/Scenes/UpGround.unity", OpenSceneMode.Single);
        SessionState.SetInt(ResultKey, 1);
        SessionState.SetString(PhaseKey, "enter");
        EditorApplication.EnterPlaymode();
    }

    private static void Tick()
    {
        string phase = SessionState.GetString(PhaseKey, "");
        if (phase == "exit" && !EditorApplication.isPlayingOrWillChangePlaymode)
        {
            SessionState.EraseString(PhaseKey);
            PlayerSettings.companyName = SessionState.GetString(CompanyKey, PlayerSettings.companyName);
            PlayerSettings.productName = SessionState.GetString(ProductKey, PlayerSettings.productName);
            SessionState.EraseString(CompanyKey);
            SessionState.EraseString(ProductKey);
            AssetDatabase.SaveAssets();
            if (Application.isBatchMode) EditorApplication.Exit(SessionState.GetInt(ResultKey, 1));
            return;
        }
        if (phase != "render" || !EditorApplication.isPlaying || EditorApplication.timeSinceStartup < readyAt) return;
        SessionState.SetString(PhaseKey, "working");
        try
        {
            Directory.CreateDirectory(AbsoluteAssetPath(IconFolder));
            string proofFolder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "ArtProof");
            Directory.CreateDirectory(proofFolder);
            string woodPath = IconFolder + "/Wood.png";
            string petPath = IconFolder + "/FlyingCompanion.png";
            RenderModel(WoodPrefab, new Vector3(1.1f, 0.8f, -1.3f), AbsoluteAssetPath(woodPath));
            Vector3[] directions = { new Vector3(0.25f, 0.25f, 1f), new Vector3(-0.25f, 0.25f, -1f),
                new Vector3(1f, 0.25f, 0.1f), new Vector3(-1f, 0.25f, -0.1f) };
            for (int index = 0; index < directions.Length; index++)
                RenderModel(PetPrefab, directions[index], Path.Combine(proofFolder, "pet-view-" + index + ".png"));
            File.Copy(Path.Combine(proofFolder, "pet-view-0.png"), AbsoluteAssetPath(petPath), true);
            AssetDatabase.Refresh();
            Sprite wood = ImportSprite(woodPath);
            Sprite pet = ImportSprite(petPath);
            SettlementPresentationCatalog catalog = AssetDatabase.LoadAssetAtPath<SettlementPresentationCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<SettlementPresentationCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }
            if (catalog.resources == null) catalog.resources = new List<SettlementPresentationCatalog.ResourcePresentation>();
            if (catalog.pets == null) catalog.pets = new List<SettlementPresentationCatalog.PetPresentation>();
            if (!catalog.TryGetResource(ResourceType.LootPumkin, out var resource))
            {
                resource = new SettlementPresentationCatalog.ResourcePresentation { type = ResourceType.LootPumkin, displayName = "木材" };
                catalog.resources.Add(resource);
            }
            resource.icon = wood;
            if (!catalog.TryGetPet(PetType.FlyingCompanion, out var presentation))
            {
                presentation = new SettlementPresentationCatalog.PetPresentation { type = PetType.FlyingCompanion, displayName = "飞行随从" };
                catalog.pets.Add(presentation);
            }
            presentation.icon = pet;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            SessionState.SetInt(ResultKey, 0);
            Debug.Log("SETTLEMENT_ART_BUILD_PASS " + CatalogPath);
        }
        catch (Exception error)
        {
            Debug.LogError("SETTLEMENT_ART_BUILD_FAIL: " + error);
        }
        finally
        {
            SessionState.SetString(PhaseKey, "exit");
            EditorApplication.ExitPlaymode();
        }
    }

    private static Sprite ImportSprite(string path)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.alphaIsTransparency = true;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
        Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) throw new InvalidOperationException("Cannot import presentation icon: " + path);
        return sprite;
    }

    private static string AbsoluteAssetPath(string path)
    {
        return Path.Combine(Directory.GetParent(Application.dataPath).FullName, path);
    }

    private static void RenderModel(string prefabPath, Vector3 direction, string outputPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) throw new InvalidOperationException("Missing source model: " + prefabPath);
        GameObject model = UnityEngine.Object.Instantiate(prefab, new Vector3(1000, 1000, 1000), Quaternion.identity);
        Camera camera = new GameObject("SettlementIconCamera", typeof(Camera)).GetComponent<Camera>();
        Light light = new GameObject("SettlementIconLight", typeof(Light)).GetComponent<Light>();
        RenderTexture target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
        var temporaryMaterials = new List<Material>();
        try
        {
            foreach (MonoBehaviour behaviour in model.GetComponentsInChildren<MonoBehaviour>(true))
                UnityEngine.Object.DestroyImmediate(behaviour);
            foreach (Collider collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            foreach (Rigidbody body in model.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(body);
            foreach (Animator animator in model.GetComponentsInChildren<Animator>(true))
            {
                animator.Rebind();
                animator.Update(0f);
                animator.enabled = false;
            }
            foreach (Transform child in model.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = Layer;
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("No visible renderer in " + prefabPath);
            Bounds bounds = renderers[0].bounds;
            foreach (Renderer renderer in renderers)
            {
                bounds.Encapsulate(renderer.bounds);
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    Material source = materials[index];
                    if (source == null) continue;
                    string shaderName = source.shader == null ? "" : source.shader.name;
                    if (shaderName.StartsWith("ProPixelizer/", StringComparison.Ordinal) ||
                        shaderName.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal)) continue;
                    // Convert only the preview copy; original materials and textures stay untouched.
                    var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    if (source.mainTexture != null) material.SetTexture("_BaseMap", source.mainTexture);
                    material.SetColor("_BaseColor", source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
                    material.SetFloat("_Smoothness", 0.2f);
                    temporaryMaterials.Add(material);
                    materials[index] = material;
                }
                renderer.sharedMaterials = materials;
            }
            camera.enabled = false;
            camera.orthographic = true;
            camera.aspect = 1f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.cullingMask = 1 << Layer;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 100f;
            camera.transform.position = bounds.center + direction.normalized * 10f;
            camera.transform.LookAt(bounds.center);
            float extent = 0;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            for (int mask = 0; mask < 8; mask++)
            {
                Vector3 corner = new Vector3((mask & 1) == 0 ? min.x : max.x,
                    (mask & 2) == 0 ? min.y : max.y, (mask & 4) == 0 ? min.z : max.z);
                Vector3 local = camera.transform.InverseTransformPoint(corner);
                extent = Mathf.Max(extent, Mathf.Abs(local.x), Mathf.Abs(local.y));
            }
            camera.orthographicSize = Mathf.Max(0.05f, extent * 1.14f);
            UniversalAdditionalCameraData cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.volumeLayerMask = 0;
            light.type = LightType.Point;
            light.cullingMask = 1 << Layer;
            light.range = 30f;
            light.intensity = 5f;
            light.transform.position = bounds.center + new Vector3(-2, 3, 3);
            target.antiAliasing = 1;
            target.Create();
            camera.targetTexture = target;
            Color32[] dark = RenderPixels(camera, target, Color.black);
            Color32[] bright = RenderPixels(camera, target, Color.white);
            Color32[] pixels = new Color32[dark.Length];
            int visible = 0;
            for (int index = 0; index < pixels.Length; index++)
            {
                Color black = dark[index];
                Color white = bright[index];
                float alpha = Mathf.Clamp01(1f - ((white.r - black.r) + (white.g - black.g) + (white.b - black.b)) / 3f);
                if (alpha < 0.02f) { pixels[index] = new Color32(0, 0, 0, 0); continue; }
                if (alpha > 0.98f) alpha = 1f;
                pixels[index] = new Color(Mathf.Clamp01(black.r / alpha), Mathf.Clamp01(black.g / alpha),
                    Mathf.Clamp01(black.b / alpha), alpha);
                visible++;
            }
            if (visible < pixels.Length / 50 || visible > pixels.Length * 0.9f)
                throw new InvalidOperationException("Invalid transparent silhouette for " + prefabPath + ": " + visible);
            Texture2D image = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            image.SetPixels32(pixels);
            image.Apply();
            File.WriteAllBytes(outputPath, image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            Debug.Log("SETTLEMENT_ICON_RENDERED " + outputPath + " silhouette=" + visible + "/" + pixels.Length);
        }
        finally
        {
            camera.targetTexture = null;
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
            UnityEngine.Object.DestroyImmediate(light.gameObject);
            UnityEngine.Object.DestroyImmediate(model);
            foreach (Material material in temporaryMaterials) UnityEngine.Object.DestroyImmediate(material);
        }
    }

    private static Color32[] RenderPixels(Camera camera, RenderTexture target, Color background)
    {
        camera.backgroundColor = background;
        RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = target;
        Texture2D image = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        try
        {
            image.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            image.Apply();
            return image.GetPixels32();
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
