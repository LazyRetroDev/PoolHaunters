using System;
using UnityEngine;

public enum SwimmingPoolObjectiveState : byte
{
    Empty = 0,
    Contaminated = 1,
    Clean = 2
}

[DisallowMultipleComponent]
public class SwimmingPoolObjective : MonoBehaviour
{
    [Header("Objective")]
    [SerializeField] private bool requiredForLevelCompletion = true;
    [SerializeField] private bool startsEmpty = true;

    [Header("Visuals")]
    [SerializeField] private GameObject waterVisualRoot;
    [SerializeField] private GameObject contaminatedWaterVisualRoot;
    [SerializeField] private GameObject cleanWaterVisualRoot;
    [SerializeField] private GameObject dirtSpotRoot;
    [SerializeField] private bool hideDirtSpotsWhenClean = true;

    [Header("Dirt")]
    [SerializeField] private bool autoFindDirtSpots = true;
    [SerializeField] private DirtSpot[] dirtSpots = new DirtSpot[0];
    [SerializeField, Range(0.01f, 1f)] private float poolCleanCompletionThreshold = 0.95f;

    [Header("Debug")]
    [SerializeField] private int debugSyncId;
    [SerializeField] private bool filled;
    [SerializeField] private bool cleaned;
    [SerializeField] private int totalDirtSpotCount;
    [SerializeField] private int cleanedDirtSpotCount;

    private bool applyingSynchronizedState;
    private readonly System.Collections.Generic.List<DirtSpot> trackedDirtSpots =
        new System.Collections.Generic.List<DirtSpot>();
    private readonly System.Collections.Generic.HashSet<DirtSpot> subscribedDirtSpots =
        new System.Collections.Generic.HashSet<DirtSpot>();

    public event Action<SwimmingPoolObjective> OnPoolStateChanged;
    public event Action<SwimmingPoolObjective> OnPoolCleaned;
    public event Action<SwimmingPoolObjective> OnPoolActivelyCleaned;

    public float LastCleanedTime { get; private set; } = -100f;
    public bool RequiredForLevelCompletion
    {
        get => requiredForLevelCompletion;
        set => requiredForLevelCompletion = value;
    }
    public bool IsFilled => filled;
    public bool IsCleaned => cleaned;
    public bool IsApplyingSynchronizedState => applyingSynchronizedState;
    public int SyncId => GetSyncId();
    public byte SyncState => (byte)GetState();
    public int TotalDirtSpotCount => totalDirtSpotCount;
    public int CleanedDirtSpotCount => cleanedDirtSpotCount;
    public float CleanProgress
    {
        get
        {
            RefreshDirtSpots();
            return trackedDirtSpots.Count > 0
                ? CalculateDirtCleanProgress()
                : cleaned ? 1f : 0f;
        }
    }

    public void NotifyActivelyCleaned()
    {
        LastCleanedTime = Time.time;
        OnPoolActivelyCleaned?.Invoke(this);
    }

    void Awake()
    {
        AutoBindReferences();
        RefreshDirtSpots();
    }

    void OnEnable()
    {
        RegisterDirtSpotEvents();

        if (LevelObjectiveManager.Instance != null)
        {
            LevelObjectiveManager.Instance.RegisterPoolObjective(this);
            LevelObjectiveManager.Instance.OnWaterValveActivated += HandleWaterValveActivated;
        }

        if (LevelObjectiveManager.Instance != null &&
            LevelObjectiveManager.Instance.WaterValveActivated)
        {
            FillContaminated();
        }
        else if (startsEmpty)
        {
            SetEmpty();
        }
        else
        {
            FillContaminated();
        }
    }

    void OnDisable()
    {
        UnregisterDirtSpotEvents();

        if (LevelObjectiveManager.Instance != null)
        {
            LevelObjectiveManager.Instance.OnWaterValveActivated -= HandleWaterValveActivated;
            LevelObjectiveManager.Instance.UnregisterPoolObjective(this);
        }
    }

    public void SetEmpty()
    {
        filled = false;
        cleaned = false;
        RefreshCleanProgress();
        ApplyVisualState();
        NotifyStateChanged();
    }

    public void FillContaminated()
    {
        filled = true;
        RefreshCleanProgress();
        ApplyVisualState();
        NotifyStateChanged();
    }

    public void ForceClean()
    {
        filled = true;

        RefreshDirtSpots();
        if (dirtSpots != null)
        {
            for (int i = 0; i < dirtSpots.Length; i++)
            {
                if (dirtSpots[i] != null)
                    dirtSpots[i].ForceClean();
            }
        }

        MarkCleaned();
    }

    public void RefreshAndEvaluateCleanState(bool notifyWhenUnchanged = false)
    {
        if (cleaned)
            return;

        RefreshCleanProgress();
        if (filled && IsPoolCleanComplete())
            MarkCleaned();
        else if (notifyWhenUnchanged)
            NotifyStateChanged();
    }

