using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 9f;
    public bool IsMoving() => moveInput != Vector2.zero;

    [Header("Stamina")]
    public float maxStamina = 100f;
    public float staminaDrainRate = 20f;
    public float staminaRegenRate = 10f;
    public float staminaRegenDelay = 1.5f;
    public bool IsSprinting() => playerInput.actions["Sprint"].IsPressed() && currentStamina > 0f;

    [Header("Footstep Noise")]
    public float walkNoiseRadius = 4f;
    public float sprintNoiseRadius = 10f;
    public float walkStepInterval = 0.55f;
    public float sprintStepInterval = 0.32f;

    [Header("Footstep Audio")]
    public AudioSource footstepAudioSource;
    public AudioClip walkFootstepClip;
    public AudioClip sprintFootstepClip;
    [Range(0f, 1f)] public float footstepVolume = 0.75f;

    private Rigidbody rb;
    private PlayerInput playerInput;
    private Vector2 moveInput;
    private bool isSprinting;
    private float currentStamina;
    private float regenTimer;
    private float staminaDrainMultiplier = 1f;
    private float staminaDrainMultiplierTimer;
    private float footstepTimer;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        playerInput = GetComponent<PlayerInput>();
        currentStamina = maxStamina;

        if (footstepAudioSource == null)
            footstepAudioSource = GetComponent<AudioSource>();
    }

    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
    }

    void Update()
    {
        UpdateTimedStaminaMultiplier();
        UpdateFootstepNoise();
    }

    void FixedUpdate()
    {
        Vector3 camForward = Camera.main.transform.forward;
        Vector3 camRight = Camera.main.transform.right;
        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        isSprinting = playerInput.actions["Sprint"].IsPressed();
        bool canSprint = isSprinting && currentStamina > 0f && moveInput != Vector2.zero;
        float speed = canSprint ? sprintSpeed : walkSpeed;

        Vector3 move = camForward * moveInput.y + camRight * moveInput.x;
        rb.MovePosition(rb.position + move * speed * Time.fixedDeltaTime);

        Quaternion targetRotation = Quaternion.Euler(0f, Camera.main.transform.eulerAngles.y, 0f);
        rb.MoveRotation(targetRotation);

        // Stamina logic
        if (canSprint)
        {
            currentStamina -= staminaDrainRate * staminaDrainMultiplier * Time.fixedDeltaTime;
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

    void UpdateFootstepNoise()
    {
        bool moving = moveInput != Vector2.zero;
        if (!moving)
        {
            footstepTimer = 0f;
            return;
        }

        bool sprintingNow = IsSprinting();
        float interval = sprintingNow ? sprintStepInterval : walkStepInterval;
        footstepTimer -= Time.deltaTime;

        if (footstepTimer > 0f) return;

        footstepTimer = interval;
        float noiseRadius = sprintingNow ? sprintNoiseRadius : walkNoiseRadius;
        NoiseEvent.Emit(transform.position, noiseRadius, gameObject);
        PlayFootstepAudio(sprintingNow);
    }

    void PlayFootstepAudio(bool sprintingNow)
    {
        if (footstepAudioSource == null) return;

        AudioClip clip = sprintingNow ? sprintFootstepClip : walkFootstepClip;
        if (clip != null)
            footstepAudioSource.PlayOneShot(clip, footstepVolume);
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

    public float GetStaminaPercent() => maxStamina > 0f ? currentStamina / maxStamina : 0f;
}
