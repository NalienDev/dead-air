using System.Collections.Generic;
using System.Linq;
using PurrNet;
using UnityEngine;

public class TheEchoAI : NetworkBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("Echo Playback")]
    [SerializeField, Range(0f, 30f)] private float _echoDelaySeconds = 3f;
    [SerializeField] private AudioSource _audioSource;

    [Header("AI Behaviour")]
    [SerializeField] private float _playerDetectionRadius = 15f;
    [SerializeField] private float _minPlayIntervalSeconds = 5f;
    [SerializeField] private float _maxPlayIntervalSeconds = 15f;

    // ── Private state ──────────────────────────────────────────────────────

    // Server only: clips waiting to be broadcast to clients
    private readonly Queue<(float playAt, CapturedVoiceClip clip)> _pending = new();
    private float _nextDecisionTime;

    // ── Unity lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();
    }

    private void Start()
    {
        TrySubscribe();
        ScheduleNextDecision();
    }

    private void OnEnable() => TrySubscribe();
    private void OnDisable()
    {
        if (VoiceRecordingStore.Instance != null)
            VoiceRecordingStore.Instance.OnClipEnqueued -= HandleClipEnqueued;
    }

    private void Update()
    {
        if (!isServer) return;

        DrainPendingQueue();

        if (Time.time >= _nextDecisionTime)
            TryPickAndScheduleClip();
    }

    // ── Subscription ───────────────────────────────────────────────────────

    private void TrySubscribe()
    {
        if (VoiceRecordingStore.Instance == null) return;
        VoiceRecordingStore.Instance.OnClipEnqueued -= HandleClipEnqueued;
        VoiceRecordingStore.Instance.OnClipEnqueued += HandleClipEnqueued;
    }

    private void HandleClipEnqueued(CapturedVoiceClip incoming) { }

    // ── AI decision loop (server only) ─────────────────────────────────────

    private void TryPickAndScheduleClip()
    {
        ScheduleNextDecision();

        if (VoiceRecordingStore.Instance == null || VoiceRecordingStore.Instance.Count == 0)
            return;

        List<PlayerManager> nearbyPlayers = GetPlayersWithinRadius(_playerDetectionRadius);
        List<PlayerManager> distantPlayers = GetAllPlayers().Except(nearbyPlayers).ToList();

        if (nearbyPlayers.Count == 0)
            return;

        CapturedVoiceClip clip = null;

        if (distantPlayers.Count > 0)
            clip = TryDequeueFromAny(distantPlayers);

        if (clip == null)
        {
            Debug.Log("[TheEchoAI] All players are nearby — choosing audio from a random player.");
            clip = VoiceRecordingStore.Instance.Dequeue();
        }

        if (clip == null)
            return;

        float playAt = Time.time + _echoDelaySeconds;
        _pending.Enqueue((playAt, clip));

        Debug.Log($"[TheEchoAI] Scheduled clip from player '{clip.PlayerId}' — plays at t={playAt:F2}s.");
    }

    private void ScheduleNextDecision()
    {
        _nextDecisionTime = Time.time + Random.Range(_minPlayIntervalSeconds, _maxPlayIntervalSeconds);
    }

    // ── Playback ───────────────────────────────────────────────────────────

    private void DrainPendingQueue()
    {
        while (_pending.Count > 0 && Time.time >= _pending.Peek().playAt)
        {
            (float _, CapturedVoiceClip clip) = _pending.Dequeue();

            // Send raw PCM to all clients so every machine can play it locally
            BroadcastClipToClients(clip.Clip);

            Debug.Log($"[TheEchoAI] Broadcasting clip from '{clip.PlayerId}' to all clients.");
        }
    }

    /// <summary>
    /// Extracts PCM from the AudioClip and sends it to every client via ObserversRpc.
    /// </summary>
    private void BroadcastClipToClients(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);
        RpcPlayClipOnClients(samples, clip.frequency, clip.channels);
    }

    /// <summary>
    /// Runs on every client (including host). Rebuilds the AudioClip and plays it.
    /// </summary>
    [ObserversRpc]
    private void RpcPlayClipOnClients(float[] samples, int frequency, int channels)
    {
        AudioClip clip = AudioClip.Create("EchoPlayback",
            samples.Length / channels, channels, frequency, stream: false);
        clip.SetData(samples, 0);

        _audioSource.Stop();
        _audioSource.clip = clip;
        _audioSource.Play();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private CapturedVoiceClip TryDequeueFromAny(List<PlayerManager> players)
    {
        List<PlayerManager> shuffled = new List<PlayerManager>(players);
        ShuffleList(shuffled);

        foreach (PlayerManager player in shuffled)
        {
            CapturedVoiceClip clip = VoiceRecordingStore.Instance.DequeueFromPlayer(player.owner.ToString());
            if (clip != null)
            {
                Debug.Log($"[TheEchoAI] Dequeued clip from distant player '{clip.PlayerId}' successfully.");
                return clip;
            }
        }

        return null;
    }

    private List<PlayerManager> GetAllPlayers()
        => FindObjectsByType<PlayerManager>(FindObjectsSortMode.None).ToList();

    private List<PlayerManager> GetPlayersWithinRadius(float radius)
    {
        float radiusSq = radius * radius;
        return GetAllPlayers()
            .Where(p => (p.transform.position - transform.position).sqrMagnitude <= radiusSq)
            .ToList();
    }

    private static void ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}