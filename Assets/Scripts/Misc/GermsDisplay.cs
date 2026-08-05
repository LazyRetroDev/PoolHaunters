using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class GermsDisplay : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private string balanceFormat = "Germs: {0}";
    [SerializeField] private bool showLastRunReward = true;
    [SerializeField] private string lastRunFormat = "Last run: +{0}";
    [SerializeField] private float refreshInterval = 0.25f;

    private float refreshTimer;

    void Awake()
    {
        if (label == null)
            label = GetComponentInChildren<TMP_Text>(true);

        Refresh();
    }

    void Update()
    {
        refreshTimer -= Time.deltaTime;
        if (refreshTimer > 0f)
            return;

        refreshTimer = Mathf.Max(0.05f, refreshInterval);
        Refresh();
    }

    public void Refresh()
    {
        if (label == null)
            return;

        string text = string.Format(balanceFormat, PlayerCurrencyState.Germs);
        if (showLastRunReward)
            text += "\n" + string.Format(
                lastRunFormat,
                PlayerCurrencyState.LastRunEarnedGerms);

        label.text = text;
    }
}
