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
    public bool detachFromParentOnBegin = true;

    private bool isSpectating;
    private float yaw;
    private float pitch;
    private CursorLockMode previousLockState;
    private bool previousCursorVisible;
    private Transform previousParent;
    private MonoBehaviour[] disabledCameraControllers;

    public static PlayerSpectatorMode ActivateFor(PlayerStatus deadPlayer)
    {
        GameObject cameraRig = GetSpectatorCameraObject();
        if (cameraRig == null) return null;

        PlayerSpectatorMode spectator = cameraRig.GetComponent<PlayerSpectatorMode>();
        if (spectator == null)
            spectator = cameraRig.AddComponent<PlayerSpectatorMode>();

        spectator.BeginSpectating(deadPlayer);
        return spectator;
    }

    static GameObject GetSpectatorCameraObject()
    {
        if (Camera.main != null)
            return Camera.main.gameObject;

        GameObject mainCamera = GameObject.Find("Main Camera");
        if (mainCamera != null)
            return mainCamera;

        return GameObject.Find("Cinemachine Camera");
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
        previousParent = transform.parent;

        if (detachFromParentOnBegin)
            transform.SetParent(null, true);

        if (disableCinemachineControllers)
            DisableConflictingCameraControllers();

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
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    public void EndSpectating()
    {
        if (!isSpectating) return;

        isSpectating = false;
        Cursor.lockState = previousLockState;
        Cursor.visible = previousCursorVisible;
        RestoreDisabledCameraControllers();

        if (detachFromParentOnBegin && previousParent != null)
            transform.SetParent(previousParent, true);

        enabled = false;
    }

    void LateUpdate()
    {
        if (!isSpectating) return;

        UpdateLook();
        UpdateMovement();
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
    }

    void UpdateLook()
    {
        if (Mouse.current == null) return;

        Vector2 lookDelta = Mouse.current.delta.ReadValue();
        yaw += lookDelta.x * lookSensitivity;
        pitch -= lookDelta.y * lookSensitivity;
        pitch = Mathf.Clamp(pitch, -85f, 85f);
    }

    void UpdateMovement()
    {
        if (Keyboard.current == null) return;

        Vector2 input = Vector2.zero;
        if (Keyboard.current.wKey.isPressed) input.y += 1f;
        if (Keyboard.current.sKey.isPressed) input.y -= 1f;
        if (Keyboard.current.dKey.isPressed) input.x += 1f;
        if (Keyboard.current.aKey.isPressed) input.x -= 1f;

        input = input.normalized;

        Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 flatForward = yawRotation * Vector3.forward;
        Vector3 flatRight = yawRotation * Vector3.right;
        Vector3 worldMove = (flatForward * input.y + flatRight * input.x) * moveSpeed;

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
        int disabledCount = 0;

        for (int i = 0; i < components.Length; i++)
        {
            MonoBehaviour component = components[i];
            if (component == null || component == this || !component.enabled) continue;

            string typeName = component.GetType().Name;
            if (typeName == "CinemachineBrain" ||
                typeName == "CinemachinePanTilt" ||
                typeName == "CinemachineInputAxisController" ||
                typeName == "CinemachineHardLockToTarget" ||
                typeName == "CameraWobble")
            {
                disabledCount++;
            }
        }

        disabledCameraControllers = new MonoBehaviour[disabledCount];
        int index = 0;

        for (int i = 0; i < components.Length; i++)
        {
            MonoBehaviour component = components[i];
            if (component == null || component == this || !component.enabled) continue;

            string typeName = component.GetType().Name;
            if (typeName == "CinemachineBrain" ||
                typeName == "CinemachinePanTilt" ||
                typeName == "CinemachineInputAxisController" ||
                typeName == "CinemachineHardLockToTarget" ||
                typeName == "CameraWobble")
            {
                disabledCameraControllers[index] = component;
                component.enabled = false;
                index++;
            }
        }
    }

    void RestoreDisabledCameraControllers()
    {
        if (disabledCameraControllers == null) return;

        for (int i = 0; i < disabledCameraControllers.Length; i++)
        {
            if (disabledCameraControllers[i] != null)
                disabledCameraControllers[i].enabled = true;
        }

        disabledCameraControllers = null;
    }

    float NormalizePitch(float angle)
    {
        if (angle > 180f)
            angle -= 360f;

        return angle;
    }
}
