using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class SettlementPanelBuilder
{
    private const string OutputPath = "Assets/Resources/UI/SettlementPanel.prefab";
    private const string ArtworkPath = "Assets/NewVersion/UI/\u6218\u6597\u7ed3\u7b97\u754c\u9762/";
    private const string SpriteFolder = "Assets/Resources/UI/SettlementArt";
    private static readonly Color Ink = new Color32(248, 235, 195, 255);
    private static readonly Color MutedInk = new Color32(220, 217, 174, 255);
    private static readonly Color PaperInk = new Color32(76, 60, 42, 255);
    private static readonly Dictionary<string, Texture2D> FormalTextures = new Dictionary<string, Texture2D>();

    [MenuItem("Tools/Chef Dungeon/Build Settlement Panel")]
    public static void Build()
    {
        FormalTextures.Clear();
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Resources", "UI", "SettlementArt", "Textures"));
        AssetDatabase.Refresh();
        Font font = Load<Font>("Assets/Font/AaHuanMengKongJianXiangSuTi-2.ttf");
        Sprite panelSprite = LoadFormalSprite("\u7ed3\u7b97\u5e95\u677f.png", "FormalBase");
        Sprite titleSprite = LoadFormalSprite("\u7ed3\u7b97\u6807\u9898.png", "FormalTitle");
        Sprite buttonSprite = LoadFormalSprite("\u6309\u952e.png", "FormalButton");
        Sprite rewardSprite = LoadFormalSprite("\u83b7\u53d6\u7269\u5c55\u793a\u6846.png", "FormalRewardFrame",
            null, new Vector4(100, 30, 100, 30));
        Sprite statisticSprite = LoadFormalSprite("\u6587\u672c\u5c55\u793a\u6846.png", "FormalTextFrame",
            null, new Vector4(50, 20, 42, 20));
        Sprite decorationSprite = LoadFormalSprite("\u88c5\u9970.png", "FormalBagDecoration");
        // Slice the supplied scroll instead of stretching its four painted slots across every bag size.
        Sprite bagTop = LoadFormalSprite("\u80cc\u5305.png", "FormalBagTop", new Rect(0, 615, 270, 76));
        Sprite bagSlot = LoadFormalSprite("\u80cc\u5305.png", "FormalBagSlot", new Rect(0, 465, 270, 150));
        Sprite bagBottom = LoadFormalSprite("\u80cc\u5305.png", "FormalBagBottom", new Rect(0, 0, 270, 34));

        GameObject root = new GameObject("SettlementPanel", typeof(RectTransform), typeof(Canvas),
            typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(CanvasGroup), typeof(SettlementUIController));
        try
        {
            root.layer = 5;
            RectTransform rootRect = root.GetComponent<RectTransform>();
            Stretch(rootRect);
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32766;
            CanvasScaler scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            SettlementUIController view = root.GetComponent<SettlementUIController>();
            view.canvasGroup = root.GetComponent<CanvasGroup>();
            view.presentationCatalog = AssetDatabase.LoadAssetAtPath<SettlementPresentationCatalog>(
                "Assets/Resources/UI/SettlementPresentationCatalog.asset");
            GameObject input = new GameObject("SettlementEventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            input.transform.SetParent(rootRect, false);
            input.layer = 5;
            view.eventSystem = input.GetComponent<EventSystem>();

            Image backdrop = MakeImage("Backdrop", rootRect, null, new Color32(13, 23, 15, 224));
            Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = true;
            RectTransform body = MakeRect("Panel", rootRect);
            SetRect(body, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1600f, 1000f));
            Image surface = MakeImage("Surface", body, panelSprite, Color.white);
            PlaceTopLeft(surface.rectTransform, 340, 70, 1220, 766);
            surface.raycastTarget = true;
            Image titlePlate = MakeImage("TitlePlate", body, titleSprite, Color.white);
            PlaceTopLeft(titlePlate.rectTransform, 575, 118, 750, 92);
            view.titleText = MakeText("Title", body, "\u63a2\u7d22\u7ed3\u675f", font, 44, Ink, TextAnchor.MiddleCenter);
            PlaceTopLeft(view.titleText.rectTransform, 597, 125, 706, 76);
            view.titleText.verticalOverflow = VerticalWrapMode.Overflow;

            Image decoration = MakeImage("BagDecoration", body, decorationSprite, Color.white);
            PlaceTopLeft(decoration.rectTransform, 57, 28, 222, 220);
            decoration.preserveAspect = true;
            Image scrollTop = MakeImage("BagTop", body, bagTop, Color.white);
            PlaceTopLeft(scrollTop.rectTransform, 52, 250, 235, 66);
            view.inventoryScroll = MakeScroll("Inventory", body, new Vector2(52, -316),
                new Vector2(251, 520), out RectTransform inventoryContent, 16);
            view.inventoryContent = inventoryContent;
            GridLayoutGroup grid = inventoryContent.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(235, 130);
            grid.spacing = Vector2.zero;
            grid.padding = new RectOffset();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 1;
            grid.childAlignment = TextAnchor.UpperCenter;
            Image scrollBottom = MakeImage("BagBottom", body, bagBottom, Color.white);
            PlaceTopLeft(scrollBottom.rectTransform, 52, 836, 235, 30);
            view.inventoryFooter = scrollBottom.rectTransform;
            view.inventorySummaryText = MakeText("InventorySummary", body, "\u80cc\u5305 0 / 0", font, 23, Ink, TextAnchor.MiddleCenter);
            PlaceTopLeft(view.inventorySummaryText.rectTransform, 52, 872, 235, 34);
            view.inventorySummaryText.verticalOverflow = VerticalWrapMode.Overflow;

            view.detailsScroll = MakeScroll("Details", body, new Vector2(400, -232),
                new Vector2(1100, 576), out RectTransform detailsContent, 18);
            view.detailsContent = detailsContent;
            AddVerticalLayout(detailsContent, 10);
            view.petSection = MakeSection("Pets", detailsContent, "\u65b0\u5ba0\u7269", font, rewardSprite, out RectTransform pets);
            view.petContent = pets;
            view.recipeSection = MakeSection("Recipes", detailsContent, "\u65b0\u83dc\u8c31", font, rewardSprite, out RectTransform recipes);
            view.recipeContent = recipes;
            view.gatheredSection = MakeSection("Gathered", detailsContent, "\u91c7\u96c6\u7269", font, rewardSprite, out RectTransform gathered);
            view.gatheredContent = gathered;

            RectTransform statistics = MakeRect("Statistics", detailsContent);
            AddVerticalLayout(statistics, 6);
            view.ingredientDeltaText = MakeStat(statistics, "IngredientDelta", "\u5e26\u51fa\u98df\u6750\u6570", font, statisticSprite);
            view.durationText = MakeStat(statistics, "Duration", "\u5192\u9669\u65f6\u957f", font, statisticSprite);
            view.killsText = MakeStat(statistics, "Kills", "\u51fb\u6740\u602a\u7269\u6570", font, statisticSprite);

            view.statusText = MakeText("Status", body, string.Empty, font, 22, new Color32(255, 194, 171, 255), TextAnchor.MiddleCenter);
            PlaceTopLeft(view.statusText.rectTransform, 400, 837, 1100, 34);
            view.statusText.resizeTextForBestFit = true;
            view.statusText.resizeTextMinSize = 18;
            view.statusText.resizeTextMaxSize = 22;
            view.statusText.verticalOverflow = VerticalWrapMode.Overflow;
            view.statusText.gameObject.SetActive(false);
            view.retryButton = MakeButton("Retry", body, "\u91cd\u65b0\u63a2\u7d22", font, buttonSprite);
            PlaceTopLeft(view.retryButton.GetComponent<RectTransform>(), 405, 875, 480, 87);
            view.homeButton = MakeButton("Home", body, "\u8fd4\u56de\u5c0f\u9547", font, buttonSprite);
            PlaceTopLeft(view.homeButton.GetComponent<RectTransform>(), 1015, 875, 480, 87);

            RectTransform templates = MakeRect("Templates", rootRect);
            view.inventorySlotTemplate = MakeSlotTemplate(templates, bagSlot, font);
            view.rewardRowTemplate = MakeRewardTemplate(templates, font);
            templates.gameObject.SetActive(false);
            view.petSection.SetActive(false);
            view.recipeSection.SetActive(false);
            view.gatheredSection.SetActive(false);
            root.SetActive(false);
            PrefabUtility.SaveAsPrefabAsset(root, OutputPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Built " + OutputPath + " with the formal settlement artwork.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static RectTransform MakeSlotTemplate(Transform parent, Sprite sprite, Font font)
    {
        Image image = MakeImage("InventorySlotTemplate", parent, sprite, Color.white);
        RectTransform rect = image.rectTransform;
        rect.sizeDelta = new Vector2(235, 130);
        Image icon = MakeImage("Icon", rect, null, Color.white);
        icon.preserveAspect = true;
        SetRect(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-13, 8), new Vector2(72, 72));
        Text fallback = MakeText("Name", rect, string.Empty, font, 21, PaperInk, TextAnchor.MiddleCenter);
        SetRect(fallback.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-13, 8), new Vector2(86, 76));
        fallback.resizeTextForBestFit = true;
        fallback.resizeTextMinSize = 14;
        fallback.resizeTextMaxSize = 21;
        Text count = MakeText("Count", rect, string.Empty, font, 25, Color.white, TextAnchor.MiddleRight);
        SetRect(count.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-8, -27), new Vector2(86, 28));
        count.resizeTextForBestFit = true;
        count.resizeTextMinSize = 13;
        count.resizeTextMaxSize = 25;
        count.verticalOverflow = VerticalWrapMode.Overflow;
        Outline shadow = count.gameObject.AddComponent<Outline>();
        shadow.effectColor = PaperInk;
        shadow.effectDistance = new Vector2(1, -1);
        return rect;
    }

    private static RectTransform MakeRewardTemplate(Transform parent, Font font)
    {
        RectTransform rect = MakeRect("RewardRowTemplate", parent);
        LayoutElement layout = rect.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = 76;
        layout.preferredHeight = 76;
        Image icon = MakeImage("Icon", rect, null, Color.white);
        icon.preserveAspect = true;
        SetRect(icon.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(38, 0), new Vector2(64, 64));
        Text placeholder = MakeText("Placeholder", icon.transform, "", font, 28, Ink, TextAnchor.MiddleCenter);
        Stretch(placeholder.rectTransform);
        Text name = MakeText("Name", rect, string.Empty, font, 26, Ink);
        Stretch(name.rectTransform, new Vector2(88, 8), new Vector2(-270, -8));
        name.resizeTextForBestFit = true;
        name.resizeTextMinSize = 20;
        name.resizeTextMaxSize = 26;
        name.verticalOverflow = VerticalWrapMode.Overflow;
        Text gain = MakeText("Gain", rect, string.Empty, font, 25, Ink, TextAnchor.MiddleRight);
        SetRect(gain.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-128, -24), new Vector2(248, 32));
        gain.resizeTextForBestFit = true;
        gain.resizeTextMinSize = 18;
        gain.resizeTextMaxSize = 25;
        gain.verticalOverflow = VerticalWrapMode.Overflow;
        Text total = MakeText("Total", rect, string.Empty, font, 21, MutedInk, TextAnchor.MiddleRight);
        SetRect(total.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-128, -54), new Vector2(248, 28));
        total.resizeTextForBestFit = true;
        total.resizeTextMinSize = 16;
        total.resizeTextMaxSize = 21;
        total.verticalOverflow = VerticalWrapMode.Overflow;
        Text outcome = MakeText("Outcome", rect, string.Empty, font, 20, new Color32(255, 208, 159, 255), TextAnchor.MiddleRight);
        outcome.rectTransform.anchorMin = new Vector2(0, 0);
        outcome.rectTransform.anchorMax = new Vector2(1, 0);
        outcome.rectTransform.pivot = new Vector2(0.5f, 0);
        outcome.rectTransform.offsetMin = new Vector2(88, 2);
        outcome.rectTransform.offsetMax = new Vector2(-4, 30);
        outcome.resizeTextForBestFit = true;
        outcome.resizeTextMinSize = 16;
        outcome.resizeTextMaxSize = 20;
        outcome.verticalOverflow = VerticalWrapMode.Overflow;
        Image marker = MakeImage("New", icon.transform, null, new Color32(166, 58, 31, 255));
        SetRect(marker.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(-3, 3), new Vector2(45, 21), new Vector2(0, 1));
        Text markerText = MakeText("Label", marker.transform, "NEW", font, 16, Ink, TextAnchor.MiddleCenter);
        Stretch(markerText.rectTransform);
        markerText.verticalOverflow = VerticalWrapMode.Overflow;
        return rect;
    }

    private static GameObject MakeSection(string name, Transform parent, string title, Font font, Sprite sprite,
        out RectTransform content)
    {
        Image frame = MakeImage(name, parent, sprite, Color.white);
        frame.type = Image.Type.Sliced;
        RectTransform section = frame.rectTransform;
        AddVerticalLayout(section, 0);
        section.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(34, 34, 6, 6);
        Text header = MakeText("Header", section, title, font, 26, Ink);
        header.verticalOverflow = VerticalWrapMode.Overflow;
        LayoutElement headerLayout = header.gameObject.AddComponent<LayoutElement>();
        headerLayout.minHeight = 32;
        headerLayout.preferredHeight = 32;
        content = MakeRect("Rows", section);
        AddVerticalLayout(content, 4);
        content.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(0, 0, 4, 4);
        return section.gameObject;
    }

    private static Text MakeStat(Transform parent, string name, string label, Font font, Sprite sprite)
    {
        Image image = MakeImage(name, parent, sprite, Color.white);
        image.type = Image.Type.Sliced;
        RectTransform row = image.rectTransform;
        LayoutElement layout = row.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = 48;
        layout.preferredHeight = 48;
        Text caption = MakeText("Label", row, label, font, 26, Ink);
        Stretch(caption.rectTransform, new Vector2(52, 0), new Vector2(-310, 0));
        caption.verticalOverflow = VerticalWrapMode.Overflow;
        Text value = MakeText("Value", row, "0", font, 28, Ink, TextAnchor.MiddleRight);
        SetRect(value.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-167, 0), new Vector2(280, 48));
        value.resizeTextForBestFit = true;
        value.resizeTextMinSize = 18;
        value.resizeTextMaxSize = 28;
        value.verticalOverflow = VerticalWrapMode.Overflow;
        return value;
    }

    private static Button MakeButton(string name, Transform parent, string label, Font font, Sprite sprite)
    {
        Image image = MakeImage(name, parent, sprite, Color.white);
        image.raycastTarget = true;
        Button button = image.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.08f, 1.08f, 1.08f, 1f);
        colors.selectedColor = colors.highlightedColor;
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.disabledColor = new Color(0.65f, 0.65f, 0.65f, 0.72f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        Text caption = MakeText("Label", image.transform, label, font, 32, new Color32(255, 242, 202, 255), TextAnchor.MiddleCenter);
        Stretch(caption.rectTransform, new Vector2(26, 8), new Vector2(-26, -8));
        caption.verticalOverflow = VerticalWrapMode.Overflow;
        Outline outline = caption.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color32(102, 59, 34, 220);
        outline.effectDistance = new Vector2(1, -1);
        return button;
    }

    private static ScrollRect MakeScroll(string name, Transform parent, Vector2 position, Vector2 size,
        out RectTransform content, float scrollbarSpace)
    {
        RectTransform root = MakeRect(name, parent);
        SetRect(root, new Vector2(0, 1), new Vector2(0, 1), position, size, new Vector2(0, 1));
        ScrollRect scroll = root.gameObject.AddComponent<ScrollRect>();
        Image viewport = MakeImage("Viewport", root, null, new Color(1f, 1f, 1f, 0.001f));
        viewport.raycastTarget = true;
        Stretch(viewport.rectTransform, Vector2.zero, new Vector2(-scrollbarSpace, 0f));
        viewport.gameObject.AddComponent<RectMask2D>();
        content = MakeRect("Content", viewport.transform);
        content.anchorMin = new Vector2(0, 1);
        content.anchorMax = new Vector2(1, 1);
        content.pivot = new Vector2(0.5f, 1);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        ContentSizeFitter fit = content.gameObject.AddComponent<ContentSizeFitter>();
        fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.viewport = viewport.rectTransform;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 36;
        Image track = MakeImage("Scrollbar", root, null, new Color32(174, 171, 92, 60));
        track.raycastTarget = true;
        track.rectTransform.anchorMin = new Vector2(1, 0);
        track.rectTransform.anchorMax = new Vector2(1, 1);
        track.rectTransform.pivot = new Vector2(1, 0.5f);
        track.rectTransform.sizeDelta = new Vector2(7, 0);
        track.rectTransform.anchoredPosition = Vector2.zero;
        Scrollbar scrollbar = track.gameObject.AddComponent<Scrollbar>();
        RectTransform slideArea = MakeRect("SlidingArea", track.transform);
        Stretch(slideArea);
        Image handle = MakeImage("Handle", slideArea, null, new Color32(201, 194, 109, 220));
        Stretch(handle.rectTransform);
        handle.raycastTarget = true;
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;
        scrollbar.direction = Scrollbar.Direction.BottomToTop;
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        return scroll;
    }

    private static void AddVerticalLayout(RectTransform rect, float spacing)
    {
        VerticalLayoutGroup layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private static RectTransform MakeRect(string name, Transform parent)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.layer = 5;
        obj.transform.SetParent(parent, false);
        return obj.GetComponent<RectTransform>();
    }

    private static Image MakeImage(string name, Transform parent, Sprite sprite, Color color)
    {
        RectTransform rect = MakeRect(name, parent);
        Image image = rect.gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Text MakeText(string name, Transform parent, string text, Font font, int size, Color color,
        TextAnchor alignment = TextAnchor.MiddleLeft)
    {
        RectTransform rect = MakeRect(name, parent);
        Text label = rect.gameObject.AddComponent<Text>();
        label.font = font;
        label.fontSize = size;
        label.color = color;
        label.text = text;
        label.alignment = alignment;
        label.raycastTarget = false;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        return label;
    }

    private static void PlaceTopLeft(RectTransform rect, float x, float y, float width, float height)
    {
        SetRect(rect, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(width, height), new Vector2(0, 1));
    }

    private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size,
        Vector2? pivot = null)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot ?? new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect, Vector2? min = null, Vector2? max = null)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = min ?? Vector2.zero;
        rect.offsetMax = max ?? Vector2.zero;
    }

    private static T Load<T>(string path) where T : UnityEngine.Object
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset == null) throw new InvalidOperationException("Required settlement asset is missing: " + path);
        return asset;
    }

    private static Sprite LoadFormalSprite(string filename, string assetName, Rect? sourceRect = null, Vector4 border = default)
    {
        Texture2D texture = LoadFormalTexture(filename);
        Rect rect = sourceRect ?? new Rect(0, 0, texture.width, texture.height);
        if (rect.xMin < 0 || rect.yMin < 0 || rect.xMax > texture.width || rect.yMax > texture.height)
            throw new InvalidOperationException("The formal artwork no longer matches the configured crop: " + filename);
        string path = SpriteFolder + "/" + assetName + ".asset";
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null && existing.texture == texture && existing.rect == rect && existing.border == border)
            return existing;
        Sprite sprite = Sprite.Create(texture, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, border);
        sprite.name = assetName;
        if (existing == null)
        {
            AssetDatabase.CreateAsset(sprite, path);
            return sprite;
        }
        EditorUtility.CopySerialized(sprite, existing);
        UnityEngine.Object.DestroyImmediate(sprite);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    private static Texture2D LoadFormalTexture(string filename)
    {
        Texture2D cached;
        if (FormalTextures.TryGetValue(filename, out cached)) return cached;
        string path = SpriteFolder + "/Textures/" + filename;
        string source = Path.Combine(Application.dataPath, ArtworkPath.Substring("Assets/".Length), filename);
        string target = Path.Combine(Application.dataPath, path.Substring("Assets/".Length));
        byte[] sourceBytes = File.ReadAllBytes(source);
        bool changed = !File.Exists(target) || new FileInfo(target).Length != sourceBytes.LongLength ||
            !SameBytes(sourceBytes, File.ReadAllBytes(target));
        if (changed) File.WriteAllBytes(target, sourceBytes);
        if (changed || AssetImporter.GetAtPath(path) == null)
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) throw new InvalidOperationException("Cannot import formal UI texture: " + path);
        bool settingsChanged = importer.textureType != TextureImporterType.Sprite ||
            importer.spriteImportMode != SpriteImportMode.Single || importer.spritePixelsPerUnit != 100f ||
            importer.npotScale != TextureImporterNPOTScale.None || importer.mipmapEnabled || !importer.alphaIsTransparency ||
            importer.textureCompression != TextureImporterCompression.Uncompressed || importer.maxTextureSize != 2048 ||
            importer.filterMode != FilterMode.Bilinear || importer.wrapMode != TextureWrapMode.Clamp;
        if (settingsChanged)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 100f;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 2048;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }
        Texture2D texture = Load<Texture2D>(path);
        FormalTextures.Add(filename, texture);
        return texture;
    }

    private static bool SameBytes(byte[] first, byte[] second)
    {
        if (first.Length != second.Length) return false;
        for (int index = 0; index < first.Length; index++)
            if (first[index] != second[index]) return false;
        return true;
    }
}
