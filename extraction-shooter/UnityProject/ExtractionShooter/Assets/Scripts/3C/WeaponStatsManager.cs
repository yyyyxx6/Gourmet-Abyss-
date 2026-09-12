using Game.Core;
using UnityEngine;
using System.Collections.Generic;


[System.Serializable]
public class MapDensityBinding
{
    public PlantGenerationSettings settings;   // 对应的Setting文件
    public float densityMultiplier = 1f; // 乘积因子
}

[System.Serializable]
public class EnemyLootBinding
{
    public ResourceType type;   // 对应的Setting文件
    public float lootDensityMultiplier = 1f; // 乘积因子
}

[System.Serializable]
public class LevelParamRateItem
{
    public string id;
    public float monsterCountRate = 1f;
    public float monsterRapidSpawnIntervalRate = 1f;
    public float monsterWaitTimeRate = 1f;
    public float propProbabilityRate = 1f;

    [System.NonSerialized] public bool levelBuffBasesCaptured;
    [System.NonSerialized] public float levelBuffBaseMonsterCountRate = 1f;
    [System.NonSerialized] public float levelBuffBaseSpawnIntervalRate = 1f;
    [System.NonSerialized] public float levelBuffBaseWaitTimeRate = 1f;
    [System.NonSerialized] public float levelBuffBasePropProbabilityRate = 1f;

    public void EnsureLevelBuffBases()
    {
        if (levelBuffBasesCaptured) return;
        levelBuffBaseMonsterCountRate = monsterCountRate;
        levelBuffBaseSpawnIntervalRate = monsterRapidSpawnIntervalRate;
        levelBuffBaseWaitTimeRate = monsterWaitTimeRate;
        levelBuffBasePropProbabilityRate = propProbabilityRate;
        levelBuffBasesCaptured = true;
    }

    /// <summary>
    /// 技能 buff：各 Rate 相对关卡条目的初始快照做乘法 — 最终 = 快照 * (1 + v * 技能等级)。
    /// </summary>
    public void ApplyLevelRatesBuff(float countV, float spawnV, float waitV, float propV, int skillLevel)
    {
        EnsureLevelBuffBases();
        float lc = Mathf.Max(0, skillLevel);
        monsterCountRate = levelBuffBaseMonsterCountRate * (1f + countV * lc);
        monsterRapidSpawnIntervalRate = levelBuffBaseSpawnIntervalRate * (1f + spawnV * lc);
        monsterWaitTimeRate = levelBuffBaseWaitTimeRate * (1f + waitV * lc);
        propProbabilityRate = levelBuffBasePropProbabilityRate * (1f + propV * lc);
    }
}

public class WeaponStatsManager : PersistentMonoSingleton<WeaponStatsManager>
{
    [Header("宠物状态（进入战斗时由 PetManager 读取）")]
    public List<PetStateEntry> petStateList = new List<PetStateEntry>();

    [System.Serializable]
    public class PetStateEntry
    {
        public PetType petType;
        public bool isEnabled;
    }

    [Header("主武器数值")]
    public bool isPrimaryEnable=true;
    public float primaryFireRate = 0.2f;
    public int primaryPelletCount = 1;
    public int primaryPenetrationCount = 0;
    public float primaryBulletSpeed = 20f;
    public float primaryBulletSize = 1f;
    public float primaryBaseDamage = 10f;
    public float primaryCriticalChance = 0.1f;
    public float primaryCriticalMultiplier = 2f;
    public float primaryMaxTravelDistance = 100f;
    [Header("主武器击杀分裂")]
    public bool primaryEnableKillSplit = false;
    [Min(1)] public int primaryKillSplitCount = 4;
    [Min(0)] public int primaryKillSplitMaxIterations = 1;
    [Range(0f, 1f)] public float primaryKillSplitChildDamageRatio = 0.5f;
    public GameObject primaryKillSplitChildProjectilePrefab;
    [Header("主武器AOE伤害")]
    public bool primaryEnableAOE = false;
    [Min(0f)] public float primaryAOERadius = 3f;
    [Range(0f, 1f)] public float primaryAOEEdgeMinDamageRatio = 0.3f;
    public GameObject primaryAOEEffectPrefab;

