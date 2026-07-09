using System.Collections.Generic;
using Dissonance;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives a settings canvas that lets the player pick their microphone device
/// and adjust how loud other players sound (Dissonance's RemoteVoiceVolume).
/// Wire the three UI references up in the Inspector; everything else -
/// listing devices, applying the change, saving/restoring the choice - is
/// handled here.
/// </summary>
public class VoiceSettingsUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TMP_Dropdown microphoneDropdown;
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private TextMeshProUGUI volumeLabel;
    [Tooltip("Optional: lets the player mute their own mic entirely.")]
    [SerializeField] private Toggle muteToggle;

    private const string PrefMicDevice = "VoiceSettings_MicDevice";
    private const string PrefVolume = "VoiceSettings_Volume";

    private DissonanceComms _comms;
    private readonly List<string> _devices = new List<string>();
    private bool _initializing;

    private void OnEnable()
    {
        _comms = FindFirstObjectByType<DissonanceComms>();
        if (_comms == null)
        {
            Debug.LogWarning("[VoiceSettingsUI] No DissonanceComms found in the scene - voice settings are disabled.");
            SetInteractable(false);
            return;
        }

        SetInteractable(true);
        PopulateMicrophoneDropdown();
        InitVolumeSlider();
        InitMuteToggle();

        if (microphoneDropdown != null) microphoneDropdown.onValueChanged.AddListener(OnMicrophoneChanged);
        if (volumeSlider != null) volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        if (muteToggle != null) muteToggle.onValueChanged.AddListener(OnMuteChanged);
    }

    private void OnDisable()
    {
        if (microphoneDropdown != null) microphoneDropdown.onValueChanged.RemoveListener(OnMicrophoneChanged);
        if (volumeSlider != null) volumeSlider.onValueChanged.RemoveListener(OnVolumeChanged);
        if (muteToggle != null) muteToggle.onValueChanged.RemoveListener(OnMuteChanged);
    }

    private void SetInteractable(bool value)
    {
        if (microphoneDropdown != null) microphoneDropdown.interactable = value;
        if (volumeSlider != null) volumeSlider.interactable = value;
        if (muteToggle != null) muteToggle.interactable = value;
    }

    // ── Microphone device ────────────────────────────────────────────────

    private void PopulateMicrophoneDropdown()
    {
        if (microphoneDropdown == null) return;

        _devices.Clear();
        _comms.GetMicrophoneDevices(_devices);

        _initializing = true;
        microphoneDropdown.ClearOptions();
        microphoneDropdown.AddOptions(_devices);

        string savedDevice = PlayerPrefs.GetString(PrefMicDevice, _comms.MicrophoneName ?? string.Empty);
        int index = _devices.IndexOf(savedDevice);
        if (index < 0) index = 0;

        if (_devices.Count > 0)
        {
            microphoneDropdown.SetValueWithoutNotify(index);
            _comms.MicrophoneName = _devices[index];
        }
        _initializing = false;
    }

    private void OnMicrophoneChanged(int index)
    {
        if (_initializing || _comms == null) return;
        if (index < 0 || index >= _devices.Count) return;

        string device = _devices[index];
        _comms.MicrophoneName = device;
        PlayerPrefs.SetString(PrefMicDevice, device);
    }

    // ── Output volume (how loud other players are) ──────────────────────

    private void InitVolumeSlider()
    {
        if (volumeSlider == null) return;

        float saved = PlayerPrefs.GetFloat(PrefVolume, _comms.RemoteVoiceVolume);
        volumeSlider.minValue = 0f;
        volumeSlider.maxValue = 1f;
        volumeSlider.SetValueWithoutNotify(saved);

        _comms.RemoteVoiceVolume = saved;
        UpdateVolumeLabel(saved);
    }

    private void OnVolumeChanged(float value)
    {
        if (_comms == null) return;

        _comms.RemoteVoiceVolume = value;
        PlayerPrefs.SetFloat(PrefVolume, value);
        UpdateVolumeLabel(value);
    }

    private void UpdateVolumeLabel(float value)
    {
        if (volumeLabel != null)
            volumeLabel.text = Mathf.RoundToInt(value * 100f) + "%";
    }

    // ── Self mute ────────────────────────────────────────────────────────

    private void InitMuteToggle()
    {
        if (muteToggle == null) return;
        // Listener isn't attached yet at this point in OnEnable, so a plain
        // assignment here won't fire OnMuteChanged (Toggle has no
        // SetValueWithoutNotify in this project's UI package version).
        muteToggle.isOn = _comms.IsMuted;
    }

    private void OnMuteChanged(bool isMuted)
    {
        if (_comms == null) return;
        _comms.IsMuted = isMuted;
    }
}
