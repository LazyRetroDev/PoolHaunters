using System.Collections.Generic;
using UnityEngine;

public class FireHazard : MonoBehaviour
{
    [Header("Lifetime")]
    public float lifetime = 8f;
    public bool destroyWhenLifetimeEnds = true;

    [Header("Damage")]
    public float radius = 2f;
    public float damagePerSecond = 12f;
    public float damageTickInterval = 0.5f;
    public LayerMask damageMask = ~0;

    [Header("Item Ignition")]
    public bool canIgniteItems = true;
    public bool destroyItemsOnIgnite = false;
    public float itemIgniteRadius = 1.75f;
    public string itemTag = "Item";
    public string poolTag = "Pool";
    public string swimmingPoolTag = "SwimmingPool";

    [Header("Water Source Drying")]
    public bool canDryWaterSources = true;
    public float waterSourceDryRadius = 2f;
    public string waterSourceTag = "WaterSource";

    private readonly Dictionary<PlayerStatus, float> nextDamageTimes = new Dictionary<PlayerStatus, float>();
    private readonly HashSet<GameObject> driedWaterSources = new HashSet<GameObject>();
    private float lifetimeTimer;

    void Start()
    {
        lifetimeTimer = lifetime;
    }

    void Update()
    {
        if (!EnemyAuthority.CanRunGameplay())
            return;

        DamagePlayersInRadius();
        TryIgniteItems();
        TryDryWaterSources();
        UpdateLifetime();
    }

    void DamagePlayersInRadius()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, radius, damageMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            PlayerStatus player = hits[i].GetComponentInParent<PlayerStatus>();
            if (player == null) continue;

            if (nextDamageTimes.TryGetValue(player, out float nextTime) && Time.time < nextTime)
                continue;

            player.TakeDamage(damagePerSecond * damageTickInterval);
            nextDamageTimes[player] = Time.time + damageTickInterval;
        }
    }

    void TryIgniteItems()
    {
        if (!canIgniteItems) return;

        Collider[] hits = Physics.OverlapSphere(transform.position, itemIgniteRadius, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            GameObject target = hits[i].gameObject;
            if (IsPool(target)) continue;

            GameObject itemRoot = FindTaggedAncestor(target, itemTag);
            if (itemRoot == null) continue;

            itemRoot.SendMessageUpwards("OnIgnited", SendMessageOptions.DontRequireReceiver);

            if (destroyItemsOnIgnite)
                Destroy(itemRoot);
        }
    }

    void TryDryWaterSources()
    {
        if (!canDryWaterSources) return;

        Collider[] hits = Physics.OverlapSphere(transform.position, waterSourceDryRadius, ~0, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            GameObject waterSourceRoot = FindTaggedAncestor(hits[i].gameObject, waterSourceTag);
            if (waterSourceRoot == null || IsPool(waterSourceRoot)) continue;
            if (driedWaterSources.Contains(waterSourceRoot)) continue;

            driedWaterSources.Add(waterSourceRoot);
            waterSourceRoot.SendMessage("DryOut", SendMessageOptions.DontRequireReceiver);
            waterSourceRoot.SendMessage("OnDriedByFire", SendMessageOptions.DontRequireReceiver);
        }
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

    void UpdateLifetime()
    {
        if (lifetime <= 0f) return;

        lifetimeTimer -= Time.deltaTime;
        if (lifetimeTimer <= 0f && destroyWhenLifetimeEnds)
            Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, radius);
        Gizmos.color = new Color(1f, 0.6f, 0f, 0.75f);
        Gizmos.DrawWireSphere(transform.position, itemIgniteRadius);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, waterSourceDryRadius);
    }
}
