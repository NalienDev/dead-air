using System.Collections.Generic;
using UnityEngine;

public class ReviveDeadPlayersButton : Interactable
{
    public override InteractionType OnInteract(GameObject user)
    {
        PlayerManager[] players = FindObjectsByType<PlayerManager>(sortMode: FindObjectsSortMode.None);
        for (int i = 0; i < players.Length; i++)
        {
            if (players[i].IsDead)
            {
                players[i].GetComponent<PlayerDeathHandler>().Revive(100, 360);
            }
        }
        return InteractionType.PRESS;
    }
}
