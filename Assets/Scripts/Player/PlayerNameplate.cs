using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerNameplate : MonoBehaviour
{
    private const int DefaultRenderLayer = 0;

    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Transform targetCamera;
    [SerializeField] private bool hideWhenEmpty = true;
    [SerializeField] private bool forceDefaultRenderLayer = true;

    private Canvas canvas;

    private void Awake()
    {
        ResolveReferences();
        ConfigureCanvas();
        ConfigureText();

        if (forceDefaultRenderLayer)
            SetLayerRecursively(transform, DefaultRenderLayer);
    }

    private void LateUpdate()
    {
        if (!isActiveAndEnabled)
            return;

        if (targetCamera == null && Camera.main != null)
            targetCamera = Camera.main.transform;

        if (targetCamera == null)
            return;

        if (canvas != null && canvas.worldCamera == null)
            canvas.worldCamera = targetCamera.GetComponent<Camera>();

        transform.LookAt(
            transform.position + targetCamera.rotation * Vector3.forward,
            targetCamera.rotation * Vector3.up);
    }

    public void SetName(string playerName)
    {
        ResolveReferences();

        string displayName = string.IsNullOrWhiteSpace(playerName)
            ? string.Empty
            : playerName.Trim();

        if (nameText != null)
            nameText.text = displayName;

        if (hideWhenEmpty)
            SetVisible(!string.IsNullOrWhiteSpace(displayName));
    }

    public void SetVisible(bool visible)
    {
        if (canvas != null)
            canvas.enabled = visible;
        else
            gameObject.SetActive(visible);
    }

    private void ResolveReferences()
    {
        if (nameText == null)
            nameText = GetComponentInChildren<TMP_Text>(true);

        if (canvas == null)
            canvas = GetComponent<Canvas>();

        if (canvas == null)
            canvas = GetComponentInChildren<Canvas>(true);

        if (canvas == null)
            canvas = GetComponentInParent<Canvas>(true);
    }

    private void ConfigureCanvas()
    {
        if (canvas == null)
            return;

        canvas.renderMode = RenderMode.WorldSpace;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 1000;
        canvas.enabled = true;
    }

    private void ConfigureText()
    {
        if (nameText == null)
            return;

        nameText.enabled = true;
        nameText.raycastTarget = false;
        nameText.alignment = TextAlignmentOptions.Center;
    }

    private void SetLayerRecursively(Transform root, int layer)
    {
        if (root == null)
            return;

        root.gameObject.layer = layer;

        for (int i = 0; i < root.childCount; i++)
            SetLayerRecursively(root.GetChild(i), layer);
    }
}
