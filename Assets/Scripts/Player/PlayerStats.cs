using UnityEngine;

public class PlayerStatus : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;

    [Header("Water")]
    public float maxWater = 100f;
    public float fillRate = 10f;

<<<<<<< Updated upstream
    private float currentHealth;
    private float currentWater = 0f;
    private bool inWater = false;
=======
    [Header("Death Transformation")]
    public bool hideRenderersOnDeath = true;
    public bool disableCollidersOnDeath = true;
    public MonoBehaviour[] componentsToDisableOnDeath;

    public event Action<PlayerStatus> OnDeath;

    private float currentHealth;
    private float currentWater = 0f;
    private bool inWater = false;
    private bool isDead = false;
    private bool deathTransformationApplied = false;
>>>>>>> Stashed changes

    private PlayerMovement movement;
    public bool IsMoving() => movement != null && movement.IsMoving();

    void Start()
    {
        movement = GetComponent<PlayerMovement>();
        currentHealth = maxHealth;
    }

    public void SetInWater(bool value) => inWater = value;
    public float GetCurrentHealth() => currentHealth;
    public float GetMaxHealth() => maxHealth;
    public float GetCurrentWater() => currentWater;
    public float GetHealthPercent() => maxHealth > 0f ? currentHealth / maxHealth : 0f;
    public float GetWaterPercent() => currentWater / maxWater;
    public bool IsSprinting() => movement != null && movement.IsSprinting();

    void Update()
    {
        if (inWater && currentWater < maxWater)
        {
            currentWater += fillRate * Time.deltaTime;
            currentWater = Mathf.Clamp(currentWater, 0f, maxWater);
        }
    }

    public void TakeDamage(float damage)
    {
        if (damage <= 0f) return;

        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
    }

    public void ApplyDeathTransformation()
    {
        if (deathTransformationApplied) return;
        deathTransformationApplied = true;

        DisableConfiguredComponents();

        if (hideRenderersOnDeath)
        {
            Renderer[] renderers = GetComponentsInChildren<Renderer>();
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].enabled = false;
        }

        if (disableCollidersOnDeath)
        {
            Collider[] colliders = GetComponentsInChildren<Collider>();
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
        }
    }

    void DisableConfiguredComponents()
    {
        if (componentsToDisableOnDeath != null && componentsToDisableOnDeath.Length > 0)
        {
            for (int i = 0; i < componentsToDisableOnDeath.Length; i++)
            {
                MonoBehaviour component = componentsToDisableOnDeath[i];
                if (component != null && component != this)
                    component.enabled = false;
            }

            return;
        }

        PlayerMovement playerMovement = GetComponent<PlayerMovement>();
        if (playerMovement != null) playerMovement.enabled = false;

        PlayerInventory inventory = GetComponent<PlayerInventory>();
        if (inventory != null) inventory.enabled = false;

        WaterCannon waterCannon = GetComponentInChildren<WaterCannon>();
        if (waterCannon != null) waterCannon.enabled = false;
    }

    public bool ConsumeWater(float amount)
    {
        if (amount <= 0f || currentWater <= 0f) return false;

        currentWater -= amount;
        currentWater = Mathf.Clamp(currentWater, 0f, maxWater);
        return true;
    }
}