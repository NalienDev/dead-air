using UnityEngine;

/// <summary>
/// Not repeatable, rare, gated behind MinExpeditions (e.g. 3). A GLOBAL effect: it
/// pitches up the Echo's stolen-voice playback for everyone, making the mimic easier
/// to catch. Routed through the buyer's PlayerUpgrades for a uniform apply path, but
/// it reaches into every TheEchoAI in the scene server-side.
/// </summary>
[CreateAssetMenu(menuName = "DeadAir/Upgrades/Echo Voice Pitch", fileName = "Upgrade_EchoVoicePitch")]
public class EchoVoicePitchUpgrade : UpgradeDefinition
{
    [Tooltip("Pitch multiplier applied to the Echo's voice (1 = normal, 1.5 = higher).")]
    [SerializeField] private float _pitch = 1.5f;

    public override void ServerApply(PlayerUpgrades player, float rolledValue)
        => player.ServerSetEchoVoicePitch(_pitch);
}
