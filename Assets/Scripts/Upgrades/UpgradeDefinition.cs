using UnityEngine;

/// <summary>
/// Base class for every upgrade the <see cref="UpgradeMachine"/> can offer. One
/// ScriptableObject asset = one upgrade. Adding a new upgrade is meant to be trivial:
/// make a subclass, implement <see cref="ServerApply"/>, and drop the asset into the
/// <see cref="UpgradeDatabase"/>. Everything else (rolling, rarity, requirements, the
/// HUD card, networking) is handled generically here and in the machine.
///
/// Effects are applied SERVER-SIDE (see <see cref="ServerApply"/>) so the whole thing is
/// authoritative on PurrNet — the client only ever asks for and picks an option.
/// </summary>
public abstract class UpgradeDefinition : ScriptableObject
{
    [Header("Display")]
    public string DisplayName = "Upgrade";

    [Tooltip("Card text. Use the {value} token to inject the rolled amount, e.g. " +
             "\"Move faster (+{value})\".")]
    [TextArea] public string Description = "";

    [Tooltip("Optional icon shown on the upgrade card.")]
    public Sprite Icon;

    [Tooltip("Tint for this card / its rarity. Purely cosmetic.")]
    public Color RarityColor = Color.white;

    [Header("Availability")]
    [Tooltip("Relative weight when the machine rolls which options to offer. " +
             "Higher = appears more often. Ignored by super-rare gating below.")]
    [Min(0f)] public float Weight = 1f;

    [Tooltip("Independent chance (0..1) this upgrade is even eligible to appear in a " +
             "given roll. Use for super-rare upgrades — e.g. 0.01 for a 1% chance, " +
             "0.1 for 10%. Leave at 1 for normal upgrades.")]
    [Range(0f, 1f)] public float AppearChance = 1f;

    [Tooltip("If false this upgrade can only be taken once per run.")]
    public bool Repeatable = true;

    [Tooltip("Completed expeditions required before this can appear " +
             "(e.g. 3 = only from the return of the 3rd expedition onward). 0 = always.")]
    [Min(0)] public int MinExpeditions = 0;

    [Header("Rolled Value (optional)")]
    [Tooltip("If y > x the effect magnitude is rolled uniformly in [x, y] each time it's " +
             "taken. Leave both at 0 (or equal) for upgrades with no numeric value.")]
    public Vector2 ValueRange = Vector2.zero;

    /// <summary>Whether this upgrade has a rolled numeric magnitude at all.</summary>
    public bool HasValue => !Mathf.Approximately(ValueRange.x, ValueRange.y);

    /// <summary>Rolls the effect magnitude. Called on the server at purchase time.</summary>
    public virtual float Roll() =>
        ValueRange.y > ValueRange.x ? Random.Range(ValueRange.x, ValueRange.y) : ValueRange.x;

    /// <summary>
    /// How a single rolled value is formatted for display. Override for percentages,
    /// multipliers, etc. Default prints up to two decimals.
    /// </summary>
    protected virtual string FormatValue(float v) => v.ToString("0.##");

    /// <summary>Card text shown BEFORE picking — {value} becomes the possible range.</summary>
    public virtual string PreviewDescription()
    {
        if (string.IsNullOrEmpty(Description)) return DisplayName;
        string token = HasValue
            ? $"{FormatValue(ValueRange.x)}–{FormatValue(ValueRange.y)}"
            : FormatValue(ValueRange.x);
        return Description.Replace("{value}", token);
    }

    /// <summary>Text shown AFTER picking — {value} becomes the actual rolled amount.</summary>
    public virtual string ResultDescription(float rolledValue) =>
        string.IsNullOrEmpty(Description)
            ? DisplayName
            : Description.Replace("{value}", FormatValue(rolledValue));

    /// <summary>
    /// Server only. Applies this upgrade's effect to <paramref name="player"/> using the
    /// already-rolled <paramref name="rolledValue"/>. Implement per upgrade.
    /// </summary>
    public abstract void ServerApply(PlayerUpgrades player, float rolledValue);
}
