using UnityEngine;

/// <summary>
/// Upgrade that pitches up the Echo's stolen-voice playback, making the mimic easier to catch.
/// </summary>
[CreateAssetMenu(menuName = "DeadAir/Upgrades/Echo Voice Pitch", fileName = "Upgrade_EchoVoicePitch")]
public class EchoVoicePitchUpgrade : UpgradeDefinition
{
    [Tooltip("Pitch multiplier applied to the Echo's voice.")]
    [SerializeField] private float _pitch = 1.5f;

    public override void ServerApply(PlayerUpgrades player, float rolledValue)
        => player.ServerSetEchoVoicePitch(_pitch);
}
