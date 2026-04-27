using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Server-authoritative queue of every voice clip recorded from all players.
/// Lives on a dedicated GameObject in the scene (not on the player prefab).
///
/// Only server-side scripts should read/remove from this store.
/// </summary>
public class VoiceRecordingStore : MonoBehaviour
{
    public static VoiceRecordingStore Instance { get; private set; }

    /// <summary>Raised on the server whenever a new clip is enqueued.</summary>
    public event Action<CapturedVoiceClip> OnClipEnqueued;

    private readonly Queue<CapturedVoiceClip> _clips = new();

    // ── Unity lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── Public API (server only) ───────────────────────────────────────────

    /// <summary>Adds a captured clip to the back of the queue.</summary>
    public void Enqueue(CapturedVoiceClip clip)
    {
        if (clip == null) throw new ArgumentNullException(nameof(clip));
        _clips.Enqueue(clip);
        OnClipEnqueued?.Invoke(clip);
        Debug.Log($"[VoiceRecordingStore] Enqueued clip from '{clip.PlayerId}' " +
                  $"({clip.Clip.length:F2}s). Queue size: {_clips.Count}");
    }

    /// <summary>
    /// Removes and returns the oldest clip, or null if the queue is empty.
    /// </summary>
    public CapturedVoiceClip Dequeue()
        => _clips.Count > 0 ? _clips.Dequeue() : null;

    /// <summary>
    /// Removes and returns the oldest clip from a specific player, or null if none exist.
    /// </summary>
    public CapturedVoiceClip DequeueFromPlayer(string playerId)
    {
        // Rebuild the queue excluding the first match — O(n) but called rarely
        CapturedVoiceClip found = null;
        int count = _clips.Count;
        for (int i = 0; i < count; i++)
        {
            CapturedVoiceClip item = _clips.Dequeue();
            if (found == null && item.PlayerId == playerId)
                found = item;
            else
                _clips.Enqueue(item);
        }
        return found;
    }

    /// <summary>Peeks at all clips without removing them (read-only snapshot).</summary>
    public IReadOnlyCollection<CapturedVoiceClip> PeekAll() => _clips;

    public int Count => _clips.Count;
}