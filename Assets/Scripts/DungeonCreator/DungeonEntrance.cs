using PurrNet;
using UnityEngine;

/// <summary>
/// Interior dungeon exit — teleports the interacting player back to the outside point.
/// Attach to the exit trigger/door inside the dungeon.
/// Extends <see cref="Interactable"/> so your existing <see cref="Interactor"/> detects it.
/// </summary>
public class DungeonEntrance : Interactable
{
    public override InteractionType OnInteract(GameObject user)
    {
        Debug.Log("Ran lol");

        GameObject _outsideSpawnPoint = FindFirstObjectByType<OutsideSpawnPoint>().gameObject;

        if (_outsideSpawnPoint == null)
        {
            Debug.LogWarning("[DungeonEntrance] No outside spawn point assigned.", this);
            return InteractionType.NONE;
        }

        // Disable the CharacterController around the teleport to avoid physics conflicts
        CharacterController cc = user.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        user.transform.SetPositionAndRotation(
            _outsideSpawnPoint.transform.position,
            _outsideSpawnPoint.transform.rotation
        );

        if (cc != null) cc.enabled = true;

        // Tell PlayerManager the player is no longer inside the dungeon
        PlayerManager playerManager = user.GetComponent<PlayerManager>();
        if (playerManager != null)
            playerManager.SetInsideDungeon(false);

        return InteractionType.PRESS;
    }
}
