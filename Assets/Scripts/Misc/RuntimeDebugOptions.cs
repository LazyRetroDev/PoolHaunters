using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class RuntimeDebugOptions : MonoBehaviour
{
    [Header("Availability")]
    public bool enableInReleaseBuild = false;
    public Key toggleKey = Key.F1;

    [Header("Resource Toggles")]
    public bool infiniteHealth;
    public bool infiniteWater;
    public bool infiniteStamina;
    public bool petrifyImmunity;
    public WaterQuality debugWaterQuality = WaterQuality.Clean;
    public int debugGermsAmount = 50;

    [Header("Enemy Debug")]
    public bool enemyTracking;
    public float enemyTrackingMaxDistance = 120f;
    public float enemyCompassWidth = 520f;
    public float enemyCompassTopOffset = 64f;

    [Header("Movement Debug")]
    public float debugSpeedMultiplier = 1f;
    public bool debugNoclip;

    [Header("Character")]
    public bool persistDebugCharacterSelection = true;

    [Header("Window")]
    public Rect windowRect = new Rect(24f, 80f, 320f, 440f);

    private bool visible;
    private bool cursorUnlockRequested;
    private Vector2 scrollPosition;
    private GUIStyle enemyCompassStyle;
    private GUIStyle enemyCompassCenterStyle;
    private GUIStyle gmodTextStyle;

    private static readonly string[] EnemyTypeNames =
    {
        "BathroomBlondeBehavior",
        "GhostWaterBehavior",
        "GoldenMouthBehavior",
        "Photographer",
        "RaccoonBehavior",
        "TimeCamper",
        "TubaraoBehavior",
        "VictoriaRegiaBehavior",
        "WillOWispBehavior"
    };

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

        ApplyDebugToggles();
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
        if (!IsAllowed())
            return;

        if (enemyTracking)
            DrawEnemyTrackingOverlay();

        if (visible)
        {
            windowRect = GUILayout.Window(
                GetInstanceID(),
                windowRect,
                DrawWindow,
                "Debug Options");
        }
    }

    void DrawWindow(int windowId)
    {
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        GUILayout.Label("Resources");
        bool nextInfiniteHealth = GUILayout.Toggle(infiniteHealth, "Infinite Health");
        if (nextInfiniteHealth != infiniteHealth)
        {
            infiniteHealth = nextInfiniteHealth;
            ApplyDebugToggles();
        }

        bool nextInfiniteWater = GUILayout.Toggle(infiniteWater, "Infinite Water");
        if (nextInfiniteWater != infiniteWater)
        {
            infiniteWater = nextInfiniteWater;
            ApplyDebugToggles();
        }

        bool nextInfiniteStamina = GUILayout.Toggle(infiniteStamina, "Infinite Stamina");
        if (nextInfiniteStamina != infiniteStamina)
        {
            infiniteStamina = nextInfiniteStamina;
            ApplyDebugToggles();
        }

        bool nextPetrifyImmunity = GUILayout.Toggle(petrifyImmunity, "Petrify Immunity");
        if (nextPetrifyImmunity != petrifyImmunity)
        {
            petrifyImmunity = nextPetrifyImmunity;
            ApplyDebugToggles();
        }

        GUILayout.Label($"Water Quality: {debugWaterQuality}");
        GUILayout.BeginHorizontal();
        DrawWaterQualityButton(WaterQuality.Clean);
        DrawWaterQualityButton(WaterQuality.Contaminated);
        DrawWaterQualityButton(WaterQuality.ChemicallyEnhanced);
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Fill Water"))
            FillWater();

        if (GUILayout.Button("Fill Stamina"))
            FillStamina();

        if (GUILayout.Button("Fill Health"))
            FillHealth();

        if (GUILayout.Button("Resurrect Player"))
            ResurrectPlayers();

        GUILayout.Label($"Germs: {PlayerCurrencyState.Germs}");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button($"+{debugGermsAmount} Germs"))
            PlayerCurrencyState.AddGerms(debugGermsAmount);
        if (GUILayout.Button("Reset Germs"))
            PlayerCurrencyState.ResetGerms();
        GUILayout.EndHorizontal();

        GUILayout.Space(12f);
        GUILayout.Label("Objectives");

        if (GUILayout.Button("Activate Water Valve"))
            ActivateWaterValve();

        if (GUILayout.Button("Start Storm"))
            StartStorm();

        if (GUILayout.Button("Spread Storm Now"))
            SpreadStormNow();

        if (GUILayout.Button("Start Flood"))
            StartFlood();

        if (GUILayout.Button("Reset Flood"))
            ResetFlood();

        if (GUILayout.Button("Clean All Pools"))
            CleanAllPools();

        if (GUILayout.Button("Clean All Dirt"))
            CleanAllDirt();

        if (GUILayout.Button("Complete Objectives"))
            CompleteObjectives();

        if (GUILayout.Button("Reveal Minimap"))
            RevealMinimap();

        DrawObjectiveSummary();

        GUILayout.Space(12f);
        GUILayout.Label("Enemy Debug");
        enemyTracking = GUILayout.Toggle(enemyTracking, "Enemy Tracking");
        GUILayout.Label($"Tracking range: {Mathf.RoundToInt(enemyTrackingMaxDistance)}m");
        enemyTrackingMaxDistance = GUILayout.HorizontalSlider(
            enemyTrackingMaxDistance,
            10f,
            250f);

        GUILayout.Space(12f);
        GUILayout.Label("Movement Debug");
        GUILayout.Label($"Speed: {debugSpeedMultiplier:0.0}x");
        float nextSpeedMultiplier = GUILayout.HorizontalSlider(
            debugSpeedMultiplier,
            0.5f,
            5f);
        if (!Mathf.Approximately(nextSpeedMultiplier, debugSpeedMultiplier))
        {
            debugSpeedMultiplier = nextSpeedMultiplier;
            ApplyDebugToggles();
        }

        bool nextNoclip = GUILayout.Toggle(debugNoclip, "Noclip");
        if (nextNoclip != debugNoclip)
        {
            debugNoclip = nextNoclip;
            ApplyDebugToggles();
        }

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

    void ApplyDebugToggles()
    {
        PlayerStatus[] statuses =
            FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);
        for (int i = 0; i < statuses.Length; i++)
        {
            if (statuses[i] != null)
            {
                statuses[i].SetDebugInfiniteHealth(infiniteHealth);
                statuses[i].SetDebugInfiniteWater(infiniteWater);
            }
        }

        PlayerMovement[] movements =
            FindObjectsByType<PlayerMovement>(FindObjectsInactive.Exclude);
        for (int i = 0; i < movements.Length; i++)
        {
            if (movements[i] != null)
            {
                movements[i].SetDebugInfiniteStamina(infiniteStamina);
                movements[i].SetDebugSpeedMultiplier(debugSpeedMultiplier);
                movements[i].SetDebugNoclip(debugNoclip);
            }
        }

        PlayerPetrify[] petrifyComponents =
            FindObjectsByType<PlayerPetrify>(FindObjectsInactive.Exclude);
        for (int i = 0; i < petrifyComponents.Length; i++)
        {
            if (petrifyComponents[i] != null)
                petrifyComponents[i].SetDebugPetrifyImmune(petrifyImmunity);
        }
    }

    void FillWater()
    {
        PlayerStatus[] statuses =
            FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);
        for (int i = 0; i < statuses.Length; i++)
        {
            if (statuses[i] != null)
                statuses[i].DebugFillWater(debugWaterQuality);
        }
    }

    void FillHealth()
    {
        PlayerStatus[] statuses =
            FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);
        for (int i = 0; i < statuses.Length; i++)
        {
            if (statuses[i] != null)
                statuses[i].DebugFillHealth();
        }
    }

    void ResurrectPlayers()
    {
        PlayerStatus[] statuses =
            FindObjectsByType<PlayerStatus>(FindObjectsInactive.Include);
        for (int i = 0; i < statuses.Length; i++)
        {
            if (statuses[i] != null)
                statuses[i].DebugResurrect();
        }
    }

    void DrawWaterQualityButton(WaterQuality quality)
    {
        string label = debugWaterQuality == quality
            ? $"{GetWaterQualityLabel(quality)} *"
            : GetWaterQualityLabel(quality);

        if (GUILayout.Button(label))
            debugWaterQuality = quality;
    }

    string GetWaterQualityLabel(WaterQuality quality)
    {
        switch (quality)
        {
            case WaterQuality.Contaminated:
                return "Dirty";
            case WaterQuality.ChemicallyEnhanced:
                return "Chemical";
            default:
                return "Clean";
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

    void StartStorm()
    {
        ContaminationStormController[] storms =
            FindObjectsByType<ContaminationStormController>(FindObjectsInactive.Exclude);
        for (int i = 0; i < storms.Length; i++)
        {
            if (storms[i] != null)
                storms[i].StartStorm();
        }
    }

    void SpreadStormNow()
    {
        ContaminationStormController[] storms =
            FindObjectsByType<ContaminationStormController>(FindObjectsInactive.Exclude);
        for (int i = 0; i < storms.Length; i++)
        {
            if (storms[i] != null)
            {
                storms[i].StartStorm();
                storms[i].SpreadToNextRoom();
            }
        }
    }

    void StartFlood()
    {
        LevelFloodController[] floods =
            FindObjectsByType<LevelFloodController>(FindObjectsInactive.Exclude);
        for (int i = 0; i < floods.Length; i++)
        {
            if (floods[i] != null)
                floods[i].StartFlood();
        }
    }

    void ResetFlood()
    {
        LevelFloodController[] floods =
            FindObjectsByType<LevelFloodController>(FindObjectsInactive.Exclude);
        for (int i = 0; i < floods.Length; i++)
        {
            if (floods[i] != null)
                floods[i].ResetFlood();
        }
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

    void RevealMinimap()
    {
        if (LevelObjectiveManager.Instance != null)
            LevelObjectiveManager.Instance.DebugRevealAllRooms();
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

    void DrawEnemyTrackingOverlay()
    {
        Camera camera = Camera.main;
        List<GameObject> enemies = FindEnemyObjects();

        if (camera == null)
            return;

        PlayerStatus player = FindNearestPlayer(camera.transform.position, out _);
        EnsureEnemyTrackingStyles();
        DrawEnemyTrackingPanel(enemies);
        DrawEnemyCompass(camera, player, enemies);

        if (debugNoclip)
            DrawGmodText();
    }

    void DrawEnemyTrackingPanel(List<GameObject> enemies)
    {
        GUILayout.BeginArea(new Rect(24f, 540f, 360f, 240f), GUI.skin.box);

        GUILayout.Label($"Enemies: {enemies.Count}");
        for (int i = 0; i < enemies.Count; i++)
        {
            GameObject enemy = enemies[i];
            if (enemy == null)
                continue;

            float distance = 0f;
            PlayerStatus closest = FindNearestPlayer(enemy.transform.position, out distance);
            string closestText = closest != null ? $"{Mathf.RoundToInt(distance)}m" : "no player";

            Color previousColor = GUI.color;
            GUI.color = GetEnemyTrackingColor(enemy);
            GUILayout.Label($"{GetEnemyShortName(enemy)} - {enemy.name} - {closestText}");
            GUI.color = previousColor;
        }

        GUILayout.EndArea();
    }

    void DrawEnemyCompass(
        Camera camera,
        PlayerStatus player,
        List<GameObject> enemies)
    {
        float width = Mathf.Clamp(enemyCompassWidth, 220f, Screen.width - 32f);
        Rect compassRect = new Rect(
            (Screen.width - width) * 0.5f,
            enemyCompassTopOffset,
            width,
            30f);

        GUI.Box(compassRect, GUIContent.none);
        GUI.Label(
            new Rect(compassRect.center.x - 12f, compassRect.y - 2f, 24f, 24f),
            "^",
            enemyCompassCenterStyle);

        for (int i = 0; i < enemies.Count; i++)
        {
            GameObject enemy = enemies[i];
            if (enemy == null)
                continue;

            float distance;
            if (player != null)
                distance = Vector3.Distance(player.transform.position, enemy.transform.position);
            else
                FindNearestPlayer(enemy.transform.position, out distance);

            if (distance > enemyTrackingMaxDistance)
                continue;

            Vector3 direction = enemy.transform.position - camera.transform.position;
            float signedAngle = Vector3.SignedAngle(
                camera.transform.forward,
                direction,
                Vector3.up);
            float normalizedAngle = Mathf.Clamp(signedAngle / 90f, -1f, 1f);
            float x = Mathf.Lerp(
                compassRect.xMin + 12f,
                compassRect.xMax - 12f,
                (normalizedAngle + 1f) * 0.5f);

            Rect labelRect = new Rect(
                x - 58f,
                compassRect.yMax + 4f + (i % 2) * 18f,
                116f,
                18f);

            Color previousColor = GUI.color;
            GUI.color = GetEnemyTrackingColor(enemy);
            GUI.Label(
                labelRect,
                $"{GetEnemyShortName(enemy)} {Mathf.RoundToInt(distance)}m",
                enemyCompassStyle);
            GUI.color = previousColor;
        }
    }

    void DrawGmodText()
    {
        Rect textRect = new Rect(
            Screen.width - 260f,
            16f,
            244f,
            28f);
        GUI.Label(textRect, "woah like the gmod :D", gmodTextStyle);
    }

    List<GameObject> FindEnemyObjects()
    {
        List<GameObject> enemies = new List<GameObject>();
        HashSet<GameObject> seen = new HashSet<GameObject>();
        MonoBehaviour[] behaviours =
            FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude);

        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
                continue;

            if (!IsKnownEnemyType(behaviour.GetType().Name))
                continue;

            GameObject enemyObject = behaviour.gameObject;
            if (enemyObject != null && seen.Add(enemyObject))
                enemies.Add(enemyObject);
        }

        return enemies;
    }

    bool IsKnownEnemyType(string typeName)
    {
        for (int i = 0; i < EnemyTypeNames.Length; i++)
        {
            if (EnemyTypeNames[i] == typeName)
                return true;
        }

        return false;
    }

    string GetEnemyShortName(GameObject enemy)
    {
        string typeName = GetEnemyTypeName(enemy);
        switch (typeName)
        {
            case "BathroomBlondeBehavior":
                return "Blonde";
            case "GhostWaterBehavior":
                return "Ghost";
            case "GoldenMouthBehavior":
                return "Mouth";
            case "Photographer":
                return "Photo";
            case "RaccoonBehavior":
                return "Raccoon";
            case "TimeCamper":
                return "Camper";
            case "TubaraoBehavior":
                return "Shark";
            case "VictoriaRegiaBehavior":
                return "Victoria";
            case "WillOWispBehavior":
                return "Wisp";
            default:
                return enemy != null ? enemy.name : "Enemy";
        }
    }

    Color GetEnemyTrackingColor(GameObject enemy)
    {
        string typeName = GetEnemyTypeName(enemy);
        switch (typeName)
        {
            case "GoldenMouthBehavior":
                return Color.yellow;
            case "Photographer":
                return Color.red;
            case "RaccoonBehavior":
                return new Color(0.55f, 0.32f, 0.12f, 1f);
            case "TubaraoBehavior":
                return new Color(0.2f, 0.65f, 1f, 1f);
            case "TimeCamper":
                return new Color(1f, 0.45f, 0.1f, 1f);
            case "GhostWaterBehavior":
                return new Color(0.45f, 0.9f, 1f, 1f);
            case "BathroomBlondeBehavior":
                return new Color(1f, 0.85f, 0.25f, 1f);
            case "VictoriaRegiaBehavior":
                return new Color(1f, 0.25f, 0.75f, 1f);
            case "WillOWispBehavior":
                return new Color(0.65f, 0.45f, 1f, 1f);
            default:
                return Color.white;
        }
    }

    string GetEnemyTypeName(GameObject enemy)
    {
        if (enemy == null)
            return string.Empty;

        MonoBehaviour[] behaviours =
            enemy.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] == null)
                continue;

            string typeName = behaviours[i].GetType().Name;
            if (IsKnownEnemyType(typeName))
                return typeName;
        }

        return string.Empty;
    }

    void EnsureEnemyTrackingStyles()
    {
        if (enemyCompassStyle == null)
        {
            enemyCompassStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
        }

        if (enemyCompassCenterStyle == null)
        {
            enemyCompassCenterStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
        }

        if (gmodTextStyle == null)
        {
            gmodTextStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold,
                fontSize = 18
            };
        }
    }

    PlayerStatus FindNearestPlayer(Vector3 position, out float distance)
    {
        distance = float.PositiveInfinity;
        PlayerStatus closest = null;
        PlayerStatus[] players =
            FindObjectsByType<PlayerStatus>(FindObjectsInactive.Exclude);

        for (int i = 0; i < players.Length; i++)
        {
            PlayerStatus player = players[i];
            if (player == null)
                continue;

            float currentDistance = Vector3.Distance(position, player.transform.position);
            if (currentDistance < distance)
            {
                distance = currentDistance;
                closest = player;
            }
        }

        return closest;
    }

    bool IsAllowed()
    {
        return enableInReleaseBuild || Application.isEditor || Debug.isDebugBuild;
    }
}
