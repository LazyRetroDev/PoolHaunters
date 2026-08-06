using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class PhysicalLobbyReadyStation : MonoBehaviour, IPlayerInteractable
{
    [SerializeField] private PhysicalLobbyManager lobbyManager;
    [SerializeField] private TMP_Text label;
    [SerializeField] private string readyText = "READY";
    [SerializeField] private string notReadyText = "NOT READY";

    public void Interact(PlayerInventory inventory)
    {
        PlayerStatus player = inventory != null
            ? inventory.GetComponent<PlayerStatus>()
            : null;

        if (lobbyManager == null)
            lobbyManager = FindFirstObjectByType<PhysicalLobbyManager>();

        if (lobbyManager == null)
        {
            Debug.LogWarning("Ready station has no PhysicalLobbyManager.");
            return;
        }

        lobbyManager.ToggleReady(player);
        RefreshLabel(player);
    }

    void OnValidate()
    {
        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);
    }

    void RefreshLabel(PlayerStatus player)
    {
        if (label == null || lobbyManager == null)
            return;

        label.text = lobbyManager.IsPlayerReady(player) ? readyText : notReadyText;
    }
}
