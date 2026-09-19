using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using System.Collections;
using System.Collections.Generic;

public class InventoryItemUI : MonoBehaviour, IPointerClickHandler
{
    [Header("UI Components")]
    [SerializeField] private Image itemIcon;      // 物品图标
    [SerializeField] private Text countText;  // 物品数量

    [Header("背景（多出的预览格）")]
    [SerializeField] private Image BG;
    [Tooltip("多出来的那一格：背景用此图")]
    [SerializeField] private Sprite LockSprite;
    [Tooltip("正常格子的背景；强烈建议指定。若预制体默认 BG 已是 Lock 图，不填则解锁后可能无法恢复普通底图。")]
    [SerializeField] private Sprite normalBGSprite;

    [Header("图标设置")]
    [SerializeField] private List<ResourceIcon> resourceIcons = new List<ResourceIcon>();

    [System.Serializable]
    public class ResourceIcon
    {
        public ResourceType type;
        public Sprite icon;
    }

    // 背包格子的数据
    public class SlotData
    {
        public ResourceType itemType = ResourceType.Money;
        public int currentCount = 0;
        public int maxCapacity = 4;
        public bool isEmpty = true;
        public int slotIndex = -1;
    }

    // 当前格子的数据
    private SlotData slotData = new SlotData();
    private bool snapshotViewNeedsRefresh;

    /// <summary>多出来的那一格：仅展示 Lock 背景，不参与存物。</summary>
    private bool _isLockedPreviewSlot;
    private Sprite _cachedNormalBgSprite;

    // 事件
    public System.Action<int> OnSlotClicked; // 参数：格子索引

    private Vector3 _pulseBaseLocalScale;
    private Coroutine _pulseCoroutine;

    private void Awake()
    {
        _pulseBaseLocalScale = transform.localScale;
        RebuildNormalBackgroundCache();
        // 确保UI正确显示
        UpdateUI();
    }

    /// <summary>普通格背景缓存；避免把 Lock 图误当作「解锁后应恢复的图」。</summary>
    private void RebuildNormalBackgroundCache()
    {
        _cachedNormalBgSprite = null;
        if (normalBGSprite != null)
            _cachedNormalBgSprite = normalBGSprite;
        else if (BG != null && BG.sprite != null && (LockSprite == null || BG.sprite != LockSprite))
            _cachedNormalBgSprite = BG.sprite;
    }

    private void RestoreNormalBackgroundSprite()
    {
        if (BG == null) return;
        Sprite s = normalBGSprite != null ? normalBGSprite : _cachedNormalBgSprite;
        if (s != null)
            BG.sprite = s;
    }

    public bool IsLockedPreviewSlot() => _isLockedPreviewSlot;

    /// <summary>与烹饪队列「多一格」一致：Lock 背景，隐藏图标与数量。</summary>
    public void SetLockedPreviewSlot(bool locked)
    {
        _isLockedPreviewSlot = locked;
        if (locked)
        {
            slotData.isEmpty = true;
            slotData.currentCount = 0;
        }
        UpdateUI();
    }

    /// <summary>扣除食材等时机：格子缩放反馈。</summary>
    public void PlaySlotFeedbackPulse()
    {
        if (!isActiveAndEnabled || _isLockedPreviewSlot) return;
        if (_pulseCoroutine != null)
            StopCoroutine(_pulseCoroutine);
        _pulseCoroutine = StartCoroutine(CoSlotPulse());
    }

    private IEnumerator CoSlotPulse()
    {
        yield return UIFeedbackPulse.CoScalePulse(transform, _pulseBaseLocalScale, 1.26f, 0.32f);
        _pulseCoroutine = null;
    }

    // 初始化格子
    public void Initialize(int slotIndex, int maxCapacity = 4)
    {
        slotData.slotIndex = slotIndex;
        slotData.maxCapacity = maxCapacity;
        UpdateUI();
    }
    public void UpdateSlotCapacity(int newCapacity)
    {
        if (newCapacity < 0) return;

        slotData.maxCapacity = newCapacity;
        // 更新UI显示容量
        UpdateUI();
    }

    public bool ApplySnapshot(InventorySlotSnapshot snapshot)
    {
        if (snapshot == null || _isLockedPreviewSlot ||
            snapshot.Index != slotData.slotIndex || snapshot.Capacity != slotData.maxCapacity)
            return false;

        ApplySnapshotData(snapshot);
        RefreshSnapshotView();
        return true;
    }

    // The caller validates the complete inventory before applying any slot data.
    internal void ApplySnapshotData(InventorySlotSnapshot snapshot)
    {
        bool changed = slotData.itemType != snapshot.ItemType ||
            slotData.currentCount != snapshot.Count || slotData.isEmpty != snapshot.IsEmpty;
        slotData.itemType = snapshot.ItemType;
        slotData.currentCount = snapshot.Count;
        slotData.isEmpty = snapshot.IsEmpty;
        snapshotViewNeedsRefresh |= changed;
    }

    internal void RefreshSnapshotView()
    {
        if (!snapshotViewNeedsRefresh) return;
        UpdateUI();
        snapshotViewNeedsRefresh = false;
    }

