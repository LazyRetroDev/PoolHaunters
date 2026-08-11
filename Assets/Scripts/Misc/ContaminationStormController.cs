using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

public class ContaminationStormController : MonoBehaviour
{
    [Header("Timing")]
    public bool startStormOnStart = true;
    public float initialDelay = 480f;
    public float spreadInterval = 120f;
    public bool contaminateFirstRoomImmediately = false;
    public bool stopAfterFinalRoom = true;

    [Header("Procedural Map")]
    public RoomGenerator roomGenerator;
    public bool useRoomGeneratorOrder = true;
    public bool includeInactiveRooms = false;

    [Header("Contamination Effects")]
    public GameObject contaminationZonePrefab;
    public DirtSpot dirtSpotPrefab;
    public bool contaminateWaterSources = true;
    public bool spawnContaminationZones = true;
    public bool spawnDirtSpots = true;
    public int dirtSpotsPerRoom = 4;
    public int contaminationZonesPerRoom = 1;
    public bool scaleDirtByRunDifficulty = true;
    public float easyDirtMultiplier = 0.75f;
    public float mediumDirtMultiplier = 1f;
    public float hardDirtMultiplier = 1.35f;
    public float gradualStartingDirtMultiplier = 0.75f;
    public float gradualEndingDirtMultiplier = 1.35f;
    [Min(1)] public int gradualDirtMaxPhase = 8;
    public float spawnHeightOffset = 0.08f;
    public float navMeshSampleRadius = 6f;
    public float roomBoundsInset = 1f;
    public LayerMask dirtPlacementMask = ~0;
    [Range(0f, 1f)] public float minimumFloorNormalDot = 0.65f;
    public bool allowWallDirtSpots = true;
    [Range(0f, 1f)] public float wallDirtChance = 0.35f;
    [Range(-1f, 1f)] public float minimumWallSurfaceUpDot = -0.1f;
    [Range(0f, 1f)] public float maximumWallSurfaceUpDot = 0.35f;
    public float wallDirtSearchRadius = 5f;
    public float wallDirtHeightOffset = 1.2f;
    public float dirtPlacementRaycastPadding = 2f;

    [Header("Multiplayer")]
    public bool runOnlyOnServer = true;

    [Header("Debug")]
    public bool logSpread = true;
    [SerializeField] private int nextRoomIndex;
    [SerializeField] private bool stormRunning;
    [SerializeField] private bool finalRoomReached;

    private const string SpawnedRoomsFieldName = "spawnedRooms";
    private float spreadTimer;
    private readonly List<GameObject> generatedRooms = new List<GameObject>();
    private readonly HashSet<GameObject> contaminatedRooms = new HashSet<GameObject>();
    private FieldInfo spawnedRoomsField;

    void Start()
    {
        if (roomGenerator == null)
            roomGenerator = FindObjectOfType<RoomGenerator>();

        CacheRoomGeneratorField();
        ResetSpreadTimerForStormStart();
        stormRunning = startStormOnStart;
    }

    void Update()
    {
        if (!stormRunning || finalRoomReached) return;
        if (!CanRunStorm()) return;

        spreadTimer -= Time.deltaTime;
        if (spreadTimer > 0f) return;

        spreadTimer = Mathf.Max(0.01f, spreadInterval);
        SpreadToNextRoom();
    }

    public void StartStorm()
    {
        stormRunning = true;
        finalRoomReached = false;
        ResetSpreadTimerForStormStart();
    }

    public void StopStorm()
    {
        stormRunning = false;
    }

    public void ResetStorm()
    {
        nextRoomIndex = 0;
        finalRoomReached = false;
        contaminatedRooms.Clear();
        ResetSpreadTimerForStormStart();
    }

    void ResetSpreadTimerForStormStart()
    {
        spreadTimer = contaminateFirstRoomImmediately
            ? 0f
            : Mathf.Max(0.01f, initialDelay);
    }

    [ContextMenu("Spread To Next Room")]
    public void SpreadToNextRoom()
    {
        if (!CanRunStorm()) return;

        RefreshGeneratedRooms();
        if (generatedRooms.Count == 0) return;

        nextRoomIndex = Mathf.Clamp(nextRoomIndex, 0, generatedRooms.Count - 1);

        while (nextRoomIndex < generatedRooms.Count)
        {
            GameObject room = generatedRooms[nextRoomIndex];
            nextRoomIndex++;

            if (room == null || contaminatedRooms.Contains(room))
                continue;

            ContaminateRoom(room, nextRoomIndex - 1);

            RoomDefinition definition = room.GetComponent<RoomDefinition>();
            if (stopAfterFinalRoom && definition != null && definition.category == RoomCategory.Final)
            {
                finalRoomReached = true;
                stormRunning = false;
            }

            return;
        }
    }

