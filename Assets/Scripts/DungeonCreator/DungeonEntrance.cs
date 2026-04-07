using PurrNet;
using UnityEngine;

/// <summary>
/// Interior dungeon exit — teleports the interacting player back to the outside point.
/// Attach to the exit trigger/door inside the dungeon.
/// Extends <see cref="Interactable"/> so your existing <see cref="Interactor"/> detects it.
/// </summary>
public class DungeonEntrance : Interactable
{
    // ── Inspector ──────────────────────────────────────────────────────────

    [Tooltip("The world-space point the player is sent to when they use this exit.")]
    [SerializeField] private Transform _outsideSpawnPoint;

    [Tooltip("Optional sun GameObject to re-enable when the player exits the dungeon.")]
    [SerializeField] private GameObject _sun;

    // ── Interactable ───────────────────────────────────────────────────────

    public override InteractionType OnInteract(GameObject user)
    {
        if (_outsideSpawnPoint == null)
        {
            Debug.LogWarning("[DungeonEntrance] No outside spawn point assigned.", this);
            return InteractionType.NONE;
        }

        // Disable the CharacterController around the teleport to avoid physics conflicts
        CharacterController cc = user.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        user.transform.SetPositionAndRotation(
            _outsideSpawnPoint.position,
            _outsideSpawnPoint.rotation
        );

        if (cc != null) cc.enabled = true;

        // Tell PlayerManager the player is no longer inside the dungeon
        PlayerManager playerManager = user.GetComponent<PlayerManager>();
        if (playerManager != null)
            playerManager.SetInsideDungeon(false);

        _sun?.SetActive(true);

        return InteractionType.PRESS;
    }
}
