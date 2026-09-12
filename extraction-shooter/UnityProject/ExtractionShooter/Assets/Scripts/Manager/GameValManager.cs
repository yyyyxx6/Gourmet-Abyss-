using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Game.Core;
using UnityEngine;
using UnityEngine.Events;

[System.Serializable]
public class ResourceItem
{
    public ResourceType type;
    public ResourceKind resourceKind=ResourceKind.Others;
    public int count;
    public int maxCapacity = 9999; // 可选：资源最大容量
    public string name;
    public string description;
    public Sprite Icon;
    public GameObject ItemObject;
    public ResourceItem(ResourceType type, int count = 0, int maxCapacity = 9999)
    {
        this.type = type;
        this.count = count;
        this.maxCapacity = maxCapacity;

    }
    
    public bool CanAdd(int amount)
    {
        return count + amount <= maxCapacity;
    }
    
    public int Add(int amount)
    {
        int previousCount = count;
        count = Mathf.Clamp(count + amount, 0, maxCapacity);
        return count - previousCount; // 返回实际增加的数量
    }
    
    public bool TryConsume(int amount)
    {
        if (count >= amount)
        {
            count -= amount;
            return true;
        }
        return false;
    }
}

[System.Serializable]
public class ResourceChangedEvent : UnityEvent<ResourceType, int, int> { }

[DefaultExecutionOrder(-100)] // 确保资源管理器尽早初始化
public class GameValManager : PersistentMonoSingleton<GameValManager>
{
    [Header("资源配置")]
    [SerializeField] public List<ResourceItem> resources = new List<ResourceItem>();
    
    [Header("资源事件")]
    public ResourceChangedEvent OnResourceAdded = new ResourceChangedEvent();
    public ResourceChangedEvent OnResourceConsumed = new ResourceChangedEvent();
    public ResourceChangedEvent OnResourceChanged = new ResourceChangedEvent();
    public UnityEvent OnResourcesLoaded = new UnityEvent();
    public UnityEvent OnResourcesSaved = new UnityEvent();
    
    [Header("自动保存设置")]
    [SerializeField] private bool autoSave = true;
    [SerializeField] private float saveInterval = 60f; // 自动保存间隔（秒）
    private float saveTimer = 0f;
    
    [Header("调试")]
    [SerializeField] private bool debugMode = false;
    
    #region 单例和初始化
    protected override void OnAwake()
    {
        InitializeResources();
    }

    private void Start()
    {
        //LoadResources();
        //AddResource(ResourceType.LootCrabStick, 4);
    }
    
    private void Update()
    {
        if (autoSave)
        {
            // saveTimer += Time.deltaTime;
            // if (saveTimer >= saveInterval)
            // {
            //     SaveResources();
            //     saveTimer = 0f;
            // }
        }
    }
    
    /// <summary>
    /// 初始化资源列表
    /// </summary>
    private void InitializeResources()
    {
        // 确保所有资源类型都存在
        foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
        {
            if (!resources.Exists(r => r.type == type))
            {
                resources.Add(new ResourceItem(type, 0));
                if (debugMode) Debug.Log($"添加缺失的资源类型: {type}");
            }
        }
    }
    #endregion
    
    #region 增删改查核心方法
    
    /// <summary>
    /// 获取指定类型的资源数量
    /// </summary>
    public int GetResourceCount(ResourceType type)
    {
        ResourceItem item = resources.Find(r => r.type == type);
        if (item != null)
        {
            return item.count;
        }
        
        // 如果资源不存在，自动创建
        AddResourceType(type, 0);
        return 0;
    }
    
