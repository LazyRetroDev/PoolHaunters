using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using System.Collections.Generic;

public class PlayerMovement : NetworkBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 9f;
    public float knockedOutCrawlSpeed = 1.35f;
    public bool IsMoving() => acceptsInput && moveInput != Vector2.zero;

    [Header("Jump")]
    public float jumpVelocity = 5.5f;
    public float groundCheckRadius = 0.4f;
    public float groundCheckDistance = 0.12f;
    public LayerMask groundLayers = ~0;

    [Header("Crouch")]
    public float crouchSpeed = 2.5f;
    [Range(0.5f, 2f)] public float crouchColliderHeight = 1f;
    public Transform crouchView;
    public float crouchViewOffset = 0.65f;
    public float crouchTransitionSpeed = 10f;
    public bool IsCrouching() => isCrouching;

    [Header("Stamina")]
    public float maxStamina = 100f;
    public float staminaDrainRate = 20f;
    public float staminaRegenRate = 10f;
    public float staminaRegenDelay = 1.5f;
    public bool IsSprinting() => acceptsInput && CanSprintNow();

    [Header("Footstep Noise")]
    public float walkNoiseRadius = 4f;
    public float sprintNoiseRadius = 10f;
    public float crouchNoiseRadius = 1.5f;
    public float walkStepInterval = 0.55f;
    public float sprintStepInterval = 0.32f;
    public float crouchStepInterval = 0.8f;

    [Header("Footstep Audio")]
    public AudioSource footstepAudioSource;
    public AudioClip walkFootstepClip;
    public AudioClip sprintFootstepClip;
    [Range(0f, 1f)] public float footstepVolume = 0.75f;
    [Range(0f, 1f)] public float crouchFootstepVolumeMultiplier = 0.45f;

    private Rigidbody rb;
    private CapsuleCollider bodyCollider;
    private PlayerInput playerInput;
    private PlayerStatus playerStatus;
    private Vector2 moveInput;
    private bool isSprinting;
    private bool crouchRequested;
    private bool isCrouching;
    private bool jumpRequested;
    private float standingColliderHeight;
    private Vector3 standingColliderCenter;
    private Vector3 standingViewLocalPosition;
    private float currentStamina;
    private float regenTimer;
    private float staminaDrainMultiplier = 1f;
    private float staminaDrainMultiplierTimer;
    private float footstepTimer;
    private bool acceptsInput = true;
    private PlayerVignetteEffect localVignetteEffect;
    private float lastThreatEffectSendTime = -999f;
    private float lastThreatEffectSentIntensity = -1f;
    private float lastShakeEffectSendTime = -999f;
    private bool threatEffectKnownClear = true;
    private const float ThreatEffectSendInterval = 0.08f;
    private const float ThreatEffectIntensityEpsilon = 0.02f;
    public bool AcceptsInput => acceptsInput;

    public void SetAcceptsInput(bool value)
    {
        acceptsInput = value;

        if (!acceptsInput)
        {
            moveInput = Vector2.zero;
            isSprinting = false;
            crouchRequested = false;
            jumpRequested = false;
            footstepTimer = 0f;

            if (footstepAudioSource != null)
                footstepAudioSource.Stop();
        }
    }

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        bodyCollider = GetComponent<CapsuleCollider>();
        playerInput = GetComponent<PlayerInput>();
        playerStatus = GetComponent<PlayerStatus>();
        currentStamina = maxStamina;

        if (bodyCollider != null)
        {
            standingColliderHeight = bodyCollider.height;
            standingColliderCenter = bodyCollider.center;
            crouchColliderHeight = Mathf.Clamp(
                crouchColliderHeight,
                bodyCollider.radius * 2f,
                standingColliderHeight);
        }

        if (crouchView == null && Camera.main != null &&
            Camera.main.transform.IsChildOf(transform))
        {
            crouchView = Camera.main.transform;
        }

        if (crouchView != null)
            standingViewLocalPosition = crouchView.localPosition;

        if (footstepAudioSource == null)
            footstepAudioSource = GetComponent<AudioSource>();
    }

    public void OnMove(InputValue value)
    {
        if (!acceptsInput)
        {
            moveInput = Vector2.zero;
            return;
        }

        moveInput = value.Get<Vector2>();
    }

    public void OnCrouch(InputValue value)
    {
        if (!acceptsInput || !value.isPressed)
            return;

        crouchRequested = !crouchRequested;
    }

    public void OnJump(InputValue value)
    {
        if (!acceptsInput || !value.isPressed)
            return;

        jumpRequested = true;
    }

    void Update()
    {
        if (!acceptsInput) return;

        UpdateTimedStaminaMultiplier();
        UpdateFootstepNoise();
        UpdateCrouchView();
    }

    void FixedUpdate()
    {
        if (!acceptsInput) return;
        if (playerStatus != null && playerStatus.IsDead()) return;
        if (playerStatus != null && playerStatus.IsTransformed()) return;

        bool knockedOut = playerStatus != null && playerStatus.IsKnockedOut();
        UpdateCrouchState(knockedOut);
        TryJump(knockedOut);

        Camera movementCamera = Camera.main;
        if (movementCamera == null) return;

        Vector3 camForward = movementCamera.transform.forward;
        Vector3 camRight = movementCamera.transform.right;
        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        isSprinting = !knockedOut && !isCrouching &&
            playerInput != null && playerInput.actions["Sprint"].IsPressed();
        bool canSprint = !knockedOut && isSprinting &&
            currentStamina > 0f && moveInput != Vector2.zero;
        float speed = knockedOut
            ? knockedOutCrawlSpeed
            : isCrouching
                ? crouchSpeed
                : canSprint ? sprintSpeed : walkSpeed;

        Vector3 move = camForward * moveInput.y + camRight * moveInput.x;
        rb.MovePosition(rb.position + move * speed * Time.fixedDeltaTime);

        Quaternion targetRotation = Quaternion.Euler(
            0f,
            movementCamera.transform.eulerAngles.y,
            0f);
        rb.MoveRotation(targetRotation);

        if (knockedOut)
        {
            regenTimer = 0f;
            return;
        }

        if (canSprint)
        {
            currentStamina -= staminaDrainRate * staminaDrainMultiplier *
                Time.fixedDeltaTime;
            currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);
            regenTimer = 0f;
        }
        else
        {
            regenTimer += Time.fixedDeltaTime;
            if (regenTimer >= staminaRegenDelay)
            {
                currentStamina += staminaRegenRate * Time.fixedDeltaTime;
                currentStamina = Mathf.Clamp(currentStamina, 0f, maxStamina);
            }
        }
    }

    void UpdateCrouchState(bool knockedOut)
    {
        bool shouldCrouch = !knockedOut && crouchRequested;

        if (!shouldCrouch && isCrouching && !HasStandingClearance())
            shouldCrouch = true;

        if (shouldCrouch == isCrouching)
            return;

        isCrouching = shouldCrouch;
        ApplyColliderHeight();
    }

    void ApplyColliderHeight()
    {
        if (bodyCollider == null)
            return;

        float heightDifference = standingColliderHeight - crouchColliderHeight;
        bodyCollider.height = isCrouching
            ? crouchColliderHeight
            : standingColliderHeight;
        bodyCollider.center = isCrouching
            ? standingColliderCenter - Vector3.up * (heightDifference * 0.5f)
            : standingColliderCenter;
    }

    bool HasStandingClearance()
    {
        if (bodyCollider == null)
            return true;

        float radius = GetWorldColliderRadius();
        float halfSegment = Mathf.Max(0f, standingColliderHeight * 0.5f -
            bodyCollider.radius);
        Vector3 center = transform.TransformPoint(standingColliderCenter);
        Vector3 up = transform.up;
        Collider[] overlaps = Physics.OverlapCapsule(
            center - up * halfSegment,
            center + up * halfSegment,
            radius,
            groundLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < overlaps.Length; i++)
        {
            if (overlaps[i] != null &&
                !overlaps[i].transform.IsChildOf(transform))
            {
                return false;
            }
        }

        return true;
    }

    void TryJump(bool knockedOut)
    {
        if (!jumpRequested)
            return;

        jumpRequested = false;
        if (knockedOut || rb == null || !IsGrounded())
            return;

        if (isCrouching)
        {
            crouchRequested = false;
            if (!HasStandingClearance())
                return;

            isCrouching = false;
            ApplyColliderHeight();
        }

        Vector3 velocity = rb.linearVelocity;
        velocity.y = jumpVelocity;
        rb.linearVelocity = velocity;
    }

    bool IsGrounded()
    {
        if (bodyCollider == null)
            return Physics.Raycast(
                transform.position,
                Vector3.down,
                groundCheckDistance + 0.1f,
                groundLayers,
                QueryTriggerInteraction.Ignore);

        float radius = Mathf.Min(groundCheckRadius, GetWorldColliderRadius());
        Vector3 bottom = transform.TransformPoint(
            bodyCollider.center - Vector3.up * (bodyCollider.height * 0.5f));
        Vector3 checkPosition = bottom + transform.up * groundCheckDistance;
        Collider[] overlaps = Physics.OverlapSphere(
            checkPosition,
            radius,
            groundLayers,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < overlaps.Length; i++)
        {
            if (overlaps[i] != null &&
                !overlaps[i].transform.IsChildOf(transform))
            {
                return true;
            }
        }

        return false;
    }

    float GetWorldColliderRadius()
    {
        Vector3 scale = bodyCollider.transform.lossyScale;
        return bodyCollider.radius * Mathf.Max(
            Mathf.Abs(scale.x),
            Mathf.Abs(scale.z));
    }

    void UpdateCrouchView()
    {
        if (crouchView == null)
            return;

        Vector3 target = standingViewLocalPosition;
        if (isCrouching)
            target.y -= crouchViewOffset;

        crouchView.localPosition = Vector3.Lerp(
            crouchView.localPosition,
            target,
            crouchTransitionSpeed * Time.deltaTime);
    }

    bool CanSprintNow()
    {
        if (!acceptsInput || isCrouching) return false;
        if (playerStatus != null && !playerStatus.CanAct()) return false;
        if (playerInput == null) return false;
        return playerInput.actions["Sprint"].IsPressed() &&
            currentStamina > 0f;
    }

    void UpdateFootstepNoise()
    {
        if (playerStatus != null && playerStatus.IsKnockedOut())
        {
            footstepTimer = 0f;
            return;
        }

        bool moving = moveInput != Vector2.zero;
        if (!moving)
        {
            footstepTimer = 0f;
            return;
        }

        bool sprintingNow = IsSprinting();
        float interval = isCrouching
            ? crouchStepInterval
            : sprintingNow ? sprintStepInterval : walkStepInterval;
        footstepTimer -= Time.deltaTime;

        if (footstepTimer > 0f) return;

        footstepTimer = interval;
        float noiseRadius = isCrouching
            ? crouchNoiseRadius
            : sprintingNow ? sprintNoiseRadius : walkNoiseRadius;
        EmitFootstepNoise(noiseRadius);
        PlayFootstepAudio(sprintingNow);
    }

    void EmitFootstepNoise(float noiseRadius)
    {
        if (IsSpawned && !IsServer)
        {
            EmitFootstepNoiseServerRpc(transform.position, noiseRadius);
            return;
        }

        NoiseEvent.Emit(transform.position, noiseRadius, gameObject);
    }

    [ServerRpc]
    void EmitFootstepNoiseServerRpc(Vector3 noisePosition, float noiseRadius)
    {
        NoiseEvent.Emit(noisePosition, noiseRadius, gameObject);
    }

    public void RequestApplyWaterToGoldenMouth(
        GoldenMouthBehavior target,
        WaterQuality quality,
        float amount)
    {
        if (target == null || amount <= 0f)
            return;

        if (IsSpawned && !IsServer)
        {
            NetworkObjectReference targetReference;
            if (TryGetNetworkObjectReference(target.gameObject, out targetReference))
                ApplyWaterToGoldenMouthServerRpc(targetReference, (int)quality, amount);

            return;
        }

        target.ApplyWater(quality, amount);
    }

    public void RequestEnemyWaterHit(GameObject target, Vector3 sourcePosition)
    {
        if (target == null)
            return;

        if (IsSpawned && !IsServer)
        {
            NetworkObjectReference targetReference;
            if (TryGetNetworkObjectReference(target, out targetReference))
                EnemyWaterHitServerRpc(targetReference, sourcePosition);

            return;
        }

        ApplyEnemyWaterHit(target, sourcePosition);
    }

    [ServerRpc]
    void ApplyWaterToGoldenMouthServerRpc(
        NetworkObjectReference targetReference,
        int quality,
        float amount)
    {
        NetworkObject targetObject;
        if (!targetReference.TryGet(out targetObject))
            return;

        GoldenMouthBehavior target =
            targetObject.GetComponentInChildren<GoldenMouthBehavior>(true);
        if (target != null)
            target.ApplyWater((WaterQuality)quality, amount);
    }

    [ServerRpc]
    void EnemyWaterHitServerRpc(
        NetworkObjectReference targetReference,
        Vector3 sourcePosition)
    {
        NetworkObject targetObject;
        if (!targetReference.TryGet(out targetObject))
            return;

        ApplyEnemyWaterHit(targetObject.gameObject, sourcePosition);
    }

    public void PublishWaterSprayVisual(
        bool isSpraying,
        WaterQuality quality,
        Vector3 originPosition,
        Quaternion originRotation)
    {
        if (!IsSpawned)
            return;

        if (IsServer)
        {
            BroadcastWaterSprayVisual(
                isSpraying,
                quality,
                originPosition,
                originRotation);
            return;
        }

        PublishWaterSprayVisualServerRpc(
            isSpraying,
            (int)quality,
            originPosition,
            originRotation);
    }

    [ServerRpc]
    void PublishWaterSprayVisualServerRpc(
        bool isSpraying,
        int quality,
        Vector3 originPosition,
        Quaternion originRotation)
    {
        BroadcastWaterSprayVisual(
            isSpraying,
            (WaterQuality)quality,
            originPosition,
            originRotation);
    }

    void BroadcastWaterSprayVisual(
        bool isSpraying,
        WaterQuality quality,
        Vector3 originPosition,
        Quaternion originRotation)
    {
        ClientRpcParams clientRpcParams;
        if (!TryBuildNonOwnerClientRpcParams(out clientRpcParams))
            return;

        ApplyRemoteWaterSprayVisualClientRpc(
            isSpraying,
            (int)quality,
            originPosition,
            originRotation,
            clientRpcParams);
    }

    [ClientRpc]
    void ApplyRemoteWaterSprayVisualClientRpc(
        bool isSpraying,
        int quality,
        Vector3 originPosition,
        Quaternion originRotation,
        ClientRpcParams clientRpcParams = default)
    {
        if (IsOwner)
            return;

        WaterCannon waterCannon = GetComponentInChildren<WaterCannon>(true);
        if (waterCannon != null)
        {
            waterCannon.ApplyRemoteSprayVisual(
                isSpraying,
                (WaterQuality)quality,
                originPosition,
                originRotation);
        }
    }

    bool TryBuildNonOwnerClientRpcParams(out ClientRpcParams clientRpcParams)
    {
        clientRpcParams = default;

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
            return false;

        List<ulong> targetClientIds = new List<ulong>();
        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            if (clientId != OwnerClientId)
                targetClientIds.Add(clientId);
        }

        if (targetClientIds.Count == 0)
            return false;

        clientRpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = targetClientIds.ToArray()
            }
        };
        return true;
    }

    public void SetLocalThreatIntensity(float intensity)
    {
        intensity = Mathf.Clamp01(intensity);

        if (ShouldSendLocalEffectToOwner())
        {
            if (!ShouldSendThreatIntensity(intensity))
                return;

            threatEffectKnownClear = false;
            SetLocalThreatIntensityClientRpc(intensity, OwnerClientRpcParams());
            return;
        }

        ApplyLocalThreatIntensity(intensity);
    }

    public void ClearLocalThreatEffect(bool stopShake = false)
    {
        if (ShouldSendLocalEffectToOwner())
        {
            if (threatEffectKnownClear)
                return;

            threatEffectKnownClear = true;
            ResetThreatEffectSendState();
            ClearLocalThreatEffectClientRpc(stopShake, OwnerClientRpcParams());
            return;
        }

        ApplyClearLocalThreatEffect(stopShake);
    }

    public void PulseLocalThreatEffect(
        float intensity,
        float duration,
        float shakeAmplitude = 0f,
        float shakeFrequency = 0f,
        float shakeDuration = 0f)
    {
        if (ShouldSendLocalEffectToOwner())
        {
            threatEffectKnownClear = false;
            PulseLocalThreatEffectClientRpc(
                intensity,
                duration,
                shakeAmplitude,
                shakeFrequency,
                shakeDuration,
                OwnerClientRpcParams());
            return;
        }

        ApplyPulseLocalThreatEffect(
            intensity,
            duration,
            shakeAmplitude,
            shakeFrequency,
            shakeDuration);
    }

    public void ShakeLocalThreatEffect(
        float amplitude,
        float frequency,
        float duration)
    {
        if (ShouldSendLocalEffectToOwner())
        {
            if (!ShouldSendShakeEffect())
                return;

            threatEffectKnownClear = false;
            ShakeLocalThreatEffectClientRpc(
                amplitude,
                frequency,
                duration,
                OwnerClientRpcParams());
            return;
        }

        ApplyShakeLocalThreatEffect(amplitude, frequency, duration);
    }

    [ClientRpc]
    void SetLocalThreatIntensityClientRpc(
        float intensity,
        ClientRpcParams clientRpcParams = default)
    {
        ApplyLocalThreatIntensity(intensity);
    }

    [ClientRpc]
    void ClearLocalThreatEffectClientRpc(
        bool stopShake,
        ClientRpcParams clientRpcParams = default)
    {
        ApplyClearLocalThreatEffect(stopShake);
    }

    [ClientRpc]
    void PulseLocalThreatEffectClientRpc(
        float intensity,
        float duration,
        float shakeAmplitude,
        float shakeFrequency,
        float shakeDuration,
        ClientRpcParams clientRpcParams = default)
    {
        ApplyPulseLocalThreatEffect(
            intensity,
            duration,
            shakeAmplitude,
            shakeFrequency,
            shakeDuration);
    }

    [ClientRpc]
    void ShakeLocalThreatEffectClientRpc(
        float amplitude,
        float frequency,
        float duration,
        ClientRpcParams clientRpcParams = default)
    {
        ApplyShakeLocalThreatEffect(amplitude, frequency, duration);
    }

    bool ShouldSendLocalEffectToOwner()
    {
        return IsSpawned && IsServer && !IsOwner;
    }

    ClientRpcParams OwnerClientRpcParams()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { OwnerClientId }
            }
        };
    }

    bool ShouldSendThreatIntensity(float intensity)
    {
        if (Time.time - lastThreatEffectSendTime < ThreatEffectSendInterval &&
            Mathf.Abs(intensity - lastThreatEffectSentIntensity) < ThreatEffectIntensityEpsilon)
        {
            return false;
        }

        lastThreatEffectSendTime = Time.time;
        lastThreatEffectSentIntensity = intensity;
        return true;
    }

    bool ShouldSendShakeEffect()
    {
        if (Time.time - lastShakeEffectSendTime < ThreatEffectSendInterval)
            return false;

        lastShakeEffectSendTime = Time.time;
        return true;
    }

    void ResetThreatEffectSendState()
    {
        lastThreatEffectSendTime = -999f;
        lastThreatEffectSentIntensity = -1f;
        lastShakeEffectSendTime = -999f;
    }

    PlayerVignetteEffect ResolveLocalVignetteEffect()
    {
        if (localVignetteEffect == null)
            localVignetteEffect = GetComponentInChildren<PlayerVignetteEffect>(true);

        return localVignetteEffect;
    }

    void ApplyLocalThreatIntensity(float intensity)
    {
        PlayerVignetteEffect effect = ResolveLocalVignetteEffect();
        if (effect != null)
            effect.SetThreatIntensity(intensity);
    }

    void ApplyClearLocalThreatEffect(bool stopShake)
    {
        PlayerVignetteEffect effect = ResolveLocalVignetteEffect();
        if (effect == null)
            return;

        effect.ClearThreatIntensity();
        if (stopShake)
            effect.StopShake();
    }

    void ApplyPulseLocalThreatEffect(
        float intensity,
        float duration,
        float shakeAmplitude,
        float shakeFrequency,
        float shakeDuration)
    {
        PlayerVignetteEffect effect = ResolveLocalVignetteEffect();
        if (effect == null)
            return;

        if (intensity > 0f && duration > 0f)
            effect.Pulse(intensity, duration);

        if (shakeAmplitude > 0f && shakeDuration > 0f)
            effect.Shake(shakeAmplitude, shakeFrequency, shakeDuration);
    }

    void ApplyShakeLocalThreatEffect(
        float amplitude,
        float frequency,
        float duration)
    {
        PlayerVignetteEffect effect = ResolveLocalVignetteEffect();
        if (effect != null && amplitude > 0f && duration > 0f)
            effect.Shake(amplitude, frequency, duration);
    }

    bool TryGetNetworkObjectReference(
        GameObject target,
        out NetworkObjectReference targetReference)
    {
        NetworkObject networkObject = target != null
            ? target.GetComponentInParent<NetworkObject>()
            : null;

        if (networkObject == null && target != null)
            networkObject = target.GetComponentInChildren<NetworkObject>(true);

        if (networkObject == null)
        {
            targetReference = default;
            return false;
        }

        targetReference = networkObject;
        return true;
    }

    void ApplyEnemyWaterHit(GameObject target, Vector3 sourcePosition)
    {
        if (target == null)
            return;

        RaccoonBehavior raccoon =
            GetComponentInHierarchy<RaccoonBehavior>(target);
        if (raccoon != null)
            raccoon.ReceiveWaterHit(sourcePosition);

        BathroomBlondeBehavior bathroomBlonde =
            GetComponentInHierarchy<BathroomBlondeBehavior>(target);
        if (bathroomBlonde != null)
            bathroomBlonde.ReceiveWaterHit(sourcePosition);

        BathroomBlondeMirror bathroomMirror =
            GetComponentInHierarchy<BathroomBlondeMirror>(target);
        if (bathroomMirror != null)
            bathroomMirror.ReceiveWaterHit(sourcePosition);

        BathroomBlondeDrain bathroomDrain =
            GetComponentInHierarchy<BathroomBlondeDrain>(target);
        if (bathroomDrain != null)
            bathroomDrain.ReceiveWaterHit(sourcePosition);
    }

    T GetComponentInHierarchy<T>(GameObject target) where T : Component
    {
        if (target == null)
            return null;

        T component = target.GetComponentInParent<T>();
        if (component != null)
            return component;

        return target.GetComponentInChildren<T>(true);
    }

    void PlayFootstepAudio(bool sprintingNow)
    {
        if (footstepAudioSource == null) return;

        AudioClip clip = sprintingNow ? sprintFootstepClip : walkFootstepClip;
        if (clip != null)
        {
            float volume = isCrouching
                ? footstepVolume * crouchFootstepVolumeMultiplier
                : footstepVolume;
            footstepAudioSource.PlayOneShot(clip, volume);
        }
    }

    public void ApplyStaminaDrainMultiplier(float multiplier, float duration)
    {
        staminaDrainMultiplier = Mathf.Clamp(multiplier, 0f, 10f);
        staminaDrainMultiplierTimer = Mathf.Max(0f, duration);
    }

    void UpdateTimedStaminaMultiplier()
    {
        if (staminaDrainMultiplierTimer <= 0f) return;

        staminaDrainMultiplierTimer -= Time.deltaTime;
        if (staminaDrainMultiplierTimer <= 0f)
        {
            staminaDrainMultiplierTimer = 0f;
            staminaDrainMultiplier = 1f;
        }
    }

    public float GetStaminaPercent() =>
        maxStamina > 0f ? currentStamina / maxStamina : 0f;
}
