using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class PhysicalLobbyStartStation : MonoBehaviour, IPlayerInteractable
{
    [SerializeField] private PhysicalLobbyManager lobbyManager;
    [SerializeField] private TMP_Text label;
    [SerializeField] private string labelText = "START RUN";

    void Awake()
    {
        RefreshLabel();
    }

    public void Interact(PlayerInventory inventory)
    {
        PlayerStatus player = inventory != null
            ? inventory.GetComponent<PlayerStatus>()
            : null;

        if (lobbyManager == null)
            lobbyManager = FindFirstObjectByType<PhysicalLobbyManager>();

        if (lobbyManager == null)
        {
            Debug.LogWarning("Start station has no PhysicalLobbyManager.");
            return;
        }

        lobbyManager.TryStartRun(player);
    }

    void OnValidate()
    {
        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);

        RefreshLabel();
    }

    void RefreshLabel()
    {
        if (label != null)
            label.text = labelText;
    }
}
