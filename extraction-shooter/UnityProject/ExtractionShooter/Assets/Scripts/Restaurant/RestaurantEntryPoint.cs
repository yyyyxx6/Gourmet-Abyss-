using System.Collections;
using System.Collections.Generic;
using Game.Core;
using GourmetAbyss.CameraSystem;
using UnityEngine;

/// <summary>
/// 挂在餐厅「进入点」触发器上：进入范围后按 E 进入餐厅视角；通过 LeaveRestaurant()（如 UI 按钮）离开。
/// </summary>
[RequireComponent(typeof(Collider))]
public class RestaurantEntryPoint : MonoSingleton<RestaurantEntryPoint>
{
    // 每个场景各一份，后加载的接管——沿用原来的裸赋值语义。
    protected override DuplicatePolicy Duplicate => DuplicatePolicy.OverwriteReference;

    [Header("餐厅锚点（相机对焦位置；为空则使用本物体 Transform）")]
    [SerializeField] private Transform restaurantAnchor;

    [Header("进入餐厅后激活的对象（离开时休眠）")]
    [SerializeField] private GameObject restaurantActiveContent;

    [Header("玩家座位")]
    [Tooltip("进入餐厅后玩家固定到该位置；为空时使用餐厅锚点。")]
    [SerializeField] private Transform playerSeatAnchor;
    [Tooltip("离开餐厅时恢复到进入前的门口位置与朝向。")]
    [SerializeField] private bool restorePlayerPoseOnExit = true;

    [Header("相机对焦")]
    [SerializeField] private float cameraOrthoSize = 5.5f;
    [Tooltip("在锚点 Y 上的额外偏移（正值 = 相机看向更低的位置）")]
    [SerializeField] private float cameraYFocusOffset = 0.35f;
    [Tooltip("<= 0 时不改 orthographicSize，仅平移视角")]
    [SerializeField] private bool adjustOrthoSize = true;
    [SerializeField] private RestaurantCameraProfile cameraProfile;
    [Tooltip("已接入模块展示时使用；为空保留旧餐厅正交镜头。")]
    [SerializeField] private PlanarPerspectiveView moduleCameraView;
    [Tooltip("可选。指定后直接使用 Collider 边界；为空时按 cameraBoundsRoot、锚点父物体、活动内容依次回退。")]
    [SerializeField] private Collider cameraBounds;
    [Tooltip("可选。未指定 cameraBounds 时，从该对象的 Renderer 计算边界；为空时优先使用餐厅锚点的父物体。")]
    [SerializeField] private GameObject cameraBoundsRoot;

    [Header("交互")]
    [SerializeField] private KeyCode interactKey = KeyCode.E;
    [SerializeField] private KeyCode exitKey = KeyCode.Escape;
    [SerializeField] private string playerTag = "Player";

    private readonly HashSet<int> _playerColliderIdsInTrigger = new HashSet<int>();
    private TopDownController _cachedPlayer;
    private TopDownController _lockedPlayer;
    private bool _wasPlayerMoveEnabled = true;
    private bool _playerInRange;
    private bool _isEntered;
    private bool _hasSavedCameraState;
    private bool _hasSavedPlayerPose;
    private float _savedOrthoSize;
    private Vector3 _savedCameraPosition;
    private Vector3 _savedPlayerPosition;
    private Quaternion _savedPlayerRotation;
    private Rigidbody _lockedPlayerBody;
    private bool _wasPlayerBodyKinematic;
    private string _cameraRequestKey;
    private InteractiveFeedback _feedback;
    private CameraShotLease _restaurantCameraLease;
    private CameraPlanarBounds _cachedCameraBounds;
    private bool _hasCachedCameraBounds;
    private GameObject _runtimeCameraAnchor;
    [SerializeField] private Transform ingredientStorageAnchor;
    [SerializeField, Min(0.01f)] private float ingredientTransferDuration = 0.6f;
    private Coroutine _ingredientTransferRoutine;
    private GameObject _ingredientTransferVisual;
    private GameObject _backpackPresentation;
    private bool _wasBackpackActive;
    public bool IsTransferringIngredients { get; private set; }

    public bool IsEntered => _isEntered;
    public Transform IngredientStorageOrigin => ResolveIngredientStorageAnchor();

    protected override void OnAwake()
    {
        Collider col = GetComponent<Collider>();
        if (col != null && !col.isTrigger)
            col.isTrigger = true;

        _cameraRequestKey = $"restaurant_entry_{GetInstanceID()}";
        _feedback = GetComponent<InteractiveFeedback>();
        SetRestaurantContentActive(false);
    }

    // 原 OnDestroy 只做「清空 Instance」，该职责已由 MonoSingleton 基类接管。

