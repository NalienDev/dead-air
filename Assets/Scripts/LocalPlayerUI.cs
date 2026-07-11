using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Local player's vitals HUD (health + oxygen).
/// Hides itself while the local player is dead and restores on revive.
/// </summary>
public class LocalPlayerUI : MonoBehaviour
{
    public static LocalPlayerUI Instance { get; private set; }

    [Header("Vitals HUD")]
    [SerializeField] private TextMeshProUGUI _healthText;
    [SerializeField] private TextMeshProUGUI _oxygenText;

    [Header("Vitals HUD - Radial")]
    [SerializeField] private Image _healthFillImage;
    [SerializeField] private Image _oxygenFillImage;

    [Tooltip("fillAmount shown at FULL oxygen. The oxygen sprite's usable arc doesn't " +
             "cover the whole circle, so full maps to this instead of 1 to line up " +
             "visually (empty is still 0).")]
    [SerializeField, Range(0f, 1f)] private float _oxygenFillMax = 0.78f;

    [Tooltip("Optional container holding the vitals. If set, it is toggled as a " +
             "whole on death; otherwise the health/oxygen labels are toggled individually.")]
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
            _oxygenFillImage.fillAmount = oxygen01 * _oxygenFillMax; // 0..max maps to 0..0.78
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