    /// <summary>
    /// 增加资源
    /// </summary>
    public bool AddResource(ResourceType type, int amount)
    {
        if (amount <= 0)
        {
            Debug.LogWarning($"增加资源数量必须为正数: {type} {amount}");
            return false;
        }
        
        ResourceItem item = resources.Find(r => r.type == type);
        if (item == null)
        {
            item = new ResourceItem(type, 0);
            resources.Add(item);
        }
        
        int oldCount = item.count;
        
        if (!item.CanAdd(amount))
        {
            int added = item.Add(amount);
            OnResourceAdded?.Invoke(type, added, item.count);
            OnResourceChanged?.Invoke(type, oldCount, item.count);

            if (added > 0 && ShouldShowResourceGainMessage(type))
            {
                GlobalMessageUI.ShowResourceGain(item.Icon, added);
            }
            
            if (debugMode) Debug.Log($"[资源管理器] {type} 已达到最大容量，只增加了 {added} 个，当前: {item.count}/{item.maxCapacity}");
            return false;
        }
        
        int actualAdded = item.Add(amount);
        
        if (debugMode) Debug.Log($"[资源管理器] 增加了 {amount} 个 {type}，当前: {item.count}");
        
        OnResourceAdded?.Invoke(type, amount, item.count);
        OnResourceChanged?.Invoke(type, oldCount, item.count);

        if (actualAdded > 0 && ShouldShowResourceGainMessage(type))
        {
            GlobalMessageUI.ShowResourceGain(item.Icon, actualAdded);
        }
        
        return true;
    }

    private static bool ShouldShowResourceGainMessage(ResourceType type)
    {
        if (type == ResourceType.Money || type == ResourceType.LootPumkin)
            return false;
        return true;
    }
    
    /// <summary>
    /// 尝试消耗资源
    /// </summary>
    public bool TryConsumeResource(ResourceType type, int amount)
    {
        if (amount < 0)
        {
            Debug.LogWarning($"消耗资源数量必须为正数: {type} {amount}");
            return false;
        }
        
        ResourceItem item = resources.Find(r => r.type == type);
        if (item == null)
        {
            item = new ResourceItem(type, 0);
            resources.Add(item);
        }
        
        int oldCount = item.count;
        
        if (item.TryConsume(amount))
        {
            if (debugMode) Debug.Log($"[资源管理器] 消耗了 {amount} 个 {type}，剩余: {item.count}");
            
            OnResourceConsumed?.Invoke(type, amount, item.count);
            OnResourceChanged?.Invoke(type, oldCount, item.count);
            return true;
        }
        else
        {
            if (debugMode) Debug.Log($"[资源管理器] 资源不足: {type} 需要: {amount} 拥有: {item.count}");
            return false;
        }
    }
    
    /// <summary>
    /// 直接设置资源数量
    /// </summary>
    public void SetResource(ResourceType type, int newCount)
    {
        ResourceItem item = resources.Find(r => r.type == type);
        if (item == null)
        {
            item = new ResourceItem(type, 0);
            resources.Add(item);
        }
        
        int oldCount = item.count;
        newCount = Mathf.Clamp(newCount, 0, item.maxCapacity);
        item.count = newCount;
        
        int difference = newCount - oldCount;
        if (difference > 0)
        {
            OnResourceAdded?.Invoke(type, difference, newCount);
        }
        else if (difference < 0)
        {
            OnResourceConsumed?.Invoke(type, Mathf.Abs(difference), newCount);
        }
        
        OnResourceChanged?.Invoke(type, oldCount, newCount);
        
        if (debugMode) Debug.Log($"[资源管理器] 设置 {type} 为 {newCount} (旧: {oldCount})");
    }
    
    /// <summary>
    /// 批量增加资源
    /// </summary>
    public void AddResources(Dictionary<ResourceType, int> resourcesToAdd)
    {
        foreach (var kvp in resourcesToAdd)
        {
            AddResource(kvp.Key, kvp.Value);
        }
    }
    
    /// <summary>
    /// 批量消耗资源
    /// </summary>
    public bool TryConsumeResources(Dictionary<ResourceType, int> resourcesToConsume)
    {
        // 先检查所有资源是否足够
        foreach (var kvp in resourcesToConsume)
        {
            if (GetResourceCount(kvp.Key) < kvp.Value)
            {
                if (debugMode) Debug.Log($"[资源管理器] 批量消耗失败: {kvp.Key} 不足");
                return false;
            }
        }
        
        // 如果都足够，再执行消耗
        foreach (var kvp in resourcesToConsume)
        {
            TryConsumeResource(kvp.Key, kvp.Value);
        }
        
        return true;
    }
    
