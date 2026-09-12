using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class SettlementPanelBuilder
{
    private const string OutputPath = "Assets/Resources/UI/SettlementPanel.prefab";
    private static readonly Color Ink = new Color32(53, 45, 38, 255);
    private static readonly Color MutedInk = new Color32(100, 88, 69, 255);
    private static readonly Color Green = new Color32(39, 77, 58, 255);

    [MenuItem("Tools/Chef Dungeon/Build Settlement Panel")]
    public static void Build()
    {
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Resources", "UI"));
        AssetDatabase.Refresh();
        Font font = Load<Font>("Assets/Font/AaHuanMengKongJianXiangSuTi-2.ttf");
        Sprite panelSprite = Load<Sprite>("Assets/Texture/CookUI/menu_panel_blank.png");
        Sprite slotSprite = Load<Sprite>("Assets/Texture/backpack_border_normal.png");
        Sprite retrySprite = LoadButtonSprite("Assets/Texture/button_light_normal.png", "Assets/Resources/UI/SettlementButtonLight.asset");
        Sprite homeSprite = LoadButtonSprite("Assets/Texture/button_dark_normal.png", "Assets/Resources/UI/SettlementButtonDark.asset");
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

            Image backdrop = MakeImage("Backdrop", rootRect, null, new Color32(17, 25, 21, 210));
            Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = true;
            Image panel = MakeImage("Panel", rootRect, panelSprite, Color.white);
            SetRect(panel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1540f, 960f));
            panel.raycastTarget = true;
            RectTransform body = panel.rectTransform;

            Text eyebrow = MakeText("Eyebrow", body, "\u672c\u6b21\u63a2\u7d22", font, 22, MutedInk);
            PlaceTopLeft(eyebrow.rectTransform, 60, 36, 500, 30);
            view.titleText = MakeText("Title", body, "\u63a2\u7d22\u7ed3\u675f", font, 52, Green);
            PlaceTopLeft(view.titleText.rectTransform, 58, 72, 1200, 70);
            AddLine(body, new Vector2(60, -162), new Vector2(1340, 2));

            Text bagLabel = MakeText("InventoryLabel", body, "\u80cc\u5305", font, 30, Ink);
            PlaceTopLeft(bagLabel.rectTransform, 60, 190, 440, 44);
            view.inventorySummaryText = MakeText("InventorySummary", body, "0 / 0", font, 24, MutedInk, TextAnchor.MiddleRight);
            PlaceTopLeft(view.inventorySummaryText.rectTransform, 475, 190, 135, 44);
            view.inventoryScroll = MakeScroll("Inventory", body, new Vector2(60, -254), new Vector2(562, 460), out RectTransform inventoryContent, false);
            view.inventoryContent = inventoryContent;
            GridLayoutGroup grid = inventoryContent.gameObject.AddComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(124, 128);
            grid.spacing = new Vector2(18, 20);
            grid.padding = new RectOffset(4, 4, 4, 8);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            grid.childAlignment = TextAnchor.UpperLeft;

            Image divider = MakeImage("ColumnDivider", body, null, new Color32(132, 111, 76, 85));
            PlaceTopLeft(divider.rectTransform, 674, 194, 2, 532);
            view.detailsScroll = MakeScroll("Details", body, new Vector2(726, -194), new Vector2(670, 400), out RectTransform detailsContent);
            view.detailsContent = detailsContent;
            AddVerticalLayout(detailsContent, 14);
            view.petSection = MakeSection("Pets", detailsContent, "\u65b0\u5ba0\u7269", font, out RectTransform pets);
            view.petContent = pets;
            view.recipeSection = MakeSection("Recipes", detailsContent, "\u65b0\u83dc\u8c31", font, out RectTransform recipes);
            view.recipeContent = recipes;
            view.gatheredSection = MakeSection("Gathered", detailsContent, "\u91c7\u96c6\u7269", font, out RectTransform gathered);
            view.gatheredContent = gathered;
            AddLine(body, new Vector2(726, -598), new Vector2(670, 1));
            RectTransform statistics = MakeRect("Statistics", body);
            PlaceTopLeft(statistics, 726, 612, 652, 132);
            AddVerticalLayout(statistics, 0);
            view.ingredientDeltaText = MakeStat(statistics, "IngredientDelta", "\u83b7\u5f97\u98df\u6750\u6570", font);
            view.durationText = MakeStat(statistics, "Duration", "\u5192\u9669\u65f6\u957f", font);
            view.killsText = MakeStat(statistics, "Kills", "\u51fb\u6740\u602a\u7269\u6570", font);

            AddLine(body, new Vector2(60, -758), new Vector2(1340, 2));
            view.statusText = MakeText("Status", body, string.Empty, font, 22, new Color32(153, 48, 48, 255));
            PlaceTopLeft(view.statusText.rectTransform, 60, 784, 620, 84);
            view.statusText.resizeTextForBestFit = true;
            view.statusText.resizeTextMinSize = 18;
            view.statusText.resizeTextMaxSize = 22;
            view.statusText.gameObject.SetActive(false);
            view.retryButton = MakeButton("Retry", body, "\u91cd\u65b0\u63a2\u7d22", font, retrySprite, Green);
            PlaceTopLeft(view.retryButton.GetComponent<RectTransform>(), 740, 795, 310, 70);
            view.homeButton = MakeButton("Home", body, "\u8fd4\u56de\u5c0f\u9547", font, homeSprite, Color.white);
            PlaceTopLeft(view.homeButton.GetComponent<RectTransform>(), 1090, 795, 310, 70);

            // Fit the established two-column layout inside the existing wooden frame.
            foreach (RectTransform child in body)
            {
                child.anchoredPosition = new Vector2(113f, -63f) + child.anchoredPosition * 0.9f;
                child.localScale = Vector3.one * 0.9f;
            }

            RectTransform templates = MakeRect("Templates", rootRect);
            view.inventorySlotTemplate = MakeSlotTemplate(templates, slotSprite, font);
            view.rewardRowTemplate = MakeRewardTemplate(templates, font);
            templates.gameObject.SetActive(false);
            view.petSection.SetActive(false);
            view.recipeSection.SetActive(false);
            view.gatheredSection.SetActive(false);
            root.SetActive(false);

            PrefabUtility.SaveAsPrefabAsset(root, OutputPath);
            AssetDatabase.SaveAssets();
            Debug.Log("Built " + OutputPath);
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
        rect.sizeDelta = new Vector2(124, 128);
        Image icon = MakeImage("Icon", rect, null, Color.white);
        icon.preserveAspect = true;
        SetRect(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 5), new Vector2(80, 80));
        Text fallback = MakeText("Name", rect, string.Empty, font, 23, Color.white, TextAnchor.MiddleCenter);
        Stretch(fallback.rectTransform, new Vector2(9, 30), new Vector2(-9, -12));
        fallback.resizeTextForBestFit = true;
        fallback.resizeTextMinSize = 16;
        fallback.resizeTextMaxSize = 23;
        Text count = MakeText("Count", rect, string.Empty, font, 26, Color.white, TextAnchor.LowerRight);
        Stretch(count.rectTransform, new Vector2(8, 8), new Vector2(-10, -86));
        count.resizeTextForBestFit = true;
        count.resizeTextMinSize = 14;
        count.resizeTextMaxSize = 26;
        Outline shadow = count.gameObject.AddComponent<Outline>();
        shadow.effectColor = new Color32(52, 34, 29, 255);
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
        SetRect(icon.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(34, 0), new Vector2(56, 56));
        Text placeholder = MakeText("Placeholder", icon.transform, "", font, 28, MutedInk, TextAnchor.MiddleCenter);
        Stretch(placeholder.rectTransform);
        Text name = MakeText("Name", rect, string.Empty, font, 25, Ink);
        Stretch(name.rectTransform, new Vector2(80, 8), new Vector2(-218, -8));
        name.resizeTextForBestFit = true;
        name.resizeTextMinSize = 18;
        name.resizeTextMaxSize = 25;
        Text gain = MakeText("Gain", rect, string.Empty, font, 24, Green, TextAnchor.MiddleRight);
        SetRect(gain.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-104, -25), new Vector2(200, 30));
        gain.resizeTextForBestFit = true;
        gain.resizeTextMinSize = 16;
        gain.resizeTextMaxSize = 24;
        Text total = MakeText("Total", rect, string.Empty, font, 19, MutedInk, TextAnchor.MiddleRight);
        SetRect(total.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-104, -53), new Vector2(200, 26));
        total.resizeTextForBestFit = true;
        total.resizeTextMinSize = 14;
        total.resizeTextMaxSize = 19;
        Text outcome = MakeText("Outcome", rect, string.Empty, font, 18, new Color32(143, 66, 40, 255), TextAnchor.MiddleRight);
        outcome.rectTransform.anchorMin = new Vector2(0, 0);
        outcome.rectTransform.anchorMax = new Vector2(1, 0);
        outcome.rectTransform.pivot = new Vector2(0.5f, 0);
        outcome.rectTransform.offsetMin = new Vector2(80, 3);
        outcome.rectTransform.offsetMax = new Vector2(-4, 29);
        outcome.resizeTextForBestFit = true;
        outcome.resizeTextMinSize = 14;
        outcome.resizeTextMaxSize = 18;
        Image marker = MakeImage("New", icon.transform, null, new Color32(170, 62, 34, 255));
        SetRect(marker.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(-4, 4), new Vector2(44, 20), new Vector2(0, 1));
        Text markerText = MakeText("Label", marker.transform, "NEW", font, 15, new Color32(255, 249, 216, 255), TextAnchor.MiddleCenter);
        Stretch(markerText.rectTransform);
        return rect;
    }

    private static GameObject MakeSection(string name, Transform parent, string title, Font font, out RectTransform content)
    {
        RectTransform section = MakeRect(name, parent);
        AddVerticalLayout(section, 4);
        Text header = MakeText("Header", section, title, font, 28, Ink);
        // Pixel-font line metrics can round above this single-line box at smaller canvas scales.
        header.verticalOverflow = VerticalWrapMode.Overflow;
        LayoutElement headerLayout = header.gameObject.AddComponent<LayoutElement>();
        headerLayout.minHeight = 34;
        headerLayout.preferredHeight = 34;
        content = MakeRect("Rows", section);
        AddVerticalLayout(content, 4);
        content.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(0, 0, 4, 4);
        return section.gameObject;
    }

    private static Text MakeStat(Transform parent, string name, string label, Font font)
    {
        RectTransform row = MakeRect(name, parent);
        LayoutElement layout = row.gameObject.AddComponent<LayoutElement>();
        layout.minHeight = 44;
        layout.preferredHeight = 44;
        Text caption = MakeText("Label", row, label, font, 25, MutedInk);
        Stretch(caption.rectTransform, new Vector2(4, 0), new Vector2(-230, 0));
        Text value = MakeText("Value", row, "0", font, 30, Ink, TextAnchor.MiddleRight);
        SetRect(value.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-112, 0), new Vector2(220, 48));
        value.resizeTextForBestFit = true;
        value.resizeTextMinSize = 16;
        value.resizeTextMaxSize = 30;
        return value;
    }

    private static Button MakeButton(string name, Transform parent, string label, Font font, Sprite sprite, Color textColor)
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
        Text caption = MakeText("Label", image.transform, label, font, 30, textColor, TextAnchor.MiddleCenter);
        Stretch(caption.rectTransform, new Vector2(16, 5), new Vector2(-16, -5));
        return button;
    }

    private static ScrollRect MakeScroll(string name, Transform parent, Vector2 position, Vector2 size, out RectTransform content,
        bool showScrollbar = true)
    {
        RectTransform root = MakeRect(name, parent);
        SetRect(root, new Vector2(0, 1), new Vector2(0, 1), position, size, new Vector2(0, 1));
        ScrollRect scroll = root.gameObject.AddComponent<ScrollRect>();
        Image viewport = MakeImage("Viewport", root, null, new Color(1f, 1f, 1f, 0.001f));
        viewport.raycastTarget = true;
        Stretch(viewport.rectTransform, Vector2.zero, new Vector2(showScrollbar ? -18f : 0f, 0f));
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
        scroll.scrollSensitivity = 30;
        if (showScrollbar)
        {
            Image track = MakeImage("Scrollbar", root, null, new Color32(112, 97, 73, 35));
            track.raycastTarget = true;
            track.rectTransform.anchorMin = new Vector2(1, 0);
            track.rectTransform.anchorMax = new Vector2(1, 1);
            track.rectTransform.pivot = new Vector2(1, 0.5f);
            track.rectTransform.sizeDelta = new Vector2(7, 0);
            track.rectTransform.anchoredPosition = Vector2.zero;
            Scrollbar scrollbar = track.gameObject.AddComponent<Scrollbar>();
            RectTransform slideArea = MakeRect("SlidingArea", track.transform);
            Stretch(slideArea);
            Image handle = MakeImage("Handle", slideArea, null, new Color32(93, 111, 80, 210));
            Stretch(handle.rectTransform);
            handle.raycastTarget = true;
            scrollbar.handleRect = handle.rectTransform;
            scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }
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

    private static void AddLine(Transform parent, Vector2 position, Vector2 size)
    {
        Image image = MakeImage("Divider", parent, null, new Color32(132, 111, 76, 110));
        SetRect(image.rectTransform, new Vector2(0, 1), new Vector2(0, 1), position, size, new Vector2(0, 1));
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

    private static Sprite LoadButtonSprite(string texturePath, string spritePath)
    {
        Sprite imported = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
        if (imported != null) return imported;
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
        if (existing != null) return existing;

        Texture2D texture = Load<Texture2D>(texturePath);
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
        sprite.name = Path.GetFileNameWithoutExtension(spritePath);
        AssetDatabase.CreateAsset(sprite, spritePath);
        return sprite;
    }
}
