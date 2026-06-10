using UnityEngine;

public class PlayerPetrify : MonoBehaviour
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

    void Start()
    {
        movement = GetComponent<PlayerMovement>();
        inventory = GetComponent<PlayerInventory>();
        ResolveCameraEffects();
    }

    void Update()
    {
        if (!isPetrified) return;

        ResolveCameraEffects();
        if (cameraEffects != null)
            cameraEffects.SetThreatIntensity(petrifiedVignetteIntensity);

        petrifyTimer -= Time.deltaTime;
        ApplyPetrifiedControlLock();

        if (petrifyTimer <= 0f)
            Unpetrify();
    }

    public bool IsPetrified()
    {
        return isPetrified;
    }

    public void Petrify()
    {
        isPetrified = true;
        petrifyTimer = petrifyDuration;
        ResolveCameraEffects();

        if (cameraEffects != null)
        {
            cameraEffects.Pulse(petrifyPulseIntensity, petrifyPulseDuration);
            cameraEffects.Shake(petrifyShakeAmplitude, petrifyShakeFrequency, petrifyShakeDuration);
            cameraEffects.SetThreatIntensity(petrifiedVignetteIntensity);
        }

        ApplyPetrifiedControlLock();
    }

    public void Unpetrify()
    {
        isPetrified = false;
        if (movement != null) movement.enabled = true;
        if (inventory != null) inventory.enabled = true;

        if (cameraEffects != null)
        {
            cameraEffects.ClearThreatIntensity();
            cameraEffects.StopShake();
        }
    }

    void ResolveCameraEffects()
    {
        if (cameraEffects != null) return;
        cameraEffects = FindObjectOfType<PlayerVignetteEffect>();
    }

    void ApplyPetrifiedControlLock()
    {
        if (movement != null) movement.enabled = false;
        if (inventory != null) inventory.enabled = false;
    }
}
