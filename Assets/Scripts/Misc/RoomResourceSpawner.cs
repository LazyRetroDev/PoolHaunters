using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class RoomResourceSpawner : MonoBehaviour
{
    [Serializable]
    public class ResourceEntry
    {
        public string label;
        public GameObject prefab;
        public RoomResourceCategory category = RoomResourceCategory.Pickup;

        [Min(0f)]
        public float weight = 1f;

        [Min(0)]
        public int minimumRoomIndex;

        [Tooltip("Use -1 for no maximum.")]
        public int maximumRoomIndex = -1;

        public bool IsAvailableForRoom(int roomIndex)
        {
            if (prefab == null || weight <= 0f) return false;
            if (roomIndex < minimumRoomIndex) return false;
            return maximumRoomIndex < 0 || roomIndex <= maximumRoomIndex;
        }
    }

    [Header("Spawn Table")]
    public ResourceEntry[] resources;

    [Header("Randomization")]
    public int seedOffset = 1847;

    [Range(0f, 2f)]
    public float spawnChanceMultiplier = 1f;

    [Header("Missing Spawn Point Fallback")]
    public bool useFallbackSpawnPoints = true;

    [Min(1)]
    public int fallbackAttemptsPerRoom = 2;

    [Range(0f, 1f)]
    public float fallbackSpawnChance = 0.65f;

    public RoomResourceCategory fallbackAllowedCategories =
        RoomResourceCategory.All;

    [Min(0f)]
    public float fallbackEdgePadding = 1.25f;

    [Min(0f)]
    public float fallbackFloorOffset = 0.08f;

    public LayerMask fallbackGroundLayers = ~0;

    [Header("Multiplayer")]
    [Tooltip("During a network session, resource prefabs must have a NetworkObject and be registered with NetworkManager.")]
    public bool requireNetworkObjectOnline = true;

    [Tooltip("Warn when a selected online prefab cannot be network-spawned.")]
    public bool logInvalidNetworkPrefabs = true;

    private readonly Dictionary<GameObject, List<GameObject>> resourcesByRoom =
        new Dictionary<GameObject, List<GameObject>>();

    public void SpawnResourcesForRoom(GameObject room, int roomIndex, int runSeed)
    {
        if (room == null || !CanSpawnAuthoritatively()) return;

        RoomResourceSpawnPoint[] spawnPoints =
            room.GetComponentsInChildren<RoomResourceSpawnPoint>(true);

        System.Random random = new System.Random(
            CreateRoomSeed(runSeed, roomIndex));
        List<GameObject> spawnedResources = new List<GameObject>();

        if (spawnPoints == null || spawnPoints.Length == 0)
        {
            if (useFallbackSpawnPoints)
            {
                SpawnFallbackResources(
                    room,
                    roomIndex,
                    random,
                    spawnedResources);
            }

            if (spawnedResources.Count > 0)
                resourcesByRoom[room] = spawnedResources;
            return;
        }

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            RoomResourceSpawnPoint point = spawnPoints[i];
            if (point == null) continue;

            float chance = Mathf.Clamp01(point.spawnChance * spawnChanceMultiplier);
            if (random.NextDouble() > chance) continue;

            ResourceEntry selected = ChooseResource(point, roomIndex, random);
            if (selected == null) continue;

            point.GetSpawnPose(out Vector3 position, out Quaternion rotation);
            GameObject instance = SpawnResource(selected, position, rotation);
            if (instance != null)
                spawnedResources.Add(instance);
        }

        if (spawnedResources.Count > 0)
            resourcesByRoom[room] = spawnedResources;
    }

    public void DespawnResourcesForRoom(GameObject room)
    {
        if (room == null || !resourcesByRoom.TryGetValue(room, out List<GameObject> spawned))
            return;

        if (CanSpawnAuthoritatively())
        {
            for (int i = 0; i < spawned.Count; i++)
                DespawnResource(spawned[i]);
        }

        resourcesByRoom.Remove(room);
    }

    void SpawnFallbackResources(
        GameObject room,
        int roomIndex,
        System.Random random,
        List<GameObject> spawnedResources)
    {
        RoomDefinition definition = room.GetComponent<RoomDefinition>();
        if (definition == null)
            definition = room.GetComponentInChildren<RoomDefinition>(true);

        if (definition == null)
        {
            Debug.LogWarning(
                room.name +
                " has no resource spawn points or RoomDefinition for fallback placement.");
            return;
        }

        int attempts = Mathf.Max(1, fallbackAttemptsPerRoom);
        for (int i = 0; i < attempts; i++)
        {
            if (random.NextDouble() > fallbackSpawnChance *
                spawnChanceMultiplier)
            {
                continue;
            }

            ResourceEntry selected = ChooseResource(
                fallbackAllowedCategories,
                roomIndex,
                random);
            if (selected == null)
                continue;

            Vector3 position;
            Quaternion rotation;
            if (!TryGetFallbackSpawnPose(
                definition,
                random,
                out position,
                out rotation))
            {
                continue;
            }

            GameObject instance = SpawnResource(
                selected,
                position,
                rotation);
            if (instance != null)
                spawnedResources.Add(instance);
        }
    }

    bool TryGetFallbackSpawnPose(
        RoomDefinition definition,
        System.Random random,
        out Vector3 position,
        out Quaternion rotation)
    {
        Vector3 size = definition.size;
        float halfX = Mathf.Max(0f, size.x * 0.5f - fallbackEdgePadding);
        float halfZ = Mathf.Max(0f, size.z * 0.5f - fallbackEdgePadding);
        float localX = Mathf.Lerp(
            -halfX,
            halfX,
            (float)random.NextDouble());
        float localZ = Mathf.Lerp(
            -halfZ,
            halfZ,
            (float)random.NextDouble());

        Vector3 localTop = definition.boundsCenter +
            new Vector3(localX, size.y * 0.5f + 1f, localZ);
        Vector3 rayOrigin = definition.transform.TransformPoint(localTop);
        Vector3 down = -definition.transform.up;
        float rayDistance = Mathf.Max(3f, size.y + 3f);
        RaycastHit[] hits = Physics.RaycastAll(
            rayOrigin,
            down,
            rayDistance,
            fallbackGroundLayers,
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

            float height = Vector3.Dot(
                hit.point,
                definition.transform.up);
            if (height >= lowestHeight)
                continue;

            lowestHeight = height;
            floorHit = hit;
            foundFloor = true;
        }

        if (foundFloor)
        {
            position = floorHit.point +
                definition.transform.up * fallbackFloorOffset;
        }
        else
        {
            Vector3 localFloor = definition.boundsCenter +
                new Vector3(
                    localX,
                    -size.y * 0.5f + fallbackFloorOffset,
                    localZ);
            position = definition.transform.TransformPoint(localFloor);
        }

        float yaw = (float)random.NextDouble() * 360f;
        rotation = Quaternion.AngleAxis(yaw, definition.transform.up) *
            definition.transform.rotation;
        return true;
    }

    ResourceEntry ChooseResource(
        RoomResourceSpawnPoint point,
        int roomIndex,
        System.Random random)
    {
        return ChooseResource(
            point.allowedCategories,
            roomIndex,
            random);
    }

    ResourceEntry ChooseResource(
        RoomResourceCategory allowedCategories,
        int roomIndex,
        System.Random random)
    {
        if (resources == null || resources.Length == 0)
            return null;

        float totalWeight = 0f;
        for (int i = 0; i < resources.Length; i++)
        {
            ResourceEntry entry = resources[i];
            if (entry == null || !entry.IsAvailableForRoom(roomIndex)) continue;
            if (!AllowsCategory(allowedCategories, entry.category)) continue;
            totalWeight += entry.weight;
        }

        if (totalWeight <= 0f)
            return null;

        double roll = random.NextDouble() * totalWeight;
        for (int i = 0; i < resources.Length; i++)
        {
            ResourceEntry entry = resources[i];
            if (entry == null || !entry.IsAvailableForRoom(roomIndex)) continue;
            if (!AllowsCategory(allowedCategories, entry.category)) continue;

            roll -= entry.weight;
            if (roll <= 0d)
                return entry;
        }

        return null;
    }

    bool AllowsCategory(
        RoomResourceCategory allowedCategories,
        RoomResourceCategory category)
    {
        return category != RoomResourceCategory.None &&
            (allowedCategories & category) != RoomResourceCategory.None;
    }

    GameObject SpawnResource(ResourceEntry entry, Vector3 position, Quaternion rotation)
    {
        GameObject instance = Instantiate(entry.prefab, position, rotation);
        NetworkManager networkManager = NetworkManager.Singleton;
        bool online = networkManager != null && networkManager.IsListening;

        if (!online)
            return instance;

        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            networkObject.Spawn(true);
            return instance;
        }

        if (requireNetworkObjectOnline)
        {
            if (logInvalidNetworkPrefabs)
            {
                string resourceName = string.IsNullOrWhiteSpace(entry.label)
                    ? entry.prefab.name
                    : entry.label;

                Debug.LogWarning(
                    $"Room resource '{resourceName}' needs a NetworkObject and NetworkManager registration for multiplayer spawning.");
            }

            Destroy(instance);
            return null;
        }

        return instance;
    }

    void DespawnResource(GameObject instance)
    {
        if (instance == null) return;

        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.IsSpawned)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                networkObject.Despawn(true);

            return;
        }

        Destroy(instance);
    }

    bool CanSpawnAuthoritatively()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return true;

        return networkManager.IsServer;
    }

    int CreateRoomSeed(int runSeed, int roomIndex)
    {
        unchecked
        {
            int result = runSeed;
            result = result * 397 ^ seedOffset;
            result = result * 397 ^ roomIndex;
            return result;
        }
    }
}
