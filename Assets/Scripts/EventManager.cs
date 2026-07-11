using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Static hub for global timer and scene-change events.
/// </summary>
public static class EventManager
{
    // Timer events & methods
    public static event UnityAction TimerStart;
    public static event UnityAction TimerStop;
    public static event UnityAction<float> TimerUpdate;
    public static void OnTimerStart() => TimerStart?.Invoke();
    public static void OnTimerStop() => TimerStop?.Invoke();
    public static void OnTimerUpdate(float value) => TimerUpdate?.Invoke(value);


    // SceneChange events & methods
    public static event UnityAction<string> SceneChange;

    public static void OnSceneChange(string sceneName)
    {
        SceneChange?.Invoke(sceneName);
    }

}