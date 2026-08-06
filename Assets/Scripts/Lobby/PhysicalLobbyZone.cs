using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public class PhysicalLobbyZone : MonoBehaviour
{
    [SerializeField] private PhysicalLobbyManager lobbyManager;

    void Reset()
    {
        Collider trigger = GetComponent<Collider>();
        if (trigger != null)
            trigger.isTrigger = true;
    }

    void Awake()
    {
        if (lobbyManager == null)
            lobbyManager = FindFirstObjectByType<PhysicalLobbyManager>();
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerStatus player = other.GetComponentInParent<PlayerStatus>();
        if (player == null)
            return;

        if (lobbyManager == null)
            lobbyManager = FindFirstObjectByType<PhysicalLobbyManager>();

        if (lobbyManager != null)
            lobbyManager.RegisterPlayerInLobby(player);
    }

    void OnTriggerExit(Collider other)
    {
        PlayerStatus player = other.GetComponentInParent<PlayerStatus>();
        if (player == null || lobbyManager == null)
            return;

        lobbyManager.UnregisterPlayerInLobby(player);
    }
}
