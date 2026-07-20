using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class PlayerInventory : NetworkBehaviour
{
    enum OwnerLocalUsableEffect
    {
        None,
        StaminaDrainMultiplier,
        WaterUsageMultiplier
    }

    public int inventorySize = 4;
    public float pickupRange = 2.5f;
    public Transform holdPoint;

    [Header("Interaction")]
    public Camera interactionCamera;
    public float networkPickupPositionTolerance = 2f;

    [Header("Throwing")]
    public float throwForce = 8f;
    public float throwUpwardForce = 1.5f;
    public float throwSpawnDistance = 1f;

    private Item[] slots;
    private int selectedSlot = 0;
    private PlayerStatus playerStatus;
    private PlayerPetrify playerPetrify;

    void Awake()
    {
        CacheReferences();
        EnsureSlots();
    }

    void Start()
    {
        CacheReferences();
        EnsureSlots();

        if (interactionCamera == null)
            interactionCamera = Camera.main;
    }

    public void OnInteract(InputValue value)
    {
        if (!CanHandleLocalInput() || !value.isPressed || IsInventoryLocked()) return;

        Camera cameraToUse = GetInteractionCamera();
        if (cameraToUse == null)
        {
            Debug.LogWarning("Cannot interact because no interaction camera was found.");
            return;
        }

        Ray ray = cameraToUse.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (Physics.Raycast(ray, out RaycastHit hit, pickupRange))
        {
            Debug.Log("Hit: " + hit.collider.gameObject.name);
            Item item = hit.collider.GetComponentInParent<Item>();
            if (item != null) TryPickup(item);
        }
        else
        {
            Debug.Log("Raycast hit nothing");
        }
    }

    public void OnUse(InputValue value)
    {
        if (!CanHandleLocalInput() || !value.isPressed || IsInventoryLocked()) return;
        UseSelectedItem();
    }

    public void OnThrow(InputValue value)
    {
        if (!CanHandleLocalInput() || !value.isPressed || IsInventoryLocked()) return;
        ThrowSelectedItem();
    }

    public void OnPrevious(InputValue value)
    {
        if (!CanHandleLocalInput() || !value.isPressed || IsInventoryLocked()) return;
        SelectSlot(0);
    }

    public void OnNext(InputValue value)
    {
        if (!CanHandleLocalInput() || !value.isPressed || IsInventoryLocked()) return;
        SelectSlot(1);
    }

    void Update()
    {
        if (!CanHandleLocalInput() || IsInventoryLocked()) return;
        if (Keyboard.current == null) return;

        for (int i = 0; i < inventorySize; i++)
        {
            if (Keyboard.current[Key.Digit1 + i].wasPressedThisFrame)
                SelectSlot(i);
        }
    }

    void CacheReferences()
    {
        if (playerStatus == null)
            playerStatus = GetComponent<PlayerStatus>();
        if (playerPetrify == null)
            playerPetrify = GetComponent<PlayerPetrify>();
    }

    void EnsureSlots()
    {
        int size = Mathf.Max(1, inventorySize);
        if (slots != null && slots.Length == size)
            return;

        Item[] previousSlots = slots;
        slots = new Item[size];
        if (previousSlots != null)
        {
            int copyCount = Mathf.Min(previousSlots.Length, slots.Length);
            for (int i = 0; i < copyCount; i++)
                slots[i] = previousSlots[i];
        }

        selectedSlot = Mathf.Clamp(selectedSlot, 0, slots.Length - 1);
    }

    bool CanHandleLocalInput()
    {
        return !IsSpawned || IsOwner;
    }

    bool IsNetworkSessionRunning()
    {
        return IsSpawned &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening;
    }

    Camera GetInteractionCamera()
    {
        if (interactionCamera != null && interactionCamera.isActiveAndEnabled)
            return interactionCamera;

        interactionCamera = Camera.main;
        return interactionCamera;
    }

    bool IsInventoryLocked()
    {
        if (playerStatus != null && !playerStatus.CanAct()) return true;
        return playerPetrify != null && playerPetrify.IsPetrified();
    }

    void TryPickup(Item item)
    {
        if (item == null || IsInventoryLocked()) return;

        if (IsNetworkSessionRunning() && !IsServer)
        {
            if (TryGetNetworkObjectReference(item, out NetworkObjectReference itemReference))
                RequestPickupServerRpc(itemReference, transform.position);
            else
                Debug.LogWarning($"Cannot pick up {item.name} because it has no NetworkObject.");

            return;
        }

        TryPickupAuthoritative(item, transform.position, 0f);
    }

    bool TryPickupAuthoritative(
        Item item,
        Vector3 pickupOrigin,
        float extraRange)
    {
        EnsureSlots();
        if (item == null || IsInventoryLocked()) return false;
        if (!CanServerPickUp(item, pickupOrigin, extraRange)) return false;

        WaterItem waterItem = item.GetComponent<WaterItem>();
        if (waterItem != null && waterItem.useImmediatelyOnPickup)
        {
            if (!waterItem.TryApply(playerStatus))
            {
                Debug.Log($"Could not use {item.itemName}; the player's water may already be full.");
                return false;
            }

            Debug.Log($"Used {item.itemName} on pickup. Water: {playerStatus.GetCurrentWater():0}/{playerStatus.maxWater:0} ({playerStatus.GetWaterQuality()})");

            if (waterItem.destroyAfterUse)
                DestroyOrDespawnItem(item);
            else
                HideItemForInventory(item);

            return true;
        }

        int emptySlot = GetFirstEmptySlot();
        if (emptySlot < 0)
        {
            Debug.Log("Inventory full!");
            return false;
        }

        slots[emptySlot] = item;
        HideItemForInventory(item);
        SendSetSlotToOwner(emptySlot, item);
        Debug.Log($"Picked up {item.itemName} into slot {emptySlot + 1}");
        return true;
    }

    bool CanServerPickUp(
        Item item,
        Vector3 pickupOrigin,
        float extraRange)
    {
        if (item == null || IsItemStoredInAnyInventory(item))
            return false;

        if (!IsFiniteVector3(pickupOrigin))
            pickupOrigin = transform.position;

        float maxDistance = pickupRange + 1f + Mathf.Max(0f, extraRange);
        float sqrDistance = (item.transform.position - pickupOrigin).sqrMagnitude;
        return sqrDistance <= maxDistance * maxDistance;
    }

    int GetFirstEmptySlot()
    {
        EnsureSlots();
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == null)
                return i;
        }

        return -1;
    }

    bool IsItemStoredInAnyInventory(Item item)
    {
        if (item == null) return false;

        PlayerInventory[] inventories = FindObjectsByType<PlayerInventory>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        for (int inventoryIndex = 0; inventoryIndex < inventories.Length; inventoryIndex++)
        {
            PlayerInventory inventory = inventories[inventoryIndex];
            if (inventory == null)
                continue;

            Item[] inventorySlots = inventory.GetSlots();
            if (inventorySlots == null)
                continue;

            for (int slotIndex = 0; slotIndex < inventorySlots.Length; slotIndex++)
            {
                if (inventorySlots[slotIndex] == item)
                    return true;
            }
        }

        return false;
    }

    void UseSelectedItem()
    {
        if (IsInventoryLocked()) return;
        EnsureSlots();
        if (selectedSlot < 0 || selectedSlot >= slots.Length) return;

        if (IsNetworkSessionRunning() && !IsServer)
        {
            RequestUseSelectedItemServerRpc(selectedSlot);
            return;
        }

        UseSelectedItemAuthoritative(selectedSlot);
    }

    bool UseSelectedItemAuthoritative(int slotIndex)
    {
        EnsureSlots();
        if (IsInventoryLocked()) return false;
        if (slotIndex < 0 || slotIndex >= slots.Length) return false;

        Item item = slots[slotIndex];
        if (item == null)
        {
            Debug.Log("No item selected.");
            return false;
        }

        UsableItem usableItem = item.GetComponent<UsableItem>();
        if (usableItem == null)
        {
            Debug.Log($"{item.itemName} cannot be used yet.");
            return false;
        }

        bool used = usableItem.Use(this, playerStatus);
        if (!used)
        {
            Debug.Log($"{item.itemName} had no effect.");
            return false;
        }

        SendOwnerLocalUsableEffect(item);
        Debug.Log($"Used {item.itemName}.");

        if (usableItem.consumeOnUse)
            RemoveItemAtSlot(slotIndex, destroyItem: true);

        return true;
    }

    void SendOwnerLocalUsableEffect(Item item)
    {
        if (!IsNetworkSessionRunning() || IsOwner)
            return;

        if (!TryGetOwnerLocalUsableEffect(
            item,
            out OwnerLocalUsableEffect effect,
            out float value,
            out float duration))
        {
            return;
        }

        ApplyOwnerLocalUsableEffectClientRpc(
            (int)effect,
            value,
            duration,
            OwnerClientRpcParams());
    }

    bool TryGetOwnerLocalUsableEffect(
        Item item,
        out OwnerLocalUsableEffect effect,
        out float value,
        out float duration)
    {
        effect = OwnerLocalUsableEffect.None;
        value = 0f;
        duration = 0f;

        if (item == null)
            return false;

        StaminaDrainPowerupItem staminaPowerup =
            item.GetComponent<StaminaDrainPowerupItem>();
        if (staminaPowerup != null)
        {
            effect = OwnerLocalUsableEffect.StaminaDrainMultiplier;
            value = staminaPowerup.staminaDrainMultiplier;
            duration = staminaPowerup.duration;
            return true;
        }

        WaterUsagePowerupItem waterPowerup =
            item.GetComponent<WaterUsagePowerupItem>();
        if (waterPowerup != null)
        {
            effect = OwnerLocalUsableEffect.WaterUsageMultiplier;
            value = waterPowerup.waterUsageMultiplier;
            duration = waterPowerup.duration;
            return true;
        }

        return false;
    }

    void ThrowSelectedItem()
    {
        if (IsInventoryLocked()) return;
        EnsureSlots();
        if (selectedSlot < 0 || selectedSlot >= slots.Length)
            return;

        Item item = slots[selectedSlot];
        if (item == null)
        {
            Debug.Log("No item selected to throw.");
            return;
        }

        GetThrowPose(out Vector3 spawnPosition, out Quaternion spawnRotation, out Vector3 impulse);

        if (IsNetworkSessionRunning() && !IsServer)
        {
            RequestThrowSelectedItemServerRpc(
                selectedSlot,
                spawnPosition,
                spawnRotation,
                impulse);
            return;
        }

        ThrowSelectedItemAuthoritative(
            selectedSlot,
            spawnPosition,
            spawnRotation,
            impulse);
    }

    bool ThrowSelectedItemAuthoritative(
        int slotIndex,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        Vector3 impulse)
    {
        EnsureSlots();
        if (slotIndex < 0 || slotIndex >= slots.Length)
            return false;

        Item item = slots[slotIndex];
        if (item == null)
        {
            Debug.Log("No item selected to throw.");
            return false;
        }

        slots[slotIndex] = null;
        SendClearSlotToOwner(slotIndex);
        ShowThrownItem(item, spawnPosition, spawnRotation, impulse);
        Debug.Log($"Threw {item.itemName} from slot {slotIndex + 1}.");
        return true;
    }

    void GetThrowPose(
        out Vector3 spawnPosition,
        out Quaternion spawnRotation,
        out Vector3 impulse)
    {
        Camera cameraToUse = GetInteractionCamera();
        Transform throwOrigin = holdPoint != null
            ? holdPoint
            : cameraToUse != null ? cameraToUse.transform : transform;
        Vector3 throwDirection = cameraToUse != null
            ? cameraToUse.transform.forward
            : transform.forward;

        spawnPosition = throwOrigin.position +
            throwDirection.normalized * throwSpawnDistance;
        spawnRotation = Quaternion.LookRotation(throwDirection.normalized, Vector3.up);
        impulse = throwDirection.normalized * throwForce +
            Vector3.up * throwUpwardForce;
    }

    public bool RemoveItem(Item item, bool destroyItem)
    {
        if (item == null)
            return false;

        EnsureSlots();
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] != item) continue;
            return RemoveItemAtSlot(i, destroyItem);
        }

        return false;
    }

    bool RemoveItemAtSlot(int slotIndex, bool destroyItem)
    {
        EnsureSlots();
        if (slotIndex < 0 || slotIndex >= slots.Length)
            return false;

        Item item = slots[slotIndex];
        if (item == null)
            return false;

        slots[slotIndex] = null;
        SendClearSlotToOwner(slotIndex);

        if (destroyItem)
            DestroyOrDespawnItem(item);

        return true;
    }

    void SelectSlot(int index)
    {
        if (IsInventoryLocked()) return;
        EnsureSlots();

        selectedSlot = Mathf.Clamp(index, 0, slots.Length - 1);

        if (IsNetworkSessionRunning() && !IsServer)
            SelectSlotServerRpc(selectedSlot);

        Debug.Log($"Selected slot {selectedSlot + 1}");
    }

    void HideItemForInventory(Item item)
    {
        if (item == null)
            return;

        ApplyHiddenItemPresentation(item);

        if (IsNetworkSessionRunning() && TryGetNetworkObjectReference(
            item,
            out NetworkObjectReference itemReference))
        {
            HideItemClientRpc(itemReference);
        }
    }

    void ShowThrownItem(
        Item item,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        Vector3 impulse)
    {
        if (item == null)
            return;

        ApplyThrownItemPresentation(item, spawnPosition, spawnRotation, impulse);

        if (IsNetworkSessionRunning() && TryGetNetworkObjectReference(
            item,
            out NetworkObjectReference itemReference))
        {
            ShowThrownItemClientRpc(
                itemReference,
                spawnPosition,
                spawnRotation,
                impulse);
        }
    }

    void DestroyOrDespawnItem(Item item)
    {
        if (item == null)
            return;

        NetworkObject networkObject = item.GetComponentInParent<NetworkObject>();
        if (IsNetworkSessionRunning() &&
            networkObject != null &&
            networkObject.IsSpawned)
        {
            if (IsServer)
                networkObject.Despawn(true);

            return;
        }

        Destroy(item.gameObject);
    }

    void ApplyHiddenItemPresentation(Item item)
    {
        if (item == null)
            return;

        item.SetPresentationState(Item.PresentationState.HiddenInInventory);
        item.transform.SetParent(null, true);

        Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = false;
        }

        Collider[] colliders = item.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }

        Rigidbody itemBody = item.GetComponent<Rigidbody>();
        if (itemBody != null)
        {
            itemBody.linearVelocity = Vector3.zero;
            itemBody.angularVelocity = Vector3.zero;
            itemBody.useGravity = false;
            itemBody.isKinematic = true;
        }
    }

    void ApplyThrownItemPresentation(
        Item item,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        Vector3 impulse)
    {
        if (item == null)
            return;

        item.SetPresentationState(Item.PresentationState.World);
        item.transform.SetParent(null, true);
        item.transform.SetPositionAndRotation(spawnPosition, spawnRotation);

        Renderer[] renderers = item.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = true;
        }

        Collider[] colliders = item.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = true;
        }

        Rigidbody itemBody = item.GetComponent<Rigidbody>();
        if (itemBody == null)
            itemBody = item.gameObject.AddComponent<Rigidbody>();

        itemBody.isKinematic = false;
        itemBody.useGravity = true;
        itemBody.linearVelocity = Vector3.zero;
        itemBody.angularVelocity = Vector3.zero;
        itemBody.AddForce(impulse, ForceMode.VelocityChange);
    }

    bool TryGetNetworkObjectReference(
        Item item,
        out NetworkObjectReference itemReference)
    {
        NetworkObject networkObject = item != null
            ? item.GetComponentInParent<NetworkObject>()
            : null;

        if (networkObject == null)
        {
            itemReference = default;
            return false;
        }

        itemReference = networkObject;
        return true;
    }

    bool TryResolveItem(
        NetworkObjectReference itemReference,
        out Item item)
    {
        item = null;

        NetworkObject networkObject;
        if (!itemReference.TryGet(out networkObject) || networkObject == null)
            return false;

        item = networkObject.GetComponentInChildren<Item>(true);
        return item != null;
    }

    void SendSetSlotToOwner(int slotIndex, Item item)
    {
        if (!IsNetworkSessionRunning() || IsOwner)
            return;

        if (!TryGetNetworkObjectReference(item, out NetworkObjectReference itemReference))
            return;

        SetSlotClientRpc(slotIndex, itemReference, OwnerClientRpcParams());
    }

    void SendClearSlotToOwner(int slotIndex)
    {
        if (!IsNetworkSessionRunning() || IsOwner)
            return;

        ClearSlotClientRpc(slotIndex, OwnerClientRpcParams());
    }

    ClientRpcParams OwnerClientRpcParams()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };
    }

    bool CanProcessClientInventoryRequest(ServerRpcParams serverRpcParams)
    {
        if (!IsNetworkSessionRunning())
            return true;

        ulong senderClientId = serverRpcParams.Receive.SenderClientId;
        if (senderClientId == OwnerClientId)
            return true;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null &&
            networkManager.ConnectedClients.TryGetValue(
                senderClientId,
                out NetworkClient senderClient) &&
            senderClient.PlayerObject == NetworkObject)
        {
            return true;
        }

        Debug.LogWarning(
            $"Ignored inventory request from client {senderClientId} for player owned by {OwnerClientId}.");
        return false;
    }

    static bool IsFiniteVector3(Vector3 value)
    {
        return IsFiniteFloat(value.x) &&
            IsFiniteFloat(value.y) &&
            IsFiniteFloat(value.z);
    }

    static bool IsFiniteFloat(float value)
    {
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestPickupServerRpc(
        NetworkObjectReference itemReference,
        Vector3 requesterPosition,
        ServerRpcParams serverRpcParams = default)
    {
        if (!CanProcessClientInventoryRequest(serverRpcParams))
            return;
        if (!TryResolveItem(itemReference, out Item item))
            return;

        TryPickupAuthoritative(
            item,
            requesterPosition,
            networkPickupPositionTolerance);
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestUseSelectedItemServerRpc(
        int slotIndex,
        ServerRpcParams serverRpcParams = default)
    {
        if (!CanProcessClientInventoryRequest(serverRpcParams))
            return;

        selectedSlot = Mathf.Clamp(slotIndex, 0, Mathf.Max(0, inventorySize - 1));
        UseSelectedItemAuthoritative(selectedSlot);
    }

    [ServerRpc(RequireOwnership = false)]
    void RequestThrowSelectedItemServerRpc(
        int slotIndex,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        Vector3 impulse,
        ServerRpcParams serverRpcParams = default)
    {
        if (!CanProcessClientInventoryRequest(serverRpcParams))
            return;

        selectedSlot = Mathf.Clamp(slotIndex, 0, Mathf.Max(0, inventorySize - 1));
        ThrowSelectedItemAuthoritative(
            selectedSlot,
            spawnPosition,
            spawnRotation,
            impulse);
    }

    [ServerRpc(RequireOwnership = false)]
    void SelectSlotServerRpc(
        int slotIndex,
        ServerRpcParams serverRpcParams = default)
    {
        if (!CanProcessClientInventoryRequest(serverRpcParams))
            return;

        EnsureSlots();
        selectedSlot = Mathf.Clamp(slotIndex, 0, slots.Length - 1);
    }

    [ClientRpc]
    void SetSlotClientRpc(
        int slotIndex,
        NetworkObjectReference itemReference,
        ClientRpcParams clientRpcParams = default)
    {
        if (IsServer)
            return;
        if (!TryResolveItem(itemReference, out Item item))
            return;

        EnsureSlots();
        if (slotIndex >= 0 && slotIndex < slots.Length)
            slots[slotIndex] = item;
    }

    [ClientRpc]
    void ClearSlotClientRpc(
        int slotIndex,
        ClientRpcParams clientRpcParams = default)
    {
        if (IsServer)
            return;

        EnsureSlots();
        if (slotIndex >= 0 && slotIndex < slots.Length)
            slots[slotIndex] = null;
    }

    [ClientRpc]
    void HideItemClientRpc(NetworkObjectReference itemReference)
    {
        if (IsServer)
            return;
        if (TryResolveItem(itemReference, out Item item))
            ApplyHiddenItemPresentation(item);
    }

    [ClientRpc]
    void ShowThrownItemClientRpc(
        NetworkObjectReference itemReference,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        Vector3 impulse)
    {
        if (IsServer)
            return;
        if (TryResolveItem(itemReference, out Item item))
            ApplyThrownItemPresentation(
                item,
                spawnPosition,
                spawnRotation,
                impulse);
    }

    [ClientRpc]
    void ApplyOwnerLocalUsableEffectClientRpc(
        int effect,
        float value,
        float duration,
        ClientRpcParams clientRpcParams = default)
    {
        switch ((OwnerLocalUsableEffect)effect)
        {
            case OwnerLocalUsableEffect.StaminaDrainMultiplier:
                PlayerMovement movement = GetComponent<PlayerMovement>();
                if (movement != null)
                    movement.ApplyStaminaDrainMultiplier(value, duration);
                break;
            case OwnerLocalUsableEffect.WaterUsageMultiplier:
                WaterCannon waterCannon = GetComponentInChildren<WaterCannon>();
                if (waterCannon != null)
                    waterCannon.ApplyWaterUsageMultiplier(value, duration);
                break;
        }
    }

    public Item[] GetSlots()
    {
        EnsureSlots();
        return slots;
    }

    public int GetSelectedSlot() => selectedSlot;
}
