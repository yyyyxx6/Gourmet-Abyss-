using System.Collections;
using System.Collections.Generic;
using Game.Core;
using UnityEngine;
using UnityEngine.UI;
public class RestaurantPanel : MonoSingleton<RestaurantPanel>
{
    // 每个场景各一份，后加载的接管——沿用原来的裸赋值语义。
    protected override DuplicatePolicy Duplicate => DuplicatePolicy.OverwriteReference;

    /// <summary>兼容旧调用点的别名，等价于 Instance。</summary>
    public static RestaurantPanel instance => Instance;

    public IQueueIngredientPresentation QueueIngredientPresentation { get; set; }

    [Header("食材背包UI配置")]
    public GameObject foodItemPrefabs;
    public Transform foodItemParent;
    public Text foodInformationTitle;
    public Text foodInformationDescription;

    [Header("菜单UI配置")]
    public GameObject dishItemPrefabs;       // 菜的预制体
    public GameObject dishFoodItemPrefabs;   // 菜里显示食材的预制体
    public Transform dishParent;             // 菜列表的父物体
    [Tooltip("菜肴列表 ScrollRect（可选）；用于滚轮选中后自动滚到可见区域")]
    public ScrollRect dishMenuScrollRect;

    [Header("进度条UI")]
    [Tooltip("烹饪进度条的根物体（整体容器），用于显示/隐藏整块UI")]
    public GameObject cookingProgressRoot;
    [Tooltip("显示当前锅的烹饪进度（Filled Image，Fill Amount 0-1）")]
    public Image cookingProgressImage;
    [Tooltip("已废弃：餐厅改为顾客端菜售卖，不再使用碟子自动售卖进度条")]
    public GameObject plateSellProgressRoot;
    [Tooltip("已废弃")]
    public Image plateSellProgressImage;

    [Header("选中菜单食材指示")]
    [Tooltip("左侧食材指示根物体；1 种食材时仅显示左侧，右侧整侧隐藏")]
    public GameObject leftIngredientIndicatorRoot;
    public Image leftIngredientIcon;
    public Text leftIngredientCountText;
    [Tooltip("右侧食材指示根物体；2 种食材时左右各一种")]
    public GameObject rightIngredientIndicatorRoot;
    public Image rightIngredientIcon;
    public Text rightIngredientCountText;

    [Header("选中食材指示反馈（Scale 波动）")]
    [SerializeField] private bool enableIngredientIndicatorScalePulse = true;
    [SerializeField] private float ingredientIndicatorPulseScaleMultiplier = 1.12f;
    [SerializeField] private float ingredientIndicatorPulseDuration = 0.18f;
    [SerializeField] private float ingredientIndicatorPulseDelay = 0f;

    private Coroutine _leftIndicatorPulseCoroutine;
    private Coroutine _rightIndicatorPulseCoroutine;
    private Coroutine _ingredientIndicatorDelayedPulseCoroutine;
    private Vector3 _leftIndicatorBaseScale = Vector3.one;
    private Vector3 _rightIndicatorBaseScale = Vector3.one;

    [Header("菜单数据")]
    public List<DishRecipe> dishRecipes = new List<DishRecipe>();

    private List<GameObject> currentFoodItems = new List<GameObject>();
    private List<GameObject> currentDishes = new List<GameObject>();
    private readonly List<dishItemPrefabs> _dishItemWidgets = new List<dishItemPrefabs>();
    private int _selectedDishIndex = -1;
    [Header("锅数据")]
    public List<Pot> potsList = new List<Pot>(); //餐厅有的锅
    [Header("锅数据（全部候选）")]
    public List<Pot> allPots = new List<Pot>();
    [Header("食材实例效果")]
    public GameObject ingredientInstancePrefab;  // 食材实例预制体
    public Transform ingredientSpawnParent;      // 生成父物体
    [Header("背包 -> 排队槽 飞行动画")]
    [SerializeField] private GameObject queueIngredientFlyPrefab;
    [SerializeField] private Transform queueIngredientFlyParent;
    [SerializeField] private RectTransform queueIngredientFlyUIRoot;
    [SerializeField] private Camera queueIngredientFlyUICamera;
    [SerializeField] private float queueIngredientFlyDuration = 0.32f;
    [Tooltip("两段式轨迹：先相对起点沿正上方抬升的距离（anchored Y+）；0 则用下方 Arc 作为默认抬升量")]
    [SerializeField] private float queueIngredientFlyLiftHeight;
    [Tooltip("总时长中用于「先向上」阶段的占比，其余为飞向队列槽")]
    [SerializeField] [Range(0.08f, 0.75f)] private float queueIngredientFlyLiftPhaseRatio = 0.38f;
    [SerializeField] private float queueIngredientFlyArcHeight = 46f;
    [SerializeField] private float queueIngredientSpawnInterval = 0.03f;
    [SerializeField] private float queueIngredientLandingDuration = 0.14f;
    [SerializeField] private float queueIngredientLandingScaleUp = 1.18f;
    [Tooltip("飞行路径随机横向/纵向浮动最大幅度（anchored 像素）；0 关闭")]
    [SerializeField] private float queueIngredientFlyWobbleAmplitude = 14f;
    [Tooltip("扰动主频率随机范围（Hz 量级，越大抖动越密）")]
    [SerializeField] private float queueIngredientFlyWobbleFreqMin = 2.2f;
    [SerializeField] private float queueIngredientFlyWobbleFreqMax = 5.8f;
    [Header("菜碟配置")]
    public List<Plate> platesList = new List<Plate>(); //餐厅有的菜碟
    [Header("菜碟配置（全部候选）")]
    public List<Plate> allPlates = new List<Plate>();
    [Header("烹饪排队槽（UI，顺序即队列下标 0=队首）")]
    [Tooltip("队首（下标 0）优先下锅；有空闲且类型匹配的锅会从 potsList 中取锅并行烹饪；队首空则整列左移递补。")]
    public List<DishQueueSlot> allDishQueueSlots = new List<DishQueueSlot>();
    [Tooltip("单槽最大叠堆份数；若已存在 WeaponStatsManager 则优先使用其 slotCapacity")]
    [SerializeField] private int cookQueueMaxPerSlotFallback = 20;

    private class CookQueueStackEntry
    {
        public DishRecipe recipe;
        public int count;
        public bool IsEmpty => recipe == null || count <= 0;
        public void Clear() { recipe = null; count = 0; }
    }

    private readonly List<CookQueueStackEntry> _cookQueueData = new List<CookQueueStackEntry>();
    private int _lastCookQueueActiveCount = -1;

    /// <summary>判断是否同一道菜（用于装盘唯一性、叠堆判定）。</summary>
    public static bool RecipesMatch(DishRecipe a, DishRecipe b)
    {
        if (a == null || b == null) return false;
        if (a.dishID != b.dishID) return false;
        return a.dishName == b.dishName && Mathf.Approximately(a.baseDishPrice, b.baseDishPrice);
    }

    /// <summary>锅烹饪进度：0-1；由 Pot 在 CookingProcess 中调用。</summary>
    public void SetCookingProgress(float t)
    {
        if (cookingProgressImage == null) return;
        float v = Mathf.Clamp01(t);
        cookingProgressImage.fillAmount = v;
        if (cookingProgressRoot != null)
            cookingProgressRoot.SetActive(v > 0f);
        else
            cookingProgressImage.gameObject.SetActive(v > 0f);
    }

    /// <summary>已废弃：碟子定时自动售卖已移除，进度条常隐藏。</summary>
    public void SetPlateSellProgress(float t)
    {
        HidePlateSellProgress();
    }

    private void HidePlateSellProgress()
    {
        if (plateSellProgressRoot != null)
            plateSellProgressRoot.SetActive(false);
        else if (plateSellProgressImage != null)
            plateSellProgressImage.gameObject.SetActive(false);
    }

    protected override void OnAwake()
    {
        if (allPots.Count == 0 && potsList.Count > 0)
        {
            allPots.AddRange(potsList);
        }

        if (allPlates.Count == 0 && platesList.Count > 0)
        {
            allPlates.AddRange(platesList);
        }

        // 不在这里强制同步：避免 WeaponStatsManager 加载顺序导致 Instance 为空
        EnsureCookQueueDataSize();

        // 初始进度状态：无烹饪 / 无售卖 → 进度条隐藏
        SetCookingProgress(0f);
        SetPlateSellProgress(0f);
        HideIngredientSideIndicators();

        // 缓存一个稳定的“基准缩放”，后续脉冲动画每次都从这个值开始/结束，避免叠加越变越大
        if (leftIngredientIndicatorRoot != null)
            _leftIndicatorBaseScale = leftIngredientIndicatorRoot.transform.localScale;
        if (rightIngredientIndicatorRoot != null)
            _rightIndicatorBaseScale = rightIngredientIndicatorRoot.transform.localScale;
    }

