using UnityEngine;

[DisallowMultipleComponent]
public class ShopSceneBootstrap : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private bool useNetworkManagerPlayerPrefab = true;
    [SerializeField] private Transform playerSpawn;
    [SerializeField] private string playerSpawnName = "PlayerSpawn";
    [SerializeField] private bool spawnOfflinePlayer = true;

    [Header("Debug")]
    [SerializeField] private bool logBootstrap = true;

    void Start()
    {
        if (!spawnOfflinePlayer)
            return;

        if (IsNetworkSessionRunning())
            return;

        SpawnOfflinePlayerIfNeeded();
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

        Unity.Netcode.NetworkManager networkManager =
            Unity.Netcode.NetworkManager.Singleton;
        if (networkManager == null || networkManager.NetworkConfig == null)
            return null;

        return networkManager.NetworkConfig.PlayerPrefab;
    }

    bool IsNetworkSessionRunning()
    {
        return Unity.Netcode.NetworkManager.Singleton != null &&
            Unity.Netcode.NetworkManager.Singleton.IsListening;
    }
}
