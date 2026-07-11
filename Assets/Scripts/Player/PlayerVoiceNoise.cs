using System;
using Dissonance;
using Dissonance.Audio.Capture;
using NAudio.Wave;
using UnityEngine;

/// <summary>
/// Measures the local player's mic loudness through Dissonance and streams it to the server for the Conductor.
/// </summary>
public class PlayerVoiceNoise : BaseMicrophoneSubscriber
{
    [Header("Loudness Calibration (RMS)")]
    [Tooltip("Mic RMS treated as the quiet end, mapped to loudness 0.")]
    [SerializeField, Range(0f, 0.2f)] private float _whisperRms = 0.02f;
    [Tooltip("Mic RMS treated as the loud end, mapped to loudness 1.")]
    [SerializeField, Range(0.05f, 1f)] private float _shoutRms = 0.18f;

    [Header("Reporting")]
    [Tooltip("How often loudness is sent to the server while speaking.")]
    [SerializeField, Range(0.03f, 0.5f)] private float _reportInterval = 0.1f;
    [Tooltip("Loudness below this is treated as silence and not reported.")]
    [SerializeField, Range(0f, 0.5f)] private float _reportGate = 0.05f;

    private PlayerManager _playerManager;
    private DissonanceComms _comms;
    private int _sampleRate;

    // ProcessAudio runs per mic frame; the peak is flushed on a timer to avoid an RPC per frame.
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

    // base.Update() pumps mic data into ProcessAudio, so it must run before our reporting.
    public override void Update()
    {
        base.Update();

        _reportTimer += Time.deltaTime;
        if (_reportTimer < _reportInterval) return;
        _reportTimer = 0f;

        float loudness = _peakLoudness;
        _peakLoudness = 0f;

        // Below the gate we stop reporting and the server value decays to 0.
        if (loudness >= _reportGate)
            _playerManager.ReportVoiceLoudness(loudness);
    }

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
