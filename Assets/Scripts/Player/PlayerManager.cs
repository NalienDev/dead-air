using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// Manages synced player state: health, oxygen, dungeon flag, and voice relay.
/// IsDead is a convenience property that reads from PlayerDeathHandler.
/// </summary>
public class PlayerManager : NetworkIdentity, ISoundListener
{
    // ── Synced state ───────────────────────────────────────────────────────

    public SyncVar<int> currentHealth = new(100);
    public SyncVar<int> maxHealth = new(100);
    public SyncVar<int> maxOxygen = new(360);
    public SyncVar<int> currentOxygen = new(360);

    /// <summary>When true, oxygen never drains (from the infinite-oxygen upgrade).</summary>
    public SyncVar<bool> hasInfiniteOxygen = new(false);

    /// <summary>Oxygen-station charges. Reset to max on expedition completion;
    /// max grows with the +charge upgrade.</summary>
    public SyncVar<int> maxOxygenCharges = new(3);
    public SyncVar<int> currentOxygenCharges = new(3);

    /// <summary>True while this player is inside a dungeon.</summary>
    public SyncVar<bool> isInsideDungeon = new(false); 

    // ── Local accessor ─────────────────────────────────────────────────────

    public static PlayerManager Local { get; private set; }

    // ── Convenience ────────────────────────────────────────────────────────

    /// <summary>
    /// True when this player is dead. Reads from PlayerDeathHandler.isDead so
    /// there is a single source of truth — no duplicated SyncVar.
    /// </summary>
    public bool IsDead
    {
        get
        {
            if (_deathHandler == null) _deathHandler = GetComponent<PlayerDeathHandler>();
            return _deathHandler != null && _deathHandler.isDead.value;
        }
    }

    // ── Suffocation (0 oxygen) ───────────────────────────────────────────────

    [Header("Suffocation (at 0 oxygen)")]
    [Tooltip("Health lost per second while out of oxygen.")]
    [SerializeField] private int _suffocationDamagePerSecond = 10;
    [Tooltip("Looping gasp/alarm played for the local player while suffocating.")]
    [SerializeField] private AudioClip _suffocationLoop;

    private AudioSource _suffocationSource;

    // ── Private state ──────────────────────────────────────────────────────

    private float _oxygenTimer = 0f;
    private PlayerDeathHandler _deathHandler;

    // Server-side view of the player's current mic loudness (0..1). Updated by
    // ServerReportVoiceLoudness and read by the blind Conductor. Decays to 0 on its
    // own once the owner stops sending reports, so we never need an Update here
    // (this component is disabled on the server's copy of remote clients).
    private float _lastVoiceLoudness;
    private float _lastVoiceTime = -999f;
    private const float VoiceStaleSeconds = 0.3f;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    protected override void OnSpawned(bool asServer)
    {
        if (isOwner)
        {
            Local = this;

            // Hide the loading screen once the local player is fully spawned in Main
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Main")
            {
                if (LoadingScreenManager.Instance != null)
                    LoadingScreenManager.Instance.HideLoadingScreen();
            }
        }
    }

    // ── Update ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!isOwner) return;

        // Keep streaming any captured voice to the server even while dead, so an
        // in-flight utterance finishes sending.
        PumpVoiceStreaming();

        if (IsDead)
        {
            StopSuffocationLoop(); // no gasping once dead
            return;
        }

        _oxygenTimer += Time.deltaTime;
        if (_oxygenTimer >= 1f)
        {
            _oxygenTimer = 0f;
            ServerOxygenTick(); // drains, or suffocation-damages at 0
        }

        // Loop the suffocation sound locally (per-frame so it starts/stops promptly).
        if (IsSuffocatingNow()) StartSuffocationLoop();
        else StopSuffocationLoop();

