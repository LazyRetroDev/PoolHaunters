using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using System.Collections.Generic;

public class EnemySpawner : MonoBehaviour
{
    public static EnemySpawner Instance;

    public GameObject timeCamperPrefab;
    public Transform player;
    public float minSpawnDistanceFromPlayer = 10f;
    public int spawnAttempts = 30;
    public float sampleRadius = 10f;

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

        surfaces.Add(surface);

        if (buildNow)
            surface.BuildNavMesh();

        navMeshReady = surfaces.Count > 0;
    }

    public void UnregisterSurface(NavMeshSurface surface)
    {
        if (surface == null) return;

        surfaces.Remove(surface);
        navMeshReady = surfaces.Count > 0;
    }

    public void RebuildAllSurfaces()
    {
        surfaces.RemoveAll(surface => surface == null);

        foreach (NavMeshSurface surface in surfaces)
            surface.BuildNavMesh();

        navMeshReady = surfaces.Count > 0;
    }

    public void SpawnTimeCamper(bool isClone = false)
    {
        if (!navMeshReady || timeCamperPrefab == null) return;
        if (TimeCamperManager.Instance == null || !TimeCamperManager.Instance.CanSpawn()) return;

        Vector3 spawnPos;
        if (!TryGetValidSpawnPosition(out spawnPos)) return;

        GameObject entity = Instantiate(timeCamperPrefab, spawnPos, Quaternion.identity);
        TimeCamper timeCamper = entity.GetComponent<TimeCamper>();
        if (timeCamper == null) return;

        timeCamper.player = player;
        timeCamper.isClone = isClone;
        TimeCamperManager.Instance.Register(timeCamper);
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

            if (player != null)
            {
                float distToPlayer = Vector3.Distance(hit.position, player.position);
                if (distToPlayer < minSpawnDistanceFromPlayer)
                    continue;
            }

            result = hit.position;
            return true;
        }

        result = Vector3.zero;
        return false;
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
}
