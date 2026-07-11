using System;
using UnityEngine;

/// <summary>
/// Server-side noise bus connecting noise sources to listeners like the Conductor.
/// </summary>
// Loudness is normalised 0..1: ~0.15 whisper, ~0.40 talking/walking, ~0.60 running,
// ~0.85+ shout. Raise only on the server; owner clients forward local noise via a ServerRpc.
public static class NoiseEvents
{
    // Raised on the server whenever a noise is made: (worldPosition, loudness01).
    public static event Action<Vector3, float> OnNoise;

    // Safe to call every frame; the Conductor decides whether a loudness is worth reacting to.
    public static void Report(Vector3 worldPosition, float loudness01)
    {
        if (loudness01 <= 0f) return;
        OnNoise?.Invoke(worldPosition, Mathf.Clamp01(loudness01));
    }
}