    private void OnEnable()
    {
        if (WeaponStatsManager.Instance != null)
        {
            WeaponStatsManager.Instance.OnRestaurantStatsChanged -= SyncRestaurantUnitsFromStats;
            WeaponStatsManager.Instance.OnRestaurantStatsChanged += SyncRestaurantUnitsFromStats;
        }

        if (ShopSlotManager.Instance != null)
        {
            ShopSlotManager.Instance.OnRestaurantPlateSlotsChanged -= SyncRestaurantUnitsFromStats;
            ShopSlotManager.Instance.OnRestaurantPlateSlotsChanged += SyncRestaurantUnitsFromStats;
        }

        // 无论菜单UI能否立即生成，都先确保锅/盘数量同步（隐藏多余对象）
        StartCoroutine(WaitAndSyncRestaurantUnits());

        RefreshOnOpen();

        // 每次打开面板时，同步一次进度条状态（大多数情况下此时没有在烹饪/售卖）
        SetCookingProgress(0f);
        SetPlateSellProgress(0f);
    }

    /// <summary>
    /// UI 打开/启用时刷新入口（可由外部脚本调用）。同一帧内会自动去重，避免重复生成导致滚动/选中被冲掉。
    /// </summary>
    public void RefreshOnOpen()
    {
        if (!isActiveAndEnabled) return;
        if (_lastRefreshOnOpenFrame == Time.frameCount) return;
        _lastRefreshOnOpenFrame = Time.frameCount;

        if (GameValManager.Instance == null || foodItemParent == null) return;
        GenerateFoodItems();
        GenerateDishList();
        StartForceDishScrollToLeftOnOpen();
        StartCoroutine(CoEnsureDefaultDishSelectedHard());
    }

    private IEnumerator CoEnsureDefaultDishSelected()
    {
        // 等一帧：让 Instantiate/Layout/动画初始化结束，避免选中效果被后续刷新覆盖
        yield return null;
        if (!isActiveAndEnabled) yield break;
        if (_dishItemWidgets == null || _dishItemWidgets.Count == 0) yield break;
        if (_selectedDishIndex >= 0 && _selectedDishIndex < _dishItemWidgets.Count) yield break;
        SelectDishItemByIndex(0, false);
    }

    private IEnumerator CoEnsureDefaultDishSelectedHard()
    {
        // 连续两帧兜底（父物体动画/布局重算可能在下一帧发生）
        yield return null;
        yield return null;

        if (!isActiveAndEnabled) yield break;
        if (_dishItemWidgets == null || _dishItemWidgets.Count == 0) yield break;
        SelectDishItemByIndex(0, false);
    }

    private void OnDisable()
    {
        if (WeaponStatsManager.Instance != null)
        {
            WeaponStatsManager.Instance.OnRestaurantStatsChanged -= SyncRestaurantUnitsFromStats;
        }

        if (ShopSlotManager.Instance != null)
        {
            ShopSlotManager.Instance.OnRestaurantPlateSlotsChanged -= SyncRestaurantUnitsFromStats;
        }

        if (_leftIndicatorPulseCoroutine != null)
        {
            StopCoroutine(_leftIndicatorPulseCoroutine);
            _leftIndicatorPulseCoroutine = null;
        }
        if (_rightIndicatorPulseCoroutine != null)
        {
            StopCoroutine(_rightIndicatorPulseCoroutine);
            _rightIndicatorPulseCoroutine = null;
        }
        if (_ingredientIndicatorDelayedPulseCoroutine != null)
        {
            StopCoroutine(_ingredientIndicatorDelayedPulseCoroutine);
            _ingredientIndicatorDelayedPulseCoroutine = null;
        }
    }

    private IEnumerator WaitAndSyncRestaurantUnits()
    {
        // 等待 WeaponStatsManager 单例创建完成
        float timeout = 3f;
        while (WeaponStatsManager.Instance == null && timeout > 0f)
        {
            timeout -= Time.deltaTime;
            yield return null;
        }

        if (WeaponStatsManager.Instance != null)
        {
            // 确保无论加载顺序如何，都能订阅到实时变更事件
            WeaponStatsManager.Instance.OnRestaurantStatsChanged -= SyncRestaurantUnitsFromStats;
            WeaponStatsManager.Instance.OnRestaurantStatsChanged += SyncRestaurantUnitsFromStats;
        }

        if (ShopSlotManager.Instance != null)
        {
            ShopSlotManager.Instance.OnRestaurantPlateSlotsChanged -= SyncRestaurantUnitsFromStats;
            ShopSlotManager.Instance.OnRestaurantPlateSlotsChanged += SyncRestaurantUnitsFromStats;
        }

        SyncRestaurantUnitsFromStats();
    }
    /// <summary>
    /// 每次面板显示时刷新：根据 GameValManager 重新生成食材种类与数量、菜肴列表。
    /// </summary>
    
    /// <summary>设施解锁等外部变更后，刷新锅/碟可用列表。</summary>
    public void RefreshRestaurantUnits()
    {
        SyncRestaurantUnitsFromStats();
    }

    private void SyncRestaurantUnitsFromStats()
    {
        if (WeaponStatsManager.Instance == null)
        {
            return;
        }

        EnsureAllUnitsPopulated();
        SyncPotsByCount(WeaponStatsManager.Instance.restaurantPotCount);
        SyncPlatesByCount(GetTargetPlateSlotCount());
        SyncCookQueueSlotsFromStats();
    }

    /// <summary>餐碟数量：优先 ShopSlotManager，否则 WeaponStatsManager.restaurantPlateCount。</summary>
    private int GetTargetPlateSlotCount()
    {
        if (allPlates == null || allPlates.Count == 0) return 0;
        if (ShopSlotManager.Instance != null)
            return Mathf.Clamp(ShopSlotManager.Instance.restaurantPlateSlotCount, 1, allPlates.Count);
        return Mathf.Clamp(WeaponStatsManager.Instance.restaurantPlateCount, 1, allPlates.Count);
    }

    private int GetActiveCookQueueSlotCount()
    {
        if (allDishQueueSlots == null || allDishQueueSlots.Count == 0) return 0;
        if (WeaponStatsManager.Instance == null)
            return Mathf.Min(allDishQueueSlots.Count, 4);
        return Mathf.Clamp(WeaponStatsManager.Instance.restaurantDishQueueSlotCount, 1, allDishQueueSlots.Count);
    }

    private int GetCookQueueMaxPerSlot()
    {
        if (WeaponStatsManager.Instance != null)
            return Mathf.Max(1, WeaponStatsManager.Instance.slotCapacity);
        return Mathf.Max(1, cookQueueMaxPerSlotFallback);
    }

    private void EnsureCookQueueDataSize()
    {
        if (allDishQueueSlots == null) return;
        while (_cookQueueData.Count < allDishQueueSlots.Count)
            _cookQueueData.Add(new CookQueueStackEntry());
    }

    private void SyncCookQueueSlotsFromStats()
    {
        if (allDishQueueSlots == null || allDishQueueSlots.Count == 0)
            return;

        EnsureCookQueueDataSize();
        int newActive = GetActiveCookQueueSlotCount();

        if (_lastCookQueueActiveCount < 0)
            _lastCookQueueActiveCount = newActive;

        if (newActive < _lastCookQueueActiveCount)
            ShrinkCookQueueActiveRegion(_lastCookQueueActiveCount, newActive);

        _lastCookQueueActiveCount = newActive;

        // active 区域照常点亮；如果 UI 列表比 active 多显示一个，
        // 则额外保留 i == newActive 的那个 slot 可见（由 RefreshCookQueueUI 决定显示 LookIcon 或空）。
        for (int i = 0; i < allDishQueueSlots.Count; i++)
        {
            DishQueueSlot slot = allDishQueueSlots[i];
            if (slot == null) continue;

            bool showExtraLookEmpty = (i == newActive) && allDishQueueSlots.Count > newActive;
            bool on = (i < newActive) || showExtraLookEmpty;

            if (slot.gameObject.activeSelf != on)
                slot.gameObject.SetActive(on);
        }

        RefreshCookQueueUI();
    }

