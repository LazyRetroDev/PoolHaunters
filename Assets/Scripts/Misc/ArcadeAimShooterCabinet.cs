using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

public class ArcadeAimShooterCabinet : MonoBehaviour, IPlayerInteractable
{
    [Header("Game")]
    public float gameDuration = 20f;
    public int scoreGoal = 12;
    public float targetLifetime = 1.25f;
    public float targetSize = 72f;
    public float targetPadding = 64f;
    public float targetClickPadding = 10f;
    public bool lockPlayerWhilePlaying = true;

    [Header("Style")]
    public Color backgroundColor = new Color(0.03f, 0.035f, 0.05f, 0.94f);
    public Color panelColor = new Color(0.04f, 0.09f, 0.12f, 0.96f);
    public Color targetColor = new Color(1f, 0.15f, 0.2f, 1f);
    public Color targetRingColor = new Color(1f, 0.95f, 0.25f, 1f);
    public Color textColor = new Color(0.8f, 1f, 0.95f, 1f);

    private Canvas canvas;
    private RectTransform playArea;
    private Button targetButton;
    private TMP_Text scoreText;
    private TMP_Text timerText;
    private TMP_Text resultText;
    private Button closeButton;
    private PlayerStatus lockedPlayerStatus;
    private CursorLockController cursorLockController;
    private CursorLockMode previousLockState;
    private bool previousCursorVisible;
    private bool previousCursorLockControllerEnabled;
    private bool isPlaying;
    private bool controlLocked;
    private int score;
    private float timeRemaining;
    private float targetTimer;

    public void Interact(PlayerInventory inventory)
    {
        if (IsCanvasOpen())
            return;

        PlayerStatus playerStatus = inventory != null
            ? inventory.GetComponent<PlayerStatus>()
            : null;

        StartGame(playerStatus);
    }

    void Update()
    {
        if (!IsCanvasOpen())
            return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            CloseGame();
            return;
        }

        KeepArcadeCursorUnlocked();

