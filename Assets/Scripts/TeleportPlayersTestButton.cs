using PurrNet;
using UnityEngine;

public class TeleportPlayersTestButton : Interactable
{
    [SerializeField] private Transform _teleportTransform;

    public override InteractionType OnInteract(GameObject user)
    {

        PlayerManager[] players = FindObjectsByType<PlayerManager>(FindObjectsSortMode.None);

        for (int i = 0; i < players.Length; i++)
        {
            PlayerManager player = players[i];
            player.gameObject.transform.SetPositionAndRotation(_teleportTransform.position, _teleportTransform.rotation);

        }

        return InteractionType.PRESS;
    }

}