    public bool TryGetDirtSpotIndex(
        DirtSpot dirtSpot,
        out int dirtSpotIndex)
    {
        RefreshDirtSpots();

        for (int i = 0; i < trackedDirtSpots.Count; i++)
        {
            if (trackedDirtSpots[i] == dirtSpot)
            {
                dirtSpotIndex = i;
                return true;
            }
        }

        dirtSpotIndex = -1;
        return false;
    }

    public void ApplySynchronizedDirtCleanAtWorldPoint(
        int dirtSpotIndex,
        Vector3 worldPoint,
        float worldRadius,
        float amount)
    {
        RefreshDirtSpots();

        if (dirtSpotIndex < 0 ||
            dirtSpotIndex >= trackedDirtSpots.Count ||
            trackedDirtSpots[dirtSpotIndex] == null)
        {
            return;
        }

        applyingSynchronizedState = true;

        try
        {
            trackedDirtSpots[dirtSpotIndex].ApplySynchronizedPoolCleanAtWorldPoint(
                worldPoint,
                worldRadius,
                amount);

            RefreshCleanProgress();
            if (filled && !cleaned && IsPoolCleanComplete())
                MarkCleaned();
            else
                NotifyStateChanged();
        }
        finally
        {
            applyingSynchronizedState = false;
        }
    }

    public void ApplySynchronizedState(byte state)
    {
        applyingSynchronizedState = true;

        try
        {
            SwimmingPoolObjectiveState poolState =
                (SwimmingPoolObjectiveState)Mathf.Clamp(
                    state,
                    (byte)SwimmingPoolObjectiveState.Empty,
                    (byte)SwimmingPoolObjectiveState.Clean);

            bool wasCleaned = cleaned;

            switch (poolState)
            {
                case SwimmingPoolObjectiveState.Empty:
                    filled = false;
                    cleaned = false;
                    break;

                case SwimmingPoolObjectiveState.Clean:
                    filled = true;
                    cleaned = true;
                    break;

                default:
                    filled = true;
                    cleaned = false;
                    break;
            }

            RefreshCleanProgress();
            ApplyVisualState();

            if (cleaned && !wasCleaned)
                OnPoolCleaned?.Invoke(this);

            NotifyStateChanged();
        }
        finally
        {
            applyingSynchronizedState = false;
        }
    }

    void HandleWaterValveActivated()
    {
        FillContaminated();
    }

    void HandleDirtSpotCleaned(DirtSpot dirt)
    {
        RefreshCleanProgress();
        if (filled && !cleaned && IsPoolCleanComplete())
            MarkCleaned();
        else
            NotifyStateChanged();
    }

    void MarkCleaned()
    {
        if (cleaned)
            return;

        cleaned = true;
        RefreshCleanProgress();
        ApplyVisualState();
        OnPoolCleaned?.Invoke(this);
        NotifyStateChanged();
    }

    void RefreshCleanProgress()
    {
        RefreshDirtSpots();

        totalDirtSpotCount = trackedDirtSpots.Count;
        cleanedDirtSpotCount = 0;

        for (int i = 0; i < trackedDirtSpots.Count; i++)
        {
            DirtSpot dirt = trackedDirtSpots[i];
            if (dirt == null || dirt.IsCleaned)
                cleanedDirtSpotCount++;
        }
    }

    float CalculateDirtCleanProgress()
    {
        RefreshDirtSpots();

        if (trackedDirtSpots.Count == 0)
            return cleaned ? 1f : 0f;

        float cleanAmount = 0f;
        for (int i = 0; i < trackedDirtSpots.Count; i++)
        {
            DirtSpot dirt = trackedDirtSpots[i];
            if (dirt == null || dirt.IsCleaned)
            {
                cleanAmount += 1f;
                continue;
            }

            cleanAmount += Mathf.Clamp01(1f - dirt.GetDirtPercent());
        }

        return Mathf.Clamp01(cleanAmount / trackedDirtSpots.Count);
    }

    bool IsEveryDirtSpotCleaned()
    {
        if (trackedDirtSpots.Count == 0)
            return true;

        for (int i = 0; i < trackedDirtSpots.Count; i++)
        {
            DirtSpot dirt = trackedDirtSpots[i];
            if (dirt != null && !dirt.IsCleaned)
                return false;
        }

        return true;
    }

    bool IsPoolCleanComplete()
    {
        if (IsEveryDirtSpotCleaned())
            return true;

        return CalculateDirtCleanProgress() >= poolCleanCompletionThreshold;
    }

    void ApplyVisualState()
    {
        SetActive(waterVisualRoot, filled);

        bool showContaminated = filled && !cleaned;
        bool hasDedicatedContaminatedVisual = contaminatedWaterVisualRoot != null;

        SetActive(contaminatedWaterVisualRoot, showContaminated);
        SetActive(cleanWaterVisualRoot, filled && (cleaned || !hasDedicatedContaminatedVisual));
        SetActive(dirtSpotRoot, filled && showContaminated);

        if (hideDirtSpotsWhenClean && cleaned && dirtSpotRoot != null)
            dirtSpotRoot.SetActive(false);
    }

