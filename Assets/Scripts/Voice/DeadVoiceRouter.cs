using Dissonance;
using PurrNet;
using UnityEngine;

/// <summary>
/// Routes the local player's voice so the living use proximity chat and the dead share a global room.
/// </summary>
[RequireComponent(typeof(PlayerManager))]
public class DeadVoiceRouter : NetworkBehaviour
{
    // Global room the dead team talks and listens in.
    public const string DeadRoomName = "Dead";

    [Tooltip("How often the voice routing is reconciled with dead/alive state.")]
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

        // Dead players never broadcast into proximity chat.
        if (_proximityBroadcast.IsMuted != selfDead)
            _proximityBroadcast.IsMuted = selfDead;

        // Feed the Dead room only while someone is dead to listen.
        if (_deadBroadcast.enabled != anyDead)
            _deadBroadcast.enabled = anyDead;

        // Dead players listen to the Dead room.
        if (_deadReceipt.enabled != selfDead)
            _deadReceipt.enabled = selfDead;

        // Of the alive voices in the Dead room, keep only the spectated one.
        ReconcileSpectatorMutes(selfDead);
    }

    // While dead, locally mute every alive player except the one being watched; while
    // alive, clear those mutes so revival restores normal hearing.
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
            Debug.LogWarning("[DeadVoiceRouter] No VoiceProximityBroadcastTrigger on the DissonanceSetup object.");
            return false;
        }

        // Reuse any Dead-room triggers already on the comms object; the player is recreated
        // each scene load, so adding new ones would pile up orphaned trigger pairs.
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
