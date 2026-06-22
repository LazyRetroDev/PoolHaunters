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

        if (spawnPoints == null || spawnPoints.Length == 0)
            return;

        System.Random random = new System.Random(CreateRoomSeed(runSeed, roomIndex));
        List<GameObject> spawnedResources = new List<GameObject>();

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

    ResourceEntry ChooseResource(
        RoomResourceSpawnPoint point,
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
            if (!point.Allows(entry.category)) continue;
            totalWeight += entry.weight;
        }

        if (totalWeight <= 0f)
            return null;

        double roll = random.NextDouble() * totalWeight;
        for (int i = 0; i < resources.Length; i++)
        {
            ResourceEntry entry = resources[i];
            if (entry == null || !entry.IsAvailableForRoom(roomIndex)) continue;
            if (!point.Allows(entry.category)) continue;

            roll -= entry.weight;
            if (roll <= 0d)
                return entry;
        }

        return null;
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
