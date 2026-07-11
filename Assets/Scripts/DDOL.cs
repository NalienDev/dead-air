using UnityEngine;

/// <summary>
/// Singleton that survives scene loads via DontDestroyOnLoad.
/// </summary>
public class DDOL : MonoBehaviour
{
    public static DDOL Instance { get; private set; }
    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
