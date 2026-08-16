using System;
using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public class SubmarineReturnController : MonoBehaviour, IPlayerInteractable
{
    const string ConfirmRequestMessageName = "SubmarineReturnConfirmRequest";
    const string ConfirmStateMessageName = "SubmarineReturnConfirmState";
    const string NextPhaseStateMessageName = "SubmarineReturnNextPhaseState";

    [Header("Return Rules")]
    public bool requireLevelCompleted = true;
    public bool onlyPlayersCanTrigger = true;
    public bool allowTriggerReturn = false;
    public bool allowInteractionReturn = true;
    public bool requireSpecificRoomCategory = true;
    public RoomCategory requiredRoomCategory = RoomCategory.SubmarineSpawn;
    public bool autoAddTriggerCollider = true;
    public Vector3 fallbackTriggerSize = new Vector3(3f, 3f, 3f);

    [Header("Shop Flow")]
    public bool routeThroughPhysicalShop = true;
    public string shopSceneName = "Shop";

    [Header("Next Phase Scene Choice")]
    public RunSceneOption[] sceneOptions =
    {
        new RunSceneOption { regionName = "Hospital", sceneName = "Game", weight = 1f },
        new RunSceneOption { regionName = "Museum", sceneName = "Game 1", weight = 1f },
        new RunSceneOption { regionName = "Hotel", sceneName = "Game 2", weight = 1f }
    };
    public bool blockCurrentScene = true;
    public bool blockPreviousScene = false;
    public bool useRandomSeed = true;
    public int fixedSeed;

    [Header("Multiplayer Confirmation")]
    public bool requireAllLivingPlayersInMultiplayer = true;
    [SerializeField] private TMP_Text confirmationText;
    [SerializeField] private string confirmationTextChildName = "ConfirmationText";
    public bool createConfirmationTextIfMissing = true;
    public bool hideConfirmationTextUntilFirstConfirm = true;
    public bool billboardConfirmationText = true;
    public Vector3 confirmationTextLocalOffset = new Vector3(0f, 1.25f, 0f);
    [Min(0.1f)] public float confirmationTextFontSize = 2f;
    public Color confirmationTextColor = Color.white;

    [Header("Debug")]
    [SerializeField] private bool transitionStarted;
    [SerializeField] private string selectedNextScene;
    [SerializeField] private string selectedDestinationScene;
    [SerializeField] private int confirmedLivingPlayers;
    [SerializeField] private int requiredLivingPlayers;
    [SerializeField] private bool transitionQueued;

    private readonly HashSet<ulong> confirmedClientIds = new HashSet<ulong>();
    private NetworkManager registeredNetworkManager;
    private bool registeredConfirmRequestHandler;
    private bool registeredConfirmStateHandler;
    private bool registeredNextPhaseStateHandler;

    void Reset()
    {
        EnsureTriggerCollider();
    }

    void Awake()
    {
        if (autoAddTriggerCollider)
            EnsureTriggerCollider();

        CacheConfirmationText();
        RefreshConfirmationText();
    }

    void OnEnable()
    {
        TryRegisterNetworkMessages();
    }

    void Start()
    {
        TryRegisterNetworkMessages();
        RefreshServerConfirmationCounts();
        RefreshConfirmationText();
    }

    void OnDisable()
    {
        UnregisterNetworkMessages();
    }

    void Update()
    {
        if (!transitionQueued)
            return;

        transitionQueued = false;
        BeginReturnTransition(NetworkManager.Singleton);
    }

    void LateUpdate()
    {
        if (!billboardConfirmationText || confirmationText == null)
            return;

        Camera camera = Camera.main;
        if (camera == null)
            return;

        Transform textTransform = confirmationText.transform;
        Vector3 direction = textTransform.position - camera.transform.position;
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        textTransform.rotation =
            Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    void OnTriggerEnter(Collider other)
    {
        if (allowTriggerReturn)
            TryReturnToSubmarine(other);
    }

    public void TryReturnToSubmarine(Collider other)
    {
        TryReturnToSubmarine(other, other);
    }

    public void Interact(PlayerInventory inventory)
    {
        if (!allowInteractionReturn)
            return;

        TryReturnToSubmarine(inventory, null);
    }

    void TryReturnToSubmarine(Component playerSource, Collider playerCollider)
    {
        if (transitionStarted)
            return;

        if (requireLevelCompleted &&
            (LevelObjectiveManager.Instance == null ||
             !LevelObjectiveManager.Instance.LevelCompleted))
        {
            return;
        }

        if (requireSpecificRoomCategory && !IsInsideRequiredRoomCategory())
            return;

        if (onlyPlayersCanTrigger && !IsPlayerSource(playerSource, playerCollider))
            return;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (ShouldUseMultiplayerConfirmation(networkManager))
        {
            HandleMultiplayerConfirmation(playerSource, playerCollider, networkManager);
            return;
        }

        BeginReturnTransition(networkManager);
    }

    void BeginReturnTransition(NetworkManager networkManager)
    {
        if (transitionStarted)
            return;

        RunSceneOption nextScene = ChooseNextScene();
        if (nextScene == null || string.IsNullOrWhiteSpace(nextScene.sceneName))
        {
            Debug.LogWarning("SubmarineReturnController could not choose a next scene.");
            return;
        }

        transitionStarted = true;
        selectedNextScene = nextScene.sceneName;
        selectedDestinationScene = GetDestinationSceneName(nextScene);

        RegionRunState.SelectNextPhaseRegion(
            string.IsNullOrWhiteSpace(nextScene.regionName)
                ? nextScene.sceneName
                : nextScene.regionName,
            nextScene.sceneName,
            CreateRunSeed());

        if (networkManager != null &&
            networkManager.IsListening &&
            networkManager.SceneManager != null)
        {
            BroadcastNextPhaseState(networkManager);
            StartCoroutine(LoadNetworkSceneAfterStateSync(
                networkManager,
                selectedDestinationScene,
                "final objective"));
            return;
        }

        SceneManager.LoadScene(selectedDestinationScene);
    }

    bool ShouldUseMultiplayerConfirmation(NetworkManager networkManager)
    {
        return requireAllLivingPlayersInMultiplayer &&
            networkManager != null &&
            networkManager.IsListening;
    }

    void HandleMultiplayerConfirmation(
        Component playerSource,
        Collider playerCollider,
        NetworkManager networkManager)
    {
        if (networkManager == null || !networkManager.IsListening)
            return;

        if (!networkManager.IsServer)
        {
            SendConfirmRequest(networkManager);
            return;
        }

        if (!TryGetPlayerClientId(playerSource, playerCollider, out ulong clientId))
            clientId = networkManager.LocalClientId;

        ConfirmClient(clientId, networkManager);
    }

    void ConfirmClient(ulong clientId, NetworkManager networkManager)
    {
        if (transitionStarted || networkManager == null || !networkManager.IsServer)
            return;

        RefreshServerConfirmationCounts();

        if (requiredLivingPlayers <= 0)
        {
            QueueReturnTransition();
            return;
        }

        if (IsLivingClientId(clientId))
            confirmedClientIds.Add(clientId);

        RefreshServerConfirmationCounts();
        BroadcastConfirmationState(networkManager);

        if (confirmedLivingPlayers >= requiredLivingPlayers)
            QueueReturnTransition();
    }

    void QueueReturnTransition()
    {
        if (transitionStarted || transitionQueued)
            return;

        transitionQueued = true;
    }

    void RefreshServerConfirmationCounts()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening && !networkManager.IsServer)
            return;

        List<ulong> livingClientIds = GetLivingClientIds();
        requiredLivingPlayers = livingClientIds.Count;

        confirmedClientIds.RemoveWhere(clientId => !livingClientIds.Contains(clientId));
        confirmedLivingPlayers = 0;

        for (int i = 0; i < livingClientIds.Count; i++)
        {
            if (confirmedClientIds.Contains(livingClientIds[i]))
                confirmedLivingPlayers++;
        }
    }

    List<ulong> GetLivingClientIds()
    {
        List<ulong> livingClientIds = new List<ulong>();
        PlayerStatus[] players =
            FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);
        NetworkManager networkManager = NetworkManager.Singleton;
        bool networked = networkManager != null && networkManager.IsListening;

        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus player = players[i];
            if (player == null || player.IsDead())
                continue;

            ulong clientId = 0;
            if (networked)
            {
                if (player.NetworkObject == null || !player.NetworkObject.IsSpawned)
                    continue;

                clientId = player.NetworkObject.OwnerClientId;
            }

            if (!livingClientIds.Contains(clientId))
                livingClientIds.Add(clientId);
        }

        return livingClientIds;
    }

    bool IsLivingClientId(ulong clientId)
    {
        List<ulong> livingClientIds = GetLivingClientIds();
        return livingClientIds.Contains(clientId);
    }

    bool TryGetPlayerClientId(
        Component playerSource,
        Collider playerCollider,
        out ulong clientId)
    {
        clientId = 0;

        PlayerStatus status = null;
        if (playerSource != null)
            status = playerSource.GetComponentInParent<PlayerStatus>();
        if (status == null && playerCollider != null)
            status = playerCollider.GetComponentInParent<PlayerStatus>();

        if (status != null &&
            status.NetworkObject != null &&
            status.NetworkObject.IsSpawned)
        {
            clientId = status.NetworkObject.OwnerClientId;
            return true;
        }

        PlayerInventory inventory = null;
        if (playerSource != null)
            inventory = playerSource.GetComponentInParent<PlayerInventory>();
        if (inventory == null && playerCollider != null)
            inventory = playerCollider.GetComponentInParent<PlayerInventory>();

        if (inventory != null &&
            inventory.NetworkObject != null &&
            inventory.NetworkObject.IsSpawned)
        {
            clientId = inventory.NetworkObject.OwnerClientId;
            return true;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            clientId = networkManager.LocalClientId;
            return true;
        }

        return false;
    }

    void SendConfirmRequest(NetworkManager networkManager)
    {
        if (networkManager == null ||
            networkManager.CustomMessagingManager == null ||
            !networkManager.IsClient)
        {
            return;
        }

        using FastBufferWriter writer = new FastBufferWriter(1, Allocator.Temp);
        networkManager.CustomMessagingManager.SendNamedMessage(
            ConfirmRequestMessageName,
            NetworkManager.ServerClientId,
            writer,
            NetworkDelivery.ReliableSequenced);
    }

    void BroadcastConfirmationState(NetworkManager networkManager)
    {
        RefreshConfirmationText();

        if (networkManager == null ||
            !networkManager.IsServer ||
            networkManager.CustomMessagingManager == null)
        {
            return;
        }

        IReadOnlyList<ulong> clients = networkManager.ConnectedClientsIds;
        for (int i = 0; i < clients.Count; i++)
        {
            using FastBufferWriter writer = new FastBufferWriter(8, Allocator.Temp);
            writer.WriteValueSafe(confirmedLivingPlayers);
            writer.WriteValueSafe(requiredLivingPlayers);

            networkManager.CustomMessagingManager.SendNamedMessage(
                ConfirmStateMessageName,
                clients[i],
                writer,
                NetworkDelivery.ReliableSequenced);
        }
    }

    void TryRegisterNetworkMessages()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null ||
            !networkManager.IsListening ||
            networkManager.CustomMessagingManager == null ||
            !ShouldHandleMessagesForThisObjective())
        {
            return;
        }

        if (registeredNetworkManager != null && registeredNetworkManager != networkManager)
            UnregisterNetworkMessages();

        registeredNetworkManager = networkManager;

        if (networkManager.IsServer && !registeredConfirmRequestHandler)
        {
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(
                ConfirmRequestMessageName,
                HandleConfirmRequestMessage);
            registeredConfirmRequestHandler = true;
        }

        if (networkManager.IsClient && !registeredConfirmStateHandler)
        {
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(
                ConfirmStateMessageName,
                HandleConfirmStateMessage);
            registeredConfirmStateHandler = true;
        }

        if (networkManager.IsClient && !registeredNextPhaseStateHandler)
        {
            networkManager.CustomMessagingManager.RegisterNamedMessageHandler(
                NextPhaseStateMessageName,
                HandleNextPhaseStateMessage);
            registeredNextPhaseStateHandler = true;
        }
    }

    void UnregisterNetworkMessages()
    {
        if (registeredNetworkManager == null ||
            registeredNetworkManager.CustomMessagingManager == null)
        {
            registeredNetworkManager = null;
            registeredConfirmRequestHandler = false;
            registeredConfirmStateHandler = false;
            return;
        }

        if (registeredConfirmRequestHandler)
        {
            registeredNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(
                ConfirmRequestMessageName);
        }

        if (registeredConfirmStateHandler)
        {
            registeredNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(
                ConfirmStateMessageName);
        }

        if (registeredNextPhaseStateHandler)
        {
            registeredNetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(
                NextPhaseStateMessageName);
        }

        registeredNetworkManager = null;
        registeredConfirmRequestHandler = false;
        registeredConfirmStateHandler = false;
        registeredNextPhaseStateHandler = false;
    }

    bool ShouldHandleMessagesForThisObjective()
    {
        return !requireSpecificRoomCategory || IsInsideRequiredRoomCategory();
    }

    void HandleConfirmRequestMessage(ulong senderClientId, FastBufferReader reader)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsServer || transitionStarted)
            return;

        if (requireLevelCompleted &&
            (LevelObjectiveManager.Instance == null ||
             !LevelObjectiveManager.Instance.LevelCompleted))
        {
            return;
        }

        if (requireSpecificRoomCategory && !IsInsideRequiredRoomCategory())
            return;

        ConfirmClient(senderClientId, networkManager);
    }

    void HandleConfirmStateMessage(ulong senderClientId, FastBufferReader reader)
    {
        reader.ReadValueSafe(out confirmedLivingPlayers);
        reader.ReadValueSafe(out requiredLivingPlayers);
        RefreshConfirmationText();
    }

    void BroadcastNextPhaseState(NetworkManager networkManager)
    {
        if (networkManager == null ||
            !networkManager.IsServer ||
            networkManager.CustomMessagingManager == null)
        {
            return;
        }

        IReadOnlyList<ulong> clients = networkManager.ConnectedClientsIds;
        for (int i = 0; i < clients.Count; i++)
        {
            ulong clientId = clients[i];
            if (clientId == NetworkManager.ServerClientId)
                continue;

            using FastBufferWriter writer = new FastBufferWriter(1024, Allocator.Temp);
            writer.WriteValueSafe(ToFixedString(RegionRunState.RegionName));
            writer.WriteValueSafe(ToFixedString(RegionRunState.SceneName));
            writer.WriteValueSafe(RegionRunState.RunSeed);
            writer.WriteValueSafe(RegionRunState.PhaseNumber);
            writer.WriteValueSafe(ToFixedString(RegionRunState.PreviousSceneName));
            writer.WriteValueSafe(ToFixedString(RegionRunState.RelayJoinCode));
            writer.WriteValueSafe(ToFixedString(RegionRunState.RelayConnectionType));
            writer.WriteValueSafe(RegionRunState.RelayMaxConnections);
            writer.WriteValueSafe((int)RegionRunState.DifficultyMode);

            networkManager.CustomMessagingManager.SendNamedMessage(
                NextPhaseStateMessageName,
                clientId,
                writer,
                NetworkDelivery.ReliableSequenced);
        }
    }

    void HandleNextPhaseStateMessage(ulong senderClientId, FastBufferReader reader)
    {
        if (senderClientId != NetworkManager.ServerClientId)
            return;

        reader.ReadValueSafe(out FixedString128Bytes regionName);
        reader.ReadValueSafe(out FixedString128Bytes sceneName);
        reader.ReadValueSafe(out int runSeed);
        reader.ReadValueSafe(out int phaseNumber);
        reader.ReadValueSafe(out FixedString128Bytes previousSceneName);
        reader.ReadValueSafe(out FixedString128Bytes relayJoinCode);
        reader.ReadValueSafe(out FixedString128Bytes relayConnectionType);
        reader.ReadValueSafe(out int relayMaxConnections);
        reader.ReadValueSafe(out int difficultyIndex);

        RegionRunState.SelectSyncedMultiplayerPhase(
            regionName.ToString(),
            sceneName.ToString(),
            runSeed,
            phaseNumber,
            previousSceneName.ToString(),
            relayJoinCode.ToString(),
            relayConnectionType.ToString(),
            relayMaxConnections,
            (RunDifficulty)Mathf.Clamp(
                difficultyIndex,
                0,
                Enum.GetValues(typeof(RunDifficulty)).Length - 1));
    }

    System.Collections.IEnumerator LoadNetworkSceneAfterStateSync(
        NetworkManager networkManager,
        string sceneName,
        string reason)
    {
        yield return null;
        yield return null;

        if (networkManager == null ||
            !networkManager.IsListening ||
            networkManager.SceneManager == null)
        {
            SceneManager.LoadScene(sceneName);
            yield break;
        }

        SceneEventProgressStatus status =
            networkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogWarning(
                $"SubmarineReturnController could not load scene '{sceneName}' from {reason}. Scene event status: {status}.");
        }
    }

    FixedString128Bytes ToFixedString(string value)
    {
        return new FixedString128Bytes(
            string.IsNullOrWhiteSpace(value) ? string.Empty : value);
    }

    string GetDestinationSceneName(RunSceneOption nextScene)
    {
        if (routeThroughPhysicalShop && !string.IsNullOrWhiteSpace(shopSceneName))
            return shopSceneName;

        return nextScene != null ? nextScene.sceneName : string.Empty;
    }

    RunSceneOption ChooseNextScene()
    {
        if (sceneOptions == null || sceneOptions.Length == 0)
            return null;

        string currentScene = GetCurrentSceneName();
        string previousScene = RegionRunState.PreviousSceneName;

        float totalWeight = 0f;
        for (int i = 0; i < sceneOptions.Length; i++)
        {
            RunSceneOption option = sceneOptions[i];
            if (!CanUseSceneOption(option, currentScene, previousScene))
                continue;

            totalWeight += Mathf.Max(0f, option.weight);
        }

        if (totalWeight <= 0f)
        {
            Debug.LogWarning(
                "SubmarineReturnController could not find a next scene different from the current scene.");
            return null;
        }

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        for (int i = 0; i < sceneOptions.Length; i++)
        {
            RunSceneOption option = sceneOptions[i];
            if (!CanUseSceneOption(option, currentScene, previousScene))
                continue;

            roll -= Mathf.Max(0f, option.weight);
            if (roll <= 0f)
                return option;
        }

        return FindFirstUsableSceneOption();
    }

    bool CanUseSceneOption(
        RunSceneOption option,
        string currentScene,
        string previousScene)
    {
        if (option == null ||
            string.IsNullOrWhiteSpace(option.sceneName) ||
            option.weight <= 0f)
        {
            return false;
        }

        if (blockCurrentScene &&
            string.Equals(option.sceneName, currentScene, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (blockPreviousScene &&
            string.Equals(option.sceneName, previousScene, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    RunSceneOption FindFirstUsableSceneOption()
    {
        for (int i = 0; i < sceneOptions.Length; i++)
        {
            RunSceneOption option = sceneOptions[i];
            if (option == null ||
                string.IsNullOrWhiteSpace(option.sceneName) ||
                option.weight <= 0f)
            {
                continue;
            }

            if (CanUseSceneOption(option, GetCurrentSceneName(), RegionRunState.PreviousSceneName))
                return option;
        }

        return null;
    }

    int CreateRunSeed()
    {
        if (!useRandomSeed)
            return fixedSeed;

        unchecked
        {
            return Environment.TickCount ^
                DateTime.UtcNow.Millisecond ^
                UnityEngine.Random.Range(0, int.MaxValue);
        }
    }

    string GetCurrentSceneName()
    {
        if (RegionRunState.HasSelectedRegion &&
            !string.IsNullOrWhiteSpace(RegionRunState.SceneName))
        {
            return RegionRunState.SceneName;
        }

        return SceneManager.GetActiveScene().name;
    }

    bool IsPlayerSource(Component playerSource, Collider playerCollider)
    {
        if (playerSource != null &&
            (playerSource.GetComponentInParent<PlayerStatus>() != null ||
             playerSource.GetComponentInParent<PlayerInventory>() != null))
        {
            return true;
        }

        return IsPlayerCollider(playerCollider);
    }

    bool IsPlayerCollider(Collider other)
    {
        if (other == null)
            return false;

        return other.GetComponentInParent<PlayerStatus>() != null ||
            other.GetComponentInParent<PlayerInventory>() != null;
    }

    bool IsInsideRequiredRoomCategory()
    {
        RoomDefinition room = GetComponentInParent<RoomDefinition>();
        return room != null && room.category == requiredRoomCategory;
    }

    void EnsureTriggerCollider()
    {
        Collider existingCollider = GetComponent<Collider>();
        if (existingCollider != null)
        {
            existingCollider.isTrigger = true;
            return;
        }

        BoxCollider box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = fallbackTriggerSize;
    }

    void CacheConfirmationText()
    {
        if (confirmationText == null)
            confirmationText = GetComponentInChildren<TMP_Text>(true);

        if (confirmationText == null &&
            !string.IsNullOrWhiteSpace(confirmationTextChildName))
        {
            Transform child = transform.Find(confirmationTextChildName);
            if (child != null)
                confirmationText = child.GetComponent<TMP_Text>();
        }

        if (confirmationText == null && createConfirmationTextIfMissing)
            confirmationText = CreateConfirmationText();

        if (confirmationText == null)
            return;

        confirmationText.text = string.Empty;
        confirmationText.color = confirmationTextColor;
        confirmationText.fontSize = confirmationTextFontSize;
        confirmationText.alignment = TextAlignmentOptions.Center;
    }

    TMP_Text CreateConfirmationText()
    {
        GameObject textObject = new GameObject(confirmationTextChildName);
        Transform textTransform = textObject.transform;
        textTransform.SetParent(transform, false);
        textTransform.localPosition = confirmationTextLocalOffset;

        TextMeshPro text = textObject.AddComponent<TextMeshPro>();
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.rectTransform.sizeDelta = new Vector2(4f, 1f);
        return text;
    }

    void RefreshConfirmationText()
    {
        if (confirmationText == null)
            return;

        bool hasProgress = confirmedLivingPlayers > 0;
        bool shouldShow = !hideConfirmationTextUntilFirstConfirm || hasProgress;
        confirmationText.gameObject.SetActive(shouldShow);

        if (!shouldShow)
        {
            confirmationText.text = string.Empty;
            return;
        }

        int required = Mathf.Max(1, requiredLivingPlayers);
        confirmationText.text = $"{confirmedLivingPlayers}/{required}";
    }
}
