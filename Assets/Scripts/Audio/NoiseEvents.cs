using System;
using UnityEngine;

/// <summary>
/// Server-side noise bus. Decouples noise <b>sources</b> (player voices, footsteps,
/// dropped/grabbed items, world <see cref="SoundEmitter"/>s) from noise
/// <b>listeners</b> (currently only <c>TheConductorAI</c>).
///
/// Everything here is plain C# and is expected to be raised ONLY on the server/host —
/// the Conductor's whole brain runs server-side, so there is no reason to pay for an
/// RPC per footstep. Owner clients that detect a local noise (e.g. their own mic
/// loudness) forward it to the server through a normal <c>ServerRpc</c> first, and the
/// server body then calls <see cref="Report"/>.
///
/// <para><b>Loudness</b> is a normalised 0..1 value so every source speaks the same
/// language regardless of how it was measured:</para>
/// <list type="bullet">
/// <item>~0.15 whisper (usually below the Conductor's hearing threshold)</item>
/// <item>~0.40 talking at a normal volume / walking</item>
/// <item>~0.60 running / grabbing a noisy item</item>
/// <item>~0.85+ shout or scream — a "sound spike"</item>
/// </list>
/// </summary>
public static class NoiseEvents
{
    /// <summary>
    /// Raised on the server whenever a noise is made.
    /// Args: (worldPosition, loudness01).
    /// </summary>
    public static event Action<Vector3, float> OnNoise;

    /// <summary>
    /// Report a noise at a world position. Safe to call every frame; the Conductor
    /// decides for itself whether a given loudness is worth reacting to.
    /// </summary>
    public static void Report(Vector3 worldPosition, float loudness01)
    {
        if (loudness01 <= 0f) return;
        OnNoise?.Invoke(worldPosition, Mathf.Clamp01(loudness01));
    }
}
