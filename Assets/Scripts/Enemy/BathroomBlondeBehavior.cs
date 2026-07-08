using UnityEngine;
using UnityEngine.AI;

public class BathroomBlondeBehavior : MonoBehaviour
{
    enum BathroomBlondeState
    {
        Hidden,
        EmergingFromMirror,
        Retreating
    }

    [Header("Hazards")]
    public BathroomBlondeMirror mirrorPrefab;
    public BathroomBlondeDrain drainPrefab;
    public Transform[] hazardSpawnPoints;
    public bool useWaterSourcesAsSpawnAnchors = true;
    public float mirrorSpawnInterval = 18f;
    public float drainSpawnInterval = 12f;
    public int maxActiveMirrors = 3;
    public int maxActiveDrains = 4;
    public float spawnRadius = 9f;
    public float spawnNavMeshSampleRadius = 4f;

    [Header("Manifestation")]
    public GameObject hiddenVisualRoot;
    public GameObject emergingVisualRoot;
    public Transform bodyRoot;
    public float hiddenLocalY = -1.5f;
    public float emergedLocalY = 0f;
    public float emergeDuration = 5f;
    public float retreatDuration = 1.5f;
    public float riseSpeed = 2.5f;

    [Header("Camera Effects")]
    public PlayerVignetteEffect cameraEffects;
    [Range(0f, 1f)] public float mirrorThreatVignette = 0.75f;
    public float emergeShakeAmplitude = 0.8f;
    public float emergeShakeFrequency = 10f;
    public float emergeShakeDuration = 0.35f;

    private BathroomBlondeState state = BathroomBlondeState.Hidden;
    private BathroomBlondeMirror activeMirror;
    private PlayerStatus activeVictim;
    private float mirrorSpawnTimer;
    private float drainSpawnTimer;
    private float stateTimer;
    private Vector3 hiddenPosition;

    void Start()
    {
        hiddenPosition = transform.position;
        mirrorSpawnTimer = Random.Range(2f, Mathf.Max(2f, mirrorSpawnInterval));
        drainSpawnTimer = Random.Range(2f, Mathf.Max(2f, drainSpawnInterval));
        ResolveCameraEffects();
        EnterHidden(true);
    }

    void Update()
    {
        ResolveCameraEffects();
        UpdateHazardSpawning();

        switch (state)
        {
            case BathroomBlondeState.Hidden:
                UpdateHidden();
                break;
            case BathroomBlondeState.EmergingFromMirror:
                UpdateEmerging();
                break;
            case BathroomBlondeState.Retreating:
                UpdateRetreating();
                break;
        }
    }

    void UpdateHazardSpawning()
    {
        mirrorSpawnTimer -= Time.deltaTime;
        drainSpawnTimer -= Time.deltaTime;

        if (mirrorSpawnTimer <= 0f)
        {
            mirrorSpawnTimer = mirrorSpawnInterval;
            TrySpawnMirror();
        }

        if (drainSpawnTimer <= 0f)
        {
            drainSpawnTimer = drainSpawnInterval;
            TrySpawnDrain();
        }
    }

    void UpdateHidden()
    {
        MoveBodyToward(hiddenLocalY, riseSpeed);
        ClearCameraEffect();
    }

    void UpdateEmerging()
    {
        if (activeMirror == null || activeVictim == null || activeVictim.IsDead())
        {
            BeginRetreat();
            return;
        }

        FollowActiveMirror();
        FaceTarget(activeVictim.transform.position);
        UpdateCameraEffect(mirrorThreatVignette);

        stateTimer -= Time.deltaTime;
        float percent = emergeDuration > 0f ? 1f - Mathf.Clamp01(stateTimer / emergeDuration) : 1f;
        SetBodyLocalY(Mathf.Lerp(hiddenLocalY, emergedLocalY, percent));
    }

    void UpdateRetreating()
    {
        stateTimer -= Time.deltaTime;
        MoveBodyToward(hiddenLocalY, riseSpeed * 2f);
        UpdateCameraEffect(mirrorThreatVignette * 0.35f);

        if (stateTimer <= 0f)
            EnterHidden(false);
    }

    public void BeginMirrorEmergence(BathroomBlondeMirror mirror, PlayerStatus victim, Transform emergePoint)
    {
        if (mirror == null || victim == null || victim.IsDead()) return;

        activeMirror = mirror;
        activeVictim = victim;
        state = BathroomBlondeState.EmergingFromMirror;
        stateTimer = emergeDuration;
        FollowActiveMirror();
        SetBodyLocalY(hiddenLocalY);
        UpdateVisuals();

        if (cameraEffects != null)
        {
            cameraEffects.Pulse(mirrorThreatVignette, emergeShakeDuration);
            cameraEffects.Shake(emergeShakeAmplitude, emergeShakeFrequency, emergeShakeDuration);
        }
    }

    public void CancelMirrorEmergence(BathroomBlondeMirror mirror)
    {
        if (mirror != activeMirror) return;
        BeginRetreat();
    }

    public void CompleteMirrorSwallow(BathroomBlondeMirror mirror)
    {
        if (mirror != activeMirror) return;
        BeginRetreat();
    }

    void BeginRetreat()
    {
        activeMirror = null;
        activeVictim = null;
        state = BathroomBlondeState.Retreating;
        stateTimer = retreatDuration;
        UpdateVisuals();
    }

    void EnterHidden(bool immediate)
    {
        activeMirror = null;
        activeVictim = null;
        state = BathroomBlondeState.Hidden;
        transform.position = hiddenPosition;

        if (immediate)
            SetBodyLocalY(hiddenLocalY);

        UpdateVisuals();
        ClearCameraEffect();
    }

