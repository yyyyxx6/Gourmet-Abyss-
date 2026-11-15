using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Independent views of actual pots. Waiting orders and their cancellation stay in DishQueueSlot.</summary>
public sealed class RestaurantCookingStatusUI : MonoBehaviour
{
    [Serializable]
    public sealed class PotView
    {
        public GameObject root;
        public Image icon;
        public Image progress;
        public GameObject progressRoot;
        public Text count;
        public Text state;
    }

    public PotView[] slots = Array.Empty<PotView>();
    public float slotSpacing = 118f;
    public RectTransform queueRoot;
    public RectTransform cookingTitle;
    public RectTransform waitingTitle;
    public RectTransform cookButton;
    public Sprite waitingBackground;
    private readonly List<Pot> visiblePots = new List<Pot>();
    private RestaurantPanel restaurant;

    public void Bind(RestaurantPanel owner)
    {
        restaurant = owner;
        if (restaurant != null)
        {
            restaurant.ShowLockedQueuePreview = false;
            foreach (var slot in restaurant.allDishQueueSlots)
                if (slot != null) slot.ConfigureCookingBar(waitingBackground);
            restaurant.RefreshRestaurantUnits();
        }
        Refresh();
    }

    private void LateUpdate() { Refresh(); }

    public void Refresh()
    {
        visiblePots.Clear();
        if (restaurant != null)
            foreach (var pot in restaurant.potsList)
                if (pot != null && pot.isActiveAndEnabled && pot.IsFacilityUnlocked && !visiblePots.Contains(pot))
                    visiblePots.Add(pot);

        int potCount = Mathf.Min(visiblePots.Count, slots.Length);
        int waitingCount = restaurant != null ? restaurant.WaitingSlotCapacity : 0;
        float width = (potCount + waitingCount) * slotSpacing;
        float firstSlot = -width * .5f + slotSpacing * .5f;
        if (queueRoot != null) queueRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        if (cookingTitle != null)
        {
            cookingTitle.gameObject.SetActive(potCount > 0);
            cookingTitle.anchoredPosition = new Vector2(firstSlot + (potCount - 1) * slotSpacing * .5f, 89);
            cookingTitle.sizeDelta = new Vector2(potCount * slotSpacing, 30);
        }
        if (waitingTitle != null)
        {
            waitingTitle.anchoredPosition = new Vector2(firstSlot + (potCount + (waitingCount - 1) * .5f) * slotSpacing, 89);
            waitingTitle.sizeDelta = new Vector2(waitingCount * slotSpacing, 30);
        }
        if (cookButton != null) cookButton.anchoredPosition = new Vector2(width * .5f + 62f, 0);
        if (restaurant != null)
            for (int i = 0; i < restaurant.allDishQueueSlots.Count; i++)
                if (restaurant.allDishQueueSlots[i] != null && restaurant.allDishQueueSlots[i].transform is RectTransform rect)
                    rect.anchoredPosition = new Vector2(firstSlot + (potCount + i) * slotSpacing, 0);

        for (int i = 0; i < slots.Length; i++)
        {
            var view = slots[i];
            if (view == null || view.root == null) continue;
            bool available = i < visiblePots.Count;
            if (view.root.activeSelf != available) view.root.SetActive(available);
            if (!available) continue;
            if (view.root.transform is RectTransform rect)
                rect.anchoredPosition = new Vector2(firstSlot + i * slotSpacing, 0);
            Pot pot = visiblePots[i];
            bool occupied = pot.potState == potState.Used && pot.currentRecipe != null;
            if (view.icon != null)
            {
                view.icon.sprite = occupied ? pot.currentRecipe.dishIcon : null;
                view.icon.enabled = view.icon.sprite != null;
            }
            if (view.progress != null) view.progress.fillAmount = occupied ? pot.CookingProgress : 0f;
            if (view.progressRoot != null) view.progressRoot.SetActive(occupied && pot.isCooking);
            if (view.count != null) view.count.text = occupied ? Mathf.Max(1, pot.PendingServingCount).ToString() : "";
            if (view.state != null) view.state.text = !occupied ? "空闲" :
                pot.isCooking ? "制作中" : pot.PendingServingCount > 0 ? "待装盘" : "装盘中";
        }
    }
}
