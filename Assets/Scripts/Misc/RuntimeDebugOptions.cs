using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class RuntimeDebugOptions : MonoBehaviour
{
    [Header("Availability")]
    public bool enableInReleaseBuild = false;
    public Key toggleKey = Key.F1;

    [Header("Resource Toggles")]
    public bool infiniteWater;
    public bool infiniteStamina;

    [Header("Character")]
    public bool persistDebugCharacterSelection = true;

    [Header("Window")]
    public Rect windowRect = new Rect(24f, 80f, 320f, 440f);

    private bool visible;
    private bool cursorUnlockRequested;
    private Vector2 scrollPosition;

    void Update()
    {
        if (!IsAllowed())
            return;

        if (toggleKey != Key.None &&
            Keyboard.current != null &&
            Keyboard.current[toggleKey].wasPressedThisFrame)
        {
            SetVisible(!visible);
        }

        ApplyResourceToggles();
    }

    void OnDisable()
    {
        ReleaseDebugCursor();
    }

    void OnDestroy()
    {
        ReleaseDebugCursor();
    }

    void OnGUI()
    {
        if (!IsAllowed() || !visible)
            return;

        windowRect = GUILayout.Window(
            GetInstanceID(),
            windowRect,
            DrawWindow,
            "Debug Options");
    }

    void DrawWindow(int windowId)
    {
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        GUILayout.Label("Resources");
        bool nextInfiniteWater = GUILayout.Toggle(infiniteWater, "Infinite Water");
        if (nextInfiniteWater != infiniteWater)
        {
            infiniteWater = nextInfiniteWater;
            ApplyResourceToggles();
        }

        bool nextInfiniteStamina = GUILayout.Toggle(infiniteStamina, "Infinite Stamina");
        if (nextInfiniteStamina != infiniteStamina)
        {
            infiniteStamina = nextInfiniteStamina;
            ApplyResourceToggles();
        }

        if (GUILayout.Button("Fill Water"))
            FillWater();

        if (GUILayout.Button("Fill Stamina"))
            FillStamina();

        GUILayout.Space(12f);
        GUILayout.Label("Objectives");

        if (GUILayout.Button("Activate Water Valve"))
            ActivateWaterValve();

        if (GUILayout.Button("Clean All Pools"))
            CleanAllPools();

        if (GUILayout.Button("Clean All Dirt"))
            CleanAllDirt();

        if (GUILayout.Button("Complete Objectives"))
            CompleteObjectives();

        DrawObjectiveSummary();

        GUILayout.Space(12f);
        GUILayout.Label("Character");
        DrawCharacterButton(PlayerAgentType.JennyPie);
        DrawCharacterButton(PlayerAgentType.Sylvian);
        DrawCharacterButton(PlayerAgentType.SecretAgent);
        DrawCharacterButton(PlayerAgentType.Louise);

        GUILayout.Space(12f);
        if (GUILayout.Button("Close"))
            SetVisible(false);

        GUILayout.EndScrollView();
        GUI.DragWindow(new Rect(0f, 0f, 10000f, 24f));
    }

    void SetVisible(bool value)
    {
        if (visible == value)
            return;

        visible = value;

        if (visible)
        {
            if (!cursorUnlockRequested)
            {
                CursorLockController.RequestCursorUnlocked();
                cursorUnlockRequested = true;
            }
        }
        else
        {
            ReleaseDebugCursor();
            ForceLockAnyLocalCursor();
        }
    }

    void ReleaseDebugCursor()
    {
        if (!cursorUnlockRequested)
            return;

        CursorLockController.ReleaseCursorUnlocked();
        cursorUnlockRequested = false;
    }

    void ForceLockAnyLocalCursor()
    {
        CursorLockController[] cursorLocks =
            FindObjectsByType<CursorLockController>(FindObjectsInactive.Exclude);
        for (int i = 0; i < cursorLocks.Length; i++)
        {
            if (cursorLocks[i] != null)
            {
                cursorLocks[i].ForceLockCursor();
                return;
            }
        }
    }

    void ApplyResourceToggles()
    {
        PlayerStatus[] statuses =
            FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);
        for (int i = 0; i < statuses.Length; i++)
        {
            if (statuses[i] != null)
                statuses[i].SetDebugInfiniteWater(infiniteWater);
        }

        PlayerMovement[] movements =
            FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude);
        for (int i = 0; i < movements.Length; i++)
        {
            if (movements[i] != null)
                movements[i].SetDebugInfiniteStamina(infiniteStamina);
        }
    }

    void FillWater()
    {
        PlayerStatus[] statuses =
            FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);
        for (int i = 0; i < statuses.Length; i++)
        {
            if (statuses[i] != null)
                statuses[i].DebugFillWater(WaterQuality.Clean);
        }
    }

    void FillStamina()
    {
        PlayerMovement[] movements =
            FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude);
        for (int i = 0; i < movements.Length; i++)
        {
            if (movements[i] != null)
                movements[i].DebugFillStamina();
        }
    }

    void ActivateWaterValve()
    {
        if (LevelObjectiveManager.Instance != null)
            LevelObjectiveManager.Instance.ActivateWaterValve();
    }

    void CleanAllPools()
    {
        if (LevelObjectiveManager.Instance != null)
        {
            LevelObjectiveManager.Instance.DebugCleanAllPools();
            return;
        }

        SwimmingPoolObjective[] pools =
            FindObjectsByType<SwimmingPoolObjective>(FindObjectsInactive.Include);
        for (int i = 0; i < pools.Length; i++)
        {
            if (pools[i] != null)
                pools[i].ForceClean();
        }
    }

    void CleanAllDirt()
    {
        if (LevelObjectiveManager.Instance != null)
        {
            LevelObjectiveManager.Instance.DebugCleanAllDirt();
            return;
        }

        DirtSpot[] dirtSpots =
            FindObjectsByType<DirtSpot>(FindObjectsInactive.Include);
        for (int i = 0; i < dirtSpots.Length; i++)
        {
            if (dirtSpots[i] != null)
                dirtSpots[i].ForceClean();
        }
    }

    void CompleteObjectives()
    {
        if (LevelObjectiveManager.Instance != null)
            LevelObjectiveManager.Instance.DebugCompleteObjectives();
    }

    void DrawCharacterButton(PlayerAgentType agent)
    {
        string displayName = AgentSelectionState.GetDisplayName(agent);
        bool selected = AgentSelectionState.SelectedAgent == agent;
        string label = selected ? $"{displayName} (Selected)" : displayName;

        if (GUILayout.Button(label))
            SwitchCharacter(agent);
    }

    void SwitchCharacter(PlayerAgentType agent)
    {
        if (persistDebugCharacterSelection)
            AgentSelectionState.Select(agent);

        PlayerAgentLoadout[] loadouts =
            FindObjectsByType<PlayerAgentLoadout>(FindObjectsInactive.Exclude);

        if (loadouts.Length == 0)
        {
            PlayerStatus[] players =
                FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);
            for (int i = 0; i < players.Length; i++)
            {
                if (players[i] == null)
                    continue;

                PlayerAgentLoadout loadout =
                    players[i].GetComponent<PlayerAgentLoadout>();
                if (loadout == null)
                    loadout = players[i].gameObject.AddComponent<PlayerAgentLoadout>();

                loadout.ApplyAgent(agent);
            }

            return;
        }

        for (int i = 0; i < loadouts.Length; i++)
        {
            if (loadouts[i] != null)
                loadouts[i].ApplyAgent(agent);
        }
    }

    void DrawObjectiveSummary()
    {
        LevelObjectiveManager objectiveManager = LevelObjectiveManager.Instance;
        if (objectiveManager == null)
        {
            GUILayout.Label("No LevelObjectiveManager found.");
            return;
        }

        GUILayout.Label($"Valve: {(objectiveManager.WaterValveActivated ? "On" : "Off")}");
        GUILayout.Label($"Pools: {objectiveManager.CleanedRequiredPoolCount}/{objectiveManager.RequiredPoolCount}");
        GUILayout.Label($"Pool progress: {Mathf.RoundToInt(objectiveManager.CurrentPoolCleanPercent * 100f)}%");
        GUILayout.Label($"Completed: {(objectiveManager.LevelCompleted ? "Yes" : "No")}");
    }

    bool IsAllowed()
    {
        return enableInReleaseBuild || Application.isEditor || Debug.isDebugBuild;
    }
}