        // Debug bindings — remove before shipping (F is the flashlight now)
        if (Input.GetKeyDown(KeyCode.X)) GainOxygen(10);
    }

    /// <summary>
    /// True while the player is out of air and unprotected — i.e. actively suffocating.
    /// Shared shape between the local audio and the server-side damage so they agree.
    /// </summary>
    private bool IsSuffocatingNow()
    {
        return !IsDead
            && currentOxygen.value <= 0
            && !hasInfiniteOxygen.value
            && !FogClearingZone.ContainsPoint(transform.position);
    }

    // ── Suffocation audio (local player only) ────────────────────────────────

    private void StartSuffocationLoop()
    {
        if (_suffocationLoop == null) return;

        if (_suffocationSource == null)
        {
            _suffocationSource = gameObject.AddComponent<AudioSource>();
            _suffocationSource.playOnAwake = false;
            _suffocationSource.loop = true;
            _suffocationSource.spatialBlend = 0f; // your own gasping — 2D
            _suffocationSource.clip = _suffocationLoop;
        }

        if (!_suffocationSource.isPlaying) _suffocationSource.Play();
    }

    private void StopSuffocationLoop()
    {
        if (_suffocationSource != null && _suffocationSource.isPlaying)
            _suffocationSource.Stop();
    }

    // ── Public getters ─────────────────────────────────────────────────────

    public int GetCurrentHealth() => currentHealth.value;
    public int GetMaxHealth() => maxHealth.value;
    public int GetMaxOxygen() => maxOxygen.value;
    public int GetCurrentOxygen() => currentOxygen.value;
    public bool IsInsideDungeon() => isInsideDungeon.value;

    // ── Dungeon state ──────────────────────────────────────────────────────

    public void SetInsideDungeon(bool value) => ServerSetInsideDungeon(value);

    [ServerRpc(requireOwnership: false)]
    private void ServerSetInsideDungeon(bool value) => isInsideDungeon.value = value;

    // ── Teleport all players (server-authoritative) ────────────────────────

    /// <summary>
    /// Any client can call this. The server finds every PlayerManager and
    /// tells each one to warp to <paramref name="position"/> via ObserversRpc.
    /// </summary>
    [ServerRpc(requireOwnership: false)]
    public void RequestTeleportAllPlayers(Vector3 position, Quaternion rotation)
    {
        PlayerManager[] all = FindObjectsByType<PlayerManager>(FindObjectsSortMode.None);
        foreach (PlayerManager pm in all)
            pm.TeleportToPosition(position, rotation);
    }

    /// <summary>Runs on every client (and the server) for this specific player object.</summary>
    [ObserversRpc(runLocally: true)]
    public void TeleportToPosition(Vector3 position, Quaternion rotation)
    {
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        if (cc != null) cc.enabled = true;
    }

    // ── Server RPCs ────────────────────────────────────────────────────────

    /// <summary>
    /// Owner → server, once per second. Drains 1 oxygen; once oxygen hits 0 the player
    /// suffocates instead of dying outright — losing <see cref="_suffocationDamagePerSecond"/>
    /// health each tick until they get air back (death then comes from health reaching 0).
    /// Breathing air (silence zone) or infinite oxygen protects from both.
    /// </summary>
    [ServerRpc]
    private void ServerOxygenTick()
    {
        if (IsDead) return;

        // Silence zone / infinite oxygen → breathing, so neither drain nor suffocate.
        if (hasInfiniteOxygen.value || FogClearingZone.ContainsPoint(transform.position))
            return;

        if (currentOxygen.value > 0)
            currentOxygen.value = Mathf.Clamp(currentOxygen.value - 1, 0, maxOxygen.value);

        if (currentOxygen.value <= 0)
        {
            currentHealth.value = Mathf.Max(currentHealth.value - _suffocationDamagePerSecond, 0);
            CheckServerDeath();
        }
    }

    /// <summary>External oxygen reduction (hazards etc.). Clamped; no longer lethal on
    /// its own — running out just starts suffocation on the next oxygen tick.</summary>
    [ServerRpc]
    public void DrainOxygen(int amount)
    {
        if (IsDead) return;
        if (hasInfiniteOxygen.value) return;
        // Inside a silence zone (FogClearingZone) the air is breathable — no drain.
        if (FogClearingZone.ContainsPoint(transform.position)) return;
        currentOxygen.value = Mathf.Clamp(currentOxygen.value - amount, 0, maxOxygen.value);
    }

    // ── Oxygen upgrades (server-side, called by PlayerUpgrades) ──────────────

    /// <summary>Raises max oxygen by a fraction of its current value and refills to full.</summary>
    public void ServerAddMaxOxygenPercent(float pct)
    {
        if (!isServer) return;
        int add = Mathf.Max(1, Mathf.RoundToInt(maxOxygen.value * pct));
        maxOxygen.value += add;
        currentOxygen.value = maxOxygen.value; // taking the upgrade maxes you out
    }

    /// <summary>Enables/disables infinite oxygen and refills to full when enabling.</summary>
    public void ServerSetInfiniteOxygen(bool value)
    {
        if (!isServer) return;
        hasInfiniteOxygen.value = value;
        if (value) currentOxygen.value = maxOxygen.value;
    }

    // ── Oxygen station (server-side, called by OxygenStation) ────────────────

    /// <summary>
    /// Spends one oxygen-station charge and refills oxygen to full. Returns false when
    /// out of charges, dead, or already at full oxygen (no charge wasted).
    /// </summary>
    public bool ServerTryUseOxygenCharge()
    {
        if (!isServer || IsDead) return false;
        if (currentOxygenCharges.value <= 0) return false;
        if (currentOxygen.value >= maxOxygen.value) return false;

        currentOxygenCharges.value--;
        currentOxygen.value = maxOxygen.value;
        return true;
    }

    /// <summary>Grants extra station charges (the +charge upgrade): raises the max and
    /// hands the new charges over immediately.</summary>
    public void ServerAddOxygenCharges(int amount)
    {
        if (!isServer || amount == 0) return;
        maxOxygenCharges.value += amount;
        currentOxygenCharges.value += amount;
    }

    // ── Expedition reset (server-side, called by RoverManager on return) ─────

    /// <summary>
    /// Completing an expedition wipes the slate: dead players are revived, and
    /// health / oxygen / station charges all reset to max.
    /// </summary>
    public void ServerResetForNewExpedition()
    {
        if (!isServer) return;

        if (_deathHandler == null) _deathHandler = GetComponent<PlayerDeathHandler>();
        if (_deathHandler != null && _deathHandler.isDead.value)
            _deathHandler.Revive(maxHealth.value, maxOxygen.value);

        currentHealth.value = maxHealth.value;
        currentOxygen.value = maxOxygen.value;
        currentOxygenCharges.value = maxOxygenCharges.value;
    }

    [ServerRpc]
    public void Damage(int damage)
    {
        if (IsDead) return;
        currentHealth.value = Mathf.Max(currentHealth.value - damage, 0);
        CheckServerDeath();
    }

    [ServerRpc]
    public void GainOxygen(int amount)
    {
        if (IsDead) return;
        currentOxygen.value = Mathf.Clamp(currentOxygen.value + amount, 0, maxOxygen.value);
    }

    [ServerRpc]
    public void Heal(int amount)
    {
        if (IsDead) return;
        currentHealth.value = Mathf.Clamp(currentHealth.value + amount, 0, maxHealth.value);
    }

    /// <summary>
    /// Server-authoritative death check, run after any server-side change to
    /// health/oxygen. This lives here — not in PlayerDeathHandler.Update —
    /// because NetworkOwnershipToggle disables PlayerDeathHandler (and this
    /// component) on the server's copy of a remote client, so their Update never
    /// runs. These ServerRpc bodies, however, always execute on the server, and
    /// calling a method on a disabled component is still valid.
    /// </summary>
    private void CheckServerDeath()
    {
        if (_deathHandler == null) _deathHandler = GetComponent<PlayerDeathHandler>();
        if (_deathHandler != null) _deathHandler.ServerCheckDeath();
    }

    // ── Voice recording relay ──────────────────────────────────────────────
    //
    // A whole utterance can be hundreds of KB to several MB of raw float samples.
    // Sent as one RPC it fragments and head-of-line-blocks the reliable channel that
    // also carries movement sync, which makes remote players stutter/teleport. So we
    // compress to 16-bit PCM (half the bytes) and stream it to the server in small
    // per-frame chunks, and the server reassembles it.

    // ≈0.1s of 48 kHz audio per frame (~9.6 KB as PCM16). Far more than realtime
    // speech needs, but small enough that it never floods the channel in one frame.
    private const int VoiceSamplesPerFrame = 4800;
    // Backpressure: if utterances pile up faster than they send, drop the oldest.
    private const int VoiceMaxQueuedClips = 8;

    private sealed class PendingVoiceClip
    {
        public float[] Samples;
        public int SampleRate;
        public int Channels;
        public int Id;
    }

    // Owner-side outgoing queue. Enqueue may be called from the audio capture thread,
    // so it's guarded; everything else runs on the main thread in Update.
    private readonly Queue<PendingVoiceClip> _voiceOutQueue = new();
    private readonly object _voiceOutLock = new();
    private PendingVoiceClip _voiceSending;   // clip currently being streamed
    private int _voiceSendOffset;             // samples of _voiceSending already sent
    private int _voiceClipCounter;            // client-side id generator

    // Server-side reassembly state for the in-flight clip from this player.
    private int _rxClipId = -1;
    private int _rxSampleRate;
    private int _rxChannels;
    private List<float> _rxSamples;

    /// <summary>Owner → server: queue a finished utterance to be streamed up.</summary>
    public void SubmitVoiceClipToServer(float[] samples, int sampleRate, int channels)
    {
        if (samples == null || samples.Length == 0) return;

        var pending = new PendingVoiceClip
        {
            Samples = samples,
            SampleRate = sampleRate,
            Channels = channels
        };

        lock (_voiceOutLock)
        {
            _voiceOutQueue.Enqueue(pending);
            while (_voiceOutQueue.Count > VoiceMaxQueuedClips)
            {
                _voiceOutQueue.Dequeue();
                Debug.LogWarning("[PlayerManager] Voice send backlog — dropping oldest utterance.");
            }
        }
    }

    // Owner, every frame: stream at most one bounded chunk of the current utterance.
    private void PumpVoiceStreaming()
    {
        if (_voiceSending == null)
        {
            lock (_voiceOutLock)
            {
                if (_voiceOutQueue.Count == 0) return;
                _voiceSending = _voiceOutQueue.Dequeue();
            }

            _voiceSending.Id = ++_voiceClipCounter;
            _voiceSendOffset = 0;
            ServerVoiceBegin(_voiceSending.Id, _voiceSending.SampleRate,
                             _voiceSending.Channels, _voiceSending.Samples.Length);
        }

        int remaining = _voiceSending.Samples.Length - _voiceSendOffset;
        int count = Mathf.Min(VoiceSamplesPerFrame, remaining);
        ServerVoiceChunk(_voiceSending.Id, EncodePcm16(_voiceSending.Samples, _voiceSendOffset, count));
        _voiceSendOffset += count;

        if (_voiceSendOffset >= _voiceSending.Samples.Length)
        {
            ServerVoiceEnd(_voiceSending.Id);
            _voiceSending = null;
        }
    }

    private static byte[] EncodePcm16(float[] samples, int offset, int count)
    {
        byte[] bytes = new byte[count * 2];
        for (int i = 0; i < count; i++)
        {
            float f = Mathf.Clamp(samples[offset + i], -1f, 1f);
            short s = (short)Mathf.RoundToInt(f * 32767f);
            bytes[i * 2] = (byte)(s & 0xFF);
            bytes[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return bytes;
    }

    [ServerRpc]
    private void ServerVoiceBegin(int clipId, int sampleRate, int channels, int totalSamples)
    {
        _rxClipId = clipId;
        _rxSampleRate = sampleRate;
        _rxChannels = channels;
        _rxSamples = new List<float>(Mathf.Max(0, totalSamples));
    }

    [ServerRpc]
    private void ServerVoiceChunk(int clipId, byte[] pcm16)
    {
        if (_rxSamples == null || clipId != _rxClipId) return; // stray / out of order
        for (int i = 0; i + 1 < pcm16.Length; i += 2)
        {
            short s = (short)(pcm16[i] | (pcm16[i + 1] << 8));
            _rxSamples.Add(s / 32768f);
        }
    }

    [ServerRpc]
    private void ServerVoiceEnd(int clipId)
    {
        if (_rxSamples == null || clipId != _rxClipId) return;

        List<float> assembled = _rxSamples;
        _rxSamples = null;

        if (VoiceRecordingStore.Instance == null)
        {
            Debug.LogWarning("[PlayerManager] VoiceRecordingStore not found in scene.");
            return;
        }

        int channels = Mathf.Max(1, _rxChannels);
        float[] samples = assembled.ToArray();

        AudioClip clip = AudioClip.Create(
            $"voice_{owner}", samples.Length / channels, channels, _rxSampleRate, stream: false);
        clip.SetData(samples, offsetSamples: 0);

        string playerId = owner?.ToString() ?? "unknown";
        VoiceRecordingStore.Instance.Enqueue(new CapturedVoiceClip(playerId, clip, Time.time));
    }

    public void OnHearSound(Vector3 origin)
    {
        Debug.Log($"[{owner}] Ouvi um som em {origin}");
    }

    // ── Noise the Conductor can hear ────────────────────────────────────────

    /// <summary>
    /// Server-side estimate of how loud this player currently is (0..1). Reads back
    /// to 0 automatically once the owner stops streaming voice reports. Used by
    /// TheConductorAI both to pick a target and to detect a sustained scream.
    /// </summary>
    public float CurrentVoiceLoudness =>
        (Time.time - _lastVoiceTime <= VoiceStaleSeconds) ? _lastVoiceLoudness : 0f;

    /// <summary>Owner → server: the local mic's current normalised loudness.</summary>
    public void ReportVoiceLoudness(float loudness01) => ServerReportVoiceLoudness(loudness01);

    [ServerRpc]
    private void ServerReportVoiceLoudness(float loudness01)
    {
        _lastVoiceLoudness = Mathf.Clamp01(loudness01);
        _lastVoiceTime = Time.time;

        // Voices are the loudest thing in the dungeon — feed the Conductor's ears.
        NoiseEvents.Report(transform.position, _lastVoiceLoudness);
    }

    private PlayerUpgrades _upgrades;

    /// <summary>Owner → server: a momentary noise at the player's position (footstep, sprint).
    /// The quiet-footsteps upgrade scales it down here, so every noise source benefits.</summary>
    public void ReportNoise(float loudness01)
    {
        if (_upgrades == null) _upgrades = GetComponent<PlayerUpgrades>();
        if (_upgrades != null) loudness01 *= _upgrades.FootstepNoiseMultiplier;
        ServerReportNoise(loudness01);
    }

    [ServerRpc]
    private void ServerReportNoise(float loudness01)
    {
        NoiseEvents.Report(transform.position, Mathf.Clamp01(loudness01));
    }
}