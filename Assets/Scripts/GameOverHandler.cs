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
        
        if (LoadingScreenManager.Instance != null)
            LoadingScreenManager.Instance.ShowLoadingScreen();

        if (QuotaManager.Instance != null)
            QuotaManager.Instance.ServerResetGame();

        // Game Over is a separate scene, so we MUST load back into the main game scene
        if (SceneChanger.Instance != null)
            SceneChanger.Instance.LoadSceneForEveryone("City"); // Ensure this matches your main scene name
        else
            UnityEngine.SceneManagement.SceneManager.LoadScene("City");
    }
}
