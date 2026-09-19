using UnityEngine;
using GourmetAbyss.CameraSystem;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
public class EnemyHealth : MonoBehaviour
{
    public enum HealthBarType
    {
        Monster,
        Gatherable
    }

    [Header("生命值设置")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth;
    
    [Header("进度条UI设置")]
    [SerializeField] private RectTransform whiteBar;      // 白色进度条RectTransform
    [SerializeField] private RectTransform redBar;       // 红色进度条RectTransform
    [SerializeField] private GameObject healthBarParent;  // 血量条父物体
    [SerializeField] private float maxBarWidth = 200f;   // 进度条最大宽度
    [SerializeField] private float fillDelay = 0.5f;     // 白色到红色的延迟时间
    [SerializeField] private float fillDuration = 0.3f;  // 红色填充持续时间
    
    [Header("血量条隐藏设置")]
    [SerializeField] private float hideDelay = 2f;       // 停止受击后隐藏血量条的延迟时间
    
    [Header("受击反馈设置")]
    [SerializeField] private float flashDuration = 0.1f;
    [SerializeField] private Color flashColor = Color.white;
    [SerializeField] private float scaleMultiplier = 1.2f;
    [SerializeField] private float scaleDuration = 0.15f;
    [SerializeField] private GameObject hitParticlePrefab;
    [SerializeField] private Transform hitParticleSpawnPoint;
    
    [Header("死亡效果设置")]
    [SerializeField] private GameObject explosionEffectPrefab; // 爆炸特效预制体
    [SerializeField] private float deathEffectDelay = 0.2f;   // 死亡后多久播放爆炸特效
    [SerializeField] private float destroyDelayAfterDeath = 0.1f; // 死亡后多久销毁自身
    
    [Header("死亡击退设置")]
    [SerializeField] private float deathKnockbackDistance = 1.5f; // 击退距离
    [SerializeField] private float deathKnockbackDuration = 0.3f; // 击退持续时间
    [SerializeField] private AnimationCurve deathKnockbackCurve = AnimationCurve.EaseInOut(0, 0, 1, 1); // 击退曲线
    
    [Header("屏幕震动设置")]
    [SerializeField] private float shakeDuration = 0.1f;
    [SerializeField] private float shakeMagnitude = 0.1f;
    
    [Header("动画设置")]
    [SerializeField] private Animator animator;
    [SerializeField] private string hitAnimationTrigger = "Hit";
    
    [Header("掉落物设置")]
    [SerializeField] private LootItem[] lootItems; // 可掉落的物品列表
    private string runtimeLootConfigID;
    private bool lootConfigApplied;
    
    [System.Serializable]
    public class LootItem
    {
        public GameObject itemPrefab;    // 掉落物预制体
        [Range(0f, 1f)]
        public float dropChance = 0.3f; // 掉落概率 (0-1)
        public int minAmount = 1;       // 最小掉落数量
        public int maxAmount = 3;       // 最大掉落数量
        public float scatterForce = 5f; // 散射力
        
        // 新增：资源类型和数量
        public ResourceType resourceType = ResourceType.Money;
        public int resourceMinAmount = 1;
        public int resourceMaxAmount = 1;
        
        // 新增：资源数量计算
        public int GetRandomResourceAmount()
        {
            return Random.Range(resourceMinAmount, resourceMaxAmount + 1);
        }
    }
    
    private List<Renderer> allRenderers = new List<Renderer>();
    private Dictionary<Renderer, MaterialPropertyBlock[]> originalPropertyBlocks = new Dictionary<Renderer, MaterialPropertyBlock[]>();
    private readonly List<SpriteRenderer> spriteRenderersForFlash = new List<SpriteRenderer>();
    private readonly Dictionary<SpriteRenderer, Color> originalSpriteColors = new Dictionary<SpriteRenderer, Color>();
    private Vector3 originalScale;
    private CameraFollow cameraFollow;
    private EnemyAI enemyAI;
    private BossAI bossAI;
    private Vector3 lastHitDirection; // 记录最后一次被击中的方向
    private Vector3 lastHitPoint; // 记录最后一次击中点
    private bool isDead = false;

    public bool IsDead => isDead;
    
    // 进度条相关变量
    [SerializeField] private HealthBarType healthBarType = HealthBarType.Monster;
    [SerializeField] private Color monsterHealthColor = new Color(0.86f, 0.34f, 0.34f, 1f);
    [SerializeField] private Color gatherableHealthColor = new Color(0.55f, 0.55f, 0.55f, 1f);

    private Coroutine redFillCoroutine;
    
    // 血量条隐藏相关变量
    private Coroutine hideCoroutine;
    
    private void Awake()
    {
        currentHealth = maxHealth;
        originalScale = transform.localScale;
        
        GetAllRenderers();
        
        if (hitParticleSpawnPoint == null)
            hitParticleSpawnPoint = transform;
            
        if (animator == null)
            animator = GetComponent<Animator>();
            
        cameraFollow = FindFirstObjectByType<CameraFollow>();
        enemyAI = GetComponent<EnemyAI>();
        bossAI = GetComponent<BossAI>();
        
        // 初始化进度条
        InitializeHealthBar();
    }
    
    private void InitializeHealthBar()
    {
        if (whiteBar != null && redBar != null)
        {
            // 设置锚点和轴心为中间
            SetAnchorAndPivotToCenter(whiteBar);
            SetAnchorAndPivotToLeft(redBar);
            ApplyHealthBarColor();
            
            // 初始时进度条宽度为0
            SetBarWidth(whiteBar, maxBarWidth);
            SetBarWidth(redBar, maxBarWidth);
            
            // 初始隐藏进度条
            if (healthBarParent != null)
            {
                healthBarParent.SetActive(false);
            }
            else
            {
                // 如果没有指定父物体，隐藏两个进度条
                whiteBar.gameObject.SetActive(false);
                redBar.gameObject.SetActive(false);
            }
        }
    }
    
    // 设置RectTransform的锚点和轴心为中间
    private void SetAnchorAndPivotToCenter(RectTransform rectTransform)
    {
        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
    }
    
    // 设置进度条宽度
    private void SetBarWidth(RectTransform bar, float width)
    {
        if(width>maxBarWidth) width = maxBarWidth;
        bar.sizeDelta = new Vector2(width, bar.sizeDelta.y);
    }

    private void SetAnchorAndPivotToLeft(RectTransform rectTransform)
    {
        rectTransform.anchorMin = new Vector2(0f, 0.5f);
        rectTransform.anchorMax = new Vector2(0f, 0.5f);
        rectTransform.pivot = new Vector2(0f, 0.5f);
        rectTransform.anchoredPosition = new Vector2(0f, rectTransform.anchoredPosition.y);
    }

    private void ApplyHealthBarColor()
    {
        Image fillImage = redBar != null ? redBar.GetComponent<Image>() : null;
        if (fillImage != null)
        {
            fillImage.color = healthBarType == HealthBarType.Gatherable
                ? gatherableHealthColor
                : monsterHealthColor;
        }
    }
    
    // 获取当前宽度
    private float GetBarWidth(RectTransform bar)
    {
        return bar.sizeDelta.x;
    }
    
    // 显示血量条
    private void ShowHealthBar()
    {
        if (healthBarParent != null)
        {
            healthBarParent.SetActive(true);
        }
        else if (whiteBar != null && redBar != null)
        {
            whiteBar.gameObject.SetActive(true);
            redBar.gameObject.SetActive(true);
        }
    }
    
    // 隐藏血量条
    private void HideHealthBar()
    {
        // if (isDead) return; // 死亡时不隐藏血量条
        
        if (healthBarParent != null)
        {
            healthBarParent.SetActive(false);
        }
        else if (whiteBar != null && redBar != null)
        {
            whiteBar.gameObject.SetActive(false);
            redBar.gameObject.SetActive(false);
        }
    }
    
    // 重置血量条隐藏计时器
    private void ResetHideTimer()
    {
        // 先停止之前的隐藏协程
        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
        }
        
        // 显示血量条
        ShowHealthBar();
        
        // 开始新的隐藏计时
        hideCoroutine = StartCoroutine(StartHideTimer());
    }
    