    [Header("副武器数值")]
    public bool isSecondaryEnable=true;
    public float secondaryDamageValue = 20f;
    public float secondaryFireRate = 0.5f;
    public float secondaryLaserLength = 30f;
    public int secondaryLaserCount = 1;
    public float secondaryLaserWidth = 1f;
    public float secondaryCritChance = 0.1f;
    public float secondaryCritMultiplier = 2f;
    public int secondaryMaxChainCount = 3;
    public float secondaryChainSearchRadius = 10f;

    [Header("商店数值")]
    public float sellPriceMultiplier = 1f; // 售卖价格倍率
    public float sellTimeMultiplier = 1f;   // 售卖时间缩短倍率
    public int shopSlotCount = 4;
    public int slotCapacity = 4;

    [Header("餐厅数值")]
    public int restaurantPotCount = 3;
    public int restaurantPlateCount = 3;
    [Tooltip("激活的餐桌数量（与 RestaurantTableManager.allTables 下标对应）")]
    public int restaurantTableCount = 3;
    [Tooltip("烹饪排队槽位数量（与 RestaurantPanel.allDishQueueSlots 下标对应）")]
    public int restaurantDishQueueSlotCount = 4;
    [Tooltip("烹饪时间倍率：最终时间 = 原时间 * cookingTimeMultiplier")]
    public float cookingTimeMultiplier = 1f;
    [Tooltip("每个摆菜碟的总容量（同类碟子统一）")]
    public int restaurantPlateCapacity = 5;
    [Tooltip("顾客就餐速度倍率：实际用餐时间 = 基础时间 / restaurantDiningSpeedMultiplier")]
    public float restaurantDiningSpeedMultiplier = 1f;
    [Tooltip("售卖价格加成比例：最终价格 = 原价格 * (1 + restaurantSellBonusRate)")]
    public float restaurantSellBonusRate = 0f;

    [Header("顾客数值")]
    public int restaurantCustomerPrefabCount = 3;
    public int restaurantMaxCustomersInside = 3;
    public int restaurantMaxTotalCustomers = 20;
    public float customerMoveSpeedMultiplier = 1f;

    [Header("背包数值")]
    public int inventorySlotCount = 4;        // 背包插槽个数
    public int inventorySlotCapacity = 4;    // 背包每个插槽的容量
    [Tooltip("死亡时保留的资源比例；statID 71，0.1 表示保留 10%，技能加成为百分点。")]
    [Range(0f, 1f)] public float deathRetentionRate = 0.1f;
    
    [Header("氧气与弹药数值")]
    public float oxygenMax = 100f;                // 氧气总量
    public float oxygenConsumeRate = 1f;          // 氧气每秒消耗速度
    [Tooltip("BOSS 攻击命中时：伤害数值 × 该系数 = 扣除的氧气量")]
    public float bossDamageToOxygenMultiplier = 1f;
    public int primaryAmmoMax = 100;                // 主武器弹容量
    public int primaryAmmoConsumePerShot = 1;         // 主武器每次射击消耗
    [Tooltip("主武器弹夹容量（充能完成后装填的最大子弹数）")]
    public int primaryMagazineCapacity = 100;
    public int secondaryAmmoMax = 50;               // 副武器弹容量
    public int secondaryAmmoConsumePerShot = 1;       // 副武器每次射击消耗
    [Tooltip("副武器弹夹容量（充能完成后装填的最大子弹数）")]
    public int secondaryMagazineCapacity = 50;

    [Header("弹夹充能 / 换弹夹时间")]
    [Tooltip("主武器弹夹耗尽后，充能(换弹夹)持续时间，期间禁止开火并播放抬起+Y轴旋转动画")]
    public float primaryReloadDuration = 0.8f;
    [Tooltip("副武器弹夹耗尽后，充能(换弹夹)持续时间，期间禁止开火并播放抬起+Z轴旋转动画")]
    public float secondaryReloadDuration = 0.8f;

