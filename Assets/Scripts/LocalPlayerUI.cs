using TMPro;
using UnityEngine;

public class LocalPlayerUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _healthText;
    [SerializeField] private TextMeshProUGUI _oxygenText;

    private void Update()
    {
        if (PlayerManager.Local == null) return;
        _healthText.text = $"Health: {PlayerManager.Local.GetCurrentHealth()} / {PlayerManager.Local.GetMaxHealth()}";
        _oxygenText.text = $"Oxygen: {PlayerManager.Local.GetCurrentOxygen()} / {PlayerManager.Local.GetMaxOxygen()}";
    }
}
