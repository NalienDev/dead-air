using PurrNet;
using UnityEngine;

/// <summary>
/// Exterior dungeon entrance that teleports the interacting player to the dungeon spawn point.
/// </summary>
public class MainEntrance : Interactable
{
    [Tooltip("The point inside the dungeon the player is sent to.")]
    [SerializeField] private Transform _dungeonSpawnPoint;

    [Tooltip("Sun to disable while the player is underground.")]
    [SerializeField] private GameObject _sun;

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

        PlayerManager playerManager = user.GetComponent<PlayerManager>();
        if (playerManager != null)
            playerManager.SetInsideDungeon(true);

        _sun?.SetActive(false);

        return InteractionType.PRESS;
    }

    public Transform getSpawnPoint()
    {
        return _dungeonSpawnPoint;
    }
}
