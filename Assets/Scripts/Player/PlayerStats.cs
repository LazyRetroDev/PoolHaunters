using UnityEngine;
using System;

public class PlayerStatus : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;

    [Header("Water")]
    public float maxWater = 100f;
    public float fillRate = 10f;

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

    private PlayerMovement movement;
    public bool IsMoving() => movement != null && movement.IsMoving();

    void Start()
    {
        movement = GetComponent<PlayerMovement>();
        currentHealth = maxHealth;
        isDead = currentHealth <= 0f;
    }

    public void SetInWater(bool value) => inWater = value;
    public float GetCurrentHealth() => currentHealth;
    public float GetMaxHealth() => maxHealth;
    public float GetCurrentWater() => currentWater;
    public float GetHealthPercent() => maxHealth > 0f ? currentHealth / maxHealth : 0f;
    public float GetWaterPercent() => maxWater > 0f ? currentWater / maxWater : 0f;
    public bool IsSprinting() => movement != null && movement.IsSprinting();
    public bool IsDead() => isDead;

    void Update()
    {
        if (inWater && currentWater < maxWater)
        {
            currentWater += fillRate * Time.deltaTime;
            currentWater = Mathf.Clamp(currentWater, 0f, maxWater);
        }
    }

    public bool TakeDamage(float damage)
    {
        if (damage <= 0f || isDead) return false;

        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        if (currentHealth > 0f) return false;

        isDead = true;
        OnDeath?.Invoke(this);
        return true;
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