    private void Update()
    {
        if (_isEntered)
        {
            if (Input.GetKeyDown(exitKey))
                LeaveRestaurant();
            return;
        }

        if (_playerInRange && Input.GetKeyDown(interactKey))
            EnterEntryState();
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        int id = other.GetInstanceID();
        if (!_playerColliderIdsInTrigger.Add(id)) return;
        if (_playerColliderIdsInTrigger.Count > 1) return;

        _playerInRange = true;
        _cachedPlayer = other.GetComponent<TopDownController>();
        if (_cachedPlayer == null)
            _cachedPlayer = other.GetComponentInParent<TopDownController>();

        _feedback?.PlayFeedback();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!other.CompareTag(playerTag)) return;

        int id = other.GetInstanceID();
        if (!_playerColliderIdsInTrigger.Remove(id)) return;
        if (_playerColliderIdsInTrigger.Count > 0) return;

        _playerInRange = false;
        if (!_isEntered)
            _cachedPlayer = null;

        _feedback?.StopFeedbackSmoothly();
    }

    private void OnDisable()
    {
        if (_isEntered)
            LeaveRestaurant();

        _playerInRange = false;
        _cachedPlayer = null;
        _playerColliderIdsInTrigger.Clear();
        _feedback?.StopFeedbackSmoothly();
    }

    private void EnterEntryState()
    {
        TryEnterRestaurant(_cachedPlayer);
    }

    public bool TryEnterRestaurant(TopDownController player)
    {
        if (_isEntered || player == null || player.isDead || !player.isActiveAndEnabled ||
            (PlayerStateManager.instance != null &&
             (PlayerStateManager.instance.currentState != PlayerState.UpGround || PlayerStateManager.instance.isSettingUIActive)) ||
            (LevelManager.instance != null && LevelManager.instance.IsTransitioning())) return false;
        InventoryManager inventory = InventoryManager.instance;
        if (inventory == null) return false;
        List<InventoryManager.RestaurantTransferSource> sources;
        if (!RestaurantIngredientStorage.EnsureExists().TryTransferFromBackpack(inventory, out sources)) return false;
        _backpackPresentation = inventory.InventoryPresentationRoot;
        _wasBackpackActive = _backpackPresentation != null && _backpackPresentation.activeSelf;

        SaveCameraStateBeforeEnter();
        AcquireRestaurantCamera();

        _lockedPlayer = player;
        _wasPlayerMoveEnabled = player.canPlayerMove;
        SavePlayerPose(player);
        player.SetPlayerMovementEnabled(false);
        LockPlayerPhysics(player);
        SnapPlayerToSeat(player);
        _isEntered = true;
        SetRestaurantContentActive(true);
        RefreshRestaurantMenuOnEnter();

        AudioManager.Instance?.PlayAudio("3");
        if (sources.Count > 0)
        {
            IsTransferringIngredients = true;
            _ingredientTransferRoutine = StartCoroutine(PresentIngredientDeposit(sources));
        }
        else SetBackpackHidden();
        return true;
    }

    private IEnumerator PresentIngredientDeposit(List<InventoryManager.RestaurantTransferSource> sources)
    {
        // The module adapter opens its HUD and selects the restaurant camera in LateUpdate.
        yield return null;
        _ingredientTransferVisual = RestaurantIngredientTransferPresentation.Create(sources);
        yield return RestaurantIngredientTransferPresentation.Animate(_ingredientTransferVisual, sources,
            ResolveIngredientStorageAnchor(), ingredientTransferDuration);
        _ingredientTransferVisual = null;
        _ingredientTransferRoutine = null;
        IsTransferringIngredients = false;
        if (_isEntered) SetBackpackHidden();
    }

    private Transform ResolveIngredientStorageAnchor()
    {
        if (ingredientStorageAnchor != null) return ingredientStorageAnchor;
        foreach (RestaurantModuleAdapter adapter in FindObjectsOfType<RestaurantModuleAdapter>(true))
        {
            if (adapter.entry != this || adapter.pair == null || adapter.pair.world == null) continue;
            Transform anchor = adapter.pair.world.GetAnchor("storage");
            if (anchor != null) return anchor;
            foreach (Transform candidate in adapter.pair.world.GetComponentsInChildren<Transform>(true))
                if (candidate.name == "Cabinet") return candidate;
        }
        return restaurantAnchor;
    }

    private void SetBackpackHidden()
    {
        if (_backpackPresentation != null) _backpackPresentation.SetActive(false);
    }

    private void FinishIngredientPresentation()
    {
        if (_ingredientTransferRoutine != null) StopCoroutine(_ingredientTransferRoutine);
        _ingredientTransferRoutine = null;
        if (_ingredientTransferVisual != null) Destroy(_ingredientTransferVisual);
        _ingredientTransferVisual = null;
        IsTransferringIngredients = false;
        if (_backpackPresentation != null) _backpackPresentation.SetActive(_wasBackpackActive);
        _backpackPresentation = null;
    }

    private static void RefreshRestaurantMenuOnEnter()
    {
        if (RestaurantPanel.instance == null)
            return;

        RestaurantPanel.instance.RefreshRestaurantUnits();
        RestaurantPanel.instance.RefreshOnOpen();
    }

    /// <summary>
    /// 离开餐厅（可绑定到 UI 按钮 OnClick）。始终恢复相机与玩家移动。
    /// </summary>
    public void LeaveRestaurant()
    {
        if (!_isEntered) return;
        _isEntered = false;
        FinishIngredientPresentation();
        HideDecorationPanel();
        RestoreRestaurantCamera();
        RestorePlayerMovement();
        SetRestaurantContentActive(false);
    }

    private static void HideDecorationPanel()
    {
        if (RestaurantDecorationPanelUI.Instance != null)
        {
            RestaurantDecorationPanelUI.Instance.HidePanel();
            return;
        }

        RestaurantDecorationPanelUI panel = FindObjectOfType<RestaurantDecorationPanelUI>(true);
        panel?.HidePanel();
    }

    private void SaveCameraStateBeforeEnter()
    {
        Camera cam = Camera.main;
        if (cam == null)
            cam = FindObjectOfType<Camera>();

        if (cam == null)
            return;

        _savedOrthoSize = cam.orthographicSize;
        _savedCameraPosition = cam.transform.position;
        _hasSavedCameraState = true;
    }

    private void RestoreRestaurantCamera()
    {
        if (moduleCameraView != null) moduleCameraView.Close();
        _restaurantCameraLease?.Dispose();
        _restaurantCameraLease = null;
        if (_runtimeCameraAnchor != null)
        {
            Destroy(_runtimeCameraAnchor);
            _runtimeCameraAnchor = null;
        }

        // 新框架会自动混合回请求栈的下一层；只有框架未初始化时才走旧恢复兜底。
        if (CameraService.Active == null)
        {
            CameraFollow cameraFollow = FindObjectOfType<CameraFollow>();
            if (cameraFollow != null)
            {
                if (_hasSavedCameraState)
                    cameraFollow.SnapBackToDefaultFollow(_savedOrthoSize, _savedCameraPosition);
                else
                    cameraFollow.SnapBackToDefaultFollow();
            }
        }

        _hasSavedCameraState = false;

        if (UIFloatingButtonGroup.Instance != null)
            UIFloatingButtonGroup.Instance.DeselectIfSelected(UIFloatingButtonGroup.Instance.RestaurantButtonIndex);
    }

    private void RestorePlayerMovement()
    {
        if (_lockedPlayer == null)
            return;

        RestorePlayerPose(_lockedPlayer);
        RestorePlayerPhysics();
        _lockedPlayer.SetPlayerMovementEnabled(_wasPlayerMoveEnabled);
        _lockedPlayer = null;
        _hasSavedPlayerPose = false;
    }

    private void SavePlayerPose(TopDownController player)
    {
        _savedPlayerPosition = player.transform.position;
        _savedPlayerRotation = player.transform.rotation;
        _hasSavedPlayerPose = true;
    }

    private void SnapPlayerToSeat(TopDownController player)
    {
        Transform seat = ResolvePlayerSeatAnchor();
        if (seat == null)
            return;

        Rigidbody body = player.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = seat.position;
            body.rotation = seat.rotation;
            player.transform.SetPositionAndRotation(seat.position, seat.rotation);
            return;
        }

        player.transform.SetPositionAndRotation(seat.position, seat.rotation);
    }

    private void LockPlayerPhysics(TopDownController player)
    {
        _lockedPlayerBody = player.GetComponent<Rigidbody>();
        if (_lockedPlayerBody == null)
            return;

        _wasPlayerBodyKinematic = _lockedPlayerBody.isKinematic;
        _lockedPlayerBody.velocity = Vector3.zero;
        _lockedPlayerBody.angularVelocity = Vector3.zero;
        _lockedPlayerBody.isKinematic = true;
    }

    private void RestorePlayerPhysics()
    {
        if (_lockedPlayerBody == null)
            return;

        _lockedPlayerBody.velocity = Vector3.zero;
        _lockedPlayerBody.angularVelocity = Vector3.zero;
        _lockedPlayerBody.isKinematic = _wasPlayerBodyKinematic;
        _lockedPlayerBody = null;
    }

    private void RestorePlayerPose(TopDownController player)
    {
        if (!_hasSavedPlayerPose || !restorePlayerPoseOnExit)
            return;

        Rigidbody body = player.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = _savedPlayerPosition;
            body.rotation = _savedPlayerRotation;
            // The camera evaluates in LateUpdate, before the next physics step can publish this teleport.
            player.transform.SetPositionAndRotation(_savedPlayerPosition, _savedPlayerRotation);
            return;
        }

        player.transform.SetPositionAndRotation(_savedPlayerPosition, _savedPlayerRotation);
    }

    private Transform ResolvePlayerSeatAnchor()
    {
        if (playerSeatAnchor != null)
            return playerSeatAnchor;
        if (restaurantAnchor != null)
            return restaurantAnchor;
        return transform;
    }

    private void SetRestaurantContentActive(bool active)
    {
        if (restaurantActiveContent == null) return;
        if (restaurantActiveContent.activeSelf != active)
            restaurantActiveContent.SetActive(active);
    }

    private Vector3 GetAnchorPosition()
    {
        return restaurantAnchor != null ? restaurantAnchor.position : transform.position;
    }

    private void AcquireRestaurantCamera()
    {
        if (moduleCameraView != null) { moduleCameraView.Open(); return; }
        CameraDirector director = CameraService.Active;
        Transform anchor = restaurantAnchor != null ? restaurantAnchor : transform;
        if (director == null || anchor == null)
            return;

        _restaurantCameraLease?.Dispose();
        _hasCachedCameraBounds = false;

        float requestedSize = adjustOrthoSize && cameraOrthoSize > 0f
            ? cameraOrthoSize
            : director.CurrentPose.OrthographicSize;
        if (cameraProfile != null && cameraProfile.orthographicSize > 0f)
            requestedSize = cameraProfile.orthographicSize;

        CameraDamping damping = cameraProfile != null
            ? cameraProfile.damping
            : new CameraDamping(0.15f, 0.15f, 0.18f);
        float dragSensitivity = cameraProfile != null ? cameraProfile.dragSensitivity : 1f;
        bool blockDragOverUi = cameraProfile == null || cameraProfile.blockDragWhenPointerOverUi;

        // 兼容旧配置中的 Y 对焦偏移：沿当前镜头平面的屏幕 Up 方向移动锚点。
        CameraPlane plane = CameraPlane.FromRotation(director.CurrentPose.Rotation, anchor.position);
        if (_runtimeCameraAnchor != null)
            Destroy(_runtimeCameraAnchor);
        _runtimeCameraAnchor = new GameObject("~RestaurantCameraAnchor");
        _runtimeCameraAnchor.hideFlags = HideFlags.HideAndDontSave;
        _runtimeCameraAnchor.transform.SetParent(anchor, false);
        _runtimeCameraAnchor.transform.position = anchor.position - plane.Up * cameraYFocusOffset;
        Transform effectiveAnchor = _runtimeCameraAnchor.transform;

        RestaurantPanCameraSource source = new RestaurantPanCameraSource(
            effectiveAnchor,
            director.CurrentPose,
            requestedSize,
            dragSensitivity,
            blockDragOverUi,
            damping,
            ResolveRestaurantBounds);

        float blendIn = cameraProfile != null ? cameraProfile.blendIn : 0.3f;
        float blendOut = cameraProfile != null ? cameraProfile.blendOut : 0.25f;
        _restaurantCameraLease = director.AcquireShot(
            this,
            source,
            new CameraShotOptions(50, blendIn, blendOut, "Restaurant"));
    }

    private CameraPlanarBounds ResolveRestaurantBounds(CameraPlane plane)
    {
        if (_hasCachedCameraBounds)
            return _cachedCameraBounds;

        Bounds worldBounds;
        bool found = false;
        if (cameraBounds != null)
        {
            worldBounds = cameraBounds.bounds;
            found = true;
        }
        else
        {
            GameObject boundsRoot = ResolveCameraBoundsRoot();
            found = CameraBoundsUtility.TryCollectRendererBounds(boundsRoot, out worldBounds);
            if (!found && boundsRoot != restaurantActiveContent)
                found = CameraBoundsUtility.TryCollectRendererBounds(restaurantActiveContent, out worldBounds);
        }

        _cachedCameraBounds = found
            ? CameraPlanarBounds.FromWorldBounds(worldBounds, plane)
            : default;
        _hasCachedCameraBounds = true;
        return _cachedCameraBounds;
    }

    private GameObject ResolveCameraBoundsRoot()
    {
        if (cameraBoundsRoot != null)
            return cameraBoundsRoot;

        if (restaurantAnchor != null && restaurantAnchor.parent != null)
            return restaurantAnchor.parent.gameObject;

        return restaurantActiveContent;
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 anchorPos = GetAnchorPosition();
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(anchorPos, 0.35f);
        Gizmos.DrawLine(transform.position, anchorPos);
    }
}
