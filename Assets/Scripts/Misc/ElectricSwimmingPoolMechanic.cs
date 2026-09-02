using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class ElectricSwimmingPoolMechanic : MonoBehaviour
{
    [Header("Pool")]
    [SerializeField] private SwimmingPoolObjective poolObjective;

    [Header("Power Cycle")]
    [SerializeField] private bool startsPowered = true;
    [SerializeField, Min(1f)] private float disabledDuration = 60f;
    [SerializeField] private bool lockCleaningWhilePowered = true;

    [Header("Hazards")]
    [SerializeField] private ElectricPoolPowerDevice powerDevicePrefab;
    [SerializeField] private ElectricPoolCable cablePrefab;
    [SerializeField, Min(0)] private int floorCableCount = 6;
    [SerializeField, Min(0)] private int ceilingCableCount = 3;
    [SerializeField, Min(0.05f)] private float spawnFloorOffset = 0.04f;
    [SerializeField] private LayerMask groundLayers = ~0;

    [Header("Noise")]
    [SerializeField] private bool emitNoiseWhilePowered = true;
    [SerializeField, Min(0.1f)] private float noiseInterval = 2f;
    [SerializeField, Min(0f)] private float noiseRadius = 18f;

    private readonly List<ElectricPoolCable> cables = new List<ElectricPoolCable>();
    private ElectricPoolPowerDevice powerDevice;
    private bool powered;
    private float repowerTimer;
    private float nextNoiseTime;
    private bool spawnedHazards;
    private Coroutine waitForMapRoutine;

    public bool IsPowered => powered;
    public int ActiveCableCount
    {
        get
        {
            PruneCables();
            return cables.Count;
        }
    }
    public float PowerReturnSeconds => powered ? 0f : Mathf.Max(0f, repowerTimer);

    private void Awake()
    {
        AutoBindReferences();
        powered = startsPowered;
        RefreshPoolLock();
    }

    private void OnEnable()
    {
        AutoBindReferences();

        RoomGenerator.OnGeneratedMapReady += HandleGeneratedMapReady;
        waitForMapRoutine = StartCoroutine(WaitForExistingGeneratedMap());
        RefreshPoolLock();
    }

    private void OnDisable()
    {
        RoomGenerator.OnGeneratedMapReady -= HandleGeneratedMapReady;

        if (waitForMapRoutine != null)
        {
            StopCoroutine(waitForMapRoutine);
            waitForMapRoutine = null;
        }
    }

    private void Update()
    {
        if (!CanSpawnAuthoritatively())
            return;

        if (!powered)
        {
            repowerTimer -= Time.deltaTime;
            if (repowerTimer <= 0f)
                SetPowered(true);
            return;
        }

        if (emitNoiseWhilePowered && Time.time >= nextNoiseTime)
        {
            NoiseEvent.Emit(transform.position, noiseRadius, gameObject);
            nextNoiseTime = Time.time + noiseInterval;
        }
    }

    public void DisablePowerTemporarily()
    {
        if (!CanSpawnAuthoritatively())
            return;

        repowerTimer = Mathf.Max(1f, disabledDuration);
        SetPowered(false);
    }

    public void RegisterCable(ElectricPoolCable cable)
    {
        if (cable == null || cables.Contains(cable))
            return;

        cables.Add(cable);
        cable.BindPool(this);
        cable.SetPowered(powered);
    }

    public void NotifyCableDisabled(ElectricPoolCable cable)
    {
        if (cable != null)
            cables.Remove(cable);
    }

    private IEnumerator WaitForExistingGeneratedMap()
    {
        yield return null;

        RoomGenerator[] generators = FindObjectsByType<RoomGenerator>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);

        for (int i = 0; i < generators.Length; i++)
        {
            if (generators[i] != null && generators[i].IsGeneratedMapReady)
            {
                HandleGeneratedMapReady(generators[i]);
                break;
            }
        }

        waitForMapRoutine = null;
    }

    private void HandleGeneratedMapReady(RoomGenerator generator)
    {
        if (spawnedHazards || generator == null || !CanSpawnAuthoritatively())
            return;

        RoomDefinition ownRoom = GetComponentInParent<RoomDefinition>();
        if (ownRoom != null && !generator.ContainsGeneratedRoom(ownRoom.gameObject))
            return;

        spawnedHazards = true;
        SpawnPowerDevice(ownRoom);
        SpawnCables(generator, ownRoom);
        SetPowered(startsPowered);
    }

    private void SpawnPowerDevice(RoomDefinition ownRoom)
    {
        if (powerDevicePrefab == null)
            return;

        Vector3 up = ownRoom != null ? ownRoom.transform.up : Vector3.up;
        Vector3 point = transform.position + up * spawnFloorOffset;
        powerDevice = Instantiate(powerDevicePrefab, point, Quaternion.identity);
        powerDevice.BindPool(this);
        SpawnNetworkObject(powerDevice.gameObject);
    }

    private void SpawnCables(RoomGenerator generator, RoomDefinition ownRoom)
    {
        if (cablePrefab == null)
            return;

        List<GameObject> rooms = generator.GetSpawnedRoomsSnapshot();
        if (rooms.Count == 0)
            return;

        System.Random random = new System.Random(CreateSpawnSeed(97));
        int targetCount = floorCableCount + ceilingCableCount;
        int spawned = 0;
        int guard = targetCount * 16;

        while (spawned < targetCount && guard-- > 0)
        {
            GameObject room = rooms[random.Next(rooms.Count)];
            if (room == null)
                continue;

            RoomDefinition definition = room.GetComponent<RoomDefinition>();
            Vector3 point;
            Vector3 up;
            bool ceiling = spawned >= floorCableCount;
            if (!TryGetCablePose(definition, random, ceiling, out point, out up))
                continue;

            Quaternion rotation = Quaternion.AngleAxis(
                (float)random.NextDouble() * 360f,
                up);
            ElectricPoolCable cable = Instantiate(
                cablePrefab,
                point + up.normalized * spawnFloorOffset,
                rotation);

            RegisterCable(cable);
            SpawnNetworkObject(cable.gameObject);
            spawned++;
        }
    }

    private bool TryGetCablePose(
        RoomDefinition definition,
        System.Random random,
        bool ceiling,
        out Vector3 point,
        out Vector3 surfaceUp)
    {
        point = Vector3.zero;
        surfaceUp = Vector3.up;
        if (definition == null)
            return false;

        surfaceUp = ceiling ? -definition.transform.up : definition.transform.up;
        Vector3 rayDirection = ceiling ? definition.transform.up : -definition.transform.up;
        Vector3 size = definition.size;
        float localX = Mathf.Lerp(-size.x * 0.35f, size.x * 0.35f, (float)random.NextDouble());
        float localZ = Mathf.Lerp(-size.z * 0.35f, size.z * 0.35f, (float)random.NextDouble());
        float y = ceiling ? -size.y * 0.5f - 1f : size.y * 0.5f + 1f;
        Vector3 origin = definition.transform.TransformPoint(
            definition.boundsCenter + new Vector3(localX, y, localZ));

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            rayDirection,
            Mathf.Max(4f, size.y + 4f),
            groundLayers,
            QueryTriggerInteraction.Ignore);

        float bestDistance = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider == null)
                continue;
            if (Vector3.Dot(hits[i].normal, surfaceUp) < 0.35f)
                continue;
            if (hits[i].distance >= bestDistance)
                continue;

            bestDistance = hits[i].distance;
            point = hits[i].point;
        }

        return bestDistance < float.PositiveInfinity;
    }

    private void SetPowered(bool value)
    {
        powered = value;
        nextNoiseTime = Time.time;

        if (powerDevice != null)
            powerDevice.SetPowered(powered);

        for (int i = cables.Count - 1; i >= 0; i--)
        {
            if (cables[i] == null)
            {
                cables.RemoveAt(i);
                continue;
            }

            cables[i].SetPowered(powered);
        }

        RefreshPoolLock();
    }

    private void RefreshPoolLock()
    {
        if (poolObjective == null)
            return;

        bool unsafePower = lockCleaningWhilePowered && powered;
        poolObjective.SetCleaningLocked(unsafePower);
    }

    private void SpawnNetworkObject(GameObject target)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
            return;

        NetworkObject networkObject = target != null
            ? target.GetComponent<NetworkObject>()
            : null;
        if (networkObject != null && !networkObject.IsSpawned)
            networkObject.Spawn(true);
    }

    private void AutoBindReferences()
    {
        if (poolObjective == null)
            poolObjective = GetComponent<SwimmingPoolObjective>();
        if (poolObjective == null)
            poolObjective = GetComponentInParent<SwimmingPoolObjective>();
    }

    private bool CanSpawnAuthoritatively()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager == null || !networkManager.IsListening || networkManager.IsServer;
    }

    private int CreateSpawnSeed(int salt)
    {
        unchecked
        {
            int hash = poolObjective != null ? poolObjective.SyncId : transform.position.GetHashCode();
            hash = hash * 397 ^ salt;
            return hash;
        }
    }

    private void PruneCables()
    {
        for (int i = cables.Count - 1; i >= 0; i--)
        {
            if (cables[i] == null)
                cables.RemoveAt(i);
        }
    }
}
