using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

public class Photographer : MonoBehaviour
{
    [Header("Wandering")]
    public float wanderRadius = 15f;
    public float minWaitTime = 2f;
    public float maxWaitTime = 5f;
    public float wanderSpeed = 2f;

    [Header("Vision")]
    public float sightRange = 10f;
    public float fieldOfView = 90f;
    public Transform player;

    [Header("Chase")]
    public float sprintDetectDelay = 1.5f;
    public float chaseSpeed = 6f;
    public float petrifyRange = 1.5f;

    [Header("Snapshot")]
    public float snapshotDistance = 10f;
    public float snapshotCooldown = 8f;
    public GameObject decalProjectorPrefab;
    public float admireTime = 3f;
    public GameObject photoItemPrefab;
    public int maxPhotos = 5;
    public LayerMask snapshotMask = ~0;
    public int randomSnapshotAttempts = 10;
    public float snapshotEyeHeight = 0.5f;

    [Header("Snapshot Cone")]
    public float snapshotConeHorizontalAngle = 55f;
    public float snapshotConeVerticalAngle = 28f;
    public int snapshotConeHorizontalRays = 5;
    public int snapshotConeVerticalRays = 3;

    [Header("Photo Contamination")]
    public float decalContaminationDelay = 20f;
    public GameObject dirtPrefab;

    [Header("Dropped Photo")]
    public Transform capturedPhotoPoint;
    public Vector3 capturedPhotoOffset = new Vector3(0.8f, 1f, 0.4f);
    public bool parentCapturedPhotoToPhotographer = false;

    [Header("Petrify")]
    public float petrifyWanderDuration = 5f;

    private NavMeshAgent agent;
    private PlayerStatus playerStatus;
    private float waitTimer;
    private float sprintTimer;
    private float snapshotTimer;
    private float admireTimer;
    private int photosTaken = 0;
    private float petrifyWanderCooldown = 0f;

    private bool playerDetected = false;
    private float loseSightTimer = 0f;
    public float loseSightDelay = 1f;

    private GameObject activeCapturedPhoto;

    enum State { Wandering, Observing, Chasing, Admiring }
    State currentState = State.Wandering;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();

        if (player != null)
            playerStatus = player.GetComponent<PlayerStatus>();

        if (agent != null)
        {
            agent.speed = wanderSpeed;
            agent.angularSpeed = 120f;
        }

