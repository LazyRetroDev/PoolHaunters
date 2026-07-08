using UnityEngine;

public class BathroomBlondeDrain : MonoBehaviour
{
    enum DrainState
    {
        Armed,
        Holding,
        Broken,
        Spent
    }

    [Header("Trap")]
    public float holdDuration = 3f;
    public float damagePerSecond = 4f;
    public bool contaminateWaterOnHold = true;
    public bool destroyAfterTrigger = false;
    public Transform holdPoint;
    public Vector3 holdOffset = new Vector3(0f, 0.45f, 0f);

    [Header("Durability")]
    public float maxDurability = 35f;
    public float waterParticleDamage = 8f;
    public bool cleanWaterDamages = true;
    public bool chemicalWaterDamages = true;

    [Header("Visuals")]
    public GameObject armedVisualRoot;
    public GameObject holdingVisualRoot;
    public GameObject brokenVisualRoot;

    private BathroomBlondeBehavior owner;
    private DrainState state = DrainState.Armed;
    private PlayerStatus heldStatus;
    private Transform heldPlayer;
    private PlayerMovement heldMovement;
    private Rigidbody heldRigidbody;
    private bool heldMovementWasEnabled;
    private bool heldRigidbodyWasKinematic;
    private bool heldRigidbodyUsedGravity;
    private float holdTimer;
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
        if (state == DrainState.Holding)
            UpdateHolding();
    }

    void OnTriggerEnter(Collider other)
    {
        if (state != DrainState.Armed) return;

        PlayerStatus status = other.GetComponentInParent<PlayerStatus>();
        if (status == null || status.IsDead() || !status.CanAct()) return;

        BeginHold(status);
    }

    void BeginHold(PlayerStatus victim)
    {
        heldStatus = victim;
        heldPlayer = victim.transform;
        holdTimer = holdDuration;
        state = DrainState.Holding;
        StoreAndBlockVictimMovement(victim);
        UpdateVisuals();
    }

    void UpdateHolding()
    {
        if (heldStatus == null || heldPlayer == null || heldStatus.IsDead())
        {
            ReleaseVictim();
            return;
        }

        HoldPlayer();
        heldStatus.TakeDamage(damagePerSecond * Time.deltaTime);

        if (contaminateWaterOnHold)
            heldStatus.ContaminateWater();

        holdTimer -= Time.deltaTime;
        if (holdTimer <= 0f)
            ReleaseVictim();
    }

    void ReleaseVictim()
    {
        RestoreVictimMovement(heldStatus);
        ClearHeldReferences();

        if (destroyAfterTrigger)
        {
            state = DrainState.Spent;
            Destroy(gameObject);
            return;
        }

        state = DrainState.Armed;
        UpdateVisuals();
    }

    public void ApplyWater(WaterQuality quality, float amount)
    {
        if (amount <= 0f || state == DrainState.Broken || state == DrainState.Spent)
            return;

        if (quality == WaterQuality.Clean && cleanWaterDamages)
            DamageDrain(amount);
        else if (quality == WaterQuality.ChemicallyEnhanced && chemicalWaterDamages)
            DamageDrain(amount);
    }

    public void ReceiveWaterHit(Vector3 sourcePosition)
    {
        if (state == DrainState.Broken || state == DrainState.Spent)
            return;

        DamageDrain(waterParticleDamage);
    }

    void DamageDrain(float damage)
    {
        durability -= damage;
        if (durability <= 0f)
            BreakDrain();
    }

    public void BreakDrain()
    {
        if (state == DrainState.Broken || state == DrainState.Spent) return;

        RestoreVictimMovement(heldStatus);
        ClearHeldReferences();
        state = DrainState.Broken;
        UpdateVisuals();
        Destroy(gameObject, 0.25f);
    }

    void StoreAndBlockVictimMovement(PlayerStatus victim)
    {
        if (victim == null) return;

        heldMovement = victim.GetComponent<PlayerMovement>();
        heldRigidbody = victim.GetComponent<Rigidbody>();

        if (heldMovement != null)
        {
            heldMovementWasEnabled = heldMovement.enabled;
            heldMovement.enabled = false;
        }

        if (heldRigidbody != null)
        {
            heldRigidbodyWasKinematic = heldRigidbody.isKinematic;
            heldRigidbodyUsedGravity = heldRigidbody.useGravity;
            heldRigidbody.linearVelocity = Vector3.zero;
            heldRigidbody.angularVelocity = Vector3.zero;
            heldRigidbody.useGravity = false;
            heldRigidbody.isKinematic = true;
        }
    }

    void RestoreVictimMovement(PlayerStatus victim)
    {
        bool canRestore = victim != null && victim.CanAct();

        if (heldMovement != null)
            heldMovement.enabled = canRestore && heldMovementWasEnabled;

        if (heldRigidbody != null && canRestore)
        {
            heldRigidbody.isKinematic = heldRigidbodyWasKinematic;
            heldRigidbody.useGravity = heldRigidbodyUsedGravity;
        }
    }

    void ClearHeldReferences()
    {
        heldStatus = null;
        heldPlayer = null;
        heldMovement = null;
        heldRigidbody = null;
        heldMovementWasEnabled = false;
    }

    void HoldPlayer()
    {
        if (heldPlayer == null) return;

        Vector3 holdPosition = holdPoint != null
            ? holdPoint.position
            : transform.TransformPoint(holdOffset);

        Rigidbody body = heldPlayer.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = holdPosition;
            return;
        }

        heldPlayer.position = holdPosition;
    }

    void UpdateVisuals()
    {
        bool armed = state == DrainState.Armed;
        bool holding = state == DrainState.Holding;
        bool broken = state == DrainState.Broken;

        if (armedVisualRoot != null)
            armedVisualRoot.SetActive(armed);

        if (holdingVisualRoot != null)
            holdingVisualRoot.SetActive(holding);

        if (brokenVisualRoot != null)
            brokenVisualRoot.SetActive(broken);
    }

    void OnDestroy()
    {
        RestoreVictimMovement(heldStatus);
    }
}
