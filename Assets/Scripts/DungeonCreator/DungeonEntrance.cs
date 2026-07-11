using PurrNet;
using UnityEngine;

/// <summary>
/// Interior dungeon exit that teleports the interacting player back to the outside spawn point.
/// </summary>
public class DungeonEntrance : Interactable
{
    public override InteractionType OnInteract(GameObject user)
    {
        GameObject _outsideSpawnPoint = FindFirstObjectByType<OutsideSpawnPoint>().gameObject;

        if (_outsideSpawnPoint == null)
        {
            Debug.LogWarning("[DungeonEntrance] No outside spawn point assigned.", this);
            return InteractionType.NONE;
        }

        // Disable the CharacterController around the teleport to avoid physics conflicts.
        CharacterController cc = user.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        user.transform.SetPositionAndRotation(
            _outsideSpawnPoint.transform.position,
            _outsideSpawnPoint.transform.rotation
        );

        if (cc != null) cc.enabled = true;

        PlayerManager playerManager = user.GetComponent<PlayerManager>();
        if (playerManager != null)
            playerManager.SetInsideDungeon(false);

        return InteractionType.PRESS;
    }
}
