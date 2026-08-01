using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Item))]
public class PhotoItem : UsableItem
{
    [Header("Photo Effect")]
    public bool clearPhotographerDecals = true;
    public bool freePetrifiedPlayers = true;
    public float freeRadius = 4f;
    public LayerMask playerMask = ~0;

    private PlayerPetrify capturedPlayer;
    private readonly List<PhotographerDecal> linkedDecals = new List<PhotographerDecal>();
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
        AddLinkedDecal(decal);
    }

    public void AddLinkedDecal(PhotographerDecal decal)
    {
        if (decal == null || linkedDecals.Contains(decal)) return;

        linkedDecals.Add(decal);
        decal.LinkPhoto(this);
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
            didSomething |= ClearLinkedDecals();

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

    bool ClearLinkedDecals()
    {
        bool clearedAny = false;

        for (int i = linkedDecals.Count - 1; i >= 0; i--)
        {
            PhotographerDecal decal = linkedDecals[i];
            if (decal == null)
            {
                linkedDecals.RemoveAt(i);
                continue;
            }

            decal.ClearFromPhoto();
            linkedDecals.RemoveAt(i);
            clearedAny = true;
        }

        return clearedAny;
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

        PlayerInventory[] inventories =
            FindObjectsByType<PlayerInventory>(FindObjectsInactive.Exclude);
        for (int i = 0; i < inventories.Length; i++)
            inventories[i].RemoveItem(item, destroyItem: false);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, freeRadius);
    }
}