    [Header("地图生成数值")]
    [Tooltip("默认地图密度乘积因子，当没有特定绑定时使用此值")]
    public float defaultMapDensityMultiplier = 1f;

    [Tooltip("各PlantGenerationSettings专属密度乘积绑定")]
    public List<MapDensityBinding> mapDensityBindings = new List<MapDensityBinding>();
    [Tooltip("各掉落物专属密度乘积绑定")]
    public List<EnemyLootBinding> enemtLootDensityBindings = new List<EnemyLootBinding>();

    [Header("关卡参数")]
    public List<LevelParamRateItem> levelParamRateItems = new List<LevelParamRateItem>();
    // 运行时用的快速查找字典
    private Dictionary<string, float> mapDensityMultipliers = new Dictionary<string, float>();

    // 数值变化事件
    public event System.Action OnShopStatsChanged;
    public event System.Action OnInventoryStatsChanged;
    public event System.Action OnBattleStatsChanged;
    public event System.Action OnRestaurantStatsChanged;
    public event System.Action OnCustomerStatsChanged;
    public event System.Action OnLevelStatsChanged;
    public event System.Action OnWeaponStatsChanged;

    protected override void OnAwake()
    {
        // 初始化字典
        RebuildDensityDictionary();

        if (levelParamRateItems != null)
        {
            for (int i = 0; i < levelParamRateItems.Count; i++)
                levelParamRateItems[i]?.EnsureLevelBuffBases();
        }
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        // 让 Inspector 里直接改值也能实时同步到餐厅
        restaurantPotCount = Mathf.Max(1, restaurantPotCount);
        restaurantPlateCount = Mathf.Max(1, restaurantPlateCount);
        restaurantTableCount = Mathf.Max(1, restaurantTableCount);
        restaurantDishQueueSlotCount = Mathf.Max(1, restaurantDishQueueSlotCount);
        cookingTimeMultiplier = Mathf.Max(0.01f, cookingTimeMultiplier);
        restaurantPlateCapacity = Mathf.Max(1, restaurantPlateCapacity);
        restaurantDiningSpeedMultiplier = Mathf.Max(0.01f, restaurantDiningSpeedMultiplier);
        restaurantSellBonusRate = Mathf.Max(0f, restaurantSellBonusRate);
        restaurantCustomerPrefabCount = Mathf.Max(0, restaurantCustomerPrefabCount);
        restaurantMaxCustomersInside = Mathf.Max(1, restaurantMaxCustomersInside);
        restaurantMaxTotalCustomers = Mathf.Max(1, restaurantMaxTotalCustomers);
        customerMoveSpeedMultiplier = Mathf.Max(0.01f, customerMoveSpeedMultiplier);
        primaryKillSplitCount = Mathf.Max(1, primaryKillSplitCount);
        primaryKillSplitMaxIterations = Mathf.Max(0, primaryKillSplitMaxIterations);
        primaryKillSplitChildDamageRatio = Mathf.Clamp01(primaryKillSplitChildDamageRatio);
        primaryAOERadius = Mathf.Max(0f, primaryAOERadius);
        primaryAOEEdgeMinDamageRatio = Mathf.Clamp01(primaryAOEEdgeMinDamageRatio);

        if (Instance == this)
        {
            OnRestaurantStatsChanged?.Invoke();
            OnCustomerStatsChanged?.Invoke();
            OnLevelStatsChanged?.Invoke();
            OnWeaponStatsChanged?.Invoke();
        }
    }
#endif

    /// <summary>
    /// 从List重建字典
    /// </summary>
    public void RebuildDensityDictionary()
    {
        mapDensityMultipliers.Clear();
        foreach (var binding in mapDensityBindings)
        {
            if (binding.settings != null)
            {
                mapDensityMultipliers[binding.settings.name] = binding.densityMultiplier;
            }
        }
    }

