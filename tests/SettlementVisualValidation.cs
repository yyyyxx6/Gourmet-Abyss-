#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

// Copy into the isolated project's Assets/Editor before using -executeMethod.
[InitializeOnLoad]
public static class SettlementVisualValidation
{
    private const string PhaseKey = "ChefDungeon.SettlementVisual.Phase";
    private const string ResultKey = "ChefDungeon.SettlementVisual.Result";
    private const string DeadlineKey = "ChefDungeon.SettlementVisual.Deadline";
    private const string OutputDirectory = "L:/\u6599\u7406\u5730\u7262/.validation/Batch1/Screenshots";
    private const int CaptureLayer = 31;
    private static double readyAt;
    private static int shotIndex;
    private static SettlementUIController view;
    private static Camera renderCamera;
    private static RenderTexture renderTarget;
    private static Canvas renderCanvas;
    private static RunResultSnapshot successResult;
    private static RunResultSnapshot deathResult;
    private static RunResultSnapshot largeResult;
    private static RunResultSnapshot singleEmptyResult;
    private static SettlementPresentationCatalog catalog;
    private static Sprite woodIcon;
    private static Sprite petIcon;
    private static Sprite recipeIcon;
    private static readonly List<ShotReport> reports = new List<ShotReport>();
    private static readonly Shot[] shots =
    {
        new Shot("settlement-success-1920x1080", 1920, 1080, Fixture.Success),
        new Shot("settlement-success-1280x720", 1280, 720, Fixture.Success),
        new Shot("settlement-death-1024x768", 1024, 768, Fixture.Death),
        new Shot("settlement-large-numbers-1024x768", 1024, 768, Fixture.LargeNumbers),
        new Shot("settlement-single-empty-1280x720", 1280, 720, Fixture.SingleEmpty)
    };

