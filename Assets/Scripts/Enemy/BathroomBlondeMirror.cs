using UnityEngine;

public class BathroomBlondeMirror : MonoBehaviour
{
    enum MirrorState
    {
        Idle,
        Summoning,
        BlondeOut,
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
    public bool destroyWhenBlondeFinishes = true;

    [Header("Mirror Health")]
    public float maxDurability = 60f;
    public float waterParticleDamage = 8f;
    public bool cleanWaterDamages = true;
    public bool chemicalWaterDamages = true;

    [Header("Visuals")]
    public GameObject idleVisualRoot;
    public GameObject summoningVisualRoot;
    public GameObject blondeOutVisualRoot;
    public GameObject brokenVisualRoot;

    private BathroomBlondeBehavior owner;
    private MirrorState state = MirrorState.Idle;
    private int lookCount;
    private float lookTimer;
    private float durability;

    void Awake()
    {
        durability = maxDurability;
        ResolveOwner();
        UpdateVisuals();
    }

    public void Initialize(BathroomBlondeBehavior blondeOwner)
    {
        owner = blondeOwner;
    }

    void Update()
    {
        if (!EnemyAuthority.CanRunGameplay())
            return;

        lookTimer -= Time.deltaTime;

        if (state == MirrorState.Idle)
            UpdateIdleCurse();
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
        PlayerStatus[] players =
            FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);
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

        ResolveOwner();
        if (owner == null) return;

        state = MirrorState.Summoning;
        owner.BeginMirrorEmergence(this, victim, GetEmergencePoint());
        UpdateVisuals();
    }

    void ResolveOwner()
    {
        if (owner != null) return;
        owner = FindAnyObjectByType<BathroomBlondeBehavior>();
    }

    public void MarkBlondeOut()
    {
        if (state != MirrorState.Summoning) return;

        state = MirrorState.BlondeOut;
        UpdateVisuals();
    }

    public void DestroyAfterBlondeFinished()
    {
        state = MirrorState.Spent;
        UpdateVisuals();

        if (destroyWhenBlondeFinishes)
            Destroy(gameObject);
    }

    public void ApplyWater(WaterQuality quality, float amount)
    {
        if (!EnemyAuthority.CanRunGameplay())
            return;

        if (amount <= 0f || state == MirrorState.Broken || state == MirrorState.Spent)
            return;

        if (quality == WaterQuality.Clean && cleanWaterDamages)
            DamageMirror(amount);
        else if (quality == WaterQuality.ChemicallyEnhanced && chemicalWaterDamages)
            DamageMirror(amount);
    }

    public void ReceiveWaterHit(Vector3 sourcePosition)
    {
        if (!EnemyAuthority.CanRunGameplay())
            return;

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

        ResolveOwner();
        if (owner != null)
            owner.CancelMirrorEmergence(this);

        state = MirrorState.Broken;
        UpdateVisuals();
        Destroy(gameObject, 0.25f);
    }

    public Transform GetEmergencePoint()
    {
        return emergencePoint != null ? emergencePoint : transform;
    }

    void UpdateVisuals()
    {
        bool idle = state == MirrorState.Idle;
        bool summoning = state == MirrorState.Summoning;
        bool blondeOut = state == MirrorState.BlondeOut;
        bool broken = state == MirrorState.Broken;

        if (idleVisualRoot != null)
            idleVisualRoot.SetActive(idle);

        if (summoningVisualRoot != null)
            summoningVisualRoot.SetActive(summoning);

        if (blondeOutVisualRoot != null)
            blondeOutVisualRoot.SetActive(blondeOut);

        if (brokenVisualRoot != null)
            brokenVisualRoot.SetActive(broken);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, lookDistance);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(GetLookPoint(), 0.25f);
    }
}
