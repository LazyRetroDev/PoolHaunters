using Unity.Netcode;
using UnityEngine;
using System;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class PoolCleanBoxItemConsumer : MonoBehaviour
{
    [SerializeField] private string acceptedItemTag = "Item";

    private readonly HashSet<Item> consumedItems = new HashSet<Item>();

    public event Action<Item> OnItemConsumed;

    private void OnTriggerEnter(Collider other)
    {
        TryConsumeItem(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryConsumeItem(other);
    }

    private void TryConsumeItem(Collider other)
    {
        Item item = FindConsumableItem(other);
        if (item == null)
            return;
        if (!consumedItems.Add(item))
            return;

        OnItemConsumed?.Invoke(item);
        ConsumeItem(item);
    }

    private Item FindConsumableItem(Collider other)
    {
        if (other == null)
            return null;

        Item item = other.GetComponentInParent<Item>();
        if (item == null)
            item = other.GetComponentInChildren<Item>(true);
        if (item == null || item.CurrentPresentationState != Item.PresentationState.World)
            return null;

        GameObject itemObject = item.gameObject;
        if (!itemObject.CompareTag(acceptedItemTag) &&
            !other.CompareTag(acceptedItemTag))
        {
            return null;
        }

        return item;
    }

    private void ConsumeItem(Item item)
    {
        NetworkObject networkObject = item.GetComponentInParent<NetworkObject>();
        if (IsNetworkSessionRunning() &&
            networkObject != null &&
            networkObject.IsSpawned)
        {
            if (NetworkManager.Singleton.IsServer)
                networkObject.Despawn(true);

            return;
        }

        Destroy(item.gameObject);
    }

    private static bool IsNetworkSessionRunning()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening;
    }
}
