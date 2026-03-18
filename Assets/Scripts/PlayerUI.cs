using UnityEngine;
using TMPro;

public class PlayerUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _healthText;
    [SerializeField] private TextMeshProUGUI _oxygenText;
    
    private void Awake()
    {
        EventManager.PlayerHit += OnPlayerHit;
    }

    private void OnDestroy()
    {
        EventManager.PlayerHit -= OnPlayerHit;
    }

    private void OnPlayerHit(int currentHealth)
    {
        _healthText.text = "Health: " + currentHealth.ToString() + " / 100";
    }
}