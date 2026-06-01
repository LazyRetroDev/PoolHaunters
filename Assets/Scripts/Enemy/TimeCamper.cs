using UnityEngine;
using UnityEngine.AI;
using TMPro;
using System.Collections;

public class TimeCamper : MonoBehaviour
{
    [Header("Detection")]
    public Transform player;
    public float detectionRadius = 5f;

    [Header("Countdown")]
    public float minCountdown = 8f;
    public float maxCountdown = 14f;

    [Header("Damage")]
    public float damagePerSecond = 90f;
    public float damageDuration = 5f;
    public float damageRadius = 2.5f;

    [Header("Teleport")]
    public float teleportInterval = 45f;

    [Header("Visuals")]
    public GameObject redCirclePrefab;
    public GameObject beamPrefab;
    public GameObject contaminationPrefab;

    [Header("Countdown UI")]
    public TextMeshProUGUI countdownText;

    [Header("Clone")]
    public bool isClone = false;

    private float countdownDuration;
    private float countdownTimer;
    private float damageTimer;
    private float teleportTimer;
    private bool isLeaving;
    private bool spawnedCloneForDeath;

    private GameObject redCircleInstance;
    private GameObject beamInstance;
    private PlayerStatus playerStatus;
    private NavMeshAgent agent;

    enum State { Idle, Countdown, Beam, Leaving }
    State currentState = State.Idle;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        ResolvePlayerReferences();
        ResetTimers();
        SpawnWarningCircle();

        if (countdownText != null)
            countdownText.gameObject.SetActive(false);
    }

    void Update()
    {
        ResolvePlayerReferences();

        switch (currentState)
        {
            case State.Idle:
                CheckForPlayer();
                teleportTimer -= Time.deltaTime;
                if (teleportTimer <= 0f)
                    StartLeaving();
                break;

            case State.Countdown:
                UpdateCountdown();
                break;

            case State.Beam:
                damageTimer -= Time.deltaTime;
                DamagePlayerInRadius();
                if (damageTimer <= 0f)
                    StartLeaving();
                break;
        }
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

    void CheckForPlayer()
    {
        if (player == null) return;

        float dist = Vector3.Distance(transform.position, player.position);
        if (dist <= detectionRadius)
            currentState = State.Countdown;
    }

    void UpdateCountdown()
    {
        countdownTimer -= Time.deltaTime;

        if (countdownText != null)
        {
            countdownText.gameObject.SetActive(true);
            countdownText.text = Mathf.Ceil(countdownTimer).ToString();
        }

        if (countdownTimer <= 0f)
        {
            if (countdownText != null)
                countdownText.gameObject.SetActive(false);

            StartCoroutine(FireBeam());
        }
    }

    IEnumerator FireBeam()
    {
        currentState = State.Beam;
        damageTimer = damageDuration;

        if (beamPrefab != null)
            beamInstance = Instantiate(beamPrefab, transform.position, Quaternion.identity);

        yield return null;
    }

    void DamagePlayerInRadius()
    {
        if (playerStatus == null || player == null) return;

        float dist = Vector3.Distance(transform.position, player.position);
        if (dist > damageRadius) return;

        bool killedPlayer = playerStatus.TakeDamage(damagePerSecond * Time.deltaTime);
        if (killedPlayer)
        {
            SpawnCloneFromPlayerDeath();
            StartLeaving();
        }
    }

    void SpawnCloneFromPlayerDeath()
    {
        if (spawnedCloneForDeath) return;
        if (EnemySpawner.Instance == null) return;
        if (TimeCamperManager.Instance == null || !TimeCamperManager.Instance.CanSpawn()) return;

        spawnedCloneForDeath = true;
        EnemySpawner.Instance.SpawnTimeCamper(isClone: true);
    }

    void StartLeaving()
    {
        if (isLeaving) return;
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
            ResetState();

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

    void ResetState()
    {
        isLeaving = false;
        currentState = State.Idle;
        spawnedCloneForDeath = false;
        ResetTimers();
        SpawnWarningCircle();
    }

    void ResetTimers()
    {
        teleportTimer = teleportInterval;
        countdownDuration = Random.Range(minCountdown, maxCountdown);
        countdownTimer = countdownDuration;
        damageTimer = 0f;
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
        if (contaminationPrefab == null) return;

        Instantiate(contaminationPrefab,
            new Vector3(transform.position.x, transform.position.y + 0.05f, transform.position.z),
            Quaternion.identity);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
        Gizmos.color = new Color(1f, 0.3f, 0f);
        Gizmos.DrawWireSphere(transform.position, damageRadius);
    }
}