    /// <summary>
    /// 按资源类型读取敌人掉落密度倍率；列表未配置或下标越界时返回 1。
    /// </summary>
    public float GetEnemyLootDensityMultiplier(ResourceType type)
    {
        if (type == ResourceType.None || enemtLootDensityBindings == null || enemtLootDensityBindings.Count == 0)
            return 1f;

        int index = (int)type;
        if (index >= 0 && index < enemtLootDensityBindings.Count)
        {
            EnemyLootBinding binding = enemtLootDensityBindings[index];
            if (binding != null && binding.type == type)
                return Mathf.Max(0f, binding.lootDensityMultiplier);
        }

        for (int i = 0; i < enemtLootDensityBindings.Count; i++)
        {
            EnemyLootBinding binding = enemtLootDensityBindings[i];
            if (binding != null && binding.type == type)
                return Mathf.Max(0f, binding.lootDensityMultiplier);
        }

        return 1f;
    }
    
    public void OnShopStatsChangedInvoke()
    {
        OnShopStatsChanged?.Invoke();
    }
    
    public void OnInventoryStatsChangedInvoke()
    {
        OnInventoryStatsChanged?.Invoke();
    }
    
    public void OnBattleStatsChangedInvoke()
    {
        OnBattleStatsChanged?.Invoke();
    }

    public void OnRestaurantStatsChangedInvoke()
    {
        OnRestaurantStatsChanged?.Invoke();
    }

    public void OnCustomerStatsChangedInvoke()
    {
        OnCustomerStatsChanged?.Invoke();
    }

    public void OnLevelStatsChangedInvoke()
    {
        OnLevelStatsChanged?.Invoke();
    }

    public void OnWeaponStatsChangedInvoke()
    {
        OnWeaponStatsChanged?.Invoke();
    }

    public void SetPrimaryReloadDuration(float duration)
    {
        primaryReloadDuration = Mathf.Max(0.01f, duration);
        OnWeaponStatsChanged?.Invoke();
    }

    public void SetSecondaryReloadDuration(float duration)
    {
        secondaryReloadDuration = Mathf.Max(0.01f, duration);
        OnWeaponStatsChanged?.Invoke();
    }

    public void SetSellPriceMultiplier(float multiplier)
    {
        sellPriceMultiplier = multiplier;
        OnShopStatsChanged?.Invoke();
    }

    public void SetSellTimeMultiplier(float multiplier)
    {
        sellTimeMultiplier = multiplier;
        OnShopStatsChanged?.Invoke();
    }

    public void SetShopSlotCount(int count)
    {
        shopSlotCount = count;
        OnShopStatsChanged?.Invoke();
    }

    public void SetSlotCapacity(int capacity)
    {
        slotCapacity = capacity;
        OnShopStatsChanged?.Invoke();
    }

    public void SetRestaurantPotCount(int count)
    {
        restaurantPotCount = Mathf.Max(1, count);
        OnRestaurantStatsChanged?.Invoke();
    }

    public void SetRestaurantPlateCount(int count)
    {
        restaurantPlateCount = Mathf.Max(1, count);
        OnRestaurantStatsChanged?.Invoke();
    }

    public void SetRestaurantTableCount(int count)
    {
        restaurantTableCount = Mathf.Max(1, count);
        OnRestaurantStatsChanged?.Invoke();
    }

    public void SetRestaurantDishQueueSlotCount(int count)
    {
        restaurantDishQueueSlotCount = Mathf.Max(1, count);
        OnRestaurantStatsChanged?.Invoke();
    }

    public void SetCookingTimeMultiplier(float multiplier)
    {
        cookingTimeMultiplier = Mathf.Max(0.01f, multiplier);
        OnRestaurantStatsChanged?.Invoke();
    }

    public void SetRestaurantPlateCapacity(int capacity)
    {
        restaurantPlateCapacity = Mathf.Max(1, capacity);
        OnRestaurantStatsChanged?.Invoke();
    }

    public void SetRestaurantDiningSpeedMultiplier(float multiplier)
    {
        restaurantDiningSpeedMultiplier = Mathf.Max(0.01f, multiplier);
        OnRestaurantStatsChanged?.Invoke();
    }

