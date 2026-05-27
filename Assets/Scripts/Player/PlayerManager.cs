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

    // ── Lifecycle ──────────────────────────────────────────────────────────

    protected override void OnSpawned(bool asServer)
    {
        if (isOwner)
            Local = this;

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

        // Debug bindings — remove before shipping
        if (Input.GetKeyDown(KeyCode.F)) Damage(10);
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

    // ── Server RPCs ────────────────────────────────────────────────────────

    [ServerRpc]
    public void DrainOxygen(int amount)
    {
        if (IsDead) return;
        currentOxygen.value = Mathf.Clamp(currentOxygen.value - amount, 0, maxOxygen.value);
    }

    [ServerRpc]
    public void Damage(int damage)
    {
        if (IsDead) return;
        currentHealth.value = Mathf.Max(currentHealth.value - damage, 0);
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
}