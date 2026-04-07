using PurrNet;
using UnityEngine;

/// <summary>
/// Exterior dungeon entrance — teleports the interacting player to the dungeon spawn point.
/// Attach to the door/trigger outside the dungeon.
/// Extends <see cref="Interactable"/> so your existing <see cref="Interactor"/> detects it.
/// </summary>
public class MainEntrance : Interactable
{
    // ── Inspector ──────────────────────────────────────────────────────────

    [Tooltip("The world-space point inside the dungeon the player is sent to.")]
    [SerializeField] private Transform _dungeonSpawnPoint;

    [Tooltip("Optional sun GameObject to disable while the player is underground.")]
    [SerializeField] private GameObject _sun;

    // ── Interactable ───────────────────────────────────────────────────────

    public override InteractionType OnInteract(GameObject user)
    {
        if (_dungeonSpawnPoint == null)
        {
            Debug.LogWarning("[MainEntrance] No dungeon spawn point assigned.", this);
            return InteractionType.NONE;
        }

        CharacterController cc = user.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        user.transform.SetPositionAndRotation(
            _dungeonSpawnPoint.position,
            _dungeonSpawnPoint.rotation
        );

        if (cc != null) cc.enabled = true;

        // Tell PlayerManager the player is now inside the dungeon
        PlayerManager playerManager = user.GetComponent<PlayerManager>();
        if (playerManager != null)
            playerManager.SetInsideDungeon(true);

        _sun?.SetActive(false);

        return InteractionType.PRESS;
    }
}
