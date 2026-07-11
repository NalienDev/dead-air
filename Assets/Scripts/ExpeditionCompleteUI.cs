using TMPro;
using UnityEngine;

/// <summary>
/// The "EXPEDITION COMPLETE" splash. One per scene, on a Canvas: assign the root panel
/// (disabled by default) and optionally a label. <see cref="RoverManager"/> shows it on
/// every client for a configurable number of seconds before players are teleported back
/// to the lobby.
/// </summary>
public class ExpeditionCompleteUI : MonoBehaviour
{
    public static ExpeditionCompleteUI Instance { get; private set; }

    [Tooltip("Panel toggled on/off. Leave it disabled in the scene.")]
    [SerializeField] private GameObject _root;
    [Tooltip("Optional label. Leave empty to keep whatever text the panel already has.")]
    [SerializeField] private TMP_Text _label;
    [Tooltip("Optional message written into the label when shown.")]
    [SerializeField] private string _message = "EXPEDITION COMPLETE";

    private float _hideAt = -1f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (_root != null) _root.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Shows the splash for <paramref name="seconds"/>, then hides itself.</summary>
    public void Show(float seconds)
    {
        if (_root != null) _root.SetActive(true);
        if (_label != null && !string.IsNullOrEmpty(_message)) _label.text = _message;
        _hideAt = Time.unscaledTime + seconds;
    }

    public void Hide()
    {
        if (_root != null) _root.SetActive(false);
        _hideAt = -1f;
    }

    private void Update()
    {
        if (_hideAt > 0f && Time.unscaledTime >= _hideAt)
            Hide();
    }
}
