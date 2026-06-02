using UnityEngine;

[RequireComponent(typeof(Item))]
public class PhotoItem : UsableItem
{
    [Header("Photo Effect")]
    public bool clearPhotographerDecals = true;
    public bool freePetrifiedPlayers = true;
    public float freeRadius = 4f;
    public LayerMask playerMask = ~0;

    public override bool Use(PlayerInventory inventory, PlayerStatus playerStatus)
    {
        bool didSomething = false;

        if (clearPhotographerDecals)
            didSomething |= ClearDecals();

        if (freePetrifiedPlayers)
        {
            Transform origin = playerStatus != null ? playerStatus.transform : transform;
            didSomething |= FreePetrifiedPlayers(origin.position);
        }

        return didSomething;
    }

    bool ClearDecals()
    {
        PhotographerDecal[] decals = FindObjectsOfType<PhotographerDecal>();
        for (int i = 0; i < decals.Length; i++)
        {
            if (decals[i] != null)
                Destroy(decals[i].gameObject);
        }

        return decals.Length > 0;
    }

    bool FreePetrifiedPlayers(Vector3 origin)
    {
        bool freedAny = false;
        Collider[] hits = Physics.OverlapSphere(origin, freeRadius, playerMask, QueryTriggerInteraction.Collide);

        for (int i = 0; i < hits.Length; i++)
        {
            PlayerPetrify petrify = hits[i].GetComponentInParent<PlayerPetrify>();
            if (petrify == null || !petrify.IsPetrified()) continue;

            petrify.Unpetrify();
            freedAny = true;
        }

        return freedAny;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, freeRadius);
    }
}