        waitTimer = 0f;
        SetNewDestination();
    }

    void Update()
    {
        bool playerVisible = GetPlayerVisibility();

        switch (currentState)
        {
            case State.Wandering:
                if (agent != null) agent.isStopped = false;
                HandleWander();
                TrySnapshot();
                if (playerVisible)
                {
                    if (agent != null)
                    {
                        agent.isStopped = true;
                        agent.ResetPath();
                    }
                    currentState = State.Observing;
                    sprintTimer = 0f;
                }
                break;

            case State.Observing:
                if (agent != null)
                {
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                }

                if (player != null)
                    FaceTarget(player.position);

                TrySnapshot();

                if (!playerVisible)
                {
                    if (agent != null)
                    {
                        agent.isStopped = false;
                        agent.speed = wanderSpeed;
                    }
                    SetNewDestination();
                    sprintTimer = 0f;
                    currentState = State.Wandering;
                    break;
                }

                if (playerStatus != null && playerStatus.IsSprinting() && playerStatus.IsMoving())
                {
                    sprintTimer += Time.deltaTime;
                    Debug.Log("Sprint timer: " + sprintTimer);
                    if (sprintTimer >= sprintDetectDelay)
                    {
                        if (agent != null)
                        {
                            agent.isStopped = false;
                            agent.speed = chaseSpeed;
                        }
                        sprintTimer = 0f;
                        currentState = State.Chasing;
                    }
                }
                else
                {
                    sprintTimer = 0f;
                }
                break;

            case State.Chasing:
                if (agent != null)
                {
                    agent.isStopped = false;
                    if (player != null) agent.SetDestination(player.position);
                }

                if (!playerDetected)
                {
                    if (agent != null) agent.speed = wanderSpeed;
                    SetNewDestination();
                    currentState = State.Wandering;
                    break;
                }

                if (player != null && Vector3.Distance(transform.position, player.position) <= petrifyRange)
                    PhotographPlayer();
                break;

            case State.Admiring:
                if (agent != null)
                {
                    agent.isStopped = true;
                    agent.velocity = Vector3.zero;
                }

                if (activeCapturedPhoto != null && activeCapturedPhoto.transform.parent != transform)
                    FaceTarget(activeCapturedPhoto.transform.position);

                admireTimer -= Time.deltaTime;
                bool photoWasTaken = activeCapturedPhoto == null;
                if (photoWasTaken || admireTimer <= 0f)
                    FinishAdmiring();
                break;
        }
    }

    void HandleWander()
    {
        if (agent == null) return;

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
        if (photosTaken >= maxPhotos) return;
        if (decalProjectorPrefab == null) return;

        if (TryFindSnapshotSurface(out RaycastHit hit))
        {
            TakeSnapshotCone(hit.point);
            snapshotTimer = snapshotCooldown;
        }
    }

    bool TryFindSnapshotSurface(out RaycastHit snapshotHit)
    {
        Vector3 eyePosition = GetEyePosition();

        if (TrySnapshotRay(eyePosition, transform.forward, out snapshotHit))
            return true;

        for (int i = 0; i < randomSnapshotAttempts; i++)
        {
            Vector3 direction = Random.onUnitSphere;
            direction.y = Random.Range(-0.65f, 0.25f);
            direction.Normalize();

            if (Vector3.Dot(transform.forward, direction) < -0.2f)
                direction = Vector3.Reflect(direction, -transform.forward).normalized;

            if (TrySnapshotRay(eyePosition, direction, out snapshotHit))
                return true;
        }

        return false;
    }

    bool TrySnapshotRay(Vector3 origin, Vector3 direction, out RaycastHit hit)
    {
        if (Physics.Raycast(origin, direction, out hit, snapshotDistance, snapshotMask, QueryTriggerInteraction.Ignore))
            return !hit.collider.CompareTag("Player");

        return false;
    }

    void TakeSnapshotCone(Vector3 focusPoint)
    {
        photosTaken++;

        PhotoItem photoItem = SpawnPhotoItem(null);
        Vector3 eyePosition = GetEyePosition();
        Vector3 focusDirection = (focusPoint - eyePosition).normalized;
        List<PhotographerDecal> decals = CreateConeDecals(eyePosition, focusDirection);

        if (photoItem != null)
        {
            for (int i = 0; i < decals.Count; i++)
                photoItem.AddLinkedDecal(decals[i]);
        }

        if (agent != null) agent.ResetPath();
        admireTimer = admireTime;
        currentState = State.Admiring;
    }

    List<PhotographerDecal> CreateConeDecals(Vector3 origin, Vector3 forward)
    {
        List<PhotographerDecal> decals = new List<PhotographerDecal>();
        HashSet<Collider> hitColliders = new HashSet<Collider>();
        Quaternion coneRotation = Quaternion.LookRotation(forward);

        int horizontalCount = Mathf.Max(1, snapshotConeHorizontalRays);
        int verticalCount = Mathf.Max(1, snapshotConeVerticalRays);

        for (int y = 0; y < verticalCount; y++)
        {
            float verticalT = verticalCount == 1 ? 0.5f : (float)y / (verticalCount - 1);
            float pitch = Mathf.Lerp(-snapshotConeVerticalAngle * 0.5f, snapshotConeVerticalAngle * 0.5f, verticalT);

            for (int x = 0; x < horizontalCount; x++)
            {
                float horizontalT = horizontalCount == 1 ? 0.5f : (float)x / (horizontalCount - 1);
                float yaw = Mathf.Lerp(-snapshotConeHorizontalAngle * 0.5f, snapshotConeHorizontalAngle * 0.5f, horizontalT);
                Vector3 direction = coneRotation * Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;

                if (!TrySnapshotRay(origin, direction, out RaycastHit hit)) continue;
                if (hitColliders.Contains(hit.collider)) continue;

                hitColliders.Add(hit.collider);
                PhotographerDecal decal = CreateDecal(hit.point, hit.normal);
                if (decal != null)
                    decals.Add(decal);
            }
        }

        return decals;
    }

    PhotographerDecal CreateDecal(Vector3 position, Vector3 normal)
    {
        Quaternion rotation = Quaternion.LookRotation(-normal);
        GameObject decalObject = Instantiate(decalProjectorPrefab, position, rotation);
        PhotographerDecal decal = decalObject.GetComponent<PhotographerDecal>();
        if (decal == null)
            decal = decalObject.AddComponent<PhotographerDecal>();

        decal.contaminationDelay = decalContaminationDelay;
        decal.dirtPrefab = dirtPrefab;
        return decal;
    }

    void PhotographPlayer()
    {
        if (player == null) return;

        PlayerPetrify petrify = player.GetComponent<PlayerPetrify>();
        if (petrify != null)
            petrify.Petrify();

        SpawnPhotoItem(petrify);

        petrifyWanderCooldown = petrifyWanderDuration;
        playerDetected = false;
        loseSightTimer = loseSightDelay + 1f;
        admireTimer = admireTime;
        currentState = State.Admiring;

        if (agent != null)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }
    }

    PhotoItem SpawnPhotoItem(PlayerPetrify petrify)
    {
        if (photoItemPrefab == null) return null;

        Vector3 spawnPosition = GetPhotoItemPosition();
        activeCapturedPhoto = Instantiate(photoItemPrefab, spawnPosition, transform.rotation);
        activeCapturedPhoto.SetActive(true);

        if (parentCapturedPhotoToPhotographer)
            activeCapturedPhoto.transform.SetParent(transform, true);

        PhotoItem photoItem = activeCapturedPhoto.GetComponent<PhotoItem>();
        if (photoItem == null)
            photoItem = activeCapturedPhoto.AddComponent<PhotoItem>();

        photoItem.SetCapturedPlayer(petrify);
        return photoItem;
    }

    Vector3 GetPhotoItemPosition()
    {
        if (capturedPhotoPoint != null)
            return capturedPhotoPoint.position;

        return transform.position + transform.TransformDirection(capturedPhotoOffset);
    }

    Vector3 GetEyePosition()
    {
        return transform.position + Vector3.up * snapshotEyeHeight;
    }

    void FinishAdmiring()
    {
        if (activeCapturedPhoto != null)
            activeCapturedPhoto.transform.SetParent(null, true);

        activeCapturedPhoto = null;

        if (agent != null)
        {
            agent.isStopped = false;
            agent.speed = wanderSpeed;
        }

        currentState = State.Wandering;
        SetNewDestination();
    }

    void FaceTarget(Vector3 target)
    {
        Vector3 direction = (target - transform.position).normalized;
        direction.y = 0f;
        if (direction != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 8f);
        }
    }

    void SetNewDestination()
    {
        if (agent == null) return;

        Vector3 randomDirection = Random.insideUnitSphere * wanderRadius;
        randomDirection += transform.position;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(randomDirection, out hit, wanderRadius, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
    }

    bool GetPlayerVisibility()
    {
        if (petrifyWanderCooldown > 0f)
        {
            petrifyWanderCooldown -= Time.deltaTime;
            playerDetected = false;
            return false;
        }

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

        Vector3 directionToPlayer = player.position - GetEyePosition();
        float distanceToPlayer = directionToPlayer.magnitude;

        if (distanceToPlayer > sightRange) return false;

        float angle = Vector3.Angle(transform.forward, directionToPlayer);
        if (angle > fieldOfView / 2f) return false;

        if (Physics.Raycast(GetEyePosition(), directionToPlayer.normalized, out RaycastHit hit, sightRange, snapshotMask, QueryTriggerInteraction.Ignore))
            return hit.collider.CompareTag("Player");

        return false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, sightRange);
        Vector3 eye = Application.isPlaying ? GetEyePosition() : transform.position + Vector3.up * snapshotEyeHeight;
        Gizmos.color = Color.blue;
        Gizmos.DrawRay(eye, transform.forward * sightRange);
        if (Application.isPlaying && CanSeePlayer())
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(eye, player.position);
        }
    }
}
