using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

[DisallowMultipleComponent]
public class GameRunBootstrap : MonoBehaviour
{
    [Header("Player")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private Transform playerSpawn;
    [SerializeField] private string playerSpawnName = "PlayerSpawn";
    [SerializeField, Min(0f)] private float multiplayerSpawnSpacing = 1.25f;

    [Header("Network")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private UnityTransport unityTransport;
    [SerializeField] private string hostListenAddress = "0.0.0.0";

    [Header("Debug")]
    [SerializeField] private bool logBootstrap = true;

    private int approvedPlayerCount;
    private bool registeredConnectionApprovalCallback;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Start()
    {
        ResolveReferences();

        if (!RegionRunState.HasSelectedRegion || RegionRunState.IsSinglePlayer)
        {
            StartSinglePlayer();
            return;
        }

        StartMultiplayer();
    }

    private void OnDestroy()
    {
        if (registeredConnectionApprovalCallback && networkManager != null)
            networkManager.ConnectionApprovalCallback = null;
    }

    private void StartSinglePlayer()
    {
        if (networkManager != null && networkManager.IsListening)
            networkManager.Shutdown();

        SpawnOfflinePlayer();

        if (logBootstrap)
            Debug.Log("GameRunBootstrap started Game scene as single player.");
    }

    private void StartMultiplayer()
    {
        if (networkManager == null)
        {
            Debug.LogError("GameRunBootstrap cannot start multiplayer because no NetworkManager was found.");
            return;
        }

        ConfigureConnectionApproval();
        ConfigureTransport();

        bool started = RegionRunState.IsHost
            ? networkManager.StartHost()
            : networkManager.StartClient();

        if (!started)
        {
            Debug.LogError($"GameRunBootstrap failed to start {RegionRunState.LaunchMode}.");
            return;
        }

        if (logBootstrap)
            Debug.Log($"GameRunBootstrap started {RegionRunState.LaunchMode}.");
    }

    private void SpawnOfflinePlayer()
    {
        if (playerPrefab == null)
        {
            Debug.LogError("GameRunBootstrap cannot spawn the single player Ben because playerPrefab is missing.");
            return;
        }

        if (FindActiveOfflinePlayer() != null)
            return;

        Transform spawn = GetPlayerSpawn();
        Vector3 spawnPosition = spawn != null ? spawn.position : Vector3.zero;
        Quaternion spawnRotation = spawn != null ? spawn.rotation : Quaternion.identity;

        GameObject player = Instantiate(playerPrefab, spawnPosition, spawnRotation);
        player.name = playerPrefab.name;
    }

    private void ConfigureConnectionApproval()
    {
        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.ConnectionApprovalCallback = ApproveConnection;
        registeredConnectionApprovalCallback = true;
        approvedPlayerCount = 0;
    }

    private void ConfigureTransport()
    {
        if (unityTransport == null)
            unityTransport = networkManager.NetworkConfig.NetworkTransport as UnityTransport;

        if (unityTransport == null)
            return;

        if (RegionRunState.IsHost)
        {
            unityTransport.SetConnectionData(
                RegionRunState.ConnectionAddress,
                RegionRunState.ConnectionPort,
                hostListenAddress);
            return;
        }

        unityTransport.SetConnectionData(
            RegionRunState.ConnectionAddress,
            RegionRunState.ConnectionPort);
    }

    private void ApproveConnection(
        NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        Transform spawn = GetPlayerSpawn();
        Vector3 spawnPosition = spawn != null ? spawn.position : Vector3.zero;
        Quaternion spawnRotation = spawn != null ? spawn.rotation : Quaternion.identity;

        response.Approved = true;
        response.CreatePlayerObject = true;
        response.Position = spawnPosition + GetMultiplayerSpawnOffset(approvedPlayerCount);
        response.Rotation = spawnRotation;

        approvedPlayerCount++;
    }

    private Vector3 GetMultiplayerSpawnOffset(int spawnIndex)
    {
        if (playerSpawn == null || spawnIndex <= 0 || multiplayerSpawnSpacing <= 0f)
            return Vector3.zero;

        int side = spawnIndex % 2 == 0 ? -1 : 1;
        int step = (spawnIndex + 1) / 2;
        return playerSpawn.right * side * step * multiplayerSpawnSpacing;
    }

    private Transform GetPlayerSpawn()
    {
        if (playerSpawn != null)
            return playerSpawn;

        GameObject spawnObject = GameObject.Find(playerSpawnName);
        if (spawnObject != null)
            playerSpawn = spawnObject.transform;

        return playerSpawn;
    }

    private PlayerStatus FindActiveOfflinePlayer()
    {
        PlayerStatus[] activePlayers = FindObjectsByType<PlayerStatus>(
            FindObjectsInactive.Exclude);

        return activePlayers.Length > 0 ? activePlayers[0] : null;
    }

    private void ResolveReferences()
    {
        if (networkManager == null)
            networkManager = GetComponent<NetworkManager>();

        if (networkManager == null)
            networkManager = NetworkManager.Singleton;

        if (unityTransport == null)
            unityTransport = GetComponent<UnityTransport>();

        if (unityTransport == null && networkManager != null)
            unityTransport = networkManager.NetworkConfig.NetworkTransport as UnityTransport;

        GetPlayerSpawn();
    }
}
