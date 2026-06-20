using System.Collections;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
public class OfflinePlayerNetworkCleanup : MonoBehaviour
{
    [SerializeField] private bool removeOfflinePlayersWhenServerStarts = true;
    [SerializeField] private bool removeOfflinePlayersWhenClientStarts = true;

    private NetworkManager networkManager;
    private Coroutine removeOfflinePlayersCoroutine;

    private void Awake()
    {
        networkManager = GetComponent<NetworkManager>();
    }

    private void OnEnable()
    {
        if (networkManager == null)
            networkManager = GetComponent<NetworkManager>();

        if (networkManager != null)
        {
            networkManager.OnServerStarted += HandleServerStarted;
            networkManager.OnClientStarted += HandleClientStarted;
        }
    }

    private void OnDisable()
    {
        if (networkManager != null)
        {
            networkManager.OnServerStarted -= HandleServerStarted;
            networkManager.OnClientStarted -= HandleClientStarted;
        }
    }

    private void Start()
    {
        if (networkManager != null && networkManager.IsServer)
            QueueRemoveOfflinePlayers();
    }

    private void HandleServerStarted()
    {
        if (!removeOfflinePlayersWhenServerStarts) return;

        QueueRemoveOfflinePlayers();
    }

    private void HandleClientStarted()
    {
        if (!removeOfflinePlayersWhenClientStarts) return;

        QueueRemoveOfflinePlayers();
    }

    private void QueueRemoveOfflinePlayers()
    {
        if (removeOfflinePlayersCoroutine != null) return;

        removeOfflinePlayersCoroutine = StartCoroutine(RemoveOfflinePlayersAfterNetworkSpawn());
    }

    private IEnumerator RemoveOfflinePlayersAfterNetworkSpawn()
    {
        yield return null;
        yield return null;

        RemoveOfflinePlayers();
        removeOfflinePlayersCoroutine = null;
    }

    private void RemoveOfflinePlayers()
    {
        NetworkPlayerSetup[] players = FindObjectsByType<NetworkPlayerSetup>(FindObjectsInactive.Include);

        foreach (NetworkPlayerSetup player in players)
        {
            if (player == null) continue;

            NetworkObject networkObject = player.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsSpawned && networkObject.IsPlayerObject)
                continue;

            RemovePlayer(player.gameObject, networkObject);
        }
    }

    private void RemovePlayer(GameObject playerObject, NetworkObject networkObject)
    {
        if (playerObject == null) return;

        if (networkObject != null && networkObject.IsSpawned && networkManager != null && networkManager.IsServer)
        {
            networkObject.Despawn(true);
            return;
        }

        Destroy(playerObject);
    }
}
