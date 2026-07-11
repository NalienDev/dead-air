using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// Manages synced player state: health, oxygen, dungeon flag, and voice relay to the server.
/// </summary>
public class PlayerManager : NetworkIdentity, ISoundListener
{
    public SyncVar<int> currentHealth = new(100);
    public SyncVar<int> maxHealth = new(100);
    public SyncVar<int> maxOxygen = new(360);
    public SyncVar<int> currentOxygen = new(360);

    // When true, oxygen never drains (from the infinite-oxygen upgrade).
    public SyncVar<bool> hasInfiniteOxygen = new(false);

    // Oxygen-station charges; reset to max on expedition completion, max grows with the upgrade.
    public SyncVar<int> maxOxygenCharges = new(3);
    public SyncVar<int> currentOxygenCharges = new(3);

    public SyncVar<bool> isInsideDungeon = new(false);

    public static PlayerManager Local { get; private set; }

    // True when dead; reads PlayerDeathHandler so there's a single source of truth.
    public bool IsDead
    {
        get
        {
            if (_deathHandler == null) _deathHandler = GetComponent<PlayerDeathHandler>();
            return _deathHandler != null && _deathHandler.isDead.value;
        }
    }

    [Header("Suffocation")]
    [Tooltip("Health lost per second while out of oxygen.")]
    [SerializeField] private int _suffocationDamagePerSecond = 10;
    [Tooltip("Looping gasp played for the local player while suffocating.")]
    [SerializeField] private AudioClip _suffocationLoop;

    private AudioSource _suffocationSource;

    private float _oxygenTimer = 0f;
    private PlayerDeathHandler _deathHandler;

    // Server-side view of the player's mic loudness, read by the Conductor. Decays to 0
    // on its own once the owner stops sending reports.
    private float _lastVoiceLoudness;
    private float _lastVoiceTime = -999f;
    private const float VoiceStaleSeconds = 0.3f;

