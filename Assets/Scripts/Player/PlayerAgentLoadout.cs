using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerAgentLoadout : MonoBehaviour
{
    [Header("Selection")]
    public PlayerAgentType currentAgent = PlayerAgentType.JennyPie;
    public bool applySelectionOnStart = true;

    [Header("Jenny Pie")]
    public bool jennyUsesMopInsteadOfWaterCannon = true;
    public bool hideWaterCannonObjectForJenny = true;
    public bool enforceToolState = true;
    public JennyMopCleaner jennyMop;

    [Header("Optional UI")]
    public TMP_Text agentNameText;

    private WaterCannon[] waterCannons;
    private bool[] originalWaterCannonObjectStates;

    void Awake()
    {
        ResolveReferences();
    }

    void Start()
    {
        if (applySelectionOnStart)
            ApplySelectedAgent();
    }

    void LateUpdate()
    {
        if (enforceToolState)
            ApplyToolState();
    }

    public void ApplySelectedAgent()
    {
        ApplyAgent(AgentSelectionState.SelectedAgent);
    }

    public void ApplyAgent(PlayerAgentType agent)
    {
        currentAgent = agent;
        ResolveReferences();

        bool isJenny = currentAgent == PlayerAgentType.JennyPie;

        if (isJenny && jennyMop == null)
        {
            jennyMop = gameObject.AddComponent<JennyMopCleaner>();
            ResolveReferences();
        }

        ApplyToolState();

        if (agentNameText != null)
            agentNameText.text = AgentSelectionState.GetDisplayName(currentAgent);
    }

    void ApplyToolState()
    {
        bool isJenny = currentAgent == PlayerAgentType.JennyPie;

        if (jennyMop != null && jennyMop.enabled != isJenny)
            jennyMop.enabled = isJenny;

        if (waterCannons == null || !jennyUsesMopInsteadOfWaterCannon)
            return;

        for (int i = 0; i < waterCannons.Length; i++)
        {
            if (waterCannons[i] == null) continue;

            bool shouldEnable = !isJenny;
            if (waterCannons[i].enabled != shouldEnable)
                waterCannons[i].enabled = shouldEnable;

            if (!hideWaterCannonObjectForJenny ||
                waterCannons[i].gameObject == gameObject)
            {
                continue;
            }

            bool shouldBeActive = !isJenny;
            if (waterCannons[i].gameObject.activeSelf != shouldBeActive)
                waterCannons[i].gameObject.SetActive(shouldBeActive);
        }
    }

    void ResolveReferences()
    {
        if (jennyMop == null)
            jennyMop = GetComponent<JennyMopCleaner>();

        waterCannons = GetComponentsInChildren<WaterCannon>(true);
        if (originalWaterCannonObjectStates == null ||
            originalWaterCannonObjectStates.Length != waterCannons.Length)
        {
            originalWaterCannonObjectStates = new bool[waterCannons.Length];
            for (int i = 0; i < waterCannons.Length; i++)
            {
                originalWaterCannonObjectStates[i] =
                    waterCannons[i] != null && waterCannons[i].gameObject.activeSelf;
            }
        }
    }

    public bool ShouldDisableWaterCannon()
    {
        return jennyUsesMopInsteadOfWaterCannon &&
            currentAgent == PlayerAgentType.JennyPie;
    }

    public static bool ShouldDisableWaterCannonFor(GameObject player)
    {
        if (player == null)
            return false;

        PlayerAgentLoadout loadout = player.GetComponent<PlayerAgentLoadout>();
        return loadout != null && loadout.ShouldDisableWaterCannon();
    }
}
