using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class NetworkPlayerSetup : NetworkBehaviour
{
    [Header("Owner Setup")]
    [SerializeField] private bool bindHudOnOwner = true;
    [SerializeField] private bool makeRemoteRigidbodyKinematic = true;

    [Header("Remote Disable")]
    [SerializeField] private bool disableRemotePlayerInput = true;
    [SerializeField] private bool disableRemoteWaterCannon = true;
    [SerializeField] private bool disableRemoteCameraBehaviours = true;
    [SerializeField] private bool disableRemoteAudioListeners = true;

    [Header("Optional Local Only")]
    [SerializeField] private Behaviour[] ownerOnlyBehaviours;
    [SerializeField] private GameObject[] ownerOnlyObjects;

    private PlayerMovement movement;
    private PlayerInput playerInput;
    private PlayerInventory inventory;
    private PlayerStatus playerStatus;
    private Rigidbody playerRigidbody;
    private WaterCannon[] waterCannons;
    private Camera[] cameras;
    private AudioListener[] audioListeners;
    private Behaviour[] cameraBehaviours;
    private CameraWobble[] cameraWobbles;
    private PlayerVignetteEffect[] vignetteEffects;
    private CursorLockController cursorLockController;
    private bool originalRigidbodyKinematic;
    private bool cachedReferences;
    private bool appliedOwnershipState;
    private bool lastAppliedLocalControl;

    private void Awake()
    {
        CacheReferences();
    }

    public override void OnNetworkSpawn()
    {
        CacheReferences();
        bool shouldControlLocally = ShouldControlLocally();
        ApplyOwnershipState(shouldControlLocally);

        if (shouldControlLocally && bindHudOnOwner)
            BindLocalHud();
    }

    public override void OnGainedOwnership()
    {
        ApplyOwnershipState(true);

        if (bindHudOnOwner)
            BindLocalHud();
    }

    public override void OnLostOwnership()
    {
        ApplyOwnershipState(false);
    }

    private void Start()
    {
        if (!IsSpawned)
        {
            if (!IsNetworkSessionRunning())
            {
                ApplyOwnershipState(true);

                if (bindHudOnOwner)
                    BindLocalHud();
            }

            return;
        }

        bool shouldControlLocally = ShouldControlLocally();
        ApplyOwnershipState(shouldControlLocally);

        if (shouldControlLocally && bindHudOnOwner)
            BindLocalHud();
    }

    private void Update()
    {
        if (!IsSpawned) return;

        bool shouldControlLocally = ShouldControlLocally();
        bool needsRefresh = !appliedOwnershipState ||
            shouldControlLocally != lastAppliedLocalControl ||
            shouldControlLocally && IsLocalControlIncomplete();

        if (!needsRefresh) return;

        ApplyOwnershipState(shouldControlLocally);

        if (shouldControlLocally && bindHudOnOwner)
            BindLocalHud();
    }

    private void CacheReferences()
    {
        if (cachedReferences) return;

        movement = GetComponent<PlayerMovement>();
        playerInput = GetComponent<PlayerInput>();
        inventory = GetComponent<PlayerInventory>();
        playerStatus = GetComponent<PlayerStatus>();
        playerRigidbody = GetComponent<Rigidbody>();
        waterCannons = GetComponentsInChildren<WaterCannon>(true);
        cameras = GetComponentsInChildren<Camera>(true);
        audioListeners = GetComponentsInChildren<AudioListener>(true);
        cameraBehaviours = GetComponentsInChildren<Behaviour>(true);
        cameraWobbles = GetComponentsInChildren<CameraWobble>(true);
        vignetteEffects = GetComponentsInChildren<PlayerVignetteEffect>(true);
        cursorLockController = GetComponent<CursorLockController>();

        if (playerRigidbody != null)
            originalRigidbodyKinematic = playerRigidbody.isKinematic;

        cachedReferences = true;
    }

    private void ApplyOwnershipState(bool isOwner)
    {
        CacheReferences();
        appliedOwnershipState = true;
        lastAppliedLocalControl = isOwner;

        if (movement != null)
        {
            movement.SetAcceptsInput(isOwner);
            movement.enabled = isOwner;
        }

        if (playerInput != null && disableRemotePlayerInput)
            playerInput.enabled = isOwner;

        if (playerRigidbody != null && makeRemoteRigidbodyKinematic)
            playerRigidbody.isKinematic = isOwner ? originalRigidbodyKinematic : true;

        if (disableRemoteWaterCannon)
            SetEnabled(waterCannons, isOwner);

        if (disableRemoteCameraBehaviours)
        {
            SetEnabled(cameras, isOwner);
            SetEnabled(cameraWobbles, isOwner);
            SetEnabled(vignetteEffects, isOwner);

            foreach (Behaviour behaviour in cameraBehaviours)
            {
                if (behaviour == null || behaviour == this) continue;
                if (IsCinemachineBehaviour(behaviour))
                    behaviour.enabled = isOwner;
            }
        }

        if (disableRemoteAudioListeners)
            SetEnabled(audioListeners, isOwner);

        if (cursorLockController != null)
            cursorLockController.enabled = isOwner;

        SetEnabled(ownerOnlyBehaviours, isOwner);
        SetActive(ownerOnlyObjects, isOwner);
    }

    private void BindLocalHud()
    {
        HUD hud = FindAnyObjectByType<HUD>();
        if (hud == null) return;

        hud.Bind(movement, inventory, playerStatus);
    }

    private bool ShouldControlLocally()
    {
        if (!IsSpawned)
            return !IsNetworkSessionRunning();

        if (NetworkObject == null)
            return IsOwner;

        if (!NetworkObject.IsPlayerObject)
            return false;

        NetworkObject localPlayerObject = GetLocalPlayerObject();
        return IsOwner || NetworkObject.IsLocalPlayer || localPlayerObject == NetworkObject;
    }

    private bool IsLocalControlIncomplete()
    {
        CacheReferences();

        if (movement != null && (!movement.enabled || !movement.AcceptsInput))
            return true;

        if (playerInput != null && disableRemotePlayerInput && !playerInput.enabled)
            return true;

        if (playerRigidbody != null && makeRemoteRigidbodyKinematic && playerRigidbody.isKinematic != originalRigidbodyKinematic)
            return true;

        if (disableRemoteWaterCannon && HasDisabledBehaviour(waterCannons))
            return true;

        if (disableRemoteCameraBehaviours && (HasDisabledBehaviour(cameras) || HasDisabledBehaviour(cameraWobbles) || HasDisabledBehaviour(vignetteEffects)))
            return true;

        if (disableRemoteCameraBehaviours && HasDisabledCinemachineBehaviour(cameraBehaviours))
            return true;

        if (disableRemoteAudioListeners && HasDisabledBehaviour(audioListeners))
            return true;

        return cursorLockController != null && !cursorLockController.enabled;
    }

    private static NetworkObject GetLocalPlayerObject()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || networkManager.SpawnManager == null)
            return null;

        return networkManager.SpawnManager.GetLocalPlayerObject();
    }

    private static bool IsNetworkSessionRunning()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
    }

    private static bool IsCinemachineBehaviour(Behaviour behaviour)
    {
        string namespaceName = behaviour.GetType().Namespace;
        return !string.IsNullOrEmpty(namespaceName) && namespaceName.StartsWith("Unity.Cinemachine");
    }

    private static void SetEnabled(Behaviour[] behaviours, bool value)
    {
        if (behaviours == null) return;

        foreach (Behaviour behaviour in behaviours)
        {
            if (behaviour != null)
                behaviour.enabled = value;
        }
    }

    private static bool HasDisabledBehaviour(Behaviour[] behaviours)
    {
        if (behaviours == null) return false;

        foreach (Behaviour behaviour in behaviours)
        {
            if (behaviour != null && !behaviour.enabled)
                return true;
        }

        return false;
    }

    private static bool HasDisabledCinemachineBehaviour(Behaviour[] behaviours)
    {
        if (behaviours == null) return false;

        foreach (Behaviour behaviour in behaviours)
        {
            if (behaviour != null && IsCinemachineBehaviour(behaviour) && !behaviour.enabled)
                return true;
        }

        return false;
    }

    private static void SetActive(GameObject[] objects, bool value)
    {
        if (objects == null) return;

        foreach (GameObject target in objects)
        {
            if (target != null)
                target.SetActive(value);
        }
    }
}
