using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Restaurant deposit and queue presentation; never changes ingredient quantities.</summary>
public static class RestaurantIngredientTransferPresentation
{
    public static GameObject Create(List<InventoryManager.RestaurantTransferSource> sources, bool showQuantity = true)
    {
        var host = new GameObject("RestaurantIngredientTransfer", typeof(RectTransform), typeof(Canvas));
        host.layer = 5;
        Canvas canvas = host.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;
        foreach (var source in sources)
        {
            var visual = new GameObject("Ingredient_" + source.type, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            visual.transform.SetParent(host.transform, false);
            CanvasGroup group = visual.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            Image image = visual.GetComponent<Image>();
            image.sprite = source.icon;
            image.enabled = source.icon != null;
            image.preserveAspect = true;
            image.raycastTarget = false;
            RectTransform rect = visual.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.sizeDelta = Vector2.one * (56f * Screen.height / 1080f);
            rect.anchoredPosition = source.screenPoint;
            var label = new GameObject("Count", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text), typeof(Outline));
            label.transform.SetParent(rect, false);
            RectTransform labelRect = label.GetComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
            Text text = label.GetComponent<Text>();
            text.font = RestaurantPanel.Instance != null && RestaurantPanel.Instance.foodInformationTitle != null
                ? RestaurantPanel.Instance.foodInformationTitle.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = source.icon != null ? source.quantity.ToString() :
                RestaurantIngredientStorage.GetDisplayName(source.type) + " " + source.quantity;
            if (!showQuantity) text.text = source.icon != null ? "" : RestaurantIngredientStorage.GetDisplayName(source.type);
            text.color = Color.white; text.alignment = TextAnchor.LowerRight;
            text.resizeTextForBestFit = true; text.resizeTextMinSize = 10; text.resizeTextMaxSize = 18;
            if (source.icon == null)
            {
                rect.sizeDelta = new Vector2(110f, 56f) * Screen.height / 1080f;
                text.alignment = TextAnchor.MiddleCenter;
            }
            text.raycastTarget = false;
            label.GetComponent<Outline>().effectColor = Color.black;
        }
        return host;
    }

    public static IEnumerator Animate(GameObject host, List<InventoryManager.RestaurantTransferSource> sources,
        Transform target, float duration, float stagger = 0.045f, bool requireVisibleTarget = false)
    {
        try
        {
            float elapsed = 0f;
            duration = Mathf.Max(0.01f, duration);
            // Keep large stacks responsive: the final item launches at most 0.18 seconds later.
            stagger = sources.Count > 1 ? Mathf.Clamp(stagger, 0f, 0.18f / (sources.Count - 1)) : 0f;
            float totalDuration = duration + stagger * Mathf.Max(0, sources.Count - 1);
            var visuals = new RectTransform[sources.Count];
            var groups = new CanvasGroup[sources.Count];
            for (int index = 0; index < sources.Count; index++)
            {
                visuals[index] = host.transform.GetChild(index) as RectTransform;
                groups[index] = visuals[index].GetComponent<CanvasGroup>();
            }
            // Fallback cleanup if the business owner is destroyed while nested coroutines are suspended.
            Object.Destroy(host, totalDuration + 1f);
            while (host != null)
            {
                bool visible = !requireVisibleTarget || (target != null && target.gameObject.activeInHierarchy);
                Vector2 destination = target != null
                    ? Game.Modules.ModuleScreenFlight.ScreenPoint(target, target.position)
                    : new Vector2(Screen.width * 0.5f, Screen.height * 0.7f);
                for (int index = 0; index < sources.Count; index++)
                {
                    float localTime = elapsed - index * stagger;
                    float t = Mathf.Clamp01(localTime / duration);
                    RectTransform visual = visuals[index];
                    visual.anchoredPosition = FlightPosition(sources[index].screenPoint, destination, t, index);
                    float lift = Mathf.Sin(Mathf.PI * Smooth01(Mathf.Clamp01(t / 0.38f)));
                    float arrival = Smooth01(Mathf.InverseLerp(0.64f, 1f, t));
                    visual.localScale = Vector3.one * ((1f + 0.1f * lift) * Mathf.Lerp(1f, 0.22f, arrival));
                    float tilt = (index % 2 == 0 ? -1f : 1f) * 11f * Mathf.Sin(Mathf.PI * Smooth01(t));
                    visual.localRotation = Quaternion.Euler(0f, 0f, tilt);
                    groups[index].alpha = visible && localTime >= 0f
                        ? 1f - Smooth01(Mathf.InverseLerp(0.82f, 1f, t)) : 0f;
                }
                if (elapsed >= totalDuration) break;
                yield return null;
                elapsed += Time.unscaledDeltaTime;
            }
        }
        finally { if (host != null) Object.Destroy(host); }
    }

    private static float Smooth01(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

    private static Vector2 FlightPosition(Vector2 start, Vector2 end, float time, int index)
    {
        Vector2 delta = end - start;
        float distance = delta.magnitude;
        float pixels = Screen.height / 1080f;
        // Small deterministic variations separate multiple ingredients without jitter each frame.
        float variation = (index % 5 - 2) * 0.08f;
        float lift = Mathf.Min(distance * 0.32f, 150f * pixels) * (1f + variation);
        Vector2 side = distance > 0.01f ? new Vector2(-delta.y, delta.x) / distance : Vector2.zero;
        float spread = Mathf.Min(distance * 0.08f, 30f * pixels) * (index % 2 == 0 ? -1f : 1f);
        Vector2 controlA = start + delta * 0.18f + Vector2.up * lift + side * spread;
        Vector2 controlB = end - delta * 0.22f + Vector2.up * (lift * 0.5f) + side * (spread * 0.3f);
        float t = Smooth01(time);
        float inverse = 1f - t;
        return inverse * inverse * inverse * start + 3f * inverse * inverse * t * controlA +
            3f * inverse * t * t * controlB + t * t * t * end;
    }

    public static IEnumerator PlayQueue(List<InventoryManager.IngredientFlySource> sources, Transform target,
        float duration, float spawnInterval)
    {
        var visuals = new List<InventoryManager.RestaurantTransferSource>(sources.Count);
        foreach (var source in sources)
            visuals.Add(new InventoryManager.RestaurantTransferSource
            {
                type = source.itemType, quantity = 1, icon = source.icon,
                screenPoint = Game.Modules.ModuleScreenFlight.ScreenPoint(source.fromUITransform, source.fromWorldPos)
            });
        GameObject host = Create(visuals, false);
        host.name = "RestaurantQueueIngredientFlight";
        try { yield return Animate(host, visuals, target, duration, Mathf.Max(0.035f, spawnInterval), true); }
        finally { if (host != null) Object.Destroy(host); }
    }
}