    private void ShrinkCookQueueActiveRegion(int oldActive, int newActive)
    {
        List<(DishRecipe r, int c)> order = new List<(DishRecipe, int)>();
        for (int i = 0; i < oldActive && i < _cookQueueData.Count; i++)
        {
            CookQueueStackEntry e = _cookQueueData[i];
            if (!e.IsEmpty) order.Add((e.recipe, e.count));
        }

        for (int i = 0; i < _cookQueueData.Count; i++)
            _cookQueueData[i].Clear();

        if (order.Count > newActive)
            Debug.LogWarning($"[餐厅] 烹饪排队槽减至 {newActive}，将丢失末尾 {order.Count - newActive} 条队列。");

        for (int i = 0; i < order.Count && i < newActive; i++)
        {
            _cookQueueData[i].recipe = order[i].r;
            _cookQueueData[i].count = order[i].c;
        }
    }

    private void RefreshCookQueueUI()
    {
        if (allDishQueueSlots == null) return;
        EnsureCookQueueDataSize();
        int active = GetActiveCookQueueSlotCount();
        int slotListCount = allDishQueueSlots.Count;

        for (int i = 0; i < slotListCount; i++)
        {
            DishQueueSlot ui = allDishQueueSlots[i];
            if (ui == null) continue;

            // 与背包一致：多出的最后一格为锁定/预览；active 增加后先解锁旧格，再把锁移到新的「第 active 个」下标
            bool isLockPreview = active < slotListCount && i == active;

            if (isLockPreview)
            {
                ui.SetLockedPreviewSlot(true);
                continue;
            }

            ui.SetLockedPreviewSlot(false);

            if (i > active)
            {
                ui.SetEmpty();
                continue;
            }

            if (i >= _cookQueueData.Count)
            {
                ui.SetEmpty();
                continue;
            }

            CookQueueStackEntry e = _cookQueueData[i];
            ui.SetVisual(e.recipe, e.count);
        }
    }

    /// <summary>点击菜谱：入队（扣食材），再由空闲锅自动开始烹饪。</summary>
    public bool TryEnqueueDishForCooking(DishRecipe recipe)
    {
        if (recipe == null) return false;
        EnsureCookQueueDataSize();
        if (!CheckIngredientsAvailableForRecipe(recipe))
        {
            Debug.Log("食材不足，无法加入烹饪队列：" + recipe.dishName);
            return false;
        }

        int idx = FindSuitableCookQueueSlotIndex(recipe);
        if (idx < 0)
        {
            Debug.Log("烹饪排队已满：" + recipe.dishName);
            return false;
        }

        if (InventoryManager.instance == null)
            return false;

        if (!InventoryManager.instance.TryBuildIngredientFlySourcesForRecipe(recipe, out List<InventoryManager.IngredientFlySource> flySources))
            return false;

        StartCoroutine(CoEnqueueDishAfterIngredientFly(recipe, flySources));
        return true;
    }

    private IEnumerator CoEnqueueDishAfterIngredientFly(DishRecipe recipe, List<InventoryManager.IngredientFlySource> flySources)
    {
        if (recipe == null) yield break;

        int idx = FindSuitableCookQueueSlotIndex(recipe);
        if (idx < 0) yield break;

        // 关键：在生成食材飞出表现（即开始飞行）之前就先扣除背包数量，
        // 这样“数量减少”的时刻与飞出物生成的时刻一致，而不是落点/飞行动画结束后才减少。
        if (!ConsumeIngredientsForRecipe(recipe))
            yield break;

        yield return StartCoroutine(PlayIngredientFlyToQueueCoroutine(flySources, idx));

        idx = FindSuitableCookQueueSlotIndex(recipe);
        if (idx < 0)
        {
            RefundIngredientsForRecipe(recipe);
            if (GameValManager.Instance != null && foodItemParent != null)
                GenerateFoodItems();
            yield break;
        }

        if (!TryCommitRecipeToCookQueueSlot(idx, recipe))
        {
            RefundIngredientsForRecipe(recipe);
            if (GameValManager.Instance != null && foodItemParent != null)
                GenerateFoodItems();
            yield break;
        }

        RefreshCookQueueUI();
        PulseCookQueueSlotIfValid(idx);
        TryDispatchQueueToPots();
        StartCoroutine(CoRefreshMenuUiDeferred());
    }

    private void PulseCookQueueSlotIfValid(int index)
    {
        if (allDishQueueSlots == null || index < 0 || index >= allDishQueueSlots.Count) return;
        DishQueueSlot slot = allDishQueueSlots[index];
        if (slot != null)
            slot.PlayQueueSlotPulse();
    }

    private IEnumerator CoRefreshMenuUiDeferred()
    {
        yield return null;
        if (GameValManager.Instance != null && foodItemParent != null)
            GenerateFoodItems();
        if (dishParent != null)
            GenerateDishList();
    }

    private int FindSuitableCookQueueSlotIndex(DishRecipe recipe)
    {
        int active = GetActiveCookQueueSlotCount();
        int maxPer = GetCookQueueMaxPerSlot();
        int best = -1;
        int bestRemaining = int.MaxValue;

        for (int i = 0; i < active && i < _cookQueueData.Count; i++)
        {
            CookQueueStackEntry e = _cookQueueData[i];
            if (e.IsEmpty) continue;
            if (!RecipesMatch(e.recipe, recipe)) continue;
            int remaining = maxPer - e.count;
            if (remaining > 0 && remaining < bestRemaining)
            {
                bestRemaining = remaining;
                best = i;
            }
        }

        if (best >= 0) return best;

        for (int i = 0; i < active && i < _cookQueueData.Count; i++)
        {
            if (_cookQueueData[i].IsEmpty)
                return i;
        }

        return -1;
    }

    private bool CheckIngredientsAvailableForRecipe(DishRecipe recipe)
    {
        if (recipe == null || recipe.ingredients == null || InventoryManager.instance == null)
            return false;

        foreach (DishIngredient ingredient in recipe.ingredients)
        {
            if (ingredient.requiredCount <= 0) continue;
            if (InventoryManager.instance.GetItemCount(ingredient.resourceType) < ingredient.requiredCount)
                return false;
        }
        return true;
    }

    private bool ConsumeIngredientsForRecipe(DishRecipe recipe)
    {
        if (InventoryManager.instance == null || recipe == null) return false;
        return InventoryManager.instance.TryConsumeIngredientsForRecipe(recipe);
    }

    private void RefundIngredientsForRecipe(DishRecipe recipe)
    {
        if (InventoryManager.instance == null || recipe == null || recipe.ingredients == null)
            return;

        for (int i = 0; i < recipe.ingredients.Count; i++)
        {
            DishIngredient ing = recipe.ingredients[i];
            if (ing == null || ing.requiredCount <= 0) continue;
            InventoryManager.instance.AddItem(ing.resourceType, ing.requiredCount);
        }
    }

    private bool TryCommitRecipeToCookQueueSlot(int idx, DishRecipe recipe)
    {
        if (recipe == null || idx < 0 || idx >= _cookQueueData.Count)
            return false;

        int maxPer = GetCookQueueMaxPerSlot();
        CookQueueStackEntry e = _cookQueueData[idx];
        if (e.IsEmpty)
        {
            e.recipe = recipe;
            e.count = 1;
            return true;
        }

        if (!RecipesMatch(e.recipe, recipe) || e.count >= maxPer)
            return false;

        e.count++;
        return true;
    }

    private IEnumerator PlayIngredientFlyToQueueCoroutine(List<InventoryManager.IngredientFlySource> flySources, int queueIndex)
    {
        if (flySources == null || flySources.Count == 0)
            yield break;

        if (allDishQueueSlots == null || queueIndex < 0 || queueIndex >= allDishQueueSlots.Count)
            yield break;
        DishQueueSlot queueSlot = allDishQueueSlots[queueIndex];
        if (queueSlot == null) yield break;

        var presentation = QueueIngredientPresentation;
        if (presentation != null && presentation.CanPresentQueueIngredients)
        {
            yield return presentation.PlayQueueIngredients(flySources, queueSlot,
                queueIngredientFlyDuration, queueIngredientSpawnInterval, queueIngredientLandingDuration);
            yield break;
        }

        Vector3 targetWorldPos = queueSlot.GetQueueFlyTargetWorldPosition();
        float spawnInterval = Mathf.Max(0f, queueIngredientSpawnInterval);

        for (int i = 0; i < flySources.Count; i++)
        {
            StartCoroutine(PlaySingleIngredientFlyToQueueCoroutine(flySources[i], targetWorldPos));
            if (spawnInterval > 0f)
                yield return new WaitForSeconds(spawnInterval);
        }

        float wait = Mathf.Max(0.01f, queueIngredientFlyDuration + queueIngredientLandingDuration + 0.02f);
        yield return new WaitForSeconds(wait);
    }

