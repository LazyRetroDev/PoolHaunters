using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Collections;

public class TimeCamper : MonoBehaviour
{
    [Header("Detection")]
    public Transform player;
    public float detectionRadius = 2.5f;

    [Header("Countdown")]
    public float minCountdown = 10f;
    public float maxCountdown = 10f;

    [Header("Damage")]
    public float damagePerSecond = 90f;
    public float damageDuration = 5f;
    public float damageRadius = 2.5f;

    [Header("Teleport")]
    public float teleportInterval = 45f;

    [Header("Water Contamination")]
    public bool contaminateWaterOnMark = true;
    public float waterContaminationRadius = 0f;
    public float waterContaminationInterval = 0.5f;
    public float markContaminationLifetime = 0f;
    public LayerMask playerContaminationMask = ~0;

    [Header("Visuals")]
    public GameObject redCirclePrefab;
    public GameObject beamPrefab;
    public GameObject contaminationPrefab;

    [Header("Camera Effects")]
    public PlayerVignetteEffect cameraEffects;
    [Range(0f, 1f)] public float countdownMinVignette = 0.18f;
    [Range(0f, 1f)] public float countdownMaxVignette = 0.75f;
    [Range(0f, 1f)] public float beamVignette = 0.8f;
    [Range(0f, 1f)] public float beamPulseIntensity = 0.9f;
    public float beamPulseDuration = 0.6f;

    [Header("Countdown UI")]
    public TextMeshProUGUI countdownText;

    [Header("Clone")]
    public bool isClone = false;

    private float countdownTimer;
    private float countdownDuration;
    private float damageTimer;
    private float teleportTimer;
    private bool isLeaving;
    private bool spawnedCloneForDeath;

    private GameObject redCircleInstance;
    private GameObject beamInstance;
    private PlayerStatus playerStatus;
    private NavMeshAgent agent;

