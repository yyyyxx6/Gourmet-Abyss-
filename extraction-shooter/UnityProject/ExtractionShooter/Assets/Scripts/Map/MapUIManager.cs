using Game.Core;
using UnityEngine;
using System.Collections.Generic;
using UnityEngine.UI;

[System.Serializable]
public class MapData
{
    public string mapID;
    public string mapName;
    public bool isUnlocked; // 是否解锁
    public List<RegionData> regions; // 该地图对应的3个区域

    public MapData(string id, string name, bool unlocked)
    {
        mapID = id;
        mapName = name;
        isUnlocked = unlocked;
        regions = new List<RegionData>();
    }
}

[System.Serializable]
public class RegionData
{
    public string regionID;
    public string regionName;
    public bool isUnlocked; // 是否解锁
    public string sceneName; // 对应的场景名称
    public List<CreatureData> creatures; // 生物列表
    public List<ProductData> products; // 物产列表

    public RegionData(string id, string name, bool unlocked, string scene)
    {
        regionID = id;
        regionName = name;
        isUnlocked = unlocked;
        sceneName = scene;
        creatures = new List<CreatureData>();
        products = new List<ProductData>();
    }
}

[System.Serializable]
public class CreatureData
{
    public string creatureID;
    public string creatureName;
    public Sprite avatar; // 头像图片
    public string description;
}

[System.Serializable]
public class ProductData
{
    public string productID;
    public string productName;
    public Sprite avatar; // 头像图片
    public string description;
}

// 解锁状态快速查看数据结构
[System.Serializable]
public class UnlockStatus
{
    public Dictionary<string, bool> mapUnlockStatus = new Dictionary<string, bool>();
    public Dictionary<string, Dictionary<string, bool>> regionUnlockStatus = new Dictionary<string, Dictionary<string, bool>>();

    public UnlockStatus() { }

    public UnlockStatus(List<MapData> allMaps)
    {
        foreach (var map in allMaps)
        {
            mapUnlockStatus[map.mapID] = map.isUnlocked;

            var regionStatus = new Dictionary<string, bool>();
            foreach (var region in map.regions)
            {
                regionStatus[region.regionID] = region.isUnlocked;
            }
            regionUnlockStatus[map.mapID] = regionStatus;
        }
    }

    public override string ToString()
    {
        string result = "解锁状态概览:\n";

        foreach (var mapEntry in mapUnlockStatus)
        {
            result += $"\n地图 [{mapEntry.Key}]: {(mapEntry.Value ? "已解锁" : "未解锁")}";

            if (regionUnlockStatus.ContainsKey(mapEntry.Key))
            {
                foreach (var regionEntry in regionUnlockStatus[mapEntry.Key])
                {
                    result += $"\n  - 区域 [{regionEntry.Key}]: {(regionEntry.Value ? "已解锁" : "未解锁")}";
                }
            }
        }

        return result;
    }
}

public class MapUIManager : MonoSingleton<MapUIManager>
{
    // 每个场景各一份，后加载的接管——沿用原来的裸赋值语义。
    protected override DuplicatePolicy Duplicate => DuplicatePolicy.OverwriteReference;

    [Header("UI References")]
    [SerializeField] private Transform mapContent; // 地图Item的父对象
    [SerializeField] private Transform regionContent; // 区域Item的父对象
    [SerializeField] private Transform creatureContent; // 生物显示区域
    [SerializeField] private Transform productContent; // 物产显示区域

    [Header("Prefabs")]
    [SerializeField] private GameObject mapItemPrefab;
    [SerializeField] private GameObject regionItemPrefab;
    [SerializeField] private GameObject creatureItemPrefab;
    [SerializeField] private GameObject productItemPrefab;

    [Header("当前选择")]
    [SerializeField] private MapData currentSelectedMap;
    [SerializeField] private RegionData currentSelectedRegion;

    [Header("调试")]
    [SerializeField] private bool showDebugLog = true;
    [SerializeField] private UnlockStatus currentUnlockStatus;

    // Item对象缓存
    private Dictionary<string, MapItemUI> mapItems = new Dictionary<string, MapItemUI>();
    private Dictionary<string, RegionItemUI> regionItems = new Dictionary<string, RegionItemUI>();

    // 当前选中的Item
    private MapItemUI currentSelectedMapItem;
    private RegionItemUI currentHoveredRegionItem;
    private void Start()
    {
        InitializeMapUI();
    }

    // 初始化地图UI
    private void InitializeMapUI()
    {
        // 清空现有地图UI
        ClearMapItems();

        // 清空字典，避免重复键
        mapItems.Clear();

        ClearRegionItems();
        regionItems.Clear();

        // 获取所有地图数据
        List<MapData> allMaps = MapDataManager.Instance.GetAllMaps();

        // 更新解锁状态
        UpdateUnlockStatus();

        // 生成地图Item
        foreach (var mapData in allMaps)
        {
            GameObject mapItemObj = Instantiate(mapItemPrefab, mapContent);
            MapItemUI mapItemUI = mapItemObj.GetComponent<MapItemUI>();

            if (mapItemUI != null)
            {
                // 添加前先检查是否已存在相同 key
                if (!mapItems.ContainsKey(mapData.mapID))
                {
                    mapItemUI.Initialize(mapData, this);
                    mapItems.Add(mapData.mapID, mapItemUI);
                }
                else
                {
                    Debug.LogWarning($"地图ID重复: {mapData.mapID}");
                }
            }
        }

        // 选中第一个地图
        if (allMaps.Count > 0)
        {
            SelectMap(allMaps[0]);
        }
    }

