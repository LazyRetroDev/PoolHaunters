using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class PhysicalLobbyManager : NetworkBehaviour
{
    [Header("Run")]
    [SerializeField] private string regionName = "Submarino";
    [SerializeField] private string gameSceneName = "Game";
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int fixedSeed;

    [Header("Rules")]
    [SerializeField, Min(1)] private int maxPlayers = 4;
    [SerializeField] private bool requireAllConnectedPlayersReady = true;
    [SerializeField] private bool requireAllPlayersInLobbyZone;
    [SerializeField] private bool hostOnlyCanStart = true;
    [SerializeField] private bool autoStartWhenAllReady;

    [Header("Optional UI")]
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private TMP_Text readyCountText;

    private readonly Dictionary<ulong, bool> readyByClientId =
        new Dictionary<ulong, bool>();
    private readonly HashSet<ulong> playersInLobbyZone =
        new HashSet<ulong>();

    private bool runStarting;
    private bool offlineReady;

    void Awake()
    {
        RefreshPresentation();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                EnsureClientTracked(clientId);
        }

        RefreshPresentation();
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    public void ToggleReady(PlayerStatus player)
    {
        SetReady(player, !IsPlayerReady(player));
    }

    public void SetReady(PlayerStatus player, bool ready)
    {
        if (IsNetworkSessionRunning())
        {
            ulong clientId = GetClientId(player);
            if (IsServer)
                ApplyReady(clientId, ready);
            else
                SetReadyServerRpc(ready);

            return;
        }

        offlineReady = ready;
        RefreshPresentation();
    }

    public bool IsPlayerReady(PlayerStatus player)
    {
        if (IsNetworkSessionRunning())
        {
            ulong clientId = GetClientId(player);
            return readyByClientId.TryGetValue(clientId, out bool ready) && ready;
        }

        return offlineReady;
    }

    public void TryStartRun(PlayerStatus requester)
    {
        if (IsNetworkSessionRunning())
        {
            if (IsServer)
                TryStartRunServerSide(GetClientId(requester));
            else
                TryStartRunServerRpc();

            return;
        }

        if (!CanStartRunOffline())
        {
            SetStatus("Ready up before starting.");
            return;
        }

        StartOfflineRun();
    }

    public void RegisterPlayerInLobby(PlayerStatus player)
    {
        if (player == null) return;

        if (IsNetworkSessionRunning())
        {
            if (IsServer)
            {
                ulong serverClientId = GetClientId(player);
                playersInLobbyZone.Add(serverClientId);
                EnsureClientTracked(serverClientId);
                RefreshPresentationClientRpc(BuildReadyCount(), BuildTotalCount(), CanStartRunServerSide(), ToFixedString(GetStatusText()));
            }
            else if (player.IsOwner)
            {
                SetPlayerInLobbyServerRpc(true);
            }

            return;
        }

        ulong clientId = GetClientId(player);
        playersInLobbyZone.Add(clientId);
        EnsureClientTracked(clientId);
        RefreshPresentation();
    }

    public void UnregisterPlayerInLobby(PlayerStatus player)
    {
        if (player == null) return;

        if (IsNetworkSessionRunning())
        {
            if (IsServer)
            {
                playersInLobbyZone.Remove(GetClientId(player));
                RefreshPresentationClientRpc(BuildReadyCount(), BuildTotalCount(), CanStartRunServerSide(), ToFixedString(GetStatusText()));
            }
            else if (player.IsOwner)
            {
                SetPlayerInLobbyServerRpc(false);
            }

            return;
        }

        ulong clientId = GetClientId(player);
        playersInLobbyZone.Remove(clientId);
        RefreshPresentation();
    }

    void HandleClientConnected(ulong clientId)
    {
        EnsureClientTracked(clientId);
        RefreshPresentationClientRpc(BuildReadyCount(), BuildTotalCount(), CanStartRunServerSide(), ToFixedString(GetStatusText()));
    }

    void HandleClientDisconnected(ulong clientId)
    {
        readyByClientId.Remove(clientId);
        playersInLobbyZone.Remove(clientId);
        RefreshPresentationClientRpc(BuildReadyCount(), BuildTotalCount(), CanStartRunServerSide(), ToFixedString(GetStatusText()));
    }

    void EnsureClientTracked(ulong clientId)
    {
        if (!readyByClientId.ContainsKey(clientId))
            readyByClientId[clientId] = false;
    }

    void ApplyReady(ulong clientId, bool ready)
    {
        EnsureClientTracked(clientId);
        readyByClientId[clientId] = ready;
        RefreshPresentationClientRpc(BuildReadyCount(), BuildTotalCount(), CanStartRunServerSide(), ToFixedString(GetStatusText()));

        if (autoStartWhenAllReady && CanStartRunServerSide())
            StartNetworkRun();
    }

    void TryStartRunServerSide(ulong requesterClientId)
    {
        if (runStarting) return;

        if (hostOnlyCanStart && requesterClientId != Unity.Netcode.NetworkManager.ServerClientId)
        {
            SendStatusClientRpc(ToFixedString("Only the host can start the run."));
            return;
        }

        if (!CanStartRunServerSide())
        {
            SendStatusClientRpc(ToFixedString("Waiting for all players to ready up."));
            return;
        }

        StartNetworkRun();
    }

    bool CanStartRunOffline()
    {
        return !requireAllConnectedPlayersReady || offlineReady;
    }

    bool CanStartRunServerSide()
    {
        if (!IsServer || NetworkManager == null || readyByClientId.Count == 0)
            return false;

        if (readyByClientId.Count > Mathf.Max(1, maxPlayers))
            return false;

        if (!requireAllConnectedPlayersReady)
            return !requireAllPlayersInLobbyZone || AllConnectedPlayersAreInLobbyZone();

        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (!readyByClientId.TryGetValue(clientId, out bool ready) || !ready)
                return false;
        }

        return !requireAllPlayersInLobbyZone || AllConnectedPlayersAreInLobbyZone();
    }

    bool AllConnectedPlayersAreInLobbyZone()
    {
        if (NetworkManager == null)
            return false;

        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (!playersInLobbyZone.Contains(clientId))
                return false;
        }

        return true;
    }

    void StartNetworkRun()
    {
        if (runStarting || NetworkManager == null)
            return;

        runStarting = true;
        int runSeed = CreateRunSeed();
        string sceneName = string.IsNullOrWhiteSpace(gameSceneName) ? "Game" : gameSceneName;
        string safeRegionName = string.IsNullOrWhiteSpace(regionName) ? sceneName : regionName;
        string relayJoinCode = RegionRunState.RelayJoinCode;
        string relayConnectionType = RegionRunState.RelayConnectionType;
        int relayMaxConnections = Mathf.Max(1, maxPlayers - 1);

        PrepareRunStateClientRpc(
            ToFixedString(safeRegionName),
            ToFixedString(sceneName),
            runSeed,
            ToFixedString(relayJoinCode),
            ToFixedString(relayConnectionType),
            (int)RegionRunState.Difficulty);

        RegionRunState.SelectRelayHostRegion(
            safeRegionName,
            sceneName,
            runSeed,
            relayMaxConnections,
            relayConnectionType,
            RegionRunState.Difficulty);
        RegionRunState.SetRelayJoinCode(relayJoinCode);

        SetStatus("Starting run...");

        if (NetworkManager.SceneManager != null)
            NetworkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        else
            SceneManager.LoadScene(sceneName);
    }

    void StartOfflineRun()
    {
        runStarting = true;
        string sceneName = string.IsNullOrWhiteSpace(gameSceneName) ? "Game" : gameSceneName;
        string safeRegionName = string.IsNullOrWhiteSpace(regionName) ? sceneName : regionName;
        RegionRunState.SelectSinglePlayerRegion(
            safeRegionName,
            sceneName,
            CreateRunSeed(),
            RegionRunState.Difficulty);
        SceneManager.LoadScene(sceneName);
    }

    int CreateRunSeed()
    {
        return useRandomSeed ? Random.Range(1, int.MaxValue) : fixedSeed;
    }

    ulong GetClientId(PlayerStatus player)
    {
        if (player != null && player.NetworkObject != null && player.NetworkObject.IsSpawned)
            return player.NetworkObject.OwnerClientId;

        return NetworkManager != null ? NetworkManager.LocalClientId : 0;
    }

    bool IsNetworkSessionRunning()
    {
        return Unity.Netcode.NetworkManager.Singleton != null &&
            Unity.Netcode.NetworkManager.Singleton.IsListening;
    }

    int BuildReadyCount()
    {
        int count = 0;
        foreach (bool ready in readyByClientId.Values)
        {
            if (ready)
                count++;
        }

        return count;
    }

    int BuildTotalCount()
    {
        if (NetworkManager != null && NetworkManager.IsListening)
            return NetworkManager.ConnectedClientsIds.Count;

        return 1;
    }

    string GetStatusText()
    {
        if (runStarting)
            return "Starting run...";

        if (IsNetworkSessionRunning())
            return CanStartRunServerSide()
                ? "Everyone is ready."
                : "Waiting for players.";

        return offlineReady ? "Ready." : "Not ready.";
    }

    void RefreshPresentation()
    {
        SetStatus(GetStatusText());
        int total = IsNetworkSessionRunning() ? BuildTotalCount() : 1;
        int ready = IsNetworkSessionRunning() ? BuildReadyCount() : offlineReady ? 1 : 0;
        SetReadyCount(ready, total);
    }

    void SetStatus(string status)
    {
        if (statusText != null)
            statusText.text = status;
    }

    void SetReadyCount(int ready, int total)
    {
        if (readyCountText != null)
            readyCountText.text = $"{ready}/{Mathf.Max(1, total)} READY";
    }

    FixedString128Bytes ToFixedString(string value)
    {
        return new FixedString128Bytes(string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    [ServerRpc(RequireOwnership = false)]
    void SetReadyServerRpc(bool ready, ServerRpcParams serverRpcParams = default)
    {
        ApplyReady(serverRpcParams.Receive.SenderClientId, ready);
    }

    [ServerRpc(RequireOwnership = false)]
    void TryStartRunServerRpc(ServerRpcParams serverRpcParams = default)
    {
        TryStartRunServerSide(serverRpcParams.Receive.SenderClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    void SetPlayerInLobbyServerRpc(bool inLobby, ServerRpcParams serverRpcParams = default)
    {
        ulong clientId = serverRpcParams.Receive.SenderClientId;
        EnsureClientTracked(clientId);

        if (inLobby)
            playersInLobbyZone.Add(clientId);
        else
            playersInLobbyZone.Remove(clientId);

        RefreshPresentationClientRpc(BuildReadyCount(), BuildTotalCount(), CanStartRunServerSide(), ToFixedString(GetStatusText()));
    }

    [ClientRpc]
    void RefreshPresentationClientRpc(int ready, int total, bool canStart, FixedString128Bytes status)
    {
        SetStatus(canStart ? "Ready to start." : status.ToString());
        SetReadyCount(ready, total);
    }

    [ClientRpc]
    void SendStatusClientRpc(FixedString128Bytes status)
    {
        SetStatus(status.ToString());
    }

    [ClientRpc]
    void PrepareRunStateClientRpc(
        FixedString128Bytes safeRegionName,
        FixedString128Bytes sceneName,
        int runSeed,
        FixedString128Bytes relayJoinCode,
        FixedString128Bytes relayConnectionType,
        int difficultyIndex)
    {
        if (IsServer)
            return;

        RegionRunState.SelectRelayClientRegion(
            safeRegionName.ToString(),
            sceneName.ToString(),
            runSeed,
            relayJoinCode.ToString(),
            relayConnectionType.ToString(),
            (RunDifficulty)Mathf.Clamp(
                difficultyIndex,
                0,
                System.Enum.GetValues(typeof(RunDifficulty)).Length - 1));
    }
}