    protected override void OnSpawned(bool asServer)
    {
        if (isOwner)
        {
            Local = this;

            // Hide the loading screen once the local player is fully spawned in Main.
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "Main")
            {
                if (LoadingScreenManager.Instance != null)
                    LoadingScreenManager.Instance.HideLoadingScreen();
            }
        }
    }

    private void Update()
    {
        if (!isOwner) return;

        // Keep streaming captured voice even while dead so an in-flight utterance finishes.
        PumpVoiceStreaming();

        if (IsDead)
        {
            StopSuffocationLoop();
            return;
        }

        _oxygenTimer += Time.deltaTime;
        if (_oxygenTimer >= 1f)
        {
            _oxygenTimer = 0f;
            ServerOxygenTick();
        }

        if (IsSuffocatingNow()) StartSuffocationLoop();
        else StopSuffocationLoop();

        // Debug: X grants oxygen.
        if (Input.GetKeyDown(KeyCode.X)) GainOxygen(10);
    }

    // True while the player is out of air and unprotected; shared by the audio and damage checks.
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
            _suffocationSource.spatialBlend = 0f; // the player's own gasping, so 2D
            _suffocationSource.clip = _suffocationLoop;
        }

        if (!_suffocationSource.isPlaying) _suffocationSource.Play();
    }

    private void StopSuffocationLoop()
    {
        if (_suffocationSource != null && _suffocationSource.isPlaying)
            _suffocationSource.Stop();
    }

    public int GetCurrentHealth() => currentHealth.value;
    public int GetMaxHealth() => maxHealth.value;
    public int GetMaxOxygen() => maxOxygen.value;
    public int GetCurrentOxygen() => currentOxygen.value;
    public bool IsInsideDungeon() => isInsideDungeon.value;

    public void SetInsideDungeon(bool value) => ServerSetInsideDungeon(value);

    [ServerRpc(requireOwnership: false)]
    private void ServerSetInsideDungeon(bool value) => isInsideDungeon.value = value;

    // Any client can call this; the server warps every player to the position.
    [ServerRpc(requireOwnership: false)]
    public void RequestTeleportAllPlayers(Vector3 position, Quaternion rotation)
    {
        PlayerManager[] all = FindObjectsByType<PlayerManager>(FindObjectsSortMode.None);
        foreach (PlayerManager pm in all)
            pm.TeleportToPosition(position, rotation);
    }

    [ObserversRpc(runLocally: true)]
    public void TeleportToPosition(Vector3 position, Quaternion rotation)
    {
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        if (cc != null) cc.enabled = true;
    }

    // Once per second: drains oxygen, then suffocation-damages health while at 0.
    // A silence zone or infinite oxygen protects from both.
    [ServerRpc]
    private void ServerOxygenTick()
    {
        if (IsDead) return;

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

    // External oxygen reduction (hazards); running out just starts suffocation next tick.
    [ServerRpc]
    public void DrainOxygen(int amount)
    {
        if (IsDead) return;
        if (hasInfiniteOxygen.value) return;
        // A silence zone has breathable air, so no drain.
        if (FogClearingZone.ContainsPoint(transform.position)) return;
        currentOxygen.value = Mathf.Clamp(currentOxygen.value - amount, 0, maxOxygen.value);
    }

    // Raises max oxygen by a fraction of its current value and refills to full.
    public void ServerAddMaxOxygenPercent(float pct)
    {
        if (!isServer) return;
        int add = Mathf.Max(1, Mathf.RoundToInt(maxOxygen.value * pct));
        maxOxygen.value += add;
        currentOxygen.value = maxOxygen.value;
    }

    // Enables or disables infinite oxygen, refilling to full when enabling.
    public void ServerSetInfiniteOxygen(bool value)
    {
        if (!isServer) return;
        hasInfiniteOxygen.value = value;
        if (value) currentOxygen.value = maxOxygen.value;
    }

    // Spends one station charge and refills to full; false when out of charges, dead, or already full.
    public bool ServerTryUseOxygenCharge()
    {
        if (!isServer || IsDead) return false;
        if (currentOxygenCharges.value <= 0) return false;
        if (currentOxygen.value >= maxOxygen.value) return false;

        currentOxygenCharges.value--;
        currentOxygen.value = maxOxygen.value;
        return true;
    }

    // Grants extra station charges, raising the max and handing them over immediately.
    public void ServerAddOxygenCharges(int amount)
    {
        if (!isServer || amount == 0) return;
        maxOxygenCharges.value += amount;
        currentOxygenCharges.value += amount;
    }

    // Completing an expedition revives dead players and resets health, oxygen, and charges to max.
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

    [Header("Hit Stun Settings")]
    [Tooltip("Movement speed multiplier during hit stun (e.g. 0.3 = 30% speed).")]
    [SerializeField] private float _hitStunMultiplier = 0.3f;
    [Tooltip("Duration of the movement speed stun in seconds.")]
    [SerializeField] private float _hitStunDuration = 2f;

    [ServerRpc]
    public void Damage(int damage)
    {
        if (IsDead) return;
        currentHealth.value = Mathf.Max(currentHealth.value - damage, 0);
        RpcOnHit();
        CheckServerDeath();
    }

    // Notifies all clients of a hit so the owning client applies the movement stun.
    [ObserversRpc(runLocally: true)]
    private void RpcOnHit()
    {
        if (TryGetComponent(out StarterAssets.FirstPersonController fpc))
            fpc.ApplyHitStun(_hitStunMultiplier, _hitStunDuration);
        
        if (TryGetComponent(out PlayerMovement pm))
            pm.ApplyHitStun(_hitStunMultiplier, _hitStunDuration);
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

    // Death check run after any server-side health/oxygen change. Lives here because
    // PlayerDeathHandler.Update is disabled on the server's copy of remote clients,
    // but these ServerRpc bodies always run on the server.
    private void CheckServerDeath()
    {
        if (_deathHandler == null) _deathHandler = GetComponent<PlayerDeathHandler>();
        if (_deathHandler != null) _deathHandler.ServerCheckDeath();
    }

    // Voice relay: an utterance is compressed to 16-bit PCM and streamed to the server
    // in small per-frame chunks so it doesn't head-of-line-block movement sync.

    // ~0.1s of 48 kHz audio per frame, small enough not to flood the channel in one frame.
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

    // Owner-side outgoing queue; Enqueue may run on the audio thread, so it's guarded.
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

    // Queues a finished utterance to be streamed up to the server.
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
                Debug.LogWarning("[PlayerManager] Voice send backlog, dropping oldest utterance.");
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
        if (_rxSamples == null || clipId != _rxClipId) return; // stray or out of order
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
        Debug.Log($"[{owner}] Heard a sound at {origin}");
    }

    // Server-side estimate of how loud this player is, decaying to 0 once reports stop.
    public float CurrentVoiceLoudness =>
        (Time.time - _lastVoiceTime <= VoiceStaleSeconds) ? _lastVoiceLoudness : 0f;

    // Reports the local mic's normalised loudness to the server.
    public void ReportVoiceLoudness(float loudness01) => ServerReportVoiceLoudness(loudness01);

    [ServerRpc]
    private void ServerReportVoiceLoudness(float loudness01)
    {
        _lastVoiceLoudness = Mathf.Clamp01(loudness01);
        _lastVoiceTime = Time.time;

        NoiseEvents.Report(transform.position, _lastVoiceLoudness);
    }

    private PlayerUpgrades _upgrades;

    // Reports a momentary noise at the player's position; the quiet-footsteps upgrade scales it down.
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