using UnityEngine;
using UnityEngine.SceneManagement;

public class SceneChanger : MonoBehaviour
{

    public void LoadScene(string sceneName)
    {
        SceneManager.LoadScene(sceneName); // Loads the scene by name
        Time.timeScale = 1.0f;
        PauseMenu.GameIsPaused = false;
        EventManager.OnSceneChange(sceneName);
    }

    public void LoadSceneByIndex(int buildIndex)
    {
        SceneManager.LoadScene(buildIndex); // Loads the scene by its build index
        Time.timeScale = 1.0f;
        PauseMenu.GameIsPaused = false;
        EventManager.OnSceneChange(SceneManager.GetSceneByBuildIndex(buildIndex).name);
    }

    public void LoadNextScene()
    {
        if (SceneManager.GetActiveScene().buildIndex != SceneManager.sceneCountInBuildSettings - 1)
        {
            LoadSceneByIndex(SceneManager.GetActiveScene().buildIndex + 1);
        }
    }

    public void ReloadScene()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
        Time.timeScale = 1.0f;
        Destroy(gameObject, 3);
    }
}