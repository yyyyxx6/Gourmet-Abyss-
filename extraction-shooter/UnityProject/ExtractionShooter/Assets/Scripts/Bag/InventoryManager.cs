using Game.Core;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using UnityEngine.UI;

public class InventoryManager : MonoSingleton<InventoryManager>
{
    // 每个场景各一份，后加载的接管——沿用原来的裸赋值语义。
    protected override DuplicatePolicy Duplicate => DuplicatePolicy.OverwriteReference;

    /// <summary>兼容旧调用点的别名，等价于 Instance。</summary>
    public static InventoryManager instance => Instance;

    [Header("背包设置")]
    [SerializeField] private Transform gridParent; // Grid Layout Group的父物体
    [SerializeField] private GameObject slotPrefab; // 格子预制体
    [SerializeField] private int fixedSlotCount = 4; // 固定格子数量，不会增加
    [SerializeField] private int slotCapacity = 4; // 每个格子的容量
    [SerializeField] private GameObject inventoryFullObject; // 背包满时激活的物体

    [Header("回地面食材飞行动画")]
    [SerializeField] private GameObject transferToRestaurantFlyPrefab; // 飞行预制体（SpriteRenderer 或 Image）
    [SerializeField] private Transform transferFlyParent; // 可选：飞行特效父节点
    [SerializeField] private RectTransform transferUIRoot; // 若预制体是UI，使用该根节点坐标系
    [SerializeField] private Camera transferUICamera; // Screen Space Camera 模式可指定
    [SerializeField] private Transform transferTarget; // 飞行终点（优先使用）
    [SerializeField] private float transferFlyDuration = 0.4f;
    [SerializeField] private float transferFlyArcHeight = 45f;
    [SerializeField] private float transferSpawnInterval = 0.04f;

    [Header("背包槽位入场动效")]
    [SerializeField] private bool playEntranceOnStart = true;
    [SerializeField] private float slotEntranceDelayStep = 0.045f;
    [SerializeField] private float slotEntranceDuration = 0.42f;
    [SerializeField] private float slotEntranceFromYOffset = -95f;
    [SerializeField] private float slotEntranceOverhead = 42f;
    [SerializeField] private float slotEntranceStartScale = 0.84f;
    [SerializeField] private float slotLandingWobbleDuration = 0.28f;
    [SerializeField] private float slotLandingWobbleScale = 1.18f;

    // 格子列表（含多出的 1 格锁定预览；真正可用数见 usableSlotCount）
    private List<InventoryItemUI> slots = new List<InventoryItemUI>();

    // The legacy field name is retained; only gathered resources use this no-slot store.
    private readonly RunIngredientStore runIngredients = new RunIngredientStore();
    private bool isCommittingRunGathered;

    /// <summary>Resource type, previous count, current count.</summary>
    public event Action<ResourceType, int, int> OnRunGatheredChanged
    {
        add => runIngredients.Changed += value;
        remove => runIngredients.Changed -= value;
    }
    public event Action<ResourceType, int, int> OnRunIngredientChanged
    {
        add => OnRunGatheredChanged += value;
        remove => OnRunGatheredChanged -= value;
    }
    [Header("局内采集物调试")]
    [SerializeField] private bool showRunIngredientDebugUI = false;
    /// <summary>与 WeaponStatsManager.inventorySlotCount 一致，不含多出来的预览格。</summary>
    private int usableSlotCount;
    private Coroutine slotEntranceCoroutine;
    private readonly List<SlotEntranceAnimation> activeSlotEntrances = new List<SlotEntranceAnimation>();

    private sealed class SlotEntranceAnimation
    {
        public RectTransform Rect;
        public Vector2 Position;
        public Vector3 Scale;
        public CanvasGroup Group;
        public Coroutine Routine;
    }

    private struct TransferFlyRequest
    {
        public ResourceType itemType;
        public Sprite icon;
        public int count;
        public Vector3 fromWorldPos;
    }

    public struct IngredientFlySource
    {
        public ResourceType itemType;
        public Sprite icon;
        public int slotIndex;
        public Vector3 fromWorldPos;
        public Transform fromUITransform;
    }

    protected override void OnAwake()
    {
        InitializeInventory();
    }

    // 初始化背包
    private void InitializeInventory()
    {
        if (gridParent == null)
        {
            Debug.LogError("InventoryManager: gridParent 未设置！");
            return;
        }

        if (slotPrefab == null)
        {
            Debug.LogError("InventoryManager: slotPrefab 未设置！");
            return;
        }

        if (WeaponStatsManager.Instance != null)
        {
            fixedSlotCount = WeaponStatsManager.Instance.inventorySlotCount;
            slotCapacity = WeaponStatsManager.Instance.inventorySlotCapacity;
        }
        fixedSlotCount = Mathf.Max(0, fixedSlotCount);
        slotCapacity = Mathf.Max(0, slotCapacity);
        usableSlotCount = fixedSlotCount;

        // 清除现有格子
        ClearExistingSlots();

        // 可用格 + 多 1 格锁定预览（与烹饪队列一致）
        int totalUiSlots = Mathf.Max(1, usableSlotCount + 1);
        for (int i = 0; i < totalUiSlots; i++)
        {
            CreateNewSlot(i);
        }
        SyncAllSlotIndices();
        RefreshAllSlotLockStates();

        // 初始化背包满物体状态
        UpdateInventoryFullState();
    }