    void RefreshGeneratedRooms()
    {
        generatedRooms.Clear();

        if (useRoomGeneratorOrder && TryReadRoomsFromGenerator())
            return;

        RoomDefinition[] definitions = FindObjectsOfType<RoomDefinition>(includeInactiveRooms);
        List<RoomDefinition> sortedDefinitions = new List<RoomDefinition>(definitions);
        sortedDefinitions.Sort(CompareRoomsForFallbackOrder);

        for (int i = 0; i < sortedDefinitions.Count; i++)
        {
            if (sortedDefinitions[i] != null)
                generatedRooms.Add(sortedDefinitions[i].gameObject);
        }
    }

    bool TryReadRoomsFromGenerator()
    {
        if (roomGenerator == null)
            roomGenerator = FindObjectOfType<RoomGenerator>();

        if (roomGenerator == null)
            return false;

        if (spawnedRoomsField == null)
            CacheRoomGeneratorField();

        if (spawnedRoomsField == null)
            return false;

        List<GameObject> rooms = spawnedRoomsField.GetValue(roomGenerator) as List<GameObject>;
        if (rooms == null || rooms.Count == 0)
            return false;

        for (int i = 0; i < rooms.Count; i++)
        {
            if (rooms[i] != null)
                generatedRooms.Add(rooms[i]);
        }

        return generatedRooms.Count > 0;
    }

    void CacheRoomGeneratorField()
    {
        spawnedRoomsField = typeof(RoomGenerator).GetField(
            SpawnedRoomsFieldName,
            BindingFlags.Instance | BindingFlags.NonPublic);
    }

    int CompareRoomsForFallbackOrder(RoomDefinition first, RoomDefinition second)
    {
        int firstIndex = GetFallbackRoomIndex(first);
        int secondIndex = GetFallbackRoomIndex(second);

        int indexCompare = firstIndex.CompareTo(secondIndex);
        if (indexCompare != 0) return indexCompare;

        string firstName = first != null ? first.gameObject.name : string.Empty;
        string secondName = second != null ? second.gameObject.name : string.Empty;
        return string.CompareOrdinal(firstName, secondName);
    }

    int GetFallbackRoomIndex(RoomDefinition definition)
    {
        if (definition == null) return int.MaxValue;

        string roomName = definition.gameObject.name;
        for (int i = roomName.Length - 1; i >= 0; i--)
        {
            if (!char.IsDigit(roomName[i]))
            {
                if (i == roomName.Length - 1)
                    return int.MaxValue;

                string digits = roomName.Substring(i + 1);
                int parsedIndex;
                return int.TryParse(digits, out parsedIndex) ? parsedIndex : int.MaxValue;
            }
        }

        int wholeNameIndex;
        return int.TryParse(roomName, out wholeNameIndex) ? wholeNameIndex : int.MaxValue;
    }

    void ContaminateRoom(GameObject room, int roomIndex)
    {
        contaminatedRooms.Add(room);

        if (contaminateWaterSources)
            ContaminateWaterSources(room);

        if (spawnContaminationZones)
            SpawnContaminationZones(room);

        if (spawnDirtSpots)
            SpawnDirtSpots(room);

        if (logSpread)
            Debug.Log($"Contamination storm spread to room {roomIndex}: {room.name}");
    }