    enum State { WaitingForPlayer, Countdown, Beam, Leaving }
    State currentState = State.WaitingForPlayer;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        ResolvePlayerReferences();
        ResolveCameraEffects();
        ResetCycle();
    }

    void Update()
    {
        ResolvePlayerReferences();
        ResolveCameraEffects();

        switch (currentState)
        {
            case State.WaitingForPlayer:
                teleportTimer -= Time.deltaTime;
                if (PlayerIsInImpactArea())
                    StartCountdown();
                else if (teleportTimer <= 0f)
                    StartLeaving();
                break;

            case State.Countdown:
                UpdateCountdown();
                break;

            case State.Beam:
                UpdateBeamEffect();
                damageTimer -= Time.deltaTime;
                DamagePlayerInRadius();
                if (damageTimer <= 0f)
                    StartLeaving();
                break;
        }
    }

    public float GetImpactRadius()
    {
        return Mathf.Max(detectionRadius, damageRadius);
    }

    void ResolvePlayerReferences()
    {
        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
                player = playerObject.transform;
        }

        if (playerStatus == null && player != null)
            playerStatus = player.GetComponent<PlayerStatus>();
    }

    void ResolveCameraEffects()
    {
        if (cameraEffects != null) return;
        cameraEffects = FindObjectOfType<PlayerVignetteEffect>();
    }

    bool PlayerIsInImpactArea()
    {
        if (player == null) return false;
        return Vector3.Distance(transform.position, player.position) <= detectionRadius;
    }

    void StartCountdown()
    {
        currentState = State.Countdown;
        countdownTimer = Random.Range(minCountdown, maxCountdown);
        countdownDuration = Mathf.Max(0.01f, countdownTimer);
    }

    void UpdateCountdown()
    {
        countdownTimer -= Time.deltaTime;
        UpdateCountdownEffect();

        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
            countdownText.text = Mathf.Ceil(countdownTimer).ToString();
        }

        if (countdownTimer <= 0f)
        {
            if (countdownText != null)
                countdownText.gameObject.SetActive(false);

            StartBeam();
        }
    }

    void UpdateCountdownEffect()
    {
        if (cameraEffects == null) return;

        float progress = 1f - Mathf.Clamp01(countdownTimer / Mathf.Max(0.01f, countdownDuration));
        cameraEffects.SetThreatIntensity(Mathf.Lerp(countdownMinVignette, countdownMaxVignette, progress));
    }

    void StartBeam()
    {
        currentState = State.Beam;
        damageTimer = damageDuration;

        if (cameraEffects != null)
            cameraEffects.Pulse(beamPulseIntensity, beamPulseDuration);

        if (beamPrefab != null)
            beamInstance = Instantiate(beamPrefab, transform.position, Quaternion.identity);
    }

    void UpdateBeamEffect()
    {
        if (cameraEffects != null)
            cameraEffects.SetThreatIntensity(beamVignette);
    }

    void DamagePlayerInRadius()
    {
        if (playerStatus == null || player == null) return;

        float dist = Vector3.Distance(transform.position, player.position);
        if (dist > damageRadius) return;

        bool killedPlayer = playerStatus.TakeDamage(damagePerSecond * Time.deltaTime);
        if (!killedPlayer) return;

        TransformKilledPlayerIntoClone();
        StartLeaving();
    }

    void TransformKilledPlayerIntoClone()
    {
        if (spawnedCloneForDeath) return;
        if (EnemySpawner.Instance == null || playerStatus == null) return;
        if (TimeCamperManager.Instance == null || !TimeCamperManager.Instance.CanSpawn()) return;

        spawnedCloneForDeath = true;
        Vector3 clonePosition = playerStatus.transform.position;
        EnemySpawner.Instance.SpawnTimeCamperAt(clonePosition, isClone: true);
        playerStatus.ApplyDeathTransformation();
    }

    void StartLeaving()
    {
        if (isLeaving) return;
        ClearCameraEffect();
        StartCoroutine(LeaveAndTeleport());
    }

    IEnumerator LeaveAndTeleport()
    {
        isLeaving = true;
        currentState = State.Leaving;

        CleanupVisuals();
        LeaveContaminationMark();

        if (TimeCamperManager.Instance != null)
            TimeCamperManager.Instance.Unregister(this);

        yield return new WaitForSeconds(0.1f);

        Vector3 newPos;
        if (EnemySpawner.Instance != null && EnemySpawner.Instance.TryGetValidSpawnPosition(out newPos))
        {
            TeleportTo(newPos);
            ResetCycle();

            if (TimeCamperManager.Instance != null)
                TimeCamperManager.Instance.Register(this);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void TeleportTo(Vector3 newPos)
    {
        if (agent != null && agent.enabled)
            agent.Warp(newPos);
        else
            transform.position = newPos;
    }

    void ResetCycle()
    {
        isLeaving = false;
        spawnedCloneForDeath = false;
        currentState = State.WaitingForPlayer;
        teleportTimer = teleportInterval;
        damageTimer = 0f;
        ClearCameraEffect();

        if (countdownText != null)
            countdownText.gameObject.SetActive(false);

        SpawnWarningCircle();
    }

    void ClearCameraEffect()
    {
        if (cameraEffects != null)
            cameraEffects.ClearThreatIntensity();
    }

    void CleanupVisuals()
    {
        if (redCircleInstance != null) Destroy(redCircleInstance);
        if (beamInstance != null) Destroy(beamInstance);
        if (countdownText != null) countdownText.gameObject.SetActive(false);
    }

    void SpawnWarningCircle()
    {
        if (redCirclePrefab == null) return;

        redCircleInstance = Instantiate(redCirclePrefab,
            new Vector3(transform.position.x, transform.position.y + 0.05f, transform.position.z),
            Quaternion.identity);
    }

    void LeaveContaminationMark()
    {
        GameObject mark = null;

        if (contaminationPrefab != null)
        {
            mark = Instantiate(contaminationPrefab,
                new Vector3(transform.position.x, transform.position.y + 0.05f, transform.position.z),
                Quaternion.identity);
        }

        if (!contaminateWaterOnMark) return;

        if (mark == null)
            mark = new GameObject("TimeCamperWaterContaminationZone");

        mark.transform.position = new Vector3(transform.position.x, transform.position.y + 0.05f, transform.position.z);

        WaterContaminationZone zone = mark.GetComponent<WaterContaminationZone>();
        if (zone == null)
            zone = mark.AddComponent<WaterContaminationZone>();

        zone.radius = GetContaminationRadius();
        zone.contaminationQuality = WaterQuality.Contaminated;
        zone.contaminateInterval = waterContaminationInterval;
        zone.lifetime = markContaminationLifetime;
        zone.playerMask = playerContaminationMask;
    }

    float GetContaminationRadius()
    {
        return waterContaminationRadius > 0f ? waterContaminationRadius : detectionRadius;
    }

    void OnDestroy()
    {
        ClearCameraEffect();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
        Gizmos.color = new Color(1f, 0.3f, 0f);
        Gizmos.DrawWireSphere(transform.position, damageRadius);
        Gizmos.color = new Color(0.2f, 1f, 0.35f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, GetContaminationRadius());
    }
}