    // 开始隐藏计时器
    private IEnumerator StartHideTimer()
    {
        yield return new WaitForSeconds(hideDelay);
        HideHealthBar();
    }
    
    // 更新进度条显示
    private void UpdateHealthBar()
    {
        if (whiteBar == null || redBar == null) return;
        
        // 白条固定为满，红条表示已损失血量
        SetBarWidth(whiteBar, maxBarWidth);

        // 计算损失的血量百分比
        float healthPercent = maxHealth > 0f
            ? Mathf.Clamp01(currentHealth / maxHealth)
            : 0f;
        float targetWidth = healthPercent * maxBarWidth;
        
        // 重置隐藏计时器
        ResetHideTimer();
        
        // 红条平滑增长到目标
        if (redFillCoroutine != null)
        {
            StopCoroutine(redFillCoroutine);
        }
        redFillCoroutine = StartCoroutine(FillRedBarToTarget(targetWidth));
    }

    // 红色条填充到目标宽度
    private IEnumerator FillRedBarToTarget(float targetWidth)
    {
        float startWidth = GetBarWidth(redBar);
        float elapsedTime = 0f;
        float duration = Mathf.Max(fillDuration, 0.01f);

        while (elapsedTime < duration)
        {
            elapsedTime += Time.deltaTime;
            float t = elapsedTime / duration;
            float currentWidth = Mathf.Lerp(startWidth, targetWidth, t);
            SetBarWidth(redBar, currentWidth);
            yield return null;
        }

        SetBarWidth(redBar, targetWidth);
        redFillCoroutine = null;
    }
    
