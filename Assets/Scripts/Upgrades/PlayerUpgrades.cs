using PurrNet;
using UnityEngine;

/// <summary>
/// Per-player upgrade state, replicated with PurrNet and re-applied on every client.
/// </summary>
public class PlayerUpgrades : NetworkIdentity
{
    public static PlayerUpgrades Local { get; private set; }

    // Cumulative bonuses. Server writes; everyone reads and re-applies locally.
    public readonly SyncVar<float> bonusWalkSpeed = new(0f);
    public readonly SyncVar<int> bonusInventorySlots = new(0);

    // Personal Echo-voice pitch. Server writes it, but only the owning client reads it,
    // so only the buyer hears the difference.
    public readonly SyncVar<float> echoVoicePitch = new(1f);

    public float EchoVoicePitch => echoVoicePitch.value;

    // Multiplier on this player's footstep noise, stacking multiplicatively per upgrade.
    public readonly SyncVar<float> footstepNoiseMult = new(1f);

    public float FootstepNoiseMultiplier => footstepNoiseMult.value;

    // Def indices of taken upgrades, used to gate non-repeatable upgrades and for UI.
    public readonly SyncList<int> ownedUpgrades = new();

    private PlayerManager _playerManager;
    private PlayerMovement _movement;
    private Interactor _interactor;
    private StarterAssets.FirstPersonController _fpc;

    // FirstPersonController base speeds, cached before the first bonus is applied so
    // re-applies (or a bonus shrinking) never compound.
    private float _fpcBaseMoveSpeed = -1f;
    private float _fpcBaseSprintSpeed = -1f;

    // Refs are resolved lazily so this works even when the component is disabled on a copy
    // (e.g. by NetworkOwnershipToggle on the server's copy of a remote player).
    private void EnsureRefs()
    {
        if (_playerManager == null) _playerManager = GetComponentInChildren<PlayerManager>(true);
        if (_movement == null) _movement = GetComponentInChildren<PlayerMovement>(true);
        if (_interactor == null) _interactor = GetComponentInChildren<Interactor>(true);
        if (_fpc == null) _fpc = GetComponentInChildren<StarterAssets.FirstPersonController>(true);
    }

    protected override void OnSpawned(bool asServer)
    {
        base.OnSpawned(asServer);

        EnsureRefs();

        if (isOwner) Local = this;

        bonusWalkSpeed.onChanged += ApplyWalkSpeed;
        bonusInventorySlots.onChanged += ApplyInventorySlots;

        // Snap to current state, covering late joiners whose SyncVars arrive pre-populated.
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

    public bool HasUpgrade(int defIndex) => ownedUpgrades.Contains(defIndex);

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

    // Cuts footstep noise by a fraction, stacking multiplicatively and floored above silence.
    public void ServerReduceFootstepNoise(float fraction)
    {
        if (!isServer) return;
        footstepNoiseMult.value =
            Mathf.Clamp(footstepNoiseMult.value * (1f - Mathf.Clamp01(fraction)), 0.05f, 1f);
    }

    public void ServerAddOxygenCharges(int amount)
    {
        if (!isServer) return;
        EnsureRefs();
        _playerManager?.ServerAddOxygenCharges(amount);
    }

    // Records a non-repeatable upgrade so it won't be offered again.
    public void ServerMarkOwned(int defIndex)
    {
        if (!isServer) return;
        if (!ownedUpgrades.Contains(defIndex)) ownedUpgrades.Add(defIndex);
    }

    private void ApplyWalkSpeed(float bonus)
    {
        EnsureRefs();

        // Test prefab (simple mover).
        if (_movement != null) _movement.SetBonusSpeed(bonus);

        // Real prefab: the StarterAssets controller's speeds are the source of truth.
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
