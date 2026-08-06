using UnityEngine;
using UnityEngine.InputSystem;

public class CursorLockController : MonoBehaviour
{
    private static int unlockRequestCount;

    public bool lockCursorOnStart = true;
    public bool hideCursorWhenLocked = true;
    public bool relockOnLeftClick = true;
    public bool relockOnFocus = true;

    void Start()
    {
        if (HasUnlockRequest())
            SetCursorLocked(false);
        else if (lockCursorOnStart)
            SetCursorLocked(true);
    }

    void Update()
    {
        if (HasUnlockRequest())
        {
            SetCursorLocked(false);
            return;
        }

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            SetCursorLocked(false);

        if (relockOnLeftClick && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            SetCursorLocked(true);
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && relockOnFocus && !HasUnlockRequest())
            SetCursorLocked(true);
    }

    public void ForceLockCursor()
    {
        if (HasUnlockRequest())
        {
            SetCursorLocked(false);
            return;
        }

        SetCursorLocked(true);
    }

    public void ForceUnlockCursor()
    {
        SetCursorLocked(false);
    }

    public static void RequestCursorUnlocked()
    {
        unlockRequestCount = Mathf.Max(0, unlockRequestCount + 1);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public static void ReleaseCursorUnlocked()
    {
        unlockRequestCount = Mathf.Max(0, unlockRequestCount - 1);
    }

    public static bool HasUnlockRequest()
    {
        return unlockRequestCount > 0;
    }

    void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked || !hideCursorWhenLocked;
    }
}
