using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Draws the local player's inventory slots and highlights the active one.
/// </summary>
public class InventoryHUD : MonoBehaviour
{
    [System.Serializable]
    private struct SlotUI
    {
        public Image background;
        public TMP_Text itemLabel;
        public Image icon;
        public RectTransform root;
    }

    [SerializeField] private SlotUI[] _slotUIs = new SlotUI[2];
    [SerializeField] private Color _activeColor = new Color(1f, 1f, 1f, 0.9f);
    [SerializeField] private Color _inactiveColor = new Color(0.4f, 0.4f, 0.4f, 0.6f);
    [SerializeField] private Color _occupiedTint = new Color(0.6f, 1f, 0.6f, 1f);
    [SerializeField] private float _activeScale = 1.2f;
    [SerializeField] private float _inactiveScale = 0.85f;
    [SerializeField] private float _scaleLerpSpeed = 8f;

    private Interactor _interactor;
    private bool _hidden;

    private void Update()
    {
        // Hide the inventory UI while the local player is dead / spectating.
        bool dead = PlayerManager.Local != null && PlayerManager.Local.IsDead;
        if (dead)
        {
            if (!_hidden) SetSlotsVisible(false);
            return;
        }
        if (_hidden) SetSlotsVisible(true);

        if (_interactor == null)
        {
            // Try to find the local player's Interactor once it exists
            _interactor = FindLocalInteractor();
            if (_interactor == null) return;
        }

        Refresh();
    }

    private void SetSlotsVisible(bool visible)
    {
        _hidden = !visible;
        foreach (SlotUI slot in _slotUIs)
            SetSlotObjectActive(slot, visible);
    }

    // Toggles a whole slot UI on/off. Prefers the slot's root object so the extra
    // slot(s) show/hide as one unit; falls back to the individual widgets.
    private static void SetSlotObjectActive(SlotUI slot, bool active)
    {
        if (slot.root != null)
        {
            if (slot.root.gameObject.activeSelf != active)
                slot.root.gameObject.SetActive(active);
            return;
        }
        if (slot.background != null) slot.background.gameObject.SetActive(active);
        if (slot.itemLabel != null) slot.itemLabel.gameObject.SetActive(active);
    }

    private void Refresh()
    {
        // The inventory grows with upgrades, so only drive the slots that currently exist.
        int slotCount = _interactor.Slots != null ? _interactor.Slots.Length : 0;

        for (int i = 0; i < _slotUIs.Length; i++)
        {
            bool unlocked = i < slotCount;
            SetSlotObjectActive(_slotUIs[i], unlocked);
            if (!unlocked) continue;

            bool isActive = i == _interactor.ActiveSlot;
            bool hasItem = _interactor.Slots[i] != null;

            if (_slotUIs[i].background != null)
            {
                _slotUIs[i].background.color = isActive ? _activeColor : _inactiveColor;

                if (hasItem)
                    _slotUIs[i].background.color *= _occupiedTint;
            }

            if (_slotUIs[i].icon != null)
            {
                bool hasIcon = hasItem && _interactor.Slots[i].Icon != null;
                _slotUIs[i].icon.enabled = hasIcon;
                if (hasIcon) _slotUIs[i].icon.sprite = _interactor.Slots[i].Icon;
            }

            if (_slotUIs[i].root != null)
            {
                float targetScale = isActive ? _activeScale : _inactiveScale;
                _slotUIs[i].root.localScale = Vector3.Lerp(
                    _slotUIs[i].root.localScale,
                    Vector3.one * targetScale,
                    Time.deltaTime * _scaleLerpSpeed);
            }

            if (_slotUIs[i].itemLabel != null)
                _slotUIs[i].itemLabel.text = hasItem ? _interactor.Slots[i].name : string.Empty;
        }
    }

    private static Interactor FindLocalInteractor()
    {
        foreach (Interactor interactor in FindObjectsByType<Interactor>(FindObjectsSortMode.None))
        {
            var net = interactor.GetComponent<PurrNet.NetworkTransform>();
            if (net != null && net.isOwner)
                return interactor;
        }
        return null;
    }
}