    public void SetRestaurantSellBonusRate(float bonusRate)
    {
        restaurantSellBonusRate = Mathf.Max(0f, bonusRate);
        OnRestaurantStatsChanged?.Invoke();
    }

    /// <summary>
    /// 餐厅售卖金币：单价已含技能树 sellPriceMultiplier 时，再叠加外卖升级 sellBonusRate。
    /// </summary>
    public int CalcRestaurantSellGold(float unitPrice, int count = 1)
    {
        count = Mathf.Max(0, count);
        if (count <= 0)
            return 0;

        float price = unitPrice * count * (1f + restaurantSellBonusRate);
        return Mathf.Max(0, Mathf.RoundToInt(price));
    }

    public void SetRestaurantCustomerPrefabCount(int count)
    {
        restaurantCustomerPrefabCount = Mathf.Max(0, count);
        OnCustomerStatsChanged?.Invoke();
    }

    public void SetRestaurantMaxCustomersInside(int count)
    {
        restaurantMaxCustomersInside = Mathf.Max(1, count);
        OnCustomerStatsChanged?.Invoke();
    }

    public void SetRestaurantMaxTotalCustomers(int count)
    {
        restaurantMaxTotalCustomers = Mathf.Max(1, count);
        OnCustomerStatsChanged?.Invoke();
    }

    public void SetCustomerMoveSpeedMultiplier(float multiplier)
    {
        customerMoveSpeedMultiplier = Mathf.Max(0.01f, multiplier);
        OnCustomerStatsChanged?.Invoke();
    }

    /// <summary>
    /// 设置背包插槽个数
    /// </summary>
    public void SetInventorySlotCount(int count)
    {
        if (count <= 0) return;
        
        inventorySlotCount = Mathf.Max(1, count);
        OnInventoryStatsChanged?.Invoke();
    }

    /// <summary>
    /// 设置背包插槽容量
    /// </summary>
    public void SetInventorySlotCapacity(int capacity)
    {
        if (capacity <= 0) return;
        
        inventorySlotCapacity = Mathf.Max(1, capacity);
        OnInventoryStatsChanged?.Invoke();
    }

    /// <summary>
    /// 设置氧气总量
    /// </summary>
    public void SetOxygenMax(float value)
    {
        if (value <= 0) return;
        
        oxygenMax = value;
        OnBattleStatsChanged?.Invoke();
    }

    /// <summary>
    /// 设置氧气消耗速度
    /// </summary>
    public void SetOxygenConsumeRate(float rate)
    {
        oxygenConsumeRate = Mathf.Max(0.1f, rate);
        OnBattleStatsChanged?.Invoke();
    }

    /// <summary>
    /// 设置主武器弹容量
    /// </summary>
    public void SetPrimaryAmmoMax(int value)
    {
        if (value <= 0) return;
        
        primaryAmmoMax = value;
        OnBattleStatsChanged?.Invoke();
    }

    /// <summary>
    /// 设置主武器每次射击消耗
    /// </summary>
    public void SetPrimaryAmmoConsumePerShot(int value)
    {
        if (value <= 0) return;
        
        primaryAmmoConsumePerShot = value;
        OnBattleStatsChanged?.Invoke();
    }

    /// <summary>
    /// 设置副武器弹容量
    /// </summary>
    public void SetSecondaryAmmoMax(int value)
    {
        if (value <= 0) return;
        
        secondaryAmmoMax = value;
        OnBattleStatsChanged?.Invoke();
    }

    /// <summary>
    /// 设置副武器每次射击消耗
    /// </summary>
    public void SetSecondaryAmmoConsumePerShot(int value)
    {
        if (value <= 0) return;
        
        secondaryAmmoConsumePerShot = value;
        OnBattleStatsChanged?.Invoke();
    }

