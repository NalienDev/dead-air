using PurrNet;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// A grabbable "loot" object that contributes bandwidth to the quota.
///
/// Every object rolls a <see cref="Condition"/> on the server when it spawns:
/// <list type="bullet">
/// <item><b>Perfect</b> — worth 100% of its base value</item>
/// <item><b>Damaged</b> — worth 60%</item>
/// <item><b>Worthless</b> — worth 0% (junk)</item>
/// </list>
/// The odds are tunable per-prefab in the Inspector, and the rolled condition is
/// replicated so every client (and the Rover that banks it) agrees on the value.
///
/// Audio-wise each object can:
/// <list type="bullet">
/// <item>Hum a constant, very quiet looping sound (its own <see cref="AudioSource"/>).</item>
/// <item>Have a chance to make a louder one-shot noise when grabbed — which the
/// blind Conductor can hear (see <see cref="NoiseEvents"/>).</item>
/// </list>
/// </summary>
public class BandwidthObject : GrabbableObject
{
    public enum Condition { Perfect = 0, Damaged = 1, Worthless = 2 }

    // ── Value ──────────────────────────────────────────────────────────────

    [Header("Bandwidth Value")]
    [Tooltip("Base value at 100% (Perfect) condition.")]
    [SerializeField, FormerlySerializedAs("_bandwidthValue")] private int _baseValue = 100;

    [Tooltip("Value multipliers per condition. Industry-standard defaults: 100% / 60% / 0%.")]
    [SerializeField, Range(0f, 2f)] private float _perfectMultiplier = 1.0f;
    [SerializeField, Range(0f, 2f)] private float _damagedMultiplier = 0.6f;
    [SerializeField, Range(0f, 2f)] private float _worthlessMultiplier = 0.0f;

    [Header("Condition Odds (per prefab)")]
    [Tooltip("Chance this object spawns in Perfect condition.")]
    [SerializeField, Range(0f, 1f)] private float _perfectChance = 0.45f;
    [Tooltip("Chance this object spawns Damaged. Whatever probability is left over " +
             "after Perfect + Damaged becomes the chance of spawning Worthless.")]
    [SerializeField, Range(0f, 1f)] private float _damagedChance = 0.4f;

    [Header("Condition Visuals (optional)")]
    [Tooltip("Enabled only while the object is in Perfect condition.")]
    [SerializeField] private GameObject _perfectVisual;
    [Tooltip("Enabled only while the object is Damaged.")]
    [SerializeField] private GameObject _damagedVisual;
    [Tooltip("Enabled only while the object is Worthless.")]
    [SerializeField] private GameObject _worthlessVisual;

    // ── Ambient hum ────────────────────────────────────────────────────────

    [Header("Constant Ambient Sound (optional)")]
    [Tooltip("Looping, quiet hum played from this object. Configure clip/volume/pitch/" +
             "spatial blend directly on this AudioSource. Leave empty for a silent object.")]
    [SerializeField] private AudioSource _ambientSource;
    [Tooltip("Start the ambient hum automatically when the object spawns.")]
    [SerializeField] private bool _playAmbientOnSpawn = true;

    // ── Pickup noise ───────────────────────────────────────────────────────

    [Header("Pickup Sound (optional)")]
    [Tooltip("One-shot played when grabbed. Leave empty for a silent pickup.")]
    [SerializeField] private AudioClip _pickupSound;
    [Tooltip("Chance the pickup sound fires when grabbed. 0 = never, 1 = always.")]
    [SerializeField, Range(0f, 1f)] private float _pickupSoundChance = 0.35f;
    [SerializeField, Range(0f, 1f)] private float _pickupSoundVolume = 1f;
    [Tooltip("How loud the pickup is to the Conductor (0..1). Grabbing a noisy item " +
             "should be enough to make it come investigate.")]
    [SerializeField, Range(0f, 1f)] private float _pickupNoiseLoudness = 0.55f;

    // ── Networked state ────────────────────────────────────────────────────

    // Stored as int (0/1/2) so it works on every PurrNet build regardless of enum
    // serialization support. Rolled once on the server in OnSpawned.
    private readonly SyncVar<int> _conditionIndex = new((int)Condition.Perfect);

