using UnityEngine;

public class WaterContaminationZone : MonoBehaviour
{
    public float radius = 2.5f;
    public WaterQuality contaminationQuality = WaterQuality.Contaminated;
    public float contaminateInterval = 0.5f;
    public float lifetime = 0f;
    public LayerMask playerMask = ~0;

    private float contaminateTimer;
    private float lifeTimer;

    void OnEnable()
    {
        contaminateTimer = 0f;
        lifeTimer = lifetime;
    }

    void Update()
    {
        if (lifetime > 0f)
        {
            lifeTimer -= Time.deltaTime;
            if (lifeTimer <= 0f)
            {
                Destroy(gameObject);
                return;
            }
        }

        contaminateTimer -= Time.deltaTime;
        if (contaminateTimer > 0f) return;

        contaminateTimer = contaminateInterval;
        ContaminatePlayersInRadius();
    }

    void ContaminatePlayersInRadius()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, radius, playerMask, QueryTriggerInteraction.Collide);
        for (int i = 0; i < hits.Length; i++)
        {
            PlayerStatus playerStatus = hits[i].GetComponentInParent<PlayerStatus>();
            if (playerStatus == null) continue;

            if (contaminationQuality == WaterQuality.Contaminated)
                playerStatus.ContaminateWater();
            else if (contaminationQuality == WaterQuality.Clean)
                playerStatus.PurifyWater();
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.35f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, radius);
    }
}
