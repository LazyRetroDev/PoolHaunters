using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using System.Collections.Generic;
using Unity.Netcode;

public class EnemySpawner : MonoBehaviour
{
    public static EnemySpawner Instance;

    public GameObject timeCamperPrefab;
    public Transform player;
    public float minSpawnDistanceFromPlayer = 10f;
    public float minSpawnDistanceFromTimeCampers = 5f;
    public int spawnAttempts = 30;
    public float sampleRadius = 10f;

    [Header("Multiplayer")]
    public bool requireNetworkObjectOnline = true;
    public bool logInvalidNetworkPrefabs = true;

    private readonly List<NavMeshSurface> surfaces = new List<NavMeshSurface>();
    private bool navMeshReady;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void RegisterSurface(NavMeshSurface surface, bool buildNow = false)
    {
        if (surface == null || surfaces.Contains(surface)) return;

        ConfigureSurfaceForRuntimeRoom(surface);
        surfaces.Add(surface);

        if (buildNow)
            surface.BuildNavMesh();

        navMeshReady = surfaces.Count > 0;
    }

    public void UnregisterSurface(NavMeshSurface surface)
    {
        if (surface == null) return;

        surface.RemoveData();
        surfaces.Remove(surface);
        navMeshReady = surfaces.Count > 0;
    }

    public void RebuildAllSurfaces()
    {
        surfaces.RemoveAll(surface => surface == null);

        foreach (NavMeshSurface surface in surfaces)
        {
            ConfigureSurfaceForRuntimeRoom(surface);
            surface.BuildNavMesh();
        }

        navMeshReady = surfaces.Count > 0;
    }

    void ConfigureSurfaceForRuntimeRoom(NavMeshSurface surface)
    {
        surface.collectObjects = CollectObjects.Children;
    }

    public void SpawnTimeCamper(bool isClone = false)
    {
        if (!CanSpawnAuthoritatively()) return;
        if (!navMeshReady || timeCamperPrefab == null) return;
        if (TimeCamperManager.Instance == null || !TimeCamperManager.Instance.CanSpawn()) return;

        Vector3 spawnPos;
        if (!TryGetValidSpawnPosition(out spawnPos)) return;

        CreateTimeCamper(spawnPos, isClone);
    }

    public TimeCamper SpawnTimeCamperAt(Vector3 position, bool isClone = true)
    {
        if (!CanSpawnAuthoritatively()) return null;
        if (timeCamperPrefab == null) return null;
        if (TimeCamperManager.Instance == null || !TimeCamperManager.Instance.CanSpawn()) return null;

        Vector3 spawnPos = position;
        NavMeshHit hit;
        if (NavMesh.SamplePosition(position, out hit, sampleRadius, NavMesh.AllAreas))
            spawnPos = hit.position;

        return CreateTimeCamper(spawnPos, isClone);
    }

    TimeCamper CreateTimeCamper(Vector3 spawnPos, bool isClone)
    {
        GameObject entity = Instantiate(timeCamperPrefab, spawnPos, Quaternion.identity);
        TimeCamper timeCamper = entity.GetComponent<TimeCamper>();
        if (timeCamper == null)
        {
            Destroy(entity);
            return null;
        }

        timeCamper.player = player;
        timeCamper.isClone = isClone;

        if (!TrySpawnNetworkObject(entity))
        {
            Destroy(entity);
            return null;
        }

        TimeCamperManager.Instance.Register(timeCamper);
        return timeCamper;
    }

    public bool TryGetValidSpawnPosition(out Vector3 result)
    {
        surfaces.RemoveAll(surface => surface == null);

        if (surfaces.Count == 0)
        {
            result = Vector3.zero;
            navMeshReady = false;
            return false;
        }

        int attempts = Mathf.Max(1, spawnAttempts);
        for (int i = 0; i < attempts; i++)
        {
            Vector3 randomPoint = GetRandomSurfacePoint();
            NavMeshHit hit;

            if (!NavMesh.SamplePosition(randomPoint, out hit, sampleRadius, NavMesh.AllAreas))
                continue;

            if (!IsFarEnoughFromPlayer(hit.position))
                continue;

            if (!IsFarEnoughFromOtherTimeCampers(hit.position))
                continue;

            result = hit.position;
            return true;
        }

        result = Vector3.zero;
        return false;
    }

    bool IsFarEnoughFromPlayer(Vector3 position)
    {
        if (player == null) return true;
        return Vector3.Distance(position, player.position) >= minSpawnDistanceFromPlayer;
    }

    bool IsFarEnoughFromOtherTimeCampers(Vector3 position)
    {
        TimeCamper[] timeCampers =
            FindObjectsByType<TimeCamper>(FindObjectsInactive.Exclude);
        for (int i = 0; i < timeCampers.Length; i++)
        {
            TimeCamper timeCamper = timeCampers[i];
            if (timeCamper == null) continue;

            float minDistance = Mathf.Max(minSpawnDistanceFromTimeCampers, timeCamper.GetImpactRadius());
            if (Vector3.Distance(position, timeCamper.transform.position) < minDistance)
                return false;
        }

        return true;
    }

    Vector3 GetRandomSurfacePoint()
    {
        NavMeshSurface surface = surfaces[Random.Range(0, surfaces.Count)];
        Bounds bounds = GetSurfaceBounds(surface);

        return new Vector3(
            Random.Range(bounds.min.x, bounds.max.x),
            bounds.center.y,
            Random.Range(bounds.min.z, bounds.max.z)
        );
    }

    Bounds GetSurfaceBounds(NavMeshSurface surface)
    {
        Collider[] colliders = surface.GetComponentsInChildren<Collider>();
        if (colliders.Length > 0)
        {
            Bounds bounds = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++)
                bounds.Encapsulate(colliders[i].bounds);
            return bounds;
        }

        Renderer[] renderers = surface.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        return new Bounds(surface.transform.position, Vector3.one * 20f);
    }

    bool TrySpawnNetworkObject(GameObject entity)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        bool online = networkManager != null && networkManager.IsListening;

        if (!online)
            return true;

        NetworkObject networkObject = entity.GetComponent<NetworkObject>();
        if (networkObject != null)
        {
            networkObject.Spawn(true);
            return true;
        }

        if (!requireNetworkObjectOnline)
            return true;

        if (logInvalidNetworkPrefabs)
        {
            string prefabName = timeCamperPrefab != null
                ? timeCamperPrefab.name
                : "TimeCamper";
            Debug.LogWarning(
                $"Enemy '{prefabName}' needs a NetworkObject and NetworkManager registration for multiplayer spawning.");
        }

        return false;
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
}