        if (!isPlaying)
            return;

        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && IsPointerOverTarget())
        {
            HitTarget();
            return;
        }

        timeRemaining -= Time.unscaledDeltaTime;
        targetTimer -= Time.unscaledDeltaTime;

        if (timeRemaining <= 0f)
        {
            EndGame(score >= scoreGoal);
            return;
        }

        if (targetTimer <= 0f)
            MoveTarget();

        UpdateText();
    }

    void OnDisable()
    {
        ReleasePlayerControl();
    }

    public void StartGame(PlayerStatus playerStatus)
    {
        EnsureCanvas();

        lockedPlayerStatus = playerStatus;
        LockPlayerControl();

        score = 0;
        timeRemaining = Mathf.Max(1f, gameDuration);
        resultText.text = string.Empty;
        targetButton.gameObject.SetActive(true);
        closeButton.gameObject.SetActive(false);
        canvas.gameObject.SetActive(true);
        isPlaying = true;

        MoveTarget();
        UpdateText();
    }

    void HitTarget()
    {
        if (!isPlaying)
            return;

        score++;
        if (score >= scoreGoal)
        {
            EndGame(true);
            return;
        }

        MoveTarget();
        UpdateText();
    }

    void MoveTarget()
    {
        if (playArea == null || targetButton == null)
            return;

        Rect rect = playArea.rect;
        float halfSize = targetSize * 0.5f;
        float minX = rect.xMin + targetPadding + halfSize;
        float maxX = rect.xMax - targetPadding - halfSize;
        float minY = rect.yMin + targetPadding + halfSize;
        float maxY = rect.yMax - targetPadding - halfSize;
        float x = minX < maxX ? Random.Range(minX, maxX) : rect.center.x;
        float y = minY < maxY ? Random.Range(minY, maxY) : rect.center.y;

        targetButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(x, y);
        targetTimer = Mathf.Max(0.2f, targetLifetime);
    }

    void EndGame(bool won)
    {
        isPlaying = false;
        KeepArcadeCursorUnlocked();

        if (targetButton != null)
            targetButton.gameObject.SetActive(false);

        if (resultText != null)
            resultText.text = won ? "CLEAR" : "GAME OVER";

        if (closeButton != null)
            closeButton.gameObject.SetActive(true);
    }

    void CloseGame()
    {
        isPlaying = false;
        ReleasePlayerControl();

        if (canvas != null)
            canvas.gameObject.SetActive(false);
    }

    void UpdateText()
    {
        if (scoreText != null)
            scoreText.text = $"SCORE {score}/{scoreGoal}";

        if (timerText != null)
            timerText.text = $"TIME {Mathf.CeilToInt(Mathf.Max(0f, timeRemaining))}";
    }

    void LockPlayerControl()
    {
        previousLockState = Cursor.lockState;
        previousCursorVisible = Cursor.visible;
        cursorLockController = lockedPlayerStatus != null
            ? lockedPlayerStatus.GetComponent<CursorLockController>()
            : null;
        previousCursorLockControllerEnabled = cursorLockController != null && cursorLockController.enabled;

        if (cursorLockController != null)
            cursorLockController.enabled = false;

        KeepArcadeCursorUnlocked();

        if (!lockPlayerWhilePlaying ||
            lockedPlayerStatus == null ||
            controlLocked)
        {
            return;
        }

        lockedPlayerStatus.AddExternalControlLock();
        controlLocked = true;
    }

    void ReleasePlayerControl()
    {
        Cursor.lockState = previousLockState;
        Cursor.visible = previousCursorVisible;

        if (cursorLockController != null)
            cursorLockController.enabled = previousCursorLockControllerEnabled;

        if (lockedPlayerStatus != null && controlLocked)
            lockedPlayerStatus.RemoveExternalControlLock();

        controlLocked = false;
        lockedPlayerStatus = null;
        cursorLockController = null;
    }

    bool IsCanvasOpen()
    {
        return canvas != null && canvas.gameObject.activeSelf;
    }

    void KeepArcadeCursorUnlocked()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void EnsureCanvas()
    {
        if (canvas != null)
            return;

        EnsureEventSystem();

        GameObject canvasObject = new GameObject("Arcade Aim Shooter Canvas");
        canvasObject.transform.SetParent(transform, false);
        canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        canvasObject.AddComponent<GraphicRaycaster>();

        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        BuildUi(canvasObject.transform);
        canvasObject.SetActive(false);
    }

    void BuildUi(Transform root)
    {
        RectTransform background = CreateImage(
            "Background",
            root,
            backgroundColor,
            Vector2.zero,
            Vector2.one,
            Vector2.zero,
            Vector2.zero);

        playArea = CreateImage(
            "Play Area",
            background,
            panelColor,
            new Vector2(0.18f, 0.15f),
            new Vector2(0.82f, 0.85f),
            Vector2.zero,
            Vector2.zero);

        scoreText = CreateText("Score", background, new Vector2(0.19f, 0.86f), new Vector2(0.5f, 0.94f), TextAlignmentOptions.Left);
        timerText = CreateText("Timer", background, new Vector2(0.5f, 0.86f), new Vector2(0.81f, 0.94f), TextAlignmentOptions.Right);
        resultText = CreateText("Result", background, new Vector2(0.25f, 0.42f), new Vector2(0.75f, 0.58f), TextAlignmentOptions.Center);
        resultText.fontSize = 54f;

        targetButton = CreateButton("Target", playArea, targetColor, targetSize, targetSize);
        targetButton.GetComponent<Image>().raycastTarget = false;

        Image ring = CreateImage(
            "Ring",
            targetButton.transform,
            targetRingColor,
            Vector2.zero,
            Vector2.one,
            new Vector2(10f, 10f),
            new Vector2(-10f, -10f)).GetComponent<Image>();
        ring.raycastTarget = false;

        closeButton = CreateButton("Close", background, new Color(0.12f, 0.18f, 0.2f, 1f), 180f, 56f);
        closeButton.GetComponent<RectTransform>().anchorMin = new Vector2(0.5f, 0.18f);
        closeButton.GetComponent<RectTransform>().anchorMax = new Vector2(0.5f, 0.18f);
        closeButton.GetComponent<RectTransform>().anchoredPosition = Vector2.zero;
        TMP_Text closeText = closeButton.GetComponentInChildren<TMP_Text>();
        closeText.text = "CLOSE";
        closeButton.onClick.AddListener(CloseGame);
        closeButton.gameObject.SetActive(false);
    }

    RectTransform CreateImage(
        string objectName,
        Transform parent,
        Color color,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 offsetMin,
        Vector2 offsetMax)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(parent, false);

        RectTransform rectTransform = imageObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = offsetMin;
        rectTransform.offsetMax = offsetMax;

        Image image = imageObject.GetComponent<Image>();
        image.color = color;

        return rectTransform;
    }

    TMP_Text CreateText(
        string objectName,
        Transform parent,
        Vector2 anchorMin,
        Vector2 anchorMax,
        TextAlignmentOptions alignment)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(parent, false);

        RectTransform rectTransform = textObject.GetComponent<RectTransform>();
        rectTransform.anchorMin = anchorMin;
        rectTransform.anchorMax = anchorMax;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        TMP_Text text = textObject.GetComponent<TMP_Text>();
        text.color = textColor;
        text.fontSize = 32f;
        text.alignment = alignment;
        text.enableWordWrapping = false;

        return text;
    }

    Button CreateButton(
        string objectName,
        Transform parent,
        Color color,
        float width,
        float height)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.transform.SetParent(parent, false);

        RectTransform rectTransform = buttonObject.GetComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(width, height);

        Image image = buttonObject.GetComponent<Image>();
        image.color = color;

        Button button = buttonObject.GetComponent<Button>();

        GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(buttonObject.transform, false);
        RectTransform labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;

        TMP_Text label = labelObject.GetComponent<TMP_Text>();
        label.text = string.Empty;
        label.color = textColor;
        label.fontSize = 24f;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = false;
        label.raycastTarget = false;

        return button;
    }

    bool IsPointerOverTarget()
    {
        if (targetButton == null || !targetButton.gameObject.activeInHierarchy || Mouse.current == null)
            return false;

        RectTransform targetRect = targetButton.GetComponent<RectTransform>();
        if (targetRect == null)
            return false;

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(targetRect, mousePosition, null, out Vector2 localPosition))
            return false;

        Rect hitRect = targetRect.rect;
        float padding = Mathf.Max(0f, targetClickPadding);
        hitRect.xMin -= padding;
        hitRect.xMax += padding;
        hitRect.yMin -= padding;
        hitRect.yMax += padding;
        return hitRect.Contains(localPosition);
    }

    void EnsureEventSystem()
    {
        if (FindObjectOfType<EventSystem>() != null)
            return;

        GameObject eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();
    }
}
