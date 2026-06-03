using UnityEngine;

[RequireComponent(typeof(Item))]
public class PhotoItem : UsableItem
{
    [Header("Photo Effect")]
    public bool clearPhotographerDecals = true;
    public bool freePetrifiedPlayers = true;
    public float freeRadius = 4f;
    public LayerMask playerMask = ~0;

    private PlayerPetrify capturedPlayer;
    private PhotographerDecal linkedDecal;
    private Item item;

    void Awake()
    {
        item = GetComponent<Item>();
    }

    public void SetCapturedPlayer(PlayerPetrify playerPetrify)
    {
        capturedPlayer = playerPetrify;
    }

    public void SetLinkedDecal(PhotographerDecal decal)
    {
        linkedDecal = decal;
        if (linkedDecal != null)
            linkedDecal.LinkPhoto(this);
    }

    public void InvalidateFromLinkedDecal()
    {
        RemoveFromInventories();
        Destroy(gameObject);
    }

    public override bool Use(PlayerInventory inventory, PlayerStatus playerStatus)
    {
        bool didSomething = false;

        if (clearPhotographerDecals)
            didSomething |= ClearLinkedDecal();

        if (freePetrifiedPlayers)
        {
            if (capturedPlayer != null && capturedPlayer.IsPetrified())
            {
                capturedPlayer.Unpetrify();
                didSomething = true;
            }
            else
            {
                Transform origin = playerStatus != null ? playerStatus.transform : transform;
                didSomething |= FreePetrifiedPlayers(origin.position);
            }
        }

        return didSomething;
    }

    bool ClearLinkedDecal()
    {
        if (linkedDecal == null) return false;

        linkedDecal.ClearFromPhoto();
        linkedDecal = null;
        return true;
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

    void RemoveFromInventories()
    {
        if (item == null)
            item = GetComponent<Item>();

        PlayerInventory[] inventories = FindObjectsOfType<PlayerInventory>();
        for (int i = 0; i < inventories.Length; i++)
            inventories[i].RemoveItem(item, destroyItem: false);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, freeRadius);
    }
}