    /// <summary>
    /// 检查是否拥有足够资源
    /// </summary>
    public bool HasEnoughResources(Dictionary<ResourceType, int> requiredResources)
    {
        foreach (var kvp in requiredResources)
        {
            if (GetResourceCount(kvp.Key) < kvp.Value)
            {
                return false;
            }
        }
        return true;
    }
    
    /// <summary>
    /// 检查是否拥有足够数量的单一资源
    /// </summary>
    public bool HasEnoughResource(ResourceType type, int amount)
    {
        return GetResourceCount(type) >= amount;
    }

    /// <summary>是否够支付设施解锁（金币）。</summary>
    public bool CanAffordFacilityUnlock(int goldCost)
    {
        return HasEnoughResource(ResourceType.Money, Mathf.Max(0, goldCost));
    }

    /// <summary>支付设施解锁费用；成功则扣除金币。</summary>
    public bool TryPayFacilityUnlock(int goldCost)
    {
        if (goldCost <= 0)
            return true;
        return TryConsumeResource(ResourceType.Money, goldCost);
    }
    
    #endregion
    
    #region 资源类型管理
    
    /// <summary>
    /// 添加新的资源类型
    /// </summary>
    public bool AddResourceType(ResourceType type, int initialAmount = 0, int maxCapacity = 9999)
    {
        if (resources.Exists(r => r.type == type))
        {
            Debug.LogWarning($"资源类型已存在: {type}");
            return false;
        }
        
        resources.Add(new ResourceItem(type, initialAmount, maxCapacity));
        if (debugMode) Debug.Log($"[资源管理器] 添加了新资源类型: {type} 初始数量: {initialAmount}");
        return true;
    }
    
    /// <summary>
    /// 移除资源类型
    /// </summary>
    public bool RemoveResourceType(ResourceType type)
    {
        int removedCount = resources.RemoveAll(r => r.type == type);
        if (debugMode) Debug.Log($"[资源管理器] 移除了资源类型: {type}");
        return removedCount > 0;
    }
    
    /// <summary>
    /// 获取所有资源类型的列表
    /// </summary>
    public List<ResourceType> GetAllResourceTypes()
    {
        return resources.Select(r => r.type).ToList();
    }
    
    /// <summary>
    /// 获取所有资源列表
    /// </summary>
    public List<ResourceItem> GetAllResources()
    {
        return new List<ResourceItem>(resources);
    }
    
    /// <summary>
    /// 获取资源的详细信息
    /// </summary>
    public ResourceItem GetResourceInfo(ResourceType type)
    {
        return resources.Find(r => r.type == type);
    }
    
    #endregion
    
    #region 存储和加载
    
    /// <summary>
    /// 保存资源到PlayerPrefs
    /// </summary>
    public void SaveResources()
    {
        try
        {
            // 保存每种资源的数量
            foreach (ResourceItem item in resources)
            {
                PlayerPrefs.SetInt($"Resource_{item.type}", item.count);
            }
            
            // 保存时间戳
            PlayerPrefs.SetString("Resources_SaveTime", DateTime.Now.ToString());
            PlayerPrefs.Save();
            
            OnResourcesSaved?.Invoke();
            
            if (debugMode) Debug.Log("[资源管理器] 资源保存成功");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[资源管理器] 保存资源时出错: {e.Message}");
        }
    }
    
    /// <summary>
    /// 从PlayerPrefs加载资源
    /// </summary>
    public void LoadResources()
    {
        try
        {
            foreach (ResourceItem item in resources)
            {
                int savedCount = PlayerPrefs.GetInt($"Resource_{item.type}", item.count);
                item.count = savedCount;
            }
            
            OnResourcesLoaded?.Invoke();
            
            if (debugMode) Debug.Log("[资源管理器] 资源加载成功");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[资源管理器] 加载资源时出错: {e.Message}");
        }
    }
    
