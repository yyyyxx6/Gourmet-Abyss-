using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>A cancel press belongs to a batch, even if queue compaction rebinds this slot before release.</summary>
public sealed class DishQueueCancelButton : Button
{
    private RestaurantPanel owner;
    private long batchId;
    private RestaurantPanel pressedOwner;
    private long pressedBatchId;
    private bool pointerArmed;
    private float lastCancellation = float.NegativeInfinity;

    public long BatchId => batchId;

    public void Bind(RestaurantPanel restaurant, long waitingBatchId)
    {
        owner = restaurant;
        batchId = waitingBatchId;
        bool available = owner != null && batchId > 0;
        interactable = available;
        if (gameObject.activeSelf != available) gameObject.SetActive(available);
    }

    public override void OnPointerDown(PointerEventData eventData)
    {
        base.OnPointerDown(eventData);
        pointerArmed = eventData.button == PointerEventData.InputButton.Left && IsActive() && IsInteractable();
        if (!pointerArmed) return;
        pressedOwner = owner;
        pressedBatchId = batchId;
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
        bool armed = pointerArmed;
        pointerArmed = false;
        if (!armed || eventData.button != PointerEventData.InputButton.Left || eventData.clickCount > 1) return;
        Cancel(pressedOwner, pressedBatchId);
    }

    public override void OnSubmit(BaseEventData eventData) => Cancel(owner, batchId);

    private void Cancel(RestaurantPanel target, long requestedBatchId)
    {
        if (!IsActive() || !IsInteractable() || target == null || Time.unscaledTime - lastCancellation < .3f) return;
        if (target.TryCancelWaitingBatch(requestedBatchId)) lastCancellation = Time.unscaledTime;
    }

    protected override void OnDisable()
    {
        pointerArmed = false;
        pressedOwner = null;
        base.OnDisable();
    }
}
