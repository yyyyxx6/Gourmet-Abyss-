using UnityEngine;
using GourmetAbyss.CameraSystem;
using System.Collections.Generic;
using UnityEngine.UI;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Animator))]
public class TopDownController : MonoBehaviour
{
    [System.Serializable]
    private class TreasureBuffVfxEntry
    {
        public battlePropType propType;
        public GameObject vfxPrefab;
        [Tooltip("对应道具拾取后在 WeaponTipsUI 显示的简短文本（可留空不显示）")]
        public string tipsText;
        [Tooltip("覆盖 WeaponTipsUI 显示时长（<=0 则使用 treasureTipsDuration）")]
        public float tipsDurationOverride = -1f;
    }

    [Header("掉落设置")]
    [Tooltip("掉落物品的预制体列表")]
    public List<GameObject> dropItems = new List<GameObject>();
    [Tooltip("掉落数量 (0表示掉落所有物品)")]
    [Range(0, 10)]
    public int dropCount = 3;

    [Tooltip("掉落半径")]
    [Range(0f, 5f)]
    public float dropRadius = 2f;

    [Tooltip("掉落力量")]
    [Range(0f, 20f)]
    public float dropForce = 8f;

    [Tooltip("向上弹跳的力量")]
    [Range(0f, 10f)]
    public float upwardForce = 3f;
    [Tooltip("随机旋转掉落物品")]
    public bool randomRotation = true;
    #region --- 1. 基础设置 ---
    [Header("移动设置")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("旋转设置")]
    [Tooltip("数值越小旋转越平滑，越大越跟手")]
    [SerializeField] private float turnSpeed = 15f;
    #endregion

    #region --- 2. 状态系统 ---
    [Header("战斗状态")]
    [SerializeField] private bool isInCombat = true; // 是否处于战斗状态
    [SerializeField] private KeyCode toggleCombatKey = KeyCode.F; // 切换战斗状态的按键

    [Header("非战斗状态设置")]
    [Tooltip("非战斗状态下是否允许鼠标控制旋转")]
    [SerializeField] private bool allowMouseInNonCombat = false;
    [Tooltip("非战斗状态下是否允许鼠标右键瞄准")]
    [SerializeField] private bool allowAimingInNonCombat = false;
    #endregion

    #region --- 3. 武器引用 ---
    [Header("武器引用")]
    [SerializeField] private PrimaryWeapon primaryWeapon;
    [SerializeField] private SecondaryWeapon secondaryWeapon;
    [SerializeField] private Text weaponTipsUI;
    [SerializeField] private float weaponTipsDuration = 1.5f; // 提示持续时间
    private Coroutine weaponTipsCoroutine;

    [Header("道具拾取特效")]
    [Tooltip("当某个道具的专属配置缺失时，回退使用这个通用粒子特效（会跟随玩家生成，并在道具效果持续时间后隐藏）")]
    [SerializeField] private GameObject treasureEffectVfxPrefab;
    [Tooltip("粒子特效生成位置偏移（相对于玩家）")]
    [SerializeField] private Vector3 treasureEffectVfxOffset = new Vector3(0f, 1.2f, 0f);
    [Tooltip("拾取道具后 WeaponTipsUI 显示的默认持续时间（秒）")]
    [SerializeField] private float treasureTipsDuration = 0.9f;

    [Header("拾取瞬间共用特效")]
    [Tooltip("拾取道具的瞬间共用特效（会播放一小段时间后隐藏），不用于 BUFF 持续显示")]
    [SerializeField] private GameObject treasurePickupInstantVfxPrefab;
    [Tooltip("拾取瞬间共用特效持续时间（秒）")]
    [SerializeField] private float treasurePickupInstantVfxDuration = 0.35f;
    [Tooltip("拾取瞬间共用特效位置偏移（相对于玩家）")]
    [SerializeField] private Vector3 treasurePickupInstantVfxOffset = new Vector3(0f, 1.3f, 0f);

    [Header("道具-粒子/文案映射")]
    [Tooltip("每个战斗道具各自对应的粒子特效与短文本提示；未配置时会回退到 treasureEffectVfxPrefab。")]
    [SerializeField] private List<TreasureBuffVfxEntry> treasureBuffVfxEntries = new List<TreasureBuffVfxEntry>();

    private GameObject activeTreasureVfxInstance;
    private Coroutine treasureVfxCoroutine;

    private GameObject activeTreasurePickupInstantVfxInstance;
    private Coroutine treasurePickupInstantVfxCoroutine;
    private Dictionary<battlePropType, TreasureBuffVfxEntry> treasureBuffVfxEntryMap;

    // 记录上帧是否在充能，避免提示文字每帧刷屏
    private bool wasPrimaryReloading = false;
    private bool wasSecondaryReloading = false;
    // 记录上帧是否已经提示过“没子弹了”，避免每帧重复刷屏
    private bool wasPrimaryNoAmmo = false;
    private bool wasSecondaryNoAmmo = false;
    #endregion

    #region --- 4. 瞄准系统 ---
    [Header("瞄准修正 (防止打地板)")]
    [Tooltip("鼠标能检测到的所有层级 (包括地面、墙、敌人)")]
    [SerializeField] private LayerMask aimLayerMask;
    [Tooltip("仅属于地面的层级 (打在这里时会抬高准星)")]
    [SerializeField] private LayerMask groundLayerMask;
    [Tooltip("准星抬高偏移量 (通常设为 1.0 - 1.5，对应胸口高度)")]
    [SerializeField] private float aimHeightOffset = 1.3f;
    #endregion

    #region --- 5. 动画与组件 ---
    [Header("动画参数")]
    [SerializeField] private string speedParamName = "Speed";
    [SerializeField] private string combatParamName = "IsInCombat";
    [SerializeField] private string hitTriggerName = "Hit";

    [Header("组件引用")]
    [SerializeField] private Camera mainCamera;
    [SerializeField] private Animator animator;

