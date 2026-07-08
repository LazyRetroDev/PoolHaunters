using UnityEngine;
using UnityEngine.InputSystem;

public class BathroomBlondeMirror : MonoBehaviour
{
    enum MirrorState
    {
        Idle,
        Summoning,
        Broken,
        Spent
    }

    [Header("Curse")]
    public int looksRequired = 3;
    public float lookAngle = 12f;
    public float lookDistance = 12f;
    public float lookCooldown = 0.75f;
    public LayerMask lineOfSightMask = ~0;
    public bool requireLineOfSight = true;

    [Header("Summon")]
    public Transform emergencePoint;
    public Transform victimHoldPoint;
    public Vector3 victimHoldOffset = new Vector3(0f, 1.1f, 0.45f);
    public float summonDuration = 5f;
    public int escapeClicksRequired = 18;
    public bool destroyAfterEscape = true;
    public bool destroyAfterSwallow = true;

    [Header("Mirror Health")]
    public float maxDurability = 60f;
    public float waterParticleDamage = 8f;
    public bool cleanWaterDamages = true;
    public bool chemicalWaterDamages = true;

    [Header("Visuals")]
    public GameObject idleVisualRoot;
    public GameObject summoningVisualRoot;
    public GameObject brokenVisualRoot;

    private BathroomBlondeBehavior owner;
    private MirrorState state = MirrorState.Idle;
    private PlayerStatus trappedStatus;
    private Transform trappedPlayer;
    private PlayerMovement trappedMovement;
    private PlayerInventory trappedInventory;
    private WaterCannon trappedWaterCannon;
    private Rigidbody trappedRigidbody;
    private bool trappedMovementWasEnabled;
    private bool trappedInventoryWasEnabled;
    private bool trappedWaterCannonWasEnabled;
    private bool trappedRigidbodyWasKinematic;
    private bool trappedRigidbodyUsedGravity;
    private int lookCount;
    private int escapeClicks;
    private float lookTimer;
    private float summonTimer;
    private float durability;

    void Awake()
    {
        durability = maxDurability;
        UpdateVisuals();
    }

    public void Initialize(BathroomBlondeBehavior blondeOwner)
    {
        owner = blondeOwner;
    }

    void Update()
    {
        lookTimer -= Time.deltaTime;

        switch (state)
        {
            case MirrorState.Idle:
                UpdateIdleCurse();
                break;
            case MirrorState.Summoning:
                UpdateSummoning();
                break;
        }
    }

    void UpdateIdleCurse()
    {
        if (lookTimer > 0f) return;

        PlayerStatus lookingPlayer = FindLookingPlayer();
        if (lookingPlayer == null) return;

        lookTimer = lookCooldown;
        lookCount++;

        if (lookCount >= looksRequired)
            BeginSummoning(lookingPlayer);
    }

    PlayerStatus FindLookingPlayer()
    {
        PlayerStatus[] players = FindObjectsOfType<PlayerStatus>();
        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus status = players[i];
            if (status == null || status.IsDead()) continue;

            Transform view = FindPlayerCamera(status.transform);
            if (view != null && IsViewLookingAtMirror(view))
                return status;
        }

