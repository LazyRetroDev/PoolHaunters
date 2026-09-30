using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class CursedSwimmingPoolMechanic : MonoBehaviour
{
    private static readonly HashSet<CursedSwimmingPoolMechanic> ActiveMechanics =
        new HashSet<CursedSwimmingPoolMechanic>();
    private static readonly HashSet<int> ProcessedFogRoomIndices = new HashSet<int>();
    private static readonly HashSet<GameObject> ActiveGlobalFogs = new HashSet<GameObject>();
    private static RoomGenerator fogDistributionGenerator;
    private static CursedSwimmingPoolMechanic fogSettingsSource;
    private static bool fullMapFogPassComplete;
    private static bool globalFogCleanupStarted;

    [Header("Pool")]
    [SerializeField] private SwimmingPoolObjective poolObjective;
    [SerializeField] private PoolCleanBoxItemConsumer cleanBox;
    [SerializeField] private string requiredItemName = "AguaBenta";

    [Header("Holy Water Spawn")]
    [SerializeField] private GameObject holyWaterPrefab;
    [SerializeField] private bool spawnHolyWaterAfterMapGeneration = true;
    [SerializeField] private bool avoidCurrentPoolRoom = true;
    [SerializeField] private bool avoidSubmarineAndFinalRooms = true;
    [SerializeField] private bool avoidPoolRooms = true;
    [SerializeField, Min(0)] private int spawnAttemptsPerRoom = 6;
    [SerializeField, Min(0f)] private float roomEdgePadding = 1.25f;
    [SerializeField, Min(0f)] private float floorOffset = 0.08f;
    [SerializeField] private LayerMask groundLayers = ~0;

    [Header("Cursed Fog")]
    [SerializeField] private GameObject cursedFogPrefab;
    [SerializeField, Range(0f, 1f)] private float fogSpawnChance = 0.4f;
    [SerializeField] private Vector3 fogRoomOffset = Vector3.zero;
    [SerializeField] private bool snapFogToFloor = true;
    [SerializeField] private bool onlyInDiscoveredRooms = true;

    private bool blessed;
    private bool holyWaterSpawned;
    private GameObject activeHolyWaterInstance;
    private Vector3 cachedHolyWaterPos;
    private Vector3 cachedHolyWaterUp;
    private Quaternion cachedHolyWaterRot;
    private Coroutine waitForMapRoutine;
    private void Awake()
    {
        AutoBindReferences();
        SetPoolLocked();
    }

    private void Update()
    {
        // Failsafe: if the holy water was spawned but no longer exists (e.g. player disconnected holding it or it fell out of the world)
        // and the pool is still not blessed, we respawn it at the original location.
        if (CanSpawnAuthoritatively() && !blessed && holyWaterSpawned && activeHolyWaterInstance == null)
        {
            Debug.LogWarning($"[CursedPool] Holy Water was lost! Respawning at last known location.");
            holyWaterSpawned = false; // Reset to allow spawning logic
            SpawnHolyWater(cachedHolyWaterPos, cachedHolyWaterUp, cachedHolyWaterRot);
        }
    }

    private void OnEnable()
    {
        ActiveMechanics.Add(this);
        AutoBindReferences();

        if (cleanBox != null)
        {
            cleanBox.CanConsume = CheckCanConsume;
            cleanBox.OnItemConsumed += HandleCleanBoxItemConsumed;
        }

        RoomGenerator.OnGeneratedMapReady += HandleGeneratedMapReady;
        if (LevelObjectiveManager.Instance != null)
            LevelObjectiveManager.Instance.OnRoomDiscovered += HandleRoomDiscovered;

        waitForMapRoutine = StartCoroutine(WaitForExistingGeneratedMap());
        SetPoolLocked();
    }

    private void OnDisable()
    {
        if (cleanBox != null)
        {
            cleanBox.CanConsume = null;
            cleanBox.OnItemConsumed -= HandleCleanBoxItemConsumed;
        }

        RoomGenerator.OnGeneratedMapReady -= HandleGeneratedMapReady;
        if (LevelObjectiveManager.Instance != null)
            LevelObjectiveManager.Instance.OnRoomDiscovered -= HandleRoomDiscovered;

        if (waitForMapRoutine != null)
        {
            StopCoroutine(waitForMapRoutine);
            waitForMapRoutine = null;
        }
        
        ActiveMechanics.Remove(this);
        if (ActiveMechanics.Count == 0)
        {
            DespawnGlobalFogsImmediately();
            ResetGlobalFogState();
        }
    }

    private bool CheckCanConsume(Item item)
    {
        if (IsRequiredItem(item))
        {
            // Only allow consuming the Holy Water if the pool has been filled by the valve
            if (poolObjective != null && poolObjective.GetState() == SwimmingPoolObjectiveState.Empty)
            {
                Debug.LogWarning("[CursedPool] Tried to consume Holy Water, but the pool is not filled yet! Valve must be opened first.");
                return false;
            }
        }
        return true;
    }

    private void HandleCleanBoxItemConsumed(Item item)
    {
        if (!IsRequiredItem(item))
            return;

        blessed = true;
        if (poolObjective != null)
            poolObjective.SetCleaningLocked(false);

        ClearGlobalFogsWhenAllPoolsAreBlessed();
    }

    private void HandleGeneratedMapReady(RoomGenerator generator)
    {
        TrySpawnHolyWater(generator);

        TrySpawnGlobalFogMap(generator);
    }

    private IEnumerator WaitForExistingGeneratedMap()
    {
        yield return null;

        RoomGenerator[] generators = FindObjectsByType<RoomGenerator>(
            FindObjectsInactive.Exclude);

        for (int i = 0; i < generators.Length; i++)
        {
            RoomGenerator generator = generators[i];
            if (generator != null && generator.IsGeneratedMapReady)
            {
                TrySpawnHolyWater(generator);
                break;
            }
        }

        waitForMapRoutine = null;
    }

    private void TrySpawnHolyWater(RoomGenerator generator)
    {
        if (!spawnHolyWaterAfterMapGeneration || holyWaterSpawned)
            return;
        if (generator == null || !CanSpawnAuthoritatively())
            return;

        RoomDefinition ownRoom = GetComponentInParent<RoomDefinition>();
        if (ownRoom != null && !generator.ContainsGeneratedRoom(ownRoom.gameObject))
            return;

        if (holyWaterPrefab == null)
        {
            Debug.LogWarning(
                $"{name} cannot spawn {requiredItemName} because no holyWaterPrefab is assigned.");
            return;
        }

        List<GameObject> eligibleRooms = GetEligibleRooms(generator, ownRoom);
        if (eligibleRooms.Count == 0)
        {
            Debug.LogWarning(
                $"{name} could not find an eligible room to spawn {requiredItemName}.");
            return;
        }

        System.Random random = new System.Random(CreateSpawnSeed(generator));
        while (eligibleRooms.Count > 0)
        {
            int roomListIndex = random.Next(eligibleRooms.Count);
            GameObject room = eligibleRooms[roomListIndex];
            eligibleRooms.RemoveAt(roomListIndex);

            RoomDefinition definition = room != null
                ? room.GetComponent<RoomDefinition>()
                : null;
            if (definition == null)
                continue;

            int attempts = Mathf.Max(1, spawnAttemptsPerRoom);
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                Vector3 position;
                Vector3 surfaceUp;
                Quaternion rotation;
                if (!TryGetSpawnPose(
                    definition,
                    random,
                    out position,
                    out surfaceUp,
                    out rotation))
                {
                    continue;
                }

                SpawnHolyWater(position, surfaceUp, rotation);
                return;
            }
        }

        Debug.LogWarning($"{name} failed to place {requiredItemName} after map generation.");
    }

    private List<GameObject> GetEligibleRooms(
        RoomGenerator generator,
        RoomDefinition ownRoom)
    {
        List<GameObject> rooms = generator.GetSpawnedRoomsSnapshot();
        List<GameObject> eligible = new List<GameObject>();

        for (int i = 0; i < rooms.Count; i++)
        {
            GameObject room = rooms[i];
            if (room == null)
                continue;
            if (avoidCurrentPoolRoom && ownRoom != null && room == ownRoom.gameObject)
                continue;

            RoomDefinition definition = room.GetComponent<RoomDefinition>();
            if (definition == null)
                continue;

            if (avoidSubmarineAndFinalRooms &&
                (definition.category == RoomCategory.SubmarineSpawn ||
                 definition.category == RoomCategory.Final))
            {
                continue;
            }

            if (avoidPoolRooms && definition.category == RoomCategory.Pool)
                continue;

            eligible.Add(room);
        }

        return eligible;
    }

    private bool TryGetSpawnPose(
        RoomDefinition definition,
        System.Random random,
        out Vector3 position,
        out Vector3 surfaceUp,
        out Quaternion rotation)
    {
        position = Vector3.zero;
        surfaceUp = Vector3.up;
        rotation = Quaternion.identity;
        if (definition == null)
            return false;

        surfaceUp = definition.transform.up;
        Vector3 size = definition.size;
        float halfX = Mathf.Max(0f, size.x * 0.5f - roomEdgePadding);
        float halfZ = Mathf.Max(0f, size.z * 0.5f - roomEdgePadding);
        float localX = Mathf.Lerp(-halfX, halfX, (float)random.NextDouble());
        float localZ = Mathf.Lerp(-halfZ, halfZ, (float)random.NextDouble());
        Vector3 localTop = definition.boundsCenter +
            new Vector3(localX, size.y * 0.5f + 1f, localZ);
        Vector3 rayOrigin = definition.transform.TransformPoint(localTop);
        Vector3 down = -definition.transform.up;
        float rayDistance = Mathf.Max(3f, size.y + 3f);

        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            down,
            rayDistance,
            groundLayers,
            QueryTriggerInteraction.Ignore);

        bool foundFloor = false;
        RaycastHit floorHit = new RaycastHit();
        float lowestHeight = float.PositiveInfinity;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null ||
                !hit.collider.transform.IsChildOf(definition.transform))
            {
                continue;
            }

            if (Vector3.Dot(hit.normal, definition.transform.up) < 0.5f)
                continue;

            float height = Vector3.Dot(hit.point, definition.transform.up);
            if (height >= lowestHeight)
                continue;

            lowestHeight = height;
            floorHit = hit;
            foundFloor = true;
        }

        if (!foundFloor)
            return false;

        position = floorHit.point;
        float yaw = (float)random.NextDouble() * 360f;
        rotation = Quaternion.AngleAxis(yaw, definition.transform.up) *
            definition.transform.rotation;
        return true;
    }

    private void SpawnHolyWater(
        Vector3 surfacePoint,
        Vector3 surfaceUp,
        Quaternion rotation)
    {
        cachedHolyWaterPos = surfacePoint;
        cachedHolyWaterUp = surfaceUp;
        cachedHolyWaterRot = rotation;

        GameObject instance = Instantiate(
            holyWaterPrefab,
            surfacePoint + surfaceUp.normalized * floorOffset,
            rotation);
        SnapInstanceBaseToSurface(instance, surfacePoint, surfaceUp);
        holyWaterSpawned = true;
        activeHolyWaterInstance = instance;

        NetworkManager networkManager = NetworkManager.Singleton;
        bool online = networkManager != null && networkManager.IsListening;
        if (!online)
            return;

        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            networkObject.Spawn(true);
            return;
        }

        Debug.LogWarning(
            $"{holyWaterPrefab.name} needs a NetworkObject to spawn from {name} in multiplayer.");
        Destroy(instance);
        holyWaterSpawned = false;
    }

    private void SnapInstanceBaseToSurface(
        GameObject instance,
        Vector3 surfacePoint,
        Vector3 surfaceUp)
    {
        if (instance == null)
            return;

        Vector3 up = surfaceUp.sqrMagnitude > 0.0001f
            ? surfaceUp.normalized
            : Vector3.up;

        Bounds bounds;
        if (!TryGetInstanceBounds(instance, out bounds))
        {
            instance.transform.position =
                surfacePoint + up * Mathf.Max(0f, floorOffset);
            return;
        }

        float bottom = GetMinProjection(bounds, up);
        float target = Vector3.Dot(surfacePoint, up) + Mathf.Max(0f, floorOffset);
        instance.transform.position += up * (target - bottom);
    }

    private bool TryGetInstanceBounds(GameObject instance, out Bounds bounds)
    {
        bounds = new Bounds(
            instance != null ? instance.transform.position : Vector3.zero,
            Vector3.zero);

        if (instance == null)
            return false;

        bool hasBounds = TryGetColliderBounds(instance, out bounds);
        if (hasBounds)
            return true;

        return TryGetRendererBounds(instance, out bounds);
    }

    private bool TryGetColliderBounds(GameObject instance, out Bounds bounds)
    {
        bounds = new Bounds(instance.transform.position, Vector3.zero);
        Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
        bool hasBounds = false;

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider itemCollider = colliders[i];
            if (itemCollider == null ||
                !itemCollider.enabled ||
                itemCollider.isTrigger)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = itemCollider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(itemCollider.bounds);
            }
        }

        return hasBounds;
    }

    private bool TryGetRendererBounds(GameObject instance, out Bounds bounds)
    {
        bounds = new Bounds(instance.transform.position, Vector3.zero);
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer itemRenderer = renderers[i];
            if (itemRenderer == null || !itemRenderer.enabled)
                continue;

            if (!hasBounds)
            {
                bounds = itemRenderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(itemRenderer.bounds);
            }
        }

        return hasBounds;
    }

    private float GetMinProjection(Bounds bounds, Vector3 axis)
    {
        Vector3 center = bounds.center;
        Vector3 extents = bounds.extents;
        float min = float.PositiveInfinity;

        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = center + Vector3.Scale(
                        extents,
                        new Vector3(x, y, z));
                    min = Mathf.Min(min, Vector3.Dot(corner, axis));
                }
            }
        }

        return min;
    }

    private bool IsRequiredItem(Item item)
    {
        if (item == null)
            return false;

        if (string.Equals(
            item.itemName,
            requiredItemName,
            System.StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(
            item.gameObject.name.Replace("(Clone)", string.Empty).Trim(),
            requiredItemName,
            System.StringComparison.OrdinalIgnoreCase);
    }

    private void SetPoolLocked()
    {
        if (poolObjective != null)
            poolObjective.SetCleaningLocked(!blessed);
    }

    private void HandleRoomDiscovered(RoomDefinition room, int index)
    {
        CursedSwimmingPoolMechanic settings = GetFogSettingsSource();
        if (settings == null || !settings.onlyInDiscoveredRooms)
            return;

        RoomGenerator generator = FindAnyObjectByType<RoomGenerator>();
        TrySpawnGlobalFogInRoom(generator, room, index);
    }
    
    private static void TrySpawnGlobalFogMap(RoomGenerator generator)
    {
        if (generator == null || !CanSpawnAuthoritatively())
            return;

        PrepareGlobalFogMap(generator);
        CursedSwimmingPoolMechanic settings = GetFogSettingsSource();
        if (settings == null || settings.onlyInDiscoveredRooms || fullMapFogPassComplete)
            return;

        fullMapFogPassComplete = true;
        List<GameObject> rooms = generator.GetSpawnedRoomsSnapshot();
        for (int i = 0; i < rooms.Count; i++)
        {
            GameObject roomObject = rooms[i];
            if (roomObject == null)
                continue;

            RoomDefinition room = roomObject.GetComponent<RoomDefinition>();
            if (room != null)
                TrySpawnGlobalFogInRoom(generator, room, i);
        }
    }

    private static void TrySpawnGlobalFogInRoom(
        RoomGenerator generator,
        RoomDefinition room,
        int index)
    {
        if (generator == null || room == null || index < 0 || !CanSpawnAuthoritatively())
            return;

        PrepareGlobalFogMap(generator);
        if (!ProcessedFogRoomIndices.Add(index))
            return;

        CursedSwimmingPoolMechanic settings = GetFogSettingsSource();
        if (settings == null || settings.cursedFogPrefab == null || settings.blessed ||
            (!settings.onlyInDiscoveredRooms && !fullMapFogPassComplete))
        {
            return;
        }

        if (room.category == RoomCategory.SubmarineSpawn ||
            (settings.avoidPoolRooms && room.category == RoomCategory.Pool))
        {
            return;
        }

        if (settings.avoidCurrentPoolRoom &&
            room.GetComponentInChildren<CursedSwimmingPoolMechanic>(true) != null)
        {
            return;
        }

        int seed;
        unchecked
        {
            seed = generator.CurrentSeed * 397 ^ index * 7919 ^ 0x5F3759DF;
        }
        System.Random random = new System.Random(seed);
        if (random.NextDouble() > Mathf.Clamp01(settings.fogSpawnChance))
            return;

        Vector3 localSpawnPos = room.boundsCenter + settings.fogRoomOffset;
        Vector3 spawnPosition = room.transform.TransformPoint(localSpawnPos);
        
        if (settings.snapFogToFloor)
        {
            // Try to snap it to floor so it's not floating in the middle
            Vector3 rayStart = room.transform.TransformPoint(localSpawnPos + Vector3.up * (room.size.y * 0.5f));
            RaycastHit[] hits = Physics.RaycastAll(
                rayStart,
                -room.transform.up,
                room.size.y + 2f,
                settings.groundLayers,
                QueryTriggerInteraction.Ignore);
                
            float lowestHeight = float.PositiveInfinity;
            bool foundFloor = false;
            RaycastHit floorHit = new RaycastHit();
            for (int i = 0; i < hits.Length; i++)
            {
                if (hits[i].collider == null || !hits[i].collider.transform.IsChildOf(room.transform)) continue;
                float height = Vector3.Dot(hits[i].point, room.transform.up);
                if (height < lowestHeight)
                {
                    lowestHeight = height;
                    floorHit = hits[i];
                    foundFloor = true;
                }
            }
            
            if (foundFloor)
                spawnPosition = floorHit.point + room.transform.up * settings.floorOffset;
        }

        GameObject fog = Instantiate(
            settings.cursedFogPrefab,
            spawnPosition,
            Quaternion.identity);
        ActiveGlobalFogs.Add(fog);

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            NetworkObject netObj = fog.GetComponent<NetworkObject>();
            if (netObj != null && networkManager.IsServer)
                netObj.Spawn(true);
        }
    }

    private static void PrepareGlobalFogMap(RoomGenerator generator)
    {
        if (fogDistributionGenerator == generator)
            return;

        fogDistributionGenerator = generator;
        ProcessedFogRoomIndices.Clear();
        fullMapFogPassComplete = false;
        globalFogCleanupStarted = false;
        fogSettingsSource = null;
    }

    private static CursedSwimmingPoolMechanic GetFogSettingsSource()
    {
        if (fogSettingsSource != null && !fogSettingsSource.blessed &&
            fogSettingsSource.cursedFogPrefab != null)
        {
            return fogSettingsSource;
        }

        foreach (CursedSwimmingPoolMechanic mechanic in ActiveMechanics)
        {
            if (mechanic != null && !mechanic.blessed && mechanic.cursedFogPrefab != null)
            {
                fogSettingsSource = mechanic;
                return mechanic;
            }
        }

        return null;
    }

    private void ClearGlobalFogsWhenAllPoolsAreBlessed()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening && !networkManager.IsServer)
            return;

        if (globalFogCleanupStarted || !AreAllCursedPoolsBlessed())
            return;

        globalFogCleanupStarted = true;
        float maxLifetime = CursedFogVolume.BeginFadeForAll();

        if (networkManager != null && networkManager.IsListening)
            StartCoroutine(DespawnGlobalFogsAfterFade(maxLifetime));
        else
            DespawnGlobalFogsAfterDelay(maxLifetime);
    }

    private static bool AreAllCursedPoolsBlessed()
    {
        bool foundPool = false;
        foreach (CursedSwimmingPoolMechanic mechanic in ActiveMechanics)
        {
            if (mechanic == null)
                continue;

            foundPool = true;
            if (!mechanic.blessed)
                return false;
        }

        return foundPool;
    }

    private IEnumerator DespawnGlobalFogsAfterFade(float delay)
    {
        yield return new WaitForSeconds(delay);
        DespawnGlobalFogsImmediately();
    }

    private static void DespawnGlobalFogsAfterDelay(float delay)
    {
        foreach (GameObject fog in ActiveGlobalFogs)
        {
            if (fog != null)
                Destroy(fog, delay);
        }
    }

    private static void DespawnGlobalFogsImmediately()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        foreach (GameObject fog in ActiveGlobalFogs)
        {
            if (fog == null)
                continue;

            NetworkObject netObj = fog.GetComponent<NetworkObject>();
            if (netObj != null && netObj.IsSpawned &&
                networkManager != null && networkManager.IsServer)
            {
                netObj.Despawn(true);
            }
            else if (netObj == null || !netObj.IsSpawned)
            {
                Destroy(fog);
            }
        }

        ActiveGlobalFogs.Clear();
    }

    private static void ResetGlobalFogState()
    {
        ProcessedFogRoomIndices.Clear();
        fogDistributionGenerator = null;
        fogSettingsSource = null;
        fullMapFogPassComplete = false;
        globalFogCleanupStarted = false;
    }

    private void AutoBindReferences()
    {
        if (poolObjective == null)
            poolObjective = GetComponent<SwimmingPoolObjective>();
        if (poolObjective == null)
            poolObjective = GetComponentInParent<SwimmingPoolObjective>();

        if (cleanBox == null)
            cleanBox = GetComponentInChildren<PoolCleanBoxItemConsumer>(true);
    }

    private static bool CanSpawnAuthoritatively()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return true;

        return networkManager.IsServer;
    }

    private int CreateSpawnSeed(RoomGenerator generator)
    {
        unchecked
        {
            int result = generator != null ? generator.CurrentSeed : 0;
            result = result * 397 ^ (poolObjective != null ? poolObjective.SyncId : 0);
            result = result * 397 ^ requiredItemName.GetHashCode();
            return result;
        }
    }
}
