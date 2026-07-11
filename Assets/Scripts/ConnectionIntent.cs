using UnityEngine;

/// <summary>
/// Carries the player's host/join intent across the load from main menu to the bootstrap scene.
/// </summary>
public class ConnectionIntent : MonoBehaviour
{
    public static ConnectionIntent Instance { get; private set; }

    public enum Intent { None, Host, Join }

    public Intent CurrentIntent { get; private set; } = Intent.None;
    public string RoomCode { get; private set; } = string.Empty;

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public static void SetHost()
    {
        EnsureExists();
        Instance.CurrentIntent = Intent.Host;
        Instance.RoomCode = string.Empty;
    }

    public static void SetJoin(string roomCode)
    {
        EnsureExists();
        Instance.CurrentIntent = Intent.Join;
        Instance.RoomCode = roomCode.Trim().ToUpper();
    }

    public static void Consume()
    {
        if (Instance != null)
            Destroy(Instance.gameObject);
    }

    private static void EnsureExists()
    {
        if (Instance != null) return;
        var go = new GameObject("ConnectionIntent");
        go.AddComponent<ConnectionIntent>();
    }
}
