using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Server-side queue of every voice clip recorded from all players.
/// </summary>
public class VoiceRecordingStore : MonoBehaviour
{
    public static VoiceRecordingStore Instance { get; private set; }

    // Raised on the server whenever a new clip is enqueued.
    public event Action<CapturedVoiceClip> OnClipEnqueued;

    private readonly Queue<CapturedVoiceClip> _clips = new();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void Enqueue(CapturedVoiceClip clip)
    {
        if (clip == null) throw new ArgumentNullException(nameof(clip));
        _clips.Enqueue(clip);
        OnClipEnqueued?.Invoke(clip);
        Debug.Log($"[VoiceRecordingStore] Enqueued clip from '{clip.PlayerId}' " +
                  $"({clip.Clip.length:F2}s). Queue size: {_clips.Count}");
    }

    // Removes and returns the oldest clip, or null if the queue is empty.
    public CapturedVoiceClip Dequeue()
        => _clips.Count > 0 ? _clips.Dequeue() : null;

    // Removes and returns the oldest clip from a specific player, or null if none exist.
    public CapturedVoiceClip DequeueFromPlayer(string playerId)
    {
        // Rebuild the queue excluding the first match; O(n) but called rarely.
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

    public IReadOnlyCollection<CapturedVoiceClip> PeekAll() => _clips;

    public int Count => _clips.Count;
}