        return null;
    }

    Transform FindPlayerCamera(Transform playerRoot)
    {
        if (playerRoot == null) return null;

        Camera[] cameras = playerRoot.GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (cameras[i] != null && cameras[i].enabled)
                return cameras[i].transform;
        }

        return playerRoot;
    }

    bool IsViewLookingAtMirror(Transform view)
    {
        Vector3 origin = view.position;
        Vector3 targetPoint = GetLookPoint();
        Vector3 toMirror = targetPoint - origin;
        float distance = toMirror.magnitude;
        if (distance > lookDistance || distance <= 0.01f) return false;

        float angle = Vector3.Angle(view.forward, toMirror.normalized);
        if (angle > lookAngle) return false;

        if (!requireLineOfSight) return true;

        RaycastHit hit;
        if (!Physics.Raycast(origin, toMirror.normalized, out hit, distance, lineOfSightMask, QueryTriggerInteraction.Ignore))
            return true;

        return hit.collider != null && hit.collider.transform.IsChildOf(transform);
    }

    Vector3 GetLookPoint()
    {
        return transform.position + Vector3.up * 1.2f;
    }

    void BeginSummoning(PlayerStatus victim)
    {
        if (victim == null || victim.IsDead()) return;

        trappedStatus = victim;
        trappedPlayer = victim.transform;
        summonTimer = summonDuration;
        escapeClicks = 0;
        state = MirrorState.Summoning;
        StoreAndBlockVictimControls(victim);
        owner?.BeginMirrorEmergence(this, victim, GetEmergencePoint());
        UpdateVisuals();
    }

    void UpdateSummoning()
    {
        if (trappedStatus == null || trappedPlayer == null || trappedStatus.IsDead())
        {
            BreakMirror();
            return;
        }

        HoldTrappedPlayer();
        CountEscapeClicks();

        summonTimer -= Time.deltaTime;
        if (escapeClicks >= escapeClicksRequired)
        {
            EscapeMirror();
            return;
        }

        if (summonTimer <= 0f)
            SwallowVictim();
    }

    void CountEscapeClicks()
    {
        if (Mouse.current == null) return;
        if (Mouse.current.leftButton.wasPressedThisFrame)
            escapeClicks++;
    }

    void EscapeMirror()
    {
        RestoreVictimControls(trappedStatus);
        ClearTrappedReferences();
        owner?.CancelMirrorEmergence(this);
        state = MirrorState.Spent;
        UpdateVisuals();

        if (destroyAfterEscape)
            Destroy(gameObject);
    }

    void SwallowVictim()
    {
        PlayerStatus victim = trappedStatus;
        RestoreVictimControls(victim);
        ClearTrappedReferences();

        if (victim != null && !victim.IsDead())
            victim.Die();

        owner?.CompleteMirrorSwallow(this);
        state = MirrorState.Spent;
        UpdateVisuals();

        if (destroyAfterSwallow)
            Destroy(gameObject);
    }

    public void ApplyWater(WaterQuality quality, float amount)
    {
        if (amount <= 0f || state == MirrorState.Broken || state == MirrorState.Spent)
            return;

        if (quality == WaterQuality.Clean && cleanWaterDamages)
            DamageMirror(amount);
        else if (quality == WaterQuality.ChemicallyEnhanced && chemicalWaterDamages)
            DamageMirror(amount);
    }

    public void ReceiveWaterHit(Vector3 sourcePosition)
    {
        if (state == MirrorState.Broken || state == MirrorState.Spent)
            return;

        DamageMirror(waterParticleDamage);
    }

    void DamageMirror(float damage)
    {
        durability -= damage;
        if (durability <= 0f)
            BreakMirror();
    }

    public void BreakMirror()
    {
        if (state == MirrorState.Broken || state == MirrorState.Spent) return;

        RestoreVictimControls(trappedStatus);
        ClearTrappedReferences();
        owner?.CancelMirrorEmergence(this);
        state = MirrorState.Broken;
        UpdateVisuals();
        Destroy(gameObject, 0.25f);
    }

    public Transform GetEmergencePoint()
    {
        return emergencePoint != null ? emergencePoint : transform;
    }

    void StoreAndBlockVictimControls(PlayerStatus victim)
    {
        if (victim == null) return;

        trappedMovement = victim.GetComponent<PlayerMovement>();
        trappedInventory = victim.GetComponent<PlayerInventory>();
        trappedWaterCannon = victim.GetComponentInChildren<WaterCannon>();
        trappedRigidbody = victim.GetComponent<Rigidbody>();

        if (trappedMovement != null)
        {
            trappedMovementWasEnabled = trappedMovement.enabled;
            trappedMovement.enabled = false;
        }

        if (trappedInventory != null)
        {
            trappedInventoryWasEnabled = trappedInventory.enabled;
            trappedInventory.enabled = false;
        }

        if (trappedWaterCannon != null)
        {
            trappedWaterCannonWasEnabled = trappedWaterCannon.enabled;
            trappedWaterCannon.enabled = false;
        }

        if (trappedRigidbody != null)
        {
            trappedRigidbodyWasKinematic = trappedRigidbody.isKinematic;
            trappedRigidbodyUsedGravity = trappedRigidbody.useGravity;
            trappedRigidbody.linearVelocity = Vector3.zero;
            trappedRigidbody.angularVelocity = Vector3.zero;
            trappedRigidbody.useGravity = false;
            trappedRigidbody.isKinematic = true;
        }
    }

    void RestoreVictimControls(PlayerStatus victim)
    {
        bool canRestore = victim != null && victim.CanAct();

        if (trappedMovement != null)
            trappedMovement.enabled = canRestore && trappedMovementWasEnabled;

        if (trappedInventory != null)
            trappedInventory.enabled = canRestore && trappedInventoryWasEnabled;

        if (trappedWaterCannon != null)
            trappedWaterCannon.enabled = canRestore && trappedWaterCannonWasEnabled;

        if (trappedRigidbody != null && canRestore)
        {
            trappedRigidbody.isKinematic = trappedRigidbodyWasKinematic;
            trappedRigidbody.useGravity = trappedRigidbodyUsedGravity;
        }
    }

    void ClearTrappedReferences()
    {
        trappedStatus = null;
        trappedPlayer = null;
        trappedMovement = null;
        trappedInventory = null;
        trappedWaterCannon = null;
        trappedRigidbody = null;
        trappedMovementWasEnabled = false;
        trappedInventoryWasEnabled = false;
        trappedWaterCannonWasEnabled = false;
    }

    void HoldTrappedPlayer()
    {
        if (trappedPlayer == null) return;

        Vector3 holdPosition = victimHoldPoint != null
            ? victimHoldPoint.position
            : transform.TransformPoint(victimHoldOffset);

        TeleportTransform(trappedPlayer, holdPosition, transform.rotation);
    }

    void TeleportTransform(Transform target, Vector3 position, Quaternion rotation)
    {
        if (target == null) return;

        Rigidbody body = target.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = position;
            body.rotation = rotation;
            return;
        }

        target.SetPositionAndRotation(position, rotation);
    }

    void UpdateVisuals()
    {
        bool idle = state == MirrorState.Idle;
        bool summoning = state == MirrorState.Summoning;
        bool broken = state == MirrorState.Broken;

        if (idleVisualRoot != null)
            idleVisualRoot.SetActive(idle);

        if (summoningVisualRoot != null)
            summoningVisualRoot.SetActive(summoning);

        if (brokenVisualRoot != null)
            brokenVisualRoot.SetActive(broken);
    }

    void OnDestroy()
    {
        RestoreVictimControls(trappedStatus);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, lookDistance);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(GetLookPoint(), 0.25f);
    }
}
