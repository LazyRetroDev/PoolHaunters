using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 9f;

    [Header("Stamina")]
    public float maxStamina = 100f;
    public float staminaDrainRate = 20f;
    public float staminaRegenRate = 10f;
    public float staminaRegenDelay = 1.5f;

    private Rigidbody rb;
    private PlayerInput playerInput;
    private Vector2 moveInput;
    private bool isSprinting;
    private float currentStamina;
    private float regenTimer;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        playerInput = GetComponent<PlayerInput>();
        currentStamina = maxStamina;
    }

    public void OnMove(InputValue value)
    {
        moveInput = value.Get<Vector2>();
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
            currentStamina -= staminaDrainRate * Time.fixedDeltaTime;
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

    public float GetStaminaPercent() => currentStamina / maxStamina;
}