    private IEnumerator PlaySingleIngredientFlyToQueueCoroutine(InventoryManager.IngredientFlySource source, Vector3 targetWorldPos)
    {
        GameObject prefab = queueIngredientFlyPrefab != null ? queueIngredientFlyPrefab : ingredientInstancePrefab;
        if (prefab == null) yield break;

        GameObject flyObj = Instantiate(prefab, queueIngredientFlyParent);
        if (flyObj == null) yield break;

        IngredientInstanceController ingredientController = flyObj.GetComponent<IngredientInstanceController>();
        if (ingredientController != null)
            ingredientController.enabled = false;

        // 若飞行预制体带刚体，则禁用重力/动力学，避免到达 slot 后再被 Rigidbody “多掉一段”
        Rigidbody2D rb2d = flyObj.GetComponent<Rigidbody2D>();
        if (rb2d != null)
        {
            rb2d.velocity = Vector2.zero;
            rb2d.angularVelocity = 0f;
            rb2d.gravityScale = 0f;
            rb2d.isKinematic = true;
        }

        if (source.icon != null)
        {
            Image img = flyObj.GetComponentInChildren<Image>(true);
            if (img != null) img.sprite = source.icon;
            SpriteRenderer sr = flyObj.GetComponentInChildren<SpriteRenderer>(true);
            if (sr != null) sr.sprite = source.icon;
        }

        RectTransform flyRect = flyObj.GetComponent<RectTransform>();
        bool isUIFly = flyRect != null && queueIngredientFlyUIRoot != null;
        float duration = Mathf.Max(0.01f, queueIngredientFlyDuration);
        float liftUi = queueIngredientFlyLiftHeight > 0f ? queueIngredientFlyLiftHeight : queueIngredientFlyArcHeight;
        float liftPhase = Mathf.Clamp(queueIngredientFlyLiftPhaseRatio, 0.08f, 0.75f);
        float wobbleAmpUi = Mathf.Max(0f, queueIngredientFlyWobbleAmplitude);
        float wobbleAmpWorld = wobbleAmpUi * 0.01f;
        float fMin = Mathf.Min(queueIngredientFlyWobbleFreqMin, queueIngredientFlyWobbleFreqMax);
        float fMax = Mathf.Max(queueIngredientFlyWobbleFreqMin, queueIngredientFlyWobbleFreqMax);
        float wFreq1 = UnityEngine.Random.Range(fMin, fMax);
        float wFreq2 = UnityEngine.Random.Range(fMin, fMax);
        float wPh1 = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float wPh2 = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        float wAng = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
        Vector2 wU = new Vector2(Mathf.Cos(wAng), Mathf.Sin(wAng));
        Vector2 wV = new Vector2(-wU.y, wU.x);

        if (isUIFly)
        {
            flyRect.SetParent(queueIngredientFlyUIRoot, false);
            Vector2 startPos = WorldToAnchoredPosition(queueIngredientFlyUIRoot, source.fromWorldPos);
            Vector2 endPos = WorldToAnchoredPosition(queueIngredientFlyUIRoot, targetWorldPos);
            float elapsed = 0f;
            flyRect.anchoredPosition = startPos;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                Vector2 basePos = EvaluateAnchoredUpThenToTarget(startPos, endPos, liftUi, liftPhase, t);
                flyRect.anchoredPosition = ApplyAnchoredFlightWobble(basePos, t, wobbleAmpUi, wFreq1, wFreq2, wPh1, wPh2, wU, wV);
                yield return null;
            }
            flyRect.anchoredPosition = endPos;
        }
        else
        {
            Vector3 startPos = source.fromWorldPos;
            float liftWorld = liftUi * 0.01f;
            float wAng3 = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
            Vector3 wU3 = new Vector3(Mathf.Cos(wAng3), 0f, Mathf.Sin(wAng3));
            Vector3 wV3 = Vector3.Cross(Vector3.up, wU3).normalized;
            float elapsed = 0f;
            flyObj.transform.position = startPos;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                Vector3 basePos = EvaluateWorldUpThenToTarget(startPos, targetWorldPos, liftWorld, liftPhase, t);
                flyObj.transform.position = ApplyWorldFlightWobble(basePos, t, wobbleAmpWorld, wFreq1, wFreq2, wPh1, wPh2, wU3, wV3, 0.28f);
                yield return null;
            }
            flyObj.transform.position = targetWorldPos;
        }

        yield return StartCoroutine(CoPlayLandingScalePulse(flyObj.transform));
        Destroy(flyObj);
    }

    private IEnumerator CoPlayLandingScalePulse(Transform target)
    {
        if (target == null) yield break;
        Vector3 baseScale = target.localScale;
        Vector3 peak = baseScale * Mathf.Max(1f, queueIngredientLandingScaleUp);
        float duration = Mathf.Max(0.01f, queueIngredientLandingDuration);
        float half = duration * 0.5f;

        float elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / half);
            target.localScale = Vector3.LerpUnclamped(baseScale, peak, t);
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / half);
            target.localScale = Vector3.LerpUnclamped(peak, baseScale, t);
            yield return null;
        }

        target.localScale = baseScale;
    }

    private Vector2 WorldToAnchoredPosition(RectTransform root, Vector3 worldPos)
    {
        Camera cam = queueIngredientFlyUICamera;
        Vector2 screenPos = RectTransformUtility.WorldToScreenPoint(cam, worldPos);
        RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screenPos, cam, out Vector2 localPos);
        return localPos;
    }

    /// <summary>先沿 UI anchored 正上方抬升，再飞向终点（两段缓动，折线感明显）。</summary>
    private static Vector2 EvaluateAnchoredUpThenToTarget(Vector2 start, Vector2 end, float liftUp, float liftPhaseRatio, float t01)
    {
        t01 = Mathf.Clamp01(t01);
        liftPhaseRatio = Mathf.Clamp(liftPhaseRatio, 0.05f, 0.95f);
        Vector2 apex = start + new Vector2(0f, liftUp);
        if (t01 <= liftPhaseRatio)
        {
            float u = liftPhaseRatio > 1e-5f ? t01 / liftPhaseRatio : 1f;
            u = Mathf.Clamp01(u);
            float e = 1f - Mathf.Pow(1f - u, 3f);
            return Vector2.LerpUnclamped(start, apex, e);
        }

        float v = (t01 - liftPhaseRatio) / (1f - liftPhaseRatio);
        v = Mathf.Clamp01(v);
        float e2 = v * v * (3f - 2f * v);
        return Vector2.LerpUnclamped(apex, end, e2);
    }

    /// <summary>在基准路径上叠加随机频相位的正弦扰动；包络使起终点为 0，落点仍准确。</summary>
    private static Vector2 ApplyAnchoredFlightWobble(
        Vector2 basePos, float t01, float amplitude,
        float freq1, float freq2, float phase1, float phase2,
        Vector2 uAxis, Vector2 vAxis)
    {
        if (amplitude <= 1e-4f) return basePos;
        float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t01));
        float w = amplitude * env;
        float s1 = Mathf.Sin(t01 * freq1 * Mathf.PI * 2f + phase1);
        float s2 = Mathf.Sin(t01 * freq2 * Mathf.PI * 2f + phase2);
        return basePos + uAxis * (s1 * w) + vAxis * (s2 * w * 0.78f);
    }

    /// <summary>世界空间：水平面内椭圆扰动 + 少量竖直分量，动感更明显。</summary>
    private static Vector3 ApplyWorldFlightWobble(
        Vector3 basePos, float t01, float amplitude,
        float freq1, float freq2, float phase1, float phase2,
        Vector3 uAxis, Vector3 vAxis, float verticalMix)
    {
        if (amplitude <= 1e-6f) return basePos;
        float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t01));
        float w = amplitude * env;
        float s1 = Mathf.Sin(t01 * freq1 * Mathf.PI * 2f + phase1);
        float s2 = Mathf.Sin(t01 * freq2 * Mathf.PI * 2f + phase2);
        float sY = Mathf.Sin(t01 * (freq1 * 1.31f + freq2 * 0.27f) * Mathf.PI * 2f + (phase1 + phase2) * 0.5f);
        Vector3 horiz = uAxis * s1 + vAxis * (s2 * 0.78f);
        return basePos + horiz * w + Vector3.up * (sY * w * verticalMix);
    }

    /// <summary>世界坐标：先沿 Vector3.up 抬升，再飞向终点。</summary>
    private static Vector3 EvaluateWorldUpThenToTarget(Vector3 start, Vector3 end, float liftUp, float liftPhaseRatio, float t01)
    {
        t01 = Mathf.Clamp01(t01);
        liftPhaseRatio = Mathf.Clamp(liftPhaseRatio, 0.05f, 0.95f);
        Vector3 apex = start + Vector3.up * liftUp;
        if (t01 <= liftPhaseRatio)
        {
            float u = liftPhaseRatio > 1e-5f ? t01 / liftPhaseRatio : 1f;
            u = Mathf.Clamp01(u);
            float e = 1f - Mathf.Pow(1f - u, 3f);
            return Vector3.LerpUnclamped(start, apex, e);
        }

        float v = (t01 - liftPhaseRatio) / (1f - liftPhaseRatio);
        v = Mathf.Clamp01(v);
        float e2 = v * v * (3f - 2f * v);
        return Vector3.LerpUnclamped(apex, end, e2);
    }

    /// <summary>在 <see cref="potsList"/> 中查找第一个空闲且锅型匹配的菜谱可用锅。</summary>
    public Pot FindAvailablePotForRecipe(List<potType> acceptablePotTypes)
    {
        if (acceptablePotTypes == null || acceptablePotTypes.Count == 0) return null;
        if (potsList == null || potsList.Count == 0) return null;

        for (int i = 0; i < potsList.Count; i++)
        {
            Pot pot = potsList[i];
            if (pot == null || !pot.isActiveAndEnabled) continue;
            if (!pot.IsAvailable()) continue;
            if (acceptablePotTypes.Contains(pot.potType))
                return pot;
        }

        return null;
    }

    /// <summary>
    /// 将队首排队菜尽可能派给所有空闲锅。每次从 <see cref="_cookQueueData"/> 下标 0 取 1 份，直到无队首菜或无可用锅。
    /// </summary>
    private void TryDispatchQueueToPots()
    {
        if (!isActiveAndEnabled || potsList == null || potsList.Count == 0) return;

        int maxDispatch = Mathf.Max(potsList.Count, GetActiveCookQueueSlotCount());
        for (int i = 0; i < maxDispatch; i++)
        {
            if (!TryDispatchOneCookFromQueueFront())
                break;
        }
    }

    /// <summary>从排队槽下标 0 取 1 份菜；<see cref="potsList"/> 中任一空闲且接受该菜谱的锅开始烹饪。</summary>
    private bool TryDispatchOneCookFromQueueFront()
    {
        EnsureCookQueueDataSize();
        int active = GetActiveCookQueueSlotCount();
        if (active <= 0 || _cookQueueData.Count == 0) return false;

        if (_cookQueueData[0].IsEmpty)
            CompactCookQueueLeftPreserveOrder();

        if (_cookQueueData[0].IsEmpty)
            return false;

        DishRecipe r = _cookQueueData[0].recipe;
        if (r == null) return false;

        Pot pot = FindAvailablePotForRecipe(r.acceptablePot);
        if (pot == null)
            return false;

        PulseCookQueueSlotIfValid(0);

        // 食材实例仅在 Pot.CookingProcess 内生成一次（勿在此处再 StartCoroutine，否则会与锅内协程重复实例化）
        if (!pot.StartCooking(r))
            return false;

        _cookQueueData[0].count--;
        if (_cookQueueData[0].count <= 0)
        {
            _cookQueueData[0].Clear();
            CompactCookQueueLeftPreserveOrder();
        }
        else
            RefreshCookQueueUI();

        return true;
    }

    /// <summary>队列槽 0 卖空概念：与餐碟一致，非空槽整体左移。</summary>
    private void CompactCookQueueLeftPreserveOrder()
    {
        int active = GetActiveCookQueueSlotCount();
        EnsureCookQueueDataSize();
        List<(DishRecipe r, int c)> filled = new List<(DishRecipe, int)>();
        for (int i = 0; i < active && i < _cookQueueData.Count; i++)
        {
            CookQueueStackEntry e = _cookQueueData[i];
            if (!e.IsEmpty) filled.Add((e.recipe, e.count));
        }

        for (int i = 0; i < active && i < _cookQueueData.Count; i++)
            _cookQueueData[i].Clear();

        for (int i = 0; i < filled.Count && i < active; i++)
        {
            _cookQueueData[i].recipe = filled[i].r;
            _cookQueueData[i].count = filled[i].c;
        }

        RefreshCookQueueUI();
    }

    public void NotifyCookingPotFreed()
    {
        TryDispatchQueueToPots();
    }

    private int _dispatchFrameCounter;
    private Coroutine _forceScrollResetCoroutine;
    private int _lastRefreshOnOpenFrame = -1;

    private void Update()
    {
        HandleDishMenuScrollWheel();

        if ((_dispatchFrameCounter++ % 12) != 0) return;
        TryDispatchQueueToPots();
    }

    private void HandleDishMenuScrollWheel()
    {
        if (!isActiveAndEnabled || _dishItemWidgets == null || _dishItemWidgets.Count == 0) return;
        float w = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(w) < 0.01f) return;
        if (w > 0f)
            SelectDishItemByIndex(_selectedDishIndex - 1);
        else
            SelectDishItemByIndex(_selectedDishIndex + 1);
    }

    /// <summary>点击菜谱行或滚轮选中时调用。</summary>
    public void SelectDishItemByIndex(int index, bool autoScrollToSelection = true)
    {
        if (_dishItemWidgets == null || _dishItemWidgets.Count == 0) return;
        int prevSelectedIndex = _selectedDishIndex;
        index = Mathf.Clamp(index, 0, _dishItemWidgets.Count - 1);
        _selectedDishIndex = index;
        for (int i = 0; i < _dishItemWidgets.Count; i++)
        {
            dishItemPrefabs w = _dishItemWidgets[i];
            if (w == null || w.recipeData == null) continue;
            bool canCook = RecipeCanBeCookable(w.recipeData);
            w.ApplyVisual(i == _selectedDishIndex, canCook);
        }
        if (autoScrollToSelection)
            ScrollDishListToSelected();
        RefreshIngredientSideIndicators();

        // 选中项变化时：给食材指示做一次 Scale 波动反馈
        if (enableIngredientIndicatorScalePulse && prevSelectedIndex != _selectedDishIndex)
        {
            PulseIngredientIndicatorScale();
        }
    }

    public bool IsDishSelected(int index)
    {
        return _selectedDishIndex == index;
    }

    private void HideIngredientSideIndicators()
    {
        if (leftIngredientIndicatorRoot != null)
            leftIngredientIndicatorRoot.SetActive(false);
        if (rightIngredientIndicatorRoot != null)
            rightIngredientIndicatorRoot.SetActive(false);
    }

    /// <summary>根据当前选中的菜单项刷新左右食材指示（1 种仅左，2 种左右各一）。</summary>
    private void RefreshIngredientSideIndicators()
    {
        if (leftIngredientIndicatorRoot == null && rightIngredientIndicatorRoot == null)
            return;

        if (_dishItemWidgets == null || _dishItemWidgets.Count == 0
            || _selectedDishIndex < 0 || _selectedDishIndex >= _dishItemWidgets.Count)
        {
            HideIngredientSideIndicators();
            return;
        }

        dishItemPrefabs sel = _dishItemWidgets[_selectedDishIndex];
        if (sel == null || sel.recipeData == null || sel.recipeData.ingredients == null
            || sel.recipeData.ingredients.Count == 0)
        {
            HideIngredientSideIndicators();
            return;
        }

        List<DishIngredient> ings = sel.recipeData.ingredients;
        if (ings.Count == 1)
        {
            if (leftIngredientIndicatorRoot != null)
                leftIngredientIndicatorRoot.SetActive(true);
            ApplyIngredientSideIndicator(ings[0], leftIngredientIcon, leftIngredientCountText);
            if (rightIngredientIndicatorRoot != null)
                rightIngredientIndicatorRoot.SetActive(false);
        }
        else
        {
            if (leftIngredientIndicatorRoot != null)
                leftIngredientIndicatorRoot.SetActive(true);
            ApplyIngredientSideIndicator(ings[0], leftIngredientIcon, leftIngredientCountText);
            if (rightIngredientIndicatorRoot != null)
                rightIngredientIndicatorRoot.SetActive(true);
            ApplyIngredientSideIndicator(ings[1], rightIngredientIcon, rightIngredientCountText);
        }

        CaptureIndicatorBaseScaleIfSafe();
    }

    private void CaptureIndicatorBaseScaleIfSafe()
    {
        if (!enableIngredientIndicatorScalePulse) return;

        // 只有在没有对应脉冲协程运行时才更新“基准缩放”，避免把当前波动中的 Scale 当成基准导致越叠越大
        if (_leftIndicatorPulseCoroutine == null && leftIngredientIndicatorRoot != null && leftIngredientIndicatorRoot.activeSelf)
            _leftIndicatorBaseScale = leftIngredientIndicatorRoot.transform.localScale;
        if (_rightIndicatorPulseCoroutine == null && rightIngredientIndicatorRoot != null && rightIngredientIndicatorRoot.activeSelf)
            _rightIndicatorBaseScale = rightIngredientIndicatorRoot.transform.localScale;
    }

    private void PulseIngredientIndicatorScale()
    {
        if (!enableIngredientIndicatorScalePulse) return;

        if (ingredientIndicatorPulseDelay > 0f)
        {
            if (_ingredientIndicatorDelayedPulseCoroutine != null)
                StopCoroutine(_ingredientIndicatorDelayedPulseCoroutine);
            _ingredientIndicatorDelayedPulseCoroutine = StartCoroutine(CoDelayedPulse(ingredientIndicatorPulseDelay));
            return;
        }

        // 同时对左/右都能起效（但 RefreshIngredientSideIndicators 会决定谁处于激活状态）
        if (leftIngredientIndicatorRoot != null && leftIngredientIndicatorRoot.activeSelf)
        {
            if (_leftIndicatorPulseCoroutine != null) StopCoroutine(_leftIndicatorPulseCoroutine);
            leftIngredientIndicatorRoot.transform.localScale = _leftIndicatorBaseScale;
            _leftIndicatorPulseCoroutine = StartCoroutine(CoPulseIndicatorScale(leftIngredientIndicatorRoot.transform, _leftIndicatorBaseScale, true));
        }
        if (rightIngredientIndicatorRoot != null && rightIngredientIndicatorRoot.activeSelf)
        {
            if (_rightIndicatorPulseCoroutine != null) StopCoroutine(_rightIndicatorPulseCoroutine);
            rightIngredientIndicatorRoot.transform.localScale = _rightIndicatorBaseScale;
            _rightIndicatorPulseCoroutine = StartCoroutine(CoPulseIndicatorScale(rightIngredientIndicatorRoot.transform, _rightIndicatorBaseScale, false));
        }
    }

    private IEnumerator CoDelayedPulse(float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        PulseIngredientIndicatorScale();
        _ingredientIndicatorDelayedPulseCoroutine = null;
    }

    private IEnumerator CoPulseIndicatorScale(Transform target, Vector3 baseScale, bool isLeft)
    {
        if (target == null) yield break;

        float duration = Mathf.Max(0.01f, ingredientIndicatorPulseDuration);
        float half = duration * 0.5f;

        // 起始保证回到基准
        target.localScale = baseScale;

        float elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / half);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            target.localScale = Vector3.LerpUnclamped(baseScale, baseScale * ingredientIndicatorPulseScaleMultiplier, eased);
            yield return null;
        }

        elapsed = 0f;
        while (elapsed < half)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / half);
            float eased = Mathf.SmoothStep(0f, 1f, t);
            target.localScale = Vector3.LerpUnclamped(baseScale * ingredientIndicatorPulseScaleMultiplier, baseScale, eased);
            yield return null;
        }

        target.localScale = baseScale;

        if (isLeft) _leftIndicatorPulseCoroutine = null;
        else _rightIndicatorPulseCoroutine = null;
    }

    private static void ApplyIngredientSideIndicator(DishIngredient ing, Image icon, Text countText)
    {
        if (ing == null) return;
        int owned = InventoryManager.instance != null
            ? InventoryManager.instance.GetItemCount(ing.resourceType)
            : 0;
        ResourceItem res = GameValManager.Instance != null
            ? GameValManager.Instance.GetResourceInfo(ing.resourceType)
            : null;
        if (icon != null)
        {
            if (res != null && res.Icon != null)
            {
                icon.sprite = res.Icon;
                icon.enabled = true;
            }
            else
            {
                icon.enabled = false;
            }
        }
        if (countText != null)
            countText.text = $"{owned}/{ing.requiredCount}";
    }

    private void ScrollDishListToSelected()
    {
        if (dishMenuScrollRect == null || dishParent == null) return;
        if (_selectedDishIndex < 0 || _selectedDishIndex >= dishParent.childCount) return;

        RectTransform content = dishMenuScrollRect.content;
        RectTransform viewport = dishMenuScrollRect.viewport;
        if (content == null || viewport == null) return;

        RectTransform item = dishParent.GetChild(_selectedDishIndex) as RectTransform;
        if (item == null) return;

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(content);

        Vector3 itemCenterWorld = item.TransformPoint(item.rect.center);
        Vector3 itemLocalInContent = content.InverseTransformPoint(itemCenterWorld);

        if (dishMenuScrollRect.vertical)
        {
            float contentBottom = content.rect.yMin;
            float contentTop = content.rect.yMax;
            float viewportHeight = viewport.rect.height;
            float scrollRangeY = (contentTop - contentBottom) - viewportHeight;

            // 内容不足以滚动：保持在中间（例如只有一条菜单）
            if (scrollRangeY <= 1f)
            {
                dishMenuScrollRect.verticalNormalizedPosition = 0.5f;
            }
            else
            {
                // verticalNormalizedPosition：0=底端，1=顶端；使选中项中心对齐视口中心
                float itemCenterY = itemLocalInContent.y;
                float lowY = contentBottom + viewportHeight * 0.5f;
                float nY = (itemCenterY - lowY) / scrollRangeY;
                dishMenuScrollRect.verticalNormalizedPosition = Mathf.Clamp01(nY);
            }
        }

        if (dishMenuScrollRect.horizontal)
        {
            float contentLeft = content.rect.xMin;
            float contentRight = content.rect.xMax;
            float viewportWidth = viewport.rect.width;
            float scrollRangeX = (contentRight - contentLeft) - viewportWidth;

            if (scrollRangeX <= 1f)
            {
                dishMenuScrollRect.horizontalNormalizedPosition = 0.5f;
            }
            else
            {
                // horizontalNormalizedPosition：0=左端，1=右端；使选中项中心对齐视口中心
                float itemCenterX = itemLocalInContent.x;
                float lowX = contentLeft + viewportWidth * 0.5f;
                float nX = (itemCenterX - lowX) / scrollRangeX;
                dishMenuScrollRect.horizontalNormalizedPosition = Mathf.Clamp01(nX);
            }
        }
    }

    private bool HasAcceptablePot(DishRecipe recipe)
    {
        if (recipe == null || recipe.acceptablePot == null || recipe.acceptablePot.Count == 0) return false;
        if (potsList == null) return false;
        if (potsList.Count == 0) return false;
        foreach (Pot pot in potsList)
        {
            if (pot != null && recipe.acceptablePot.Contains(pot.potType))
                return true;
        }
        return false;
    }

    /// <summary>当前背包食材足够且存在可用锅类型。</summary>
    private bool RecipeCanBeCookable(DishRecipe recipe)
    {
        if (recipe == null) return false;
        if (recipe.locked) return false;
        return CheckIngredientsAvailableForRecipe(recipe) && HasAcceptablePot(recipe);
    }

    private List<DishRecipe> GetSortedUnlockedRecipes()
    {
        List<DishRecipe> list = new List<DishRecipe>();
        for (int i = 0; i < dishRecipes.Count; i++)
        {
            DishRecipe r = dishRecipes[i];
            if (r == null) continue;
            list.Add(r);
        }

        list.Sort((a, b) =>
        {
            if (a.locked != b.locked) return a.locked ? 1 : -1;
            bool ca = RecipeCanBeCookable(a);
            bool cb = RecipeCanBeCookable(b);
            if (ca != cb) return ca ? -1 : 1;
            int priceCmp = a.baseDishPrice.CompareTo(b.baseDishPrice);
            if (priceCmp != 0) return priceCmp;
            return a.dishID.CompareTo(b.dishID);
        });

        return list;
    }

    private void EnsureAllUnitsPopulated()
    {
        // 兜底：如果 Inspector 没填 allPots/allPlates，则自动从场景里收集（包含未激活对象）
        if (allPots == null) allPots = new List<Pot>();
        if (allPlates == null) allPlates = new List<Plate>();

        if (allPots.Count == 0)
        {
            Pot[] potsInScene = FindObjectsOfType<Pot>(true);
            allPots.AddRange(potsInScene);
        }

        if (allPlates.Count == 0)
        {
            Plate[] platesInScene = FindObjectsOfType<Plate>(true);
            allPlates.AddRange(platesInScene);
        }
    }

    private void SyncPotsByCount(int targetCount)
    {
        potsList.Clear();

        int activeCount = Mathf.Clamp(targetCount, 0, allPots.Count);
        for (int i = 0; i < allPots.Count; i++)
        {
            Pot pot = allPots[i];
            if (pot == null) continue;

            bool shouldShowInScene = ShouldShowFacilityInScene(pot, i, activeCount);
            if (pot.gameObject.activeSelf != shouldShowInScene)
                pot.gameObject.SetActive(shouldShowInScene);

            if (shouldShowInScene && IsFacilityUnlocked(pot))
                potsList.Add(pot);
        }

        RefreshAllFacilityUnlockVisuals(allPots);
    }

    private void SyncPlatesByCount(int targetCount)
    {
        platesList.Clear();

        int activeCount = Mathf.Clamp(targetCount, 0, allPlates.Count);
        for (int i = 0; i < allPlates.Count; i++)
        {
            Plate plate = allPlates[i];
            if (plate == null) continue;

            bool shouldShowInScene = ShouldShowFacilityInScene(plate, i, activeCount);
            if (plate.gameObject.activeSelf != shouldShowInScene)
                plate.gameObject.SetActive(shouldShowInScene);

            if (shouldShowInScene && IsFacilityUnlocked(plate))
                platesList.Add(plate);
        }

        RefreshAllFacilityUnlockVisuals(allPlates);
    }

    /// <summary>
    /// 槽位内始终显示；槽位外仅未解锁时显示以便点击。
    /// 已解锁的设施不再因「槽位外 + 已解锁」被整物体关掉。
    /// </summary>
    private static bool ShouldShowFacilityInScene(MonoBehaviour facility, int index, int activeSlotCount)
    {
        if (facility == null)
            return false;

        FacilityUnlockable unlock = facility.GetComponent<FacilityUnlockable>();

        if (index < activeSlotCount)
            return true;

        if (unlock != null && unlock.IsUnlocked)
            return true;

        return unlock != null && !unlock.IsUnlocked;
    }

    private static void RefreshAllFacilityUnlockVisuals<T>(List<T> facilities) where T : MonoBehaviour
    {
        if (facilities == null) return;
        for (int i = 0; i < facilities.Count; i++)
        {
            if (facilities[i] == null) continue;
            FacilityUnlockable unlock = facilities[i].GetComponent<FacilityUnlockable>();
            unlock?.RefreshVisualState();
        }
    }

    private static bool IsFacilityUnlocked(MonoBehaviour facility)
    {
        if (facility == null) return false;
        FacilityUnlockable unlock = facility.GetComponent<FacilityUnlockable>();
        return unlock == null || unlock.IsUnlocked;
    }

    public void GenerateFoodItems()
    {
        // 先清空旧UI
        ClearFoodItems();

        if (GameValManager.Instance == null)
        {
            Debug.LogError("GameValManager.Instance 未初始化！");
            return;
        }

        // 遍历资源列表
        foreach (var item in GameValManager.Instance.resources)
        {
            // 只显示食物类资源
            if (item.resourceKind != ResourceKind.Food || item.type==ResourceType.None) continue;

            // 实例化预制体
            GameObject foodGO = Instantiate(foodItemPrefabs, foodItemParent);
            foodItemPrefabs script = foodGO.GetComponent<foodItemPrefabs>();
            //print("EEEAAA");
            //print(item.type);
            foodGO.GetComponent<foodItemPrefabs>().resourceType = item.type;
            if (script != null)
            {
                int invCount = InventoryManager.instance != null
                    ? InventoryManager.instance.GetItemCount(item.type)
                    : 0;
                // 设置图标和数量（数量来自背包）
                script.foodIcon.sprite = item.Icon;
                script.foodAmount.text = invCount.ToString();
                //print("EEEAAAAA"+item.name);
                //print("EEEAAAAA"+item.count);
            }

            currentFoodItems.Add(foodGO);
        }
    }

    /// <summary>
    /// 清空食材UI
    /// </summary>
    public void ClearFoodItems()
    {
        foreach (var go in currentFoodItems)
        {
            if (go != null) Destroy(go);
        }
        currentFoodItems.Clear();

        // 或者直接清空父物体下所有子物体（更保险）
        for (int i = foodItemParent.childCount - 1; i >= 0; i--)
        {
            Destroy(foodItemParent.GetChild(i).gameObject);
        }
    }

    /// <summary>
    /// 设置选中食材信息
    /// </summary>
    public void SetFoodInformation(ResourceType resourceType)
    {
        foreach (var item in GameValManager.Instance.resources)
        {
            if (item.type == resourceType)
            {
                foodInformationTitle.text = item.name;
                foodInformationDescription.text = item.description;
                break;
            }
        }

    }

    /// <summary>
    /// 根据 dishRecipes 生成菜肴UI列表
    /// </summary>
    public void GenerateDishList()
    {
        ClearDishList();

        List<DishRecipe> sorted = GetSortedUnlockedRecipes();
        for (int i = 0; i < sorted.Count; i++)
        {
            DishRecipe recipe = sorted[i];
            GameObject dishGO = Instantiate(dishItemPrefabs, dishParent);
            dishItemPrefabs script = dishGO.GetComponent<dishItemPrefabs>();
            if (script == null)
            {
                Debug.LogWarning("dishItemPrefabs prefab 缺少 dishItemPrefabs 脚本！");
                continue;
            }

            script.disName.text = recipe.dishName;
            script.dishItem.sprite = recipe.dishIcon;
            if (script.dishPrice != null)
                script.dishPrice.text = recipe.baseDishPrice.ToString("F0");

            script.SetRecipeData(recipe);
            script.SetOwner(this, i);

            for (int j = script.dishFoodParent.childCount - 1; j >= 0; j--)
            {
                Destroy(script.dishFoodParent.GetChild(j).gameObject);
            }

            foreach (DishIngredient ing in recipe.ingredients)
            {
                GameObject foodGO = Instantiate(dishFoodItemPrefabs, script.dishFoodParent);
                foodItemPrefabs foodScript = foodGO.GetComponent<foodItemPrefabs>();
                if (foodScript == null)
                {
                    Debug.LogWarning("dishFoodItemPrefabs 缺少 foodItemPrefabs 脚本！");
                    continue;
                }

                ResourceItem resItem = GameValManager.Instance != null
                    ? GameValManager.Instance.GetResourceInfo(ing.resourceType)
                    : null;
                int owned = InventoryManager.instance != null
                    ? InventoryManager.instance.GetItemCount(ing.resourceType)
                    : 0;

                if (resItem != null)
                {
                    foodScript.foodIcon.sprite = resItem.Icon;
                    foodScript.foodAmount.text = $"{owned}/{ing.requiredCount}";
                    foodScript.resourceType = ing.resourceType;
                }
                else
                {
                    foodScript.foodAmount.text = $"{owned}/{ing.requiredCount}";
                }
            }

            bool canCook = RecipeCanBeCookable(recipe);
            script.ApplyVisual(false, canCook);

            currentDishes.Add(dishGO);
            _dishItemWidgets.Add(script);
        }

        if (_dishItemWidgets.Count > 0)
        {
            // 首次生成：选中第一个，但不触发“滚动到选中项”，避免首帧被自动推到另一侧
            SelectDishItemByIndex(0, false);
            // 再显式设定初始停靠边（按当前项目UI方向校正）
            ResetDishMenuScrollToStart();
            StartForceDishScrollToLeftOnOpen();
        }
    }

    private void ResetDishMenuScrollToStart()
    {
        if (dishMenuScrollRect == null) return;
        Canvas.ForceUpdateCanvases();
        // Unity ScrollRect：horizontal 0=左、1=右；vertical 0=下、1=上
        if (dishMenuScrollRect.horizontal)
            dishMenuScrollRect.horizontalNormalizedPosition = 0f;
        if (dishMenuScrollRect.vertical)
            dishMenuScrollRect.verticalNormalizedPosition = 1f;
    }

    /// <summary>打开 UI 后多帧强制将菜单归位到最左（纵向则最上）。</summary>
    public void StartForceDishScrollToLeftOnOpen()
    {
        if (!isActiveAndEnabled || dishMenuScrollRect == null) return;
        if (_forceScrollResetCoroutine != null)
            StopCoroutine(_forceScrollResetCoroutine);
        _forceScrollResetCoroutine = StartCoroutine(CoForceDishScrollToLeftOnOpen());
    }

    private IEnumerator CoForceDishScrollToLeftOnOpen()
    {
        for (int i = 0; i < 8; i++)
        {
            yield return null;
            ResetDishMenuScrollToStart();
        }
        _forceScrollResetCoroutine = null;
    }
    /// <summary>
    /// 清空菜肴UI列表
    /// </summary>
    public void ClearDishList()
    {
        foreach (var go in currentDishes)
        {
            if (go != null) Destroy(go);
        }
        currentDishes.Clear();
        _dishItemWidgets.Clear();
        _selectedDishIndex = -1;
        HideIngredientSideIndicators();

        for (int i = dishParent.childCount - 1; i >= 0; i--)
        {
            Destroy(dishParent.GetChild(i).gameObject);
        }
    }

    // 生成食材实例效果
    public IEnumerator SpawnIngredientInstances(DishRecipe recipe, Pot pot)
    {

        if (RestaurantPanel.instance == null || RestaurantPanel.instance.ingredientInstancePrefab == null)
        {
            Debug.LogWarning("食材实例预制体未设置！");
            yield break;
        }

        foreach (DishIngredient ingredient in recipe.ingredients)
        {
            ResourceItem resource = GameValManager.Instance != null
                ? GameValManager.Instance.GetResourceInfo(ingredient.resourceType)
                : null;
            if (resource == null || resource.Icon == null)
            {
                Debug.LogWarning($"找不到食材 {ingredient.resourceType} 的图标");
                continue;
            }

            for (int i = 0; i < ingredient.requiredCount; i++)
            {
                Transform spawnParent = RestaurantPanel.instance.ingredientSpawnParent != null ?
                    RestaurantPanel.instance.ingredientSpawnParent : pot.transform;

                GameObject instance = Instantiate(
                    RestaurantPanel.instance.ingredientInstancePrefab
                );

                IngredientInstanceController controller = instance.GetComponent<IngredientInstanceController>();
                if (controller != null)
                {
                    controller.Initialize(ingredient.resourceType, pot, resource.Icon);
                }
            }
        }
    }

    // 在 RestaurantPanel 类中添加以下方法

    /// <summary>
    /// 初始化菜碟UI
    /// </summary>
    public void InitializePlates()
    {
        foreach (Plate plate in platesList)
        {
            if (plate != null)
            {
                // 可以在这里添加菜碟的初始化逻辑
                // 例如：设置菜碟的UI引用、按钮事件等
            }
        }
    }

    /// <summary>
    /// 查找可以容纳指定菜肴的菜碟
    /// </summary>
    public Plate FindAvailablePlateForDish(DishRecipe recipe)
    {
        return FindSuitablePlateForOutgoingRecipe(recipe);
    }

    /// <summary>
    /// 出锅装盘：优先填满已装着同菜的碟子（剩余容量最少的最优先；并列时取更靠前的碟子），否则取最靠前的空碟（可在多盘上堆同一种菜）。
    /// 仅用于「当前碟子数据」下的选址；真正加菜在飞行落点之后由 <see cref="Pot"/> 调用 <see cref="Plate.TryAddDish"/>。
    /// </summary>
    public Plate FindSuitablePlateForOutgoingRecipe(DishRecipe recipe)
    {
        if (recipe == null || platesList == null || platesList.Count == 0) return null;

        Plate mostFilledSameType = null;
        int mostFilledRemainingCap = int.MaxValue;

        for (int i = 0; i < platesList.Count; i++)
        {
            Plate plate = platesList[i];
            if (plate == null || plate.currentDish == null || plate.currentDish.IsEmpty()) continue;
            if (!RecipesMatch(plate.currentDish.recipe, recipe)) continue;

            int remaining = plate.GetRemainingCapacity();
            if (remaining > 0 && remaining < mostFilledRemainingCap)
            {
                mostFilledRemainingCap = remaining;
                mostFilledSameType = plate;
            }
        }

        if (mostFilledSameType != null)
            return mostFilledSameType;

        for (int i = 0; i < platesList.Count; i++)
        {
            Plate plate = platesList[i];
            if (plate == null) continue;
            if (!plate.IsPlateEmpty()) continue;
            if (plate.CanAddDish(recipe))
                return plate;
        }

        return null;
    }

    /// <summary>
    /// 首碟卖空后：将其余碟子上有食物的盘整体按顺序左移补位（不改变相对顺序）。
    /// </summary>
    public void CompactPlatesLeftPreserveOrder()
    {
        if (platesList == null || platesList.Count == 0) return;

        List<Dish> dishes = new List<Dish>(platesList.Count);
        List<Pot> pots = new List<Pot>(platesList.Count);

        for (int i = 0; i < platesList.Count; i++)
        {
            Plate p = platesList[i];
            if (p == null) continue;
            if (p.currentDish != null && !p.currentDish.IsEmpty())
            {
                dishes.Add(p.currentDish);
                pots.Add(p.sourcePot);
            }
        }

        if (dishes.Count > platesList.Count)
            Debug.LogWarning($"[餐厅] 食物种类数量({dishes.Count})多于碟子数，将只保留前 {platesList.Count} 盘。");

        for (int i = 0; i < platesList.Count; i++)
        {
            Plate p = platesList[i];
            if (p == null) continue;
            p.ClearDishDataFieldsOnly();
        }

        int n = Mathf.Min(dishes.Count, platesList.Count);
        for (int i = 0; i < n; i++)
        {
            Plate p = platesList[i];
            if (p == null) continue;
            p.ApplyContentForRebalance(dishes[i], pots[i]);
        }

        for (int i = 0; i < platesList.Count; i++)
        {
            if (platesList[i] != null)
                platesList[i].RefreshDisplay();
        }
    }

    /// <summary>查找第一只仍有食物的碟子（按 platesList 顺序）。</summary>
    public Plate FindFirstPlateWithFood()
    {
        if (platesList == null) return null;
        for (int i = 0; i < platesList.Count; i++)
        {
            Plate plate = platesList[i];
            if (plate != null && !plate.IsPlateEmpty())
                return plate;
        }
        return null;
    }

    /// <summary>当前是否有可售卖的菜肴（用于控制顾客是否排队/入场）。</summary>
    public bool HasPlateWithFood()
    {
        return FindFirstPlateWithFood() != null;
    }

    /// <summary>
    /// 手动装盘（玩家交互）
    /// </summary>
    public bool ManualTransferToPlate(Pot pot, Plate plate)
    {
        if (pot == null || plate == null)
        {
            Debug.LogWarning("锅或菜碟为空");
            return false;
        }

        // 检查锅是否有烹饪完成的菜肴
        // 这里需要一个方法来检查锅的完成状态

        return false;
    }

    /// <summary>
    /// 根据菜ID解锁菜谱（供道具等调用）
    /// </summary>
    public void UnlockDishByID(int dishID)
    {
        DishRecipe recipe = dishRecipes.Find(d => d.dishID == dishID);
        if (recipe == null)
        {
            Debug.LogWarning($"未找到 dishID 为 {dishID} 的菜谱！");
            return;
        }

        if (!recipe.locked)
        {
            // 已经解锁，无需重复处理
            return;
        }

        recipe.locked = false;
        RunSessionManager.Instance?.RecordRecipeUnlocked(dishID);
        Debug.Log($"已解锁菜谱：{recipe.dishName} (ID={recipe.dishID})");

        // 如果餐厅面板当前是打开状态，刷新一次菜单显示
        GenerateDishList();
    }

    public Sprite GetDishIconByID(int dishID)
    {
        DishRecipe recipe = dishRecipes.Find(d => d.dishID == dishID);
        if (recipe == null) return null;
        return recipe.dishIcon;
    }
}
