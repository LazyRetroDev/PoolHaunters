using UnityEngine;

[DisallowMultipleComponent]
public class RunWaterSystemController : MonoBehaviour
{
    [Header("State")]
    [SerializeField] private bool phaseStartsWithDryWaterSources = true;
    [SerializeField] private bool activateWaterSourcesWhenValveTurns = true;
    [SerializeField] private WaterQuality activatedWaterSourceQuality = WaterQuality.Clean;

    [Header("Auto Find")]
    [SerializeField] private bool autoFindWaterSources = true;
    [SerializeField, Min(0.05f)] private float refreshInterval = 0.5f;

    [Header("Debug")]
    [SerializeField] private bool waterSystemActivated;
    [SerializeField] private int knownWaterSourceCount;

    private float refreshTimer;
    private LevelObjectiveManager objectiveManager;

    void OnEnable()
    {
        BindObjectiveManager();
        ApplyCurrentWaterState();
    }

    void OnDisable()
    {
        if (objectiveManager != null)
            objectiveManager.OnWaterValveActivated -= HandleWaterValveActivated;
    }

    void Start()
    {
        ApplyCurrentWaterState();
    }

    void Update()
    {
        if (!autoFindWaterSources)
            return;

        refreshTimer -= Time.deltaTime;
        if (refreshTimer > 0f)
            return;

        refreshTimer = Mathf.Max(0.05f, refreshInterval);
        BindObjectiveManager();
        ApplyCurrentWaterState();
    }

    void BindObjectiveManager()
    {
        LevelObjectiveManager nextManager = LevelObjectiveManager.Instance;
        if (objectiveManager == nextManager)
            return;

        if (objectiveManager != null)
            objectiveManager.OnWaterValveActivated -= HandleWaterValveActivated;

        objectiveManager = nextManager;

        if (objectiveManager != null)
        {
            objectiveManager.OnWaterValveActivated += HandleWaterValveActivated;
            if (objectiveManager.WaterValveActivated)
                waterSystemActivated = true;
        }
    }

    void HandleWaterValveActivated()
    {
        waterSystemActivated = true;
        ApplyCurrentWaterState();
    }

    void ApplyCurrentWaterState()
    {
        if (waterSystemActivated)
        {
            if (activateWaterSourcesWhenValveTurns)
                RestoreWaterSources();

            return;
        }

        if (objectiveManager != null && objectiveManager.WaterValveActivated)
        {
            waterSystemActivated = true;
            ApplyCurrentWaterState();
            return;
        }

        if (phaseStartsWithDryWaterSources)
            DryWaterSources();
    }

    void DryWaterSources()
    {
        WaterSourceDryable[] sources =
            FindObjectsByType<WaterSourceDryable>(FindObjectsInactive.Include);
        knownWaterSourceCount = sources.Length;

        for (int i = 0; i < sources.Length; i++)
        {
            WaterSourceDryable source = sources[i];
            if (source == null)
                continue;

            source.startsDry = true;
            source.DryOut();
        }
    }

    void RestoreWaterSources()
    {
        WaterSourceDryable[] sources =
            FindObjectsByType<WaterSourceDryable>(FindObjectsInactive.Include);
        knownWaterSourceCount = sources.Length;

        for (int i = 0; i < sources.Length; i++)
        {
            WaterSourceDryable source = sources[i];
            if (source == null)
                continue;

            source.startsDry = false;
            source.SetQuality(activatedWaterSourceQuality);
            source.RestoreWater();
        }
    }
}
