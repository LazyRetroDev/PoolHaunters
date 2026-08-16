using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-50)]
public sealed class VivoxVoiceChatManager : MonoBehaviour
{
    private const string ResourcesPrefabName = "VivoxVoiceChatManager";

    private static VivoxVoiceChatManager instance;
    private static Task servicesInitializationTask;

    [Header("Startup")]
    [SerializeField] private bool autoStartInMultiplayer = true;
    [SerializeField] private bool autoStartInSinglePlayer = true;
    [SerializeField] private bool logStatus = true;
    [SerializeField] private bool leaveChannelOnDestroy = true;

    [Header("3D Voice")]
    [SerializeField] private int audibleDistance = 18;
    [SerializeField] private int conversationalDistance = 2;
    [SerializeField] private float audioFadeIntensity = 1.35f;
    [SerializeField] private AudioFadeModel audioFadeModel = AudioFadeModel.ExponentialByDistance;
    [SerializeField] private float positionUpdateInterval = 0.3f;
    [SerializeField] private bool allowStereoPanning = true;

    [Header("Microphone")]
    [SerializeField] private bool muteInputAtStart;
    [SerializeField] private int inputVolume = 0;
    [SerializeField] private int outputVolume = 0;

    [Header("Enemy Hearing")]
    [SerializeField] private bool emitVoiceNoiseForEnemies = true;
    [SerializeField, Min(0f)] private float voiceNoiseRadius = 12f;
    [SerializeField, Min(0.05f)] private float voiceNoiseInterval = 0.45f;

    private string activeChannelName = string.Empty;
    private bool isStarting;
    private bool isConnected;
    private bool isQuitting;
    private bool subscribedToVivoxParticipantEvents;
    private bool localSpeechDetected;
    private float nextPositionUpdateTime;
    private float nextVoiceNoiseTime;
    private GameObject cachedLocalPlayerObject;
    private readonly List<VivoxParticipant> subscribedParticipants =
        new List<VivoxParticipant>();

    public static bool IsConnected
    {
        get { return instance != null && instance.isConnected; }
    }

