using UnityEngine;
using UnityEngine.AI;

public class HallucinationPhantom : MonoBehaviour
{
    public Transform target;
    public float speed = 5f;
    public float dissolveDistance = 2.5f; // Dissolves if gets too close to player
    public float maxLifetime = 6f; // Dissolves automatically after X seconds
    public GameObject poofVfx; // Optional smoke/poof effect

    private NavMeshAgent agent;

    private void Start()
    {
        // Strip networking
        var netObj = GetComponent<Unity.Netcode.NetworkObject>();
        if (netObj != null) Destroy(netObj);

        // Disable all other scripts (EnemyTargeting, Attacks, Behaviors) so it can't attack
        MonoBehaviour[] allScripts = GetComponentsInChildren<MonoBehaviour>();
        foreach (var script in allScripts)
        {
            if (script != this && script != null)
            {
                script.enabled = false;
            }
        }

        // Set all colliders to trigger so it doesn't push or damage the player
        Collider[] allColliders = GetComponentsInChildren<Collider>();
        foreach (var col in allColliders)
        {
            col.isTrigger = true;
        }

        agent = GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.speed = speed;
        }
        
        Invoke(nameof(Dissolve), maxLifetime);
    }

    private void Update()
    {
        if (target == null) return;

        // Move towards target
        if (agent != null && agent.isOnNavMesh)
        {
            agent.SetDestination(target.position);
        }
        else
        {
            // Fallback movement if no navmesh or agent is disabled
            Vector3 direction = (target.position - transform.position).normalized;
            transform.position += direction * speed * Time.deltaTime;
            
            // Look at player (flattened to ignore pitch)
            Vector3 lookDir = new Vector3(target.position.x, transform.position.y, target.position.z);
            transform.LookAt(lookDir);
        }

        // Dissolve if player looks directly at it, or it gets too close
        float dist = Vector3.Distance(transform.position, target.position);
        Vector3 dirToPhantom = (transform.position - target.position).normalized;
        float dot = Vector3.Dot(target.forward, dirToPhantom);

        // If very close, or player is looking directly at it (dot > 0.9 = within narrow front cone)
        if (dist <= dissolveDistance)
        {
            Dissolve("Player got too close");
        }
        else if (dot > 0.92f)
        {
            Dissolve("Player looked directly at it");
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // If it actually touches the player before distances catch it
        if (target != null && other.transform.IsChildOf(target))
        {
            Dissolve("Touched the player");
        }
    }

    private void Dissolve(string reason = "Lifetime expired")
    {
        Debug.Log($"[Hallucination] Phantom {gameObject.name} dissolved. Reason: {reason}");
        
        if (poofVfx != null)
        {
            Instantiate(poofVfx, transform.position + Vector3.up, Quaternion.identity);
        }
        Destroy(gameObject);
    }
}
