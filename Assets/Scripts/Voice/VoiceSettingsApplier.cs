using System.Collections;
using Dissonance;
using UnityEngine;

/// <summary>
/// Put this on the DissonanceComms GameObject in the Main scene. It applies the
/// device-persisted per-comms voice settings (<see cref="VoiceSettingsStore"/>) onto this
/// comms once it's ready — so mic/volume/mute choices made in the menu (which has no
/// comms) take effect here, on the comms that actually connects to voice chat.
///
/// The preprocessor settings (sensitivity/denoise/background removal) need nothing here:
/// Dissonance reads those from its global VoiceSettings.Instance automatically.
/// </summary>
[RequireComponent(typeof(DissonanceComms))]
public class VoiceSettingsApplier : MonoBehaviour
{
    private DissonanceComms _comms;

    private void Awake() => _comms = GetComponent<DissonanceComms>();

    private void OnEnable() => StartCoroutine(ApplyWhenReady());

    private IEnumerator ApplyWhenReady()
    {
        // Give the comms/mic pipeline a couple of frames to initialise before setting the
        // microphone device (which triggers a capture restart).
        yield return null;
        yield return null;
        VoiceSettingsStore.ApplyTo(_comms);
    }
}
