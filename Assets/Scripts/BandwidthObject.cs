using PurrNet;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Grabbable loot object that rolls a condition on spawn and contributes bandwidth to the quota.
/// </summary>
public class BandwidthObject : GrabbableObject
{
    public enum Condition { Perfect = 0, Damaged = 1, Worthless = 2 }

    [Header("Bandwidth Value")]
    [Tooltip("Base value at Perfect condition.")]
    [SerializeField, FormerlySerializedAs("_bandwidthValue")] private int _baseValue = 100;

    [Tooltip("Value multiplier per condition.")]
    [SerializeField, Range(0f, 2f)] private float _perfectMultiplier = 1.0f;
    [SerializeField, Range(0f, 2f)] private float _damagedMultiplier = 0.6f;
    [SerializeField, Range(0f, 2f)] private float _worthlessMultiplier = 0.0f;

    [Header("Condition Odds")]
    [Tooltip("Chance to spawn Perfect.")]
    [SerializeField, Range(0f, 1f)] private float _perfectChance = 0.45f;
    [Tooltip("Chance to spawn Damaged. Remaining probability becomes Worthless.")]
    [SerializeField, Range(0f, 1f)] private float _damagedChance = 0.4f;

    [Header("Condition Visuals")]
    [Tooltip("Shown only while Perfect.")]
    [SerializeField] private GameObject _perfectVisual;
    [Tooltip("Shown only while Damaged.")]
    [SerializeField] private GameObject _damagedVisual;
    [Tooltip("Shown only while Worthless.")]
    [SerializeField] private GameObject _worthlessVisual;

    [Header("Debug")]
    [Tooltip("Force this object to spawn Damaged, ignoring the odds.")]
    [SerializeField] private bool _debugForceDamaged = false;

    [Header("Ambient Sound")]
    [Tooltip("Looping hum played from this object.")]
    [SerializeField] private AudioSource _ambientSource;
    [Tooltip("Start the ambient hum on spawn.")]
    [SerializeField] private bool _playAmbientOnSpawn = true;

    [Header("Pickup Sound")]
    [Tooltip("One-shot played when grabbed.")]
    [SerializeField] private AudioClip _pickupSound;
    [Tooltip("Chance the pickup sound fires when grabbed.")]
    [SerializeField, Range(0f, 1f)] private float _pickupSoundChance = 0.35f;
    [SerializeField, Range(0f, 1f)] private float _pickupSoundVolume = 1f;
    [Tooltip("How loud the pickup is to the Conductor.")]
    [SerializeField, Range(0f, 1f)] private float _pickupNoiseLoudness = 0.55f;

    // Stored as int so it serializes on every PurrNet build; rolled once on the server.
    private readonly SyncVar<int> _conditionIndex = new((int)Condition.Perfect);

    private AudioSource _pickupSource;

    public Condition CurrentCondition => (Condition)_conditionIndex.value;

    // Effective value after the condition multiplier; the Rover banks this toward the quota.
    public int BandwidthValue =>
        Mathf.RoundToInt(_baseValue * MultiplierFor(CurrentCondition));

    protected override void OnSpawned(bool asServer)
    {
        base.OnSpawned(asServer);

        if (asServer)
            _conditionIndex.value = (int)(_debugForceDamaged ? Condition.Damaged : RollCondition());

        _conditionIndex.onChanged += OnConditionChanged;
        ApplyConditionVisuals(CurrentCondition);
        UpdateAmbientSound();

        // Separate non-looping source so pickup one-shots don't fight the ambient loop.
        _pickupSource = gameObject.AddComponent<AudioSource>();
        _pickupSource.playOnAwake = false;
        _pickupSource.spatialBlend = _ambientSource != null ? _ambientSource.spatialBlend : 1f;
    }

    protected override void OnDespawned(bool asServer)
    {
        base.OnDespawned(asServer);
        _conditionIndex.onChanged -= OnConditionChanged;
    }

    private Condition RollCondition()
    {
        float perfect = Mathf.Clamp01(_perfectChance);
        float damaged = Mathf.Clamp01(_damagedChance);

        // Normalise if the chances sum above 1.
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

    private void OnConditionChanged(int _)
    {
        ApplyConditionVisuals(CurrentCondition);
        UpdateAmbientSound();
    }

    // Worthless junk stays silent, which doubles as a tell that audible loot has value.
    private void UpdateAmbientSound()
    {
        if (_ambientSource == null) return;

        bool shouldPlay = _playAmbientOnSpawn && CurrentCondition != Condition.Worthless;
        if (shouldPlay)
        {
            _ambientSource.loop = true;
            if (!_ambientSource.isPlaying) _ambientSource.Play();
        }
        else if (_ambientSource.isPlaying)
        {
            _ambientSource.Stop();
        }
    }

    private void ApplyConditionVisuals(Condition c)
    {
        if (_perfectVisual != null) _perfectVisual.SetActive(c == Condition.Perfect);
        if (_damagedVisual != null) _damagedVisual.SetActive(c == Condition.Damaged);
        if (_worthlessVisual != null) _worthlessVisual.SetActive(c == Condition.Worthless);
    }

    public override InteractionType OnInteract(GameObject user)
    {
        InteractionType result = base.OnInteract(user);

        // Only damaged objects can make a pickup noise; perfect and worthless grabs are silent.
        if (result == InteractionType.GRAB && _pickupSound != null
            && CurrentCondition == Condition.Damaged)
            ServerTryPickupSound();

        return result;
    }

    [ServerRpc(requireOwnership: false)]
    private void ServerTryPickupSound()
    {
        if (CurrentCondition != Condition.Damaged) return; // re-validate client claim
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