    void NotifyStateChanged()
    {
        OnPoolStateChanged?.Invoke(this);

        if (LevelObjectiveManager.Instance != null)
            LevelObjectiveManager.Instance.NotifyPoolObjectiveStateChanged(this);
    }

    SwimmingPoolObjectiveState GetState()
    {
        if (cleaned)
            return SwimmingPoolObjectiveState.Clean;

        return filled
            ? SwimmingPoolObjectiveState.Contaminated
            : SwimmingPoolObjectiveState.Empty;
    }

    int GetSyncId()
    {
        debugSyncId = CalculateSyncId();
        return debugSyncId;
    }

    int CalculateSyncId()
    {
        unchecked
        {
            int hash = 216613626;
            RoomDefinition room = GetComponentInParent<RoomDefinition>();
            Transform roomTransform = room != null ? room.transform : null;

            Vector3 roomPosition = roomTransform != null
                ? roomTransform.position
                : transform.position;
            Vector3 localPoolPosition = roomTransform != null
                ? roomTransform.InverseTransformPoint(transform.position)
                : transform.localPosition;

            AddQuantizedVectorToHash(ref hash, roomPosition, 100f);
            AddQuantizedVectorToHash(ref hash, localPoolPosition, 100f);
            AddStringToHash(ref hash, BuildRelativePath(roomTransform));

            return hash != 0 ? hash : 1;
        }
    }

    string BuildRelativePath(Transform roomRoot)
    {
        Transform current = transform;
        string path = current.name;

        while (current.parent != null && current.parent != roomRoot)
        {
            current = current.parent;
            path = current.name + "/" + path;
        }

        return path;
    }

    static void AddQuantizedVectorToHash(
        ref int hash,
        Vector3 value,
        float multiplier)
    {
        AddIntToHash(ref hash, Mathf.RoundToInt(value.x * multiplier));
        AddIntToHash(ref hash, Mathf.RoundToInt(value.y * multiplier));
        AddIntToHash(ref hash, Mathf.RoundToInt(value.z * multiplier));
    }

    static void AddStringToHash(ref int hash, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        for (int i = 0; i < value.Length; i++)
            AddIntToHash(ref hash, value[i]);
    }

    static void AddIntToHash(ref int hash, int value)
    {
        unchecked
        {
            hash ^= value;
            hash *= 16777619;
        }
    }

    void RefreshDirtSpots()
    {
        if (autoFindDirtSpots)
        {
            DirtSpot[] found = GetComponentsInChildren<DirtSpot>(true);
            if (found != null)
            {
                for (int i = 0; i < found.Length; i++)
                {
                    DirtSpot ds = found[i];
                    TrackDirtSpot(ds);
                }
            }
        }

        if (dirtSpots != null)
        {
            for (int i = 0; i < dirtSpots.Length; i++)
            {
                DirtSpot ds = dirtSpots[i];
                TrackDirtSpot(ds);
            }
        }

        totalDirtSpotCount = trackedDirtSpots.Count;
    }

    void TrackDirtSpot(DirtSpot dirtSpot)
    {
        if (dirtSpot == null)
            return;

        if (!trackedDirtSpots.Contains(dirtSpot))
            trackedDirtSpots.Add(dirtSpot);

        if (subscribedDirtSpots.Add(dirtSpot))
            dirtSpot.OnCleaned += HandleDirtSpotCleaned;
    }

    void RegisterDirtSpotEvents()
    {
        RefreshDirtSpots();
    }

    void UnregisterDirtSpotEvents()
    {
        foreach (DirtSpot dirtSpot in subscribedDirtSpots)
        {
            if (dirtSpot != null)
                dirtSpot.OnCleaned -= HandleDirtSpotCleaned;
        }

        subscribedDirtSpots.Clear();
    }

    void AutoBindReferences()
    {
        if (waterVisualRoot == null)
            waterVisualRoot = FindChildGameObject("WaterVisual");

        if (cleanWaterVisualRoot == null)
            cleanWaterVisualRoot = FindChildGameObject("CleanWater");

        if (contaminatedWaterVisualRoot == null)
            contaminatedWaterVisualRoot = FindChildGameObject("ContaminatedWater");

        if (contaminatedWaterVisualRoot == null)
            contaminatedWaterVisualRoot = FindChildGameObject("DirtyWater");

        if (dirtSpotRoot == null)
            dirtSpotRoot = FindChildGameObject("Dirtspots");

        if (dirtSpotRoot == null)
            dirtSpotRoot = FindChildGameObject("DirtSpots");
    }

    GameObject FindChildGameObject(string childName)
    {
        Transform found = FindChildRecursive(transform, childName);
        return found != null ? found.gameObject : null;
    }

    Transform FindChildRecursive(Transform root, string childName)
    {
        if (root == null || string.IsNullOrWhiteSpace(childName))
            return null;

        if (root.name == childName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform result = FindChildRecursive(root.GetChild(i), childName);
            if (result != null)
                return result;
        }

        return null;
    }

    static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }
}
