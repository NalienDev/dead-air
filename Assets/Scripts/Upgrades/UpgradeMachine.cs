using System.Collections.Generic;
using PurrNet;
using UnityEngine;

/// <summary>
/// Server-authoritative upgrade machine offering a pick-one-of-N choice paid for with dampener credits.
/// </summary>
public class UpgradeMachine : Interactable
{
    [Header("Config")]
    [SerializeField] private UpgradeDatabase _database;
    [Tooltip("The dampener whose inserted cells fund upgrades. Auto-found if empty.")]
    [SerializeField] private Dampener _dampener;
    [Tooltip("How many upgrade cards to offer per interaction.")]
    [SerializeField, Min(1)] private int _optionsPerRoll = 3;
    [Tooltip("Cells 1..N grant an upgrade credit; the last grants none.")]
    [SerializeField, Min(0)] private int _maxUpgradeGrantingCells = 6;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _openSound;
    [SerializeField] private AudioClip _purchaseSound;

    [Header("Debug")]
    [Tooltip("Opens the machine with every upgrade listed and purchases free.")]
    [SerializeField] private bool _debugFreeUpgrades = false;
    [Tooltip("Key the local player presses to open the free debug picker.")]
    [SerializeField] private KeyCode _debugKey = KeyCode.F3;

    // Credits spent per player. Each player gets their own pool: one inserted cell grants
    // one upgrade to every player, so one player's purchases never drain another's.
    // Server-only — clients learn their remaining credits via the offer/purchase TargetRpcs.
    private readonly Dictionary<PlayerID, int> _spentUpgrades = new();

    // Server-side memory of what each player was offered, so a pick can be validated.
    private sealed class Offer { public int[] options; public bool free; }
    private readonly Dictionary<PlayerID, Offer> _pendingOffers = new();

    private int CellsGranting =>
        _dampener != null ? Mathf.Min(_dampener.CellCount, _maxUpgradeGrantingCells) : 0;

    private int SpentFor(PlayerID pid) => _spentUpgrades.TryGetValue(pid, out int v) ? v : 0;

    // Credits still available to a specific player.
    private int AvailableFor(PlayerID pid) => Mathf.Max(0, CellsGranting - SpentFor(pid));

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

    [ServerRpc(requireOwnership: false)]
    private void ServerRequestOffer(PlayerUpgrades player, bool debug)
    {
        if (player == null || !player.owner.HasValue) return;
        PlayerID pid = player.owner.Value;

        bool free = debug && _debugFreeUpgrades;

        if (!free && AvailableFor(pid) <= 0)
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
        TargetShowOffer(pid, options, free, AvailableFor(pid));
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

    // Called by the HUD when the local player clicks an upgrade card.
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

        // Must match an offer actually handed to this player.
        if (!_pendingOffers.TryGetValue(pid, out Offer offer)) return;
        if (System.Array.IndexOf(offer.options, defIndex) < 0) return;
        _pendingOffers.Remove(pid);

        UpgradeDefinition def = _database != null ? _database.Get(defIndex) : null;
        if (def == null) return;

        if (!offer.free)
        {
            // Re-validate server-side; the client is never trusted.
            if (AvailableFor(pid) <= 0) return;

            int expeditions = QuotaManager.Instance != null
                ? QuotaManager.Instance.expeditionsCompleted.value : 0;
            if (expeditions < def.MinExpeditions) return;
            if (!def.Repeatable && player.HasUpgrade(defIndex)) return;
        }

        float rolled = def.Roll();
        def.ServerApply(player, rolled);
        if (!def.Repeatable) player.ServerMarkOwned(defIndex);

        if (!offer.free) _spentUpgrades[pid] = SpentFor(pid) + 1;

        TargetPurchaseResult(pid, defIndex, rolled, AvailableFor(pid));
    }

    [TargetRpc]
    private void TargetPurchaseResult(PlayerID target, int defIndex, float rolledValue, int creditsLeft)
    {
        PlayLocal(_purchaseSound);
        UpgradeDefinition def = _database != null ? _database.Get(defIndex) : null;
        UpgradeMachineHud.Instance?.ShowResult(def, rolledValue, creditsLeft);
    }

    private void PlayLocal(AudioClip clip)
    {
        if (clip == null) return;
        if (_audioSource != null) _audioSource.PlayOneShot(clip);
        else AudioSource.PlayClipAtPoint(clip, transform.position);
    }
}