    /// <summary>
    /// 清除所有保存的资源数据
    /// </summary>
    public void ClearSavedData()
    {
        // 删除所有资源相关的PlayerPrefs键
        foreach (ResourceType type in Enum.GetValues(typeof(ResourceType)))
        {
            PlayerPrefs.DeleteKey($"Resource_{type}");
        }
        
        PlayerPrefs.DeleteKey("Resources_SaveTime");
        PlayerPrefs.Save();
        
        // 重置内存中的资源
        foreach (ResourceItem item in resources)
        {
            item.count = 0;
        }
        
        if (debugMode) Debug.Log("[资源管理器] 已清除所有保存的资源数据");
    }
    
    /// <summary>
    /// 重置所有资源为默认值
    /// </summary>
    public void ResetAllResources(int defaultAmount = 0)
    {
        foreach (ResourceItem item in resources)
        {
            int oldCount = item.count;
            item.count = defaultAmount;
            OnResourceChanged?.Invoke(item.type, oldCount, defaultAmount);
        }
        
        if (debugMode) Debug.Log($"[资源管理器] 已重置所有资源为 {defaultAmount}");
    }
    
    #endregion
    
    #region 调试和工具方法
    
    /// <summary>
    /// 打印所有资源状态
    /// </summary>
    public void PrintAllResources()
    {
        Debug.Log("=== 当前资源状态 ===");
        foreach (ResourceItem item in resources.OrderBy(r => r.type))
        {
            Debug.Log($"{item.type}: {item.count}/{item.maxCapacity}");
        }
        Debug.Log("==================");
    }
    
    /// <summary>
    /// 获取资源总数量统计
    /// </summary>
    public int GetTotalResourcesCount()
    {
        return resources.Sum(r => r.count);
    }
    
    /// <summary>
    /// 获取指定资源的百分比
    /// </summary>
    public float GetResourcePercentage(ResourceType type)
    {
        ResourceItem item = resources.Find(r => r.type == type);
        if (item == null || item.maxCapacity == 0) return 0f;
        return (float)item.count / item.maxCapacity;
    }
    
    /// <summary>
    /// 设置资源的最大容量
    /// </summary>
    public void SetResourceCapacity(ResourceType type, int newCapacity)
    {
        ResourceItem item = resources.Find(r => r.type == type);
        if (item != null)
        {
            item.maxCapacity = Mathf.Max(1, newCapacity);
            if (item.count > item.maxCapacity)
            {
                item.count = item.maxCapacity;
            }
        }
    }
    
    #endregion
    
    #region UI相关方法
    
    /// <summary>
    /// 获取资源的显示名称
    /// </summary>
    public string GetResourceDisplayName(ResourceType type)
    {
        return type switch
        {
            ResourceType.Money => "金币",
            ResourceType.watermelonJ => "西瓜汁",
            ResourceType.orangeJ => "橙汁",
            ResourceType.tomatoJ => "番茄汁",
            ResourceType.mushroom => "蘑菇",
            _ => type.ToString()
        };
    }
    
    /// <summary>
    /// 获取资源的图标名称（用于UI）
    /// </summary>
    public string GetResourceIconName(ResourceType type)
    {
        return $"Icon_{type}";
    }
    
    /// <summary>
    /// 获取资源的描述
    /// </summary>
    public string GetResourceDescription(ResourceType type)
    {
        return type switch
        {
            ResourceType.Money => "通用货币，可用于购买各种物品",
            ResourceType.watermelonJ => "清凉解渴的西瓜汁，恢复生命值",
            ResourceType.orangeJ => "富含维生素C的橙汁，增加攻击力",
            ResourceType.tomatoJ => "酸甜可口的番茄汁，提高防御力",
            ResourceType.mushroom => "神奇的蘑菇，用于制作特殊药剂",
            _ => "未定义的资源"
        };
    }
    
    #endregion
    
    // 保存和加载游戏状态
    private void OnApplicationQuit()
    {
        SaveResources();
    }
    
    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            SaveResources();
        }
    }
}
