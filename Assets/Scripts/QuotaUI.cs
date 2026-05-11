using TMPro;
using UnityEngine;
using PurrNet;

public class QuotaUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _dayText;
    [SerializeField] private TextMeshProUGUI _quotaText;
    [SerializeField] private TextMeshProUGUI _bankedText;
    [SerializeField] private TextMeshProUGUI _energyText;

    private void Update()
    {
        if (QuotaManager.Instance == null) return;

        var qm = QuotaManager.Instance;

        if (_dayText) _dayText.text = $"DAY {qm.currentDay.value}";
        
        if (_quotaText)
        {
            _quotaText.text = $"QUOTA: {qm.sessionBandwidth.value} / {qm.currentQuota.value}";
            _quotaText.color = qm.sessionBandwidth.value >= qm.currentQuota.value ? Color.green : Color.white;
        }

        if (_bankedText) _bankedText.text = $"BANKED: {qm.totalBandwidth.value}";
        
        if (_energyText) _energyText.text = $"ENERGY: {qm.currentEnergyCells.value}";
    }
}
