using System;
using UnityEngine;

[DisallowMultipleComponent]
public class PoolRoomPoolSelector : MonoBehaviour
{
    [Serializable]
    public class PoolVariant
    {
        public string label;
        public GameObject prefab;
        [Min(0f)] public float weight = 1f;
    }

    [Header("Variants")]
    [SerializeField] private PoolVariant[] poolPrefabs = new PoolVariant[0];
    [SerializeField] private int seedSalt = 913579;

    [Header("Placement")]
    [SerializeField] private Transform poolParent;
    [SerializeField] private Vector3 localPosition = new Vector3(0f, 1f, 0f);
    [SerializeField] private Vector3 localEulerAngles;
    [SerializeField] private Vector3 localScale = Vector3.one;

    [Header("Existing Pool")]
    [SerializeField] private bool replaceExistingPool = true;
    [SerializeField] private bool keepExistingPoolWhenNoVariant = true;
    [SerializeField] private bool selectOnStartWhenNotConfigured = true;

    [Header("Debug")]
    [SerializeField] private GameObject selectedPoolInstance;
    [SerializeField] private string selectedPoolName;
    [SerializeField] private int selectedVariantIndex = -1;
    [SerializeField] private int selectionSeed;

    private bool selected;
    private bool hasExternalSelectionSeed;
    private int externalRunSeed;
    private int externalRoomIndex = -1;

    private void Start()
    {
        if (!selected && selectOnStartWhenNotConfigured)
            SelectPool();
    }

    public void SelectPool(int runSeed, int roomIndex)
    {
        hasExternalSelectionSeed = true;
        externalRunSeed = runSeed;
        externalRoomIndex = roomIndex;
        SelectPool();
    }

    public void SelectPool()
    {
        if (selected)
            return;

        selected = true;

        Transform parent = poolParent != null ? poolParent : transform;
        int variantIndex;
        GameObject selectedPrefab = ChoosePoolPrefab(out variantIndex);
        if (selectedPrefab == null)
        {
            if (!keepExistingPoolWhenNoVariant)
                DestroyExistingPools(parent);

            return;
        }

        if (replaceExistingPool)
            DestroyExistingPools(parent);

        selectedPoolInstance = Instantiate(selectedPrefab, parent);
        selectedPoolInstance.name = selectedPrefab.name;

        Transform selectedTransform = selectedPoolInstance.transform;
        selectedTransform.localPosition = localPosition;
        selectedTransform.localRotation = Quaternion.Euler(localEulerAngles);
        selectedTransform.localScale = localScale;
        selectedPoolName = selectedPrefab.name;
        selectedVariantIndex = variantIndex;
    }

    private GameObject ChoosePoolPrefab(out int selectedIndex)
    {
        selectedIndex = -1;
        float totalWeight = 0f;
        if (poolPrefabs == null)
            return null;

        for (int i = 0; i < poolPrefabs.Length; i++)
        {
            PoolVariant variant = poolPrefabs[i];
            if (variant == null || variant.prefab == null || variant.weight <= 0f)
                continue;

            totalWeight += variant.weight;
        }

        if (totalWeight <= 0f)
            return null;

        selectionSeed = CreateSelectionSeed();
        System.Random random = new System.Random(selectionSeed);
        double roll = random.NextDouble() * totalWeight;

        for (int i = 0; i < poolPrefabs.Length; i++)
        {
            PoolVariant variant = poolPrefabs[i];
            if (variant == null || variant.prefab == null || variant.weight <= 0f)
                continue;

            roll -= variant.weight;
            if (roll <= 0d)
            {
                selectedIndex = i;
                return variant.prefab;
            }
        }

        for (int i = poolPrefabs.Length - 1; i >= 0; i--)
        {
            PoolVariant variant = poolPrefabs[i];
            if (variant != null && variant.prefab != null && variant.weight > 0f)
            {
                selectedIndex = i;
                return variant.prefab;
            }
        }

        return null;
    }

    private void DestroyExistingPools(Transform parent)
    {
        if (parent == null)
            return;

        SwimmingPoolObjective[] pools =
            parent.GetComponentsInChildren<SwimmingPoolObjective>(true);
        for (int i = 0; i < pools.Length; i++)
        {
            SwimmingPoolObjective pool = pools[i];
            if (pool == null)
                continue;

            GameObject poolObject = pool.gameObject;
            if (poolObject == selectedPoolInstance)
                continue;

            poolObject.SetActive(false);

            if (Application.isPlaying)
                Destroy(poolObject);
            else
                DestroyImmediate(poolObject);
        }
    }

    private int CreateSelectionSeed()
    {
        unchecked
        {
            int result = seedSalt;
            result = result * 397 ^ GetRunSeed();
            result = result * 397 ^ externalRoomIndex;
            AddQuantizedVector(ref result, transform.position, 100f);
            AddQuantizedVector(ref result, transform.eulerAngles, 10f);

            RoomDefinition definition = GetComponent<RoomDefinition>();
            if (definition != null)
                AddString(ref result, definition.roomName);
            else
                AddString(ref result, gameObject.name);

            return result;
        }
    }

    private int GetRunSeed()
    {
        if (hasExternalSelectionSeed)
            return externalRunSeed;

        if (RegionRunState.HasSelectedRegion)
            return RegionRunState.RunSeed;

        RoomGenerator generator = FindAnyObjectByType<RoomGenerator>();
        return generator != null ? generator.CurrentSeed : 0;
    }

    private static void AddQuantizedVector(
        ref int hash,
        Vector3 value,
        float multiplier)
    {
        hash = hash * 397 ^ Mathf.RoundToInt(value.x * multiplier);
        hash = hash * 397 ^ Mathf.RoundToInt(value.y * multiplier);
        hash = hash * 397 ^ Mathf.RoundToInt(value.z * multiplier);
    }

    private static void AddString(ref int hash, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        for (int i = 0; i < value.Length; i++)
            hash = hash * 397 ^ value[i];
    }

    private void OnValidate()
    {
        if (poolPrefabs == null)
            return;

        for (int i = 0; i < poolPrefabs.Length; i++)
        {
            if (poolPrefabs[i] != null)
                poolPrefabs[i].weight = Mathf.Max(0f, poolPrefabs[i].weight);
        }
    }
}
