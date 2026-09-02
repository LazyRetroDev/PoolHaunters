using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PlayableAdUI : MonoBehaviour
{
    [Header("Timer UI")]
    public TMP_Text timerText;
    
    [Header("CTA UI")]
    public GameObject ctaPanel;
    public Button playNowButton;

    void Start()
    {
        if (ctaPanel != null)
            ctaPanel.SetActive(false);

        if (playNowButton != null)
            playNowButton.onClick.AddListener(OnPlayNowClicked);
    }

    public void UpdateTimer(float remainingSeconds)
    {
        if (timerText != null)
        {
            int seconds = Mathf.CeilToInt(remainingSeconds);
            timerText.text = seconds.ToString() + "s";
            
            if (seconds <= 5)
                timerText.color = Color.red;
        }
    }

    public void ShowCTA(bool isVictory)
    {
        if (ctaPanel != null)
        {
            ctaPanel.SetActive(true);
        }

        // Se usar Luna/Playworks, normalmente voce chama a API deles aqui para registrar o fim do ad
        // ex: Luna.Unity.LifeCycle.GameEnded();
    }

    private void OnPlayNowClicked()
    {
        Debug.Log("CTA Clicked! Redirecting to store...");
        // API da Luna para redirecionar
        // ex: Luna.Unity.Playable.InstallFullGame();
    }
}
