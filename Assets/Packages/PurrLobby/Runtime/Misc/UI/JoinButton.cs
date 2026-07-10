using TMPro;
using UnityEngine;
using UnityEngine.Events;

namespace PurrLobby
{
    public class JoinButton : MonoBehaviour
    {
        [SerializeField] private TMP_InputField roomIdInput;
        [SerializeField] private LobbyManager lobbyManager;
        [SerializeField] private UnityEvent onStartJoin;
        
        public void JoinRoom()
        {
            string input = roomIdInput.text?.Trim();
            if (string.IsNullOrEmpty(input))
            {
                Debug.LogWarning($"Can't start join, room ID is empty.");
                return;
            }

            onStartJoin?.Invoke();

            // Short inputs are 5-char join codes; long ones are raw lobby ids
            // (kept for backwards compatibility / pasting a full Steam id).
            if (input.Length <= 8)
                lobbyManager.JoinLobbyByCode(input);
            else
                lobbyManager.JoinLobby(input);
        }
    }
}
