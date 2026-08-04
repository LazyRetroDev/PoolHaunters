using UnityEngine;
using Unity.Netcode;

public class PlayerPetrify : NetworkBehaviour
{
    public float petrifyDuration = 10f;

    [Header("Camera Effects")]
    public PlayerVignetteEffect cameraEffects;
    [Range(0f, 1f)] public float petrifiedVignetteIntensity = 0.7f;
    [Range(0f, 1f)] public float petrifyPulseIntensity = 0.9f;
    public float petrifyPulseDuration = 0.4f;
    public float petrifyShakeAmplitude = 0.8f;
    public float petrifyShakeFrequency = 13f;
    public float petrifyShakeDuration = 0.3f;

    private bool isPetrified = false;
    private float petrifyTimer;

    private PlayerMovement movement;
    private PlayerInventory inventory;
    private PlayerStatus playerStatus;
    private CursorLockController cursorLockController;
    private bool controlLockApplied;

    private readonly NetworkVariable<bool> syncedPetrified =
        new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    void Awake()
    {
        CacheReferences();
    }

    public override void OnNetworkSpawn()
    {
        CacheReferences();
        syncedPetrified.OnValueChanged += HandlePetrifiedChanged;

        if (syncedPetrified.Value)
            ApplyPetrifyState(petrifyDuration, false, false);
    }

    public override void OnNetworkDespawn()
    {
        syncedPetrified.OnValueChanged -= HandlePetrifiedChanged;
    }

    void Start()
    {
        CacheReferences();
        ResolveCameraEffects();
    }

    void Update()
    {
        if (!isPetrified) return;

        ApplyPetrifiedPresentation();

        if (IsNetworked() && !IsServer)
            return;

        petrifyTimer -= Time.deltaTime;
        if (petrifyTimer <= 0f)
            Unpetrify();
    }

    public bool IsPetrified()
    {
        return isPetrified;
    }

    public void Petrify()
    {
        if (IsNetworked() && !IsServer)
            return;

        ApplyPetrifyState(petrifyDuration, true, true);
        SyncPetrifiedState(true);
    }

    public void Unpetrify()
    {
        if (IsNetworked() && !IsServer)
            return;

        ClearPetrifyState(true);
        SyncPetrifiedState(false);
    }

    void CacheReferences()
    {
        if (movement == null)
            movement = GetComponent<PlayerMovement>();
        if (inventory == null)
            inventory = GetComponent<PlayerInventory>();
        if (playerStatus == null)
            playerStatus = GetComponent<PlayerStatus>();
        if (cursorLockController == null)
            cursorLockController = GetComponent<CursorLockController>();
    }

    void HandlePetrifiedChanged(bool previous, bool next)
    {
        if (IsServer)
            return;

        if (next)
            ApplyPetrifyState(petrifyDuration, true, false);
        else
            ClearPetrifyState(true);
    }

    void ApplyPetrifyState(
        float duration,
        bool playEffects,
        bool writeControlLock)
    {
        bool wasPetrified = isPetrified;
        isPetrified = true;
        petrifyTimer = Mathf.Max(0f, duration);
        CacheReferences();

        if (writeControlLock && !wasPetrified)
            ApplyServerControlLock();

        if (playEffects)
            PlayPetrifyPulse();

        ApplyPetrifiedPresentation();
    }

    void ClearPetrifyState(bool clearEffects)
    {
        isPetrified = false;
        petrifyTimer = 0f;

        RemoveServerControlLock();
        RestoreLocalControl();

        if (clearEffects && cameraEffects != null)
        {
            cameraEffects.ClearThreatIntensity();
            cameraEffects.StopShake();
        }
    }

    void ApplyServerControlLock()
    {
        if (!CanWritePetrifyState() || controlLockApplied)
            return;

        CacheReferences();
        if (playerStatus == null)
            return;

        playerStatus.AddExternalControlLock();
        controlLockApplied = true;
    }

    void RemoveServerControlLock()
    {
        if (!CanWritePetrifyState() || !controlLockApplied)
            return;

        CacheReferences();
        if (playerStatus != null)
            playerStatus.RemoveExternalControlLock();

        controlLockApplied = false;
    }

    void ApplyPetrifiedPresentation()
    {
        ResolveCameraEffects();

        if (ShouldApplyOwnerLocalState() && cameraEffects != null)
            cameraEffects.SetThreatIntensity(petrifiedVignetteIntensity);

        ApplyPetrifiedControlLock();
    }

    void PlayPetrifyPulse()
    {
        if (!ShouldApplyOwnerLocalState())
            return;

        ResolveCameraEffects();
        if (cameraEffects == null)
            return;

        cameraEffects.Pulse(petrifyPulseIntensity, petrifyPulseDuration);
        cameraEffects.Shake(
            petrifyShakeAmplitude,
            petrifyShakeFrequency,
            petrifyShakeDuration);
        cameraEffects.SetThreatIntensity(petrifiedVignetteIntensity);
    }

    void ResolveCameraEffects()
    {
        if (cameraEffects != null) return;
        cameraEffects = FindAnyObjectByType<PlayerVignetteEffect>();
    }

    void ApplyPetrifiedControlLock()
    {
        if (!ShouldApplyOwnerLocalState())
            return;

        CacheReferences();
        if (movement != null) movement.enabled = false;
        if (inventory != null) inventory.enabled = false;
    }

    void RestoreLocalControl()
    {
        if (!ShouldApplyOwnerLocalState())
            return;

        CacheReferences();
        if (movement != null)
            movement.enabled = true;
        if (inventory != null)
            inventory.enabled = playerStatus == null || playerStatus.CanAct();
        if (cursorLockController != null)
            cursorLockController.ForceLockCursor();
    }

    void SyncPetrifiedState(bool value)
    {
        if (IsSpawned && IsServer && syncedPetrified.Value != value)
            syncedPetrified.Value = value;
    }

    bool CanWritePetrifyState()
    {
        return !IsNetworked() || IsServer;
    }

    bool ShouldApplyOwnerLocalState()
    {
        return !IsNetworked() || IsOwner;
    }

    bool IsNetworked()
    {
        return IsSpawned &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening;
    }
}
