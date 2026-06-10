using UnityEngine;
using System;

public class PlayerStatus : MonoBehaviour
{
    [Header("Health")]
    public float maxHealth = 100f;

    [Header("Knockout")]
    public float knockoutDuration = 45f;
    [Range(0.01f, 1f)] public float reviveHealthPercent = 0.35f;
    public bool disableControlsWhileKnockedOut = true;

    [Header("Water")]
    public float maxWater = 100f;
    public float fillRate = 10f;
    public WaterQuality waterFillQuality = WaterQuality.Clean;
    public WaterQuality startingWaterQuality = WaterQuality.Clean;

    [Header("Water Cleaning Effects")]
    public float contaminatedCleaningMultiplier = 0.25f;
    public float chemicallyEnhancedCleaningMultiplier = 1.35f;

    [Header("Death Presentation")]
    public bool disableGravityOnDeath = true;
    public bool makeBodyTransparentOnDeath = true;
    [Range(0f, 1f)] public float deadBodyAlpha = 0.35f;
    public HUD[] hudsToDisableOnDeath;

    [Header("Death Transformation")]
    public bool hideRenderersOnDeath = true;
    public bool disableCollidersOnDeath = true;
    public MonoBehaviour[] componentsToDisableOnDeath;

    public event Action<PlayerStatus> OnKnockedOut;
    public event Action<PlayerStatus> OnRevived;
    public event Action<PlayerStatus> OnDeath;
    public event Action<WaterQuality> OnWaterQualityChanged;

    private float currentHealth;
    private float currentWater = 0f;
    private float knockoutTimer;
    private WaterQuality currentWaterQuality;
    private bool inWater = false;
    private bool isKnockedOut = false;
    private bool isDead = false;
    private bool deathTransformationApplied = false;

    private PlayerMovement movement;
    public bool IsMoving() => movement != null && movement.IsMoving() && (CanAct() || isKnockedOut);

    void Start()
    {
        movement = GetComponent<PlayerMovement>();
        currentHealth = maxHealth;
        currentWaterQuality = startingWaterQuality;

        if (currentHealth <= 0f)
            EnterKnockout();
    }

    public void SetInWater(bool value) => inWater = value;
    public float GetCurrentHealth() => currentHealth;
    public float GetMaxHealth() => maxHealth;
    public float GetCurrentWater() => currentWater;
    public WaterQuality GetWaterQuality() => currentWaterQuality;
    public float GetHealthPercent() => maxHealth > 0f ? currentHealth / maxHealth : 0f;
    public float GetWaterPercent() => maxWater > 0f ? currentWater / maxWater : 0f;
    public float GetKnockoutTimeRemaining() => knockoutTimer;
    public float GetKnockoutPercent() => knockoutDuration > 0f ? knockoutTimer / knockoutDuration : 0f;
    public bool IsSprinting() => movement != null && movement.IsSprinting() && CanAct();
    public bool IsKnockedOut() => isKnockedOut;
    public bool IsDead() => isDead;
    public bool IsTransformed() => deathTransformationApplied;
    public bool CanAct() => !isKnockedOut && !isDead && !deathTransformationApplied;
    public bool HasContaminatedWater() => currentWater > 0f && currentWaterQuality == WaterQuality.Contaminated;

    void Update()
    {
        if (isKnockedOut)
        {
            UpdateKnockoutTimer();
            return;
        }

        if (CanAct() && inWater && currentWater < maxWater)
            AddWater(fillRate * Time.deltaTime, waterFillQuality);
    }

    public bool TakeDamage(float damage)
    {
        if (damage <= 0f || isDead || isKnockedOut || deathTransformationApplied) return false;

        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        if (currentHealth > 0f) return false;

        EnterKnockout();
        return true;
    }

    public void EnterKnockout()
    {
        if (isDead || deathTransformationApplied || isKnockedOut) return;

        isKnockedOut = true;
        currentHealth = 0f;
        knockoutTimer = knockoutDuration;

        if (disableControlsWhileKnockedOut)
            DisableKnockoutBlockedComponents();

        OnKnockedOut?.Invoke(this);
    }

    void UpdateKnockoutTimer()
    {
        knockoutTimer -= Time.deltaTime;
        if (knockoutTimer <= 0f)
            Die();
    }

    public bool Revive()
    {
        return Revive(maxHealth * reviveHealthPercent);
    }

    public bool Revive(float revivedHealth)
    {
        if (!isKnockedOut || isDead || deathTransformationApplied) return false;

        isKnockedOut = false;
        knockoutTimer = 0f;
        currentHealth = Mathf.Clamp(revivedHealth, 1f, maxHealth);
        RestoreConfiguredComponents();
        OnRevived?.Invoke(this);
        return true;
    }

    public void Die()
    {
        if (isDead) return;

        isKnockedOut = false;
        isDead = true;
        currentHealth = 0f;
        knockoutTimer = 0f;
        DisableConfiguredComponents();
        ApplyDeathPresentation();
        ActivateSpectatorMode();
        OnDeath?.Invoke(this);
    }

