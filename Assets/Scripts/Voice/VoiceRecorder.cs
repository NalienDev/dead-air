using System;
using System.Collections.Generic;
using Dissonance;
using Dissonance.Audio.Capture;
using NAudio.Wave;
using PurrNet;
using UnityEngine;

/// <summary>
/// Captures the local player's voice into utterances and ships each completed clip to the server.
/// </summary>
public class VoiceRecorder : BaseMicrophoneSubscriber
{
    [Header("Utterance Segmentation")]
    [Tooltip("Minimum utterance length in seconds. Shorter clips are discarded.")]
    [SerializeField, Range(0.1f, 2f)] private float _minUtteranceSeconds = 0.3f;

    [Tooltip("Maximum utterance buffer length in seconds before it is force-flushed.")]
    [SerializeField, Range(1f, 30f)] private float _maxUtteranceSeconds = 20f;

    private PlayerManager _playerManager;
    private DissonanceComms _dissonanceComms;
    private int _sampleRate;
    private int _channels;

    private readonly List<float> _buffer = new();
    private bool _isRecording = false;   // true while Dissonance is transmitting

    // Cached local player state, source of the IsSpeaking/VAD signal.
    private VoicePlayerState _localState;
    private string _cachedLocalName;

    private void Awake()
    {
        _playerManager = GetComponent<PlayerManager>();
    }

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
        DissonanceComms comms = _dissonanceComms != null ? _dissonanceComms : FindFirstObjectByType<DissonanceComms>();
        comms?.UnsubscribeFromRecordedAudio(this);
    }

    protected override void ResetAudioStream(WaveFormat waveFormat)
    {
        _sampleRate = waveFormat.SampleRate;
        _channels = waveFormat.Channels;

        // Flush whatever is in the buffer, since the stream is resetting.
        TryFlushUtterance(force: true);
        _buffer.Clear();
        _isRecording = false;
    }

    // Copies each frame of pre-processed PCM out before returning, since Dissonance reuses the segment.
    protected override void ProcessAudio(ArraySegment<float> data)
    {
        if (_sampleRate == 0) return;

        // Gate on Dissonance's own transmit decision, so quiet room noise isn't captured.
        bool speaking = IsLocalSpeaking();

        if (speaking)
        {
            _isRecording = true;

            for (int i = data.Offset; i < data.Offset + data.Count; i++)
                _buffer.Add(data.Array![i]);

            // Force-flush if the buffer exceeds the max utterance length.
            if (_buffer.Count >= _maxUtteranceSeconds * _sampleRate * _channels)
                TryFlushUtterance(force: true);
        }
        else if (_isRecording)
        {
            // Dissonance stopped transmitting, so the utterance is complete.
            TryFlushUtterance(force: false);
        }
    }

    // True while Dissonance considers the local player to be transmitting.
    private bool IsLocalSpeaking()
    {
        if (_dissonanceComms == null || _dissonanceComms.IsMuted) return false;

        string localName = _dissonanceComms.LocalPlayerName;
        if (string.IsNullOrEmpty(localName)) return false;

        // Dissonance recreates the local player state on reconnect, so re-resolve it when the name changes.
        if (_localState == null || _cachedLocalName != localName)
        {
            _localState = _dissonanceComms.FindPlayer(localName);
            _cachedLocalName = localName;
        }

        return _localState != null && _localState.IsSpeaking;
    }

    private void TryFlushUtterance(bool force)
    {
        if (_buffer.Count == 0)
        {
            _isRecording = false;
            return;
        }

        float durationSecs = (float)_buffer.Count / (_sampleRate * _channels);

        if (!force && durationSecs < _minUtteranceSeconds)
        {
            _buffer.Clear();
            _isRecording = false;
            return;
        }

        float[] samples = _buffer.ToArray();
        _buffer.Clear();
        _isRecording = false;

        _playerManager.SubmitVoiceClipToServer(samples, _sampleRate, _channels);

        Debug.Log($"[VoiceRecorder] Flushed utterance: {durationSecs:F2}s");
    }
}
