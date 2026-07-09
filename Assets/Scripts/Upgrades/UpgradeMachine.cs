using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// The retro sci-fi "slot machine". Sits at base; the player looks at it and presses E
/// (see <see cref="Interactor"/>). It offers a roguelike "pick one of N" upgrade choice,
/// paid for out of a shared credit pool that fills as energy cells are fed into the
/// <see cref="Dampener"/>.
///
/// Credit model (server &amp; clients both derive it from replicated state, so they always
/// agree): each energy cell inserted into the dampener grants one upgrade credit, EXCEPT
/// the final one — cells 1..<see cref="_maxUpgradeGrantingCells"/> grant a credit, the
/// 7th (the last) does not. Credits spent are tracked in <see cref="_spentUpgrades"/>.
/// Because cells can only be inserted at base, upgrades are naturally a between-expedition
/// activity: expedition → return with cells → feed dampener → spend the credits here →
/// next expedition.
///
/// Flow (fully server-authoritative on PurrNet):
/// <list type="bullet">
/// <item>Client presses E → <see cref="OnInteract"/> → ServerRpc asks for an offer.</item>
/// <item>Server rolls the options (honouring rarity/requirements), remembers them for
/// that player, and TargetRpcs them back to open the HUD on that client only.</item>
/// <item>Client picks a card → ServerRpc → server re-validates the pick, rolls the
/// value, applies it via <see cref="PlayerUpgrades"/>, and spends a credit.</item>
/// </list>
///
/// Setup: put this on the machine object (a networked scene object on the interactable
/// layer with a collider). Assign the <see cref="UpgradeDatabase"/> and the
/// <see cref="Dampener"/>. There should be exactly one <see cref="UpgradeMachineHud"/>
/// in the scene.
/// </summary>
public class UpgradeMachine : Interactable
{
    [Header("Config")]
    [SerializeField] private UpgradeDatabase _database;
    [Tooltip("The dampener whose inserted cells fund upgrades. Auto-found if empty.")]
    [SerializeField] private Dampener _dampener;
    [Tooltip("How many upgrade cards to offer per interaction.")]
    [SerializeField, Min(1)] private int _optionsPerRoll = 3;
    [Tooltip("Cells 1..N grant an upgrade credit; the (N+1)-th (the last) grants none.")]
    [SerializeField, Min(0)] private int _maxUpgradeGrantingCells = 6;

    [Header("Audio (optional)")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _openSound;
    [SerializeField] private AudioClip _purchaseSound;

    [Header("Debug")]
    [Tooltip("When on, the debug key opens the machine with EVERY upgrade listed and " +
             "purchases are free and skip all requirement checks.")]
    [SerializeField] private bool _debugFreeUpgrades = false;
    [Tooltip("Key the local player presses to open the free debug picker.")]
    [SerializeField] private KeyCode _debugKey = KeyCode.F3;

    // Spent credits. Replicated so AvailableUpgrades matches on every client.
    private readonly SyncVar<int> _spentUpgrades = new(0);

    // Server-side memory of what each player was offered, so a pick can be validated.
    private sealed class Offer { public int[] options; public bool free; }
    private readonly Dictionary<PlayerID, Offer> _pendingOffers = new();

    // ── Credit accounting (derived, agrees on all peers) ─────────────────────

    private int CellsGranting =>
        _dampener != null ? Mathf.Min(_dampener.CellCount, _maxUpgradeGrantingCells) : 0;

    /// <summary>Upgrade credits currently available to spend.</summary>
    public int AvailableUpgrades => Mathf.Max(0, CellsGranting - _spentUpgrades.value);

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        if (_dampener == null) _dampener = GetComponentInParent<Dampener>();
        if (_dampener == null) _dampener = FindAnyObjectByType<Dampener>();
    }

    private void Update()
    {
        if (!_debugFreeUpgrades) return;
        if (!Input.GetKeyDown(_debugKey)) return;
        if (PlayerUpgrades.Local == null) return;

        RequestOffer(PlayerUpgrades.Local, debug: true);
    }

    // ── Interaction ────────────────────────────────────────────────────────