    void ContaminateWaterSources(GameObject room)
    {
        WaterSourceDryable[] sources = room.GetComponentsInChildren<WaterSourceDryable>(true);
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] != null)
                sources[i].Contaminate();
        }
    }

    void SpawnContaminationZones(GameObject room)
    {
        if (contaminationZonePrefab == null) return;

        int count = Mathf.Max(0, contaminationZonesPerRoom);
        for (int i = 0; i < count; i++)
        {
            Vector3 position;
            if (TryGetRoomSpawnPosition(room, out position))
                Instantiate(contaminationZonePrefab, position, Quaternion.identity);
        }
    }

    void SpawnDirtSpots(GameObject room)
    {
        if (dirtSpotPrefab == null) return;

        int count = GetEffectiveDirtSpotCount();
        for (int i = 0; i < count; i++)
        {
            Vector3 position;
            if (!TryGetDirtSpotSpawnPosition(room, out position)) continue;

            DirtSpot dirtSpot = Instantiate(dirtSpotPrefab, position, Quaternion.identity);
            if (!TrySpawnNetworkDirtSpot(dirtSpot))
                continue;

            dirtSpot.ConfigureGeneratedContaminatedSpot(
                0.35f,
                dirtSpot.contaminatedGrowthPerWaterChunk,
                dirtSpot.contaminatedWaterPerGrowthChunk);
        }
    }

    bool TryGetDirtSpotSpawnPosition(GameObject room, out Vector3 position)
    {
        if (allowWallDirtSpots &&
            Random.value < wallDirtChance &&
            TryGetRoomWallSpawnPosition(room, out position))
        {
            return true;
        }

        if (TryGetRoomSpawnPosition(room, out position))
            return true;

        return allowWallDirtSpots &&
            TryGetRoomWallSpawnPosition(room, out position);
    }

    int GetEffectiveDirtSpotCount()
    {
        int baseCount = Mathf.Max(0, dirtSpotsPerRoom);
        if (!scaleDirtByRunDifficulty || baseCount <= 0)
            return baseCount;

        float multiplier = GetDirtMultiplierForCurrentRun();
        return Mathf.Max(0, Mathf.RoundToInt(baseCount * multiplier));
    }

    float GetDirtMultiplierForCurrentRun()
    {
        switch (RegionRunState.Difficulty)
        {
            case RunDifficulty.Easy:
                return Mathf.Max(0f, easyDirtMultiplier);

            case RunDifficulty.Medium:
                return Mathf.Max(0f, mediumDirtMultiplier);

            case RunDifficulty.Hard:
                return Mathf.Max(0f, hardDirtMultiplier);

            case RunDifficulty.Gradual:
                int phase = Mathf.Max(1, RegionRunState.PhaseNumber);
                int maxPhase = Mathf.Max(1, gradualDirtMaxPhase);
                float t = maxPhase <= 1
                    ? 1f
                    : Mathf.Clamp01((phase - 1f) / (maxPhase - 1f));
                return Mathf.Max(
                    0f,
                    Mathf.Lerp(
                        gradualStartingDirtMultiplier,
                        gradualEndingDirtMultiplier,
                        t));

            default:
                return 1f;
        }
    }

    bool TrySpawnNetworkDirtSpot(DirtSpot dirtSpot)
    {
        if (dirtSpot == null)
            return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            if (IsMultiplayerRun())
            {
                Destroy(dirtSpot.gameObject);
                return false;
            }

            return true;
        }

        if (!networkManager.IsServer)
        {
            Destroy(dirtSpot.gameObject);
            return false;
        }

        NetworkObject networkObject = dirtSpot.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogWarning(
                $"Dirt spot prefab '{dirtSpot.name}' needs a NetworkObject for multiplayer spawning.");
            Destroy(dirtSpot.gameObject);
            return false;
        }

        if (!networkObject.IsSpawned)
            networkObject.Spawn(true);

        return true;
    }

    bool TryGetRoomSpawnPosition(GameObject room, out Vector3 position)
    {
        position = room != null ? room.transform.position : transform.position;
        if (room == null) return false;

        Bounds bounds = GetRoomBounds(room);
        Vector3 min = bounds.min + Vector3.one * roomBoundsInset;
        Vector3 max = bounds.max - Vector3.one * roomBoundsInset;

        if (min.x > max.x || min.z > max.z)
        {
            min = bounds.min;
            max = bounds.max;
        }

        for (int attempt = 0; attempt < 16; attempt++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(min.x, max.x),
                bounds.center.y,
                Random.Range(min.z, max.z));

            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidate, out hit, navMeshSampleRadius, NavMesh.AllAreas))
            {
                if (TryFindFloorAtRoomPoint(
                    hit.position,
                    bounds,
                    out RaycastHit floorHit))
                {
                    position = floorHit.point + floorHit.normal.normalized * spawnHeightOffset;
                    return true;
                }
            }

            if (TryFindFloorAtRoomPoint(candidate, bounds, out RaycastHit randomFloorHit))
            {
                position = randomFloorHit.point +
                    randomFloorHit.normal.normalized * spawnHeightOffset;
                return true;
            }
        }

        if (TryFindFloorAtRoomPoint(bounds.center, bounds, out RaycastHit fallbackHit))
        {
            position = fallbackHit.point +
                fallbackHit.normal.normalized * spawnHeightOffset;
            return true;
        }

        return false;
    }

    bool TryFindFloorAtRoomPoint(
        Vector3 point,
        Bounds roomBounds,
        out RaycastHit floorHit)
    {
        floorHit = default;

        float padding = Mathf.Max(0.1f, dirtPlacementRaycastPadding);
        Vector3 origin = new Vector3(
            point.x,
            Mathf.Min(point.y + padding, roomBounds.max.y + padding),
            point.z);
        float maxDistance = Mathf.Max(0.1f, padding * 2f);

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            maxDistance,
            dirtPlacementMask,
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return false;

        float minimumDot = Mathf.Clamp01(minimumFloorNormalDot);
        float bestY = float.NegativeInfinity;
        bool found = false;

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.collider == null)
                continue;

            if (Vector3.Dot(hit.normal.normalized, Vector3.up) < minimumDot)
                continue;

            if (!IsValidDirtSurfaceHit(hit, roomBounds))
                continue;

            if (!found || hit.point.y > bestY)
            {
                floorHit = hit;
                bestY = hit.point.y;
                found = true;
            }
        }

        return found;
    }

    bool TryGetRoomWallSpawnPosition(GameObject room, out Vector3 position)
    {
        position = room != null ? room.transform.position : transform.position;
        if (room == null) return false;

        Bounds bounds = GetRoomBounds(room);
        Vector3 min = bounds.min + Vector3.one * roomBoundsInset;
        Vector3 max = bounds.max - Vector3.one * roomBoundsInset;

        if (min.x > max.x || min.z > max.z)
        {
            min = bounds.min;
            max = bounds.max;
        }

        for (int attempt = 0; attempt < 16; attempt++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(min.x, max.x),
                bounds.center.y,
                Random.Range(min.z, max.z));

            NavMeshHit navHit;
            if (NavMesh.SamplePosition(
                candidate,
                out navHit,
                navMeshSampleRadius,
                NavMesh.AllAreas))
            {
                candidate = navHit.position;
            }

            candidate.y = Mathf.Clamp(
                candidate.y + wallDirtHeightOffset,
                bounds.min.y,
                bounds.max.y);

            if (TryFindWallNearPoint(candidate, bounds, out RaycastHit wallHit))
            {
                position = wallHit.point +
                    wallHit.normal.normalized * spawnHeightOffset;
                return true;
            }
        }

        return false;
    }

    bool TryFindWallNearPoint(
        Vector3 point,
        Bounds roomBounds,
        out RaycastHit wallHit)
    {
        wallHit = default;

        float searchRadius = Mathf.Max(0.1f, wallDirtSearchRadius);
        float minimumUpDot = minimumWallSurfaceUpDot;
        float maximumUpDot = maximumWallSurfaceUpDot;
        if (minimumUpDot > maximumUpDot)
        {
            float temp = minimumUpDot;
            minimumUpDot = maximumUpDot;
            maximumUpDot = temp;
        }

        Vector3[] directions =
        {
            Vector3.forward,
            Vector3.back,
            Vector3.left,
            Vector3.right,
            new Vector3(1f, 0f, 1f).normalized,
            new Vector3(-1f, 0f, 1f).normalized,
            new Vector3(1f, 0f, -1f).normalized,
            new Vector3(-1f, 0f, -1f).normalized
        };

        float bestDistance = float.MaxValue;
        bool found = false;

        for (int i = 0; i < directions.Length; i++)
        {
            RaycastHit[] hits = Physics.RaycastAll(
                point,
                directions[i],
                searchRadius,
                dirtPlacementMask,
                QueryTriggerInteraction.Ignore);

            for (int hitIndex = 0; hitIndex < hits.Length; hitIndex++)
            {
                RaycastHit hit = hits[hitIndex];
                if (!IsValidDirtSurfaceHit(hit, roomBounds))
                    continue;

                float upDot = Vector3.Dot(hit.normal.normalized, Vector3.up);
                if (upDot < minimumUpDot || upDot > maximumUpDot)
                    continue;

                if (hit.distance >= bestDistance)
                    continue;

                wallHit = hit;
                bestDistance = hit.distance;
                found = true;
            }
        }

        return found;
    }

    bool IsValidDirtSurfaceHit(RaycastHit hit, Bounds roomBounds)
    {
        if (hit.collider == null)
            return false;

        if (hit.collider.GetComponentInParent<DirtSpot>() != null)
            return false;

        float padding = Mathf.Max(0.1f, dirtPlacementRaycastPadding);
        if (hit.point.x < roomBounds.min.x - padding ||
            hit.point.x > roomBounds.max.x + padding ||
            hit.point.y < roomBounds.min.y - padding ||
            hit.point.y > roomBounds.max.y + padding ||
            hit.point.z < roomBounds.min.z - padding ||
            hit.point.z > roomBounds.max.z + padding)
        {
            return false;
        }

        return true;
    }

    Bounds GetRoomBounds(GameObject room)
    {
        RoomDefinition definition = room.GetComponent<RoomDefinition>();
        if (definition != null)
            return definition.GetWorldBounds();

        Renderer[] renderers = room.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            return bounds;
        }

        return new Bounds(room.transform.position, Vector3.one * 8f);
    }

    bool CanRunStorm()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (IsMultiplayerRun())
        {
            return networkManager != null &&
                networkManager.IsListening &&
                (!runOnlyOnServer || networkManager.IsServer);
        }

        if (!runOnlyOnServer) return true;

        if (networkManager != null && networkManager.IsListening)
            return networkManager.IsServer;

        return true;
    }

    static bool IsMultiplayerRun()
    {
        return RegionRunState.HasSelectedRegion && RegionRunState.IsMultiplayer;
    }
}
