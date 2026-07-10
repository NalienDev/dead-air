using TMPro;
using UnityEngine;
using PurrNet;

public class QuotaUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _dayText;
    [SerializeField] private TextMeshProUGUI _quotaText;
    [SerializeField] private TextMeshProUGUI _bankedText;
    [SerializeField] private TextMeshProUGUI _energyText;
    private Color _defaultColor;
    private Color _sufficientQuotaColor;

    private void Start()
    {
        _defaultColor = _quotaText.color;
        _sufficientQuotaColor = new Color(81, 202, 130);
    }

    private void Update()
    {
        if (QuotaManager.Instance == null) return;

        var qm = QuotaManager.Instance;

        if (_dayText) _dayText.text = $"DAY {qm.currentDay.value}";
        
        if (_quotaText)
        {
            _quotaText.text = $"{qm.sessionBandwidth.value} / {qm.currentQuota.value}";
            _quotaText.color = qm.sessionBandwidth.value >= qm.currentQuota.value ? _sufficientQuotaColor : _defaultColor;
        }

        if (_bankedText) _bankedText.text = $"BANKED: {qm.totalBandwidth.value}";
        
        if (_energyText) _energyText.text = $"ENERGY: {qm.currentEnergyCells.value}";
    }
}