    public override InteractionType OnInteract(GameObject user)
    {
        PlayerUpgrades pu = user.GetComponentInChildren<PlayerUpgrades>(true)
                            ?? user.GetComponentInParent<PlayerUpgrades>();
        if (pu == null)
        {
            Debug.LogWarning("[UpgradeMachine] Interacting object has no PlayerUpgrades.", this);
            return InteractionType.NONE;
        }

        RequestOffer(pu, debug: false);
        return InteractionType.PRESS;
    }

    private void RequestOffer(PlayerUpgrades player, bool debug) => ServerRequestOffer(player, debug);

    // ── Server: build and hand out an offer ──────────────────────────────────

    [ServerRpc(requireOwnership: false)]
    private void ServerRequestOffer(PlayerUpgrades player, bool debug)
    {
        if (player == null || !player.owner.HasValue) return;
        PlayerID pid = player.owner.Value;

        bool free = debug && _debugFreeUpgrades;

        if (!free && AvailableUpgrades <= 0)
        {
            TargetNoUpgrades(pid);
            return;
        }

        if (_database == null)
        {
            Debug.LogWarning("[UpgradeMachine] No UpgradeDatabase assigned.", this);
            return;
        }

        int expeditions = QuotaManager.Instance != null
            ? QuotaManager.Instance.expeditionsCompleted.value : 0;

        int[] options = free
            ? _database.AllOptions()
            : _database.RollOptions(_optionsPerRoll, player, expeditions);

        if (options.Length == 0)
        {
            TargetNoUpgrades(pid);
            return;
        }

        _pendingOffers[pid] = new Offer { options = options, free = free };
        TargetShowOffer(pid, options, free, AvailableUpgrades);
    }

    [TargetRpc]
    private void TargetShowOffer(PlayerID target, int[] options, bool debug, int credits)
    {
        if (UpgradeMachineHud.Instance == null)
        {
            Debug.LogWarning("[UpgradeMachine] No UpgradeMachineHud in scene.");
            return;
        }
        PlayLocal(_openSound);
        UpgradeMachineHud.Instance.Show(this, _database, options, debug, credits);
    }

    [TargetRpc]
    private void TargetNoUpgrades(PlayerID target)
    {
        UpgradeMachineHud.Instance?.ShowNoUpgrades();
    }

    // ── Client → server: the player picked a card ─────────────────────────────

    /// <summary>Called by the HUD when the local player clicks an upgrade card.</summary>
    public void ChooseUpgrade(int defIndex)
    {
        if (PlayerUpgrades.Local == null) return;
        ServerChooseUpgrade(PlayerUpgrades.Local, defIndex);
    }

    [ServerRpc(requireOwnership: false)]
    private void ServerChooseUpgrade(PlayerUpgrades player, int defIndex)
    {
        if (player == null || !player.owner.HasValue) return;
        PlayerID pid = player.owner.Value;

        // Must match an offer we actually handed this player.
        if (!_pendingOffers.TryGetValue(pid, out Offer offer)) return;
        if (System.Array.IndexOf(offer.options, defIndex) < 0) return;
        _pendingOffers.Remove(pid);

        UpgradeDefinition def = _database != null ? _database.Get(defIndex) : null;
        if (def == null) return;

        if (!offer.free)
        {
            // Re-validate everything server-side — the client is never trusted.
            if (AvailableUpgrades <= 0) return;

            int expeditions = QuotaManager.Instance != null
                ? QuotaManager.Instance.expeditionsCompleted.value : 0;
            if (expeditions < def.MinExpeditions) return;
            if (!def.Repeatable && player.HasUpgrade(defIndex)) return;
        }

        float rolled = def.Roll();
        def.ServerApply(player, rolled);
        if (!def.Repeatable) player.ServerMarkOwned(defIndex);

        if (!offer.free) _spentUpgrades.value += 1;

        TargetPurchaseResult(pid, defIndex, rolled, AvailableUpgrades);
    }

    [TargetRpc]
    private void TargetPurchaseResult(PlayerID target, int defIndex, float rolledValue, int creditsLeft)
    {
        PlayLocal(_purchaseSound);
        UpgradeDefinition def = _database != null ? _database.Get(defIndex) : null;
        UpgradeMachineHud.Instance?.ShowResult(def, rolledValue, creditsLeft);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void PlayLocal(AudioClip clip)
    {
        if (clip == null) return;
        if (_audioSource != null) _audioSource.PlayOneShot(clip);
        else AudioSource.PlayClipAtPoint(clip, transform.position);
    }
}
