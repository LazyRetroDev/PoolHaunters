using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerSpectatorMode : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 6f;
    public float fastMoveMultiplier = 2.5f;
    public float verticalMoveSpeed = 4f;
    public float lookSensitivity = 0.12f;

    [Header("Activation")]
    public Vector3 startOffset = new Vector3(0f, 2.2f, -3f);
    public bool unlockCursorWhileSpectating = false;
    public bool disableCinemachineControllers = true;

    private bool isSpectating;
    private float yaw;
    private float pitch;
    private CursorLockMode previousLockState;
    private bool previousCursorVisible;

    public static PlayerSpectatorMode ActivateFor(PlayerStatus deadPlayer)
    {
        PlayerSpectatorMode spectator = FindObjectOfType<PlayerSpectatorMode>();
        if (spectator == null)
        {
            GameObject cameraRig = GameObject.Find("Cinemachine Camera");
            if (cameraRig == null && Camera.main != null)
                cameraRig = Camera.main.gameObject;

            if (cameraRig == null) return null;
            spectator = cameraRig.AddComponent<PlayerSpectatorMode>();
        }

        spectator.BeginSpectating(deadPlayer);
        return spectator;
    }

    void Awake()
    {
        enabled = isSpectating;
    }

    public void BeginSpectating(PlayerStatus deadPlayer)
    {
        if (isSpectating) return;

        isSpectating = true;
        enabled = true;

        previousLockState = Cursor.lockState;
        previousCursorVisible = Cursor.visible;

        if (unlockCursorWhileSpectating)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        if (deadPlayer != null)
            transform.position = deadPlayer.transform.position + startOffset;

        Vector3 euler = transform.rotation.eulerAngles;
        yaw = euler.y;
        pitch = NormalizePitch(euler.x);

        if (disableCinemachineControllers)
            DisableConflictingCameraControllers();
    }

    public void EndSpectating()
    {
        if (!isSpectating) return;

        isSpectating = false;
        Cursor.lockState = previousLockState;
        Cursor.visible = previousCursorVisible;
        enabled = false;
    }

    void Update()
    {
        if (!isSpectating) return;

        UpdateLook();
        UpdateMovement();
    }

    void UpdateLook()
    {
        if (Mouse.current == null) return;

        Vector2 lookDelta = Mouse.current.delta.ReadValue();
        yaw += lookDelta.x * lookSensitivity;
        pitch -= lookDelta.y * lookSensitivity;
        pitch = Mathf.Clamp(pitch, -85f, 85f);

        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void UpdateMovement()
    {
        if (Keyboard.current == null) return;

        Vector3 input = Vector3.zero;
        if (Keyboard.current.wKey.isPressed) input += Vector3.forward;
        if (Keyboard.current.sKey.isPressed) input += Vector3.back;
        if (Keyboard.current.dKey.isPressed) input += Vector3.right;
        if (Keyboard.current.aKey.isPressed) input += Vector3.left;

        Vector3 worldMove = transform.TransformDirection(input.normalized) * moveSpeed;

        float vertical = 0f;
        if (Keyboard.current.spaceKey.isPressed || Keyboard.current.eKey.isPressed) vertical += 1f;
        if (Keyboard.current.leftCtrlKey.isPressed || Keyboard.current.qKey.isPressed) vertical -= 1f;

        worldMove += Vector3.up * vertical * verticalMoveSpeed;

        float speedMultiplier = Keyboard.current.leftShiftKey.isPressed ? fastMoveMultiplier : 1f;
        transform.position += worldMove * speedMultiplier * Time.deltaTime;
    }

    void DisableConflictingCameraControllers()
    {
        MonoBehaviour[] components = GetComponents<MonoBehaviour>();
        for (int i = 0; i < components.Length; i++)
        {
            MonoBehaviour component = components[i];
            if (component == null || component == this) continue;

            string typeName = component.GetType().Name;
            if (typeName == "CinemachinePanTilt" ||
                typeName == "CinemachineInputAxisController" ||
                typeName == "CinemachineHardLockToTarget" ||
                typeName == "CameraWobble")
            {
                component.enabled = false;
            }
        }
    }

    float NormalizePitch(float angle)
    {
        if (angle > 180f)
            angle -= 360f;

        return angle;
    }
}