    // 添加物品到格子
    public bool AddItem(ResourceType itemType, int amount, out int addedAmount)
    {
        addedAmount = 0;
        if (_isLockedPreviewSlot) return false;

        // 如果格子为空，设置物品类型
        if (slotData.isEmpty)
        {
            slotData.itemType = itemType;
            slotData.isEmpty = false;

            int canAdd = Mathf.Min(amount, slotData.maxCapacity);
            slotData.currentCount = canAdd;
            addedAmount = canAdd;

            UpdateUI();
            return true;
        }
        // 如果格子已经有物品，检查是否是同类型
        else if (slotData.itemType == itemType)
        {
            // 如果已经满了，不能添加
            if (IsFull())
            {
                addedAmount = 0;
                return false;
            }

            int canAdd = Mathf.Min(amount, slotData.maxCapacity - slotData.currentCount);
            slotData.currentCount += canAdd;
            addedAmount = canAdd;

            UpdateUI();
            return true;
        }
        // 类型不匹配，不能添加
        else
        {
            addedAmount = 0;
            return false;
        }
    }

    // 从格子移除物品
    public bool RemoveItem(int amount, out int actualRemoved)
    {
        actualRemoved = 0;
        if (_isLockedPreviewSlot) return false;

        if (slotData.isEmpty || slotData.currentCount <= 0)
            return false;

        actualRemoved = Mathf.Min(amount, slotData.currentCount);
        slotData.currentCount -= actualRemoved;

        // 如果数量为0，清空格子
        if (slotData.currentCount <= 0)
        {
            ClearSlot();
        }
        else
        {
            UpdateUI();
        }

        return actualRemoved > 0;
    }

    // 清空格子
    public void ClearSlot()
    {
        slotData.currentCount = 0;
        slotData.isEmpty = true;
        UpdateUI();
    }

    // 更新UI显示
    private void UpdateUI()
    {
        if (_isLockedPreviewSlot)
        {
            if (BG != null)
            {
                BG.enabled = true;
                if (LockSprite != null)
                    BG.sprite = LockSprite;
            }
            if (itemIcon != null)
                itemIcon.gameObject.SetActive(false);
            if (countText != null)
                countText.gameObject.SetActive(false);
            return;
        }

        RestoreNormalBackgroundSprite();

        if (slotData.isEmpty)
        {
            // 清空时隐藏图标和文本
            if (itemIcon != null)
            {
                itemIcon.gameObject.SetActive(false);
            }

            if (countText != null)
            {
                countText.gameObject.SetActive(false);
            }
        }
        else
        {
            // 有物品时显示图标和文本
            if (itemIcon != null)
            {
                itemIcon.gameObject.SetActive(true);

                // 获取对应资源的图标
                Sprite iconSprite = GetIconForType(slotData.itemType);
                if (iconSprite != null)
                {
                    itemIcon.sprite = iconSprite;
                    itemIcon.color = Color.white; // 确保颜色正常
                }
                else
                {
                    // 如果没有设置图标，使用颜色作为后备
                    itemIcon.color = GetColorForType(slotData.itemType);
                }
            }

            if (countText != null)
            {
                countText.gameObject.SetActive(true);
                countText.text = slotData.currentCount.ToString();

                // 如果达到上限，显示为红色
                if (IsFull())
                {
                    countText.color = Color.red;
                }
                else
                {
                    countText.color = Color.white;
                }
            }
        }
    }

    // 获取物品类型对应的图标
    private Sprite GetIconForType(ResourceType type)
    {
        foreach (var resourceIcon in resourceIcons)
        {
            if (resourceIcon.type == type)
            {
                return resourceIcon.icon;
            }
        }
        return null;
    }

    // 获取物品类型对应的颜色（备用）
    private Color GetColorForType(ResourceType type)
    {
        return type switch
        {
            ResourceType.Money => Color.yellow,
            ResourceType.watermelonJ => Color.red,
            ResourceType.orangeJ => new Color(1f, 0.5f, 0f), // 橙色
            ResourceType.tomatoJ => Color.red,
            ResourceType.mushroom => Color.white,
            _ => Color.white
        };
    }

    // 获取格子的数据
    public SlotData GetSlotData()
    {
        return slotData;
    }

    // 检查格子是否已满
    public bool IsFull()
    {
        return slotData.currentCount >= slotData.maxCapacity;
    }

    // 检查格子是否为空
    public bool IsEmpty()
    {
        return slotData.isEmpty || slotData.currentCount <= 0;
    }

    // 获取物品类型
    public ResourceType GetItemType()
    {
        return slotData.itemType;
    }

    // 获取当前数量
    public int GetCurrentCount()
    {
        return slotData.currentCount;
    }

    // 获取最大容量
    public int GetMaxCapacity()
    {
        return slotData.maxCapacity;
    }

    // 获取剩余容量
    public int GetRemainingCapacity()
    {
        return slotData.maxCapacity - slotData.currentCount;
    }

    // 点击事件处理
    public void OnPointerClick(PointerEventData eventData)
    {
        if (_isLockedPreviewSlot) return;
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            OnSlotClicked?.Invoke(slotData.slotIndex);
        }
    }
}
