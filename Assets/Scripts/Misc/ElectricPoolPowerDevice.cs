using UnityEngine;

[DisallowMultipleComponent]
public class ElectricPoolPowerDevice : PoolWaterReactive, IPlayerInteractable
{
    [Header("Device")]
    [SerializeField, Min(1f)] private float waterNeededToDisable = 25f;
    [SerializeField] private bool cleanWaterDisables = true;
    [SerializeField] private bool chemicalWaterDisablesFaster = true;

    [Header("Visuals")]
    [SerializeField] private GameObject poweredVisualRoot;
    [SerializeField] private GameObject disabledVisualRoot;
    [SerializeField] private Light poweredLight;
    [SerializeField] private Renderer[] tintRenderers = new Renderer[0];
    [SerializeField] private Color poweredColor = Color.yellow;
    [SerializeField] private Color disabledColor = Color.black;

    private ElectricSwimmingPoolMechanic pool;
    private float wetness;
    private bool powered;

    public void BindPool(ElectricSwimmingPoolMechanic owningPool)
    {
        pool = owningPool;
    }

    public void SetPowered(bool value)
    {
        powered = value;
        if (powered)
            wetness = 0f;

        if (poweredVisualRoot != null)
            poweredVisualRoot.SetActive(powered);
        if (disabledVisualRoot != null)
            disabledVisualRoot.SetActive(!powered);
        if (poweredLight != null)
            poweredLight.enabled = powered;

        ApplyTint();
    }

    public void Interact(PlayerInventory inventory)
    {
        if (!powered)
            return;

        pool?.DisablePowerTemporarily();
    }

    public override void ApplyPoolWaterHit(
        WaterQuality waterQuality,
        float waterPower,
        Vector3 sourcePosition)
    {
        if (!powered || waterPower <= 0f)
            return;
        if (waterQuality == WaterQuality.Clean && !cleanWaterDisables)
            return;
        if (waterQuality == WaterQuality.Contaminated)
            return;

        float multiplier = waterQuality == WaterQuality.ChemicallyEnhanced &&
            chemicalWaterDisablesFaster
            ? 1.5f
            : 1f;

        wetness += waterPower * multiplier;
        if (wetness >= waterNeededToDisable)
            pool?.DisablePowerTemporarily();
    }

    private void ApplyTint()
    {
        if (tintRenderers == null || tintRenderers.Length == 0)
            tintRenderers = GetComponentsInChildren<Renderer>(true);

        Color color = powered ? poweredColor : disabledColor;
        for (int i = 0; i < tintRenderers.Length; i++)
        {
            Renderer target = tintRenderers[i];
            if (target == null)
                continue;

            Material material = target.material;
            if (material == null)
                continue;

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
        }
    }
}
