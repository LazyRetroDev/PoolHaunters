using UnityEngine;
using UnityEngine.AI;

public class WillOWispBehavior : MonoBehaviour
{
    [Header("Movement")]
    public float wanderSpeed = 3f;
    public float wanderRadius = 14f;
    public float destinationRefreshDelay = 0.5f;

    [Header("Drying / Ignition")]
    public float effectRadius = 2.5f;
    public float effectInterval = 1f;
    public GameObject fireHazardPrefab;
    public bool destroyItemsOnIgnite = false;
    public string itemTag = "Item";
    public string poolTag = "Pool";
    public string swimmingPoolTag = "SwimmingPool";
    public string waterSourceTag = "WaterSource";

    private NavMeshAgent agent;
    private float nextDestinationTime;
    private float effectTimer;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        if (agent != null && agent.enabled && agent.isOnNavMesh)
            agent.speed = wanderSpeed;

        SetWanderDestination();
    }

    void Update()
    {
        Wander();
        ApplyAreaEffects();
    }

    void Wander()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        agent.speed = wanderSpeed;
        if (Time.time < nextDestinationTime) return;

        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance)
            SetWanderDestination();
    }

    void SetWanderDestination()
    {
        nextDestinationTime = Time.time + destinationRefreshDelay;

        if (agent == null || !agent.enabled || !agent.isOnNavMesh) return;

        Vector3 randomDirection = Random.insideUnitSphere * wanderRadius + transform.position;
        if (NavMesh.SamplePosition(randomDirection, out NavMeshHit hit, wanderRadius, NavMesh.AllAreas))
        {
            agent.isStopped = false;
            agent.SetDestination(hit.position);
        }
    }

    void ApplyAreaEffects()
    {
        effectTimer -= Time.deltaTime;
        if (effectTimer > 0f) return;

        effectTimer = effectInterval;

        Collider[] hits = Physics.OverlapSphere(transform.position, effectRadius, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            GameObject target = hits[i].gameObject;
            if (IsPool(target)) continue;

            if (IsWaterSource(target))
                DryWaterSource(target);

            GameObject itemRoot = FindTaggedAncestor(target, itemTag);
            if (itemRoot != null)
                IgniteItem(itemRoot);
        }
    }

    void DryWaterSource(GameObject target)
    {
        target.SendMessageUpwards("DryOut", SendMessageOptions.DontRequireReceiver);
        target.SendMessageUpwards("OnDriedByWillOWisp", SendMessageOptions.DontRequireReceiver);
    }

    void IgniteItem(GameObject itemRoot)
    {
        if (fireHazardPrefab != null)
            Instantiate(fireHazardPrefab, itemRoot.transform.position, Quaternion.identity);

        itemRoot.SendMessageUpwards("OnIgnited", SendMessageOptions.DontRequireReceiver);

        if (destroyItemsOnIgnite)
            Destroy(itemRoot);
    }

    GameObject FindTaggedAncestor(GameObject target, string targetTag)
    {
        Transform current = target.transform;
        while (current != null)
        {
            if (current.gameObject.tag == targetTag)
                return current.gameObject;

            current = current.parent;
        }

        return null;
    }

    bool IsWaterSource(GameObject target)
    {
        Transform current = target.transform;
        while (current != null)
        {
            if (current.gameObject.tag == waterSourceTag)
                return true;

            current = current.parent;
        }

        return false;
    }

    bool IsPool(GameObject target)
    {
        Transform current = target.transform;
        while (current != null)
        {
            string currentTag = current.gameObject.tag;
            if (currentTag == poolTag || currentTag == swimmingPoolTag)
                return true;

            current = current.parent;
        }

        return false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.85f);
        Gizmos.DrawWireSphere(transform.position, effectRadius);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, wanderRadius);
    }
}
