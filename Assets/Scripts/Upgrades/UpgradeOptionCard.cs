using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One clickable upgrade card in the <see cref="UpgradeMachineHud"/>. Put this on a UI
/// prefab that has a Button, a name label, a description label, and (optionally) an icon
/// and a background image to tint by rarity. The HUD instantiates one per offered option.
/// </summary>
public class UpgradeOptionCard : MonoBehaviour
{
    [SerializeField] private Button _button;
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private TMP_Text _descText;
    [SerializeField] private Image _icon;
    [SerializeField] private Image _background;

    [Tooltip("Optional: if this card also has a BalatroFeel Card component, its " +
             "cardVisual (the copy actually rendered on screen) gets the same " +
             "icon/name/description via UpgradeCardVisualContent, if present there. " +
             "Auto-found on the same GameObject if left empty.")]
    [SerializeField] private Card _card;

    private Action _onClick;

    private void Awake()
    {
        if (_button == null) _button = GetComponent<Button>();
        if (_button != null) _button.onClick.AddListener(() => _onClick?.Invoke());
        if (_card == null) _card = GetComponent<Card>();
    }

    public void Setup(UpgradeDefinition def, Action onClick)
    {
        _onClick = onClick;

        if (_nameText != null) _nameText.text = def.DisplayName;
        if (_descText != null) _descText.text = def.PreviewDescription();

        if (_icon != null)
        {
            _icon.sprite = def.Icon;
            _icon.enabled = def.Icon != null;
        }

        if (_background != null)
        {
            // Only touch RGB — keep whatever alpha is already on the prefab (0, so this
            // card stays invisible; CardVisual is what's actually shown). RarityColor is
            // opaque, so overwriting the whole color here would undo that every time.
            Color tint = def.RarityColor;
            tint.a = _background.color.a;
            _background.color = tint;
        }

        // Card.cardVisual is created in Card.Awake(), which Unity already ran
        // synchronously as part of Instantiate() — so it's safe to reach into it here,
        // same frame. This is what actually shows on screen.
        if (_card != null && _card.cardVisual != null &&
            _card.cardVisual.TryGetComponent(out UpgradeCardVisualContent content))
        {
            content.Setup(def);
        }
    }
}