    private void Start()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // 调试浮层，正式包不创建（它会常驻 DontDestroyOnLoad 且 sortingOrder 极高）。
        if (showRunIngredientDebugUI)
            RunIngredientDebugUI.EnsureExists(this);
#endif

        // 订阅背包数值变化事件
        if (WeaponStatsManager.Instance != null)
            WeaponStatsManager.Instance.OnInventoryStatsChanged += OnInventoryStatsUpdated;
        
        // 确保初始状态正确
        UpdateInventoryFullState();

        // 测试代码
        //AddItem(ResourceType.LootEggSmall, 4);
        // AddItem(ResourceType.LootEggBig, 4);
        // AddItem(ResourceType.LootMushroom, 4);
        // AddItem(ResourceType.LootPumkin, 4);
        OnInventoryStatsUpdated();

        if (playEntranceOnStart)
        {
            PlaySlotsEntranceAnimation();
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        CancelSlotEntranceAnimations();
        // 取消订阅事件
        if (WeaponStatsManager.Instance != null)
        {
            WeaponStatsManager.Instance.OnInventoryStatsChanged -= OnInventoryStatsUpdated;
        }
    }

    // 当背包数值更新时的回调
    private void OnInventoryStatsUpdated()
    {
        CancelSlotEntranceAnimations();
        if (WeaponStatsManager.Instance == null || gridParent == null || slotPrefab == null) return;
        int newSlotCount = Mathf.Max(0, WeaponStatsManager.Instance.inventorySlotCount);
        int newSlotCapacity = Mathf.Max(0, WeaponStatsManager.Instance.inventorySlotCapacity);

        Debug.Log($"背包数值更新: 可用格={newSlotCount}, 新容量={newSlotCapacity}, 当前UI格数={slots.Count}, 当前容量={slotCapacity}");

        // 先保存新值
        fixedSlotCount = newSlotCount;
        usableSlotCount = newSlotCount;
        slotCapacity = newSlotCapacity;

        // 如果格子数量变化，调整格子数量（可用 + 1 预览）
        int targetTotalUi = Mathf.Max(1, newSlotCount + 1);
        if (targetTotalUi != slots.Count)
        {
            AdjustSlotCount(newSlotCount, newSlotCapacity);
        }

        // 必须先刷新锁定格，再 UpdateSlotCapacities；否则仍带锁标记的格子会先 UpdateUI，锁图可能无法被正确换回普通底图
        RefreshAllSlotLockStates();
        UpdateSlotCapacities(newSlotCapacity);

        // 更新背包满状态
        UpdateInventoryFullState();
    }
    /// <param name="targetUsableCount">可用格数量（不含多出来的锁定预览格）。</param>
    private void AdjustSlotCount(int targetUsableCount, int newCapacity)
    {
        usableSlotCount = targetUsableCount;
        int targetTotalUi = Mathf.Max(1, targetUsableCount + 1);

        if (targetTotalUi > slots.Count)
        {
            int slotsToAdd = targetTotalUi - slots.Count;
            for (int i = 0; i < slotsToAdd; i++)
            {
                int slotIndex = slots.Count;
                CreateNewSlot(slotIndex, newCapacity);
            }
            Debug.Log($"增加了 {slotsToAdd} 个背包UI格，可用={targetUsableCount}，容量={newCapacity}");
        }
        else if (targetTotalUi < slots.Count)
        {
            int slotsToRemove = slots.Count - targetTotalUi;
            for (int i = 0; i < slotsToRemove; i++)
            {
                int lastIndex = slots.Count - 1;
                if (slots[lastIndex] != null && slots[lastIndex].gameObject != null)
                {
                    Destroy(slots[lastIndex].gameObject);
                }
                slots.RemoveAt(lastIndex);
            }
            Debug.Log($"移除了 {slotsToRemove} 个背包UI格");
        }

        SyncAllSlotIndices();
        RefreshAllSlotLockStates();
    }