    private AudioSource _pickupSource;

    public Condition CurrentCondition => (Condition)_conditionIndex.value;

    /// <summary>
    /// Effective bandwidth value after applying the condition multiplier.
    /// This is what the Rover banks toward the quota (RoverManager reads it).
    /// </summary>
    public int BandwidthValue =>
        Mathf.RoundToInt(_baseValue * MultiplierFor(CurrentCondition));

    // ── Lifecycle ──────────────────────────────────────────────────────────

    protected override void OnSpawned(bool asServer)
    {
        base.OnSpawned(asServer);

        if (asServer)
            _conditionIndex.value = (int)RollCondition();

        _conditionIndex.onChanged += OnConditionChanged;
        ApplyConditionVisuals(CurrentCondition);

        if (_playAmbientOnSpawn && _ambientSource != null)
        {
            _ambientSource.loop = true;
            if (!_ambientSource.isPlaying) _ambientSource.Play();
        }

        // A dedicated, non-looping source for pickup one-shots so it never fights
        // the ambient loop.
        _pickupSource = gameObject.AddComponent<AudioSource>();
        _pickupSource.playOnAwake = false;
        _pickupSource.spatialBlend = _ambientSource != null ? _ambientSource.spatialBlend : 1f;
    }

    protected override void OnDespawned(bool asServer)
    {
        base.OnDespawned(asServer);
        _conditionIndex.onChanged -= OnConditionChanged;
    }

    // ── Condition ──────────────────────────────────────────────────────────

    private Condition RollCondition()
    {
        float perfect = Mathf.Clamp01(_perfectChance);
        float damaged = Mathf.Clamp01(_damagedChance);

        // Perfect + Damaged might exceed 1 if mis-configured; normalise defensively.
        if (perfect + damaged > 1f)
        {
            float scale = 1f / (perfect + damaged);
            perfect *= scale;
            damaged *= scale;
        }

        float roll = Random.value;
        if (roll < perfect) return Condition.Perfect;
        if (roll < perfect + damaged) return Condition.Damaged;
        return Condition.Worthless;
    }

    private float MultiplierFor(Condition c) => c switch
    {
        Condition.Perfect => _perfectMultiplier,
        Condition.Damaged => _damagedMultiplier,
        _ => _worthlessMultiplier,
    };

    private void OnConditionChanged(int _) => ApplyConditionVisuals(CurrentCondition);

    private void ApplyConditionVisuals(Condition c)
    {
        if (_perfectVisual != null) _perfectVisual.SetActive(c == Condition.Perfect);
        if (_damagedVisual != null) _damagedVisual.SetActive(c == Condition.Damaged);
        if (_worthlessVisual != null) _worthlessVisual.SetActive(c == Condition.Worthless);
    }

    // ── Interaction / pickup noise ─────────────────────────────────────────

    public override InteractionType OnInteract(GameObject user)
    {
        InteractionType result = base.OnInteract(user);

        // A silent object (no clip) can't be heard, so it never alerts the Conductor.
        if (result == InteractionType.GRAB && _pickupSound != null)
            ServerTryPickupSound();

        return result;
    }

    [ServerRpc(requireOwnership: false)]
    private void ServerTryPickupSound()
    {
        // Server-authoritative roll for whether this grab actually makes a sound.
        // Sound and alert are one and the same event: if it makes a sound the blind
        // Conductor hears it; if not, nothing happens.
        if (Random.value > _pickupSoundChance) return;

        RpcPlayPickupSound();
        NoiseEvents.Report(transform.position, _pickupNoiseLoudness);
    }

    [ObserversRpc(runLocally: true)]
    private void RpcPlayPickupSound()
    {
        if (_pickupSound == null) return;
        if (_pickupSource == null)
        {
            _pickupSource = gameObject.AddComponent<AudioSource>();
            _pickupSource.playOnAwake = false;
            _pickupSource.spatialBlend = 1f;
        }
        _pickupSource.PlayOneShot(_pickupSound, _pickupSoundVolume);
    }
}
