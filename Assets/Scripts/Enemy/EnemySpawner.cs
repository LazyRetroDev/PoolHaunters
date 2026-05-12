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

    private List<NavMeshSurface> surfaces = new List<NavMeshSurface>();
    private bool navMeshReady = false;

    void Awake()
    {
        Instance = this;
    }

    public void RegisterSurface(NavMeshSurface surface)
    {
        surfaces.Add(surface);
    }

    public void BakeAllAndSpawn()
    {
        StartCoroutine(BakeAndSpawn());
    }

    IEnumerator BakeAndSpawn()
    {
        Debug.Log("Surfaces to bake: " + surfaces.Count);
        foreach (NavMeshSurface surface in surfaces)
        {
            surface.BuildNavMesh();
            yield return null;
        }
        navMeshReady = true;
        SpawnTimeCamper();
    }
    public void SpawnTimeCamper(bool isClone = false)
    {
        if (!navMeshReady) return;
        if (!TimeCamperManager.Instance.CanSpawn()) return;

        Vector3 spawnPos;
        if (TryGetValidSpawnPosition(out spawnPos))
        {
            GameObject entity = Instantiate(timeCamperPrefab, spawnPos, Quaternion.identity);
            TimeCamper tc = entity.GetComponent<TimeCamper>();
            tc.player = player;
            tc.isClone = isClone;
            TimeCamperManager.Instance.Register(tc);
        }
    }

    public bool TryGetValidSpawnPosition(out Vector3 result)
    {
        for (int i = 0; i < 30; i++)
        {
            NavMeshHit hit;
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
        }
        Debug.Log("No valid spawn found after 30 attempts!");
        result = Vector3.zero;
        return false;
    }

    Vector3 GetRandomNavMeshPoint()
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
}