    /// <summary>
    /// 设置某个PlantGenerationSettings的密度乘积
    /// </summary>
    public void SetMapDensityMultiplier(PlantGenerationSettings settings, float multiplier)
    {
        if (settings == null) return;

        // 先查List，有则更新，没有则添加
        bool found = false;
        foreach (var binding in mapDensityBindings)
        {
            if (binding.settings == settings)
            {
                binding.densityMultiplier = multiplier;
                found = true;
                break;
            }
        }
        if (!found)
        {
            mapDensityBindings.Add(new MapDensityBinding
            {
                settings = settings,
                densityMultiplier = multiplier
            });
        }

        // 更新字典
        mapDensityMultipliers[settings.name] = multiplier;
    }

    /// <summary>
    /// 获取某个PlantGenerationSettings的密度乘积
    /// </summary>
    public float GetMapDensityMultiplier(PlantGenerationSettings settings)
    {
        if (settings == null) return defaultMapDensityMultiplier;
        if (mapDensityMultipliers.TryGetValue(settings.name, out float multiplier))
        {
            return multiplier;
        }
        else
        {
            return defaultMapDensityMultiplier;
        }
    }

    public float GetMonsterCountRate(string id)
    {
        return GetLevelRateItem(id)?.monsterCountRate ?? 1f;
    }

    public float GetMonsterRapidSpawnIntervalRate(string id)
    {
        return GetLevelRateItem(id)?.monsterRapidSpawnIntervalRate ?? 1f;
    }

    public float GetMonsterWaitTimeRate(string id)
    {
        return GetLevelRateItem(id)?.monsterWaitTimeRate ?? 1f;
    }

    public float GetPropProbabilityRate(string id)
    {
        return GetLevelRateItem(id)?.propProbabilityRate ?? 1f;
    }

    private LevelParamRateItem GetLevelRateItem(string id)
    {
        if (string.IsNullOrEmpty(id) || levelParamRateItems == null) return null;

        for (int i = 0; i < levelParamRateItems.Count; i++)
        {
            LevelParamRateItem item = levelParamRateItems[i];
            if (item == null) continue;
            if (item.id == id) return item;
        }

        return null;
    }

    /// <summary>
    /// 技能 buff：按关卡 id 调整 Count / SpawnInterval / Wait / Prop 四类倍率（相对 Awake 时快照：快照 * (1 + v * 技能等级)）。
    /// </summary>
    public bool TryApplyLevelRatesBuff(string levelId, float countV, float spawnIntervalV, float waitV, float propV, int skillLevel)
    {
        LevelParamRateItem item = GetLevelRateItem(levelId?.Trim());
        if (item == null)
        {
            Debug.LogWarning($"[WeaponStatsManager] 未找到关卡 id「{levelId}」的 LevelParamRateItem。");
            return false;
        }

        item.ApplyLevelRatesBuff(countV, spawnIntervalV, waitV, propV, skillLevel);
        OnLevelStatsChangedInvoke();
        return true;
    }

    /// <summary>
    /// 查询宠物是否启用（供 PetManager 在进入战斗时读取）
    /// </summary>
    public bool IsPetEnabled(PetType petType)
    {
        if (petType == PetType.None) return false;
        if (petStateList == null) return false;

        for (int i = 0; i < petStateList.Count; i++)
        {
            var entry = petStateList[i];
            if (entry == null) continue;
            if (entry.petType != petType) continue;
            return entry.isEnabled;
        }

        return false;
    }

    /// <summary>
    /// 运行时设置宠物启用状态（可选：便于技能/配置系统写入）
    /// </summary>
    public void SetPetEnabled(PetType petType, bool enabled)
    {
        if (petType == PetType.None) return;
        if (petStateList == null) petStateList = new List<PetStateEntry>();

        for (int i = 0; i < petStateList.Count; i++)
        {
            var entry = petStateList[i];
            if (entry == null) continue;
            if (entry.petType != petType) continue;
            bool newlyEnabled = enabled && !entry.isEnabled;
            entry.isEnabled = enabled;
            if (newlyEnabled) RunSessionManager.Instance?.RecordPetUnlocked(petType);
            return;
        }

        petStateList.Add(new PetStateEntry
        {
            petType = petType,
            isEnabled = enabled
        });
        if (enabled) RunSessionManager.Instance?.RecordPetUnlocked(petType);
    }
}
