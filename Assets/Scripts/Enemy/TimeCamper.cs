using UnityEngine;
using Unity.AI.Navigation;
using UnityEngine.AI;
using TMPro;
using System.Collections;

public class TimeCamper : MonoBehaviour
{
    [Header("Detection")]
    public Transform player;
    public float detectionRadius = 2.5f;

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

    private float countdownTimer;
    private float damageTimer;
    private float teleportTimer;
    private bool playerInRadius = false;

    private GameObject redCircleInstance;
    private GameObject beamInstance;
    private PlayerStatus playerStatus;

    enum State { WaitingForPlayer, Countdown, Beam, Leaving }
    State currentState = State.WaitingForPlayer;

    void Start()
    {
<<<<<<< Updated upstream
        playerStatus = player != null ? player.GetComponent<PlayerStatus>() : null;
        teleportTimer = teleportInterval;

        // Randomize countdown for unpredictability
        countdownDuration = Random.Range(minCountdown, maxCountdown);
        countdownTimer = countdownDuration;

        // Red circle appears immediately on spawn
        if (redCirclePrefab != null)
            redCircleInstance = Instantiate(redCirclePrefab,
                new Vector3(transform.position.x, transform.position.y + 0.05f, transform.position.z),
                Quaternion.identity);

        if (countdownText != null)
            countdownText.gameObject.SetActive(false);
=======
        agent = GetComponent<NavMeshAgent>();
        ResolvePlayerReferences();
        ResetCycle();
>>>>>>> Stashed changes
    }

    void Update()
    {
        switch (currentState)
        {
            case State.WaitingForPlayer:
                teleportTimer -= Time.deltaTime;
<<<<<<< Updated upstream
                if (teleportTimer <= 0f)
                    StartCoroutine(LeaveAndTeleport());
=======
                if (PlayerIsInImpactArea())
                    StartCountdown();
                else if (teleportTimer <= 0f)
                    StartLeaving();
>>>>>>> Stashed changes
                break;

            case State.Countdown:
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
                break;

            case State.Beam:
                damageTimer -= Time.deltaTime;
                DamagePlayerInRadius();
                if (damageTimer <= 0f)
                {
                    currentState = State.Leaving;
                    StartCoroutine(LeaveAndTeleport());
                }
                break;

            case State.Leaving:
                break;
        }
    }

<<<<<<< Updated upstream
    void CheckForPlayer()
    {
        if (player == null) return;
        float dist = Vector3.Distance(transform.position, player.position);
        if (dist <= detectionRadius)
        {
            playerInRadius = true;
            currentState = State.Countdown;
=======
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

    bool PlayerIsInImpactArea()
    {
        if (player == null) return false;
        return Vector3.Distance(transform.position, player.position) <= detectionRadius;
    }

    void StartCountdown()
    {
        currentState = State.Countdown;
        countdownTimer = Random.Range(minCountdown, maxCountdown);
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

            StartBeam();
>>>>>>> Stashed changes
        }
    }

    void StartBeam()
    {
        currentState = State.Beam;
        damageTimer = damageDuration;

        // Spawn beam visually
        if (beamPrefab != null)
            beamInstance = Instantiate(beamPrefab, transform.position, Quaternion.identity);
    }

    void DamagePlayerInRadius()
    {
        if (playerStatus == null || player == null) return;
        float dist = Vector3.Distance(transform.position, player.position);
<<<<<<< Updated upstream
        if (dist <= damageRadius)
        {
            playerStatus.TakeDamage(damagePerSecond * Time.deltaTime);

            if (playerStatus.GetCurrentHealth() <= 0f)
            {
                SpawnClone();
                StartCoroutine(LeaveAndTeleport());
            }
        }
    }

    void SpawnClone()
    {
        if (TimeCamperManager.Instance != null && TimeCamperManager.Instance.CanSpawn())
            EnemySpawner.Instance.SpawnTimeCamper(isClone: true);
=======
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
        StartCoroutine(LeaveAndTeleport());
>>>>>>> Stashed changes
    }

    IEnumerator LeaveAndTeleport()
    {
        currentState = State.Leaving;

        // Clean up visuals
        if (redCircleInstance != null) Destroy(redCircleInstance);
        if (beamInstance != null) Destroy(beamInstance);
        if (countdownText != null) countdownText.gameObject.SetActive(false);

        // Leave contamination mark
        if (contaminationPrefab != null)
            Instantiate(contaminationPrefab,
                new Vector3(transform.position.x, transform.position.y + 0.05f, transform.position.z),
                Quaternion.identity);

        TimeCamperManager.Instance.Unregister(this);

        yield return new WaitForSeconds(0.1f);

        // Teleport to new location instead of destroying
        Vector3 newPos;
        if (EnemySpawner.Instance != null && EnemySpawner.Instance.TryGetValidSpawnPosition(out newPos))
        {
<<<<<<< Updated upstream
            transform.position = newPos;
            ResetState();
            TimeCamperManager.Instance.Register(this);
=======
            TeleportTo(newPos);
            ResetCycle();

            if (TimeCamperManager.Instance != null)
                TimeCamperManager.Instance.Register(this);
>>>>>>> Stashed changes
        }
        else
        {
            Destroy(gameObject);
        }
    }

<<<<<<< Updated upstream
    void ResetState()
    {
        playerInRadius = false;
        currentState = State.Idle;
=======
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
>>>>>>> Stashed changes
        teleportTimer = teleportInterval;
        damageTimer = 0f;
<<<<<<< Updated upstream
=======

        if (countdownText != null)
            countdownText.gameObject.SetActive(false);

        SpawnWarningCircle();
    }
>>>>>>> Stashed changes

        // Respawn red circle at new location
        if (redCirclePrefab != null)
            redCircleInstance = Instantiate(redCirclePrefab,
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