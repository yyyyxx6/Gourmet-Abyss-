using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public enum LootStorageMode
{
    UseLegacyConfiguration = 0,
    SlotInventory = 1,
    RunIngredientBag = 2,
    PermanentResource = 3
}

public class LootCollector : MonoBehaviour
{
    [Header("收集设置")]
    [SerializeField] private float initialDelay = 0.3f;  // 初始延迟时间
    [SerializeField] private float rotationSpeed = 180f; // 旋转速度
    [SerializeField] private float floatAmplitude = 0.5f; // 浮动幅度
    [SerializeField] private float floatFrequency = 2f;  // 浮动频率
    [SerializeField] private float randomStartRotation = 360f; // 随机起始旋转角度范围

    [Header("飞行设置")]
    [SerializeField] private float flySpeed = 15f;       // 飞行速度
    [SerializeField] private float flyDelay = 1.5f;     // 开始飞行的延迟时间
    [SerializeField] private AnimationCurve flyAccelerationCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
    [SerializeField] private float maxFlySpeed = 25f;    // 最大飞行速度
    [SerializeField] private float accelerationDuration = 0.8f; // 加速时间
    [SerializeField] private float attractionForce = 5f; // 引力大小
    [SerializeField] private float attractionRadius = 3f; // 引力半径

    [Header("销毁设置")]
    [SerializeField] private float destroyTimeout = 10f;  // Total lifetime from spawn.
    [Tooltip("Seconds of blinking before the loot expires. Included in Destroy Timeout.")]
    [SerializeField] private float expiryWarningDuration = 3f;
    [Tooltip("Visibility toggle interval during the expiry warning.")]
    [SerializeField] private float expiryBlinkInterval = 0.15f;

    [Header("特效设置")]
    [SerializeField] private GameObject collectEffectPrefab; // 收集特效
    [SerializeField] private AudioClip collectSound;         // 收集音效

    [Header("资源设置")]
    [SerializeField] private ResourceType resourceType = ResourceType.Money; // 资源类型
    [SerializeField] private int resourceAmount = 1;                         // 资源数量
    [SerializeField] private bool useCustomResource = false;                 // 是否使用自定义资源设置

    [Header("植物设置")]
    [Tooltip("是否为植物类资源")]
    [HideInInspector]
    [SerializeField] private bool isPlantResource = false; // 是否为植物类资源
    [Tooltip("Where this pickup is stored. Legacy preserves existing prefab behavior.")]
    [SerializeField] private LootStorageMode storageMode = LootStorageMode.UseLegacyConfiguration;
    [HideInInspector]
    [SerializeField] private bool plantDirectToGameVal = true; // Legacy compatibility only.

    [Header("背包已满设置")]
    [Tooltip("背包满时重试检测间隔（秒）")]
    [SerializeField] private float fullRetryInterval = 0.5f;
    [Tooltip("背包满提示消息冷却（秒），防止刷屏")]
    [SerializeField] private float fullMessageCooldown = 1.5f;
    [SerializeField] private string fullInventoryMessage = "背包已满，无法收集";

    private Transform player;
    private Rigidbody rb;
    private Collider col;
    private Vector3 startPosition;
    private Vector3 initialScale;
    private bool collectionResolved;
    private float spawnTime;
    private float timeSinceLastInteraction = 0f;
    private bool isReadyToCollect = false;
    private bool isFlyingToPlayer = false;
    private float currentFlySpeed = 0f;
    private Vector3 floatingOffset = Vector3.zero;
    private bool canBeCollected = false; // 标记是否可以收集
    private bool playerInTrigger = false; // 标记玩家是否在触发器中
    private Coroutine waitForCollectionCoroutine; // 等待收集的协程
    private bool isInventoryFull = false; // 标记背包是否已满
    private float lastFullMessageTime = -999f;
    private Renderer[] cachedRenderers;
    private bool[] rendererInitialStates;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        startPosition = transform.position;
        initialScale = transform.localScale;
        spawnTime = Time.time;
        timeSinceLastInteraction = Time.time;

