using UnityEngine;

namespace PurrLobby
{
    public class LobbyView : View
    {
        [SerializeField] private CodeButton codeButton;
        [SerializeField] private LobbyManager lobbyManager;

        public override void OnShow()
        {
            // Prefer the short 5-char join code; fall back to the raw lobby id for
            // providers that don't generate one.
            var lobby = lobbyManager.CurrentLobby;
            string code = lobby.LobbyId;
            if (lobby.Properties != null &&
                lobby.Properties.TryGetValue("ShortCode", out var shortCode) &&
                !string.IsNullOrEmpty(shortCode))
            {
                code = shortCode;
            }

            codeButton.Init(code);
        }
    }
}
