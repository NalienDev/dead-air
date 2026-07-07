using System;
using Dissonance;
using Dissonance.Audio.Capture;
using NAudio.Wave;
using UnityEngine;

/// <summary>
/// Measures how loudly the LOCAL player is speaking through Dissonance and streams a
/// normalised 0..1 loudness to the server, where <see cref="PlayerManager"/> feeds it
/// into the <see cref="NoiseEvents"/> bus for the blind Conductor.
///
/// This is what forces players to whisper: talk normally and the Conductor comes to
/// investigate; shout or scream and it charges. It subscribes to the exact same mic
/// stream as <see cref="VoiceRecorder"/> (Dissonance supports multiple subscribers),
/// so it works off the real voice-chat audio — no separate microphone handling.
///
/// Attach to the player prefab next to <see cref="PlayerManager"/>. Owner-only.
/// </summary>
public class PlayerVoiceNoise : BaseMicrophoneSubscriber
{
    [Header("Loudness Calibration (RMS)")]
    [Tooltip("Mic RMS treated as the quiet end (a whisper). Maps to loudness 0.")]
    [SerializeField, Range(0f, 0.2f)] private float _whisperRms = 0.02f;
    [Tooltip("Mic RMS treated as the loud end (a full shout). Maps to loudness 1.")]
    [SerializeField, Range(0.05f, 1f)] private float _shoutRms = 0.18f;

    [Header("Reporting")]
    [Tooltip("How often (seconds) loudness is sent to the server while speaking.")]
    [SerializeField, Range(0.03f, 0.5f)] private float _reportInterval = 0.1f;
    [Tooltip("Loudness below this is treated as silence and not reported (saves bandwidth). " +
             "The server's value then decays to 0 on its own.")]
    [SerializeField, Range(0f, 0.5f)] private float _reportGate = 0.05f;

    private PlayerManager _playerManager;
    private DissonanceComms _comms;
    private int _sampleRate;

    // ProcessAudio runs per mic frame; we accumulate the peak and flush on a timer
    // in Update so we don't spam an RPC per frame.
    private float _peakLoudness;
    private float _reportTimer;

    private void Awake() => _playerManager = GetComponent<PlayerManager>();

    private void Start()
    {
        if (_playerManager == null || !_playerManager.isOwner)
        {
            enabled = false;
            return;
        }

        _comms = FindFirstObjectByType<DissonanceComms>();
        if (_comms == null)
        {
            Debug.LogError("[PlayerVoiceNoise] DissonanceComms not found in scene.", this);
            enabled = false;
            return;
        }

        _comms.SubscribeToRecordedAudio(this);
    }

    private void OnDestroy()
    {
        DissonanceComms comms = _comms != null ? _comms : FindFirstObjectByType<DissonanceComms>();
        comms?.UnsubscribeFromRecordedAudio(this);
    }

    // BaseMicrophoneSubscriber.Update() pumps captured mic data into ProcessAudio,
    // so we MUST call base.Update() before doing our own throttled reporting.
    public override void Update()
    {
        base.Update();

        _reportTimer += Time.deltaTime;
        if (_reportTimer < _reportInterval) return;
        _reportTimer = 0f;

        float loudness = _peakLoudness;
        _peakLoudness = 0f;

        if (loudness >= _reportGate)
            _playerManager.ReportVoiceLoudness(loudness);
        // Below the gate we simply stop reporting; the server value decays to 0.
    }

    // ── BaseMicrophoneSubscriber ───────────────────────────────────────────

    protected override void ResetAudioStream(WaveFormat waveFormat)
    {
        _sampleRate = waveFormat.SampleRate;
        _peakLoudness = 0f;
    }

    protected override void ProcessAudio(ArraySegment<float> data)
    {
        if (_sampleRate == 0) return;
        if (_comms != null && _comms.IsMuted) return; // respect local mute

        float rms = CalculateRms(data);
        float loudness = Mathf.Clamp01(Mathf.InverseLerp(_whisperRms, _shoutRms, rms));
        if (loudness > _peakLoudness) _peakLoudness = loudness;
    }

    private static float CalculateRms(ArraySegment<float> data)
    {
        if (data.Count == 0) return 0f;
        float sum = 0f;
        int end = data.Offset + data.Count;
        for (int i = data.Offset; i < end; i++)
            sum += data.Array![i] * data.Array[i];
        return Mathf.Sqrt(sum / data.Count);
    }
}