    static SettlementVisualValidation()
    {
        EditorApplication.update += Tick;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    public static void Run()
    {
        PlayerSettings.companyName = "CodexValidation";
        PlayerSettings.productName = "ChefDungeonSettlementVisual";
        Directory.CreateDirectory(OutputDirectory);
        SettlementPanelBuilder.Build();
        SessionState.SetInt(ResultKey, 1);
        SessionState.SetFloat(DeadlineKey, (float)EditorApplication.timeSinceStartup + 180f);
        SessionState.SetString(PhaseKey, "enter");
        EditorSceneManager.OpenScene("Assets/Scenes/UpGround.unity", OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
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
            Finish(new TimeoutException("Settlement visual validation timed out in phase " + phase));
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
                shotIndex = 0;
                SetupShot(shots[shotIndex]);
                SessionState.SetString(PhaseKey, "capture");
                return;
            }
            if (phase != "capture" || view == null || view.canvasGroup.alpha < 0.999f) return;
            Capture(shots[shotIndex]);
            shotIndex++;
            if (shotIndex == shots.Length)
            {
                WriteReport();
                Finish(null);
            }
            else
            {
                SetupShot(shots[shotIndex]);
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
        Check(GameValManager.Instance != null && RestaurantPanel.instance != null,
            "The real resource and recipe managers must initialize before rendering.");
        foreach (ResourceType type in new[] { ResourceType.LootMushroom, ResourceType.Loot_RatMeat, ResourceType.Loot_Paste })
        {
            ResourceItem item = GameValManager.Instance.GetResourceInfo(type);
            Check(item != null && item.Icon != null, "A real resource icon is required for " + type);
        }
        catalog = Resources.Load<SettlementPresentationCatalog>("UI/SettlementPresentationCatalog");
        Check(catalog != null, "The final presentation catalog must exist.");
        SettlementPresentationCatalog.ResourcePresentation wood;
        SettlementPresentationCatalog.PetPresentation pet;
        Check(catalog.TryGetResource(ResourceType.LootPumkin, out wood) && wood.icon != null,
            "The catalog must provide the rendered wood icon.");
        Check(catalog.TryGetPet(PetType.FlyingCompanion, out pet) && pet.icon != null,
            "The catalog must provide the actual pet portrait.");
        woodIcon = wood.icon;
        petIcon = pet.icon;
        Check(AssetDatabase.GetAssetPath(woodIcon).StartsWith("Assets/Resources/UI/SettlementIcons/", StringComparison.Ordinal) &&
            AssetDatabase.GetAssetPath(petIcon).StartsWith("Assets/Resources/UI/SettlementIcons/", StringComparison.Ordinal),
            "Wood and pet presentation must use the generated real-model artwork.");
        Check(woodIcon != GameValManager.Instance.GetResourceInfo(ResourceType.LootPumkin).Icon,
            "The old pumpkin sprite must not be used for wood.");
        DishRecipe recipe = null;
        foreach (DishRecipe candidate in RestaurantPanel.instance.dishRecipes)
        {
            if (candidate != null && candidate.dishID >= 0 && candidate.dishIcon != null &&
                !string.IsNullOrWhiteSpace(candidate.dishName))
            {
                recipe = candidate;
                break;
            }
        }
        Check(recipe != null, "A real named recipe with an icon is required.");
        recipeIcon = recipe.dishIcon;

        RunSessionData success = new RunSessionData();
        success.Begin("Layer1", new Dictionary<ResourceType, int> { { ResourceType.LootMushroom, 3 } });
        success.AdvanceTime(702);
        for (int index = 0; index < 37; index++) success.RecordKill();
        success.RecordGathered(ResourceType.LootPumkin, 18);
        success.RecordRecipeUnlocked(recipe.dishID);
        success.RecordPetUnlocked(PetType.FlyingCompanion);
        List<InventorySlotSnapshot> inventory = new List<InventorySlotSnapshot>
        {
            new InventorySlotSnapshot(0, ResourceType.LootMushroom, 4, 4),
            new InventorySlotSnapshot(1, ResourceType.LootMushroom, 2, 4),
            new InventorySlotSnapshot(2, ResourceType.Loot_RatMeat, 4, 4),
            new InventorySlotSnapshot(3, ResourceType.Loot_Paste, 3, 4),
            new InventorySlotSnapshot(4, ResourceType.Loot_RatMeat, 1, 4)
        };
        for (int index = inventory.Count; index < 12; index++)
            inventory.Add(new InventorySlotSnapshot(index, ResourceType.None, 0, 4));
        successResult = success.Complete(new InventorySnapshot(inventory),
            new Dictionary<ResourceType, int> { { ResourceType.LootPumkin, 18 } });

        deathResult = CreateDeathResult(11, false, recipe.dishID);
        largeResult = CreateDeathResult(int.MaxValue, true, recipe.dishID);
        RunSessionData empty = new RunSessionData();
        empty.Begin("Layer1", new Dictionary<ResourceType, int>());
        singleEmptyResult = empty.Complete(new InventorySnapshot(new[]
        {
            new InventorySlotSnapshot(0, ResourceType.None, 0, 4)
        }), new Dictionary<ResourceType, int>());

        GameObject prefab = Resources.Load<GameObject>("UI/SettlementPanel");
        Check(prefab != null, "Build Resources/UI/SettlementPanel before running visual validation.");
        GameObject panel = UnityEngine.Object.Instantiate(prefab);
        panel.name = "SettlementVisualCapture";
        foreach (Transform child in panel.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = CaptureLayer;
        view = panel.GetComponent<SettlementUIController>();
        Check(view != null, "The built prefab must have its runtime controller.");
        Check(view.presentationCatalog == catalog, "The rebuilt prefab must serialize the final catalog reference.");
        renderCanvas = panel.GetComponent<Canvas>();
        panel.GetComponent<CanvasScaler>().enabled = false;
        renderCanvas.renderMode = RenderMode.ScreenSpaceCamera;
        renderCanvas.overrideSorting = true;
        renderCanvas.sortingOrder = 32766;
        renderCanvas.planeDistance = 100f;

        renderCamera = new GameObject("SettlementCaptureCamera", typeof(Camera)).GetComponent<Camera>();
        renderCamera.enabled = false;
        renderCamera.transform.position = new Vector3(0, 0, -100);
        renderCamera.transform.rotation = Quaternion.identity;
        renderCamera.orthographic = true;
        renderCamera.nearClipPlane = 0.1f;
        renderCamera.farClipPlane = 300f;
        renderCamera.clearFlags = CameraClearFlags.SolidColor;
        renderCamera.backgroundColor = new Color32(28, 42, 35, 255);
        renderCamera.cullingMask = 1 << CaptureLayer;
        renderCamera.allowHDR = false;
        renderCamera.allowMSAA = false;
        renderCanvas.worldCamera = renderCamera;
        Application.runInBackground = true;
        Time.timeScale = 0f;
    }

    private static RunResultSnapshot CreateDeathResult(int gatheredWood, bool addUnlocks, int recipeId)
    {
        List<InventorySlotSnapshot> slots = new List<InventorySlotSnapshot>
        {
            new InventorySlotSnapshot(0, ResourceType.LootMushroom, 4, 4),
            new InventorySlotSnapshot(1, ResourceType.LootMushroom, 4, 4),
            new InventorySlotSnapshot(2, ResourceType.Loot_RatMeat, 4, 4)
        };
        for (int index = slots.Count; index < 12; index++)
            slots.Add(new InventorySlotSnapshot(index, ResourceType.None, 0, 4));
        InventorySnapshot beforeDeath = new InventorySnapshot(slots);
        RunSessionData death = new RunSessionData();
        death.Begin("Layer1", beforeDeath.GetCounts());
        death.AdvanceTime(574);
        for (int index = 0; index < 9; index++) death.RecordKill();
        death.RecordGathered(ResourceType.LootPumkin, gatheredWood);
        if (addUnlocks)
        {
            death.RecordRecipeUnlocked(recipeId);
            death.RecordPetUnlocked(PetType.FlyingCompanion);
        }
        DeathDropResult drop = DeathDropCalculator.Calculate(beforeDeath,
            new Dictionary<ResourceType, int> { { ResourceType.LootPumkin, gatheredWood } }, 0.1m);
        Check(drop.RetainedInventory.GetItemCount(ResourceType.LootMushroom) == 1 &&
            drop.RetainedInventory.GetItemCount(ResourceType.Loot_RatMeat) == 1,
            "The real death calculator must retain two food units from the per-type quotas.");
        if (gatheredWood == 11)
            Check(drop.RetainedGathered[ResourceType.LootPumkin] == 2 && drop.DroppedGathered[ResourceType.LootPumkin] == 9,
                "The real ten-percent rule must retain two wood and drop nine.");
        return death.Complete(drop.RetainedInventory, drop.RetainedGathered);
    }

    private static RunResultSnapshot ResultFor(Shot shot)
    {
        switch (shot.fixture)
        {
            case Fixture.Death: return deathResult;
            case Fixture.LargeNumbers: return largeResult;
            case Fixture.SingleEmpty: return singleEmptyResult;
            default: return successResult;
        }
    }

    private static void SetupShot(Shot shot)
    {
        if (renderTarget != null)
        {
            renderCamera.targetTexture = null;
            renderTarget.Release();
            UnityEngine.Object.Destroy(renderTarget);
        }
        renderTarget = new RenderTexture(shot.width, shot.height, 24, RenderTextureFormat.ARGB32)
        {
            name = shot.name,
            antiAliasing = 1
        };
        renderTarget.Create();
        renderCamera.targetTexture = renderTarget;
        renderCamera.pixelRect = new Rect(0, 0, shot.width, shot.height);
        renderCamera.aspect = (float)shot.width / shot.height;
        float scale = Mathf.Sqrt((shot.width / 1920f) * (shot.height / 1080f));
        renderCamera.orthographicSize = shot.height / (2f * scale);
        renderCanvas.scaleFactor = scale;
        view.Show(ResultFor(shot),
            shot.death ? RunEndReason.Death : RunEndReason.Extracted,
            new Dictionary<ResourceType, long>
            {
                { ResourceType.LootPumkin, shot.fixture == Fixture.LargeNumbers ? 2L * int.MaxValue : 821L }
            }, () => { }, () => { });
        if (shot.fixture == Fixture.Death)
            view.SetStatusMessage("\u91c7\u96c6\u7269\u4ed3\u5e93\u7a7a\u95f4\u4e0d\u8db3\uff0c\u8bf7\u5148\u8fd4\u56de\u5c0f\u9547\u6574\u7406\u8d44\u6e90\uff0c\u7136\u540e\u91cd\u65b0\u51fa\u53d1\u3002");
        readyAt = EditorApplication.timeSinceStartup + 0.55;
    }

    private static void Capture(Shot shot)
    {
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(view.detailsContent);
        LayoutRebuilder.ForceRebuildLayoutImmediate(view.inventoryContent);
        view.detailsScroll.verticalNormalizedPosition = 1f;
        Canvas.ForceUpdateCanvases();
        if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)
            RenderPipeline.SubmitRenderRequest(renderCamera, new UniversalRenderPipeline.SingleCameraRequest { destination = renderTarget });
        else
            renderCamera.Render();
        Texture2D image = new Texture2D(shot.width, shot.height, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        try
        {
            RenderTexture.active = renderTarget;
            image.ReadPixels(new Rect(0, 0, shot.width, shot.height), 0, 0, false);
            image.Apply(false, false);
            string path = Path.Combine(OutputDirectory, shot.name + ".png");
            byte[] png = image.EncodeToPNG();
            File.WriteAllBytes(path, png);
            ShotReport report = Inspect(shot, image, png.Length);
            reports.Add(report);
            Debug.Log("SETTLEMENT_VISUAL_SHOT_PASS " + path + " " + JsonUtility.ToJson(report));
        }
        finally
        {
            RenderTexture.active = previous;
            UnityEngine.Object.Destroy(image);
        }
    }

    private static ShotReport Inspect(Shot shot, Texture2D image, int pngBytes)
    {
        Check(view.IsVisible && view.canvasGroup.alpha >= 0.999f, "The settlement must be fully visible while paused.");
        Check(view.retryButton.interactable && view.homeButton.interactable, "Both result actions must be available.");
        RunResultSnapshot result = ResultFor(shot);
        Check(view.petSection.activeSelf == (result.NewPetTypes.Count > 0) &&
            view.recipeSection.activeSelf == (result.NewRecipeIds.Count > 0) &&
            view.gatheredSection.activeSelf == (result.GatheredCounts.Count > 0), "Only populated reward categories may be visible.");
        string expectedDelta = result.IngredientDelta > 0 ? "+" + result.IngredientDelta : result.IngredientDelta.ToString(CultureInfo.InvariantCulture);
        Check(view.ingredientDeltaText.text == expectedDelta, "The exact food delta must be displayed.");
        Check(!shot.death || view.ingredientDeltaText.text == "-10", "Death must show two retained food units minus the carried-in twelve.");
        Check(!shot.death || view.ingredientDeltaText.color.r > view.ingredientDeltaText.color.g,
            "Negative ingredient changes must use red text.");

        ShotReport report = new ShotReport
        {
            name = shot.name, width = shot.width, height = shot.height,
            scaleFactor = renderCanvas.scaleFactor, pngBytes = pngBytes
        };
        RectTransform canvasRect = renderCanvas.GetComponent<RectTransform>();
        float expectedWidth = shot.width / renderCanvas.scaleFactor;
        float expectedHeight = shot.height / renderCanvas.scaleFactor;
        Check(Mathf.Abs(canvasRect.rect.width - expectedWidth) < 2f && Mathf.Abs(canvasRect.rect.height - expectedHeight) < 2f,
            "The camera-backed canvas must use target texture dimensions and the intended geometric scale.");

        AddBounds(report, "Panel", view.transform.Find("Panel").GetComponent<RectTransform>(), shot);
        AddBounds(report, "Title", view.titleText.rectTransform, shot);
        AddBounds(report, "InventoryViewport", view.inventoryScroll.viewport, shot);
        AddBounds(report, "DetailsViewport", view.detailsScroll.viewport, shot);
        RectTransform statistics = view.transform.Find("Panel/Statistics").GetComponent<RectTransform>();
        Rect statisticsBounds = AddBounds(report, "Statistics", statistics, shot);
        foreach (Text stat in new[] { view.ingredientDeltaText, view.durationText, view.killsText })
        {
            Check(!stat.transform.IsChildOf(view.detailsContent) && stat.transform.IsChildOf(statistics),
                "Food delta, duration and kills must remain outside the scrolling rewards.");
            AddBounds(report, stat.transform.parent.name, stat.rectTransform, shot);
        }
        Check(!statisticsBounds.Overlaps(ScreenBounds(view.detailsScroll.viewport)), "Reward scrolling must not cover the fixed statistics.");
        Vector2 statsPosition = statisticsBounds.position;
        view.detailsScroll.verticalNormalizedPosition = 0f;
        Canvas.ForceUpdateCanvases();
        Check(Vector2.Distance(statsPosition, ScreenBounds(statistics).position) < 0.1f, "Scrolling rewards must not move statistics.");
        view.detailsScroll.verticalNormalizedPosition = 1f;
        Canvas.ForceUpdateCanvases();
        Rect retry = AddBounds(report, "Retry", view.retryButton.GetComponent<RectTransform>(), shot);
        Rect home = AddBounds(report, "Home", view.homeButton.GetComponent<RectTransform>(), shot);
        Check(!retry.Overlaps(home), "The two navigation buttons must not overlap.");
        if (view.statusText.gameObject.activeInHierarchy)
        {
            Rect status = AddBounds(report, "Status", view.statusText.rectTransform, shot);
            Check(!status.Overlaps(retry) && !status.Overlaps(home), "Status messages must not overlap result actions.");
        }
        int visibleSlots = 0;
        int visibleIcons = 0;
        foreach (Transform entry in view.inventoryContent)
        {
            if (!entry.gameObject.activeInHierarchy) continue;
            Rect slot = ScreenBounds(entry.GetComponent<RectTransform>());
            Check(Contains(ScreenBounds(view.inventoryScroll.viewport), slot, 2f), "All owned bag slots must fit in the viewport.");
            visibleSlots++;
            Image icon = entry.Find("Icon").GetComponent<Image>();
            if (icon.gameObject.activeInHierarchy && icon.sprite != null) visibleIcons++;
        }
        int expectedIcons = 0;
        foreach (InventorySlotSnapshot slot in result.Inventory.Slots)
            if (!slot.IsEmpty) expectedIcons++;
        Check(visibleSlots == result.Inventory.Slots.Count, "All owned slots, including empty slots, must render.");
        Check(visibleIcons == expectedIcons, "Every occupied ingredient slot must use actual artwork.");
        if (shot.fixture == Fixture.SingleEmpty)
            Check(view.inventoryContent.GetComponent<GridLayoutGroup>().constraintCount == 1,
                "The one-slot layout must adapt to a single column.");
        report.slotCount = visibleSlots;
        report.ingredientIconCount = visibleIcons;
        InspectRewardPresentation(shot, result, report);

        Color32[] pixels = image.GetPixels32();
        InspectSectionHeaders(shot, report, pixels);
        Color32 background = pixels[0];
        HashSet<int> colors = new HashSet<int>();
        int inspected = 0;
        int different = 0;
        for (int index = 0; index < pixels.Length; index += 7)
        {
            Color32 color = pixels[index];
            inspected++;
            if (Math.Abs(color.r - background.r) + Math.Abs(color.g - background.g) + Math.Abs(color.b - background.b) > 45)
                different++;
            colors.Add((color.r >> 3) << 10 | (color.g >> 3) << 5 | color.b >> 3);
        }
        report.foregroundFraction = (float)different / inspected;
        report.quantizedColors = colors.Count;
        Check(report.foregroundFraction > 0.25f && report.foregroundFraction < 0.96f,
            "The result panel must occupy a substantial, correctly framed part of the image.");
        Check(colors.Count > 32 && pngBytes > 10000, "The captured canvas must contain rendered text and artwork, not a flat fill.");
        return report;
    }

    private static void InspectSectionHeaders(Shot shot, ShotReport report, Color32[] pixels)
    {
        foreach (GameObject section in new[] { view.petSection, view.recipeSection, view.gatheredSection })
        {
            if (!section.activeInHierarchy) continue;
            Text header = section.transform.Find("Header").GetComponent<Text>();
            AssertRenderedTextComplete(header);
            Check(header.cachedTextGenerator.vertexCount > 4 && !header.canvasRenderer.cull,
                "The visible section header must produce unculled glyph geometry: " + header.text);
            Rect bounds = ScreenBounds(header.rectTransform);
            Check(Contains(ScreenBounds(view.detailsScroll.viewport), bounds, 2f),
                "The section header must remain in the visible reward viewport: " + header.text);
            int minX = Mathf.Clamp(Mathf.FloorToInt(bounds.xMin), 0, shot.width - 1);
            int maxX = Mathf.Clamp(Mathf.CeilToInt(bounds.xMax), 0, shot.width);
            int minY = Mathf.Clamp(Mathf.FloorToInt(bounds.yMin) - 2, 0, shot.height - 1);
            int maxY = Mathf.Clamp(Mathf.CeilToInt(bounds.yMax) + 2, 0, shot.height);
            int inkPixels = 0;
            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    Color32 pixel = pixels[y * shot.width + x];
                    if (pixel.r < 160 && pixel.g < 140 && pixel.b < 125) inkPixels++;
                }
            }
            Check(inkPixels > 8, "The screenshot must contain visible dark header strokes: " + header.text);
            report.visibleHeaders.Add(header.text);
            report.headerInkPixels += inkPixels;
        }
    }

    private static void InspectRewardPresentation(Shot shot, RunResultSnapshot result, ShotReport report)
    {
        if (view.petSection.activeSelf)
        {
            Transform row = FirstActiveChild(view.petContent);
            Image icon = row.Find("Icon").GetComponent<Image>();
            Check(icon.sprite == petIcon && !icon.transform.Find("Placeholder").gameObject.activeSelf,
                "The pet entry must use its actual catalog portrait instead of a placeholder.");
            InspectNewMarker(icon);
            report.newMarkerCount++;
            report.petIconPath = AssetDatabase.GetAssetPath(icon.sprite);
        }
        if (view.recipeSection.activeSelf)
        {
            Transform row = FirstActiveChild(view.recipeContent);
            Image icon = row.Find("Icon").GetComponent<Image>();
            Check(icon.sprite == recipeIcon, "The new recipe must retain the actual recipe icon.");
            InspectNewMarker(icon);
            report.newMarkerCount++;
        }
        if (!view.gatheredSection.activeSelf) return;

        Transform gathered = FirstActiveChild(view.gatheredContent);
        Image wood = gathered.Find("Icon").GetComponent<Image>();
        Check(wood.sprite == woodIcon && !wood.transform.Find("Placeholder").gameObject.activeSelf,
            "Collected wood must use the catalog wood sprite, not the legacy pumpkin icon.");
        Check(!wood.transform.Find("New").gameObject.activeSelf, "Gathered materials must not acquire an unlock marker.");
        report.woodIconPath = AssetDatabase.GetAssetPath(wood.sprite);
        Text gain = gathered.Find("Gain").GetComponent<Text>();
        Text total = gathered.Find("Total").GetComponent<Text>();
        Text outcome = gathered.Find("Outcome").GetComponent<Text>();
        long expectedOwned = shot.fixture == Fixture.LargeNumbers ? 2L * int.MaxValue : 821L;
        string expectedGain = "\u672c\u5c40 +" + result.GatheredCounts[ResourceType.LootPumkin].ToString(CultureInfo.InvariantCulture);
        string expectedTotal = "\u62e5\u6709 " + expectedOwned.ToString(CultureInfo.InvariantCulture);
        Check(gain.text == expectedGain, "Death must not replace actual collected quantity with retained quantity.");
        Check(total.text == expectedTotal, "The full long owned quantity must render without narrowing or abbreviation.");
        Check(outcome.gameObject.activeSelf == shot.death, "Only death results must display carried-out and lost counts.");
        if (shot.death)
        {
            int retained = result.RetainedGatheredCounts[ResourceType.LootPumkin];
            long lost = (long)result.GatheredCounts[ResourceType.LootPumkin] - retained;
            string expectedOutcome = "\u5e26\u51fa " + retained.ToString(CultureInfo.InvariantCulture) +
                "  \u00b7  \u9057\u5931 " + lost.ToString(CultureInfo.InvariantCulture);
            Check(outcome.text == expectedOutcome, "Death must show the calculator's retained and lost quantities.");
            AssertRenderedTextComplete(outcome);
            Check(Contains(ScreenBounds(view.detailsScroll.viewport), ScreenBounds(outcome.rectTransform), 2f),
                "Death loss information must fit inside the visible rewards viewport for these fixtures.");
            report.outcomeText = outcome.text;
        }
        AssertRenderedTextComplete(gain);
        AssertRenderedTextComplete(total);
        report.gainedText = gain.text;
        report.ownedText = total.text;
    }

    private static void InspectNewMarker(Image icon)
    {
        RectTransform marker = icon.transform.Find("New").GetComponent<RectTransform>();
        Check(marker.gameObject.activeInHierarchy && marker.parent == icon.transform,
            "A NEW badge must belong to the pet or recipe icon.");
        Check(marker.anchorMin == new Vector2(0, 1) && marker.anchorMax == new Vector2(0, 1),
            "The NEW badge must be anchored to the icon's top-left corner.");
        Rect iconBounds = ScreenBounds(icon.rectTransform);
        Rect markerBounds = ScreenBounds(marker);
        Check(markerBounds.center.x < iconBounds.center.x && markerBounds.center.y > iconBounds.center.y,
            "The rendered NEW badge must remain at the upper-left of the icon.");
        Check(Contains(ScreenBounds(view.detailsScroll.viewport), markerBounds, 2f),
            "The NEW badge must remain visible inside the rewards viewport.");
        Check(marker.Find("Label").GetComponent<Text>().text == "NEW", "The icon badge must show NEW.");
    }

    private static Transform FirstActiveChild(Transform parent)
    {
        foreach (Transform child in parent)
            if (child.gameObject.activeInHierarchy) return child;
        throw new InvalidOperationException("Expected an active presentation row under " + parent.name);
    }

    private static void AssertRenderedTextComplete(Text text)
    {
        Check(text.cachedTextGenerator.characterCountVisible >= text.text.Length,
            "The text generator must retain every character of: " + text.text);
    }

    private static Rect AddBounds(ShotReport report, string name, RectTransform rect, Shot shot)
    {
        Rect bounds = ScreenBounds(rect);
        Check(Contains(new Rect(0, 0, shot.width, shot.height), bounds, 2f), name + " must stay inside the target viewport.");
        report.rects.Add(new RectReport { name = name, x = bounds.x, y = bounds.y, width = bounds.width, height = bounds.height });
        return bounds;
    }

    private static Rect ScreenBounds(RectTransform rect)
    {
        Vector3[] corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue);
        Vector2 max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector3 corner in corners)
        {
            Vector3 point = renderCamera.WorldToScreenPoint(corner);
            min = Vector2.Min(min, point);
            max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static bool Contains(Rect outer, Rect inner, float tolerance)
    {
        return inner.xMin >= outer.xMin - tolerance && inner.yMin >= outer.yMin - tolerance &&
            inner.xMax <= outer.xMax + tolerance && inner.yMax <= outer.yMax + tolerance;
    }

    private static void WriteReport()
    {
        Directory.CreateDirectory(OutputDirectory);
        File.WriteAllText(Path.Combine(OutputDirectory, "settlement-visual-report.json"),
            JsonUtility.ToJson(new ValidationReport { shots = reports }, true));
    }

    private static void Finish(Exception error)
    {
        SessionState.SetInt(ResultKey, error == null ? 0 : 1);
        if (error == null) Debug.Log("SETTLEMENT_VISUAL_VALIDATION_PASS");
        else Debug.LogError("SETTLEMENT_VISUAL_VALIDATION_FAIL: " + error);
        Time.timeScale = 1f;
        if (view != null) UnityEngine.Object.Destroy(view.gameObject);
        if (renderCamera != null)
        {
            renderCamera.targetTexture = null;
            UnityEngine.Object.Destroy(renderCamera.gameObject);
        }
        if (renderTarget != null)
        {
            renderTarget.Release();
            UnityEngine.Object.Destroy(renderTarget);
        }
        SessionState.SetString(PhaseKey, "exit");
        EditorApplication.ExitPlaymode();
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private enum Fixture { Success, Death, LargeNumbers, SingleEmpty }

    private sealed class Shot
    {
        public readonly string name;
        public readonly int width;
        public readonly int height;
        public readonly Fixture fixture;
        public bool death { get { return fixture == Fixture.Death || fixture == Fixture.LargeNumbers; } }
        public Shot(string name, int width, int height, Fixture fixture)
        {
            this.name = name;
            this.width = width;
            this.height = height;
            this.fixture = fixture;
        }
    }

    [Serializable]
    private sealed class ValidationReport { public List<ShotReport> shots; }

    [Serializable]
    private sealed class ShotReport
    {
        public string name;
        public int width;
        public int height;
        public float scaleFactor;
        public int pngBytes;
        public int slotCount;
        public int ingredientIconCount;
        public int newMarkerCount;
        public string petIconPath;
        public string woodIconPath;
        public string gainedText;
        public string ownedText;
        public string outcomeText;
        public List<string> visibleHeaders = new List<string>();
        public int headerInkPixels;
        public float foregroundFraction;
        public int quantizedColors;
        public List<RectReport> rects = new List<RectReport>();
    }

    [Serializable]
    private sealed class RectReport
    {
        public string name;
        public float x;
        public float y;
        public float width;
        public float height;
    }
}
#endif
