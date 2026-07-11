using UnityEngine;

/// <summary>
/// Base ScriptableObject for an upgrade, handling rolling, rarity, requirements, and display generically.
/// </summary>
public abstract class UpgradeDefinition : ScriptableObject
{
    [Header("Display")]
    public string DisplayName = "Upgrade";

    [Tooltip("Card text. Use the {value} token to inject the rolled amount.")]
    [TextArea] public string Description = "";

    [Tooltip("Icon shown on the upgrade card.")]
    public Sprite Icon;

    [Tooltip("Tint for this card. Purely cosmetic.")]
    public Color RarityColor = Color.white;

    [Header("Availability")]
    [Tooltip("Relative weight when rolling which options to offer.")]
    [Min(0f)] public float Weight = 1f;

    [Tooltip("Independent chance this upgrade is eligible to appear in a roll. 1 = normal.")]
    [Range(0f, 1f)] public float AppearChance = 1f;

    [Tooltip("If false this upgrade can only be taken once per run.")]
    public bool Repeatable = true;

    [Tooltip("Completed expeditions required before this can appear. 0 = always.")]
    [Min(0)] public int MinExpeditions = 0;

    [Header("Rolled Value")]
    [Tooltip("If y > x the effect magnitude is rolled uniformly in [x, y]. Equal for no value.")]
    public Vector2 ValueRange = Vector2.zero;

    public bool HasValue => !Mathf.Approximately(ValueRange.x, ValueRange.y);

    // Rolls the effect magnitude on the server at purchase time.
    public virtual float Roll() =>
        ValueRange.y > ValueRange.x ? Random.Range(ValueRange.x, ValueRange.y) : ValueRange.x;

    // Formats a rolled value for display; override for percentages, multipliers, etc.
    protected virtual string FormatValue(float v) => v.ToString("0.##");

    // Card text shown before picking; {value} becomes the possible range.
    public virtual string PreviewDescription()
    {
        if (string.IsNullOrEmpty(Description)) return DisplayName;
        string token = HasValue
            ? $"{FormatValue(ValueRange.x)}–{FormatValue(ValueRange.y)}"
            : FormatValue(ValueRange.x);
        return Description.Replace("{value}", token);
    }

    // Text shown after picking; {value} becomes the actual rolled amount.
    public virtual string ResultDescription(float rolledValue) =>
        string.IsNullOrEmpty(Description)
            ? DisplayName
            : Description.Replace("{value}", FormatValue(rolledValue));

    // Server only; applies this upgrade's effect to the player using the rolled value.
    public abstract void ServerApply(PlayerUpgrades player, float rolledValue);
}
