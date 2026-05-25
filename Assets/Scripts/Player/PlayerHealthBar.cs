using UnityEngine;
using TMPro;
using static UnityEngine.Rendering.DebugUI;

public class PlayerHealthBar : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _healthText;
    [SerializeField] private TextMeshProUGUI _oxygenText;

    private PlayerManager _playerManager;

    private void Awake()
    {
        _playerManager = GetComponent<PlayerManager>();
        _playerManager.currentHealth.onChanged += UpdateHealthUI;
        _playerManager.currentOxygen.onChanged += UpdateOxygenUI;
    }

    private void OnDestroy()
    {
        _playerManager.currentHealth.onChanged -= UpdateHealthUI;
        _playerManager.currentOxygen.onChanged -= UpdateOxygenUI;
    }

    private void UpdateHealthUI(int value)
    {
        _healthText.text = $"{value} / {_playerManager.GetMaxHealth()}";
    }

    private void UpdateOxygenUI(int value)
    {
        _oxygenText.text = $"{value} / {_playerManager.GetMaxOxygen()}";
    }
}