    /// <summary>与列表顺序对齐格子下标（扩容/删格后保证 OnSlotClicked 等索引正确）。</summary>
    private void SyncAllSlotIndices()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] == null) continue;
            slots[i].Initialize(i, slotCapacity);
        }
    }

    /// <summary>仅最后一格为「锁定预览」：可用格数变化时先清掉旧锁，再在列表末尾重新上锁。</summary>
    private void RefreshAllSlotLockStates()
    {
        int expectedTotal = Mathf.Max(1, usableSlotCount + 1);
        if (slots.Count != expectedTotal)
        {
            Debug.LogWarning(
                $"InventoryManager: UI 槽数 {slots.Count} 与预期 usable+1={expectedTotal} 不一致，锁定格可能异常。请检查 AdjustSlotCount / 初始化流程。",
                this);
        }

        int lastIndex = slots.Count - 1;
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i] == null) continue;
            bool locked = slots.Count == usableSlotCount + 1 && i == lastIndex;
            slots[i].SetLockedPreviewSlot(locked);
        }
    }

    // 更新背包满状态
    private void UpdateInventoryFullState()
    {
        if (inventoryFullObject == null)
        {
            Debug.LogWarning("InventoryManager: 未设置背包满物体");
            return;
        }
        
        bool isFull = IsInventoryFull();
        inventoryFullObject.SetActive(isFull);
    }
    
    // 检查背包是否已满
    public bool IsInventoryFull()
    {
        if (slots.Count == 0) return false;
        
        // 检查所有可用格子是否都满了（不含锁定预览格）
        for (int i = 0; i < slots.Count && i < usableSlotCount; i++)
        {
            InventoryItemUI slot = slots[i];
            if (slot == null) continue;

            if (slot.IsEmpty() || !slot.IsFull())
                return false;
        }
        
        return true; // 所有格子都满了
    }

    // 更新所有现有格子的容量
    private void UpdateSlotCapacities(int newCapacity)
    {
        for (int i = 0; i < slots.Count && i < usableSlotCount; i++)
        {
            if (slots[i] != null)
                slots[i].UpdateSlotCapacity(newCapacity);
        }
        
        // 容量变化可能影响背包满状态
        UpdateInventoryFullState();
    }

    // 增加新格子（不销毁已有的）
    private void AddNewSlots(int targetSlotCount)
    {
        int slotsToAdd = targetSlotCount - slots.Count;

        for (int i = 0; i < slotsToAdd; i++)
        {
            int slotIndex = slots.Count; // 新格子的索引
            CreateNewSlot(slotIndex);
        }

        Debug.Log($"增加了 {slotsToAdd} 个新格子，现在总格子数: {slots.Count}");
        
        // 新格子增加后更新背包状态
        UpdateInventoryFullState();
    }

    // 清除现有格子
    private void ClearExistingSlots()
    {
        foreach (Transform child in gridParent)
        {
            Destroy(child.gameObject);
        }
        slots.Clear();
    }

    /// <summary>可用槽位数（不含多出来的锁定预览格）。</summary>
    public int GetSlotCount()
    {
        return usableSlotCount;
    }

    // 重新整理背包（将物品向前移动填补空位）
    public void ReorganizeInventory()
    {
        List<(ResourceType itemType, int itemCount)> items = new List<(ResourceType, int)>();

        // 首先收集所有非空可用槽位的物品信息
        for (int i = 0; i < slots.Count && i < usableSlotCount; i++)
        {
            if (slots[i] == null) continue;

            if (!slots[i].IsEmpty())
            {
                ResourceType itemType = slots[i].GetItemType();
                int itemCount = slots[i].GetCurrentCount();
                items.Add((itemType, itemCount));
            }
        }

        // 清空所有可用槽位
        for (int i = 0; i < slots.Count && i < usableSlotCount; i++)
        {
            if (slots[i] != null)
                slots[i].ClearSlot();
        }

        // 重新填充物品（保持原有顺序）
        for (int i = 0; i < items.Count && i < usableSlotCount; i++)
        {
            slots[i].AddItem(items[i].itemType, items[i].itemCount, out int added);
        }
        
        // 重新整理后更新背包满状态
        UpdateInventoryFullState();
    }

    // 修改 CreateNewSlot 方法，明确传入容量
    private InventoryItemUI CreateNewSlot(int slotIndex, int capacity = -1)
    {
        GameObject slotObj = Instantiate(slotPrefab, gridParent);
        slotObj.name = $"InventorySlot_{slotIndex}";

        InventoryItemUI slotUI = slotObj.GetComponent<InventoryItemUI>();
        if (slotUI != null)
        {
            int actualCapacity = capacity >= 0 ? capacity : slotCapacity;
            Debug.Log($"创建格子 {slotIndex}，容量={actualCapacity}");
            slotUI.Initialize(slotIndex, actualCapacity);
        }
        else
        {
            Debug.LogError($"格子预制体缺少 InventoryItemUI 组件: {slotPrefab.name}");
        }

        slots.Add(slotUI);
        return slotUI;
    }

    public void PlaySlotsEntranceAnimation()
    {
        if (!isActiveAndEnabled || slots == null || slots.Count == 0) return;
        CancelSlotEntranceAnimations();
        slotEntranceCoroutine = StartCoroutine(CoPlaySlotsEntranceAnimation());
    }

    private void CancelSlotEntranceAnimations()
    {
        if (slotEntranceCoroutine != null)
            StopCoroutine(slotEntranceCoroutine);
        slotEntranceCoroutine = null;
        foreach (SlotEntranceAnimation animation in activeSlotEntrances)
        {
            if (animation.Routine != null) StopCoroutine(animation.Routine);
            if (animation.Rect != null)
            {
                animation.Rect.anchoredPosition = animation.Position;
                animation.Rect.localScale = animation.Scale;
            }
            if (animation.Group != null) animation.Group.alpha = 1f;
        }
        activeSlotEntrances.Clear();
    }

    private IEnumerator CoPlaySlotsEntranceAnimation()
    {
        yield return null; // 等待一帧，确保 GridLayout 已经完成布局

        float delayStep = Mathf.Max(0f, slotEntranceDelayStep);
        for (int i = 0; i < slots.Count; i++)
        {
            InventoryItemUI slot = slots[i];
            if (slot == null) continue;
            RectTransform rect = slot.transform as RectTransform;
            if (rect == null) continue;
            CanvasGroup group = rect.GetComponent<CanvasGroup>();
            if (group == null) group = rect.gameObject.AddComponent<CanvasGroup>();
            var animation = new SlotEntranceAnimation
            {
                Rect = rect,
                Position = rect.anchoredPosition,
                Scale = rect.localScale,
                Group = group
            };
            activeSlotEntrances.Add(animation);
            animation.Routine = StartCoroutine(CoAnimateSingleSlotEntrance(animation));
            if (delayStep > 0f)
                yield return new WaitForSeconds(delayStep);
        }

        slotEntranceCoroutine = null;
    }

    private IEnumerator CoAnimateSingleSlotEntrance(SlotEntranceAnimation animation)
    {
        RectTransform slotRect = animation.Rect;
        if (slotRect == null) yield break;

        Vector2 endPos = animation.Position;
        Vector2 startPos = endPos + new Vector2(0f, slotEntranceFromYOffset);
        Vector3 baseScale = animation.Scale;
        CanvasGroup cg = animation.Group;

        float duration = Mathf.Max(0.05f, slotEntranceDuration);
        float overhead = Mathf.Max(0f, slotEntranceOverhead);
        float startScaleMul = Mathf.Clamp(slotEntranceStartScale, 0.5f, 1.2f);
        float elapsed = 0f;
        cg.alpha = 0f;
        slotRect.anchoredPosition = startPos;
        slotRect.localScale = baseScale * startScaleMul;

        while (elapsed < duration)
        {
            if (slotRect == null || cg == null)
            {
                activeSlotEntrances.Remove(animation);
                yield break;
            }
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f); // 丝滑减速
            Vector2 pos = Vector2.LerpUnclamped(startPos, endPos, eased);
            pos.y += 4f * t * (1f - t) * overhead; // overhead 抛物线
            slotRect.anchoredPosition = pos;
            slotRect.localScale = Vector3.LerpUnclamped(baseScale * startScaleMul, baseScale, eased);
            cg.alpha = Mathf.LerpUnclamped(0f, 1f, eased);
            yield return null;
        }

        if (slotRect == null || cg == null)
        {
            activeSlotEntrances.Remove(animation);
            yield break;
        }
        slotRect.anchoredPosition = endPos;
        slotRect.localScale = baseScale;
        cg.alpha = 1f;

        float wobbleDuration = Mathf.Max(0.01f, slotLandingWobbleDuration);
        float wobblePeak = Mathf.Max(1f, slotLandingWobbleScale);
        float wobbleElapsed = 0f;
        while (wobbleElapsed < wobbleDuration)
        {
            if (slotRect == null)
            {
                activeSlotEntrances.Remove(animation);
                yield break;
            }
            wobbleElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(wobbleElapsed / wobbleDuration);
            float dampedWave = Mathf.Sin(t * Mathf.PI * 2.2f) * (1f - t);
            float mul = 1f + (wobblePeak - 1f) * dampedWave;
            slotRect.localScale = baseScale * mul;
            yield return null;
        }
        if (slotRect != null) slotRect.localScale = baseScale;
        activeSlotEntrances.Remove(animation);
    }

    public InventorySnapshot CaptureInventory()
    {
        var captured = new List<InventorySlotSnapshot>();
        for (int index = 0; index < usableSlotCount; index++)
        {
            InventoryItemUI slot = GetSlot(index);
            if (slot == null || slot.IsLockedPreviewSlot())
                throw new InvalidOperationException("An unlocked inventory slot is missing or locked.");
            bool empty = slot.IsEmpty();
            captured.Add(new InventorySlotSnapshot(index,
                empty ? ResourceType.None : slot.GetItemType(),
                empty ? 0 : slot.GetCurrentCount(), slot.GetMaxCapacity()));
        }
        return new InventorySnapshot(captured);
    }

    public InventorySnapshot CaptureIngredientInventory()
    {
        InventorySnapshot captured = CaptureInventory();
        var ingredientSlots = new List<InventorySlotSnapshot>();
        foreach (InventorySlotSnapshot slot in captured.Slots)
        {
            ingredientSlots.Add(ResourceStorageRules.IsIngredient(slot.ItemType)
                ? slot
                : new InventorySlotSnapshot(slot.Index, ResourceType.None, 0, slot.Capacity));
        }
        return new InventorySnapshot(ingredientSlots);
    }

    public bool ApplyInventorySnapshot(InventorySnapshot snapshot)
    {
        if (snapshot == null || snapshot.Slots.Count != usableSlotCount) return false;

        // Validate the complete layout before any live slot is changed.
        foreach (InventorySlotSnapshot captured in snapshot.Slots)
        {
            InventoryItemUI slot = GetSlot(captured.Index);
            if (slot == null || slot.IsLockedPreviewSlot() ||
                slot.GetSlotData().slotIndex != captured.Index ||
                slot.GetMaxCapacity() != captured.Capacity)
                return false;
        }

        foreach (InventorySlotSnapshot captured in snapshot.Slots)
            slots[captured.Index].ApplySnapshotData(captured);

        // A broken display must not leave half an inventory applied or replay a resource transfer.
        foreach (InventorySlotSnapshot captured in snapshot.Slots)
        {
            try
            {
                slots[captured.Index].RefreshSnapshotView();
            }
            catch (Exception error)
            {
                Debug.LogException(error, this);
            }
        }
        try
        {
            UpdateInventoryFullState();
        }
        catch (Exception error)
        {
            Debug.LogException(error, this);
        }
        return true;
    }

    public InventoryPackResult AddItems(
        IReadOnlyDictionary<ResourceType, int> requested,
        Func<ResourceType, int> priority = null)
    {
        if (requested == null) throw new ArgumentNullException(nameof(requested));
        foreach (ResourceType type in requested.Keys)
        {
            if (!ResourceStorageRules.UsesSlots(type))
                throw new ArgumentException($"Resource {type} does not use inventory slots.", nameof(requested));
        }

        InventorySnapshot before = CaptureInventory();
        InventoryPackResult result = InventoryPacking.Add(before, requested, priority);
        if (!ApplyInventorySnapshot(result.Snapshot))
            throw new InvalidOperationException("The inventory layout changed while applying a packing result.");

        for (int index = 0; index < before.Slots.Count; index++)
        {
            if (result.Snapshot.Slots[index].Count > before.Slots[index].Count)
                slots[before.Slots[index].Index].PlaySlotFeedbackPulse();
        }
        return result;
    }

    public int AddItemPartial(ResourceType itemType, int amount)
    {
        if (amount <= 0 || !ResourceStorageRules.UsesSlots(itemType))
        {
            Debug.LogWarning($"Cannot add resource to slot inventory: {itemType} {amount}");
            return 0;
        }

        int requestedAmount = Math.Min(amount, GetAvailableCapacity(itemType));
        if (requestedAmount == 0) return 0;
        InventoryPackResult result = AddItems(new Dictionary<ResourceType, int> { { itemType, requestedAmount } });
        int accepted;
        return result.Accepted.TryGetValue(itemType, out accepted) ? accepted : 0;
    }

    // The legacy bool indicates complete acceptance; a partial addition is still retained.
    public bool AddItem(ResourceType itemType, int amount)
    {
        return amount > 0 && AddItemPartial(itemType, amount) == amount;
    }

    public int GetAvailableCapacity(ResourceType itemType)
    {
        if (!ResourceStorageRules.UsesSlots(itemType)) return 0;
        long capacity = 0;
        long currentCount = 0;
        for (int index = 0; index < usableSlotCount && index < slots.Count; index++)
        {
            InventoryItemUI slot = slots[index];
            if (slot == null || slot.IsLockedPreviewSlot()) continue;
            if (slot.IsEmpty())
                capacity += Math.Max(0, slot.GetMaxCapacity());
            else if (slot.GetItemType() == itemType)
            {
                currentCount += slot.GetCurrentCount();
                capacity += Math.Max(0, slot.GetRemainingCapacity());
            }
        }
        return (int)Math.Max(0L, Math.Min(capacity, int.MaxValue - currentCount));
    }

    public bool CanAddItem(ResourceType itemType, int amount)
    {
        return amount > 0 && GetAvailableCapacity(itemType) >= amount;
    }

    // 获取指定类型物品的总数量
    public int GetItemCount(ResourceType itemType)
    {
        return slots
            .Select((slot, idx) => (slot, idx))
            .Where(t => t.idx < usableSlotCount && t.slot != null && !t.slot.IsEmpty() && t.slot.GetItemType() == itemType)
            .Sum(t => t.slot.GetCurrentCount());
    }

    public int AddRunGathered(ResourceType itemType, int amount)
    {
        if (amount <= 0 || !ResourceStorageRules.IsGathered(itemType))
        {
            Debug.LogWarning($"Cannot add resource to the gathered-resource store: {itemType} {amount}");
            return 0;
        }

        int oldCount = runIngredients.GetCount(itemType);
        runIngredients.Add(itemType, amount);
        int accepted = runIngredients.GetCount(itemType) - oldCount;
        if (accepted > 0)
            RunSessionManager.Instance?.RecordGathered(itemType, accepted);
        return accepted;
    }

    public int GetRunGatheredCount(ResourceType itemType)
    {
        return ResourceStorageRules.IsGathered(itemType) ? runIngredients.GetCount(itemType) : 0;
    }

    public Dictionary<ResourceType, int> GetRunGatheredCounts()
    {
        return runIngredients.GetSnapshot();
    }

    public void ClearRunGathered()
    {
        runIngredients.Clear();
    }

    /// <summary>Applies a death allocation without recording retained items as newly gathered.</summary>
    public void ReplaceRunGathered(IReadOnlyDictionary<ResourceType, int> counts)
    {
        Dictionary<ResourceType, int> previous = ReplaceRunGatheredSilently(counts);
        NotifyRunGatheredChanges(previous);
    }

    public Dictionary<ResourceType, int> ReplaceRunGatheredSilently(IReadOnlyDictionary<ResourceType, int> counts)
    {
        if (counts == null) throw new ArgumentNullException(nameof(counts));
        var validated = new Dictionary<ResourceType, int>();
        foreach (KeyValuePair<ResourceType, int> entry in counts)
        {
            if (!ResourceStorageRules.IsGathered(entry.Key) || entry.Value < 0)
                throw new ArgumentException("Invalid retained gathered-resource counts.", nameof(counts));
            if (entry.Value > 0) validated.Add(entry.Key, entry.Value);
        }
        return runIngredients.SilentReplace(validated);
    }

    public void NotifyRunGatheredChanges(IReadOnlyDictionary<ResourceType, int> previous)
    {
        try
        {
            foreach (Exception error in runIngredients.Notify(previous))
                Debug.LogException(error, this);
        }
        catch (Exception error)
        {
            Debug.LogException(error, this);
        }
    }

    public void CommitRunGatheredToPermanent()
    {
        if (isCommittingRunGathered || runIngredients.Count == 0) return;
        GameValManager gameValues = GameValManager.Instance;
        if (gameValues == null)
        {
            Debug.LogWarning("Cannot commit gathered resources: GameValManager is missing.");
            return;
        }

        isCommittingRunGathered = true;
        try
        {
            foreach (KeyValuePair<ResourceType, int> entry in GetRunGatheredCounts())
            {
                int before = gameValues.GetResourceCount(entry.Key);
                ResourceItem resource = gameValues.GetResourceInfo(entry.Key);
                long available = resource == null ? 0L : Math.Max(0L, (long)resource.maxCapacity - before);
                int transferAmount = (int)Math.Min(entry.Value, available);
                if (transferAmount > 0)
                {
                    gameValues.AddResource(entry.Key, transferAmount);
                    int accepted = (int)Math.Max(0L, Math.Min(transferAmount,
                        (long)gameValues.GetResourceCount(entry.Key) - before));
                    runIngredients.Remove(entry.Key, accepted);
                }

                int remaining = runIngredients.GetCount(entry.Key);
                if (remaining > 0)
                    Debug.LogWarning($"Permanent resource storage is full; retained {remaining} gathered {entry.Key} for a later commit.");
            }
        }
        finally
        {
            isCommittingRunGathered = false;
        }
    }

    // Compatibility wrappers preserve call sites while enforcing the current storage rules.
    public bool AddRunIngredient(ResourceType itemType, int amount)
    {
        if (ResourceStorageRules.IsIngredient(itemType)) return AddItem(itemType, amount);
        return amount > 0 && AddRunGathered(itemType, amount) == amount;
    }

    public int GetRunIngredientCount(ResourceType itemType)
    {
        return GetRunGatheredCount(itemType);
    }

    public Dictionary<ResourceType, int> GetAllRunIngredientCounts()
    {
        return GetRunGatheredCounts();
    }

    public void ClearRunIngredients()
    {
        ClearRunGathered();
    }

    public void TransferRunIngredientsToGameValAndClear()
    {
        CommitRunGatheredToPermanent();
    }

    // 获取指定索引的格子
    public InventoryItemUI GetSlot(int index)
    {
        if (index >= 0 && index < usableSlotCount && index < slots.Count)
            return slots[index];
        return null;
    }

    // 清空背包内后百分之多少的物体
    public void ClearBackpackByPercentage(float percentage, bool reorganizeAfter = true)
    {
        if (slots.Count == 0)
        {
            Debug.LogWarning("背包为空，无法清空");
            return;
        }

        // 确保百分比在0-1之间
        percentage = Mathf.Clamp01(percentage);

        if (percentage <= 0f)
        {
            Debug.Log("清空百分比为0，不执行任何操作");
            return;
        }

        // 计算需要清空的后百分之多少可用格子
        int slotsToClear = Mathf.CeilToInt(usableSlotCount * percentage);

        // 从后往前清空指定数量的可用格子（跳过锁定预览格）
        int clearedSlots = 0;
        for (int i = usableSlotCount - 1; i >= 0 && clearedSlots < slotsToClear; i--)
        {
            if (slots[i] != null && !slots[i].IsEmpty())
            {
                slots[i].ClearSlot();
                clearedSlots++;
            }
        }

        Debug.Log($"已清空背包后 {percentage * 100}% 的物品，清空了 {clearedSlots} 个格子");

        // 如果需要，在清空后重新整理背包
        if (reorganizeAfter)
        {
            ReorganizeInventory();
        }
        
        // 清空后更新背包满状态
        UpdateInventoryFullState();
    }

    // 清空背包内后指定数量的格子
    public void ClearBackpackBySlotCount(int slotCount, bool reorganizeAfter = false)
    {
        if (slotCount <= 0)
        {
            Debug.LogWarning("清空格子数量必须大于0");
            return;
        }

        if (slots.Count == 0)
        {
            Debug.LogWarning("背包为空，无法清空");
            return;
        }

        // 确保不超过可用格子数
        slotCount = Mathf.Min(slotCount, usableSlotCount);

        // 从后往前清空指定数量的可用格子
        int clearedSlots = 0;
        for (int i = usableSlotCount - 1; i >= 0 && clearedSlots < slotCount; i--)
        {
            if (slots[i] != null && !slots[i].IsEmpty())
            {
                slots[i].ClearSlot();
                clearedSlots++;
            }
        }

        Debug.Log($"已清空背包后 {slotCount} 个格子，实际清空了 {clearedSlots} 个格子");

        // 如果需要，在清空后重新整理背包
        if (reorganizeAfter)
        {
            ReorganizeInventory();
        }
        
        // 清空后更新背包满状态
        UpdateInventoryFullState();
    }

    // 清空背包内所有物品
    public void ClearAllItems(bool reorganizeAfter = false)
    {
        int clearedSlots = 0;

        for (int i = 0; i < slots.Count && i < usableSlotCount; i++)
        {
            if (slots[i] != null && !slots[i].IsEmpty())
            {
                slots[i].ClearSlot();
                clearedSlots++;
            }
        }

        Debug.Log($"已清空所有物品，清空了 {clearedSlots} 个格子");

        // 如果需要，在清空后重新整理背包
        if (reorganizeAfter)
        {
            ReorganizeInventory();
        }
        
        // 清空后更新背包满状态
        UpdateInventoryFullState();
    }

    /// <summary>
    /// 按菜谱从背包扣除所需食材；事前总量不足则整单失败、不改变背包。
    /// </summary>
    public bool TryConsumeIngredientsForRecipe(DishRecipe recipe)
    {
        if (recipe == null || recipe.ingredients == null) return false;

        foreach (DishIngredient ing in recipe.ingredients)
        {
            if (ing.requiredCount <= 0) continue;
            if (GetItemCount(ing.resourceType) < ing.requiredCount)
                return false;
        }

        foreach (DishIngredient ing in recipe.ingredients)
        {
            if (ing.requiredCount <= 0) continue;

            int remaining = ing.requiredCount;
            for (int i = 0; i < slots.Count && i < usableSlotCount && remaining > 0; i++)
            {
                if (slots[i] == null || slots[i].IsEmpty()) continue;
                if (slots[i].GetItemType() != ing.resourceType) continue;

                int take = Mathf.Min(remaining, slots[i].GetCurrentCount());
                if (take <= 0) continue;

                slots[i].RemoveItem(take, out int removed);
                remaining -= removed;
                if (removed > 0)
                    slots[i].PlaySlotFeedbackPulse();
            }

            if (remaining != 0)
            {
                Debug.LogError("TryConsumeIngredientsForRecipe: 扣除与预判不一致");
                ReorganizeInventory();
                UpdateInventoryFullState();
                return false;
            }
        }

        ReorganizeInventory();
        UpdateInventoryFullState();
        return true;
    }

    /// <summary>
    /// 将背包中所有道具按种类和数量加到 GameValManager，然后清空背包。
    /// （保留供特殊流程手工调用；从关卡回地面不再自动执行。）
    /// </summary>
    public void TransferAllToGameValAndClear()
    {
        if (GameValManager.Instance == null) return;

        // 按种类汇总数量
        Dictionary<ResourceType, int> typeToCount = new Dictionary<ResourceType, int>();
        List<TransferFlyRequest> flyRequests = new List<TransferFlyRequest>();

        for (int i = 0; i < slots.Count && i < usableSlotCount; i++)
        {
            if (slots[i] == null || slots[i].IsEmpty()) continue;

            ResourceType itemType = slots[i].GetItemType();
            int count = slots[i].GetCurrentCount();
            if (typeToCount.ContainsKey(itemType))
                typeToCount[itemType] += count;
            else
                typeToCount[itemType] = count;

            Sprite icon = GetResourceIcon(itemType);
            flyRequests.Add(new TransferFlyRequest
            {
                itemType = itemType,
                icon = icon,
                count = Mathf.Max(0, count),
                fromWorldPos = slots[i].transform.position
            });
        }

        // 加到资源管理器
        foreach (var kvp in typeToCount)
        {
            if (kvp.Value > 0)
                GameValManager.Instance.AddResource(kvp.Key, kvp.Value);
        }

        // 清空背包
        ClearAllItems(false);

        // 播放飞行动画表现（背包Item -> 商店位置）
        if (flyRequests.Count > 0)
        {
            StartCoroutine(PlayTransferToRestaurantEffects(flyRequests));
        }
    }

    private Sprite GetResourceIcon(ResourceType type)
    {
        if (GameValManager.Instance == null) return null;
        ResourceItem info = GameValManager.Instance.GetResourceInfo(type);
        return info != null ? info.Icon : null;
    }

    /// <summary>
    /// 仅收集食材飞行起点，不扣除背包道具。每份需求会生成一个来源点。
    /// </summary>
    public bool TryBuildIngredientFlySourcesForRecipe(DishRecipe recipe, out List<IngredientFlySource> sources)
    {
        sources = new List<IngredientFlySource>();
        if (recipe == null || recipe.ingredients == null) return false;

        foreach (DishIngredient ing in recipe.ingredients)
        {
            if (ing == null || ing.requiredCount <= 0) continue;
            int remain = ing.requiredCount;
            Sprite icon = GetResourceIcon(ing.resourceType);

            for (int i = 0; i < slots.Count && i < usableSlotCount && remain > 0; i++)
            {
                InventoryItemUI slot = slots[i];
                if (slot == null || slot.IsEmpty()) continue;
                if (slot.GetItemType() != ing.resourceType) continue;

                int provide = Mathf.Min(remain, slot.GetCurrentCount());
                for (int k = 0; k < provide; k++)
                {
                    sources.Add(new IngredientFlySource
                    {
                        itemType = ing.resourceType,
                        icon = icon,
                        slotIndex = i,
                        fromUITransform = slot.transform,
                        fromWorldPos = slot.transform.position
                    });
                }

                remain -= provide;
            }

            if (remain > 0)
            {
                sources.Clear();
                return false;
            }
        }

        return true;
    }

    private IEnumerator PlayTransferToRestaurantEffects(List<TransferFlyRequest> requests)
    {
        if (transferToRestaurantFlyPrefab == null)
        {
            yield break;
        }

        Transform targetTransform = transferTarget;
        if (targetTransform == null && ShopManager.Instance != null)
        {
            targetTransform = ShopManager.Instance.transform;
        }
        if (targetTransform == null) yield break;

        for (int i = 0; i < requests.Count; i++)
        {
            TransferFlyRequest req = requests[i];
            int spawnCount = Mathf.Max(1, req.count);

            for (int j = 0; j < spawnCount; j++)
            {
                PlaySingleTransferEffect(req.icon, req.fromWorldPos, targetTransform.position);
                float interval = Mathf.Max(0f, transferSpawnInterval);
                if (interval > 0f) yield return new WaitForSeconds(interval);
            }
        }
    }

    private void PlaySingleTransferEffect(Sprite icon, Vector3 fromWorldPos, Vector3 toWorldPos)
    {
        GameObject flyObj = Instantiate(
            transferToRestaurantFlyPrefab,
            transferFlyParent != null ? transferFlyParent : null
        );
        if (flyObj == null) return;

        SpriteRenderer sr = flyObj.GetComponentInChildren<SpriteRenderer>(true);
        if (sr != null && icon != null)
        {
            sr.sprite = icon;
        }

        Image img = flyObj.GetComponentInChildren<Image>(true);
        if (img != null && icon != null)
        {
            img.sprite = icon;
        }

        RectTransform flyRect = flyObj.GetComponent<RectTransform>();
        bool isUIFly = flyRect != null && transferUIRoot != null;
        if (isUIFly)
        {
            flyRect.SetParent(transferUIRoot, false);
            Vector2 startAnchored = WorldToAnchoredPosition(transferUIRoot, fromWorldPos);
            Vector2 endAnchored = WorldToAnchoredPosition(transferUIRoot, toWorldPos);
            StartCoroutine(FlyUIRoutine(flyObj, flyRect, startAnchored, endAnchored));
        }
        else
        {
            StartCoroutine(FlyWorldRoutine(flyObj, fromWorldPos, toWorldPos));
        }
    }

    private IEnumerator FlyWorldRoutine(GameObject flyObj, Vector3 startPos, Vector3 endPos)
    {
        float duration = Mathf.Max(0.01f, transferFlyDuration);
        float arc = transferFlyArcHeight;
        float elapsed = 0f;

        flyObj.transform.position = startPos;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            Vector3 pos = Vector3.Lerp(startPos, endPos, t);
            pos.y += 4f * t * (1f - t) * arc;
            flyObj.transform.position = pos;
            yield return null;
        }

        flyObj.transform.position = endPos;
        Destroy(flyObj);
    }

    private IEnumerator FlyUIRoutine(GameObject flyObj, RectTransform flyRect, Vector2 startPos, Vector2 endPos)
    {
        float duration = Mathf.Max(0.01f, transferFlyDuration);
        float arc = transferFlyArcHeight;
        float elapsed = 0f;

        flyRect.anchoredPosition = startPos;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            Vector2 pos = Vector2.Lerp(startPos, endPos, t);
            pos.y += 4f * t * (1f - t) * arc;
            flyRect.anchoredPosition = pos;
            yield return null;
        }

        flyRect.anchoredPosition = endPos;
        Destroy(flyObj);
    }

    private Vector2 WorldToAnchoredPosition(RectTransform root, Vector3 worldPos)
    {
        Camera cam = transferUICamera;
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, worldPos);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPos, cam, out Vector2 localPos);
        return localPos;
    }
}
