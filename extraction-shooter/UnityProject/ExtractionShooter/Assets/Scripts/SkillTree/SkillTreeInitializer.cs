using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;

public class SkillTreeInitializer : MonoBehaviour
{
    [Header("技能树设置")]
    public SkillTree skillTree;
    public SkillNode skillNodePrefab;
    public RectTransform nodesParent;
    public Sprite defaultIcon; // 默认图标，当配置的图标加载失败时使用

    [Header("布局参数")]
    public float horizontalSpacing = 200f;
    public float verticalSpacing = 120f;

    [Header("地图密度绑定")]
    public List<MapDensityBinding> mapDensityBindings;

    private Dictionary<int, SkillNode> skillNodeMap = new Dictionary<int, SkillNode>();

    private void Start()
    {
        if (skillTree == null)
            skillTree = GetComponent<SkillTree>();

        // 初始化地图密度绑定
        InitializeMapDensityBindings();

        // 从配置生成技能树
        GenerateSkillTreeFromConfig();
    }

    private void InitializeMapDensityBindings()
    {
        var wsm = WeaponStatsManager.Instance;
        if (wsm == null) return;

        // 如果有手动配置的地图密度绑定，就使用
        if (mapDensityBindings != null && mapDensityBindings.Count > 0)
        {
            wsm.mapDensityBindings = new List<MapDensityBinding>(mapDensityBindings);
            wsm.RebuildDensityDictionary();
        }
    }
    private List<SkillLevelCost> ParseLevelCosts(string str)
    {
        var costs = new List<SkillLevelCost>();
        if (string.IsNullOrEmpty(str)) return costs;

        var levelEntries = str.Split('|'); // 按等级分割
        foreach (string entry in levelEntries)
        {
            var parts = entry.Split(':'); // 货币类型:数量
            if (parts.Length == 2)
            {
                if (int.TryParse(parts[0], out int typeInt) &&
                    int.TryParse(parts[1], out int amount))
                {
                    costs.Add(new SkillLevelCost
                    {
                        costType = (ResourceType)typeInt,
                        costAmount = amount
                    });
                }
            }
        }
        return costs;
    }
    private void GenerateSkillTreeFromConfig()
    {
        var configReader = ExcelConfigReader.Instance;
        if (configReader == null)
        {
            Debug.LogError("ExcelConfigReader未找到");
            return;
        }

        var skillConfigs = configReader.GetSkillConfigs();
        skillTree.allSkillNodes.Clear();
        skillNodeMap.Clear();

        // 第一遍：创建所有技能节点
        // 第一遍：创建所有技能节点
        foreach (var config in skillConfigs)
        {
            SkillNode newNode = Instantiate(skillNodePrefab, nodesParent);
            newNode.name = $"SkillNode_{config.skillID}";

            SkillNodeData skillData = new SkillNodeData
            {
                skillID = config.skillID.ToString(),
                skillName = config.skillName,
                description = config.description,
                maxLevel = config.maxLevel,
                currentLevel = 0,
                isLearned = false,
                isRare = config.isRare == 1
            };

            // 解析等级消耗字符串到 List<SkillLevelCost>
            skillData.levelCosts = config.levelCosts;
            //print("Config路径" + config.iconPath);
            // 图标
            Sprite icon = LoadSkillIcon(config.iconPath);
            skillData.icon = icon;
            if (newNode.iconImage != null)
            {
                newNode.iconImage.sprite = icon;
            }

            // 设置技能效果回调
            skillData.onSkillLearned = new UnityEngine.Events.UnityEvent();
            skillData.onSkillLearned.AddListener(() => ApplySkillEffects(config, skillData.currentLevel));

            newNode.skillData = skillData;

            // 设置位置
            Vector2 position = ParsePosition(config.position);
            newNode.GetComponent<RectTransform>().anchoredPosition = new Vector2(
                position.x * horizontalSpacing,
                -position.y * verticalSpacing
            );

            skillTree.allSkillNodes.Add(newNode);
            skillNodeMap[config.skillID] = newNode;
        }

        // 第二遍：建立前置关系。所有前置技能只需达到 1 级。
        foreach (var config in skillConfigs)
        {
            if (config.prerequisiteSkillIDs != null && config.prerequisiteSkillIDs.Count > 0)
            {
                SkillNode currentNode = skillNodeMap[config.skillID];
                foreach (int prereqId in config.prerequisiteSkillIDs)
                {
                    if (skillNodeMap.ContainsKey(prereqId))
                    {
                        currentNode.prerequisites.Add(new PrerequisiteData
                        {
                            node = skillNodeMap[prereqId]
                        });
                    }
                }
            }
        }
    }