    public void ApplyDeathTransformation()
    {
        if (deathTransformationApplied) return;
        deathTransformationApplied = true;

        isKnockedOut = false;
        isDead = true;
        currentHealth = 0f;
        knockoutTimer = 0f;
        DisableConfiguredComponents();
        ApplyDeathPresentation();
        ActivateSpectatorMode();
        OnDeath?.Invoke(this);

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

    void ApplyDeathPresentation()
    {
        DisableGravityAndMotion();
        DisableDeathHud();

        if (makeBodyTransparentOnDeath)
            MakeBodyTransparent(deadBodyAlpha);
    }

    void DisableGravityAndMotion()
    {
        if (!disableGravityOnDeath) return;

        Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>();
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] == null) continue;
            bodies[i].useGravity = false;
            bodies[i].linearVelocity = Vector3.zero;
            bodies[i].angularVelocity = Vector3.zero;
            bodies[i].isKinematic = true;
        }
    }

    void DisableDeathHud()
    {
        if (hudsToDisableOnDeath != null && hudsToDisableOnDeath.Length > 0)
        {
            for (int i = 0; i < hudsToDisableOnDeath.Length; i++)
                DisableHud(hudsToDisableOnDeath[i]);

            return;
        }

        HUD[] huds = FindObjectsOfType<HUD>();
        for (int i = 0; i < huds.Length; i++)
        {
            if (huds[i] != null && huds[i].playerStatus == this)
                DisableHud(huds[i]);
        }
    }

    void DisableHud(HUD hud)
    {
        if (hud == null) return;

        Canvas canvas = hud.GetComponent<Canvas>();
        if (canvas != null)
            canvas.enabled = false;
        else
            hud.gameObject.SetActive(false);
    }

    void MakeBodyTransparent(float alpha)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer targetRenderer = renderers[i];
            if (targetRenderer == null || !targetRenderer.enabled) continue;

            Material[] materials = targetRenderer.materials;
            for (int m = 0; m < materials.Length; m++)
                MakeMaterialTransparent(materials[m], alpha);
        }
    }

    void MakeMaterialTransparent(Material material, float alpha)
    {
        if (material == null) return;

        if (material.HasProperty("_BaseColor"))
        {
            Color color = material.GetColor("_BaseColor");
            color.a = alpha;
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            Color color = material.GetColor("_Color");
            color.a = alpha;
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 0f);
        if (material.HasProperty("_AlphaClip"))
            material.SetFloat("_AlphaClip", 0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    void ActivateSpectatorMode()
    {
        PlayerSpectatorMode.ActivateFor(this);
    }

    void DisableKnockoutBlockedComponents()
    {
        PlayerInventory inventory = GetComponent<PlayerInventory>();
        if (inventory != null) inventory.enabled = false;

        WaterCannon waterCannon = GetComponentInChildren<WaterCannon>();
        if (waterCannon != null) waterCannon.enabled = false;
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

    void RestoreConfiguredComponents()
    {
        if (componentsToDisableOnDeath != null && componentsToDisableOnDeath.Length > 0)
        {
            for (int i = 0; i < componentsToDisableOnDeath.Length; i++)
            {
                MonoBehaviour component = componentsToDisableOnDeath[i];
                if (component != null && component != this)
                    component.enabled = true;
            }

            return;
        }

        PlayerMovement playerMovement = GetComponent<PlayerMovement>();
        if (playerMovement != null) playerMovement.enabled = true;

        PlayerInventory inventory = GetComponent<PlayerInventory>();
        if (inventory != null) inventory.enabled = true;

        WaterCannon waterCannon = GetComponentInChildren<WaterCannon>();
        if (waterCannon != null) waterCannon.enabled = true;
    }

    public bool ConsumeWater(float amount)
    {
        if (!CanAct()) return false;
        if (amount <= 0f || currentWater <= 0f) return false;

        currentWater -= amount;
        currentWater = Mathf.Clamp(currentWater, 0f, maxWater);

        if (currentWater <= 0f)
            SetWaterQuality(WaterQuality.Clean);

        return true;
    }

    public bool AddWater(float amount, WaterQuality quality, bool replaceExistingQuality = false)
    {
        if (!CanAct()) return false;
        if (amount <= 0f || currentWater >= maxWater) return false;

        bool wasEmpty = currentWater <= 0f;
        currentWater += amount;
        currentWater = Mathf.Clamp(currentWater, 0f, maxWater);

        if (wasEmpty || replaceExistingQuality || quality == WaterQuality.Contaminated)
            SetWaterQuality(quality);
        else if (currentWaterQuality != WaterQuality.Contaminated && quality == WaterQuality.ChemicallyEnhanced)
            SetWaterQuality(WaterQuality.ChemicallyEnhanced);

        return true;
    }

    public void ContaminateWater()
    {
        if (!CanAct()) return;
        if (currentWater <= 0f) return;
        SetWaterQuality(WaterQuality.Contaminated);
    }

    public void PurifyWater()
    {
        if (!CanAct()) return;
        if (currentWater <= 0f) return;
        SetWaterQuality(WaterQuality.Clean);
    }

    public float GetWaterCleaningMultiplier()
    {
        if (currentWaterQuality == WaterQuality.Contaminated)
            return contaminatedCleaningMultiplier;

        if (currentWaterQuality == WaterQuality.ChemicallyEnhanced)
            return chemicallyEnhancedCleaningMultiplier;

        return 1f;
    }

    void SetWaterQuality(WaterQuality quality)
    {
        if (currentWaterQuality == quality) return;
        currentWaterQuality = quality;
        OnWaterQualityChanged?.Invoke(currentWaterQuality);
    }
}
