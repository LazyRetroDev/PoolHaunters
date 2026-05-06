using UnityEngine;
using UnityEngine.AI;

public class Photographer : MonoBehaviour
{
    [Header("Wandering")]
    public float wanderRadius = 15f;
    public float minWaitTime = 2f;
    public float maxWaitTime = 5f;

    [Header("Vision")]
    public float sightRange = 10f;
    public float fieldOfView = 90f;
    public Transform player;

    [Header("Chase")]
    public float sprintDetectDelay = 1.5f;
    public float chaseSpeed = 6f;
    public float wanderSpeed = 3f;
    public float petrifyRange = 1.5f;

    [Header("Snapshot")]
    public float snapshotDistance = 10f;
    public float snapshotCooldown = 8f;
    public GameObject decalProjectorPrefab;
    public float admireTime = 3f;
    public GameObject photoItemPrefab;

    private NavMeshAgent agent;
    private PlayerStatus playerStatus;
    private float waitTimer;
    private float sprintTimer;
    private float snapshotTimer;
    private float admireTimer;

    private bool playerDetected = false;
    private float loseSightTimer = 0f;
    public float loseSightDelay = 0.8f;

    enum State { Wandering, Observing, Chasing, Admiring }
    State currentState = State.Wandering;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        playerStatus = player.GetComponent<PlayerStatus>();
        agent.speed = wanderSpeed;
        waitTimer = 0f;
        SetNewDestination();
    }

    void Update()
    {
        bool playerVisible = GetPlayerVisibility();

        switch (currentState)
        {
            case State.Wandering:
                HandleWander();
                TrySnapshot();
                if (playerVisible)
                    currentState = State.Observing;
                break;

            case State.Observing:
                FaceTarget(player.position);
                TrySnapshot();
                if (!playerVisible)
                {
                    currentState = State.Wandering;
                    agent.speed = wanderSpeed;
                    SetNewDestination();
                    sprintTimer = 0f;
                }
                if (playerStatus.IsSprinting())
                {
                    sprintTimer += Time.deltaTime;
                    if (sprintTimer >= sprintDetectDelay)
                    {
                        currentState = State.Chasing;
                        agent.speed = chaseSpeed;
                        sprintTimer = 0f;
                    }
                }
                else
                {
                    sprintTimer = 0f;
                }
                break;

            case State.Chasing:
                agent.SetDestination(player.position);
                if (!playerVisible)
                {
                    currentState = State.Wandering;
                    agent.speed = wanderSpeed;
                    SetNewDestination();
                }
                if (Vector3.Distance(transform.position, player.position) <= petrifyRange)
                    PetrifyPlayer();
                break;

            case State.Admiring:
                admireTimer -= Time.deltaTime;
                if (admireTimer <= 0f)
                {
                    DropPhoto();
                    currentState = State.Wandering;
                    SetNewDestination();
                }
                break;
        }
    }

    void HandleWander()
    {
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
        {
            waitTimer -= Time.deltaTime;
            if (waitTimer <= 0f)
            {
                SetNewDestination();
                waitTimer = Random.Range(minWaitTime, maxWaitTime);
            }
        }
    }

    void TrySnapshot()
    {
        snapshotTimer -= Time.deltaTime;
        if (snapshotTimer > 0f) return;

        Ray ray = new Ray(transform.position + Vector3.up * 0.5f, transform.forward);
        if (Physics.Raycast(ray, out RaycastHit hit, snapshotDistance))
        {
            if (hit.collider.CompareTag("Player")) return;
            TakeSnapshot(hit.point, hit.normal);
            snapshotTimer = snapshotCooldown;
        }
    }

    void TakeSnapshot(Vector3 position, Vector3 normal)
    {
        if (decalProjectorPrefab == null) return;
        Quaternion rotation = Quaternion.LookRotation(-normal);
        Instantiate(decalProjectorPrefab, position, rotation);
        agent.ResetPath();
        admireTimer = admireTime;
        currentState = State.Admiring;
    }

    void PetrifyPlayer()
    {
        PlayerPetrify petrify = player.GetComponent<PlayerPetrify>();
        if (petrify != null) petrify.Petrify();
        currentState = State.Wandering;
        agent.speed = wanderSpeed;
        SetNewDestination();
    }

    void DropPhoto()
    {
        if (photoItemPrefab != null)
            Instantiate(photoItemPrefab, transform.position + transform.forward, Quaternion.identity);
    }

    void FaceTarget(Vector3 target)
    {
        Vector3 direction = (target - transform.position).normalized;
        direction.y = 0f;
        if (direction != Vector3.zero)
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(direction), Time.deltaTime * 5f);
    }

    void SetNewDestination()
    {
        Vector3 randomDirection = Random.insideUnitSphere * wanderRadius;
        randomDirection += transform.position;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDirection, out hit, wanderRadius, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    bool GetPlayerVisibility()
    {
        if (CanSeePlayer())
        {
            loseSightTimer = 0f;
            playerDetected = true;
        }
        else
        {
            loseSightTimer += Time.deltaTime;
            if (loseSightTimer >= loseSightDelay)
                playerDetected = false;
        }
        return playerDetected;
    }

    bool CanSeePlayer()
    {
        if (player == null) return false;

        Vector3 directionToPlayer = player.position - transform.position;
        float distanceToPlayer = directionToPlayer.magnitude;

        if (distanceToPlayer > sightRange) return false;

        float angle = Vector3.Angle(transform.forward, directionToPlayer);
        if (angle > fieldOfView / 2f)
        {
            Debug.Log("Player out of FOV angle: " + angle);
            return false;
        }

        // Cast multiple rays across the FOV
        int rayCount = 7;
        float halfFOV = fieldOfView / 2f;

        for (int i = 0; i < rayCount; i++)
        {
            float t = (float)i / (rayCount - 1); // 0 to 1
            float rayAngle = Mathf.Lerp(-halfFOV, halfFOV, t);
            Vector3 rayDirection = Quaternion.Euler(0, rayAngle, 0) * transform.forward;

            Debug.DrawRay(transform.position + Vector3.up * 0.5f, rayDirection * sightRange, Color.green);

            if (Physics.Raycast(transform.position + Vector3.up * 0.5f, rayDirection, out RaycastHit rayHit, sightRange))
            {
                Debug.Log("Ray " + i + " hit: " + rayHit.collider.gameObject.name);
                if (rayHit.collider.CompareTag("Player"))
                {
                    Debug.Log("Player detected by ray " + i);
                    return true;
                }
            }
        }

        Debug.Log("No rays hit player. Distance: " + distanceToPlayer + " Angle: " + angle);
        return false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, sightRange);
        float halfFOV = fieldOfView / 2f;
        Vector3 leftDirection = Quaternion.Euler(0, -halfFOV, 0) * transform.forward;
        Vector3 rightDirection = Quaternion.Euler(0, halfFOV, 0) * transform.forward;
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(transform.position, leftDirection * sightRange);
        Gizmos.DrawRay(transform.position, rightDirection * sightRange);
        if (Application.isPlaying && CanSeePlayer())
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, player.position);
        }
    }
}