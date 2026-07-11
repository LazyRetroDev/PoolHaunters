using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[DisallowMultipleComponent]
public class RoomEnemySpawner : MonoBehaviour
{
    struct EnemySelection
    {
        public string label;
        public GameObject prefab;
        public RoomEnemyCategory category;

        public EnemySelection(RoomContentProfile.EnemyEntry entry)
        {
            label = entry != null ? entry.label : string.Empty;
            prefab = entry != null ? entry.prefab : null;
            category = entry != null ? entry.category : RoomEnemyCategory.None;
        }
    }

    [Header("Randomization")]
    public int seedOffset = 9361;

    [Range(0f, 2f)]
    public float spawnChanceMultiplier = 1f;

    [Header("NavMesh Placement")]
    public bool requireNavMeshPosition = true;

    [Min(0.1f)]
    public float navMeshSampleRadius = 2f;

    public int navMeshAreaMask = NavMesh.AllAreas;

    [Header("Multiplayer")]
    [Tooltip("During a network session, enemy prefabs must have a NetworkObject and be registered with NetworkManager.")]
    public bool requireNetworkObjectOnline = true;

    [Tooltip("Warn when a selected online prefab cannot be network-spawned.")]
    public bool logInvalidNetworkPrefabs = true;

    private readonly Dictionary<GameObject, List<GameObject>> enemiesByRoom =
        new Dictionary<GameObject, List<GameObject>>();

    public void SpawnEnemiesForRoom(GameObject room, int roomIndex, int runSeed)
    {
        if (room == null || !CanSpawnAuthoritatively()) return;

        RoomDefinition definition = GetRoomDefinition(room);
        RoomContentProfile contentProfile =
            definition != null ? definition.contentProfile : null;

        if (contentProfile == null || !contentProfile.HasEnemyTable)
            return;

        RoomEnemySpawnPoint[] spawnPoints =
            room.GetComponentsInChildren<RoomEnemySpawnPoint>(true);
        if (spawnPoints == null || spawnPoints.Length == 0)
            return;

        System.Random random = new System.Random(
            CreateRoomSeed(runSeed, roomIndex));
        List<GameObject> spawnedEnemies = new List<GameObject>();
        float effectiveSpawnChanceMultiplier =
            GetEffectiveSpawnChanceMultiplier(contentProfile);

        for (int i = 0; i < spawnPoints.Length; i++)
        {
            RoomEnemySpawnPoint point = spawnPoints[i];
            if (point == null) continue;

            float chance = Mathf.Clamp01(
                point.spawnChance * effectiveSpawnChanceMultiplier);
            if (random.NextDouble() > chance) continue;

            EnemySelection selected;
            if (!TryChooseEnemy(
                contentProfile,
                point.allowedCategories,
                roomIndex,
                random,
                out selected))
            {
                continue;
            }

            point.GetSpawnPose(out Vector3 position, out Quaternion rotation);
            if (!TryResolveNavMeshPose(position, rotation, out position, out rotation))
                continue;

            GameObject instance = SpawnEnemy(selected, position, rotation);
            if (instance != null)
                spawnedEnemies.Add(instance);
        }

        if (spawnedEnemies.Count > 0)
            enemiesByRoom[room] = spawnedEnemies;
    }

    public void DespawnEnemiesForRoom(GameObject room)
    {
        if (room == null || !enemiesByRoom.TryGetValue(room, out List<GameObject> spawned))
            return;

        if (CanSpawnAuthoritatively())
        {
            for (int i = 0; i < spawned.Count; i++)
                DespawnEnemy(spawned[i]);
        }

        enemiesByRoom.Remove(room);
    }

    bool TryChooseEnemy(
        RoomContentProfile contentProfile,
        RoomEnemyCategory allowedCategories,
        int roomIndex,
        System.Random random,
        out EnemySelection selected)
    {
        selected = new EnemySelection();

        RoomContentProfile.EnemyEntry entry;
        if (!contentProfile.TryChooseEnemy(
            allowedCategories,
            roomIndex,
            random,
            out entry))
        {
            return false;
        }

        selected = new EnemySelection(entry);
        return selected.prefab != null;
    }

    bool TryResolveNavMeshPose(
        Vector3 requestedPosition,
        Quaternion requestedRotation,
        out Vector3 resolvedPosition,
        out Quaternion resolvedRotation)
    {
        resolvedPosition = requestedPosition;
        resolvedRotation = requestedRotation;

        if (!requireNavMeshPosition)
            return true;

        NavMeshHit hit;
        if (!NavMesh.SamplePosition(
            requestedPosition,
            out hit,
            navMeshSampleRadius,
            navMeshAreaMask))
        {
            return false;
        }

        resolvedPosition = hit.position;
        return true;
    }

    GameObject SpawnEnemy(
        EnemySelection selection,
        Vector3 position,
        Quaternion rotation)
    {
        if (selection.prefab == null)
            return null;

        GameObject instance = Instantiate(selection.prefab, position, rotation);
        RegisterSpecialEnemySystems(instance);

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
                string enemyName = string.IsNullOrWhiteSpace(selection.label)
                    ? selection.prefab.name
                    : selection.label;

                Debug.LogWarning(
                    $"Room enemy '{enemyName}' needs a NetworkObject and NetworkManager registration for multiplayer spawning.");
            }

            UnregisterSpecialEnemySystems(instance);
            Destroy(instance);
            return null;
        }

        return instance;
    }

    void DespawnEnemy(GameObject instance)
    {
        if (instance == null) return;

        UnregisterSpecialEnemySystems(instance);

        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        if (networkObject != null && networkObject.IsSpawned)
        {
            if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
                networkObject.Despawn(true);

            return;
        }

        Destroy(instance);
    }

    void RegisterSpecialEnemySystems(GameObject instance)
    {
        if (instance == null)
            return;

        TimeCamper timeCamper = instance.GetComponent<TimeCamper>();
        if (timeCamper != null && TimeCamperManager.Instance != null)
            TimeCamperManager.Instance.Register(timeCamper);
    }

    void UnregisterSpecialEnemySystems(GameObject instance)
    {
        if (instance == null)
            return;

        TimeCamper timeCamper = instance.GetComponent<TimeCamper>();
        if (timeCamper != null && TimeCamperManager.Instance != null)
            TimeCamperManager.Instance.Unregister(timeCamper);
    }

    RoomDefinition GetRoomDefinition(GameObject room)
    {
        if (room == null)
            return null;

        RoomDefinition definition = room.GetComponent<RoomDefinition>();
        if (definition == null)
            definition = room.GetComponentInChildren<RoomDefinition>(true);

        return definition;
    }

    float GetEffectiveSpawnChanceMultiplier(RoomContentProfile contentProfile)
    {
        return spawnChanceMultiplier *
            Mathf.Max(0f, contentProfile.enemySpawnChanceMultiplier);
    }

    bool CanSpawnAuthoritatively()
    {
        NetworkManager networkManager = NetworkManager.Singleton;

        if (RegionRunState.HasSelectedRegion && RegionRunState.IsMultiplayer)
        {
            return networkManager != null &&
                networkManager.IsListening &&
                networkManager.IsServer;
        }

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
