using System;
using System.Collections.Generic;
using Dissonance;
using Dissonance.Audio.Capture;
using Dissonance.Config;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Voice options panel that works WITHOUT a DissonanceComms in the scene (e.g. in the
/// Steam-lobby menu). Everything is saved on the device and applied to the real comms
/// that spawns in Main:
/// <list type="bullet">
/// <item>Mic device / remote volume / self-mute → <see cref="VoiceSettingsStore"/>
/// (PlayerPrefs). <see cref="VoiceSettingsApplier"/> pushes these onto Main's comms.</item>
/// <item>VAD sensitivity / noise suppression / background-noise removal →
/// <c>VoiceSettings.Instance</c>, which Dissonance already persists to PlayerPrefs and
/// every comms reads on startup.</item>
/// </list>
/// If a comms DOES exist while this panel is open (e.g. opened in Main), the per-comms
/// settings are also applied to it live.
///
/// All UI references are optional — wire only the controls your panel has.
/// </summary>
public class VoiceSettingsUI : MonoBehaviour
{
    [Header("Device / Output (per-comms, saved on device)")]
    [SerializeField] private TMP_Dropdown microphoneDropdown;
    [SerializeField] private Slider volumeSlider;
    [SerializeField] private TextMeshProUGUI volumeLabel;
    [SerializeField] private Toggle muteToggle;

    [Header("Preprocessing (global Dissonance settings, saved on device)")]
    [Tooltip("Optional. VAD sensitivity — higher picks up quieter speech (and more noise).")]
    [SerializeField] private TMP_Dropdown vadSensitivityDropdown;
    [Tooltip("Optional. Noise-suppression strength.")]
    [SerializeField] private TMP_Dropdown noiseSuppressionDropdown;
    [Tooltip("Optional. Background-noise removal on/off (RNNoise).")]
    [SerializeField] private Toggle backgroundRemovalToggle;

    private readonly List<string> _devices = new List<string>();
    private bool _initializing;

    // A comms only exists in Main; in the menu this is null and that's fine.
    private static DissonanceComms FindComms() => FindFirstObjectByType<DissonanceComms>();

    private void OnEnable()
    {
        _initializing = true;

        InitMicrophones();
        InitVolume();
        InitMute();
        InitEnumDropdown(vadSensitivityDropdown, typeof(VadSensitivityLevels), (int)VoiceSettings.Instance.VadSensitivity);
        InitEnumDropdown(noiseSuppressionDropdown, typeof(NoiseSuppressionLevels), (int)VoiceSettings.Instance.DenoiseAmount);
        if (backgroundRemovalToggle != null)
            backgroundRemovalToggle.isOn = VoiceSettings.Instance.BackgroundSoundRemovalEnabled;

        _initializing = false;

        if (microphoneDropdown) microphoneDropdown.onValueChanged.AddListener(OnMicChanged);
        if (volumeSlider) volumeSlider.onValueChanged.AddListener(OnVolumeChanged);
        if (muteToggle) muteToggle.onValueChanged.AddListener(OnMuteChanged);
        if (vadSensitivityDropdown) vadSensitivityDropdown.onValueChanged.AddListener(OnVadChanged);
        if (noiseSuppressionDropdown) noiseSuppressionDropdown.onValueChanged.AddListener(OnNoiseChanged);
        if (backgroundRemovalToggle) backgroundRemovalToggle.onValueChanged.AddListener(OnBgRemovalChanged);
    }

    private void OnDisable()
    {
        if (microphoneDropdown) microphoneDropdown.onValueChanged.RemoveListener(OnMicChanged);
        if (volumeSlider) volumeSlider.onValueChanged.RemoveListener(OnVolumeChanged);
        if (muteToggle) muteToggle.onValueChanged.RemoveListener(OnMuteChanged);
        if (vadSensitivityDropdown) vadSensitivityDropdown.onValueChanged.RemoveListener(OnVadChanged);
        if (noiseSuppressionDropdown) noiseSuppressionDropdown.onValueChanged.RemoveListener(OnNoiseChanged);
        if (backgroundRemovalToggle) backgroundRemovalToggle.onValueChanged.RemoveListener(OnBgRemovalChanged);
    }

    // ── Microphone device (Unity enumerates devices, no comms needed) ────────

    private void InitMicrophones()
    {
        if (microphoneDropdown == null) return;

        _devices.Clear();
        _devices.AddRange(Microphone.devices);

        microphoneDropdown.ClearOptions();
        microphoneDropdown.AddOptions(_devices.Count > 0 ? _devices : new List<string> { "(no microphones)" });

        string saved = VoiceSettingsStore.MicDevice;
        int index = _devices.IndexOf(saved);
        if (index < 0) index = 0;
        microphoneDropdown.SetValueWithoutNotify(index);
    }

    private void OnMicChanged(int index)
    {
        if (_initializing || index < 0 || index >= _devices.Count) return;

        string device = _devices[index];
        VoiceSettingsStore.MicDevice = device;

        DissonanceComms comms = FindComms();
        if (comms != null) comms.MicrophoneName = device;
    }

    // ── Output volume (how loud other players are) ───────────────────────────

    private void InitVolume()
    {
        if (volumeSlider == null) return;

        float saved = VoiceSettingsStore.RemoteVolume;
        volumeSlider.minValue = 0f;
        volumeSlider.maxValue = 1f;
        volumeSlider.SetValueWithoutNotify(saved);
        UpdateVolumeLabel(saved);
    }

    private void OnVolumeChanged(float value)
    {
        VoiceSettingsStore.RemoteVolume = value;
        UpdateVolumeLabel(value);

        DissonanceComms comms = FindComms();
        if (comms != null) comms.RemoteVoiceVolume = value;
    }

    private void UpdateVolumeLabel(float value)
    {
        if (volumeLabel != null) volumeLabel.text = Mathf.RoundToInt(value * 100f) + "%";
    }

    // ── Self mute ────────────────────────────────────────────────────────────

    private void InitMute()
    {
        if (muteToggle == null) return;
        muteToggle.isOn = VoiceSettingsStore.SelfMuted;
    }

    private void OnMuteChanged(bool isMuted)
    {
        VoiceSettingsStore.SelfMuted = isMuted;

        DissonanceComms comms = FindComms();
        if (comms != null) comms.IsMuted = isMuted;
    }

    // ── Preprocessing (global VoiceSettings.Instance — self-persisting) ──────

    private void OnVadChanged(int index)
    {
        if (_initializing) return;
        VoiceSettings.Instance.VadSensitivity = (VadSensitivityLevels)EnumValueAt(typeof(VadSensitivityLevels), index);
    }

    private void OnNoiseChanged(int index)
    {
        if (_initializing) return;
        VoiceSettings.Instance.DenoiseAmount = (NoiseSuppressionLevels)EnumValueAt(typeof(NoiseSuppressionLevels), index);
    }

    private void OnBgRemovalChanged(bool enabled)
    {
        if (_initializing) return;
        VoiceSettings.Instance.BackgroundSoundRemovalEnabled = enabled;
    }

    // ── Enum-dropdown helpers ────────────────────────────────────────────────

    private static void InitEnumDropdown(TMP_Dropdown dropdown, Type enumType, int currentValue)
    {
        if (dropdown == null) return;

        var names = new List<string>(Enum.GetNames(enumType));
        dropdown.ClearOptions();
        dropdown.AddOptions(names);

        Array values = Enum.GetValues(enumType);
        int index = 0;
        for (int i = 0; i < values.Length; i++)
            if ((int)values.GetValue(i) == currentValue) { index = i; break; }

        dropdown.SetValueWithoutNotify(index);
    }

    private static int EnumValueAt(Type enumType, int index)
    {
        Array values = Enum.GetValues(enumType);
        index = Mathf.Clamp(index, 0, values.Length - 1);
        return (int)values.GetValue(index);
    }
}
