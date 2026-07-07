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
            
            // Hide the loading screen once the local player is fully spawned in the City
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "City")
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
        if (IsDead) return; // Stop draining oxygen / accepting debug input when dead

        _oxygenTimer += Time.deltaTime;
        if (_oxygenTimer >= 1f)
        {
            _oxygenTimer = 0f;
            DrainOxygen(1);
        }

        // Debug bindings — remove before shipping (F is the flashlight now)
        if (Input.GetKeyDown(KeyCode.X)) GainOxygen(10);
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

    [ServerRpc]
    public void DrainOxygen(int amount)
    {
        if (IsDead) return;
        currentOxygen.value = Mathf.Clamp(currentOxygen.value - amount, 0, maxOxygen.value);
        CheckServerDeath();
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

    public void SubmitVoiceClipToServer(float[] samples, int sampleRate, int channels)
        => ServerReceiveVoiceClip(samples, sampleRate, channels);

    [ServerRpc]
    private void ServerReceiveVoiceClip(float[] samples, int sampleRate, int channels)
    {
        if (VoiceRecordingStore.Instance == null)
        {
            Debug.LogWarning("[PlayerManager] VoiceRecordingStore not found in scene.");
            return;
        }

        AudioClip clip = AudioClip.Create(
            $"voice_{owner}",
            samples.Length / channels,
            channels,
            sampleRate,
            stream: false
        );
        clip.SetData(samples, offsetSamples: 0);

        string playerId = owner?.ToString() ?? "unknown";
        var captured = new CapturedVoiceClip(playerId, clip, Time.time);
        VoiceRecordingStore.Instance.Enqueue(captured);
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

    /// <summary>Owner → server: a momentary noise at the player's position (footstep, sprint).</summary>
    public void ReportNoise(float loudness01) => ServerReportNoise(loudness01);

    [ServerRpc]
    private void ServerReportNoise(float loudness01)
    {
        NoiseEvents.Report(transform.position, Mathf.Clamp01(loudness01));
    }
}