        cachedRenderers = GetComponentsInChildren<Renderer>(true);
        rendererInitialStates = new bool[cachedRenderers.Length];
        for (int i = 0; i < cachedRenderers.Length; i++)
            rendererInitialStates[i] = cachedRenderers[i] != null && cachedRenderers[i].enabled;

        // 随机浮动偏移
        floatingOffset = new Vector3(
            Random.Range(0f, 360f),
            Random.Range(0f, 360f),
            Random.Range(0f, 360f)
        );
        
        // 添加随机起始旋转角度
        ApplyRandomStartRotation();
    }

    private void Start()
    {
        // 找到玩家
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        if (playerObject != null)
        {
            player = playerObject.transform;
        }
        else
        {
            Debug.LogWarning("LootCollector: 未找到标签为'Player'的对象");
        }

        // 初始延迟后可以收集
        StartCoroutine(InitiateCollection());
    }

    private void Update()
    {
        if (!isReadyToCollect) return;

        // 旋转效果
        if (!isFlyingToPlayer)
        {
            transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);

            // 浮动效果
            float floatY = Mathf.Sin((Time.time + floatingOffset.x) * floatFrequency) * floatAmplitude;
            Vector3 newPosition = startPosition + new Vector3(0, floatY, 0);
            transform.position = newPosition;

            // 兜底：物体生成时玩家已在范围内，或 Update 直接改位置导致 OnTriggerEnter 未触发
            if (!canBeCollected && !isFlyingToPlayer)
                TryBeginCollectionFromPlayerOverlap();
        }
        else if (player != null && isFlyingToPlayer)
        {
            FlyToPlayer();
        }

        // Lifetime is based on spawn time and is never reset by trigger interaction.
        if (!isFlyingToPlayer)
            CheckDestroyTimeout();
    }

    // 添加随机起始旋转
    private void ApplyRandomStartRotation()
    {
        // 在Y轴上生成随机旋转角度
        float randomYRotation = Random.Range(0f, randomStartRotation);
        
        // 应用旋转
        transform.rotation = Quaternion.Euler(0f, randomYRotation, 0f);
        
        // 如果需要更随机的三维旋转，可以取消下面的注释
        // float randomXRotation = Random.Range(0f, randomStartRotation);
        // float randomZRotation = Random.Range(0f, randomStartRotation);
        // transform.rotation = Quaternion.Euler(randomXRotation, randomYRotation, randomZRotation);
    }

    // 检查销毁超时
    private void CheckDestroyTimeout()
    {
        if (destroyTimeout <= 0f) return;

        float age = Time.time - spawnTime;
        if (age >= destroyTimeout)
        {
            Debug.Log($"物品存在时间超过{destroyTimeout}秒，自动销毁: {resourceType}");
            Destroy(gameObject);
            return;
        }

        float warningStart = Mathf.Max(0f, destroyTimeout - Mathf.Max(0f, expiryWarningDuration));
        if (age < warningStart)
        {
            RestoreRendererVisibility();
            return;
        }

        float interval = Mathf.Max(0.03f, expiryBlinkInterval);
        bool visible = Mathf.FloorToInt((age - warningStart) / interval) % 2 == 0;
        SetRendererVisibility(visible);
    }

    // 设置掉落物的资源类型和数量
    public void SetResourceInfo(ResourceType type, int amount)
    {
        resourceType = type;
        resourceAmount = amount;
    }

    public void SetResourceInfo(ResourceType type, int amount, LootStorageMode targetStorageMode)
    {
        resourceType = type;
        resourceAmount = amount;
        storageMode = targetStorageMode;
    }

    private IEnumerator InitiateCollection()
    {
        // 初始延迟
        yield return new WaitForSeconds(initialDelay);
        isReadyToCollect = true;
        timeSinceLastInteraction = Time.time;

        TryBeginCollectionFromPlayerOverlap();
    }

    private static bool IsPlayerCollider(Collider other)
    {
        if (other == null) return false;
        if (other.CompareTag("Player")) return true;
        if (other.attachedRigidbody != null && other.attachedRigidbody.CompareTag("Player")) return true;
        return other.transform.root != null && other.transform.root.CompareTag("Player");
    }

    private static bool IsPlayerDead(Collider other)
    {
        if (other == null) return false;

        TopDownController ctrl = other.GetComponent<TopDownController>();
        if (ctrl == null && other.attachedRigidbody != null)
            ctrl = other.attachedRigidbody.GetComponent<TopDownController>();
        if (ctrl == null && other.transform.root != null)
            ctrl = other.transform.root.GetComponent<TopDownController>();

        return ctrl != null && ctrl.isDead;
    }

    private void TryBeginCollectionFromPlayerOverlap()
    {
        if (!isReadyToCollect || isFlyingToPlayer || canBeCollected || col == null)
            return;

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
                player = playerObject.transform;
        }

        if (player == null)
            return;

        Vector3 closest = col.ClosestPoint(player.position);
        if (Vector3.Distance(closest, player.position) > 0.35f)
            return;

        BeginCollectionForPlayer();
    }

    private void BeginCollectionForPlayer()
    {
        if (!CanPlayerCollect() || collectionResolved) return;
        playerInTrigger = true;
        timeSinceLastInteraction = Time.time;

        if (UsesNoSlotStorage())
        {
            canBeCollected = true;
            StartFlyToPlayer();
            return;
        }

        bool hasSpace = CheckInventorySpace();
        isInventoryFull = !hasSpace;

        if (hasSpace)
        {
            canBeCollected = true;
            StartFlyToPlayer();
            return;
        }

        ShowFullInventoryMessage();
        if (waitForCollectionCoroutine != null)
            StopCoroutine(waitForCollectionCoroutine);
        waitForCollectionCoroutine = StartCoroutine(WaitForCollectionInTrigger());
    }

    // 当玩家进入触发器
    private void OnTriggerEnter(Collider other)
    {
        if (!isReadyToCollect || isFlyingToPlayer || canBeCollected) return;
        if (!IsPlayerCollider(other) || IsPlayerDead(other)) return;

        BeginCollectionForPlayer();
    }

    private void OnTriggerStay(Collider other)
    {
        if (!isReadyToCollect || isFlyingToPlayer || canBeCollected) return;
        if (!IsPlayerCollider(other) || IsPlayerDead(other)) return;

        BeginCollectionForPlayer();
    }

    // 当玩家离开触发器
    private void OnTriggerExit(Collider other)
    {
        if (IsPlayerCollider(other))
        {
            playerInTrigger = false;

            // 停止等待重试的协程
            if (waitForCollectionCoroutine != null)
            {
                StopCoroutine(waitForCollectionCoroutine);
                waitForCollectionCoroutine = null;
            }

            // 如果还没开始飞行，重置时间戳
            if (!isFlyingToPlayer)
            {
                timeSinceLastInteraction = Time.time;
            }
        }
    }

    // 在触发器内等待收集
    private IEnumerator WaitForCollectionInTrigger()
    {
        while (playerInTrigger && !canBeCollected && !collectionResolved)
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, fullRetryInterval));

            if (!CanPlayerCollect()) continue;

            bool hasSpace = CheckInventorySpace();
            isInventoryFull = !hasSpace;
            
            if (hasSpace)
            {
                canBeCollected = true;
                StartFlyToPlayer();
                yield break;
            }
            else
            {
                ShowFullInventoryMessage();
            }
        }
    }

    private LootStorageMode GetEffectiveStorageMode()
    {
        switch (ResourceStorageRules.GetCategory(resourceType))
        {
            case RunResourceCategory.Ingredient:
                return LootStorageMode.SlotInventory;
            case RunResourceCategory.Gathered:
                return LootStorageMode.RunIngredientBag;
            case RunResourceCategory.Permanent:
                return LootStorageMode.PermanentResource;
            default:
                return storageMode == LootStorageMode.UseLegacyConfiguration
                    ? LootStorageMode.SlotInventory
                    : storageMode;
        }
    }

    private bool UsesNoSlotStorage()
    {
        LootStorageMode mode = GetEffectiveStorageMode();
        return mode == LootStorageMode.RunIngredientBag || mode == LootStorageMode.PermanentResource;
    }

    // 检查背包空间
    private bool CheckInventorySpace()
    {
        if (resourceAmount <= 0 || ResourceStorageRules.GetCategory(resourceType) == RunResourceCategory.Invalid)
            return false;
        InventoryManager inventoryManager = InventoryManager.instance;
        LootStorageMode mode = GetEffectiveStorageMode();
        if (mode == LootStorageMode.RunIngredientBag)
            return inventoryManager != null && inventoryManager.GetRunGatheredCount(resourceType) < int.MaxValue;
        if (mode == LootStorageMode.PermanentResource)
        {
            ResourceItem resource = GameValManager.Instance?.GetResourceInfo(resourceType);
            return resource != null && resource.count < resource.maxCapacity;
        }
        if (inventoryManager != null)
        {
            return inventoryManager.CanAddItem(resourceType, 1);
        }
        return false;
    }

    private void StartFlyToPlayer()
    {
        RestoreRendererVisibility();
        isFlyingToPlayer = true;
        currentFlySpeed = flySpeed;

        // 禁用物理效果
        if (rb != null)
        {
            rb.isKinematic = true;
        }

        if (col != null)
        {
            col.isTrigger = true;
        }
    }

    private void SetRendererVisibility(bool visible)
    {
        if (cachedRenderers == null) return;
        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            if (cachedRenderers[i] != null)
                cachedRenderers[i].enabled = visible && rendererInitialStates[i];
        }
    }

    private void RestoreRendererVisibility()
    {
        if (cachedRenderers == null) return;
        for (int i = 0; i < cachedRenderers.Length; i++)
        {
            if (cachedRenderers[i] != null)
                cachedRenderers[i].enabled = rendererInitialStates[i];
        }
    }

    private void FlyToPlayer()
    {
        if (!CanPlayerCollect())
        {
            ResetCollectionForRetry();
            return;
        }
        Vector3 direction = (player.position - transform.position).normalized;
        float distance = Vector3.Distance(transform.position, player.position);

        // 加速效果
        float timeSinceStartFlying = Time.time - (spawnTime + flyDelay);
        float accelerationFactor = flyAccelerationCurve.Evaluate(Mathf.Clamp01(timeSinceStartFlying / accelerationDuration));
        currentFlySpeed = Mathf.Lerp(flySpeed, maxFlySpeed, accelerationFactor);

        // 计算引力效果
        float attractionFactor = 1f;
        if (distance < attractionRadius)
        {
            attractionFactor = 1f + (attractionRadius - distance) / attractionRadius * attractionForce;
        }

        // 移动
        Vector3 movement = direction * currentFlySpeed * attractionFactor * Time.deltaTime;
        transform.position += movement;

        // 旋转面向移动方向
        if (movement.magnitude > 0.1f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(movement);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 10f);
        }

        // 缩小效果（接近时）
        float distanceScaleFactor = Mathf.Clamp01(distance / 2f);
        transform.localScale = initialScale * distanceScaleFactor;

        // 接近玩家时检测收集
        if (distance < 0.5f)
        {
            Collect();
        }
    }

    // 添加碰撞检测，以确保即使没有进入触发器也能检测到与玩家的碰撞
    private void OnCollisionEnter(Collision collision)
    {
        // 添加碰撞检测，确保在非飞行状态下也能收集
        if (!isReadyToCollect || !canBeCollected || isFlyingToPlayer) return;

        if (collision.gameObject.CompareTag("Player"))
        {
            Collect();
        }
    }

    private void Collect()
    {
        if (collectionResolved || !CanPlayerCollect() || resourceAmount <= 0) return;
        if (ResourceStorageRules.GetCategory(resourceType) == RunResourceCategory.Invalid) return;
        // Destroy is deferred until the end of the frame; block duplicate trigger callbacks now.
        collectionResolved = true;
        LootStorageMode effectiveStorageMode = GetEffectiveStorageMode();
        int accepted = 0;
        InventoryManager inventory = InventoryManager.instance;
        if (effectiveStorageMode == LootStorageMode.SlotInventory)
        {
            if (inventory != null) accepted = inventory.AddItemPartial(resourceType, resourceAmount);
        }
        else if (effectiveStorageMode == LootStorageMode.RunIngredientBag)
        {
            if (inventory != null) accepted = inventory.AddRunGathered(resourceType, resourceAmount);
        }
        else if (effectiveStorageMode == LootStorageMode.PermanentResource && GameValManager.Instance != null)
        {
            int before = GameValManager.Instance.GetResourceCount(resourceType);
            ResourceItem info = GameValManager.Instance.GetResourceInfo(resourceType);
            int amount = info == null ? 0 : Mathf.Min(resourceAmount, Mathf.Max(0, info.maxCapacity - before));
            if (amount > 0)
            {
                GameValManager.Instance.AddResource(resourceType, amount);
                accepted = GameValManager.Instance.GetResourceCount(resourceType) - before;
            }
        }

        accepted = Mathf.Clamp(accepted, 0, resourceAmount);
        resourceAmount -= accepted;
        if (accepted > 0)
        {
            if (collectEffectPrefab != null)
                Instantiate(collectEffectPrefab, transform.position, Quaternion.identity);
            if (collectSound != null)
                AudioSource.PlayClipAtPoint(collectSound, transform.position);
            TriggerPlayerFeedback();
            AudioManager.Instance?.PlayAudio("2");
        }

        if (resourceAmount == 0)
        {
            Destroy(gameObject);
        }
        else
        {
            ShowFullInventoryMessage();
            ResetCollectionForRetry();
        }
    }

    private bool CanPlayerCollect()
    {
        if (player == null) return false;
        TopDownController controller = player.GetComponent<TopDownController>();
        if (controller != null && controller.isDead) return false;
        if (LevelManager.instance != null && LevelManager.instance.IsTransitioning()) return false;
        return PlayerStateManager.instance == null ||
            (PlayerStateManager.instance.currentState != PlayerState.UI &&
             PlayerStateManager.instance.currentState != PlayerState.Settlement);
    }

    private void ResetCollectionForRetry()
    {
        collectionResolved = false;
        canBeCollected = false;
        isFlyingToPlayer = false;
        transform.position = startPosition;
        transform.localScale = initialScale;
        if (waitForCollectionCoroutine != null) StopCoroutine(waitForCollectionCoroutine);
        waitForCollectionCoroutine = StartCoroutine(WaitAndRetryCollection());
    }

    // 等待并重试收集
    private IEnumerator WaitAndRetryCollection()
    {
        while (!canBeCollected && !collectionResolved)
        {
            yield return new WaitForSeconds(Mathf.Max(0.1f, fullRetryInterval));

            if (!CanPlayerCollect() || col == null ||
                Vector3.Distance(col.ClosestPoint(player.position), player.position) > 0.35f) continue;

            bool hasSpace = CheckInventorySpace();
            isInventoryFull = !hasSpace;
            
            if (hasSpace)
            {
                canBeCollected = true;
                StartFlyToPlayer();
                yield break;
            }
            else
            {
                ShowFullInventoryMessage();
            }
        }
    }

    private void ShowFullInventoryMessage()
    {
        if (UsesNoSlotStorage())
            return;

        if (Time.time - lastFullMessageTime < Mathf.Max(0.1f, fullMessageCooldown))
            return;

        lastFullMessageTime = Time.time;
        GlobalMessageUI.Show(fullInventoryMessage);
    }

    private void TriggerPlayerFeedback()
    {
        if (player != null)
        {
            PlayerFeedback playerFeedback = player.GetComponent<PlayerFeedback>();
            if (playerFeedback != null)
            {
                playerFeedback.OnItemCollected();
            }
        }
    }

    // 在编辑器模式下绘制引力半径
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, attractionRadius);
    }
}