    private void GetAllRenderers()
    {
        allRenderers.Clear();
        originalPropertyBlocks.Clear();
        
        Renderer[] childRenderers = GetComponentsInChildren<Renderer>(true);
        allRenderers.AddRange(childRenderers);
        
        foreach (var renderer in allRenderers)
        {
            int materialCount = renderer.sharedMaterials.Length;
            MaterialPropertyBlock[] propertyBlocks = new MaterialPropertyBlock[materialCount];
            
            for (int i = 0; i < materialCount; i++)
            {
                propertyBlocks[i] = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(propertyBlocks[i], i);
            }
            
            originalPropertyBlocks[renderer] = propertyBlocks;
        }
        
        spriteRenderersForFlash.Clear();
        originalSpriteColors.Clear();
        foreach (SpriteRenderer sr in GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr == null) continue;
            spriteRenderersForFlash.Add(sr);
            originalSpriteColors[sr] = sr.color;
        }
    }
    
    // 主要伤害处理方法
    public void TakeDamage(float damage, Vector3 hitPoint, Vector3 hitNormal)
    {
        if (currentHealth <= 0 || isDead)
        {
            print("AAA");
            HideHealthBar();
            return;
        } 
        
        currentHealth -= damage;
        
        // 更新进度条
        UpdateHealthBar();
        
        // 计算击中方向
        if (hitPoint != Vector3.zero)
        {
            lastHitDirection = (hitPoint - transform.position).normalized;
        }
        
        lastHitPoint = hitPoint;

        bool playHitAnim = true;
        if (bossAI != null)
            playHitAnim = bossAI.OnHit();
        else if (enemyAI != null)
            enemyAI.OnHit();

        StartCoroutine(HitFeedbackCoroutine(hitPoint, hitNormal, playHitAnim, spawnHitParticle: true));
        
        if (currentHealth <= 0)
        {
            Die();
        }
    }
    
    // 专门处理子弹击中的方法
    public void TakeDamageFromProjectile(float damage, Vector3 hitPoint, Vector3 hitNormal, Vector3 bulletPosition, Vector3 bulletDirection)
    {
        if (currentHealth <= 0 || isDead)
        {
            print("AAA");
            HideHealthBar();
            return;
        } 
        
        
        currentHealth -= damage;
        
        // 更新进度条
        UpdateHealthBar();
        
        // 记录子弹方向
        lastHitDirection = bulletDirection.normalized;
        lastHitPoint = hitPoint;
        
        // 调试：绘制子弹方向
        Debug.DrawRay(bulletPosition, bulletDirection * 2f, Color.red, 2f);
        Debug.DrawRay(hitPoint, lastHitDirection * 2f, Color.blue, 2f);

        bool playHitAnim = true;
        if (bossAI != null)
            playHitAnim = bossAI.OnHit();
        else if (enemyAI != null)
            enemyAI.OnHit();

        // 子弹命中特效由子弹决定（Projectile 会自行生成 impactVFX / criticalHitEffext）
        StartCoroutine(HitFeedbackCoroutine(hitPoint, hitNormal, playHitAnim, spawnHitParticle: false));
        
        if (currentHealth <= 0)
        {
            Die();
        }
    }

    /// <summary>
    /// 子弹伤害（带结果返回）：返回 true 表示这一发子弹造成了击杀。
    /// </summary>
    public bool TakeDamageFromProjectileWithResult(float damage, Vector3 hitPoint, Vector3 hitNormal, Vector3 bulletPosition, Vector3 bulletDirection)
    {
        if (currentHealth <= 0 || isDead)
        {
            HideHealthBar();
            return false;
        }

        bool wasAlive = currentHealth > 0 && !isDead;

        currentHealth -= damage;

        // 更新进度条
        UpdateHealthBar();

        // 记录子弹方向
        lastHitDirection = bulletDirection.normalized;
        lastHitPoint = hitPoint;

        // 调试：绘制子弹方向
        Debug.DrawRay(bulletPosition, bulletDirection * 2f, Color.red, 2f);
        Debug.DrawRay(hitPoint, lastHitDirection * 2f, Color.blue, 2f);

        bool playHitAnim = true;
        if (bossAI != null)
            playHitAnim = bossAI.OnHit();
        else if (enemyAI != null)
            enemyAI.OnHit();

        // 子弹命中特效由子弹决定（Projectile 会自行生成 impactVFX / criticalHitEffext）
        StartCoroutine(HitFeedbackCoroutine(hitPoint, hitNormal, playHitAnim, spawnHitParticle: false));

        bool killedThisHit = false;
        if (currentHealth <= 0)
        {
            killedThisHit = wasAlive; // 只要这次从活着变成 <=0，就算击杀
            Die();
        }

        return killedThisHit;
    }
    
    private IEnumerator HitFeedbackCoroutine(Vector3 hitPoint, Vector3 hitNormal, bool playHitAnimation = true, bool spawnHitParticle = true)
    {
        StartCoroutine(FlashWhiteCoroutine());
        StartCoroutine(ScaleBounceCoroutine());
        if (spawnHitParticle)
        {
            SpawnHitParticle(hitPoint, hitNormal);
        }
        if (playHitAnimation)
            PlayHitAnimation();
        TriggerScreenShake();
        
        yield return null;
    }
    
    private IEnumerator FlashWhiteCoroutine()
    {
        SetEmissionColorForAllRenderers(flashColor);
        yield return new WaitForSeconds(flashDuration);
        RestoreOriginalEmissionColor();
    }
    
    private void SetEmissionColorForAllRenderers(Color color)
    {
        foreach (var renderer in allRenderers)
        {
            if (renderer == null) continue;
            
            int materialCount = renderer.sharedMaterials.Length;
            for (int i = 0; i < materialCount; i++)
            {
                MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(propertyBlock, i);
                bool hadNoOverrides = propertyBlock.isEmpty;
                propertyBlock.SetColor("_EmissionColor", color);
                if (hadNoOverrides)
                {
                    propertyBlock.SetColor("_BaseColor", color);
                    propertyBlock.SetColor("_Color", color);
                }
                
                renderer.SetPropertyBlock(propertyBlock, i);
            }
        }
        
        for (int s = 0; s < spriteRenderersForFlash.Count; s++)
        {
            SpriteRenderer sr = spriteRenderersForFlash[s];
            if (sr != null) sr.color = color;
        }
    }
    
    private void RestoreOriginalEmissionColor()
    {
        foreach (var renderer in allRenderers)
        {
            if (renderer == null) continue;
            
            int count = renderer.sharedMaterials.Length;
            for (int i = 0; i < count; i++)
                renderer.SetPropertyBlock(null, i);
            
            if (originalPropertyBlocks.TryGetValue(renderer, out MaterialPropertyBlock[] originalBlocks))
            {
                for (int i = 0; i < originalBlocks.Length && i < count; i++)
                {
                    if (originalBlocks[i] != null && !originalBlocks[i].isEmpty)
                        renderer.SetPropertyBlock(originalBlocks[i], i);
                }
            }
        }
        
        foreach (var kvp in originalSpriteColors)
        {
            if (kvp.Key != null)
                kvp.Key.color = kvp.Value;
        }
    }
    
    private void SetEmissionColorForRenderer(Renderer renderer, Color color)
    {
        int materialCount = renderer.sharedMaterials.Length;
        for (int i = 0; i < materialCount; i++)
        {
            MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(propertyBlock, i);
            propertyBlock.SetColor("_EmissionColor", color);
            renderer.SetPropertyBlock(propertyBlock, i);
        }
    }
    
    private IEnumerator ScaleBounceCoroutine()
    {
        Vector3 targetScale = originalScale * scaleMultiplier;
        
        float elapsedTime = 0f;
        while (elapsedTime < scaleDuration / 2)
        {
            transform.localScale = Vector3.Lerp(originalScale, targetScale, elapsedTime / (scaleDuration / 2));
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        
        elapsedTime = 0f;
        while (elapsedTime < scaleDuration / 2)
        {
            transform.localScale = Vector3.Lerp(targetScale, originalScale, elapsedTime / (scaleDuration / 2));
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        
        transform.localScale = originalScale;
    }
    
    private void SpawnHitParticle(Vector3 hitPoint, Vector3 hitNormal)
    {
        if (hitParticlePrefab != null)
        {
            Vector3 spawnPoint = hitParticleSpawnPoint.position;
            
            if (hitPoint != Vector3.zero)
            {
                spawnPoint = hitPoint;
            }
            
            Quaternion rotation = Quaternion.identity;
            if (hitNormal != Vector3.zero)
            {
                rotation = Quaternion.LookRotation(hitNormal);
            }
            
            GameObject particle = Instantiate(hitParticlePrefab, spawnPoint, rotation);
            Destroy(particle, 2f);
        }
    }
    
    private void PlayHitAnimation()
    {
        if (enemyAI != null && enemyAI.IsUsing2DFrameAnimation)
            return;
        if (animator != null && !string.IsNullOrEmpty(hitAnimationTrigger))
        {
            animator.SetTrigger(hitAnimationTrigger);
        }
    }
    
    private void TriggerScreenShake()
    {
        CameraService.PlayImpulse(shakeDuration, shakeMagnitude);
    }
    
    private IEnumerator DeathKnockbackCoroutine()
    {
        Vector3 knockbackDirection = Vector3.zero;
        
        // 计算击退方向
        if (lastHitDirection != Vector3.zero)
        {
            // 子弹飞行的方向，击退应该沿着这个方向
            knockbackDirection = lastHitDirection;
            
            // 确保不是零向量
            if (knockbackDirection.magnitude < 0.1f)
            {
                // 备用方向：朝向摄像机前方
                Vector3 cameraForward = Camera.main.transform.forward;
                cameraForward.y = 0;
                knockbackDirection = cameraForward.normalized;
            }
            
            // 调整Y轴分量，可以添加一点向上或向下的力
            knockbackDirection.y = Mathf.Clamp(knockbackDirection.y, 0.1f, 0.3f);
            knockbackDirection = knockbackDirection.normalized;
            
            //Debug.Log($"击退方向: {knockbackDirection}, 原始方向: {lastHitDirection}");
        }
        else
        {
            // 如果没有击中方向，使用默认方向
            Vector3 cameraForward = Camera.main.transform.forward;
            cameraForward.y = 0;
            knockbackDirection = cameraForward.normalized;
        }
        
        Vector3 startPosition = transform.position;
        Vector3 targetPosition = startPosition + knockbackDirection * deathKnockbackDistance;
        
        float elapsedTime = 0f;
        
        while (elapsedTime < deathKnockbackDuration)
        {
            float t = elapsedTime / deathKnockbackDuration;
            float curveValue = deathKnockbackCurve.Evaluate(t);
            
            transform.position = Vector3.Lerp(startPosition, targetPosition, curveValue);
            
            elapsedTime += Time.deltaTime;
            yield return null;
        }
        
        transform.position = targetPosition;
    }
    
    // 生成爆炸特效
    private void SpawnExplosionEffect()
    {
        if (explosionEffectPrefab != null)
        {
            // 实例化爆炸特效
            GameObject explosion = Instantiate(explosionEffectPrefab, transform.position+new Vector3(0,0.5f,0), Quaternion.identity);
            
            // 获取爆炸特效的持续时间
            ParticleSystem explosionParticles = explosion.GetComponent<ParticleSystem>();
            float explosionDuration = 2f; // 默认2秒
            
            if (explosionParticles != null)
            {
                explosionDuration = explosionParticles.main.duration;
            }
            else
            {
                // 如果没有ParticleSystem，尝试获取子物体中的ParticleSystem
                ParticleSystem[] allParticles = explosion.GetComponentsInChildren<ParticleSystem>();
                if (allParticles.Length > 0)
                {
                    // 找到持续时间最长的粒子系统
                    foreach (var ps in allParticles)
                    {
                        float duration = ps.main.duration;
                        if (duration > explosionDuration)
                        {
                            explosionDuration = duration;
                        }
                    }
                }
            }
            
            // 在特效播放完后自动销毁
            Destroy(explosion, explosionDuration);
        }
    }
    
    // 生成掉落物
    private void SpawnLootItems()
    {
        ApplyMonsterLootConfig();
        if (lootItems == null || lootItems.Length == 0) return;
        
        foreach (LootItem lootItem in lootItems)
        {
            // 根据掉落概率决定是否生成
            if (Random.value <= lootItem.dropChance)
            {
                // 生成随机数量的掉落物实例
                int itemAmount = Random.Range(lootItem.minAmount, lootItem.maxAmount + 1);
                // Enemy drop-density upgrades must not change gatherable yields.
                float lootDensityMultiplier = healthBarType == HealthBarType.Monster && WeaponStatsManager.Instance != null
                    ? WeaponStatsManager.Instance.GetEnemyLootDensityMultiplier(lootItem.resourceType)
                    : 1f;
                itemAmount = Mathf.Max(0, Mathf.RoundToInt(itemAmount * lootDensityMultiplier));
                for (int i = 0; i < itemAmount; i++)
                {
                    // 生成位置在敌人周围随机偏移
                    Vector3 spawnPosition = transform.position + 
                                          new Vector3(Random.Range(-0.5f, 0.5f), 
                                                      Random.Range(0.5f, 1.5f), 
                                                      Random.Range(-0.5f, 0.5f));
                    
                    GameObject item = Instantiate(lootItem.itemPrefab, spawnPosition, Quaternion.identity);
                    SceneManager.MoveGameObjectToScene(item, gameObject.scene);
                    // 设置掉落物的资源信息
                    LootCollector lootCollector = item.GetComponent<LootCollector>();
                    if (lootCollector != null)
                    {
                        // 从LootItem中获取资源信息
                        int resourceAmount = lootItem.GetRandomResourceAmount();
                        lootCollector.SetResourceInfo(lootItem.resourceType, resourceAmount);
                    }
                    
                    // 为掉落物添加随机的散射力
                    Rigidbody rb = item.GetComponent<Rigidbody>();
                    if (rb != null)
                    {
                        Vector3 randomDirection = new Vector3(
                            Random.Range(-1f, 1f),
                            Random.Range(0.3f, 1f),
                            Random.Range(-1f, 1f)
                        ).normalized;
                        
                        rb.AddForce(randomDirection * lootItem.scatterForce, ForceMode.Impulse);
                        rb.AddTorque(new Vector3(
                            Random.Range(-100f, 100f),
                            Random.Range(-100f, 100f),
                            Random.Range(-100f, 100f)
                        ));
                    }
                }
            }
        }
    }

    public void SetLootConfigID(string configID)
    {
        runtimeLootConfigID = configID;
        lootConfigApplied = false;
    }

    private void ApplyMonsterLootConfig()
    {
        if (lootConfigApplied || ExcelConfigReader.Instance == null) return;

        string configID = string.IsNullOrWhiteSpace(runtimeLootConfigID)
            ? NormalizeMonsterConfigID(gameObject.name)
            : runtimeLootConfigID.Trim();

        if (!ExcelConfigReader.Instance.TryGetMonsterLootConfigs(configID, out List<MonsterLootConfigData> configs))
        {
            lootConfigApplied = true;
            return;
        }

        var configuredLootItems = new List<LootItem>();
        foreach (MonsterLootConfigData config in configs)
        {
            if (!config.enabled) continue;

            int sourceIndex = config.lootItemIndex - 1;
            if (lootItems == null || sourceIndex < 0 || sourceIndex >= lootItems.Length || lootItems[sourceIndex] == null)
            {
                Debug.LogError($"怪物 {configID} 的掉落项 {config.lootItemIndex} 在预制体中不存在，无法取得掉落物预制体引用");
                continue;
            }

            LootItem source = lootItems[sourceIndex];
            if (source.itemPrefab == null)
            {
                Debug.LogError($"怪物 {configID} 的掉落项 {config.lootItemIndex} 未配置掉落物预制体");
                continue;
            }

            configuredLootItems.Add(new LootItem
            {
                itemPrefab = source.itemPrefab,
                dropChance = config.dropChance,
                minAmount = config.minAmount,
                maxAmount = config.maxAmount,
                scatterForce = config.scatterForce,
                resourceType = config.resourceType,
                resourceMinAmount = config.resourceMinAmount,
                resourceMaxAmount = config.resourceMaxAmount
            });
        }

        lootItems = configuredLootItems.ToArray();
        lootConfigApplied = true;
    }

    private static string NormalizeMonsterConfigID(string objectName)
    {
        const string cloneSuffix = "(Clone)";
        string normalized = objectName == null ? string.Empty : objectName.Trim();
        if (normalized.EndsWith(cloneSuffix, System.StringComparison.Ordinal))
            normalized = normalized.Substring(0, normalized.Length - cloneSuffix.Length).TrimEnd();
        return normalized;
    }
    
    // 死亡协程
    private IEnumerator DeathCoroutine()
    {
        // 播放死亡动画
        if (animator != null)
        {
            animator.SetTrigger("Die");
        }
        
        // 等待击退效果完成
        yield return StartCoroutine(DeathKnockbackCoroutine());
        
        // 播放爆炸特效（特效会自行销毁）
        SpawnExplosionEffect();
        
        // 生成掉落物
        SpawnLootItems();
        
        // 给剧情对话留出时间（尤其是你这个“恶霸死前遗言”的需求）
        if (destroyDelayAfterDeath > 0f)
            yield return new WaitForSeconds(destroyDelayAfterDeath);

        // 在真正销毁前再隐藏模型，保证遗言期间仍可见
        HideEnemyModel();
        
        // 立即销毁敌人对象，不需要等待特效完成
        Destroy(gameObject);
    }

    /// <summary>
    /// 剧情用途：动态延长死亡后销毁自身的时间，保证对话/镜头能播完。
    /// </summary>
    public void SetDestroyDelayAfterDeath(float delaySeconds)
    {
        destroyDelayAfterDeath = Mathf.Max(0f, delaySeconds);
    }
    
    // 隐藏敌人模型
    private void HideEnemyModel()
    {
        foreach (var renderer in allRenderers)
        {
            if (renderer != null)
            {
                renderer.enabled = false;
            }
        }
    }
    
    private void Die()
    {
        if (isDead) return;
        
        isDead = true;
        if (healthBarType == HealthBarType.Monster)
            RunSessionManager.Instance?.RecordKill();
        //Debug.Log($"{gameObject.name} 被击败了！");
        
        // 死亡时停止所有隐藏协程
        if (hideCoroutine != null)
        {
            StopCoroutine(hideCoroutine);
            hideCoroutine = null;
        }
        
        // 死亡后不再显示血条，避免模型隐藏后 UI 残留
        HideHealthBar();
        
        if (animator != null)
        {
            animator.SetTrigger("Die");
        }
        
        if (bossAI != null)
            bossAI.OnDeath();
        else if (enemyAI != null)
            enemyAI.OnDeath();
        
        Collider collider = GetComponent<Collider>();
        if (collider != null)
            collider.enabled = false;
        
        enabled = false;
        
        // 启动死亡协程
        StartCoroutine(DeathCoroutine());
    }
    
    private void OnDrawGizmosSelected()
    {
        if (hitParticleSpawnPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(hitParticleSpawnPoint.position, 0.1f);
        }
    }
}
