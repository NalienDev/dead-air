using TMPro;
using UnityEngine;

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

    [Tooltip("Optional container holding the vitals. If set, it is toggled as a " +
             "whole on death; otherwise the health/oxygen labels are toggled individually.")]
    [SerializeField] private GameObject _vitalsRoot;

    private bool _hidden;

    /// <summary>
    /// True while some other system (e.g. the upgrade/fortune-teller canvas) has
    /// forced the HUD hidden. While this is set, <see cref="Update"/> stops driving
    /// visibility from the death state so it can't fight with the external caller.
    /// </summary>
    private bool _suspended;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Force the vitals HUD hidden (true) or let it resume tracking the death state
    /// (false). Used by systems that take over the whole screen, like the upgrade
    /// canvas — call this instead of touching the vitals GameObject directly, or it
    /// will just get reactivated on the next frame's death check.
    /// </summary>
    public void SetSuspended(bool suspended)
    {
        _suspended = suspended;

        if (suspended)
        {
            SetVitalsVisible(false);
            return;
        }

        bool dead = PlayerManager.Local != null && PlayerManager.Local.IsDead;
        SetVitalsVisible(!dead);
    }

    private void Update()
    {
        if (_suspended) return;

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
