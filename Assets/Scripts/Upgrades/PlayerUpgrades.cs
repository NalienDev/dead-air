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

    // Multiplier on this player's footstep/movement noise (1 = normal). Stacks
    // multiplicatively per quiet-footsteps upgrade. Read owner-side in
    // PlayerManager.ReportNoise, so every noise source is covered.
    public readonly SyncVar<float> footstepNoiseMult = new(1f);

    /// <summary>How loud this player's footsteps are relative to normal (0..1).</summary>
    public float FootstepNoiseMultiplier => footstepNoiseMult.value;

    // Def indices of taken upgrades. Used to gate non-repeatable upgrades and for UI.
    public readonly SyncList<int> ownedUpgrades = new();

    private PlayerManager _playerManager;
    private PlayerMovement _movement;
    private Interactor _interactor;
    private StarterAssets.FirstPersonController _fpc;

    // FirstPersonController base speeds, cached before the first bonus is applied so
    // re-applies (or a bonus shrinking) never compound.
    private float _fpcBaseMoveSpeed = -1f;
    private float _fpcBaseSprintSpeed = -1f;

    // Refs are resolved lazily so this component behaves correctly whether or not it's
    // enabled on a given copy (e.g. disabled by NetworkOwnershipToggle on the server's
    // copy of a remote player). Server-side writes and event applies never depend on
    // this component's own Behaviour.enabled state.
    private void EnsureRefs()
    {
        if (_playerManager == null) _playerManager = GetComponentInChildren<PlayerManager>(true);
        if (_movement == null) _movement = GetComponentInChildren<PlayerMovement>(true);
        if (_interactor == null) _interactor = GetComponentInChildren<Interactor>(true);
        if (_fpc == null) _fpc = GetComponentInChildren<StarterAssets.FirstPersonController>(true);
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

    /// <summary>Cuts footstep noise by <paramref name="fraction"/> (0.3 = 30% quieter).
    /// Stacks multiplicatively; floored so footsteps are never fully silent.</summary>
    public void ServerReduceFootstepNoise(float fraction)
    {
        if (!isServer) return;
        footstepNoiseMult.value =
            Mathf.Clamp(footstepNoiseMult.value * (1f - Mathf.Clamp01(fraction)), 0.05f, 1f);
    }

    /// <summary>Grants extra oxygen-station charges (raises max + current).</summary>
    public void ServerAddOxygenCharges(int amount)
    {
        if (!isServer) return;
        EnsureRefs();
        _playerManager?.ServerAddOxygenCharges(amount);
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

        // Test prefab (simple mover).
        if (_movement != null) _movement.SetBonusSpeed(bonus);

        // Real prefab (StarterAssets rig) — the controller's public speeds ARE the
        // source of truth for movement, so the bonus goes straight onto them.
        if (_fpc != null)
        {
            if (_fpcBaseMoveSpeed < 0f)
            {
                _fpcBaseMoveSpeed = _fpc.MoveSpeed;
                _fpcBaseSprintSpeed = _fpc.SprintSpeed;
            }

            _fpc.MoveSpeed = _fpcBaseMoveSpeed + bonus;
            _fpc.SprintSpeed = _fpcBaseSprintSpeed + bonus;
        }
    }

    private void ApplyInventorySlots(int extra)
    {
        EnsureRefs();
        if (_interactor != null) _interactor.SetExtraSlots(extra);
    }
}