    public static string ActiveChannelName
    {
        get { return instance != null ? instance.activeChannelName : string.Empty; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateRuntimeInstance()
    {
        if (FindAnyObjectByType<VivoxVoiceChatManager>(FindObjectsInactive.Include) != null)
            return;

        GameObject prefab = Resources.Load<GameObject>(ResourcesPrefabName);
        if (prefab != null)
        {
            Instantiate(prefab);
            return;
        }

        GameObject managerObject = new GameObject("VivoxVoiceChatManager");
        managerObject.AddComponent<VivoxVoiceChatManager>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;

        if (instance == this)
            instance = null;

        if (!isQuitting && leaveChannelOnDestroy)
            _ = StopVoiceChatAsync();
    }

    private void OnApplicationQuit()
    {
        isQuitting = true;
        _ = StopVoiceChatAsync();
    }

    private void Update()
    {
        if (!autoStartInMultiplayer && !autoStartInSinglePlayer)
            return;

        if (!ShouldVoiceChatBeRunning())
        {
            if (isConnected && !isStarting)
                _ = StopVoiceChatAsync();
            return;
        }

        string desiredChannelName = BuildVoiceChannelName();
        if (isConnected &&
            !string.Equals(activeChannelName, desiredChannelName, StringComparison.Ordinal))
        {
            _ = RestartVoiceChatAsync();
            return;
        }

        if (!isConnected && !isStarting)
        {
            _ = StartVoiceChatAsync();
            return;
        }

        UpdateVivox3DPosition();
        UpdateVoiceNoiseEmission();
    }

    public static void SetInputMuted(bool muted)
    {
        if (VivoxService.Instance == null)
            return;

        if (muted)
            VivoxService.Instance.MuteInputDevice();
        else
            VivoxService.Instance.UnmuteInputDevice();
    }

    public static void SetOutputMuted(bool muted)
    {
        if (VivoxService.Instance == null)
            return;

        if (muted)
            VivoxService.Instance.MuteOutputDevice();
        else
            VivoxService.Instance.UnmuteOutputDevice();
    }

    public async Task StartVoiceChatAsync()
    {
        if (isStarting || isConnected)
            return;

        string channelName = BuildVoiceChannelName();
        if (string.IsNullOrWhiteSpace(channelName))
            return;

        isStarting = true;

        try
        {
            await EnsureUnityServicesSignedInAsync();
            await EnsureVivoxInitializedAsync();
            await EnsureVivoxLoggedInAsync();

            Channel3DProperties channel3DProperties = new Channel3DProperties(
                Mathf.Max(1, audibleDistance),
                Mathf.Clamp(conversationalDistance, 0, Mathf.Max(1, audibleDistance)),
                Mathf.Max(0f, audioFadeIntensity),
                audioFadeModel);

            ChannelOptions channelOptions = new ChannelOptions
            {
                MakeActiveChannelUponJoining = true
            };

            activeChannelName = channelName;
            await VivoxService.Instance.JoinPositionalChannelAsync(
                activeChannelName,
                ChatCapability.AudioOnly,
                channel3DProperties,
                channelOptions);
            await VivoxService.Instance.SetChannelTransmissionModeAsync(
                TransmissionMode.Single,
                activeChannelName);

            VivoxService.Instance.SetInputDeviceVolume(Mathf.Clamp(inputVolume, -50, 50));
            VivoxService.Instance.SetOutputDeviceVolume(Mathf.Clamp(outputVolume, -50, 50));
            SetInputMuted(muteInputAtStart);

            isConnected = true;
            cachedLocalPlayerObject = null;
            localSpeechDetected = false;
            nextPositionUpdateTime = 0f;
            nextVoiceNoiseTime = 0f;
            RegisterVivoxParticipantEvents();
            BindExistingVivoxParticipants();
            UpdateVivox3DPosition(force: true);

            Log($"Vivox voice connected to positional channel '{activeChannelName}'.");
        }
        catch (Exception exception)
        {
            activeChannelName = string.Empty;
            isConnected = false;
            Debug.LogWarning($"Vivox voice chat failed to start: {exception.Message}");
        }
        finally
        {
            isStarting = false;
        }
    }

    public async Task StopVoiceChatAsync()
    {
        if (VivoxService.Instance == null)
            return;

        string channelToLeave = activeChannelName;
        activeChannelName = string.Empty;
        isConnected = false;
        localSpeechDetected = false;
        cachedLocalPlayerObject = null;
        UnregisterVivoxParticipantEvents();

        try
        {
            if (!string.IsNullOrWhiteSpace(channelToLeave) &&
                VivoxService.Instance.ActiveChannels.ContainsKey(channelToLeave))
            {
                await VivoxService.Instance.LeaveChannelAsync(channelToLeave);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"Vivox voice chat failed to leave channel '{channelToLeave}': {exception.Message}");
        }
    }

    private async Task RestartVoiceChatAsync()
    {
        if (isStarting)
            return;

        await StopVoiceChatAsync();
        await StartVoiceChatAsync();
    }

    private static async Task EnsureUnityServicesSignedInAsync()
    {
        if (servicesInitializationTask == null)
            servicesInitializationTask = InitializeUnityServicesAsync();

        await servicesInitializationTask;
    }

    private static async Task InitializeUnityServicesAsync()
    {
        if (UnityServices.State != ServicesInitializationState.Initialized)
            await UnityServices.InitializeAsync();

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
    }

    private static async Task EnsureVivoxInitializedAsync()
    {
        if (VivoxService.Instance == null)
            throw new InvalidOperationException("VivoxService.Instance is null. Check Project Settings > Services > Vivox.");

        if (VivoxService.Instance.InitializationState == VivoxInitializationState.Initialized)
            return;

        await VivoxService.Instance.InitializeAsync();
    }

    private static async Task EnsureVivoxLoggedInAsync()
    {
        if (VivoxService.Instance.IsLoggedIn)
            return;

        LoginOptions loginOptions = new LoginOptions
        {
            DisplayName = GetPlayerDisplayName(),
            ParticipantUpdateFrequency = ParticipantPropertyUpdateFrequency.FivePerSecond
        };

        await VivoxService.Instance.LoginAsync(loginOptions);
    }

    private void UpdateVivox3DPosition(bool force = false)
    {
        if (!isConnected || string.IsNullOrWhiteSpace(activeChannelName))
            return;
        if (!force && Time.unscaledTime < nextPositionUpdateTime)
            return;

        GameObject localPlayerObject = GetLocalPlayerObject();
        if (localPlayerObject == null)
            return;

        cachedLocalPlayerObject = localPlayerObject;
        VivoxService.Instance.Set3DPosition(
            localPlayerObject,
            activeChannelName,
            allowStereoPanning);

        nextPositionUpdateTime = Time.unscaledTime + Mathf.Max(0.1f, positionUpdateInterval);
    }

    private void UpdateVoiceNoiseEmission()
    {
        if (!emitVoiceNoiseForEnemies || !isConnected || !localSpeechDetected)
            return;
        if (Time.unscaledTime < nextVoiceNoiseTime)
            return;

        EmitLocalVoiceNoise();
    }

    private void EmitLocalVoiceNoise()
    {
        nextVoiceNoiseTime =
            Time.unscaledTime + Mathf.Max(0.05f, voiceNoiseInterval);

        GameObject localPlayerObject = GetLocalPlayerObject();
        if (localPlayerObject == null)
            return;

        PlayerMovement movement =
            localPlayerObject.GetComponent<PlayerMovement>();
        if (movement != null)
        {
            movement.EmitVoiceNoiseForEnemies(voiceNoiseRadius);
            return;
        }

        NoiseEvent.Emit(
            localPlayerObject.transform.position,
            voiceNoiseRadius,
            localPlayerObject);
    }

    private void RegisterVivoxParticipantEvents()
    {
        if (subscribedToVivoxParticipantEvents || VivoxService.Instance == null)
            return;

        VivoxService.Instance.ParticipantAddedToChannel += HandleParticipantAdded;
        VivoxService.Instance.ParticipantRemovedFromChannel += HandleParticipantRemoved;
        subscribedToVivoxParticipantEvents = true;
    }

    private void UnregisterVivoxParticipantEvents()
    {
        if (subscribedToVivoxParticipantEvents && VivoxService.Instance != null)
        {
            VivoxService.Instance.ParticipantAddedToChannel -= HandleParticipantAdded;
            VivoxService.Instance.ParticipantRemovedFromChannel -= HandleParticipantRemoved;
        }

        subscribedToVivoxParticipantEvents = false;
        for (int i = subscribedParticipants.Count - 1; i >= 0; i--)
        {
            UnsubscribeParticipant(subscribedParticipants[i]);
        }

        subscribedParticipants.Clear();
    }

    private void BindExistingVivoxParticipants()
    {
        if (VivoxService.Instance == null ||
            VivoxService.Instance.ActiveChannels == null ||
            string.IsNullOrWhiteSpace(activeChannelName) ||
            !VivoxService.Instance.ActiveChannels.TryGetValue(
                activeChannelName,
                out var participants))
        {
            return;
        }

        for (int i = 0; i < participants.Count; i++)
        {
            TrySubscribeParticipant(participants[i]);
        }
    }

    private void HandleParticipantAdded(VivoxParticipant participant)
    {
        TrySubscribeParticipant(participant);
    }

    private void HandleParticipantRemoved(VivoxParticipant participant)
    {
        UnsubscribeParticipant(participant);
    }

    private void TrySubscribeParticipant(VivoxParticipant participant)
    {
        if (participant == null ||
            !string.Equals(participant.ChannelName, activeChannelName, StringComparison.Ordinal) ||
            subscribedParticipants.Contains(participant))
        {
            return;
        }

        participant.ParticipantSpeechDetected += HandleParticipantSpeechDetected;
        subscribedParticipants.Add(participant);

        if (participant.IsSelf)
            UpdateLocalSpeechState(participant);
    }

    private void UnsubscribeParticipant(VivoxParticipant participant)
    {
        if (participant == null)
            return;

        participant.ParticipantSpeechDetected -= HandleParticipantSpeechDetected;
        subscribedParticipants.Remove(participant);
    }

    private void HandleParticipantSpeechDetected()
    {
        for (int i = 0; i < subscribedParticipants.Count; i++)
        {
            VivoxParticipant participant = subscribedParticipants[i];
            if (participant != null && participant.IsSelf)
            {
                UpdateLocalSpeechState(participant);
                return;
            }
        }
    }

    private void UpdateLocalSpeechState(VivoxParticipant participant)
    {
        bool nextSpeechDetected =
            participant != null &&
            participant.IsSelf &&
            participant.SpeechDetected &&
            !participant.IsMuted;

        if (localSpeechDetected == nextSpeechDetected)
            return;

        localSpeechDetected = nextSpeechDetected;
        if (localSpeechDetected)
        {
            nextVoiceNoiseTime = 0f;
            EmitLocalVoiceNoise();
        }
    }

    private bool ShouldVoiceChatBeRunning()
    {
        if (RegionRunState.IsSinglePlayer)
        {
            if (!autoStartInSinglePlayer)
                return false;

            if (RegionRunState.HasSelectedRegion &&
                !string.IsNullOrWhiteSpace(RegionRunState.SceneName))
            {
                return SceneManager.GetActiveScene().name ==
                    RegionRunState.SceneName;
            }

            return FindOfflinePlayerObject() != null;
        }

        if (!autoStartInMultiplayer)
            return false;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return false;

        if (!RegionRunState.HasSelectedRegion ||
            string.IsNullOrWhiteSpace(RegionRunState.SceneName))
        {
            return false;
        }

        return SceneManager.GetActiveScene().name == RegionRunState.SceneName;
    }

    private static GameObject GetLocalPlayerObject()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || networkManager.SpawnManager == null)
            return FindOfflinePlayerObject();

        NetworkObject localPlayerObject =
            networkManager.SpawnManager.GetLocalPlayerObject();
        if (localPlayerObject != null)
            return localPlayerObject.gameObject;

        return FindOfflinePlayerObject();
    }

