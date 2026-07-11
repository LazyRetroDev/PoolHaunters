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

    [Header("Camera Effects")]
    public PlayerVignetteEffect cameraEffects;
    [Range(0f, 1f)] public float snapshotPulseIntensity = 0.65f;
    public float snapshotPulseDuration = 0.25f;
    [Range(0f, 1f)] public float playerPhotoPulseIntensity = 0.85f;
    public float playerPhotoPulseDuration = 0.45f;
    [Range(0f, 1f)] public float chaseVignetteIntensity = 0.35f;
    public float snapshotShakeAmplitude = 0.45f;
    public float snapshotShakeFrequency = 10f;
    public float snapshotShakeDuration = 0.18f;
    public float playerPhotoShakeAmplitude = 0.9f;
    public float playerPhotoShakeFrequency = 14f;
    public float playerPhotoShakeDuration = 0.35f;

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
    private PlayerMovement effectTargetMovement;

    enum State { Wandering, Observing, Chasing, Admiring }
    State currentState = State.Wandering;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        ResolvePlayerReferences();
        ResolveCameraEffects();

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
        if (!EnemyAuthority.CanRunGameplay())
            return;

        ResolvePlayerReferences();
        ResolveCameraEffects();
        bool playerVisible = GetPlayerVisibility();

        switch (currentState)
        {
            case State.Wandering:
                ClearChaseCameraEffect();
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
                SetChaseCameraEffect();
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
                    ClearChaseCameraEffect();
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
                SetChaseCameraEffect();
                if (agent != null)
                {
                    agent.isStopped = false;
                    if (player != null) agent.SetDestination(player.position);
                }

                if (!playerDetected)
                {
                    ClearChaseCameraEffect();
                    if (agent != null) agent.speed = wanderSpeed;
                    SetNewDestination();
                    currentState = State.Wandering;
                    break;
                }

                if (player != null && Vector3.Distance(transform.position, player.position) <= petrifyRange)
                    PhotographPlayer();
                break;

            case State.Admiring:
                ClearChaseCameraEffect();
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

    void ResolvePlayerReferences()
    {
        PlayerStatus closestStatus;
        Transform closestPlayer;
        if (!EnemyTargeting.TryFindClosestPlayer(
            transform.position,
            out closestStatus,
            out closestPlayer))
        {
            player = null;
            playerStatus = null;
            return;
        }

        player = closestPlayer;
        playerStatus = closestStatus;
    }

    void ResolveCameraEffects()
    {
        if (cameraEffects != null) return;
        cameraEffects = FindAnyObjectByType<PlayerVignetteEffect>();
    }

    void SetChaseCameraEffect()
    {
        EnemyPlayerEffects.SetThreatIntensity(
            ref effectTargetMovement,
            playerStatus,
            player,
            cameraEffects,
            chaseVignetteIntensity);
    }

    void ClearChaseCameraEffect()
    {
        EnemyPlayerEffects.ClearThreat(ref effectTargetMovement, cameraEffects);
    }

    void PulseSnapshotCamera(float intensity, float duration, float shakeAmplitude, float shakeFrequency, float shakeDuration)
    {
        EnemyPlayerEffects.Pulse(
            ref effectTargetMovement,
            playerStatus,
            player,
            cameraEffects,
            intensity,
            duration,
            shakeAmplitude,
            shakeFrequency,
            shakeDuration);
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
            return hit.collider.GetComponentInParent<PlayerStatus>() == null;

        return false;
    }

    void TakeSnapshotCone(Vector3 focusPoint)
    {
        photosTaken++;

        Vector3 eyePosition = GetEyePosition();
        Vector3 focusDirection = (focusPoint - eyePosition).normalized;
        if (TryPhotographPlayerInCone(eyePosition, focusDirection))
            return;

        PulseSnapshotCamera(snapshotPulseIntensity, snapshotPulseDuration, snapshotShakeAmplitude, snapshotShakeFrequency, snapshotShakeDuration);

        PhotoItem photoItem = SpawnPhotoItem(null);
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

    bool TryPhotographPlayerInCone(Vector3 origin, Vector3 forward)
    {
        ResolvePlayerReferences();
        if (player == null) return false;

        Vector3 targetPosition = GetPlayerSnapshotTargetPosition();
        Vector3 directionToPlayer = targetPosition - origin;
        float distanceToPlayer = directionToPlayer.magnitude;
        if (distanceToPlayer <= 0.001f || distanceToPlayer > snapshotDistance) return false;

        Quaternion coneRotation = Quaternion.LookRotation(forward);
        Vector3 localDirection = Quaternion.Inverse(coneRotation) * directionToPlayer.normalized;
        if (localDirection.z <= 0f) return false;

        float horizontalAngle = Mathf.Abs(Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg);
        float verticalAngle = Mathf.Abs(Mathf.Atan2(localDirection.y, localDirection.z) * Mathf.Rad2Deg);
        if (horizontalAngle > snapshotConeHorizontalAngle * 0.5f) return false;
        if (verticalAngle > snapshotConeVerticalAngle * 0.5f) return false;

        if (!Physics.Raycast(origin, directionToPlayer.normalized, out RaycastHit hit, distanceToPlayer, snapshotMask, QueryTriggerInteraction.Ignore))
            return false;

        PlayerStatus photographedStatus =
            hit.collider.GetComponentInParent<PlayerStatus>();
        if (!EnemyTargeting.IsValidTarget(photographedStatus))
            return false;

        playerStatus = photographedStatus;
        player = photographedStatus.transform;
        PhotographPlayer();
        return true;
    }

    Vector3 GetPlayerSnapshotTargetPosition()
    {
        if (player == null)
            return transform.position;

        Collider playerCollider = player.GetComponent<Collider>();
        if (playerCollider != null)
            return playerCollider.bounds.center;

        return player.position + Vector3.up * 0.75f;
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
        ResolvePlayerReferences();
        if (player == null) return;

        PulseSnapshotCamera(playerPhotoPulseIntensity, playerPhotoPulseDuration, playerPhotoShakeAmplitude, playerPhotoShakeFrequency, playerPhotoShakeDuration);

        if (playerStatus != null)
            playerStatus.ContaminateWater();

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
            return hit.collider.GetComponentInParent<PlayerStatus>() == playerStatus;

        return false;
    }

    void OnDestroy()
    {
        ClearChaseCameraEffect();
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
