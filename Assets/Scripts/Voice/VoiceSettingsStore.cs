using Dissonance;
using UnityEngine;

/// <summary>
/// Device-persistent (PlayerPrefs) store for the PER-COMMS voice settings: microphone
/// device, remote voice volume, and self-mute. These live on a <see cref="DissonanceComms"/>
/// INSTANCE, so they don't survive switching to the Main scene's comms on their own —
/// this persists them and <see cref="VoiceSettingsApplier"/> re-applies them there.
///
/// NOTE: the preprocessor settings (VAD sensitivity, noise suppression, background-noise
/// removal, AEC, quality…) are deliberately NOT here — Dissonance's own
/// <c>VoiceSettings.Instance</c> already persists those to PlayerPrefs and every comms
/// reads them automatically, so setting <c>VoiceSettings.Instance.X</c> in the menu is
/// enough and needs no comms and no applier.
/// </summary>
public static class VoiceSettingsStore
{
    private const string PrefMic = "VoiceSettings_MicDevice";
    private const string PrefVolume = "VoiceSettings_Volume";
    private const string PrefMute = "VoiceSettings_Mute";

    public static string MicDevice
    {
        get => PlayerPrefs.GetString(PrefMic, string.Empty);
        set { PlayerPrefs.SetString(PrefMic, value ?? string.Empty); PlayerPrefs.Save(); }
    }

    public static float RemoteVolume
    {
        get => PlayerPrefs.GetFloat(PrefVolume, 1f);
        set { PlayerPrefs.SetFloat(PrefVolume, Mathf.Clamp01(value)); PlayerPrefs.Save(); }
    }

    public static bool SelfMuted
    {
        get => PlayerPrefs.GetInt(PrefMute, 0) == 1;
        set { PlayerPrefs.SetInt(PrefMute, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    /// <summary>Pushes the stored instance-level settings onto a live comms.</summary>
    public static void ApplyTo(DissonanceComms comms)
    {
        if (comms == null) return;

        string mic = MicDevice;
        if (!string.IsNullOrEmpty(mic) && comms.MicrophoneName != mic)
            comms.MicrophoneName = mic;

        comms.RemoteVoiceVolume = RemoteVolume;
        comms.IsMuted = SelfMuted;
    }
}
