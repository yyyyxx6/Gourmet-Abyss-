using System;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(SphereCollider))]
public sealed class DeathLootCrate : MonoBehaviour
{
    [SerializeField] private SphereCollider pickupTrigger;
    [SerializeField] private GameObject collectionEffectPrefab;
    [SerializeField] private float collectionEffectLifetime = 4f;
    [SerializeField] private float overlapCheckInterval = 0.2f;

    private string crateId;
    private bool isCollecting;
    private bool collected;
    private float nextOverlapCheck;

    public string CrateId { get { return crateId; } }

    private void Awake()
    {
        EnsureTrigger();
    }

    public void Initialize(string crateId)
    {
        if (string.IsNullOrWhiteSpace(crateId))
            throw new ArgumentException("A death crate ID is required.", nameof(crateId));
        if (collected || isCollecting)
            throw new InvalidOperationException("A resolved or collecting death crate cannot be initialized again.");
        if (!string.IsNullOrEmpty(this.crateId) && this.crateId != crateId)
            throw new InvalidOperationException("An initialized death crate cannot change identity.");

        this.crateId = crateId;
        EnsureTrigger();
        nextOverlapCheck = 0f;
    }

    private void Update()
    {
        if (!CanAttemptCollection() || Time.unscaledTime < nextOverlapCheck) return;
        nextOverlapCheck = Time.unscaledTime + Mathf.Max(0.05f, overlapCheckInterval);

        // Also handles spawning with the player already inside the trigger.
        foreach (TopDownController player in FindObjectsOfType<TopDownController>())
            if (TryCollect(player)) return;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryCollect(ResolvePlayer(other));
    }

    private void OnTriggerStay(Collider other)
    {
        TryCollect(ResolvePlayer(other));
    }

    public bool TryCollect(TopDownController player)
    {
        if (!CanAttemptCollection() || player == null || !player.isActiveAndEnabled || player.isDead)
            return false;
        if (!IsPlayerInRange(player))
            return false;
        if (pickupTrigger == null || !pickupTrigger.enabled || !pickupTrigger.gameObject.activeInHierarchy)
            return false;

        isCollecting = true;
        try
        {
            if (!RunSessionManager.Instance.TryCollectDeathCrate(crateId)) return false;
            collected = true;
            pickupTrigger.enabled = false;
            PlayCollectionFeedback();
            return true;
        }
        finally
        {
            isCollecting = false;
        }
    }

    public bool IsPlayerInRange(TopDownController player)
    {
        if (player == null || pickupTrigger == null || !player.CompareTag("Player") ||
            player.gameObject.scene != gameObject.scene)
            return false;

        Vector3 scale = pickupTrigger.transform.lossyScale;
        float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        float radius = Mathf.Max(0f, pickupTrigger.radius) * radiusScale + 0.05f;
        Vector3 center = pickupTrigger.transform.TransformPoint(pickupTrigger.center);
        return (player.transform.position - center).sqrMagnitude <= radius * radius;
    }

    private bool CanAttemptCollection()
    {
        if (!isActiveAndEnabled || collected || isCollecting || string.IsNullOrEmpty(crateId)) return false;
        RunSessionManager session = RunSessionManager.Instance;
        if (session == null || !session.IsActive || session.IsEndingRun) return false;
        if (LevelManager.instance != null && LevelManager.instance.IsTransitioning()) return false;
        return true;
    }

    private void EnsureTrigger()
    {
        if (pickupTrigger == null) pickupTrigger = GetComponent<SphereCollider>();
        if (pickupTrigger != null) pickupTrigger.isTrigger = true;
    }

    private static TopDownController ResolvePlayer(Collider other)
    {
        if (other == null) return null;
        TopDownController player = other.GetComponentInParent<TopDownController>();
        if (player == null && other.attachedRigidbody != null)
            player = other.attachedRigidbody.GetComponent<TopDownController>();
        return player;
    }

    private void PlayCollectionFeedback()
    {
        try
        {
            AudioManager.Instance?.PlayAudio("2");
            if (collectionEffectPrefab == null) return;
            Vector3 position = pickupTrigger.transform.TransformPoint(pickupTrigger.center);
            GameObject effect = Instantiate(collectionEffectPrefab, position, Quaternion.identity);
            SceneManager.MoveGameObjectToScene(effect, gameObject.scene);
            Destroy(effect, Mathf.Max(0.1f, collectionEffectLifetime));
        }
        catch (Exception error)
        {
            // Feedback must not interrupt disposal after the service consumed this crate.
            Debug.LogException(error, this);
        }
    }
}
