using UnityEngine;

public class PlayerStatus : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;

    [Header("Water")]
    public float maxWater = 100f;
    public float fillRate = 10f;

    private float currentHealth;
    private float currentWater = 0f;
    private bool inWater = false;

    private PlayerMovement movement;

    void Start()
    {
        movement = GetComponent<PlayerMovement>();
        currentHealth = maxHealth;
    }

    public void SetInWater(bool value) => inWater = value;
    public float GetCurrentHealth() => currentHealth;
    public float GetMaxHealth() => maxHealth;
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
}
