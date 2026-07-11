using System.Collections;
using System.Threading.Tasks;
using PurrLobby;
using UnityEngine;

/// <summary>
/// Snapshots the local user's Steam id and name into LobbyLocalUser while still in the lobby scene.
/// </summary>
public class LobbyLocalUserCapture : MonoBehaviour
{
    private IEnumerator Start()
    {
        // Wait for the lobby manager and its provider.
        LobbyManager manager = null;
        while (manager == null || manager.CurrentProvider == null)
        {
            manager ??= FindFirstObjectByType<LobbyManager>();
            yield return new WaitForSeconds(0.25f);
        }

        // Wait until the provider can hand us a local user id.
        string id = "";
        while (string.IsNullOrEmpty(id))
        {
            Task<string> task = manager.CurrentProvider.GetLocalUserIdAsync();
            yield return new WaitUntil(() => task.IsCompleted);

            if (task.Status == TaskStatus.RanToCompletion)
                id = task.Result;

            if (string.IsNullOrEmpty(id)) yield return new WaitForSeconds(0.5f);
        }

        LobbyLocalUser.Id = id;

        // Best-effort display name from the current member list.
        if (manager.CurrentLobby.Members != null)
            foreach (LobbyUser m in manager.CurrentLobby.Members)
                if (m.Id == id) { LobbyLocalUser.DisplayName = m.DisplayName; break; }

        Debug.Log($"[LobbyLocalUserCapture] Captured local user '{LobbyLocalUser.DisplayName}' ({id}).");
    }
}