    private Sprite LoadSkillIcon(string iconPath)
    {
        if (string.IsNullOrEmpty(iconPath))
        {
            Debug.LogWarning($"未配置图标路径，使用默认图标");
            return defaultIcon;
        }

        // 清理图标路径
        string cleanIconPath = iconPath.Trim();  // 移除首尾空格
        cleanIconPath = cleanIconPath.Replace("\r", "").Replace("\n", "");  // 移除换行符

        // 移除可能的文件扩展名
        string pathWithoutExtension = cleanIconPath;
        if (pathWithoutExtension.EndsWith(".png") || pathWithoutExtension.EndsWith(".jpg"))
        {
            pathWithoutExtension = pathWithoutExtension.Substring(0, pathWithoutExtension.LastIndexOf('.'));
        }

        // 检查路径是否为空
        if (string.IsNullOrWhiteSpace(pathWithoutExtension))
        {
            Debug.LogWarning($"图标路径为空，使用默认图标");
            return defaultIcon;
        }

        // 调试信息
        //Debug.Log($"清理后的图标路径: '{pathWithoutExtension}'");
        //Debug.Log($"路径长度: {pathWithoutExtension.Length}");
        //Debug.Log($"第一个字符: {(int)pathWithoutExtension[0]}");
        //Debug.Log($"最后一个字符: {(int)pathWithoutExtension[pathWithoutExtension.Length - 1]}");

        // 直接尝试加载，不使用 Path.Combine
        try
        {
            Sprite icon = Resources.Load<Sprite>(pathWithoutExtension);
            if (icon != null)
            {
                //Debug.Log($"成功加载图标: {iconPath} -> {icon.name}");
                return icon;
            }
            else
            {
                // 尝试不同的加载方式
                //Debug.LogWarning($"Resources.Load<Sprite>(\"{pathWithoutExtension}\") 返回null");

                // 尝试加载Texture2D然后创建Sprite
                Texture2D texture = Resources.Load<Texture2D>(pathWithoutExtension);
                if (texture != null)
                {
                    // Debug.Log($"找到Texture2D: {texture.name}");
                    icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), Vector2.one * 0.5f);
                    return icon;
                }

                // 列出所有可用的资源
                UnityEngine.Object[] allResources = Resources.LoadAll("");
                // Debug.Log($"Resources根目录共有 {allResources.Length} 个资源:");
                foreach (UnityEngine.Object obj in allResources)
                {
                    // Debug.Log($"  - {obj.name} ({obj.GetType().Name})");
                }

                //Debug.LogWarning($"无法加载图标: {iconPath}，使用默认图标");
                return defaultIcon;
            }
        }
        catch (System.Exception e)
        {
            //Debug.LogError($"加载图标时发生错误: {e.Message}");
            //Debug.LogError($"StackTrace: {e.StackTrace}");
            return defaultIcon;
        }
    }
    private Vector2 ParsePosition(string positionStr)
    {
        if (string.IsNullOrEmpty(positionStr))
            return Vector2.zero;

        string[] parts = positionStr.Split(',');
        if (parts.Length == 2)
        {
            if (float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) &&
                float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                return new Vector2(x, y);
        }
        return Vector2.zero;
    }

    private void ApplySkillEffects(SkillConfigData config, int level = 1)
    {
        var wsm = WeaponStatsManager.Instance;
        if (wsm == null || string.IsNullOrEmpty(config.buffEffects)) return;

        // 解析buff效果字符串，支持普通效果和地图密度元组效果
        string[] effects = config.buffEffects.Split(';');
        foreach (string effect in effects)
        {
            ApplySingleEffect(effect.Trim(), wsm, level);
        }

        // 触发相应的事件
        TriggerStatChangeEvents(config.buffEffects, wsm);
    }

    private void ApplySingleEffect(string effect, WeaponStatsManager wsm, int level = 1)
    {
        // 关卡倍率元组 (49,(levelId,countV,spawnV,waitV,propV)) — levelId 与 WeaponStatsManager.levelParamRateItems[].id 一致
        var levelRateMatch = Regex.Match(effect, @"\(49,\(([^,)]+),([\d.-]+),([\d.-]+),([\d.-]+),([\d.-]+)\)\)");
        if (levelRateMatch.Success)
        {
            string levelId = levelRateMatch.Groups[1].Value.Trim().Trim('"');
            float v1 = float.Parse(levelRateMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            float v2 = float.Parse(levelRateMatch.Groups[3].Value, CultureInfo.InvariantCulture);
            float v3 = float.Parse(levelRateMatch.Groups[4].Value, CultureInfo.InvariantCulture);
            float v4 = float.Parse(levelRateMatch.Groups[5].Value, CultureInfo.InvariantCulture);
            wsm.TryApplyLevelRatesBuff(levelId, v1, v2, v3, v4, level);
            return;
        }

        // 检查是否是地图密度元组效果 (30,(levelID,multiplier))
        var mapDensityMatch = Regex.Match(effect, @"\(30,\((\d+),([\d.]+)\)\)");
        if (mapDensityMatch.Success)
        {
            // 地图密度特殊效果
            int levelID = int.Parse(mapDensityMatch.Groups[1].Value);
            float multiplier = float.Parse(mapDensityMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            ApplyMapDensityEffect(levelID, multiplier, wsm, level);
            return;
        }

        // 检查是否是凋落物密度元组效果 (31,(levelID,multiplier))
        var lootDensityMatch = Regex.Match(effect, @"\(31,\((\d+),([\d.]+)\)\)");
        if (lootDensityMatch.Success)
        {
            // 地图密度特殊效果
            int resourceID = int.Parse(lootDensityMatch.Groups[1].Value);
            float multiplier = float.Parse(lootDensityMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            ApplyLootDensityEffect(resourceID, multiplier, wsm, level);
            return;
        }
        // 普通效果 (statID,value)
        var normalMatch = Regex.Match(effect, @"\((\d+),([\d.-]+)\)");
        if (normalMatch.Success)
        {
            int statID = int.Parse(normalMatch.Groups[1].Value);
            float value = float.Parse(normalMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            ApplyStatEffect(statID, value, wsm, level);
        }
    }
    private void ApplyLootDensityEffect(int RecourcesID, float multiplier, WeaponStatsManager wsm, int level = 1)
    {
        // 通过levelID查找对应的地图密度绑定
        if (RecourcesID >= 0 && RecourcesID < wsm.enemtLootDensityBindings.Count)
        {
            var binding = wsm.enemtLootDensityBindings[RecourcesID];
            if (binding.type != ResourceType.None)
            {
                // 应用密度乘数
                binding.lootDensityMultiplier = 1 * (1 + multiplier * level);
                wsm.RebuildDensityDictionary();
            }
        }
        else
        {
            Debug.LogWarning($"未找到凋落物资源ID {RecourcesID} 对应的凋落物资源设置");
        }
    }
    private void ApplyMapDensityEffect(int levelID, float multiplier, WeaponStatsManager wsm, int level = 1)
    {
        // 通过levelID查找对应的地图密度绑定
        if (levelID >= 0 && levelID < wsm.mapDensityBindings.Count)
        {
            var binding = wsm.mapDensityBindings[levelID];
            if (binding.settings != null)
            {
                // 应用密度乘数
                binding.densityMultiplier = 1 * (1 + multiplier * level);
                wsm.RebuildDensityDictionary();
            }
        }
        else
        {
            Debug.LogWarning($"未找到关卡ID {levelID} 对应的地图设置");
        }
    }

    private void ApplyStatEffect(int statID, float value, WeaponStatsManager wsm, int level = 1)
    {
        // 获取初始值
        float initialValue = GetInitialStatValue(statID, wsm);

        switch (statID)
        {
            // 主武器
            case 0: wsm.primaryFireRate = initialValue * (1 + value * level); break; // 开火速率
            case 1: wsm.primaryPelletCount += (int)value; break; // 个数，保持加法
            case 2: wsm.primaryPenetrationCount += (int)value; break; // 个数，保持加法
            case 3: wsm.primaryBulletSpeed = initialValue * (1 + value * level); break; // 子弹速度
            case 4: wsm.primaryBulletSize = initialValue * (1 + value * level); break; // 子弹大小
            case 5: wsm.primaryBaseDamage = initialValue * (1 + value * level); break; // 基础伤害
            case 6: wsm.primaryCriticalChance = initialValue + value * level; break; // 暴击几率
            case 7: wsm.primaryCriticalMultiplier = initialValue + value * level; break; // 暴击倍率
            case 8: wsm.primaryMaxTravelDistance = initialValue * (1 + value * level); break; // 最大射程

            // 副武器
            case 9: wsm.secondaryDamageValue = initialValue * (1 + value * level); break; // 副武器伤害
            case 10: wsm.secondaryFireRate = initialValue * (1 + value * level); break; // 副武器开火速率
            case 11: wsm.secondaryLaserLength = initialValue * (1 + value * level); break; // 激光长度
            case 12: wsm.secondaryLaserCount += (int)value; break; // 个数，保持加法
            case 13: wsm.secondaryLaserWidth = initialValue * (1 + value * level); break; // 激光宽度
            case 14: wsm.secondaryCritChance = initialValue + value * level; break; // 副武器暴击几率
            case 15: wsm.secondaryCritMultiplier = initialValue + value * level; break; // 副武器暴击倍率
            case 16: wsm.secondaryMaxChainCount += (int)value; break; // 个数，保持加法
            case 17: wsm.secondaryChainSearchRadius = initialValue * (1 + value * level); break; // 连锁搜索半径

            // 商店相关
            case 18: wsm.sellPriceMultiplier = initialValue * (1 + value * level); break; // 售价乘数
            case 19: wsm.sellTimeMultiplier = initialValue / (1 + value * level); break; // 时间乘数
            case 20: wsm.shopSlotCount += (int)value; break; // 个数，保持加法
            case 21: wsm.slotCapacity += (int)value; break; // 个数，保持加法
            case 22: wsm.inventorySlotCount += (int)value; break; // 个数，保持加法
            case 23: wsm.inventorySlotCapacity += (int)value; break; // 个数，保持加法

            // 氧气系统
            case 24: wsm.oxygenMax = initialValue * (1 + value * level); break; // 氧气最大值
            case 25: wsm.oxygenConsumeRate = initialValue * (1 + value * level); break; // 氧气消耗速率

            // 弹药系统
            case 26: wsm.primaryAmmoMax = Mathf.Max(1, (int)(initialValue * (1 + value * level))); break; // 主武器弹药最大值
            case 27: wsm.primaryAmmoConsumePerShot = Mathf.Max(1, (int)(initialValue * (1 + value * level))); break; // 主武器每发弹药消耗
            case 28: wsm.secondaryAmmoMax = Mathf.Max(1, (int)(initialValue * (1 + value * level))); break; // 副武器弹药最大值
            case 29: wsm.secondaryAmmoConsumePerShot = Mathf.Max(1, (int)(initialValue * (1 + value * level))); break; // 副武器每发弹药消耗
            case 32:
                {
                    //启动副武器
                    wsm.isSecondaryEnable = (value != 0); 
                    BattleValManager.Instance.enbaleSecondWeapon();
                    break;
                }

            // 主武器分裂 / AOE
            case 33: wsm.primaryEnableKillSplit = value != 0f; break;
            case 34: wsm.primaryKillSplitCount = Mathf.Max(1, wsm.primaryKillSplitCount + (int)value); break;
            case 35: wsm.primaryKillSplitChildDamageRatio = Mathf.Clamp01(initialValue * (1f + value * level)); break;
            case 36: wsm.primaryEnableAOE = value != 0f; break;
            case 37: wsm.primaryAOERadius = Mathf.Max(0f, initialValue * (1f + value * level)); break;

            // 餐厅
            case 38: wsm.SetRestaurantPotCount(wsm.restaurantPotCount + (int)value); break;
            case 39: wsm.SetRestaurantPlateCount(wsm.restaurantPlateCount + (int)value); break;
            case 40: wsm.SetCookingTimeMultiplier(initialValue / (1f + value * level)); break; // 与换弹一致：正值缩短烹饪时间
            case 41: wsm.SetRestaurantSellBonusRate(Mathf.Max(0f, initialValue + value * level)); break;

            // 顾客
            case 42: wsm.SetRestaurantMaxTotalCustomers(wsm.restaurantMaxTotalCustomers + (int)value); break;
            case 43: wsm.SetRestaurantMaxCustomersInside(wsm.restaurantMaxCustomersInside + (int)value); break;
            case 44: wsm.SetCustomerMoveSpeedMultiplier(initialValue * (1f + value * level)); break;

            // 弹夹容量 / 换弹时间（正值 = 换弹更快，时间为 原 * 1/(1+v*等级)）
            case 45: wsm.primaryMagazineCapacity = Mathf.Max(1, Mathf.RoundToInt(initialValue * (1f + value * level))); break;
            case 46: wsm.secondaryMagazineCapacity = Mathf.Max(1, Mathf.RoundToInt(initialValue * (1f + value * level))); break;
            case 47: wsm.SetPrimaryReloadDuration(initialValue / (1f + value * level)); break;
            case 48: wsm.SetSecondaryReloadDuration(initialValue / (1f + value * level)); break;
            case 69: wsm.SetRestaurantDishQueueSlotCount(wsm.restaurantDishQueueSlotCount + (int)value); break;
            // A value of 0.05 adds five percentage points per level to the configured base rate.
            case 71: wsm.deathRetentionRate = Mathf.Clamp01(initialValue + value * level); break;

            // FlyingCompanion（数值来自 PetManager Awake 快照；statID 52–65）
            case 52:
            case 53:
            case 54:
            case 55:
            case 56:
            case 57:
            case 58:
            case 59:
            case 60:
            case 61:
            case 62:
            case 63:
            case 64:
            case 65:
                if (PetManager.Instance != null)
                    PetManager.Instance.ApplyFlyingCompanionBuffStat(statID, value, level);
                break;
        }
    }

    // 获取初始数值
    private float GetInitialStatValue(int statID, WeaponStatsManager wsm)
    {
        var configReader = ExcelConfigReader.Instance;
        if (configReader != null)
        {
            return configReader.GetInitialStatValue(statID);
        }

        // 如果无法从配置读取，则从当前值推断
        return GetCurrentStatValue(statID, wsm);
    }

    // 获取当前值（用于回退）
    private float GetCurrentStatValue(int statID, WeaponStatsManager wsm)
    {
        if (statID >= 52 && statID <= 65)
        {
            if (PetManager.Instance != null)
                return PetManager.Instance.GetFlyingCompanionStatValue(statID);
            return 0f;
        }

        if (wsm == null) return 0f;

        switch (statID)
        {
            case 0: return wsm.primaryFireRate;
            case 1: return wsm.primaryPelletCount;
            case 2: return wsm.primaryPenetrationCount;
            case 3: return wsm.primaryBulletSpeed;
            case 4: return wsm.primaryBulletSize;
            case 5: return wsm.primaryBaseDamage;
            case 6: return wsm.primaryCriticalChance;
            case 7: return wsm.primaryCriticalMultiplier;
            case 8: return wsm.primaryMaxTravelDistance;
            case 9: return wsm.secondaryDamageValue;
            case 10: return wsm.secondaryFireRate;
            case 11: return wsm.secondaryLaserLength;
            case 12: return wsm.secondaryLaserCount;
            case 13: return wsm.secondaryLaserWidth;
            case 14: return wsm.secondaryCritChance;
            case 15: return wsm.secondaryCritMultiplier;
            case 16: return wsm.secondaryMaxChainCount;
            case 17: return wsm.secondaryChainSearchRadius;
            case 18: return wsm.sellPriceMultiplier;
            case 19: return wsm.sellTimeMultiplier;
            case 20: return wsm.shopSlotCount;
            case 21: return wsm.slotCapacity;
            case 22: return wsm.inventorySlotCount;
            case 23: return wsm.inventorySlotCapacity;
            case 24: return wsm.oxygenMax;
            case 25: return wsm.oxygenConsumeRate;
            case 26: return wsm.primaryAmmoMax;
            case 27: return wsm.primaryAmmoConsumePerShot;
            case 28: return wsm.secondaryAmmoMax;
            case 29: return wsm.secondaryAmmoConsumePerShot;
            case 30: return wsm.defaultMapDensityMultiplier;
            case 31: return wsm.bossDamageToOxygenMultiplier;
            case 32: return wsm.isSecondaryEnable ? 1f : 0f;
            case 33: return wsm.primaryEnableKillSplit ? 1f : 0f;
            case 34: return wsm.primaryKillSplitCount;
            case 35: return wsm.primaryKillSplitChildDamageRatio;
            case 36: return wsm.primaryEnableAOE ? 1f : 0f;
            case 37: return wsm.primaryAOERadius;
            case 38: return wsm.restaurantPotCount;
            case 39: return wsm.restaurantPlateCount;
            case 40: return wsm.cookingTimeMultiplier;
            case 41: return wsm.restaurantSellBonusRate;
            case 42: return wsm.restaurantMaxTotalCustomers;
            case 43: return wsm.restaurantMaxCustomersInside;
            case 44: return wsm.customerMoveSpeedMultiplier;
            case 45: return wsm.primaryMagazineCapacity;
            case 46: return wsm.secondaryMagazineCapacity;
            case 47: return wsm.primaryReloadDuration;
            case 48: return wsm.secondaryReloadDuration;
            case 66: return wsm.isPrimaryEnable ? 1f : 0f;
            case 67: return wsm.primaryKillSplitMaxIterations;
            case 68: return wsm.primaryAOEEdgeMinDamageRatio;
            case 69: return wsm.restaurantDishQueueSlotCount;
            case 70: return wsm.restaurantCustomerPrefabCount;
            case 71: return wsm.deathRetentionRate;
            default: return 0f;
        }
    }

    private void TriggerStatChangeEvents(string buffEffects, WeaponStatsManager wsm)
    {
        if (buffEffects.Contains("(18,") || buffEffects.Contains("(19,") ||
            buffEffects.Contains("(20,") || buffEffects.Contains("(21,"))
        {
            wsm.OnShopStatsChangedInvoke();
        }
        if (buffEffects.Contains("(22,") || buffEffects.Contains("(23,"))
        {
            wsm.OnInventoryStatsChangedInvoke();
        }
        if (buffEffects.Contains("(24,") || buffEffects.Contains("(25,") ||
            buffEffects.Contains("(26,") || buffEffects.Contains("(27,") ||
            buffEffects.Contains("(28,") || buffEffects.Contains("(29,"))
        {
            wsm.OnBattleStatsChangedInvoke();
        }
        if (buffEffects.Contains("(30,"))
        {
            wsm.RebuildDensityDictionary();
        }
        if (buffEffects.Contains("(33,") || buffEffects.Contains("(34,") ||
            buffEffects.Contains("(35,") || buffEffects.Contains("(36,") ||
            buffEffects.Contains("(37,") || buffEffects.Contains("(45,") ||
            buffEffects.Contains("(46,") || buffEffects.Contains("(47,") ||
            buffEffects.Contains("(48,"))
        {
            wsm.OnWeaponStatsChangedInvoke();
        }
        if (buffEffects.Contains("(38,") || buffEffects.Contains("(39,") ||
            buffEffects.Contains("(40,") || buffEffects.Contains("(41,") ||
            buffEffects.Contains("(69,"))
        {
            wsm.OnRestaurantStatsChangedInvoke();
        }
        if (buffEffects.Contains("(42,") || buffEffects.Contains("(43,") ||
            buffEffects.Contains("(44,"))
        {
            wsm.OnCustomerStatsChangedInvoke();
        }
        if (buffEffects.Contains("(49,"))
        {
            wsm.OnLevelStatsChangedInvoke();
        }
        if (buffEffects.Contains("(52,") || buffEffects.Contains("(53,") ||
            buffEffects.Contains("(54,") || buffEffects.Contains("(55,") ||
            buffEffects.Contains("(56,") || buffEffects.Contains("(57,") ||
            buffEffects.Contains("(58,") || buffEffects.Contains("(59,") ||
            buffEffects.Contains("(60,") || buffEffects.Contains("(61,") ||
            buffEffects.Contains("(62,") || buffEffects.Contains("(63,") ||
            buffEffects.Contains("(64,") || buffEffects.Contains("(65,"))
        {
            wsm.OnBattleStatsChangedInvoke();
        }
    }
}
