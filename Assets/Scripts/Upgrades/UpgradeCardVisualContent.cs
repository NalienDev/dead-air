using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Sits on the same GameObject as <see cref="CardVisual"/> (the runtime-instantiated
/// "flying" copy that BalatroFeel actually renders on screen). CardVisual's own Sprite
/// is opaque card-face art with no room for text, so an upgrade card's icon/name/
/// description live here instead, as extra children drawn on top of that Sprite —
/// otherwise they'd be stuck on the underlying Card/UpgradeCard object and get
/// completely covered by the visual copy that flies on top of it.
///
/// <see cref="UpgradeOptionCard"/> looks for this on <c>Card.cardVisual</c> right after
/// spawning and pushes the upgrade's icon/name/description into it.
/// </summary>
public class UpgradeCardVisualContent : MonoBehaviour
{
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private TMP_Text _descText;
    [SerializeField] private Image _icon;
    [SerializeField] private Image _background;

    public void Setup(UpgradeDefinition def)
    {
        if (_nameText != null) _nameText.text = def.DisplayName;
        if (_descText != null) _descText.text = def.PreviewDescription();

        if (_icon != null)
        {
            _icon.sprite = def.Icon;
            _icon.enabled = def.Icon != null;
        }

        if (_background != null) _background.color = def.RarityColor;
    }
}
