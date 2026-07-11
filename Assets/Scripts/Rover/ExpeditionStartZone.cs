using PurrNet;
using UnityEngine;

/// <summary>
/// The departure area (replaces the old start-expedition button). Place it in the lobby
/// on a trigger collider shaped like the boarding area: when EVERY alive player stands
/// inside, the expedition starts automatically (server-driven via
/// <see cref="RoverManager.ServerStartExpedition"/> — sound, ≥2s loading screen,
/// generation, teleport).
///
/// Edge-armed: it only fires on the transition from "not everyone inside" to "everyone
/// inside", and re-arms only after someone leaves — so players teleported back into the
/// lobby can't instantly relaunch, plus a rearm cooldown after each return.
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
        // Server decides — player positions replicate to it, so its view is authoritative.
        NetworkManager nm = NetworkManager.main;
        if (nm == null || !nm.isServer) return;

        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = _checkInterval;

        RoverManager rover = RoverManager.Instance;
        if (rover == null) return;

        // Not in the lobby phase — disarm so nothing fires mid-expedition.
        if (rover.IsStartingExpedition || AnyPlayerInsideDungeon())
        {
            _armed = false;
            return;
        }

        // Fresh off a return — give players a moment before the zone is live again.
        if (Time.time - rover.LastReturnTime < _rearmSeconds)
        {
            _armed = false;
            return;
        }

        bool allInside = AreAllAlivePlayersInside(out int aliveCount) && aliveCount > 0;

        if (!allInside)
        {
            _armed = true; // saw a "not everyone inside" frame — ready to trigger
            return;
        }

        if (!_armed) return;
        _armed = false;

        Debug.Log($"[ExpeditionStartZone] All {aliveCount} player(s) aboard — starting expedition.");
        rover.ServerStartExpedition();
    }
}
