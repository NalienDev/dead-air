using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Attach to a Canvas GameObject.
/// Reads the local player's Interactor each frame and updates two slot UI panels.
/// Each slot panel needs an Image (background tint) and optionally a TMP_Text (item name).
/// </summary>
public class InventoryHUD : MonoBehaviour
{
    [System.Serializable]
    private struct SlotUI
    {
        public Image background;      // panel background
        public TMP_Text itemLabel;    // optional label showing item name
    }

    [SerializeField] private SlotUI[] _slotUIs = new SlotUI[2];
    [SerializeField] private Color _activeColor = new Color(1f, 1f, 1f, 0.9f);
    [SerializeField] private Color _inactiveColor = new Color(0.4f, 0.4f, 0.4f, 0.6f);
    [SerializeField] private Color _occupiedTint = new Color(0.6f, 1f, 0.6f, 1f);

    private Interactor _interactor;

    private void Update()
    {
        if (_interactor == null)
        {
            // Try to find the local player's Interactor once it exists
            _interactor = FindLocalInteractor();
            if (_interactor == null) return;
        }

        Refresh();
    }

    private void Refresh()
    {
        for (int i = 0; i < _slotUIs.Length; i++)
        {
            bool isActive = i == _interactor.ActiveSlot;
            bool hasItem = _interactor.Slots[i] != null;

            if (_slotUIs[i].background != null)
            {
                _slotUIs[i].background.color = isActive ? _activeColor : _inactiveColor;

                if (hasItem)
                    _slotUIs[i].background.color *= _occupiedTint;
            }

            if (_slotUIs[i].itemLabel != null)
                _slotUIs[i].itemLabel.text = hasItem ? _interactor.Slots[i].name : string.Empty;
        }
    }

    /// <summary>
    /// Finds the Interactor that belongs to the local player.
    /// Relies on PurrNet's NetworkTransform.isOwner to identify the local player.
    /// </summary>
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