    [Header("饥饿/氧气受伤害反馈")]
    [Tooltip("为空则从 mainCamera 上取 CameraFollow")]
    [SerializeField] private CameraFollow hungerDamageCameraFollow;
    [SerializeField] private float hungerDamageShakeDuration = 0.12f;
    [SerializeField] private float hungerDamageShakeMagnitude = 0.06f;

    [Header("受击材质闪白反馈")]
    [Tooltip("不填则自动从 Animator 对应模型层级收集 Renderer")]
    [SerializeField] private Renderer[] hitFlashRenderers;
    [SerializeField] private float hitFlashWhiteHoldTime = 0.05f;
    [SerializeField] private float hitFlashRestoreDuration = 0.12f;
    [SerializeField] private Color hitFlashColor = Color.white;
    #endregion

    #region --- 6. 足迹粒子效果 ---
    [Header("足迹粒子效果")]
    [Tooltip("足迹粒子系统")]
    [SerializeField] private ParticleSystem footstepParticles;
    [Tooltip("移动阈值，当移动速度大于此值时开始发射粒子")]
    [SerializeField] private float movementThreshold = 0.1f;
    [Tooltip("粒子发射速率，根据移动速度调整")]
    [SerializeField] private float emissionRate = 20f;
    [Tooltip("停止移动后延迟关闭粒子的时间")]
    [SerializeField] private float stopParticleDelay = 0.2f;


    #region --- 临时效果系统: 减伤、加速、加攻击距离 ---
    private Coroutine damageReduceCoroutine;
    private Coroutine speedBuffCoroutine;
    private Coroutine rangeBuffCoroutine;
    private float damageReduceEndTime;
    private float speedBuffEndTime;
    private float rangeBuffEndTime;

    public float currentDamageReducePct = 0f;
    public float currentSpeedBonus = 0f;
    public float currentAttackRangeRate = 0f;

    // 1. 在时间 s 内减免伤害百分比 a (0.3f 表示减少30%伤害)
    public void ApplyDamageReduction(float s, float a)
    {
        // 刷新计时
        damageReduceEndTime = Time.time + s;
        currentDamageReducePct = a;
        if (damageReduceCoroutine == null)
        {
            damageReduceCoroutine = StartCoroutine(DamageReduceRoutine());
        }
    }

    private IEnumerator DamageReduceRoutine()
    {
        while (Time.time < damageReduceEndTime)
        {
            yield return null;
        }
        currentDamageReducePct = 0f;
        damageReduceCoroutine = null;
    }

    // 2. 在时间 s 内增加移动速度 b
    public void ApplySpeedBuff(float s, float b)
    {
        speedBuffEndTime = Time.time + s;
        currentSpeedBonus = b;
        if (speedBuffCoroutine == null)
        {
            speedBuffCoroutine = StartCoroutine(SpeedBuffRoutine());
        }
    }

    private IEnumerator SpeedBuffRoutine()
    {
        while (Time.time < speedBuffEndTime)
        {
            yield return null;
        }
        currentSpeedBonus = 0f;
        speedBuffCoroutine = null;
    }

    // 3. 在时间 s 内增加攻击距离 c
    public void ApplyRangeBuff(float s, float c)
    {
        rangeBuffEndTime = Time.time + s;
        currentAttackRangeRate = c;
        if (rangeBuffCoroutine == null)
        {
            rangeBuffCoroutine = StartCoroutine(RangeBuffRoutine());
        }
    }

    private IEnumerator RangeBuffRoutine()
    {
        while (Time.time < rangeBuffEndTime)
        {
            yield return null;
        }
        currentAttackRangeRate = 0f;
        rangeBuffCoroutine = null;
    }
    #endregion

    private ParticleSystem.EmissionModule particleEmission;
    private float currentEmissionRate = 0f;
    private float stopTimer = 0f;
    private bool isMoving = false;
    #endregion

    // 内部变量
    private Rigidbody rb;
    private Vector3 moveInput;
    private Vector3 cameraFacingDirection = Vector3.right;
    private Vector3 currentAimPoint;
    private Coroutine hitFlashCoroutine;

    private readonly List<Material> cachedHitFlashMaterials = new List<Material>();
    private readonly List<Color> cachedHitFlashOriginalColors = new List<Color>();
    private readonly List<int> cachedHitFlashColorPropertyIds = new List<int>();
    private readonly List<bool> cachedHitFlashUsesEmission = new List<bool>();
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    // 新增：鼠标活动检测
    private bool mouseIsActive = false;
    private Vector3 lastMousePosition;
    private float mouseInactiveTimer = 0f;
    public bool isDead = false;
    public bool canPlayerMove = true;
    /// <summary>
    /// 提供给小镇镜头的稳定朝向。它只在出现有效移动输入时更新，
    /// 不受战斗瞄准时模型旋转影响。
    /// </summary>
    public Vector3 CameraFacingDirection => cameraFacingDirection;
    [SerializeField] private float mouseInactiveThreshold = 0.1f; // 鼠标静止多久后算不活动

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (animator == null) animator = GetComponent<Animator>();
        CacheHitFlashRenderersIfNeeded();


        // 初始化武器
        if (primaryWeapon != null)
        {
            primaryWeapon.Initialize(animator, mainCamera, this);
        }

        if (secondaryWeapon != null)
        {
            secondaryWeapon.Initialize(animator, mainCamera, this);
        }

        // 初始化鼠标位置
        lastMousePosition = Input.mousePosition;

        // 初始化战斗状态动画参数
        if (animator != null && !string.IsNullOrEmpty(combatParamName))
        {
            animator.SetBool(combatParamName, isInCombat);
        }
        // 订阅氧气耗尽事件
        if (BattleValManager.Instance != null)
        {
            BattleValManager.Instance.OnOxygenDepleted += HandleOxygenDepleted;
        }
        // 初始化足迹粒子系统
        InitializeFootstepParticles();

