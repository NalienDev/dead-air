using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Local player's health and oxygen HUD, hidden while dead and restored on revive.
/// </summary>
public class LocalPlayerUI : MonoBehaviour
{
    public static LocalPlayerUI Instance { get; private set; }

    [Header("Vitals HUD")]
    [SerializeField] private TextMeshProUGUI _healthText;
    [SerializeField] private TextMeshProUGUI _oxygenText;

    [Header("Radial Vitals")]
    [SerializeField] private Image _healthFillImage;
    [SerializeField] private Image _oxygenFillImage;

    [Tooltip("fillAmount shown at full oxygen, matching the sprite's usable arc.")]
    [SerializeField, Range(0f, 1f)] private float _oxygenFillMax = 0.78f;

    [Tooltip("Container toggled as a whole on death. If empty, labels are toggled individually.")]
    [SerializeField] private GameObject _vitalsRoot;

    private bool _hidden;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        bool dead = PlayerManager.Local != null && PlayerManager.Local.IsDead;

        if (dead)
        {
            if (!_hidden) SetVitalsVisible(false);
            return;
        }

        if (_hidden) SetVitalsVisible(true);

        if (PlayerManager.Local == null) return;

        if (_healthText != null)
            _healthText.text = $"Health: {PlayerManager.Local.GetCurrentHealth()} / {PlayerManager.Local.GetMaxHealth()}";
        if (_oxygenText != null)
            _oxygenText.text = $"Oxygen: {PlayerManager.Local.GetCurrentOxygen()} / {PlayerManager.Local.GetMaxOxygen()}";
    
        if (_healthFillImage != null)
            _healthFillImage.fillAmount = (float)PlayerManager.Local.GetCurrentHealth() / PlayerManager.Local.GetMaxHealth();

        if (_oxygenFillImage != null)
        {
            float oxygen01 = (float)PlayerManager.Local.GetCurrentOxygen() / PlayerManager.Local.GetMaxOxygen();
            _oxygenFillImage.fillAmount = oxygen01 * _oxygenFillMax;
        }
    }

    private void SetVitalsVisible(bool visible)
    {
        _hidden = !visible;

        if (_vitalsRoot != null)
        {
            _vitalsRoot.SetActive(visible);
            return;
        }

        if (_healthText != null) _healthText.gameObject.SetActive(visible);
        if (_oxygenText != null) _oxygenText.gameObject.SetActive(visible);
    }
}
