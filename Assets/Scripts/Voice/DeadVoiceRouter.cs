using Dissonance;
using PurrNet;
using UnityEngine;

/// <summary>
/// Routes the local player's voice between the "alive" and "dead" teams.
///
/// Alive voice is the scene's proximity chat (the VoiceProximity* triggers on the
/// DissonanceSetup object, room "Proximity", range-limited). This component adds a
/// second, GLOBAL room — <see cref="DeadRoomName"/> — and drives three switches from
/// the already-synced dead/alive state:
///
/// <list type="bullet">
/// <item><b>Proximity broadcast muted while dead</b> — a dead player's voice never
/// reaches the living, no matter how close.</item>
/// <item><b>Everyone broadcasts into the Dead room while anyone is dead</b> — but only
/// dead players listen to it, so the living hear nothing extra. (Idle when nobody is
/// dead, so it costs no bandwidth in normal play.)</item>
/// <item><b>Dead players listen to the Dead room</b> — but alive voices in it are
/// locally muted EXCEPT the player currently being spectated. So a dead player hears
/// all other dead players (global spectator chat) plus exactly the one alive player
/// they're watching.</item>
/// </list>
///
/// Result: alive ↔ alive is proximity as before; alive never hear dead; dead hear the
/// other dead from anywhere plus only the alive player they're spectating.
///
/// Sits on the player prefab next to PlayerManager; only the local owner's copy runs.
/// The triggers are created at runtime on the DissonanceSetup object — nothing to wire.
/// </summary>
[RequireComponent(typeof(PlayerManager))]
public class DeadVoiceRouter : NetworkBehaviour
{
    /// <summary>Global room the dead team talks and listens in.</summary>
    public const string DeadRoomName = "Dead";

    [Tooltip("How often (seconds) the voice routing is reconciled with dead/alive state.")]
    [SerializeField] private float _refreshInterval = 0.4f;

    private PlayerManager _self;
    private SpectatorController _spectator;
    private DissonanceComms _comms;

    private VoiceProximityBroadcastTrigger _proximityBroadcast; // scene's alive chat
    private VoiceBroadcastTrigger _deadBroadcast;               // runtime, global Dead room
    private VoiceReceiptTrigger _deadReceipt;                   // runtime, global Dead room

    private float _timer;

    private void Awake() => _self = GetComponent<PlayerManager>();

    protected override void OnSpawned(bool asServer)
    {
        // Only the local player routes the local mic/ears.
        if (!isOwner) enabled = false;
    }

    private void Update()
    {
        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _timer = _refreshInterval;

        if (!EnsureTriggers()) return;

        bool selfDead = _self.IsDead;
        bool anyDead = AnyPlayerDead();

        // 1. Dead lips: never into proximity chat.
        if (_proximityBroadcast.IsMuted != selfDead)
            _proximityBroadcast.IsMuted = selfDead;

        // 2. Feed the Dead room (alive players included, so the dead can hear them
        //    from anywhere) — but only while there is actually someone dead listening.
        if (_deadBroadcast.enabled != anyDead)
            _deadBroadcast.enabled = anyDead;

        // 3. Dead ears: hear the Dead room (all dead + all alive, global).
        if (_deadReceipt.enabled != selfDead)
            _deadReceipt.enabled = selfDead;

        // 4. Of the alive voices in the Dead room, keep only the spectated one.
        ReconcileSpectatorMutes(selfDead);
    }

    // While dead: locally mute every ALIVE player except the one being watched
    // (dead players are never muted — spectator chat is global). While alive: clear
    // every local mute we may have applied, so revival restores normal hearing.
    private void ReconcileSpectatorMutes(bool selfDead)
    {
        PlayerManager watching = null;
        if (selfDead)
        {
            if (_spectator == null) _spectator = GetComponent<SpectatorController>();
            if (_spectator != null) watching = _spectator.CurrentlyWatching;
        }

        foreach (PlayerIdentity id in FindObjectsByType<PlayerIdentity>(FindObjectsSortMode.None))
        {
            string voice = id.voiceName.value;
            // Skip unpublished voices and our own (muting the local player throws).
            if (string.IsNullOrEmpty(voice) || voice == _comms.LocalPlayerName) continue;

            VoicePlayerState state = _comms.FindPlayer(voice);
            if (state == null) continue;

            PlayerManager pm = id.GetComponent<PlayerManager>();
            bool theyAreAlive = pm != null && !pm.IsDead;

            bool shouldMute = selfDead && theyAreAlive && pm != watching;
            if (state.IsLocallyMuted != shouldMute)
                state.IsLocallyMuted = shouldMute;
        }
    }

    // Creates the runtime triggers on the DissonanceSetup object the first time the
    // comms system is available. Returns false until then.
    private bool EnsureTriggers()
    {
        if (_deadBroadcast != null && _deadReceipt != null && _proximityBroadcast != null)
            return true;

        if (_comms == null) _comms = FindFirstObjectByType<DissonanceComms>();
        if (_comms == null) return false;
        DissonanceComms comms = _comms;

        _proximityBroadcast = comms.GetComponent<VoiceProximityBroadcastTrigger>();
        if (_proximityBroadcast == null)
        {
            Debug.LogWarning("[DeadVoiceRouter] No VoiceProximityBroadcastTrigger on the " +
                             "DissonanceSetup object — alive chat setup has changed?");
            return false;
        }

        // REUSE any Dead-room triggers already on the comms object. The player object
        // is recreated on every scene load, so blindly AddComponent-ing here used to
        // pile up one orphaned trigger pair per City reload — each stuck in whatever
        // state the previous round left it (possibly broadcasting), which degraded
        // voice chat over repeated GameOver→City cycles.
        // (VoiceProximityBroadcastTrigger is its own type, not a VoiceBroadcastTrigger
        // subclass, so the alive-chat trigger can never show up in this loop.)
        foreach (VoiceBroadcastTrigger t in comms.GetComponents<VoiceBroadcastTrigger>())
        {
            if (t.ChannelType == CommTriggerTarget.Room && t.RoomName == DeadRoomName)
            {
                _deadBroadcast = t;
                break;
            }
        }

        if (_deadBroadcast == null)
        {
            _deadBroadcast = comms.gameObject.AddComponent<VoiceBroadcastTrigger>();
            _deadBroadcast.ChannelType = CommTriggerTarget.Room;
            _deadBroadcast.RoomName = DeadRoomName;
        }

        _deadBroadcast.Mode = _proximityBroadcast.Mode; // same activation as normal chat
        _deadBroadcast.enabled = false;

        foreach (VoiceReceiptTrigger t in comms.GetComponents<VoiceReceiptTrigger>())
        {
            if (t.RoomName == DeadRoomName)
            {
                _deadReceipt = t;
                break;
            }
        }

        if (_deadReceipt == null)
        {
            _deadReceipt = comms.gameObject.AddComponent<VoiceReceiptTrigger>();
            _deadReceipt.RoomName = DeadRoomName;
        }

        _deadReceipt.enabled = false;

        return true;
    }

    private static bool AnyPlayerDead()
    {
        foreach (PlayerManager pm in FindObjectsByType<PlayerManager>(FindObjectsSortMode.None))
            if (pm != null && pm.IsDead) return true;
        return false;
    }
}
