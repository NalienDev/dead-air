using Dissonance;
using UnityEngine;

/// <summary>
/// PlayerPrefs store for per-comms voice settings: microphone device, remote volume, and self-mute.
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

    // Pushes the stored settings onto a live comms.
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
