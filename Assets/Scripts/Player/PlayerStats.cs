using UnityEngine;
using Unity.Netcode;
using System;

public class PlayerStatus : NetworkBehaviour
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
    [Min(0f)] public float emptyWaterQualityThreshold = 2f;

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

    private NetworkVariable<float> syncedHealth =
        new NetworkVariable<float>();
    private NetworkVariable<float> syncedWater =
        new NetworkVariable<float>();
    private NetworkVariable<float> syncedKnockoutTimer =
        new NetworkVariable<float>();
    private NetworkVariable<int> syncedWaterQuality =
        new NetworkVariable<int>();
    private NetworkVariable<bool> syncedKnockedOut =
        new NetworkVariable<bool>();
    private NetworkVariable<bool> syncedDead =
        new NetworkVariable<bool>();
    private NetworkVariable<bool> syncedTransformed =
        new NetworkVariable<bool>();
    private NetworkVariable<int> syncedExternalControlLocks =
        new NetworkVariable<int>();

    private float currentHealth;
    private float currentWater;
    private float knockoutTimer;
    private WaterQuality currentWaterQuality;
    private bool inWater = false;
    private bool isKnockedOut = false;
    private bool isDead = false;
    private bool deathTransformationApplied = false;
    private int externalControlLocks;
    private bool localStateInitialized;
    private bool serverStateInitialized;
    private WaterZone activeWaterZone;
    private bool debugInfiniteHealth;
    private bool debugInfiniteWater;

    private PlayerMovement movement;
    private PlayerInventory inventory;
    private WaterCannon waterCannon;

    public bool IsMoving() =>
        movement != null && movement.IsMoving() && (CanAct() || isKnockedOut);

    void Awake()
    {
        CacheReferences();
    }

    public override void OnNetworkSpawn()
    {
        CacheReferences();
        SubscribeNetworkState();

        if (IsServer)
            InitializeServerState();

        ApplySyncedState(false);
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeNetworkState();
    }

    void Start()
    {
        CacheReferences();

        if (IsNetworked())
        {
            if (IsServer)
                InitializeServerState();

            ApplySyncedState(false);
            return;
        }

        InitializeLocalState();
    }

    void Update()
    {
        if (IsClientReplica())
        {
            if (IsOwner)
                UpdateOwnerPredictedWaterFill();

            return;
        }

        if (isKnockedOut)
        {
            UpdateKnockoutTimer();
            return;
        }

        if (CanAct() && inWater && currentWater < maxWater)
            FillFromCurrentWaterSource(fillRate * Time.deltaTime);
    }

    void UpdateOwnerPredictedWaterFill()
    {
        if (isKnockedOut)
            return;

        if (CanAct() && inWater && currentWater < maxWater)
            FillFromCurrentWaterSource(fillRate * Time.deltaTime);
    }

    void CacheReferences()
    {
        if (movement == null)
            movement = GetComponent<PlayerMovement>();
        if (inventory == null)
            inventory = GetComponent<PlayerInventory>();
        if (waterCannon == null)
            waterCannon = GetComponentInChildren<WaterCannon>(true);
    }

    void InitializeLocalState()
    {
        if (localStateInitialized)
            return;

        currentHealth = Mathf.Clamp(maxHealth, 0f, maxHealth);
        currentWater = 0f;
        knockoutTimer = 0f;
        currentWaterQuality = startingWaterQuality;
        isKnockedOut = false;
        isDead = false;
        deathTransformationApplied = false;
        externalControlLocks = 0;
        localStateInitialized = true;

        if (currentHealth <= 0f)
            ApplyEnterKnockout();
    }

    void InitializeServerState()
    {
        if (serverStateInitialized)
            return;

        InitializeLocalState();
        serverStateInitialized = true;
        SyncAllState();
    }

    bool IsNetworked()
    {
        return IsSpawned &&
            NetworkManager.Singleton != null &&
            NetworkManager.Singleton.IsListening;
    }

    bool IsClientReplica()
    {
        return IsNetworked() && !IsServer;
    }

    bool CanWriteState()
    {
        return !IsNetworked() || IsServer;
    }

    public void SetInWater(bool value) => inWater = value;

    public void SetWaterZone(WaterZone zone)
    {
        activeWaterZone = zone;
        inWater = zone != null;
    }

    public void ClearWaterZone(WaterZone zone)
    {
        if (activeWaterZone != zone) return;
        activeWaterZone = null;
        inWater = false;
    }

    public float GetCurrentHealth() => currentHealth;
    public float GetMaxHealth() => maxHealth;
    public float GetCurrentWater() => currentWater;
    public float GetWaterSpace() => Mathf.Max(0f, maxWater - currentWater);
    public WaterQuality GetWaterQuality() => currentWaterQuality;
    public float GetHealthPercent() =>
        maxHealth > 0f ? currentHealth / maxHealth : 0f;
    public float GetWaterPercent() =>
        maxWater > 0f ? currentWater / maxWater : 0f;
    public float GetKnockoutTimeRemaining() => knockoutTimer;
    public float GetKnockoutPercent() =>
        knockoutDuration > 0f ? knockoutTimer / knockoutDuration : 0f;
    public bool IsSprinting() =>
        movement != null && movement.IsSprinting() && CanAct();
    public bool IsKnockedOut() => isKnockedOut;
    public bool IsDead() => isDead;
    public bool IsTransformed() => deathTransformationApplied;
    public bool HasExternalControlLock() => externalControlLocks > 0;
    public bool AllowsLocalInput() =>
        !isDead && !deathTransformationApplied && externalControlLocks <= 0;
    public bool CanAct() =>
        !isKnockedOut &&
        !isDead &&
        !deathTransformationApplied &&
        externalControlLocks <= 0;
    public bool HasContaminatedWater() =>
        currentWater > 0f && currentWaterQuality == WaterQuality.Contaminated;

    void FillFromCurrentWaterSource(float amount)
    {
        if (activeWaterZone != null)
        {
            activeWaterZone.TryFillPlayer(
                this,
                amount,
                drainSource: !IsClientReplica());
            return;
        }

        AddWater(amount, waterFillQuality);
    }

    public bool TakeDamage(float damage)
    {
        if (damage <= 0f)
            return false;

        if (IsClientReplica())
        {
            TakeDamageServerRpc(damage);
            return false;
        }

        return ApplyDamage(damage);
    }

    public bool ForceTransformDeath()
    {
        if (IsClientReplica())
            return false;

        if (isDead || deathTransformationApplied)
            return false;

        ApplyDeath(true);
        return true;
    }

    public void EnterKnockout()
    {
        if (!CanWriteState())
            return;

        ApplyEnterKnockout();
    }

    void UpdateKnockoutTimer()
    {
        knockoutTimer -= Time.deltaTime;
        knockoutTimer = Mathf.Max(0f, knockoutTimer);
        SyncCoreState();

        if (knockoutTimer <= 0f)
            Die();
    }

    public bool Revive()
    {
        return Revive(maxHealth * reviveHealthPercent);
    }

    public bool Revive(float revivedHealth)
    {
        if (!CanWriteState())
            return false;

        if (!isKnockedOut || isDead || deathTransformationApplied)
            return false;

        isKnockedOut = false;
        knockoutTimer = 0f;
        currentHealth = Mathf.Clamp(revivedHealth, 1f, maxHealth);
        RestoreConfiguredComponents();
        ApplyLocalControlState();
        SyncAllState();
        OnRevived?.Invoke(this);
        return true;
    }

    public void Die()
    {
        if (!CanWriteState())
            return;

        ApplyDeath(false);
    }

    public void RequestImmediateDeath()
    {
        if (IsClientReplica())
        {
            RequestImmediateDeathServerRpc();
            return;
        }

        ApplyDeath(false);
    }

    public void DebugResurrect()
    {
        if (IsClientReplica())
        {
            DebugResurrectServerRpc();
            return;
        }

        ApplyDebugResurrection();
    }

    public void ApplyDeathTransformation()
    {
        if (!CanWriteState())
            return;

        ApplyDeath(true);
    }

    public bool ConsumeWater(float amount)
    {
        if (!CanAct()) return false;
        if (amount <= 0f) return false;

        if (debugInfiniteWater)
        {
            currentWater = maxWater;
            SyncWaterState();
            return true;
        }

        if (currentWater <= 0f) return false;

        if (IsClientReplica())
        {
            PredictConsumeWater(amount);
            ConsumeWaterServerRpc(amount);
            return true;
        }

        ApplyConsumeWater(amount);
        SyncWaterState();
        return true;
    }

    public void SetDebugInfiniteWater(bool value)
    {
        debugInfiniteWater = value;

        if (debugInfiniteWater && currentWater < maxWater)
        {
            currentWater = maxWater;
            SyncWaterState();
        }
    }

    public void DebugFillWater(WaterQuality quality = WaterQuality.Clean)
    {
        currentWater = maxWater;
        SetWaterQuality(quality);
        SyncWaterState();
    }

    public void SetDebugInfiniteHealth(bool value)
    {
        debugInfiniteHealth = value;

        if (!debugInfiniteHealth || IsClientReplica() || isDead || deathTransformationApplied)
            return;

        if (isKnockedOut)
        {
            Revive(maxHealth);
            return;
        }

        if (currentHealth < maxHealth)
        {
            currentHealth = maxHealth;
            SyncCoreState();
        }
    }

    public void DebugFillHealth()
    {
        if (IsClientReplica() || isDead || deathTransformationApplied)
            return;

        if (isKnockedOut)
        {
            Revive(maxHealth);
            return;
        }

        currentHealth = maxHealth;
        SyncCoreState();
    }

    void ApplyDebugResurrection()
    {
        bool wasDeadOrKnockedOut = isDead || isKnockedOut || deathTransformationApplied;

        isDead = false;
        isKnockedOut = false;
        deathTransformationApplied = false;
        knockoutTimer = 0f;
        currentHealth = maxHealth;

        RestoreDeathPresentation();
        RestoreConfiguredComponents();
        RestoreToolObjectsForDebugResurrection();
        ApplyLocalControlState();
        SyncAllState();

        if (wasDeadOrKnockedOut)
            OnRevived?.Invoke(this);
    }

    public bool AddWater(
        float amount,
        WaterQuality quality,
        bool replaceExistingQuality = false)
    {
        if (!CanAct()) return false;
        if (amount <= 0f || currentWater >= maxWater) return false;

        if (IsClientReplica())
        {
            PredictAddWater(amount, quality, replaceExistingQuality);
            AddWaterServerRpc(amount, (int)quality, replaceExistingQuality);
            return true;
        }

        ApplyAddWater(amount, quality, replaceExistingQuality);
        SyncWaterState();
        return true;
    }

    public void ContaminateWater()
    {
        if (!CanAct()) return;
        if (currentWater <= 0f) return;

        if (IsClientReplica())
        {
            SetWaterQuality(WaterQuality.Contaminated);
            ContaminateWaterServerRpc();
            return;
        }

        SetWaterQuality(WaterQuality.Contaminated);
        SyncWaterState();
    }

    public void PurifyWater()
    {
        if (!CanAct()) return;
        if (currentWater <= 0f) return;

        if (IsClientReplica())
        {
            SetWaterQuality(WaterQuality.Clean);
            PurifyWaterServerRpc();
            return;
        }

        SetWaterQuality(WaterQuality.Clean);
        SyncWaterState();
    }

    public void AddExternalControlLock()
    {
        if (IsClientReplica())
        {
            AddExternalControlLockServerRpc();
            return;
        }

        externalControlLocks = Mathf.Max(0, externalControlLocks + 1);
        ApplyLocalControlState();
        SyncControlLockState();
    }

    public void RemoveExternalControlLock()
    {
        if (IsClientReplica())
        {
            RemoveExternalControlLockServerRpc();
            return;
        }

        externalControlLocks = Mathf.Max(0, externalControlLocks - 1);
        ApplyLocalControlState();
        SyncControlLockState();
    }

    public void ClearExternalControlLocks()
    {
        if (IsClientReplica())
            return;

        externalControlLocks = 0;
        ApplyLocalControlState();
        SyncControlLockState();
    }

    public float GetWaterCleaningMultiplier()
    {
        if (currentWaterQuality == WaterQuality.Contaminated)
            return contaminatedCleaningMultiplier;

        if (currentWaterQuality == WaterQuality.ChemicallyEnhanced)
            return chemicallyEnhancedCleaningMultiplier;

        return 1f;
    }

    bool ApplyDamage(float damage)
    {
        if (damage <= 0f || isDead || isKnockedOut || deathTransformationApplied)
            return false;

        if (debugInfiniteHealth)
        {
            currentHealth = maxHealth;
            SyncCoreState();
            return false;
        }

        currentHealth -= damage;
        currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

        if (currentHealth > 0f)
        {
            SyncCoreState();
            return false;
        }

        ApplyEnterKnockout();
        return true;
    }

    void ApplyEnterKnockout()
    {
        if (isDead || deathTransformationApplied || isKnockedOut) return;

        isKnockedOut = true;
        currentHealth = 0f;
        knockoutTimer = knockoutDuration;

        if (disableControlsWhileKnockedOut)
            DisableKnockoutBlockedComponents();

        ApplyLocalControlState();
        SyncAllState();
        OnKnockedOut?.Invoke(this);
    }

    void ApplyDeath(bool transformed)
    {
        if (isDead && (!transformed || deathTransformationApplied))
            return;

        bool wasDead = isDead;
        isKnockedOut = false;
        isDead = true;
        deathTransformationApplied = deathTransformationApplied || transformed;
        currentHealth = 0f;
        knockoutTimer = 0f;

        DisableConfiguredComponents();
        ApplyDeathPresentation();

        if (deathTransformationApplied)
            ApplyDeathTransformationPresentation();

        ApplyLocalControlState();
        SyncAllState();

        if (!wasDead)
            OnDeath?.Invoke(this);
    }

    void ApplyConsumeWater(float amount)
    {
        currentWater -= amount;
        currentWater = Mathf.Clamp(currentWater, 0f, maxWater);

        if (currentWater <= 0f)
            SetWaterQuality(WaterQuality.Clean);
    }

    void ApplyAddWater(
        float amount,
        WaterQuality quality,
        bool replaceExistingQuality)
    {
        bool wasEmpty = currentWater <= 0f;
        bool wasEffectivelyEmpty =
            currentWater <= Mathf.Max(0f, emptyWaterQualityThreshold);
        currentWater += amount;
        currentWater = Mathf.Clamp(currentWater, 0f, maxWater);

        if (wasEmpty ||
            wasEffectivelyEmpty ||
            replaceExistingQuality ||
            quality == WaterQuality.Contaminated)
        {
            SetWaterQuality(quality);
        }
        else if (currentWaterQuality != WaterQuality.Contaminated &&
            quality == WaterQuality.ChemicallyEnhanced)
        {
            SetWaterQuality(WaterQuality.ChemicallyEnhanced);
        }
    }

    void PredictConsumeWater(float amount)
    {
        ApplyConsumeWater(amount);
    }

    void PredictAddWater(
        float amount,
        WaterQuality quality,
        bool replaceExistingQuality)
    {
        ApplyAddWater(amount, quality, replaceExistingQuality);
    }

    void ApplyDeathPresentation()
    {
        DisableGravityAndMotion();
        DisableDeathHud();

        if (makeBodyTransparentOnDeath)
            MakeBodyTransparent(deadBodyAlpha);

        ActivateSpectatorMode();
    }

    void ApplyDeathTransformationPresentation()
    {
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

    void RestoreDeathPresentation()
    {
        RestoreGravityAndMotion();
        RestoreDeathHud();
        RestoreBodyVisibility();
        EndSpectatorMode();
    }

    void RestoreGravityAndMotion()
    {
        if (!disableGravityOnDeath) return;

        Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] == null) continue;
            bodies[i].useGravity = true;
            bodies[i].isKinematic = false;
            bodies[i].linearVelocity = Vector3.zero;
            bodies[i].angularVelocity = Vector3.zero;
        }
    }

    void RestoreDeathHud()
    {
        if (!ShouldApplyOwnerLocalState())
            return;

        if (hudsToDisableOnDeath != null && hudsToDisableOnDeath.Length > 0)
        {
            for (int i = 0; i < hudsToDisableOnDeath.Length; i++)
                EnableHud(hudsToDisableOnDeath[i]);

            return;
        }

        HUD[] huds = FindObjectsByType<HUD>(FindObjectsInactive.Include);
        for (int i = 0; i < huds.Length; i++)
        {
            if (huds[i] != null && huds[i].playerStatus == this)
                EnableHud(huds[i]);
        }
    }

    void EnableHud(HUD hud)
    {
        if (hud == null) return;

        Canvas canvas = hud.GetComponent<Canvas>();
        if (canvas != null)
            canvas.enabled = true;
        else
            hud.gameObject.SetActive(true);
    }

    void RestoreBodyVisibility()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer targetRenderer = renderers[i];
            if (targetRenderer == null) continue;
            if (ShouldIgnoreDeathPresentationRenderer(targetRenderer)) continue;

            targetRenderer.enabled = true;
            Material[] materials = targetRenderer.materials;
            for (int m = 0; m < materials.Length; m++)
                RestoreMaterialAlpha(materials[m]);
        }

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = true;
        }
    }

    void RestoreMaterialAlpha(Material material)
    {
        if (material == null) return;

        if (material.HasProperty("_BaseColor"))
        {
            Color color = material.GetColor("_BaseColor");
            color.a = 1f;
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            Color color = material.GetColor("_Color");
            color.a = 1f;
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 0f);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 1f);

        material.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = -1;
    }

    void EndSpectatorMode()
    {
        if (!ShouldApplyOwnerLocalState())
            return;

        PlayerSpectatorMode spectator = FindFirstObjectByType<PlayerSpectatorMode>();
        if (spectator != null)
            spectator.EndSpectating();
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
        if (!ShouldApplyOwnerLocalState())
            return;

        if (hudsToDisableOnDeath != null && hudsToDisableOnDeath.Length > 0)
        {
            for (int i = 0; i < hudsToDisableOnDeath.Length; i++)
                DisableHud(hudsToDisableOnDeath[i]);

            return;
        }

        HUD[] huds = FindObjectsByType<HUD>(FindObjectsInactive.Exclude);
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
            if (ShouldIgnoreDeathPresentationRenderer(targetRenderer)) continue;

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

    bool ShouldIgnoreDeathPresentationRenderer(Renderer targetRenderer)
    {
        if (targetRenderer == null)
            return true;

        if (targetRenderer is ParticleSystemRenderer)
            return true;

        if (targetRenderer.GetComponentInParent<WaterCannon>() != null)
            return true;

        if (targetRenderer.GetComponentInParent<JennyMopCleaner>() != null)
            return true;

        return false;
    }

    void ActivateSpectatorMode()
    {
        if (!ShouldApplyOwnerLocalState())
            return;

        PlayerSpectatorMode.ActivateFor(this);
    }

    void DisableKnockoutBlockedComponents()
    {
        if (!ShouldApplyOwnerLocalState())
            return;

        CacheReferences();

        if (inventory != null)
            inventory.enabled = false;

        if (waterCannon != null)
            waterCannon.enabled = false;
    }

    void DisableConfiguredComponents()
    {
        if (!ShouldApplyOwnerLocalState())
            return;

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

        CacheReferences();

        if (movement != null) movement.enabled = false;
        if (inventory != null) inventory.enabled = false;
        if (waterCannon != null) waterCannon.enabled = false;
    }

    void RestoreConfiguredComponents()
    {
        if (!ShouldApplyOwnerLocalState())
            return;
        if (isDead || deathTransformationApplied)
            return;

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

        CacheReferences();

        if (movement != null)
            movement.enabled = true;
        if (inventory != null)
            inventory.enabled = CanAct();
        if (waterCannon != null)
            waterCannon.enabled = CanUseWaterCannon();
    }

    void RestoreToolObjectsForDebugResurrection()
    {
        if (!ShouldApplyOwnerLocalState())
            return;

        bool canUseWaterCannon = CanUseWaterCannon();
        WaterCannon[] waterCannons = GetComponentsInChildren<WaterCannon>(true);
        for (int i = 0; i < waterCannons.Length; i++)
        {
            WaterCannon cannon = waterCannons[i];
            if (cannon == null)
                continue;

            if (canUseWaterCannon && !cannon.gameObject.activeSelf)
                cannon.gameObject.SetActive(true);

            cannon.enabled = canUseWaterCannon;
        }

        PlayerAgentLoadout loadout = GetComponent<PlayerAgentLoadout>();
        if (loadout != null)
            loadout.ApplyAgent(loadout.currentAgent);

        CacheReferences();
    }

    void ApplyLocalControlState()
    {
        if (!ShouldApplyOwnerLocalState())
            return;

        CacheReferences();

        if (movement != null)
            movement.SetAcceptsInput(AllowsLocalInput());

        bool canUseTools = CanAct();
        if (inventory != null)
            inventory.enabled = canUseTools;
        if (waterCannon != null)
            waterCannon.enabled = CanUseWaterCannon();
    }

    bool CanUseWaterCannon()
    {
        return CanAct() &&
            !PlayerAgentLoadout.ShouldDisableWaterCannonFor(gameObject);
    }

    bool ShouldApplyOwnerLocalState()
    {
        return !IsNetworked() || IsOwner;
    }

    void SetWaterQuality(WaterQuality quality)
    {
        if (currentWaterQuality == quality) return;
        currentWaterQuality = quality;
        OnWaterQualityChanged?.Invoke(currentWaterQuality);
    }

    void SubscribeNetworkState()
    {
        syncedHealth.OnValueChanged += HandleHealthChanged;
        syncedWater.OnValueChanged += HandleWaterChanged;
        syncedKnockoutTimer.OnValueChanged += HandleKnockoutTimerChanged;
        syncedWaterQuality.OnValueChanged += HandleWaterQualityChanged;
        syncedKnockedOut.OnValueChanged += HandleKnockoutChanged;
        syncedDead.OnValueChanged += HandleDeadChanged;
        syncedTransformed.OnValueChanged += HandleTransformedChanged;
        syncedExternalControlLocks.OnValueChanged += HandleExternalControlLocksChanged;
    }

    void UnsubscribeNetworkState()
    {
        syncedHealth.OnValueChanged -= HandleHealthChanged;
        syncedWater.OnValueChanged -= HandleWaterChanged;
        syncedKnockoutTimer.OnValueChanged -= HandleKnockoutTimerChanged;
        syncedWaterQuality.OnValueChanged -= HandleWaterQualityChanged;
        syncedKnockedOut.OnValueChanged -= HandleKnockoutChanged;
        syncedDead.OnValueChanged -= HandleDeadChanged;
        syncedTransformed.OnValueChanged -= HandleTransformedChanged;
        syncedExternalControlLocks.OnValueChanged -= HandleExternalControlLocksChanged;
    }

    void ApplySyncedState(bool invokeEvents)
    {
        currentHealth = syncedHealth.Value;
        currentWater = syncedWater.Value;
        knockoutTimer = syncedKnockoutTimer.Value;
        currentWaterQuality = (WaterQuality)syncedWaterQuality.Value;
        externalControlLocks = Mathf.Max(0, syncedExternalControlLocks.Value);

        ApplySyncedKnockout(syncedKnockedOut.Value, invokeEvents);
        ApplySyncedDead(syncedDead.Value, invokeEvents);
        ApplySyncedTransformed(syncedTransformed.Value, invokeEvents);
        ApplyLocalControlState();
        localStateInitialized = true;
    }

    void HandleHealthChanged(float previous, float next)
    {
        currentHealth = next;
    }

    void HandleWaterChanged(float previous, float next)
    {
        currentWater = next;
    }

    void HandleKnockoutTimerChanged(float previous, float next)
    {
        knockoutTimer = next;
    }

    void HandleWaterQualityChanged(int previous, int next)
    {
        SetWaterQuality((WaterQuality)next);
    }

    void HandleKnockoutChanged(bool previous, bool next)
    {
        ApplySyncedKnockout(next, true);
    }

    void HandleDeadChanged(bool previous, bool next)
    {
        ApplySyncedDead(next, true);
    }

    void HandleTransformedChanged(bool previous, bool next)
    {
        ApplySyncedTransformed(next, true);
    }

    void HandleExternalControlLocksChanged(int previous, int next)
    {
        externalControlLocks = Mathf.Max(0, next);
        ApplyLocalControlState();
    }

    void ApplySyncedKnockout(bool next, bool invokeEvents)
    {
        bool wasKnockedOut = isKnockedOut;
        isKnockedOut = next;

        if (isKnockedOut && !wasKnockedOut)
        {
            if (disableControlsWhileKnockedOut)
                DisableKnockoutBlockedComponents();

            if (invokeEvents)
                OnKnockedOut?.Invoke(this);
        }
        else if (!isKnockedOut && wasKnockedOut && !isDead && !deathTransformationApplied)
        {
            RestoreConfiguredComponents();

            if (invokeEvents)
                OnRevived?.Invoke(this);
        }

        ApplyLocalControlState();
    }

    void ApplySyncedDead(bool next, bool invokeEvents)
    {
        bool wasDead = isDead;
        isDead = next;

        if (isDead && !wasDead)
        {
            isKnockedOut = false;
            currentHealth = 0f;
            knockoutTimer = 0f;
            DisableConfiguredComponents();
            ApplyDeathPresentation();

            if (invokeEvents)
                OnDeath?.Invoke(this);
        }

        ApplyLocalControlState();
    }

    void ApplySyncedTransformed(bool next, bool invokeEvents)
    {
        bool wasTransformed = deathTransformationApplied;
        deathTransformationApplied = next;

        if (deathTransformationApplied && !wasTransformed)
        {
            if (!isDead)
                ApplySyncedDead(true, invokeEvents);

            ApplyDeathTransformationPresentation();
        }

        ApplyLocalControlState();
    }

    void SyncCoreState()
    {
        if (!IsSpawned || !IsServer)
            return;

        syncedHealth.Value = currentHealth;
        syncedKnockoutTimer.Value = knockoutTimer;
        syncedKnockedOut.Value = isKnockedOut;
        syncedDead.Value = isDead;
        syncedTransformed.Value = deathTransformationApplied;
    }

    void SyncWaterState()
    {
        if (!IsSpawned || !IsServer)
            return;

        syncedWater.Value = currentWater;
        syncedWaterQuality.Value = (int)currentWaterQuality;
    }

    void SyncControlLockState()
    {
        if (!IsSpawned || !IsServer)
            return;

        syncedExternalControlLocks.Value = externalControlLocks;
    }

    void SyncAllState()
    {
        SyncCoreState();
        SyncWaterState();
        SyncControlLockState();
    }

    [ServerRpc]
    void TakeDamageServerRpc(float damage)
    {
        ApplyDamage(damage);
    }

    [ServerRpc]
    void RequestImmediateDeathServerRpc()
    {
        ApplyDeath(false);
    }

    [ServerRpc(RequireOwnership = false)]
    void DebugResurrectServerRpc()
    {
        ApplyDebugResurrection();
    }

    [ServerRpc]
    void ConsumeWaterServerRpc(float amount)
    {
        if (!CanAct() || amount <= 0f || currentWater <= 0f)
            return;

        ApplyConsumeWater(amount);
        SyncWaterState();
    }

    [ServerRpc]
    void AddWaterServerRpc(
        float amount,
        int quality,
        bool replaceExistingQuality)
    {
        if (!CanAct() || amount <= 0f || currentWater >= maxWater)
            return;

        ApplyAddWater(amount, (WaterQuality)quality, replaceExistingQuality);
        SyncWaterState();
    }

    [ServerRpc]
    void ContaminateWaterServerRpc()
    {
        if (!CanAct() || currentWater <= 0f)
            return;

        SetWaterQuality(WaterQuality.Contaminated);
        SyncWaterState();
    }

    [ServerRpc]
    void PurifyWaterServerRpc()
    {
        if (!CanAct() || currentWater <= 0f)
            return;

        SetWaterQuality(WaterQuality.Clean);
        SyncWaterState();
    }

    [ServerRpc]
    void AddExternalControlLockServerRpc()
    {
        AddExternalControlLock();
    }

    [ServerRpc]
    void RemoveExternalControlLockServerRpc()
    {
        RemoveExternalControlLock();
    }
}
