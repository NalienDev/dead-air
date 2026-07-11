using PurrNet;
using UnityEngine;

/// <summary>
/// Departure area that starts the expedition once every alive player stands inside it.
/// </summary>
public class ExpeditionStartZone : PlayerGatherZone
{
    [Tooltip("How often (seconds) the zone checks player positions.")]
    [SerializeField] private float _checkInterval = 0.25f;
    [Tooltip("Seconds after returning from an expedition before the zone can fire again.")]
    [SerializeField] private float _rearmSeconds = 5f;

    private float _timer;
    private bool _armed;

    private void Update()
    {
        // The server decides, since player positions replicate to it.
        NetworkManager nm = NetworkManager.main;
        if (nm == null || !nm.isServer) return;

        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = _checkInterval;

        RoverManager rover = RoverManager.Instance;
        if (rover == null) return;

        // Not in the lobby phase, so disarm to avoid firing mid-expedition.
        if (rover.IsStartingExpedition || AnyPlayerInsideDungeon())
        {
            _armed = false;
            return;
        }

        // Fresh off a return, so give players a moment before the zone is live again.
        if (Time.time - rover.LastReturnTime < _rearmSeconds)
        {
            _armed = false;
            return;
        }

        bool allInside = AreAllAlivePlayersInside(out int aliveCount) && aliveCount > 0;

        if (!allInside)
        {
            _armed = true; // saw a not-everyone-inside frame, ready to trigger
            return;
        }

        if (!_armed) return;
        _armed = false;

        Debug.Log($"[ExpeditionStartZone] All {aliveCount} player(s) aboard, starting expedition.");
        rover.ServerStartExpedition();
    }
}