        // 初始化道具-特效/文案映射
        treasureBuffVfxEntryMap = new Dictionary<battlePropType, TreasureBuffVfxEntry>();
        if (treasureBuffVfxEntries != null)
        {
            for (int i = 0; i < treasureBuffVfxEntries.Count; i++)
            {
                var e = treasureBuffVfxEntries[i];
                if (e == null) continue;
                if (treasureBuffVfxEntryMap.ContainsKey(e.propType)) continue;
                treasureBuffVfxEntryMap[e.propType] = e;
            }
        }
    }
    void Start()
    {
        if (mainCamera == null) mainCamera = Camera.main;
        UpdateWeaponVisibility();
    }

    /// <summary>非 <see cref="PlayerState.Battle"/> 或本地非战斗姿态时不显示武器；无 PlayerStateManager 时仅按 isInCombat。</summary>
    private bool ShouldShowWeaponVisuals()
    {
        if (PlayerStateManager.instance != null)
        {
            if (PlayerStateManager.instance.currentState != PlayerState.Battle)
                return false;
        }
        return isInCombat;
    }

    private void UpdateWeaponVisibility()
    {
        bool show = ShouldShowWeaponVisuals();
        if (primaryWeapon != null)
            primaryWeapon.gameObject.SetActive(show);
        if (secondaryWeapon != null)
        {
            bool showSec = show && WeaponStatsManager.Instance != null && WeaponStatsManager.Instance.isSecondaryEnable;
            secondaryWeapon.gameObject.SetActive(showSec);
        }
    }
    private void HandleOxygenDepleted()
    {
        // 只有在战斗状态才执行死亡
        if (isInCombat)
        {
            Die();
        }
    }

    /// <summary>近战敌人命中玩家时扣除氧气（饥饿条）。由 EnemyAI 等调用；与 BOSS 受击一致，会套用护甲减伤 currentDamageReducePct。</summary>
    public void ApplyEnemyMeleeHungerDamage(float amount)
    {
        if (amount <= 0f || isDead) return;
        var bvm = BattleValManager.Instance;
        if (bvm == null || bvm.OxygenCurrent <= 0f) return;

        float d = amount;
        if (currentDamageReducePct > 0f)
            d *= 1f - Mathf.Clamp01(currentDamageReducePct);
        if (d <= 0f) return;

        bvm.DamageOxygen(d);
        PlayHungerDamageFeedback();
    }

    /// <summary>受击反馈：触发 Animator 上的 Hit Trigger（死亡时不播放）。</summary>
    public void PlayHitAnimation()
    {
        if (isDead || animator == null || string.IsNullOrEmpty(hitTriggerName)) return;
        animator.SetTrigger(hitTriggerName);
    }

    /// <summary>怪物/BOSS 造成饥饿（氧气）伤害时的统一反馈：受击动画 + 轻微震屏。</summary>
    public void PlayHungerDamageFeedback()
    {
        PlayHitAnimation();
        TryPlayHungerDamageCameraShake();
        PlayHitFlashFeedback();
    }

    private void CacheHitFlashRenderersIfNeeded()
    {
        if (hitFlashRenderers != null && hitFlashRenderers.Length > 0) return;

        Transform root = animator != null ? animator.transform : transform;
        hitFlashRenderers = root.GetComponentsInChildren<Renderer>(true);
    }

    private void PlayHitFlashFeedback()
    {
        if (isDead) return;
        CacheHitFlashRenderersIfNeeded();
        if (hitFlashRenderers == null || hitFlashRenderers.Length == 0) return;

        if (hitFlashCoroutine != null)
        {
            StopCoroutine(hitFlashCoroutine);
            RestoreHitFlashImmediate();
        }

        hitFlashCoroutine = StartCoroutine(HitFlashRoutine());
    }

    private IEnumerator HitFlashRoutine()
    {
        cachedHitFlashMaterials.Clear();
        cachedHitFlashOriginalColors.Clear();
        cachedHitFlashColorPropertyIds.Clear();
        cachedHitFlashUsesEmission.Clear();

        for (int i = 0; i < hitFlashRenderers.Length; i++)
        {
            Renderer r = hitFlashRenderers[i];
            if (r == null) continue;

            Material[] mats = r.materials;
            for (int j = 0; j < mats.Length; j++)
            {
                Material mat = mats[j];
                if (mat == null) continue;

                int propId = 0;
                bool useEmission = false;
                if (mat.HasProperty(EmissionColorId))
                {
                    propId = EmissionColorId;
                    useEmission = true;
                }
                else if (mat.HasProperty(BaseColorId)) propId = BaseColorId;
                else if (mat.HasProperty(ColorId)) propId = ColorId;
                if (propId == 0) continue;

                cachedHitFlashMaterials.Add(mat);
                cachedHitFlashOriginalColors.Add(mat.GetColor(propId));
                cachedHitFlashColorPropertyIds.Add(propId);
                cachedHitFlashUsesEmission.Add(useEmission);
                if (useEmission) mat.EnableKeyword("_EMISSION");
                mat.SetColor(propId, hitFlashColor);
            }
        }

        if (hitFlashWhiteHoldTime > 0f)
            yield return new WaitForSeconds(hitFlashWhiteHoldTime);

        float duration = Mathf.Max(0.01f, hitFlashRestoreDuration);
        float timer = 0f;
        while (timer < duration)
        {
            float t = timer / duration;
            for (int i = 0; i < cachedHitFlashMaterials.Count; i++)
            {
                Material mat = cachedHitFlashMaterials[i];
                if (mat == null) continue;
                int propId = cachedHitFlashColorPropertyIds[i];
                Color origin = cachedHitFlashOriginalColors[i];
                mat.SetColor(propId, Color.Lerp(hitFlashColor, origin, t));
            }

            timer += Time.deltaTime;
            yield return null;
        }

        RestoreHitFlashImmediate();
        hitFlashCoroutine = null;
    }

    private void RestoreHitFlashImmediate()
    {
        for (int i = 0; i < cachedHitFlashMaterials.Count; i++)
        {
            Material mat = cachedHitFlashMaterials[i];
            if (mat == null) continue;
            int propId = cachedHitFlashColorPropertyIds[i];
            mat.SetColor(propId, cachedHitFlashOriginalColors[i]);
        }

        cachedHitFlashMaterials.Clear();
        cachedHitFlashOriginalColors.Clear();
        cachedHitFlashColorPropertyIds.Clear();
    }

    private void TryPlayHungerDamageCameraShake()
    {
        if (isDead) return;
        if (hungerDamageShakeDuration <= 0f || hungerDamageShakeMagnitude <= 0f) return;

        CameraService.PlayImpulse(hungerDamageShakeDuration, hungerDamageShakeMagnitude);
    }

    /// <summary>
    /// 无 <see cref="BossAttackReceiver"/> 时由 BossAI 等直接调用：按 BOSS 伤害规则扣氧气（饥饿）并播受击，与 BossAttackReceiver.TakeBossDamage 折算一致。
    /// </summary>
    public void ApplyBossOxygenDamage(float rawDamage)
    {
        if (rawDamage <= 0f || isDead) return;

        var bvm = BattleValManager.Instance;
        if (bvm == null || bvm.OxygenCurrent <= 0f) return;

        float d = rawDamage;
        if (currentDamageReducePct > 0f)
            d *= 1f - Mathf.Clamp01(currentDamageReducePct);
        if (WeaponStatsManager.Instance != null)
            d *= Mathf.Max(0f, WeaponStatsManager.Instance.bossDamageToOxygenMultiplier);
        if (d <= 0f) return;

        bvm.DamageOxygen(d);
        PlayHungerDamageFeedback();
    }
    public void Die()
    {
        if (RunSessionManager.Instance != null && RunSessionManager.Instance.IsEndingRun) return;
        if (isDead) return; // 防止重复调用
        isDead = true;

        // 触发动画
        if (animator != null)
        {
            animator.SetTrigger("Dead");
        }

        // 停止移动
        moveInput = Vector3.zero;
        rb.velocity = Vector3.zero;

        // 停止武器射击
        if (primaryWeapon != null) primaryWeapon.SetShooting(false);
        if (secondaryWeapon != null) secondaryWeapon.SetShooting(false);

        // 可以执行额外的函数，比如游戏结束或UI提示
        OnPlayerDead();
    }

    private void OnPlayerDead()
    {
        if (RunSessionManager.Instance == null || !RunSessionManager.Instance.TryEndRun(RunEndReason.Death))
            Debug.LogError("The player died but the run settlement could not be opened.");
    }

    public void StopForRunEnd()
    {
        moveInput = Vector3.zero;
        if (rb != null) rb.velocity = Vector3.zero;
        if (primaryWeapon != null) primaryWeapon.SetShooting(false);
        if (secondaryWeapon != null) secondaryWeapon.SetShooting(false);
        enabled = false;
    }

    public void ResumeAfterRun()
    {
        CancelInvoke("TOHome");
        isDead = false;
        canPlayerMove = true;
        moveInput = Vector3.zero;
        if (rb != null) rb.velocity = Vector3.zero;
        if (animator != null) animator.ResetTrigger("Dead");
        enabled = true;
    }
    public void DropItemsOnDeath()
    {
        if (dropItems == null || dropItems.Count == 0)
        {
            Debug.LogWarning("没有设置掉落物品！");
            return;
        }

        // 确定实际掉落数量
        int actualDropCount = dropCount;
        if (dropCount == 0 || dropCount > dropItems.Count)
        {
            actualDropCount = dropItems.Count;
        }

        // 如果掉落数量少于列表总数，随机选择掉落的物品
        List<GameObject> itemsToDrop = new List<GameObject>();
        if (actualDropCount < dropItems.Count)
        {
            // 创建临时列表进行随机选择
            List<GameObject> tempList = new List<GameObject>(dropItems);
            for (int i = 0; i < actualDropCount; i++)
            {
                int randomIndex = Random.Range(0, tempList.Count);
                itemsToDrop.Add(tempList[randomIndex]);
                tempList.RemoveAt(randomIndex);
            }
        }
        else
        {
            // 掉落所有物品
            itemsToDrop = new List<GameObject>(dropItems);
        }

        // 实例化并掉落每个物品
        foreach (GameObject itemPrefab in itemsToDrop)
        {
            if (itemPrefab == null) continue;

            // 在玩家位置实例化物品
            Vector3 dropPosition = transform.position;
            GameObject droppedItem = Instantiate(itemPrefab, dropPosition, Quaternion.identity, gameObject.transform);

            // 添加悬浮旋转脚本
            ItemFloatAndRotate floatScript = droppedItem.AddComponent<ItemFloatAndRotate>();

            // 设置随机位置（在掉落半径内）
            Vector3 randomOffset = new Vector3(
                Random.Range(-dropRadius, dropRadius),
                0.2f, // 稍微抬高一点，避免嵌入地面
                Random.Range(-dropRadius, dropRadius)
            );

            droppedItem.transform.position = dropPosition + randomOffset;

            // 如果是3D物体，添加物理效果
            Rigidbody rb = droppedItem.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = droppedItem.AddComponent<Rigidbody>();
            }
            // rb.isKinematic = true; // 防止物理引擎立即作用导致爆开
            // rb.useGravity = false; // 防止重力下落
            // rb.velocity = Vector3.zero;
            // rb.angularVelocity = Vector3.zero;
            // 添加掉落力
            if (rb != null)
            {
                Vector3 forceDirection = randomOffset.normalized;
                rb.AddForce(forceDirection * dropForce + Vector3.up * upwardForce, ForceMode.Impulse);

                // 添加随机旋转
                if (randomRotation)
                {
                    float randomTorque = Random.Range(1f, 5f);
                    rb.AddTorque(
                        Random.Range(-randomTorque, randomTorque),
                        Random.Range(-randomTorque, randomTorque),
                        Random.Range(-randomTorque, randomTorque),
                        ForceMode.Impulse
                    );
                }
            }



        }

        Debug.Log($"死亡掉落了 {itemsToDrop.Count} 个物品");
    }
    public void TOHome()
    {
        RunSessionManager.Instance?.TryEndRun(isDead ? RunEndReason.Death : RunEndReason.Extracted);
    }

    // 初始化足迹粒子效果
    private void InitializeFootstepParticles()
    {
        if (footstepParticles != null)
        {
            // 获取发射模块
            particleEmission = footstepParticles.emission;

            // 初始时关闭粒子发射
            particleEmission.rateOverTime = 0f;
            currentEmissionRate = 0f;
            isMoving = false;
        }
    }

    void Update()
    {
        if (isDead || IsSettlementBlockingInput()) return;
        // 0. 检查战斗状态切换
        CheckCombatToggle();

        // 1. 输入处理
        HandleMovementInput();

        // 仅在「大地图战斗 + 本地战斗姿态」下处理射击（与武器显隐一致）
        if (ShouldShowWeaponVisuals())
        {
            HandleWeaponInput();
        }
        else
        {
            if (animator != null)
            {
                if (primaryWeapon != null)
                    animator.SetBool(primaryWeapon.shootBoolName, false);
            }
            if (primaryWeapon != null) primaryWeapon.SetShooting(false);
            if (secondaryWeapon != null) secondaryWeapon.SetShooting(false);
        }

        UpdateWeaponVisibility();

        // 2. 检测鼠标活动状态
        CheckMouseActivity();

        // 3. 状态更新
        UpdateAnimation();
        UpdateEffects();

        // 4. 武器更新
        if (primaryWeapon != null) primaryWeapon.UpdateWeapon();
        if (secondaryWeapon != null) secondaryWeapon.UpdateWeapon();
    }

    void FixedUpdate()
    {
        if (isDead || IsSettlementBlockingInput()) return;
        // 物理移动和旋转建议在 FixedUpdate 中进行
        Move();
        Turn();

        // 更新粒子效果
        UpdateParticleEffects();
    }

    private static bool IsSettlementBlockingInput()
    {
        return (PlayerStateManager.instance != null && PlayerStateManager.instance.currentState == PlayerState.Settlement) ||
            (RunSessionManager.Instance != null && RunSessionManager.Instance.IsEndingRun);
    }

    #region --- 足迹粒子效果控制 ---
    private void UpdateParticleEffects()
    {
        if (footstepParticles == null) return;

        // 计算当前移动速度
        float currentSpeed = rb.velocity.magnitude;
        float targetEmissionRate = 0f;

        // 检查是否在移动
        bool wasMoving = isMoving;
        isMoving = currentSpeed > movementThreshold && moveInput.magnitude > 0.1f;

        if (isMoving)
        {
            // 在移动，重置停止计时器
            stopTimer = 0f;

            // 根据移动速度计算发射速率
            float speedRatio = Mathf.Clamp01(currentSpeed / moveSpeed);
            targetEmissionRate = emissionRate * speedRatio;

            // 平滑过渡到目标发射速率
            currentEmissionRate = emissionRate;

            // 应用发射速率
            particleEmission.rateOverTime = currentEmissionRate;

            // 如果之前不在移动，现在开始移动，确保粒子系统在播放
            if (!wasMoving && !footstepParticles.isPlaying)
            {
                footstepParticles.Play();
            }
        }
        else
        {
            // 不在移动，增加停止计时器
            stopTimer += Time.fixedDeltaTime;

            // 如果超过延迟时间，平滑减少发射速率
            if (stopTimer >= stopParticleDelay)
            {
                currentEmissionRate = Mathf.Lerp(currentEmissionRate, 0f, 10f * Time.fixedDeltaTime);
                particleEmission.rateOverTime = currentEmissionRate;

                // 当发射速率接近0时停止粒子系统
                if (currentEmissionRate < 0.1f)
                {
                    particleEmission.rateOverTime = 0f;
                    if (footstepParticles.isPlaying)
                    {
                        footstepParticles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    }
                }
            }
        }

        // 调试信息
        // Debug.Log($"Speed: {currentSpeed:F2}, Moving: {isMoving}, Emission: {particleEmission.rateOverTime.constant:F1}");
    }

    // 强制停止粒子效果
    public void StopFootstepParticles()
    {
        if (footstepParticles != null)
        {
            particleEmission.rateOverTime = 0f;
            footstepParticles.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            isMoving = false;
            currentEmissionRate = 0f;
        }
    }

    // 强制开始粒子效果
    public void StartFootstepParticles()
    {
        if (footstepParticles != null)
        {
            isMoving = true;
            currentEmissionRate = emissionRate;
            particleEmission.rateOverTime = currentEmissionRate;
            if (!footstepParticles.isPlaying)
            {
                footstepParticles.Play();
            }
        }
    }

    // 设置粒子系统引用
    public void SetFootstepParticles(ParticleSystem particles)
    {
        footstepParticles = particles;
        InitializeFootstepParticles();
    }
    #endregion

    #region --- 武器输入处理 ---
    private void HandleWeaponInput()
    {
        // 主武器开火
        if (primaryWeapon != null)
        {
            bool isFiring = Input.GetButton("Fire1");
            primaryWeapon.SetShooting(isFiring);

            bool isReloading = primaryWeapon.IsReloading();
            if (isFiring)
            {
                // 充能期间仍按开火：提示“充能中”
                if (isReloading && (Input.GetButtonDown("Fire1") || !wasPrimaryReloading))
                {
                    ShowWeaponTips("主武器充能中");
                }

                if (isReloading)
                {
                    wasPrimaryNoAmmo = false;
                }
                else
                {
                    bool canConsume = BattleValManager.Instance != null && BattleValManager.Instance.CanConsumePrimaryAmmo();
                    if (!canConsume)
                    {
                        bool canReload = BattleValManager.Instance != null && BattleValManager.Instance.CanReloadPrimaryMagazine();
                        if (!canReload)
                        {
                            if (Input.GetButtonDown("Fire1") || !wasPrimaryNoAmmo)
                                ShowWeaponTips("主武器没子弹了");
                            wasPrimaryNoAmmo = true;
                        }
                    }
                    else
                    {
                        wasPrimaryNoAmmo = false;
                        primaryWeapon.HandleShooting(currentAimPoint, mouseIsActive);
                    }
                }
            }
        }
        if (WeaponStatsManager.Instance)
        {
            if (WeaponStatsManager.Instance.isSecondaryEnable)
            {
                if (secondaryWeapon != null)
                {
                    bool isFiringSecondary = Input.GetButton("Fire2");
                    secondaryWeapon.SetShooting(isFiringSecondary);

                    bool isReloading = secondaryWeapon.IsReloading();
                    if (isFiringSecondary)
                    {
                        // 充能期间仍按开火：提示“充能中”
                        if (isReloading && (Input.GetButtonDown("Fire2") || !wasSecondaryReloading))
                        {
                            ShowWeaponTips("副武器充能中");
                        }

                        if (isReloading)
                        {
                            wasSecondaryNoAmmo = false;
                        }
                        else
                        {
                            bool canConsume = BattleValManager.Instance != null && BattleValManager.Instance.CanConsumeSecondaryAmmo();
                            if (!canConsume)
                            {
                                bool canReload = BattleValManager.Instance != null && BattleValManager.Instance.CanReloadSecondaryMagazine();
                                if (!canReload)
                                {
                                    if (Input.GetButtonDown("Fire2") || !wasSecondaryNoAmmo)
                                        ShowWeaponTips("副武器没子弹了");
                                    wasSecondaryNoAmmo = true;
                                }
                            }
                            else
                            {
                                wasSecondaryNoAmmo = false;
                                secondaryWeapon.HandleShooting(currentAimPoint, mouseIsActive);
                            }
                        }
                    }
                }
            }
        }

        // 更新充能状态缓存
        wasPrimaryReloading = primaryWeapon != null && primaryWeapon.IsReloading();
        wasSecondaryReloading = secondaryWeapon != null && secondaryWeapon.IsReloading();

    }
    #endregion

    #region --- 公共方法 ---

    // 获取战斗状态
    public bool GetCombatState()
    {
        return isInCombat;
    }

    // 获取鼠标活动状态
    public bool IsMouseActive()
    {
        return mouseIsActive;
    }

    // 获取瞄准点
    public Vector3 GetAimPoint()
    {
        return currentAimPoint;
    }

    // 获取角色朝向
    public Vector3 GetCharacterForward()
    {
        return transform.forward;
    }

    // 获取枪口位置
    public Vector3 GetFirePointPosition(bool isPrimary = true)
    {
        if (isPrimary && primaryWeapon != null)
            return primaryWeapon.GetFirePoint().position;
        else if (!isPrimary && secondaryWeapon != null)
            return secondaryWeapon.GetFirePoint().position;

        return transform.position + transform.forward;
    }

    // 获取是否在移动
    public bool IsMoving()
    {
        return isMoving;
    }

    /// <summary>启用/禁用玩家移动与转向；禁用时立即清零速度与输入。</summary>
    public void SetPlayerMovementEnabled(bool enabled)
    {
        canPlayerMove = enabled;
        if (enabled) return;

        moveInput = Vector3.zero;
        if (rb != null)
            rb.velocity = Vector3.zero;
        isMoving = false;
        StopFootstepParticles();
    }

    #endregion

    #region --- 原有的移动、旋转、状态管理逻辑（保持不变）---

    // 新增：检查战斗状态切换
    private void CheckCombatToggle()
    {
        if (Input.GetKeyDown(toggleCombatKey))
        {
            ToggleCombatState();
        }
    }

    // 新增：切换战斗状态
    public void ToggleCombatState()
    {
        isInCombat = !isInCombat;

        // 更新动画参数
        if (animator != null && !string.IsNullOrEmpty(combatParamName))
        {
            animator.SetBool(combatParamName, isInCombat);
        }

        UpdateWeaponVisibility();
        Debug.Log("战斗状态: " + (isInCombat ? "开启" : "关闭"));
    }

    /// <summary>
    /// 拾取道具后触发：播放粒子特效 + 在短时间内展示 WeaponTipsUI 文本。
    /// </summary>
    public void PlayTreasurePickupEffect(float effectDuration, string tipsText)
    {
        // 1) UI提示
        if (!string.IsNullOrEmpty(tipsText))
            ShowWeaponTips(tipsText, treasureTipsDuration);

        // 2) VFX
        if (treasureEffectVfxPrefab == null) return;

        float duration = Mathf.Max(0.01f, effectDuration);

        if (treasureVfxCoroutine != null)
        {
            StopCoroutine(treasureVfxCoroutine);
            treasureVfxCoroutine = null;
        }

        if (activeTreasureVfxInstance != null)
        {
            activeTreasureVfxInstance.SetActive(false);
            activeTreasureVfxInstance = null;
        }

        Vector3 spawnPos = transform.position + treasureEffectVfxOffset;
        activeTreasureVfxInstance = Instantiate(treasureEffectVfxPrefab, spawnPos, Quaternion.identity, transform);
        activeTreasureVfxInstance.SetActive(true);

        // 确保粒子立即播放（避免“预热”导致看起来没反应）
        var pSystems = activeTreasureVfxInstance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < pSystems.Length; i++)
        {
            var ps = pSystems[i];
            if (ps == null) continue;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }

        treasureVfxCoroutine = StartCoroutine(HideTreasureVfxAfterDelay(duration));
    }

    /// <summary>
    /// 拾取道具后触发：使用对应道具配置的 VFX + 文案。
    /// </summary>
    public void PlayTreasurePickupEffect(battlePropType propType, float effectDuration)
    {
        TreasureBuffVfxEntry entry = null;
        if (treasureBuffVfxEntryMap != null)
        {
            treasureBuffVfxEntryMap.TryGetValue(propType, out entry);
        }

        // 拾取瞬间共用特效（短时间显示/隐藏）
        PlayTreasurePickupInstantVfx();

        // UI提示
        if (entry != null && !string.IsNullOrEmpty(entry.tipsText))
        {
            float durationOverride = entry.tipsDurationOverride > 0f ? entry.tipsDurationOverride : treasureTipsDuration;
            ShowWeaponTips(entry.tipsText, durationOverride);
        }

        // VFX
        GameObject prefabToPlay = null;
        if (entry != null && entry.vfxPrefab != null)
            prefabToPlay = entry.vfxPrefab;
        else
            prefabToPlay = treasureEffectVfxPrefab;

        if (prefabToPlay == null) return;

        float duration = Mathf.Max(0.01f, effectDuration);

        if (treasureVfxCoroutine != null)
        {
            StopCoroutine(treasureVfxCoroutine);
            treasureVfxCoroutine = null;
        }

        if (activeTreasureVfxInstance != null)
        {
            activeTreasureVfxInstance.SetActive(false);
            activeTreasureVfxInstance = null;
        }

        Vector3 spawnPos = transform.position + treasureEffectVfxOffset;
        activeTreasureVfxInstance = Instantiate(prefabToPlay, spawnPos, Quaternion.identity, transform);
        activeTreasureVfxInstance.SetActive(true);

        // 确保粒子立即播放（避免“预热”导致看起来没反应）
        var pSystems = activeTreasureVfxInstance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < pSystems.Length; i++)
        {
            var ps = pSystems[i];
            if (ps == null) continue;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }

        treasureVfxCoroutine = StartCoroutine(HideTreasureVfxAfterDelay(duration));
    }

    private void PlayTreasurePickupInstantVfx()
    {
        if (treasurePickupInstantVfxPrefab == null) return;

        float duration = Mathf.Max(0.01f, treasurePickupInstantVfxDuration);

        if (treasurePickupInstantVfxCoroutine != null)
        {
            StopCoroutine(treasurePickupInstantVfxCoroutine);
            treasurePickupInstantVfxCoroutine = null;
        }

        if (activeTreasurePickupInstantVfxInstance != null)
        {
            activeTreasurePickupInstantVfxInstance.SetActive(false);
            activeTreasurePickupInstantVfxInstance = null;
        }

        Vector3 spawnPos = transform.position + treasurePickupInstantVfxOffset;
        activeTreasurePickupInstantVfxInstance = Instantiate(
            treasurePickupInstantVfxPrefab,
            spawnPos,
            Quaternion.identity,
            transform
        );
        activeTreasurePickupInstantVfxInstance.SetActive(true);

        // 确保粒子立即播放（避免看起来没反应）
        var pSystems = activeTreasurePickupInstantVfxInstance.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < pSystems.Length; i++)
        {
            var ps = pSystems[i];
            if (ps == null) continue;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }

        treasurePickupInstantVfxCoroutine = StartCoroutine(HideTreasurePickupInstantVfxAfterDelay(duration));
    }

    private IEnumerator HideTreasurePickupInstantVfxAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (activeTreasurePickupInstantVfxInstance != null)
        {
            activeTreasurePickupInstantVfxInstance.SetActive(false);
            activeTreasurePickupInstantVfxInstance = null;
        }
        treasurePickupInstantVfxCoroutine = null;
    }

    private IEnumerator HideTreasureVfxAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (activeTreasureVfxInstance != null)
        {
            activeTreasureVfxInstance.SetActive(false);
            activeTreasureVfxInstance = null;
        }
        treasureVfxCoroutine = null;
    }

    // 新增：设置战斗状态
    public void SetCombatState(bool combatState)
    {
        isInCombat = combatState;

        // 更新动画参数
        if (animator != null && !string.IsNullOrEmpty(combatParamName))
        {
            animator.SetBool(combatParamName, isInCombat);
        }

        UpdateWeaponVisibility();
    }

    // --- 鼠标活动检测 ---
    private void CheckMouseActivity()
    {
        // 非战斗状态下，根据设置决定鼠标是否激活
        if (!isInCombat)
        {
            if (!allowMouseInNonCombat)
            {
                mouseIsActive = false;
                return;
            }

            // 如果非战斗状态下允许鼠标控制，但限制为右键瞄准
            if (allowAimingInNonCombat && Input.GetMouseButton(1))
            {
                // 右键按下时激活鼠标
                mouseIsActive = true;
                mouseInactiveTimer = 0f;
            }
            else
            {
                mouseIsActive = false;
            }
            return;
        }

        // 以下是战斗状态下的鼠标检测逻辑
        Vector3 currentMousePos = Input.mousePosition;

        // 检查鼠标是否移动
        if (Vector3.Distance(currentMousePos, lastMousePosition) > 0.1f)
        {
            mouseIsActive = true;
            mouseInactiveTimer = 0f;
        }
        // 检查鼠标是否被按下
        else if (Input.GetMouseButton(0) || Input.GetMouseButton(1) || Input.GetMouseButton(2))
        {
            mouseIsActive = true;
            mouseInactiveTimer = 0f;
        }
        else
        {
            // 鼠标没有活动，增加计时器
            mouseInactiveTimer += Time.deltaTime;
            if (mouseInactiveTimer > mouseInactiveThreshold)
            {
                mouseIsActive = false;
            }
        }

        lastMousePosition = currentMousePos;
    }

    // --- 移动逻辑 ---
    private void HandleMovementInput()
    {
        if (canPlayerMove)
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");

            // 非战斗状态下，只允许水平移动
            if (!isInCombat)
            {
                v = 0f;
            }

            // 让移动方向相对于摄像机视角，而不是世界坐标
            Vector3 camForward = mainCamera.transform.forward;
            Vector3 camRight = mainCamera.transform.right;
            camForward.y = 0;
            camRight.y = 0;

            moveInput = (camForward.normalized * v + camRight.normalized * h).normalized;
            if (moveInput.sqrMagnitude > 0.0001f)
            {
                cameraFacingDirection = moveInput;
            }
        }
        else
        {
            moveInput = Vector3.zero;
        }

    }

    private void Move()
    {
        if (!canPlayerMove) return;

        float finalMoveSpeed = moveSpeed * (1f + currentSpeedBonus);
        rb.MovePosition(rb.position + moveInput * finalMoveSpeed * Time.fixedDeltaTime);
    }

    // --- 旋转与瞄准逻辑 (核心 3D 修正) ---
    public bool TryResolveAimPoint(Vector2 screenPoint, out Vector3 point, out Collider hitCollider)
    {
        return GourmetAbyss.CameraSystem.CameraAimUtility.TryResolve(mainCamera, screenPoint,
            aimLayerMask, groundLayerMask, transform.position.y, aimHeightOffset, out point, out hitCollider);
    }

    private void Turn()
    {
        if (!canPlayerMove)
        {
            return;
        }
        if (TryResolveAimPoint(Input.mousePosition, out var aimPoint, out _)) currentAimPoint = aimPoint;

        // 角色旋转逻辑
        if (isInCombat)
        {
            // 战斗状态：跟随鼠标旋转
            if (mouseIsActive)
            {
                // 计算鼠标到角色的水平方向
                Vector3 lookPos = currentAimPoint;
                lookPos.y = transform.position.y;

                Vector3 direction = lookPos - transform.position;

                // 只有当方向有效时才旋转
                if (direction.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(direction);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime);
                }
            }
            else if (moveInput.magnitude > 0.1f)
            {
                // 鼠标不活动但角色在移动：转向移动方向
                Vector3 direction = moveInput;
                if (direction.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(direction);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime);
                }
            }
            // 否则保持当前旋转
        }
        else
        {
            // 非战斗状态：只根据移动方向旋转
            if (moveInput.magnitude > 0.1f)
            {
                Vector3 direction = moveInput;
                if (direction.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(direction);
                    transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * Time.fixedDeltaTime);
                }
            }
            // 否则保持当前旋转
        }
    }

    // --- 动画与特效 ---
    private void UpdateAnimation()
    {
        if (animator == null) return;
        // 简单的移动混合树控制
        animator.SetFloat(speedParamName, moveInput.magnitude, 0.1f, Time.deltaTime);
    }

    private void UpdateEffects()
    {
        // 如果需要，可以在这里添加其他通用特效更新
    }

    // 新增：在禁用时清理武器
    private void OnDisable()
    {
        if (hitFlashCoroutine != null)
        {
            StopCoroutine(hitFlashCoroutine);
            hitFlashCoroutine = null;
            RestoreHitFlashImmediate();
        }

        if (secondaryWeapon != null)
        {
            secondaryWeapon.OnControllerDisabled();
        }

        // 清理道具拾取相关特效
        if (treasureVfxCoroutine != null)
        {
            StopCoroutine(treasureVfxCoroutine);
            treasureVfxCoroutine = null;
        }
        if (activeTreasureVfxInstance != null)
        {
            activeTreasureVfxInstance.SetActive(false);
            activeTreasureVfxInstance = null;
        }
        if (treasurePickupInstantVfxCoroutine != null)
        {
            StopCoroutine(treasurePickupInstantVfxCoroutine);
            treasurePickupInstantVfxCoroutine = null;
        }
        if (activeTreasurePickupInstantVfxInstance != null)
        {
            activeTreasurePickupInstantVfxInstance.SetActive(false);
            activeTreasurePickupInstantVfxInstance = null;
        }
    }

    // 新增：在销毁时清理武器
    private void OnDestroy()
    {
        if (BattleValManager.Instance != null)
        {
            BattleValManager.Instance.OnOxygenDepleted -= HandleOxygenDepleted;
        }

        if (secondaryWeapon != null)
        {
            secondaryWeapon.OnControllerDestroyed();
        }

        if (hitFlashCoroutine != null)
        {
            StopCoroutine(hitFlashCoroutine);
            hitFlashCoroutine = null;
        }
        RestoreHitFlashImmediate();

        // 清理道具拾取相关特效
        if (treasureVfxCoroutine != null)
        {
            StopCoroutine(treasureVfxCoroutine);
            treasureVfxCoroutine = null;
        }
        if (activeTreasureVfxInstance != null)
        {
            Destroy(activeTreasureVfxInstance);
            activeTreasureVfxInstance = null;
        }
        if (treasurePickupInstantVfxCoroutine != null)
        {
            StopCoroutine(treasurePickupInstantVfxCoroutine);
            treasurePickupInstantVfxCoroutine = null;
        }
        if (activeTreasurePickupInstantVfxInstance != null)
        {
            Destroy(activeTreasurePickupInstantVfxInstance);
            activeTreasurePickupInstantVfxInstance = null;
        }
    }
    #endregion

    private void ShowWeaponTips(string message, float durationOverride = -1f)
    {
        if (weaponTipsUI == null) return;

        // 停止上一个协程
        if (weaponTipsCoroutine != null)
            StopCoroutine(weaponTipsCoroutine);

        weaponTipsUI.text = message;
        weaponTipsUI.gameObject.SetActive(true);

        float duration = durationOverride > 0f ? durationOverride : weaponTipsDuration;
        weaponTipsCoroutine = StartCoroutine(HideWeaponTipsAfterDelay(duration));
    }

    private IEnumerator HideWeaponTipsAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        weaponTipsUI.gameObject.SetActive(false);
        weaponTipsCoroutine = null;
    }
}
