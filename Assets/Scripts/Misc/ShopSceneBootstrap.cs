using System.Collections;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class ShopSceneBootstrap : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private bool useNetworkManagerPlayerPrefab = true;
    [SerializeField] private Transform playerSpawn;
    [SerializeField] private string playerSpawnName = "PlayerSpawn";
    [SerializeField, Min(0f)] private float multiplayerSpawnSpacing = 1.25f;
    [SerializeField] private bool spawnOfflinePlayer = true;

    [Header("Debug")]
    [SerializeField] private bool logBootstrap = true;

    void Start()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            if (networkManager.IsServer)
                StartCoroutine(SpawnConnectedPlayersWhenReady(networkManager));

            return;
        }

        if (!spawnOfflinePlayer)
            return;

        SpawnOfflinePlayerIfNeeded();
    }

    IEnumerator SpawnConnectedPlayersWhenReady(NetworkManager networkManager)
    {
        yield return null;
        yield return null;

        SpawnMissingNetworkPlayers(networkManager);
    }

    void SpawnMissingNetworkPlayers(NetworkManager networkManager)
    {
        if (networkManager == null || !networkManager.IsServer)
            return;

        GameObject prefab = GetPlayerPrefab();
        if (prefab == null)
        {
            Debug.LogWarning("ShopSceneBootstrap cannot spawn multiplayer players because no player prefab is assigned.");
            return;
        }

        int spawnIndex = 0;
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                client.PlayerObject != null)
            {
                spawnIndex++;
                continue;
            }

            SpawnNetworkPlayerForClient(clientId, spawnIndex, prefab);
            spawnIndex++;
        }

        if (logBootstrap)
            Debug.Log("ShopSceneBootstrap spawned missing multiplayer players in the shop.");
    }

    void SpawnNetworkPlayerForClient(ulong clientId, int spawnIndex, GameObject prefab)
    {
        Transform spawn = GetPlayerSpawn();
        Vector3 position = spawn != null ? spawn.position : Vector3.zero;
        Quaternion rotation = spawn != null ? spawn.rotation : Quaternion.identity;

        GameObject player = Instantiate(
            prefab,
            position + GetMultiplayerSpawnOffset(spawnIndex),
            rotation);
        player.name = prefab.name;
        ApplyAgentLoadout(player);

        NetworkObject networkObject = player.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogWarning($"ShopSceneBootstrap cannot spawn '{prefab.name}' because it has no NetworkObject.");
            Destroy(player);
            return;
        }

        networkObject.SpawnAsPlayerObject(clientId, true);
    }

    Vector3 GetMultiplayerSpawnOffset(int spawnIndex)
    {
        if (spawnIndex <= 0 || multiplayerSpawnSpacing <= 0f)
            return Vector3.zero;

        int side = spawnIndex % 2 == 0 ? -1 : 1;
        int ring = (spawnIndex + 1) / 2;
        return Vector3.right * side * ring * multiplayerSpawnSpacing;
    }

    void SpawnOfflinePlayerIfNeeded()
    {
        if (FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude).Length > 0)
            return;

        GameObject prefab = GetPlayerPrefab();
        if (prefab == null)
        {
            Debug.LogWarning("ShopSceneBootstrap cannot spawn the player because no player prefab is assigned.");
            return;
        }

        Transform spawn = GetPlayerSpawn();
        Vector3 position = spawn != null ? spawn.position : Vector3.zero;
        Quaternion rotation = spawn != null ? spawn.rotation : Quaternion.identity;

        GameObject player = Instantiate(prefab, position, rotation);
        player.name = prefab.name;
        ApplyAgentLoadout(player);

        if (logBootstrap)
            Debug.Log("ShopSceneBootstrap spawned the offline player in the shop.");
    }

    void ApplyAgentLoadout(GameObject player)
    {
        if (player == null)
            return;

        PlayerAgentLoadout loadout = player.GetComponent<PlayerAgentLoadout>();
        if (loadout == null)
            loadout = player.AddComponent<PlayerAgentLoadout>();

        loadout.ApplySelectedAgent();
    }

    Transform GetPlayerSpawn()
    {
        if (playerSpawn != null)
            return playerSpawn;

        GameObject spawnObject = GameObject.Find(playerSpawnName);
        if (spawnObject != null)
            playerSpawn = spawnObject.transform;

        return playerSpawn;
    }

    GameObject GetPlayerPrefab()
    {
        if (playerPrefab != null)
            return playerPrefab;

        if (!useNetworkManagerPlayerPrefab)
            return null;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || networkManager.NetworkConfig == null)
            return null;

        return networkManager.NetworkConfig.PlayerPrefab;
    }
}
