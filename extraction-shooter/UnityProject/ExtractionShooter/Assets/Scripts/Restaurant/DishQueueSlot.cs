using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 餐厅烹饪排队 UI 单槽：背景、菜肴图标、数量文本。激活数量由 WeaponStatsManager 控制；
/// 只有列表中第一个槽位上的菜会下锅，其余槽为缓冲；队首煮完或取走后整列左移。
/// </summary>
public class DishQueueSlot : MonoBehaviour
{
    private Vector3 _pulseBaseLocalScale;
    private Coroutine _pulseCoroutine;

    private void Awake()
    {
        _pulseBaseLocalScale = transform.localScale;
        CacheNormalBackgroundIfNeeded();
    }

    /// <summary>入队或队首下锅时的槽位缩放反馈。</summary>
    public void PlayQueueSlotPulse()
    {
        if (!isActiveAndEnabled || _isLockedPreviewSlot) return;
        if (_pulseCoroutine != null)
            StopCoroutine(_pulseCoroutine);
        _pulseCoroutine = StartCoroutine(CoQueuePulse());
    }

    private IEnumerator CoQueuePulse()
    {
        yield return UIFeedbackPulse.CoScalePulse(transform, _pulseBaseLocalScale, 1.26f, 0.32f);
        _pulseCoroutine = null;
    }

    [Header("槽位 UI")]
    [SerializeField] private Image slotBackground;
    [SerializeField] private Image dishIcon;
    [SerializeField] private Sprite lookIcon;
    [Tooltip("解锁后恢复的背景图；为空则用本组件 Awake 时 slotBackground 的 sprite")]
    [SerializeField] private Sprite normalSlotBackgroundSprite;
    [SerializeField] private Text itemCountText;
    [SerializeField] private bool transparentNormalBackground;

    /// <summary>多出来的扩容预览格：仅展示锁/Look 图，不参与排队数据。</summary>
    private bool _isLockedPreviewSlot;
    private Sprite _cachedNormalBgSprite;

    private void CacheNormalBackgroundIfNeeded()
    {
        if (_cachedNormalBgSprite != null) return;
        if (normalSlotBackgroundSprite != null)
            _cachedNormalBgSprite = normalSlotBackgroundSprite;
        else if (slotBackground != null)
            _cachedNormalBgSprite = slotBackground.sprite;
    }

    public bool IsLockedPreviewSlot() => _isLockedPreviewSlot;

    /// <summary>由 <see cref="RestaurantPanel"/> 在槽位数变化时统一刷新：仅下标 == active 且存在多出一格时为 true。</summary>
    public void SetLockedPreviewSlot(bool locked)
    {
        CacheNormalBackgroundIfNeeded();
        _isLockedPreviewSlot = locked;
        if (transparentNormalBackground && slotBackground != null)
            slotBackground.color = locked ? Color.white : Color.clear;
        if (locked)
        {
            if (slotBackground != null)
            {
                slotBackground.enabled = true;
                if (lookIcon != null)
                    slotBackground.sprite = lookIcon;
            }

            if (dishIcon != null)
                dishIcon.enabled = false;

            if (itemCountText != null)
            {
                itemCountText.enabled = false;
                itemCountText.text = string.Empty;
            }
        }
        else
        {
            if (slotBackground != null && _cachedNormalBgSprite != null)
                slotBackground.sprite = _cachedNormalBgSprite;
        }
    }

    public void SetVisual(DishRecipe recipe, int count)
    {
        if (_isLockedPreviewSlot) return;

        bool has = recipe != null && count > 0;

        if (slotBackground != null)
            slotBackground.enabled = true;

        if (dishIcon != null)
        {
            dishIcon.enabled = has && recipe.dishIcon != null;
            if (has && recipe.dishIcon != null)
                dishIcon.sprite = recipe.dishIcon;
        }

        if (itemCountText != null)
        {
            itemCountText.enabled = true;
            itemCountText.text = has ? count.ToString() : string.Empty;
        }
    }

    public void SetEmpty()
    {
        if (_isLockedPreviewSlot) return;
        SetVisual(null, 0);
    }

    /// <summary>等同 <see cref="SetLockedPreviewSlot"/>(true)，保留旧调用。</summary>
    public void SetLookEmpty()
    {
        SetLockedPreviewSlot(true);
    }

    /// <summary>食材飞行入队的落点：优先菜图标位置。</summary>
    public Vector3 GetQueueFlyTargetWorldPosition()
    {
        if (dishIcon != null)
            return dishIcon.transform.position;
        return transform.position;
    }
}
