using System.Collections;
using Dissonance;
using UnityEngine;

/// <summary>
/// Applies the persisted mic, volume, and mute settings onto the DissonanceComms once it's ready.
/// </summary>
[RequireComponent(typeof(DissonanceComms))]
public class VoiceSettingsApplier : MonoBehaviour
{
    private DissonanceComms _comms;

    private void Awake() => _comms = GetComponent<DissonanceComms>();

    private void OnEnable() => StartCoroutine(ApplyWhenReady());

    private IEnumerator ApplyWhenReady()
    {
        // Let the comms/mic pipeline initialise before setting the device, which restarts capture.
        yield return null;
        yield return null;
        VoiceSettingsStore.ApplyTo(_comms);
    }
}
