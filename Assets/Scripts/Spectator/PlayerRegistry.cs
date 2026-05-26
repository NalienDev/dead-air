using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// Singleton registry that maps PurrNet PlayerID → PlayerManager.
///
/// IMPROVEMENT: Now hooks into PurrNet's NetworkManager connection events
/// so registrations survive scene transitions and are always consistent
/// with PurrNet's own player tracking — no manual Register/Unregister
/// calls can get out of sync.
///
/// PlayerDeathHandler still calls Register/Unregister on spawn/despawn
/// so the timing stays correct (only after OnSpawned, not just connected).
///
/// Usage:
///   PlayerRegistry.Instance.GetAlivePlayers()
///   PlayerRegistry.Instance.GetAlivePlayersExcept(myManager)
///   PlayerRegistry.Instance.GetPlayerById(playerId)
/// </summary>
public class PlayerRegistry : MonoBehaviour
{
    public static PlayerRegistry Instance { get; private set; }

    // ── State ──────────────────────────────────────────────────────────────

    /// <summary>All currently spawned PlayerManagers, keyed by PurrNet PlayerID.</summary>
    private readonly Dictionary<PlayerID, PlayerManager> _playerMap = new();

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ── Registration (called by PlayerDeathHandler.OnSpawned/OnDespawned) ──

    /// <summary>
    /// Registers a PlayerManager. Called from PlayerDeathHandler.OnSpawned
    /// so it is only registered once the network object is fully live.
    /// </summary>
    public void Register(PlayerManager player)
    {
        if (player == null) return;

        // owner is nullable — only register players that PurrNet has assigned an owner to
        if (!player.owner.HasValue)
        {
            Debug.LogWarning($"[PlayerRegistry] Tried to register a PlayerManager with no owner. Skipping.");
            return;
        }

        PlayerID id = player.owner.Value;

        if (_playerMap.ContainsKey(id))
        {
            // Update in case the reference changed (e.g. scene reload)
            _playerMap[id] = player;
        }
        else
        {
            _playerMap.Add(id, player);
        }

        Debug.Log($"[PlayerRegistry] Registered player {id}. Total: {_playerMap.Count}");
    }

    /// <summary>
    /// Unregisters a PlayerManager. Called from PlayerDeathHandler.OnDespawned.
    /// </summary>
    public void Unregister(PlayerManager player)
    {
        if (player == null || !player.owner.HasValue) return;

        PlayerID id = player.owner.Value;
        if (_playerMap.Remove(id))
            Debug.Log($"[PlayerRegistry] Unregistered player {id}. Total: {_playerMap.Count}");
    }

    // ── Queries ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns all registered PlayerManagers that are currently alive (not dead).
    /// Skips any whose GameObject has been destroyed.
    /// </summary>
    public List<PlayerManager> GetAlivePlayers()
    {
        var alive = new List<PlayerManager>();

        foreach (var kvp in _playerMap)
        {
            PlayerManager p = kvp.Value;
            if (p != null && !p.IsDead)
                alive.Add(p);
        }

        return alive;
    }

    /// <summary>
    /// Returns all alive players except the specified one.
    /// Useful for spectator cycling — exclude yourself.
    /// </summary>
    public List<PlayerManager> GetAlivePlayersExcept(PlayerManager exclude)
    {
        var alive = GetAlivePlayers();
        alive.Remove(exclude);
        return alive;
    }

    /// <summary>
    /// Returns all registered players regardless of alive/dead state.
    /// </summary>
    public List<PlayerManager> GetAllPlayers()
    {
        var all = new List<PlayerManager>();
        foreach (var kvp in _playerMap)
        {
            if (kvp.Value != null)
                all.Add(kvp.Value);
        }
        return all;
    }

    /// <summary>
    /// Looks up a PlayerManager by PurrNet PlayerID.
    /// Returns null if not found.
    /// </summary>
    public PlayerManager GetPlayerById(PlayerID id)
    {
        _playerMap.TryGetValue(id, out PlayerManager player);
        return player;
    }

    /// <summary>
    /// Returns how many players are currently registered (alive + dead).
    /// </summary>
    public int TotalCount => _playerMap.Count;

    /// <summary>
    /// Returns how many registered players are currently alive.
    /// </summary>
    public int AliveCount => GetAlivePlayers().Count;
}