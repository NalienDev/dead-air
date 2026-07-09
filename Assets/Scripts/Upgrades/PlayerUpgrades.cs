using PurrNet;
using UnityEngine;

/// <summary>
/// Per-player upgrade state, replicated with PurrNet. Lives on the player prefab
/// (alongside <see cref="PlayerManager"/>).
///
/// The <see cref="UpgradeMachine"/> validates and applies everything on the SERVER by
/// calling the <c>Server*</c> methods here. The results are held in SyncVars / a
/// SyncList so every client — and late joiners — end up with the same character:
/// <list type="bullet">
/// <item>walk-speed and inventory bonuses are cumulative SyncVars, re-applied to the
/// local <see cref="PlayerMovement"/>/<see cref="Interactor"/> whenever they change;</item>
/// <item>oxygen and "infinite oxygen" live on <see cref="PlayerManager"/> (its own
/// SyncVars) and are just poked from here;</item>
/// <item><see cref="ownedUpgrades"/> records the taken non-repeatable upgrades so they
/// can't be offered again.</item>
/// </list>
/// </summary>
public class PlayerUpgrades : NetworkIdentity
{
    /// <summary>The local player's upgrade component (set on the owner).</summary>
    public static PlayerUpgrades Local { get; private set; }

    // Cumulative bonuses. Server writes; everyone reads and re-applies locally.
    public readonly SyncVar<float> bonusWalkSpeed = new(0f);
    public readonly SyncVar<int> bonusInventorySlots = new(0);

    // Personal Echo-voice pitch. Server writes it (so the purchase stays validated), but
    // only the OWNING client ever reads it — the Echo pitches its local playback by
    // PlayerUpgrades.Local.EchoVoicePitch, so only the buyer hears the difference.
    public readonly SyncVar<float> echoVoicePitch = new(1f);

    /// <summary>Local Echo-voice pitch for this player (1 = unchanged).</summary>
    public float EchoVoicePitch => echoVoicePitch.value;

    // Def indices of taken upgrades. Used to gate non-repeatable upgrades and for UI.
    public readonly SyncList<int> ownedUpgrades = new();

    private PlayerManager _playerManager;
    private PlayerMovement _movement;
    private Interactor _interactor;

    // Refs are resolved lazily so this component behaves correctly whether or not it's
    // enabled on a given copy (e.g. disabled by NetworkOwnershipToggle on the server's
    // copy of a remote player). Server-side writes and event applies never depend on
    // this component's own Behaviour.enabled state.
    private void EnsureRefs()
    {
        if (_playerManager == null) _playerManager = GetComponentInChildren<PlayerManager>(true);
        if (_movement == null) _movement = GetComponentInChildren<PlayerMovement>(true);
        if (_interactor == null) _interactor = GetComponentInChildren<Interactor>(true);
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    protected override void OnSpawned(bool asServer)
    {
        base.OnSpawned(asServer);

        EnsureRefs();

        if (isOwner) Local = this;

        bonusWalkSpeed.onChanged += ApplyWalkSpeed;
        bonusInventorySlots.onChanged += ApplyInventorySlots;

        // Snap to current state (covers late joiners — SyncVars arrive pre-populated).
        ApplyWalkSpeed(bonusWalkSpeed.value);
        ApplyInventorySlots(bonusInventorySlots.value);
    }

    protected override void OnDespawned(bool asServer)
    {
        base.OnDespawned(asServer);
        bonusWalkSpeed.onChanged -= ApplyWalkSpeed;
        bonusInventorySlots.onChanged -= ApplyInventorySlots;
        if (Local == this) Local = null;
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    public bool HasUpgrade(int defIndex) => ownedUpgrades.Contains(defIndex);

    // ── Server-side apply (called by UpgradeMachine / UpgradeDefinition) ──────

    public void ServerAddWalkSpeed(float amount)
    {
        if (!isServer) return;
        bonusWalkSpeed.value += amount;
    }

    public void ServerAddInventorySlots(int slots)
    {
        if (!isServer) return;
        bonusInventorySlots.value += slots;
    }

    public void ServerAddOxygenPercent(float pct)
    {
        if (!isServer) return;
        EnsureRefs();
        _playerManager?.ServerAddMaxOxygenPercent(pct);
    }

    public void ServerGrantInfiniteOxygen()
    {
        if (!isServer) return;
        EnsureRefs();
        _playerManager?.ServerSetInfiniteOxygen(true);
    }

    public void ServerSetEchoVoicePitch(float pitch)
    {
        if (!isServer) return;
        echoVoicePitch.value = Mathf.Max(0.1f, pitch);
    }

    /// <summary>Records a non-repeatable upgrade so it won't be offered again.</summary>
    public void ServerMarkOwned(int defIndex)
    {
        if (!isServer) return;
        if (!ownedUpgrades.Contains(defIndex)) ownedUpgrades.Add(defIndex);
    }

    // ── Local presentation (runs on every client via the SyncVars) ───────────

    private void ApplyWalkSpeed(float bonus)
    {
        EnsureRefs();
        if (_movement != null) _movement.SetBonusSpeed(bonus);
    }

    private void ApplyInventorySlots(int extra)
    {
        EnsureRefs();
        if (_interactor != null) _interactor.SetExtraSlots(extra);
    }
}
