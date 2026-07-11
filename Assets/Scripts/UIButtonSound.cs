using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Plays a click sound (and, optionally, a hover sound) on a menu UI element. Add this
/// component directly to any Button — it finds the Button itself and hooks its onClick
/// automatically, no wiring needed. For non-Button clickables (PurrLobby's LobbyEntry,
/// MemberEntry, etc.) call <see cref="PlayClick"/> from their own click handler instead.
///
/// Defaults to Resources/Sounds/UI/button-press.mp3 for the click sound if _clickClip is
/// left empty, so you can drop this on every button in SteamLobby without assigning a
/// clip each time. Override per-button only where you want something different.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class UIButtonSound : MonoBehaviour, IPointerEnterHandler
{
    [Tooltip("Leave empty to use the shared default at Resources/Sounds/UI/button-press.")]
    [SerializeField] private AudioClip _clickClip;
    [SerializeField, Range(0f, 1f)] private float _clickVolume = 1f;

    [Tooltip("Optional. Leave empty for no hover sound (most menus don't need one).")]
    [SerializeField] private AudioClip _hoverClip;
    [SerializeField, Range(0f, 1f)] private float _hoverVolume = 0.6f;

    private static AudioClip s_defaultClickClip;

    private AudioSource _source;

    private void Awake()
    {
        _source = GetComponent<AudioSource>();
        _source.playOnAwake = false;
        _source.spatialBlend = 0f; // 2D — this is UI, not a world sound

        if (_clickClip == null)
        {
            if (s_defaultClickClip == null)
                s_defaultClickClip = Resources.Load<AudioClip>("Sounds/UI/button-press");
            _clickClip = s_defaultClickClip;
        }

        if (TryGetComponent(out Button button))
            button.onClick.AddListener(PlayClick);
    }

    /// <summary>Wire this to non-Button clickables' own click handlers (e.g. LobbyEntry.OnClick).</summary>
    public void PlayClick()
    {
        if (_clickClip != null) _source.PlayOneShot(_clickClip, _clickVolume);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_hoverClip != null) _source.PlayOneShot(_hoverClip, _hoverVolume);
    }
}
