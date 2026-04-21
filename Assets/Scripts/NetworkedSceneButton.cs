using UnityEngine;

public class NetworkedSceneButton : Interactable
{
    [SerializeField] private string _sceneName;

    public override InteractionType OnInteract(GameObject user)
    {
        SceneChanger.Instance.LoadSceneForEveryone(_sceneName);
        return InteractionType.PRESS;
    }

    public void setSceneName(string sceneName)
    {
        _sceneName = sceneName;
    }
}
