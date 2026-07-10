using System;
using System.Collections.Generic;
using Dissonance;
using Dissonance.Audio.Capture;
using NAudio.Wave;
using PurrNet;
using UnityEngine;

/// <summary>
/// Captures the local player's voice via Dissonance's BaseMicrophoneSubscriber, segments
/// it into utterances, then ships each completed AudioClip to the server via RPC so it
/// can be stored in <see cref="VoiceRecordingStore"/>.
///
/// The audio comes from <c>SubscribeToRecordedAudio</c>, which is the PRE-PROCESSED stream
/// (Dissonance has already applied noise suppression / background-noise removal / AGC).
/// Rather than run our own naive silence detection — which picked up quiet room noise —
/// we gate recording on Dissonance's OWN decision to transmit: the local player's
/// <see cref="VoicePlayerState.IsSpeaking"/>. For a VAD-activated broadcast trigger (the
/// proximity chat) that flag is driven by Dissonance's VAD at the configured sensitivity,
/// so we capture exactly what Dissonance would send to other players — nothing more.
///
/// Attach to the player prefab. Only activates for the local owner.
/// </summary>
public class VoiceRecorder : BaseMicrophoneSubscriber
{
    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("Utterance Segmentation")]
    [Tooltip("Minimum utterance length in seconds. Shorter clips are discarded.")]
    [SerializeField, Range(0.1f, 2f)] private float _minUtteranceSeconds = 0.3f;

    [Tooltip("Maximum utterance buffer length in seconds before it is force-flushed.")]
    [SerializeField, Range(1f, 30f)] private float _maxUtteranceSeconds = 20f;

    // ── Private state ──────────────────────────────────────────────────────

    private PlayerManager _playerManager;
    private DissonanceComms _dissonanceComms;
    private int _sampleRate;
    private int _channels;

    private readonly List<float> _buffer = new();
    private bool _isRecording = false;   // true while Dissonance is transmitting

    // Cached local player state (source of the IsSpeaking / VAD signal).
    private VoicePlayerState _localState;
    private string _cachedLocalName;

    // ── Unity lifecycle ────────────────────────────────────────────────────

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

    // ── BaseMicrophoneSubscriber ───────────────────────────────────────────

    /// <summary>Called by Dissonance when the audio format changes or the stream resets.</summary>
    protected override void ResetAudioStream(WaveFormat waveFormat)
    {
        _sampleRate = waveFormat.SampleRate;
        _channels = waveFormat.Channels;

        // Flush whatever is in the buffer — stream is resetting
        TryFlushUtterance(force: true);
        _buffer.Clear();
        _isRecording = false;
    }

    /// <summary>
    /// Called on the main thread by Dissonance for every frame of PRE-PROCESSED PCM data.
    /// Must copy data out before returning — the segment is reused by Dissonance.
    /// </summary>
    protected override void ProcessAudio(ArraySegment<float> data)
    {
        if (_sampleRate == 0) return;

        // Gate on Dissonance's own transmit decision (VAD/sensitivity/trigger/mute).
        // When it's not "speaking", nothing is being sent to other players, so we don't
        // record it either — this is what stops quiet room noise being captured.
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
            // Dissonance stopped transmitting — the utterance is complete.
            TryFlushUtterance(force: false);
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────

    /// <summary>
    /// True while Dissonance considers the local player to be transmitting. This reflects
    /// the VAD (at the configured sensitivity), any active broadcast trigger, and the
    /// self-mute state — i.e. exactly when real voice is being sent to other players.
    /// </summary>
    private bool IsLocalSpeaking()
    {
        if (_dissonanceComms == null || _dissonanceComms.IsMuted) return false;

        string localName = _dissonanceComms.LocalPlayerName;
        if (string.IsNullOrEmpty(localName)) return false;

        // Dissonance recreates the local player state on (re)connect, so re-resolve it
        // whenever the name changes or we don't have one yet.
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
