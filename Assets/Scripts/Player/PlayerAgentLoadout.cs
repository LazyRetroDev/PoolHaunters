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
    public bool enforceToolState = true;
    public JennyMopCleaner jennyMop;

    [Header("Optional UI")]
    public TMP_Text agentNameText;

    private WaterCannon[] waterCannons;

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
        }
    }

    void ResolveReferences()
    {
        if (jennyMop == null)
            jennyMop = GetComponent<JennyMopCleaner>();

        waterCannons = GetComponentsInChildren<WaterCannon>(true);
    }
}
