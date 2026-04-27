using System;
using System.Collections.Generic;
using Dissonance;
using Dissonance.Audio.Capture;
using NAudio.Wave;
using PurrNet;
using UnityEngine;

/// <summary>
/// Captures the local player's microphone via Dissonance's BaseMicrophoneSubscriber,
/// segments audio into utterances using silence detection, then ships each completed
/// AudioClip to the server via RPC so it can be stored in <see cref="VoiceRecordingStore"/>.
///
/// Attach to the player prefab. Only activates for the local owner.
/// </summary>
public class VoiceRecorder : BaseMicrophoneSubscriber
{
    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("Silence Detection")]
    [Tooltip("RMS level below which audio is considered silence.")]
    [SerializeField, Range(0f, 0.1f)] private float _silenceThreshold = 0.01f;

    [Tooltip("Seconds of silence required to treat the preceding audio as a complete utterance.")]
    [SerializeField, Range(0.1f, 3f)] private float _silenceGapSeconds = 0.6f;

    [Tooltip("Minimum utterance length in seconds. Shorter clips are discarded.")]
    [SerializeField, Range(0.1f, 2f)] private float _minUtteranceSeconds = 0.3f;

    [Tooltip("Maximum utterance buffer length in seconds before it is force-flushed.")]
    [SerializeField, Range(1f, 30f)] private float _maxUtteranceSeconds = 20f;

    // ── Private state ──────────────────────────────────────────────────────

    private PlayerManager _playerManager;
    private int _sampleRate;
    private int _channels;

    private readonly List<float> _buffer = new();
    private float _silenceTimer = 0f;
    private bool _isRecording = false;   // true while collecting non-silent frames

    // ── Unity lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        _playerManager = GetComponent<PlayerManager>();
    }

    private DissonanceComms _dissonanceComms;

    private void Start()
    {
        if (_playerManager == null || !_playerManager.isOwner)
        {
            enabled = false;
            return;
        }

        _dissonanceComms = FindFirstObjectByType<DissonanceComms>();
        if (_dissonanceComms == null)
        {
            Debug.LogError("[VoiceRecorder] DissonanceComms not found in scene.", this);
            enabled = false;
            return;
        }

        _dissonanceComms.SubscribeToRecordedAudio(this);
    }

    private void OnDestroy()
    {
        DissonanceComms comms = FindFirstObjectByType<DissonanceComms>();
        comms?.UnsubscribeFromRecordedAudio(this);
    }

    // ── BaseMicrophoneSubscriber ───────────────────────────────────────────

    /// <summary>Called by Dissonance when the audio format changes or the stream resets.</summary>
    protected override void ResetAudioStream(WaveFormat waveFormat)
    {
        _sampleRate = waveFormat.SampleRate;
        _channels = waveFormat.Channels;

        // Flush whatever is in the buffer — stream is resetting
        TryFlushUtterance(force: true);
        _buffer.Clear();
        _silenceTimer = 0f;
        _isRecording = false;
    }

    /// <summary>
    /// Called on the main thread by Dissonance for every frame of PCM data.
    /// Must copy data out before returning — the segment is reused by Dissonance.
    /// </summary>
    protected override void ProcessAudio(ArraySegment<float> data)
    {
        if (_sampleRate == 0) return;

        // Respect Dissonance's local mute — don't capture if the player has muted themselves
        if (_dissonanceComms != null && _dissonanceComms.IsMuted)
            return;

        float rms = CalculateRms(data);
        bool isSilent = rms < _silenceThreshold;
        float frameSecs = (float)data.Count / (_sampleRate * _channels);

        if (!isSilent)
        {
            _isRecording = true;
            _silenceTimer = 0f;

            // Copy samples into our accumulation buffer
            for (int i = data.Offset; i < data.Offset + data.Count; i++)
                _buffer.Add(data.Array![i]);

            // Force-flush if the buffer exceeds the max utterance length
            if (_buffer.Count >= _maxUtteranceSeconds * _sampleRate * _channels)
                TryFlushUtterance(force: true);
        }
        else if (_isRecording)
        {
            // Still append trailing silence so we don't clip the end of words
            for (int i = data.Offset; i < data.Offset + data.Count; i++)
                _buffer.Add(data.Array![i]);

            _silenceTimer += frameSecs;

            if (_silenceTimer >= _silenceGapSeconds)
                TryFlushUtterance(force: false);
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────

    private void TryFlushUtterance(bool force)
    {
        if (_buffer.Count == 0) return;

        float durationSecs = (float)_buffer.Count / (_sampleRate * _channels);

        if (!force && durationSecs < _minUtteranceSeconds)
        {
            _buffer.Clear();
            _silenceTimer = 0f;
            _isRecording = false;
            return;
        }

        float[] samples = _buffer.ToArray();
        _buffer.Clear();
        _silenceTimer = 0f;
        _isRecording = false;

        AudioClip clip = BuildClip(samples, _sampleRate, _channels);
        _playerManager.SubmitVoiceClipToServer(samples, _sampleRate, _channels);

        Debug.Log($"[VoiceRecorder] Flushed utterance: {durationSecs:F2}s");
    }

    private static AudioClip BuildClip(float[] samples, int sampleRate, int channels)
    {
        AudioClip clip = AudioClip.Create("VoiceCapture", samples.Length / channels,
                                          channels, sampleRate, stream: false);
        clip.SetData(samples, offsetSamples: 0);
        return clip;
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