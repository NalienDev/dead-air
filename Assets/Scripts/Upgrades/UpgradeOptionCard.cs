using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One clickable upgrade card in the upgrade picker, populated from an UpgradeDefinition.
/// </summary>
public class UpgradeOptionCard : MonoBehaviour
{
    [SerializeField] private Button _button;
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private TMP_Text _descText;
    [SerializeField] private Image _icon;
    [SerializeField] private Image _background;

    private Action _onClick;

    private void Awake()
    {
        if (_button == null) _button = GetComponent<Button>();
        if (_button != null) _button.onClick.AddListener(() => _onClick?.Invoke());
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

        if (_background != null) _background.color = def.RarityColor;
    }
}
