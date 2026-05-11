using UnityEngine;
using System.Collections;

public class GameOverHandler : MonoBehaviour
{
    [SerializeField] private float _delayBeforeReset = 5f;

    private void Start()
    {
        StartCoroutine(ResetSequence());
    }

    private IEnumerator ResetSequence()
    {
        yield return new WaitForSeconds(_delayBeforeReset);
        
        if (QuotaManager.Instance != null)
        {
            QuotaManager.Instance.ServerResetGame();
        }
        else
        {
            // Fallback if QuotaManager is missing
            UnityEngine.SceneManagement.SceneManager.LoadScene("GameLobby");
        }
    }
}
