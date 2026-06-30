using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

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
        NoiseEvent.Emit(transform.position, noiseRadius, gameObject);
        PlayFootstepAudio(sprintingNow);
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
