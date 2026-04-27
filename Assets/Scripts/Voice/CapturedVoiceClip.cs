using UnityEngine;

/// <summary>
/// An audio clip recorded from a player's microphone, captured on the server.
/// </summary>
public sealed class CapturedVoiceClip
{
    public string PlayerId { get; }
    public AudioClip Clip { get; }
    public float CapturedAt { get; }   // Time.time on server

    public CapturedVoiceClip(string playerId, AudioClip clip, float capturedAt)
    {
        PlayerId = playerId;
        Clip = clip;
        CapturedAt = capturedAt;
    }
}