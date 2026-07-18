using UnityEngine;
using Unity.Netcode;

public class Item : NetworkBehaviour
{
    public enum PresentationState : byte
    {
        World,
        HiddenInInventory,
        Carried
    }

    public string itemName = "Item";
    public Sprite itemIcon;

    private readonly NetworkVariable<byte> networkPresentationState =
        new NetworkVariable<byte>(
            (byte)PresentationState.World,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    public PresentationState CurrentPresentationState =>
        (PresentationState)networkPresentationState.Value;

    public override void OnNetworkSpawn()
    {
        networkPresentationState.OnValueChanged += HandlePresentationStateChanged;
        ApplyPresentationState(CurrentPresentationState);
    }

    public override void OnNetworkDespawn()
    {
        networkPresentationState.OnValueChanged -= HandlePresentationStateChanged;
    }

    public void SetPresentationState(PresentationState state)
    {
        if (IsSpawned && IsNetworkSessionRunning())
        {
            if (IsServer)
                networkPresentationState.Value = (byte)state;

            ApplyPresentationState(state);
            return;
        }

        ApplyPresentationState(state);
    }

    private void HandlePresentationStateChanged(byte previousState, byte nextState)
    {
        ApplyPresentationState((PresentationState)nextState);
    }

    private void ApplyPresentationState(PresentationState state)
    {
        bool showRenderers = state != PresentationState.HiddenInInventory;
        bool enableColliders = state == PresentationState.World;
        bool isStoredOrCarried = state != PresentationState.World;

        SetRenderersEnabled(showRenderers);
        SetCollidersEnabled(enableColliders);
        SetRigidbodyStoredOrCarried(isStoredOrCarried);
    }

    private void SetRenderersEnabled(bool enabled)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
                renderers[i].enabled = enabled;
        }
    }

    private void SetCollidersEnabled(bool enabled)
    {
        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = enabled;
        }
    }

    private void SetRigidbodyStoredOrCarried(bool storedOrCarried)
    {
        Rigidbody itemBody = GetComponent<Rigidbody>();
        if (itemBody == null)
            return;

        if (storedOrCarried)
        {
            itemBody.linearVelocity = Vector3.zero;
            itemBody.angularVelocity = Vector3.zero;
            itemBody.useGravity = false;
            itemBody.isKinematic = true;
            return;
        }

        itemBody.isKinematic = false;
        itemBody.useGravity = true;
    }

    private static bool IsNetworkSessionRunning()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening;
    }
}
