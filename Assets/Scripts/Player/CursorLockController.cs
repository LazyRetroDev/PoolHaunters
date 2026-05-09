using UnityEngine;
using UnityEngine.InputSystem;

public class CursorLockController : MonoBehaviour
{
    public bool lockCursorOnStart = true;
    public bool hideCursorWhenLocked = true;
    public bool relockOnLeftClick = true;
    public bool relockOnFocus = true;

    void Start()
    {
        if (lockCursorOnStart)
            SetCursorLocked(true);
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            SetCursorLocked(false);

        if (relockOnLeftClick && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            SetCursorLocked(true);
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && relockOnFocus)
            SetCursorLocked(true);
    }

    void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked || !hideCursorWhenLocked;
    }
}