    void FollowActiveMirror()
    {
        if (activeMirror == null) return;

        Transform emergePoint = activeMirror.GetEmergencePoint();
        Transform source = emergePoint != null ? emergePoint : activeMirror.transform;
        transform.SetPositionAndRotation(source.position, source.rotation);
    }

    void TrySpawnMirror()
    {
        if (mirrorPrefab == null) return;
        if (FindObjectsOfType<BathroomBlondeMirror>().Length >= maxActiveMirrors) return;

        Vector3 position;
        if (!TryChooseHazardPosition(out position)) return;

        BathroomBlondeMirror mirror = Instantiate(mirrorPrefab, position, Quaternion.identity);
        mirror.Initialize(this);
    }

    void TrySpawnDrain()
    {
        if (drainPrefab == null) return;
        if (FindObjectsOfType<BathroomBlondeDrain>().Length >= maxActiveDrains) return;

        Vector3 position;
        if (!TryChooseHazardPosition(out position)) return;

        BathroomBlondeDrain drain = Instantiate(drainPrefab, position, Quaternion.identity);
        drain.Initialize(this);
    }

    bool TryChooseHazardPosition(out Vector3 position)
    {
        position = transform.position;

        Transform explicitPoint = ChooseExplicitSpawnPoint();
        if (explicitPoint != null && TrySampleNavMesh(explicitPoint.position, spawnNavMeshSampleRadius, out position))
            return true;

        if (useWaterSourcesAsSpawnAnchors && TryChooseWaterSourcePosition(out position))
            return true;

        PlayerStatus player = FindClosestPlayer();
        Vector3 anchor = player != null ? player.transform.position : transform.position;
        Vector3 wanted = anchor + Random.insideUnitSphere * spawnRadius;
        wanted.y = anchor.y;
        return TrySampleNavMesh(wanted, spawnNavMeshSampleRadius, out position);
    }

    Transform ChooseExplicitSpawnPoint()
    {
        if (hazardSpawnPoints == null || hazardSpawnPoints.Length == 0)
            return null;

        int start = Random.Range(0, hazardSpawnPoints.Length);
        for (int i = 0; i < hazardSpawnPoints.Length; i++)
        {
            Transform point = hazardSpawnPoints[(start + i) % hazardSpawnPoints.Length];
            if (point != null)
                return point;
        }

        return null;
    }

    bool TryChooseWaterSourcePosition(out Vector3 position)
    {
        position = transform.position;
        WaterSourceDryable[] sources = FindObjectsOfType<WaterSourceDryable>();
        if (sources == null || sources.Length == 0) return false;

        int start = Random.Range(0, sources.Length);
        for (int i = 0; i < sources.Length; i++)
        {
            WaterSourceDryable source = sources[(start + i) % sources.Length];
            if (source == null || source.isDry) continue;

            Vector2 offset = Random.insideUnitCircle * spawnRadius;
            Vector3 wanted = source.transform.position + new Vector3(offset.x, 0f, offset.y);
            if (TrySampleNavMesh(wanted, spawnNavMeshSampleRadius, out position))
                return true;
        }

        return false;
    }

    PlayerStatus FindClosestPlayer()
    {
        PlayerStatus[] players = FindObjectsOfType<PlayerStatus>();
        PlayerStatus best = null;
        float bestDistance = float.PositiveInfinity;

        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus player = players[i];
            if (player == null || player.IsDead()) continue;

            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance >= bestDistance) continue;

            best = player;
            bestDistance = distance;
        }

        return best;
    }

    bool TrySampleNavMesh(Vector3 wanted, float sampleRadius, out Vector3 sampled)
    {
        sampled = wanted;
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(wanted, out hit, Mathf.Max(0.1f, sampleRadius), NavMesh.AllAreas))
            return false;

        sampled = hit.position;
        return true;
    }

    void MoveBodyToward(float targetY, float speed)
    {
        if (bodyRoot == null) return;

        Vector3 local = bodyRoot.localPosition;
        local.y = Mathf.MoveTowards(local.y, targetY, speed * Time.deltaTime);
        bodyRoot.localPosition = local;
    }

    void SetBodyLocalY(float y)
    {
        if (bodyRoot == null) return;

        Vector3 local = bodyRoot.localPosition;
        local.y = y;
        bodyRoot.localPosition = local;
    }

    void FaceTarget(Vector3 target)
    {
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.001f) return;

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(direction.normalized),
            Time.deltaTime * 8f);
    }

    void UpdateVisuals()
    {
        bool hidden = state == BathroomBlondeState.Hidden;
        bool emerging = state == BathroomBlondeState.EmergingFromMirror;

        if (hiddenVisualRoot != null)
            hiddenVisualRoot.SetActive(hidden);

        if (emergingVisualRoot != null)
            emergingVisualRoot.SetActive(emerging || state == BathroomBlondeState.Retreating);
    }

    void ResolveCameraEffects()
    {
        if (cameraEffects != null) return;
        cameraEffects = FindObjectOfType<PlayerVignetteEffect>();
    }

    void UpdateCameraEffect(float intensity)
    {
        if (cameraEffects != null)
            cameraEffects.SetThreatIntensity(intensity);
    }

    void ClearCameraEffect()
    {
        if (cameraEffects != null)
        {
            cameraEffects.ClearThreatIntensity();
            cameraEffects.StopShake();
        }
    }

    void OnDestroy()
    {
        ClearCameraEffect();
    }
}
