using System;
using System.Collections;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class GameRunBootstrap : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField] private string gameSceneName = "Game";

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
    [SerializeField] private bool showRelayJoinCodeOverlay = true;

    private int approvedPlayerCount;
    private bool registeredConnectionApprovalCallback;
    private bool multiplayerStartInProgress;
    private bool bootstrapStartedInGameScene;
    private GUIStyle relayJoinCodeStyle;

    private void Awake()
    {
        ResolveReferences();
    }

    private void Start()
    {
        ResolveReferences();

        if (!ShouldRunInActiveScene())
            return;

        bootstrapStartedInGameScene = true;

        if (!RegionRunState.HasSelectedRegion || RegionRunState.IsSinglePlayer)
        {
            StartSinglePlayer();
            return;
        }

        if (networkManager != null && networkManager.IsListening)
        {
            ContinueExistingMultiplayerSession();
            return;
        }

        StartMultiplayer();
    }

    private void OnDestroy()
    {
        if (registeredConnectionApprovalCallback && networkManager != null)
            networkManager.ConnectionApprovalCallback = null;
    }

    private void OnGUI()
    {
        if (!bootstrapStartedInGameScene)
            return;

        if (!showRelayJoinCodeOverlay ||
            !RegionRunState.UsesRelay ||
            !RegionRunState.IsHost ||
            string.IsNullOrWhiteSpace(RegionRunState.RelayJoinCode))
        {
            return;
        }

        if (relayJoinCodeStyle == null)
        {
            relayJoinCodeStyle = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 22,
                wordWrap = false
            };
            relayJoinCodeStyle.normal.textColor = Color.white;
        }

        GUI.Box(
            new Rect(16f, 16f, 360f, 64f),
            $"Relay Join Code: {RegionRunState.RelayJoinCode}",
            relayJoinCodeStyle);
    }

    private bool ShouldRunInActiveScene()
    {
        string expectedSceneName = RegionRunState.HasSelectedRegion &&
            !string.IsNullOrWhiteSpace(RegionRunState.SceneName)
                ? RegionRunState.SceneName
                : gameSceneName;

        if (string.IsNullOrWhiteSpace(expectedSceneName))
            return true;

        return SceneManager.GetActiveScene().name == expectedSceneName;
    }

    private void StartSinglePlayer()
    {
        if (networkManager != null && networkManager.IsListening)
            networkManager.Shutdown();

        SpawnOfflinePlayer();

        if (logBootstrap)
            Debug.Log("GameRunBootstrap started Game scene as single player.");
    }

    private async void StartMultiplayer()
    {
        if (multiplayerStartInProgress)
            return;

        multiplayerStartInProgress = true;

        try
        {
            if (networkManager == null)
            {
                Debug.LogError("GameRunBootstrap cannot start multiplayer because no NetworkManager was found.");
                return;
            }

            ConfigurePlayerPrefab();
            ConfigureConnectionApproval();

            bool transportConfigured = RegionRunState.UsesRelay
                ? await ConfigureRelayTransportAsync()
                : ConfigureDirectTransport();

            if (!transportConfigured)
                return;

            bool started = RegionRunState.IsHost
                ? networkManager.StartHost()
                : networkManager.StartClient();

            if (!started)
            {
                Debug.LogError($"GameRunBootstrap failed to start {RegionRunState.LaunchMode} using {RegionRunState.NetworkMode}.");
                return;
            }

            if (logBootstrap)
            {
                string message = $"GameRunBootstrap started {RegionRunState.LaunchMode} using {RegionRunState.NetworkMode}.";
                if (RegionRunState.UsesRelay && RegionRunState.IsHost)
                    message += $" Relay join code: {RegionRunState.RelayJoinCode}";

                Debug.Log(message);
            }
        }
        catch (Exception exception)
        {
            Debug.LogError($"GameRunBootstrap failed to start multiplayer: {exception}");
        }
        finally
        {
            multiplayerStartInProgress = false;
        }
    }

    private void ContinueExistingMultiplayerSession()
    {
        ConfigurePlayerPrefab();
        ConfigureConnectionApproval();

        if (networkManager != null && networkManager.IsServer)
            StartCoroutine(SpawnConnectedPlayersWhenReady());

        if (logBootstrap)
            Debug.Log("GameRunBootstrap continued an existing multiplayer lobby session in Game scene.");
    }

    private IEnumerator SpawnConnectedPlayersWhenReady()
    {
        yield return null;
        yield return null;

        SpawnMissingNetworkPlayers();
    }

    private void SpawnMissingNetworkPlayers()
    {
        if (networkManager == null || !networkManager.IsServer)
            return;

        GameObject prefab = playerPrefab != null
            ? playerPrefab
            : networkManager.NetworkConfig.PlayerPrefab;

        if (prefab == null)
        {
            Debug.LogError("GameRunBootstrap cannot spawn lobby players because playerPrefab is missing.");
            return;
        }

        approvedPlayerCount = 0;

        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (networkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                client.PlayerObject != null)
            {
                approvedPlayerCount++;
                continue;
            }

            SpawnNetworkPlayerForClient(clientId, approvedPlayerCount, prefab);
            approvedPlayerCount++;
        }
    }

    private void SpawnNetworkPlayerForClient(ulong clientId, int spawnIndex, GameObject prefab)
    {
        Transform spawn = GetPlayerSpawn();
        Vector3 spawnPosition = spawn != null ? spawn.position : Vector3.zero;
        Quaternion spawnRotation = spawn != null ? spawn.rotation : Quaternion.identity;

        GameObject player = Instantiate(
            prefab,
            spawnPosition + GetMultiplayerSpawnOffset(spawnIndex),
            spawnRotation);
        ApplyAgentLoadout(player);

        NetworkObject networkObject = player.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogError($"GameRunBootstrap cannot spawn '{prefab.name}' as a player because it has no NetworkObject.");
            Destroy(player);
            return;
        }

        networkObject.SpawnAsPlayerObject(clientId, true);
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
        ApplyAgentLoadout(player);
    }

    private void ApplyAgentLoadout(GameObject player)
    {
        if (player == null)
            return;

        PlayerAgentLoadout loadout = player.GetComponent<PlayerAgentLoadout>();
        if (loadout == null)
            loadout = player.AddComponent<PlayerAgentLoadout>();

        loadout.ApplySelectedAgent();
    }

    private void ConfigureConnectionApproval()
    {
        ConfigurePlayerPrefab();
        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.ConnectionApprovalCallback = ApproveConnection;
        registeredConnectionApprovalCallback = true;
        approvedPlayerCount = 0;
    }

    private void ConfigurePlayerPrefab()
    {
        if (networkManager == null || playerPrefab == null)
            return;

        networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
    }

    private bool ConfigureDirectTransport()
    {
        if (unityTransport == null)
            unityTransport = networkManager.NetworkConfig.NetworkTransport as UnityTransport;

        if (unityTransport == null)
            return true;

        unityTransport.UseWebSockets = false;

        if (RegionRunState.IsHost)
        {
            unityTransport.SetConnectionData(
                RegionRunState.ConnectionAddress,
                RegionRunState.ConnectionPort,
                hostListenAddress);
            return true;
        }

        unityTransport.SetConnectionData(
            RegionRunState.ConnectionAddress,
            RegionRunState.ConnectionPort);
        return true;
    }

    private async Task<bool> ConfigureRelayTransportAsync()
    {
        if (unityTransport == null)
            unityTransport = networkManager.NetworkConfig.NetworkTransport as UnityTransport;

        if (unityTransport == null)
        {
            Debug.LogError("GameRunBootstrap cannot start Relay because no UnityTransport was found.");
            return false;
        }

        await EnsureUnityServicesSignedInAsync();

        string connectionType = RegionRunState.RelayConnectionType;
        unityTransport.UseWebSockets = UsesWebSockets(connectionType);

        if (RegionRunState.IsHost)
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(
                Mathf.Max(1, RegionRunState.RelayMaxConnections));
            unityTransport.SetRelayServerData(
                AllocationUtils.ToRelayServerData(allocation, connectionType));

            string joinCode = await RelayService.Instance.GetJoinCodeAsync(
                allocation.AllocationId);
            RegionRunState.SetRelayJoinCode(joinCode);

            Debug.Log($"Relay host allocation ready. Join code: {RegionRunState.RelayJoinCode}");
            return true;
        }

        string relayJoinCode = RegionRunState.RelayJoinCode;
        if (string.IsNullOrWhiteSpace(relayJoinCode))
        {
            Debug.LogError("GameRunBootstrap cannot start Relay client because the join code is empty.");
            return false;
        }

        JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(
            relayJoinCode);
        unityTransport.SetRelayServerData(
            AllocationUtils.ToRelayServerData(joinAllocation, connectionType));

        return true;
    }

    private async Task EnsureUnityServicesSignedInAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
        {
            InitializationOptions options = new InitializationOptions();
            options.SetProfile(BuildAuthenticationProfile());
            await UnityServices.InitializeAsync(options);
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            AuthenticationService.Instance.SwitchProfile(BuildAuthenticationProfile());
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
    }

    private static bool UsesWebSockets(string connectionType)
    {
        return connectionType == "wss";
    }

    private static string BuildAuthenticationProfile()
    {
        int processId = 0;

        try
        {
            processId = System.Diagnostics.Process.GetCurrentProcess().Id;
        }
        catch
        {
            processId = Mathf.Abs(SystemInfo.deviceUniqueIdentifier.GetHashCode());
        }

        return $"ph_{Mathf.Abs(processId) % 1000000}";
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
        NetworkManager singleton = NetworkManager.Singleton;
        if (singleton != null && singleton.IsListening)
            networkManager = singleton;

        if (networkManager == null)
            networkManager = GetComponent<NetworkManager>();

        if (networkManager == null)
            networkManager = NetworkManager.Singleton;

        if (unityTransport == null && networkManager != null)
            unityTransport = networkManager.NetworkConfig.NetworkTransport as UnityTransport;

        if (unityTransport == null)
            unityTransport = GetComponent<UnityTransport>();

        GetPlayerSpawn();
    }
}
