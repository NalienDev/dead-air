using PurrNet;
using UnityEngine;

public class PlayerManager : NetworkIdentity
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

    // ── Private state ──────────────────────────────────────────────────────

    private float _oxygenTimer = 0f;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    protected override void OnSpawned(bool asServer)
    {
        if (isOwner)
            Local = this;

        DontDestroyOnLoad(gameObject);
    }

    // ── Update ─────────────────────────────────────────────────────────────

    private void Update()
    {
        if (!isOwner) return;

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

    /// <summary>
    /// Called by entrance/exit scripts to track whether the player is underground.
    /// Routes through a ServerRpc so the SyncVar is always written on the server.
    /// </summary>
    public void SetInsideDungeon(bool value)
    {
        ServerSetInsideDungeon(value);
    }

    [ServerRpc(requireOwnership: false)]
    private void ServerSetInsideDungeon(bool value)
    {
        isInsideDungeon.value = value;
    }

    // ── Server RPCs ────────────────────────────────────────────────────────

    [ServerRpc]
    public void DrainOxygen(int amount)
    {
        currentOxygen.value = Mathf.Clamp(currentOxygen.value - amount, 0, maxOxygen.value);
    }

    [ServerRpc]
    public void Damage(int damage)
    {
        currentHealth.value = Mathf.Max(currentHealth.value - damage, 0);
    }

    [ServerRpc]
    public void GainOxygen(int amount)
    {
        currentOxygen.value = Mathf.Clamp(currentOxygen.value + amount, 0, maxOxygen.value);
    }
}