    private static GameObject FindOfflinePlayerObject()
    {
        if (instance != null && instance.cachedLocalPlayerObject != null)
            return instance.cachedLocalPlayerObject;

        PlayerMovement movement = FindAnyObjectByType<PlayerMovement>(
            FindObjectsInactive.Exclude);
        if (movement != null)
            return movement.gameObject;

        PlayerStatus status = FindAnyObjectByType<PlayerStatus>(
            FindObjectsInactive.Exclude);
        return status != null ? status.gameObject : null;
    }

    private static string BuildVoiceChannelName()
    {
        if (RegionRunState.UsesRelay &&
            !string.IsNullOrWhiteSpace(RegionRunState.RelayJoinCode))
        {
            return SanitizeVivoxName(
                $"PoolHaunters_{RegionRunState.RelayJoinCode}_P{RegionRunState.PhaseNumber}");
        }

        string sceneName = string.IsNullOrWhiteSpace(RegionRunState.SceneName)
            ? SceneManager.GetActiveScene().name
            : RegionRunState.SceneName;

        return SanitizeVivoxName(
            $"PoolHaunters_{sceneName}_{RegionRunState.RunSeed}_P{RegionRunState.PhaseNumber}");
    }

    private static string GetPlayerDisplayName()
    {
        return string.IsNullOrWhiteSpace(RegionRunState.PlayerName)
            ? "Player"
            : RegionRunState.PlayerName.Trim();
    }

    private static string SanitizeVivoxName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "PoolHaunters";

        StringBuilder builder = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            char character = value[i];
            if (char.IsLetterOrDigit(character) || character == '_' || character == '-')
                builder.Append(character);
            else
                builder.Append('_');
        }

        string sanitized = builder.ToString().Trim('_');
        if (string.IsNullOrWhiteSpace(sanitized))
            sanitized = "PoolHaunters";

        return sanitized.Length <= 80
            ? sanitized
            : sanitized.Substring(0, 80);
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        cachedLocalPlayerObject = null;
        nextPositionUpdateTime = 0f;
    }

    private void Log(string message)
    {
        if (logStatus)
            Debug.Log(message);
    }
}
