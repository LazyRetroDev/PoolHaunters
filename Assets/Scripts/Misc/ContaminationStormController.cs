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
    public float spawnHeightOffset = 0.08f;
    public float navMeshSampleRadius = 6f;
    public float roomBoundsInset = 1f;

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

        int count = Mathf.Max(0, dirtSpotsPerRoom);
        for (int i = 0; i < count; i++)
        {
            Vector3 position;
            if (!TryGetRoomSpawnPosition(room, out position)) continue;

            DirtSpot dirtSpot = Instantiate(dirtSpotPrefab, position, Quaternion.identity);
            if (!TrySpawnNetworkDirtSpot(dirtSpot))
                continue;

            dirtSpot.ConfigureGeneratedContaminatedSpot(
                0.35f,
                dirtSpot.contaminatedGrowthPerWaterChunk,
                dirtSpot.contaminatedWaterPerGrowthChunk);
        }
    }

    bool TrySpawnNetworkDirtSpot(DirtSpot dirtSpot)
    {
        if (dirtSpot == null)
            return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return true;

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

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3 candidate = new Vector3(
                Random.Range(min.x, max.x),
                bounds.center.y,
                Random.Range(min.z, max.z));

            NavMeshHit hit;
            if (NavMesh.SamplePosition(candidate, out hit, navMeshSampleRadius, NavMesh.AllAreas))
            {
                position = hit.position + Vector3.up * spawnHeightOffset;
                return true;
            }
        }

        position = bounds.center + Vector3.up * spawnHeightOffset;
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
        if (!runOnlyOnServer) return true;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
            return networkManager.IsServer;

        return !RegionRunState.HasSelectedRegion ||
            RegionRunState.IsSinglePlayer ||
            RegionRunState.IsHost;
    }
}
