using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    const string ReadyMessageName = "PoolHaunters_LobbyReady";
    const string SnapshotMessageName = "PoolHaunters_LobbySnapshot";
    const string StartGameMessageName = "PoolHaunters_LobbyStartGame";

    [Header("Run")]
    [SerializeField] private string regionName = "Submarino";
    [SerializeField] private string gameSceneName = "Game";
    [SerializeField] private bool useRandomSeed = true;
    [SerializeField] private int fixedSeed;

    [Header("Multiplayer LAN / Direct")]
    [SerializeField] private string connectionAddress = "127.0.0.1";
    [SerializeField, Min(1)] private int connectionPort = 7777;

    [Header("Multiplayer Relay")]
    [SerializeField] private string relayJoinCode;
    [SerializeField, Min(1)] private int relayMaxConnections = 3;
    [SerializeField] private string relayConnectionType = "dtls";

    [Header("Lobby Network")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private UnityTransport unityTransport;
    [SerializeField] private GameObject networkManagerPrefab;
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private NetworkPrefabsList networkPrefabsList;

    [Header("Lobby UI")]
    [SerializeField] private LobbyUI lobbyUI;
    [SerializeField] private CharacterSelectMenu characterSelectMenu;
    [SerializeField] private bool showCharacterSelectBeforeRun = true;
    [SerializeField] private Button hostButton;
    [SerializeField] private Button joinButton;
    [SerializeField] private Button playButton;
    [SerializeField] private Button disconnectButton;
    [SerializeField] private Toggle readyToggle;
    [SerializeField] private TMP_Text joinCodeText;
    [SerializeField] private TMP_Text lobbyStatusText;
    [SerializeField] private TMP_InputField joinCodeInput;

    private readonly Dictionary<ulong, bool> lobbyReadyByClientId =
        new Dictionary<ulong, bool>();

    private bool registeredNetworkCallbacks;
    private bool registeredMessageHandlers;
    private bool registeredUiListeners;
    private bool lobbyStartInProgress;
    private bool suppressReadyToggleEvent;
    private bool gameStartInProgress;
    private bool cursorUnlockRequested;
    private bool characterSelectConfirmed;

    public void StartSinglePlayer()
    {
        if (ShouldShowCharacterSelect())
        {
            ShowCharacterSelectThen(StartSinglePlayer);
            return;
        }

        ShutdownActiveLobby(false);
        RegionRunState.SelectSinglePlayerRegion(regionName, gameSceneName, CreateRunSeed());
        LoadGameScene();
    }

    public void StartHost()
    {
        if (ShouldShowCharacterSelect())
        {
            ShowCharacterSelectThen(StartHost);
            return;
        }

        RegionRunState.SelectMultiplayerHostRegion(
            regionName,
            gameSceneName,
            CreateRunSeed(),
            GetConnectionPort());

        LoadGameScene();
    }

    public void StartClient()
    {
        if (ShouldShowCharacterSelect())
        {
            ShowCharacterSelectThen(StartClient);
            return;
        }

        RegionRunState.SelectMultiplayerClientRegion(
            regionName,
            gameSceneName,
            CreateRunSeed(),
            connectionAddress,
            GetConnectionPort());

        LoadGameScene();
    }

    public async void StartRelayHost()
    {
        if (ShouldShowCharacterSelect())
        {
            ShowCharacterSelectThen(StartRelayHost);
            return;
        }

        if (networkManager != null && networkManager.IsListening && networkManager.IsHost)
        {
            StartLobbyGame();
            return;
        }

        if (lobbyStartInProgress)
            return;

        lobbyStartInProgress = true;
        SetLobbyStatus("Criando lobby...");
        SetMenuButtonsInteractable(false);

        try
        {
            EnsureNetworkManager();
            ConfigureLobbyNetworkManager();
            ConfigureConnectionApproval(createPlayerObject: false);
            RegisterNetworkCallbacks();

            bool transportConfigured = await ConfigureRelayHostTransportAsync();
            if (!transportConfigured)
            {
                ResetLobbyToInitialState();
                return;
            }

            if (!networkManager.StartHost())
            {
                Debug.LogError("MainMenu failed to start Relay host lobby.");
                ResetLobbyToInitialState();
                return;
            }

            DontDestroyOnLoad(networkManager.gameObject);
            RegisterMessageHandlersWhenReady();

            lobbyReadyByClientId.Clear();
            lobbyReadyByClientId[NetworkManager.ServerClientId] = false;

            SetRelayJoinCode(RegionRunState.RelayJoinCode);
            ApplyLobbyState(isHostLobby: true);
            RefreshLobbyUI();
            BroadcastLobbySnapshot();
            SetLobbyStatus("Lobby criado.");
        }
        catch (Exception exception)
        {
            Debug.LogError($"MainMenu failed to start Relay host lobby: {exception}");
            ResetLobbyToInitialState();
        }
        finally
        {
            lobbyStartInProgress = false;
            SetMenuButtonsInteractable(true);
        }
    }

    public async void StartRelayClient()
    {
        if (ShouldShowCharacterSelect())
        {
            ShowCharacterSelectThen(StartRelayClient);
            return;
        }

        if (lobbyStartInProgress || networkManager != null && networkManager.IsListening)
            return;

        string joinCode = GetJoinCodeFromInput();
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            Debug.LogError("MainMenu cannot start Relay client because the join code is empty.");
            SetLobbyStatus("Informe o codigo da sala.");
            return;
        }

        lobbyStartInProgress = true;
        SetLobbyStatus("Entrando no lobby...");
        SetMenuButtonsInteractable(false);

        try
        {
            EnsureNetworkManager();
            ConfigureLobbyNetworkManager();
            RegisterNetworkCallbacks();

            bool transportConfigured = await ConfigureRelayClientTransportAsync(joinCode);
            if (!transportConfigured)
            {
                ResetLobbyToInitialState();
                return;
            }

            if (!networkManager.StartClient())
            {
                Debug.LogError("MainMenu failed to start Relay client lobby.");
                ResetLobbyToInitialState();
                return;
            }

            DontDestroyOnLoad(networkManager.gameObject);
            RegisterMessageHandlersWhenReady();
            SetRelayJoinCode(joinCode);
            SetLobbyStatus("Conectando...");
        }
        catch (Exception exception)
        {
            Debug.LogError($"MainMenu failed to start Relay client lobby: {exception}");
            ResetLobbyToInitialState();
        }
        finally
        {
            lobbyStartInProgress = false;
            SetMenuButtonsInteractable(true);
        }
    }

    public void SetLobbyReady(bool isReady)
    {
        if (suppressReadyToggleEvent || networkManager == null || !networkManager.IsListening)
            return;

        ulong localClientId = networkManager.LocalClientId;

        if (networkManager.IsServer)
        {
            lobbyReadyByClientId[localClientId] = isReady;
            RefreshLobbyUI();
            BroadcastLobbySnapshot();
            return;
        }

        RegisterMessageHandlersWhenReady();

        if (networkManager.CustomMessagingManager == null)
            return;

        using FastBufferWriter writer = new FastBufferWriter(16, Allocator.Temp);
        writer.WriteValueSafe(isReady);
        networkManager.CustomMessagingManager.SendNamedMessage(
            ReadyMessageName,
            NetworkManager.ServerClientId,
            writer,
            NetworkDelivery.ReliableSequenced);
    }

    public void StartLobbyGame()
    {
        if (gameStartInProgress || networkManager == null || !networkManager.IsServer)
            return;

        if (!AllLobbyPlayersReady())
        {
            SetLobbyStatus("Aguardando todos ficarem Ready.");
            RefreshLobbyUI();
            return;
        }

        gameStartInProgress = true;

        int runSeed = CreateRunSeed();
        RegionRunState.SelectRelayHostRegion(
            regionName,
            gameSceneName,
            runSeed,
            relayMaxConnections,
            relayConnectionType);
        RegionRunState.SetRelayJoinCode(relayJoinCode);

        SendStartGameMessage(runSeed);
        SetLobbyStatus("Iniciando partida...");

        StartCoroutine(LoadLobbyGameAfterStartMessage());
    }

    private IEnumerator LoadLobbyGameAfterStartMessage()
    {
        yield return null;
        yield return new WaitForSeconds(0.15f);

        if (networkManager != null && networkManager.SceneManager != null)
        {
            networkManager.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
            yield break;
        }

        SceneManager.LoadScene(gameSceneName);
    }

    public void DisconnectLobby()
    {
        ShutdownActiveLobby(true);
        ResetLobbyToInitialState();
    }

    public void SetConnectionAddress(string address)
    {
        connectionAddress = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address;
    }

    public void SetConnectionPort(string port)
    {
        if (int.TryParse(port, out int parsedPort))
            connectionPort = Mathf.Clamp(parsedPort, 1, ushort.MaxValue);
    }

    public void SetRelayJoinCode(string joinCode)
    {
        relayJoinCode = string.IsNullOrWhiteSpace(joinCode)
            ? string.Empty
            : joinCode.Trim().ToUpperInvariant();

        if (joinCodeInput != null && joinCodeInput.text != relayJoinCode)
            joinCodeInput.SetTextWithoutNotify(relayJoinCode);

        if (joinCodeText != null)
            joinCodeText.text = string.IsNullOrWhiteSpace(relayJoinCode)
                ? "JoinCode"
                : relayJoinCode;
    }

    public void SetRelayMaxConnections(string maxConnections)
    {
        if (int.TryParse(maxConnections, out int parsedMaxConnections))
            relayMaxConnections = Mathf.Max(1, parsedMaxConnections);
    }

    public void SetRelayConnectionType(string connectionType)
    {
        relayConnectionType = string.IsNullOrWhiteSpace(connectionType)
            ? "dtls"
            : connectionType.Trim().ToLowerInvariant();
    }

    public void QuitButton()
    {
        Application.Quit();
    }

    private void Awake()
    {
        UnlockCursorForMenu();
        ResolveReferences();
        RegisterUiListeners();
    }

    private void Start()
    {
        UnlockCursorForMenu();
        ResolveReferences();
        ResetLobbyToInitialState();
    }

    private void OnDestroy()
    {
        ReleaseCursorUnlock();
        UnregisterNetworkCallbacks();
        UnregisterMessageHandlers();
    }

    private void EnsureNetworkManager()
    {
        if (networkManager == null)
            networkManager = NetworkManager.Singleton;

        if (networkManager == null)
            networkManager = FindAnyObjectByType<NetworkManager>();

        if (networkManager == null && networkManagerPrefab != null)
        {
            GameObject networkObject = Instantiate(networkManagerPrefab);
            networkObject.name = networkManagerPrefab.name;
            networkManager = networkObject.GetComponent<NetworkManager>();
            unityTransport = networkObject.GetComponent<UnityTransport>();
        }

        if (networkManager == null)
        {
            GameObject networkObject = new GameObject("NetworkManager");
            unityTransport = networkObject.AddComponent<UnityTransport>();
            networkManager = networkObject.AddComponent<NetworkManager>();
        }

        if (unityTransport == null)
            unityTransport = networkManager.GetComponent<UnityTransport>();

        if (unityTransport == null)
            unityTransport = networkManager.gameObject.AddComponent<UnityTransport>();

        networkManager.NetworkConfig.NetworkTransport = unityTransport;
        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.NetworkConfig.EnableSceneManagement = true;
        networkManager.NetworkConfig.AutoSpawnPlayerPrefabClientSide = false;

        if (networkPrefabsList != null &&
            !networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists.Contains(networkPrefabsList))
        {
            networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(networkPrefabsList);
        }
    }

    private void ConfigureLobbyNetworkManager()
    {
        if (networkManager == null)
            return;

        networkManager.NetworkConfig.PlayerPrefab = null;
        networkManager.NetworkConfig.AutoSpawnPlayerPrefabClientSide = false;
        networkManager.NetworkConfig.ConnectionApproval = true;
    }

    private void ConfigureConnectionApproval(bool createPlayerObject)
    {
        EnsureNetworkManager();
        ConfigureLobbyNetworkManager();
        networkManager.NetworkConfig.ConnectionApproval = true;
        networkManager.ConnectionApprovalCallback = (request, response) =>
        {
            response.Approved = true;
            response.CreatePlayerObject = createPlayerObject;
        };
    }

    private async Task<bool> ConfigureRelayHostTransportAsync()
    {
        if (unityTransport == null)
        {
            Debug.LogError("MainMenu cannot start Relay host because no UnityTransport was found.");
            return false;
        }

        await EnsureUnityServicesSignedInAsync();

        string connectionType = SanitizeRelayConnectionType(relayConnectionType);
        unityTransport.UseWebSockets = UsesWebSockets(connectionType);

        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(
            Mathf.Max(1, relayMaxConnections));
        unityTransport.SetRelayServerData(
            AllocationUtils.ToRelayServerData(allocation, connectionType));

        string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        RegionRunState.SelectRelayHostRegion(
            regionName,
            gameSceneName,
            CreateRunSeed(),
            relayMaxConnections,
            connectionType);
        RegionRunState.SetRelayJoinCode(joinCode);

        relayConnectionType = connectionType;
        SetRelayJoinCode(joinCode);
        return true;
    }

    private async Task<bool> ConfigureRelayClientTransportAsync(string joinCode)
    {
        if (unityTransport == null)
        {
            Debug.LogError("MainMenu cannot start Relay client because no UnityTransport was found.");
            return false;
        }

        await EnsureUnityServicesSignedInAsync();

        string connectionType = SanitizeRelayConnectionType(relayConnectionType);
        unityTransport.UseWebSockets = UsesWebSockets(connectionType);

        JoinAllocation joinAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
        unityTransport.SetRelayServerData(
            AllocationUtils.ToRelayServerData(joinAllocation, connectionType));

        RegionRunState.SelectRelayClientRegion(
            regionName,
            gameSceneName,
            CreateRunSeed(),
            joinCode,
            connectionType);

        relayConnectionType = connectionType;
        SetRelayJoinCode(joinCode);
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

    private void RegisterNetworkCallbacks()
    {
        EnsureNetworkManager();

        if (registeredNetworkCallbacks)
            return;

        networkManager.OnClientConnectedCallback += HandleClientConnected;
        networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
        networkManager.OnClientStopped += HandleLocalClientStopped;
        networkManager.OnServerStopped += HandleLocalServerStopped;
        registeredNetworkCallbacks = true;
    }

    private void UnregisterNetworkCallbacks()
    {
        if (!registeredNetworkCallbacks || networkManager == null)
            return;

        networkManager.OnClientConnectedCallback -= HandleClientConnected;
        networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
        networkManager.OnClientStopped -= HandleLocalClientStopped;
        networkManager.OnServerStopped -= HandleLocalServerStopped;
        registeredNetworkCallbacks = false;
    }

    private void RegisterMessageHandlersWhenReady()
    {
        if (networkManager == null || registeredMessageHandlers)
            return;

        if (networkManager.CustomMessagingManager == null)
        {
            StartCoroutine(RegisterMessageHandlersWhenCustomMessagingIsReady());
            return;
        }

        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(
            ReadyMessageName,
            HandleReadyMessage);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(
            SnapshotMessageName,
            HandleSnapshotMessage);
        networkManager.CustomMessagingManager.RegisterNamedMessageHandler(
            StartGameMessageName,
            HandleStartGameMessage);

        registeredMessageHandlers = true;
    }

    private IEnumerator RegisterMessageHandlersWhenCustomMessagingIsReady()
    {
        while (networkManager != null &&
            networkManager.IsListening &&
            networkManager.CustomMessagingManager == null)
        {
            yield return null;
        }

        RegisterMessageHandlersWhenReady();
    }

    private void UnregisterMessageHandlers()
    {
        if (!registeredMessageHandlers ||
            networkManager == null ||
            networkManager.CustomMessagingManager == null)
        {
            registeredMessageHandlers = false;
            return;
        }

        networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(ReadyMessageName);
        networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(SnapshotMessageName);
        networkManager.CustomMessagingManager.UnregisterNamedMessageHandler(StartGameMessageName);
        registeredMessageHandlers = false;
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (networkManager == null)
            return;

        RegisterMessageHandlersWhenReady();

        if (networkManager.IsServer)
        {
            if (!lobbyReadyByClientId.ContainsKey(clientId))
                lobbyReadyByClientId[clientId] = false;

            RefreshLobbyUI();
            BroadcastLobbySnapshot();
        }

        if (clientId == networkManager.LocalClientId)
        {
            ApplyLobbyState(networkManager.IsHost);
            SetLobbyStatus("Conectado ao lobby.");
        }
    }

    private void HandleClientDisconnected(ulong clientId)
    {
        if (networkManager == null)
            return;

        if (networkManager.IsServer)
        {
            lobbyReadyByClientId.Remove(clientId);
            RefreshLobbyUI();
            BroadcastLobbySnapshot();
            return;
        }

        if (clientId == networkManager.LocalClientId ||
            clientId == NetworkManager.ServerClientId)
        {
            ResetLobbyToInitialState();
        }
    }

    private void HandleLocalClientStopped(bool wasHost)
    {
        ResetLobbyToInitialState();
    }

    private void HandleLocalServerStopped(bool wasHost)
    {
        ResetLobbyToInitialState();
    }

    private void HandleReadyMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (networkManager == null || !networkManager.IsServer)
            return;

        reader.ReadValueSafe(out bool isReady);
        lobbyReadyByClientId[senderClientId] = isReady;
        RefreshLobbyUI();
        BroadcastLobbySnapshot();
    }

    private void HandleSnapshotMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (networkManager == null ||
            networkManager.IsServer ||
            senderClientId != NetworkManager.ServerClientId)
        {
            return;
        }

        reader.ReadValueSafe(out int count);
        lobbyReadyByClientId.Clear();

        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out ulong clientId);
            reader.ReadValueSafe(out bool isReady);
            lobbyReadyByClientId[clientId] = isReady;
        }

        ApplyLobbyState(false);
        RefreshLobbyUI();
    }

    private void HandleStartGameMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId)
            return;

        reader.ReadValueSafe(out int runSeed);
        RegionRunState.SelectRelayClientRegion(
            regionName,
            gameSceneName,
            runSeed,
            relayJoinCode,
            relayConnectionType);
    }

    private void BroadcastLobbySnapshot()
    {
        if (networkManager == null ||
            !networkManager.IsServer ||
            networkManager.CustomMessagingManager == null)
        {
            return;
        }

        foreach (ulong clientId in networkManager.ConnectedClientsIds)
            SendLobbySnapshot(clientId);
    }

    private void SendLobbySnapshot(ulong targetClientId)
    {
        using FastBufferWriter writer = new FastBufferWriter(1024, Allocator.Temp);
        writer.WriteValueSafe(lobbyReadyByClientId.Count);

        foreach (KeyValuePair<ulong, bool> player in lobbyReadyByClientId)
        {
            writer.WriteValueSafe(player.Key);
            writer.WriteValueSafe(player.Value);
        }

        networkManager.CustomMessagingManager.SendNamedMessage(
            SnapshotMessageName,
            targetClientId,
            writer,
            NetworkDelivery.ReliableSequenced);
    }

    private void SendStartGameMessage(int runSeed)
    {
        if (networkManager == null ||
            !networkManager.IsServer ||
            networkManager.CustomMessagingManager == null)
        {
            return;
        }

        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (clientId == NetworkManager.ServerClientId)
                continue;

            using FastBufferWriter writer = new FastBufferWriter(16, Allocator.Temp);
            writer.WriteValueSafe(runSeed);
            networkManager.CustomMessagingManager.SendNamedMessage(
                StartGameMessageName,
                clientId,
                writer,
                NetworkDelivery.ReliableSequenced);
        }
    }

    private void RefreshLobbyUI()
    {
        if (lobbyUI == null)
            return;

        List<LobbyUI.PlayerView> players = new List<LobbyUI.PlayerView>();
        ulong localClientId = networkManager != null ? networkManager.LocalClientId : 0;

        foreach (KeyValuePair<ulong, bool> player in lobbyReadyByClientId)
        {
            bool isHost = player.Key == NetworkManager.ServerClientId;
            players.Add(new LobbyUI.PlayerView(
                player.Key,
                isHost ? "Host" : $"Player {player.Key}",
                isHost,
                player.Value,
                player.Key == localClientId));
        }

        players.Sort((a, b) => a.clientId.CompareTo(b.clientId));
        lobbyUI.SetPlayers(players);

        bool localReady = lobbyReadyByClientId.TryGetValue(localClientId, out bool ready) && ready;
        SetReadyToggleWithoutNotify(localReady);

        if (playButton != null)
            playButton.interactable = networkManager != null &&
                networkManager.IsServer &&
                AllLobbyPlayersReady();
    }

    private bool AllLobbyPlayersReady()
    {
        if (lobbyReadyByClientId.Count == 0)
            return false;

        foreach (bool isReady in lobbyReadyByClientId.Values)
        {
            if (!isReady)
                return false;
        }

        return true;
    }

    private void ApplyLobbyState(bool isHostLobby)
    {
        SetActive(hostButton, false);
        SetActive(joinButton, false);
        SetActive(playButton, isHostLobby);
        SetActive(disconnectButton, true);
        SetActive(readyToggle, true);
        SetActive(joinCodeInput, false);

        if (joinCodeText != null)
        {
            joinCodeText.gameObject.SetActive(isHostLobby);
            joinCodeText.text = string.IsNullOrWhiteSpace(relayJoinCode)
                ? "JoinCode"
                : relayJoinCode;
        }

        if (playButton != null)
            playButton.interactable = false;
    }

    private void ResetLobbyToInitialState()
    {
        lobbyStartInProgress = false;
        gameStartInProgress = false;
        characterSelectConfirmed = false;
        lobbyReadyByClientId.Clear();

        if (lobbyUI != null)
            lobbyUI.ClearPlayers();

        SetActive(hostButton, true);
        SetActive(joinButton, true);
        SetActive(playButton, false);
        SetActive(disconnectButton, false);
        SetActive(readyToggle, false);
        SetActive(joinCodeInput, true);

        if (joinCodeText != null)
            joinCodeText.gameObject.SetActive(false);

        if (playButton != null)
            playButton.interactable = false;

        SetReadyToggleWithoutNotify(false);
        SetLobbyStatus(string.Empty);
    }

    private void ShutdownActiveLobby(bool clearRunState)
    {
        if (networkManager != null && networkManager.IsListening)
            networkManager.Shutdown();

        if (clearRunState)
            RegionRunState.Clear();

        lobbyReadyByClientId.Clear();
    }

    private void SetMenuButtonsInteractable(bool interactable)
    {
        if (hostButton != null)
            hostButton.interactable = interactable;

        if (joinButton != null)
            joinButton.interactable = interactable;
    }

    private void SetReadyToggleWithoutNotify(bool isReady)
    {
        if (readyToggle == null)
            return;

        suppressReadyToggleEvent = true;
        readyToggle.SetIsOnWithoutNotify(isReady);
        suppressReadyToggleEvent = false;
    }

    private void SetLobbyStatus(string status)
    {
        if (lobbyStatusText != null)
            lobbyStatusText.text = status;
    }

    private string GetJoinCodeFromInput()
    {
        if (joinCodeInput != null && !string.IsNullOrWhiteSpace(joinCodeInput.text))
            return joinCodeInput.text.Trim().ToUpperInvariant();

        return string.IsNullOrWhiteSpace(relayJoinCode)
            ? string.Empty
            : relayJoinCode.Trim().ToUpperInvariant();
    }

    private void RegisterUiListeners()
    {
        if (registeredUiListeners)
            return;

        if (hostButton != null)
        {
            hostButton.onClick.RemoveAllListeners();
            hostButton.onClick.AddListener(StartRelayHost);
        }

        if (joinButton != null)
        {
            joinButton.onClick.RemoveAllListeners();
            joinButton.onClick.AddListener(StartRelayClient);
        }

        if (playButton != null)
        {
            playButton.onClick.RemoveAllListeners();
            playButton.onClick.AddListener(StartLobbyGame);
        }

        if (disconnectButton != null)
        {
            disconnectButton.onClick.RemoveAllListeners();
            disconnectButton.onClick.AddListener(DisconnectLobby);
        }

        if (readyToggle != null)
        {
            readyToggle.onValueChanged.RemoveAllListeners();
            readyToggle.onValueChanged.AddListener(SetLobbyReady);
        }

        registeredUiListeners = true;
    }

    private void ResolveReferences()
    {
        if (lobbyUI == null)
            lobbyUI = FindAnyObjectByType<LobbyUI>(FindObjectsInactive.Include);

        if (characterSelectMenu == null)
            characterSelectMenu = FindAnyObjectByType<CharacterSelectMenu>(FindObjectsInactive.Include);

        if (characterSelectMenu == null)
            characterSelectMenu = gameObject.AddComponent<CharacterSelectMenu>();

        Transform multiplayerMenu = FindChildByName(null, "MultiplayerMenu");
        Transform root = multiplayerMenu != null ? multiplayerMenu : transform.root;

        if (hostButton == null)
            hostButton = FindComponentByName<Button>(root, "Host");

        if (joinButton == null)
            joinButton = FindComponentByName<Button>(root, "Client");

        if (playButton == null)
            playButton = FindComponentByName<Button>(root, "Play");

        if (disconnectButton == null)
            disconnectButton = FindComponentByName<Button>(root, "Disconnect");

        if (readyToggle == null)
            readyToggle = FindComponentByName<Toggle>(root, "Ready");

        if (joinCodeText == null)
            joinCodeText = FindComponentByName<TMP_Text>(root, "JoinCode");

        if (joinCodeInput == null)
            joinCodeInput = FindComponentByName<TMP_InputField>(root, "JoinCodeField");

        if (networkManager == null)
            networkManager = NetworkManager.Singleton;

        if (networkManager == null)
            networkManager = FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);

        if (unityTransport == null && networkManager != null)
            unityTransport = networkManager.GetComponent<UnityTransport>();
    }

    private int CreateRunSeed()
    {
        if (!useRandomSeed)
            return fixedSeed;

        return UnityEngine.Random.Range(1, int.MaxValue);
    }

    private ushort GetConnectionPort()
    {
        return (ushort)Mathf.Clamp(connectionPort, 1, ushort.MaxValue);
    }

    private void LoadGameScene()
    {
        if (string.IsNullOrWhiteSpace(gameSceneName))
        {
            Debug.LogError("MainMenu cannot start the run because the game scene name is empty.");
            return;
        }

        ReleaseCursorUnlock();
        characterSelectConfirmed = false;
        SceneManager.LoadScene(gameSceneName);
    }

    private bool ShouldShowCharacterSelect()
    {
        return showCharacterSelectBeforeRun &&
            !characterSelectConfirmed &&
            characterSelectMenu != null;
    }

    private void ShowCharacterSelectThen(Action onConfirmed)
    {
        ResolveReferences();
        if (characterSelectMenu == null)
        {
            characterSelectConfirmed = true;
            onConfirmed?.Invoke();
            return;
        }

        characterSelectMenu.Show(() =>
        {
            characterSelectConfirmed = true;
            onConfirmed?.Invoke();
        });
    }

    private void UnlockCursorForMenu()
    {
        RequestCursorUnlock();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void RequestCursorUnlock()
    {
        if (cursorUnlockRequested)
            return;

        CursorLockController.RequestCursorUnlocked();
        cursorUnlockRequested = true;
    }

    private void ReleaseCursorUnlock()
    {
        if (!cursorUnlockRequested)
            return;

        CursorLockController.ReleaseCursorUnlocked();
        cursorUnlockRequested = false;
    }

    private static bool UsesWebSockets(string connectionType)
    {
        return connectionType == "wss";
    }

    private static string SanitizeRelayConnectionType(string connectionType)
    {
        if (string.IsNullOrWhiteSpace(connectionType))
            return "dtls";

        string sanitized = connectionType.Trim().ToLowerInvariant();
        switch (sanitized)
        {
            case "udp":
            case "dtls":
            case "wss":
                return sanitized;
            default:
                Debug.LogWarning(
                    $"Unsupported Relay connection type '{connectionType}'. Use udp, dtls or wss. Falling back to dtls.");
                return "dtls";
        }
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

    private static void SetActive(Selectable selectable, bool active)
    {
        if (selectable != null)
            selectable.gameObject.SetActive(active);
    }

    private static Transform FindChildByName(Transform root, string childName)
    {
        if (string.IsNullOrWhiteSpace(childName))
            return null;

        if (root == null)
        {
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null && objects[i].name == childName)
                    return objects[i].transform;
            }

            return null;
        }

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == childName)
                return child;

            Transform match = FindChildByName(child, childName);
            if (match != null)
                return match;
        }

        return null;
    }

    private static T FindComponentByName<T>(Transform root, string objectName)
        where T : Component
    {
        Transform child = FindChildByName(root, objectName);
        return child != null ? child.GetComponent<T>() : null;
    }
}