    // 选择地图
    public void SelectMap(MapData mapData)
    {
        // 如果地图未解锁，不能选择
        if (!mapData.isUnlocked) return;

        // 取消之前选中的地图
        if (currentSelectedMapItem != null)
        {
            currentSelectedMapItem.SetSelected(false);
        }

        // 设置当前选中的地图
        currentSelectedMap = mapData;

        // 更新UI
        if (mapItems.TryGetValue(mapData.mapID, out MapItemUI mapItem))
        {
            mapItem.SetSelected(true);
            currentSelectedMapItem = mapItem;
        }

        // 更新区域显示
        UpdateRegionDisplay();
    }

    // 更新区域显示
    private void UpdateRegionDisplay()
    {
        if (currentSelectedMap == null) return;

        // 清空现有区域
        ClearRegionItems();
        regionItems.Clear();
        currentHoveredRegionItem = null;
        ClearDetailDisplay();

        // 生成区域Item
        foreach (var regionData in currentSelectedMap.regions)
        {
            GameObject regionItemObj = Instantiate(regionItemPrefab, regionContent);
            RegionItemUI regionItemUI = regionItemObj.GetComponent<RegionItemUI>();

            if (regionItemUI != null)
            {
                regionItemUI.Initialize(regionData, this);
                regionItems.Add(regionData.regionID, regionItemUI);
            }
        }
    }

    // 鼠标经过区域
    public void OnRegionHover(RegionData regionData)
    {
        // 如果区域未解锁，不显示详情
        if (!regionData.isUnlocked) return;

        // 更新当前悬停的区域
        if (currentHoveredRegionItem != null)
        {
            currentHoveredRegionItem.SetHovered(false);
        }

        if (regionItems.TryGetValue(regionData.regionID, out RegionItemUI regionItem))
        {
            regionItem.SetHovered(true);
            currentHoveredRegionItem = regionItem;

            // 更新右下角详情显示
            UpdateDetailDisplay(regionData);
        }
    }

    // 鼠标离开区域
    public void OnRegionHoverEnd(RegionData regionData)
    {
        if (regionItems.TryGetValue(regionData.regionID, out RegionItemUI regionItem))
        {
            regionItem.SetHovered(false);

            // 如果离开的是当前悬停的区域，重置悬停状态
            if (currentHoveredRegionItem == regionItem)
            {
                currentHoveredRegionItem = null;
            }
        }
    }

    // 点击进入区域
    public void EnterRegion(RegionData regionData)
    {
        if (!regionData.isUnlocked)
        {
           // Debug.Log("区域未解锁！");
            return;
        }

        //Debug.Log($"进入区域: {regionData.regionName}");

        // 在这里加载场景
        if (!string.IsNullOrEmpty(regionData.sceneName))
        {
            if (LevelManager.instance == null || !LevelManager.instance.TryEnterLevel(regionData.sceneName))
                return;
            GameObject.FindGameObjectWithTag("Player").GetComponent<TopDownController>().enabled = false;
            //UnityEngine.SceneManagement.SceneManager.LoadScene(regionData.sceneName, UnityEngine.SceneManagement.LoadSceneMode.Additive);
            // 统一通过 HomeCavecar 关闭，避免仅隐藏对象导致 isUIActive 状态残留
            HomeCavecar.homeCavecar?.CloseMapUI();
        }
    }

    // 更新详情显示
    private void UpdateDetailDisplay(RegionData regionData)
    {
        ClearDetailDisplay();

        // 显示生物
        foreach (var creature in regionData.creatures)
        {
            GameObject creatureObj = Instantiate(creatureItemPrefab, creatureContent);
            Image avatarImg = creatureObj.GetComponentInChildren<Image>();
            if (avatarImg != null && creature.avatar != null)
            {
                avatarImg.sprite = creature.avatar;
            }

            // 添加悬停显示详细信息的功能
            CreatureItemDisplay display = creatureObj.AddComponent<CreatureItemDisplay>();
            display.Initialize(creature);
        }

        // 显示物产
        foreach (var product in regionData.products)
        {
            GameObject productObj = Instantiate(productItemPrefab, productContent);
            Image avatarImg = productObj.GetComponentInChildren<Image>();
            if (avatarImg != null && product.avatar != null)
            {
                avatarImg.sprite = product.avatar;
            }

            // 添加悬停显示详细信息的功能
            ProductItemDisplay display = productObj.AddComponent<ProductItemDisplay>();
            display.Initialize(product);
        }
    }

    // 清空详情显示
    private void ClearDetailDisplay()
    {
        foreach (Transform child in creatureContent)
        {
            Destroy(child.gameObject);
        }

        foreach (Transform child in productContent)
        {
            Destroy(child.gameObject);
        }
    }

    // 清空地图Item
    private void ClearMapItems()
    {
        foreach (Transform child in mapContent)
        {
            Destroy(child.gameObject);
        }
    }

    // 清空区域Item
    private void ClearRegionItems()
    {
        foreach (Transform child in regionContent)
        {
            Destroy(child.gameObject);
        }
    }

    // 更新解锁状态数据结构
    public void UpdateUnlockStatus()
    {
        List<MapData> allMaps = MapDataManager.Instance.GetAllMaps();
        currentUnlockStatus = new UnlockStatus(allMaps);

        if (showDebugLog)
        {
            //Debug.Log(currentUnlockStatus.ToString());
        }
    }

    // 获取当前解锁状态
    public UnlockStatus GetUnlockStatus()
    {
        return currentUnlockStatus;
    }

    // 刷新UI显示
    public void RefreshUI()
    {
        ClearMapItems();
        ClearRegionItems();
        InitializeMapUI();
    }
}
