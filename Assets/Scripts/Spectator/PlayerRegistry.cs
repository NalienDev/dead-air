using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lightweight singleton that every PlayerManager registers itself with on spawn.
/// Gives SpectatorController a reliable, always-up-to-date list of living players
/// without any scene searches or coupling to PurrNet internals.
/// </summary>
public class PlayerRegistry : MonoBehaviour
{
    public static PlayerRegistry Instance { get; private set; }

    private readonly List<PlayerManager> _allPlayers = new();

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    // ── Registration ───────────────────────────────────────────────────────

    public void Register(PlayerManager player)
    {
        if (!_allPlayers.Contains(player))
            _allPlayers.Add(player);
    }

    public void Unregister(PlayerManager player)
    {
        _allPlayers.Remove(player);
    }

    // ── Queries ────────────────────────────────────────────────────────────

    /// <summary>Returns all players that are currently alive (not dead).</summary>
    public List<PlayerManager> GetAlivePlayers()
    {
        var alive = new List<PlayerManager>();
        foreach (var p in _allPlayers)
        {
            if (p != null && !p.IsDead)
                alive.Add(p);
        }
        return alive;
    }

    /// <summary>Returns all players except the given one.</summary>
    public List<PlayerManager> GetAlivePlayersExcept(PlayerManager exclude)
    {
        var alive = GetAlivePlayers();
        alive.Remove(exclude);
        return alive;
    }
}
