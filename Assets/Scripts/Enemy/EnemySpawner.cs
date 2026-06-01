using UnityEngine;
using UnityEngine.AI;
using Unity.AI.Navigation;
using System.Collections;
using System.Collections.Generic;

public class EnemySpawner : MonoBehaviour
{
    public static EnemySpawner Instance;

    public GameObject timeCamperPrefab;
    public Transform player;
    public float minSpawnDistanceFromPlayer = 10f;
<<<<<<< Updated upstream
=======
    public float minSpawnDistanceFromTimeCampers = 5f;
    public int spawnAttempts = 30;
    public float sampleRadius = 10f;
>>>>>>> Stashed changes

    private List<NavMeshSurface> surfaces = new List<NavMeshSurface>();
    private bool navMeshReady = false;

    void Awake()
    {
        Instance = this;
    }

    public void RegisterSurface(NavMeshSurface surface)
    {
<<<<<<< Updated upstream
=======
        if (Instance == this)
            Instance = null;
    }

    public void RegisterSurface(NavMeshSurface surface, bool buildNow = false)
    {
        if (surface == null || surfaces.Contains(surface)) return;

        ConfigureSurfaceForRuntimeRoom(surface);
>>>>>>> Stashed changes
        surfaces.Add(surface);
    }

    public void BakeAllAndSpawn()
    {
<<<<<<< Updated upstream
        StartCoroutine(BakeAndSpawn());
=======
        if (surface == null) return;

        surface.RemoveData();
        surfaces.Remove(surface);
        navMeshReady = surfaces.Count > 0;
>>>>>>> Stashed changes
    }

    IEnumerator BakeAndSpawn()
    {
        Debug.Log("Surfaces to bake: " + surfaces.Count);
        foreach (NavMeshSurface surface in surfaces)
        {
<<<<<<< Updated upstream
            surface.BuildNavMesh();
            yield return null;
        }
        navMeshReady = true;
        SpawnTimeCamper();
    }
=======
            ConfigureSurfaceForRuntimeRoom(surface);
            surface.BuildNavMesh();
        }

        navMeshReady = surfaces.Count > 0;
    }

    void ConfigureSurfaceForRuntimeRoom(NavMeshSurface surface)
    {
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
    }

>>>>>>> Stashed changes
    public void SpawnTimeCamper(bool isClone = false)
    {
        if (!navMeshReady) return;
        if (!TimeCamperManager.Instance.CanSpawn()) return;

        Vector3 spawnPos;
<<<<<<< Updated upstream
        if (TryGetValidSpawnPosition(out spawnPos))
        {
            GameObject entity = Instantiate(timeCamperPrefab, spawnPos, Quaternion.identity);
            TimeCamper tc = entity.GetComponent<TimeCamper>();
            tc.player = player;
            tc.isClone = isClone;
            TimeCamperManager.Instance.Register(tc);
        }
=======
        if (!TryGetValidSpawnPosition(out spawnPos)) return;

        CreateTimeCamper(spawnPos, isClone);
    }

    public TimeCamper SpawnTimeCamperAt(Vector3 position, bool isClone = true)
    {
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
        if (timeCamper == null) return null;

        timeCamper.player = player;
        timeCamper.isClone = isClone;
        TimeCamperManager.Instance.Register(timeCamper);
        return timeCamper;
>>>>>>> Stashed changes
    }

    public bool TryGetValidSpawnPosition(out Vector3 result)
    {
        for (int i = 0; i < 30; i++)
        {
            NavMeshHit hit;
<<<<<<< Updated upstream
            Vector3 randomPoint = GetRandomNavMeshPoint();
            Debug.Log("Trying point: " + randomPoint);
            if (NavMesh.SamplePosition(randomPoint, out hit, 10f, NavMesh.AllAreas))
            {
                float distToPlayer = Vector3.Distance(hit.position, player.position);
                Debug.Log("Valid point found: " + hit.position + " dist: " + distToPlayer);
                if (distToPlayer >= minSpawnDistanceFromPlayer)
                {
                    result = hit.position;
                    return true;
                }
            }
            else
            {
                Debug.Log("SamplePosition failed for: " + randomPoint);
            }
=======

            if (!NavMesh.SamplePosition(randomPoint, out hit, sampleRadius, NavMesh.AllAreas))
                continue;

            if (!IsFarEnoughFromPlayer(hit.position))
                continue;

            if (!IsFarEnoughFromOtherTimeCampers(hit.position))
                continue;

            result = hit.position;
            return true;
>>>>>>> Stashed changes
        }
        Debug.Log("No valid spawn found after 30 attempts!");
        result = Vector3.zero;
        return false;
    }

<<<<<<< Updated upstream
    Vector3 GetRandomNavMeshPoint()
=======
    bool IsFarEnoughFromPlayer(Vector3 position)
    {
        if (player == null) return true;
        return Vector3.Distance(position, player.position) >= minSpawnDistanceFromPlayer;
    }

    bool IsFarEnoughFromOtherTimeCampers(Vector3 position)
    {
        TimeCamper[] timeCampers = FindObjectsOfType<TimeCamper>();
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
>>>>>>> Stashed changes
    {
        if (surfaces.Count == 0) return Vector3.zero;
        NavMeshSurface surface = surfaces[Random.Range(0, surfaces.Count)];
        Bounds bounds = surface.GetComponent<Collider>() != null
            ? surface.GetComponent<Collider>().bounds
            : new Bounds(surface.transform.position, Vector3.one * 20f);

        return new Vector3(
            Random.Range(bounds.min.x, bounds.max.x),
            bounds.center.y,
            Random.Range(bounds.min.z, bounds.max.z)
        );
    }
<<<<<<< Updated upstream
=======

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
>>>>>>> Stashed changes
}