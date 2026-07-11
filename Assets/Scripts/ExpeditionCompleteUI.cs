using TMPro;
using UnityEngine;

/// <summary>
/// Splash panel shown to every client when an expedition completes, before returning to the lobby.
/// </summary>
public class ExpeditionCompleteUI : MonoBehaviour
{
    public static ExpeditionCompleteUI Instance { get; private set; }

    [Tooltip("Panel toggled on and off.")]
    [SerializeField] private GameObject _root;
    [Tooltip("Label to write the message into.")]
    [SerializeField] private TMP_Text _label;
    [Tooltip("Message written into the label when shown.")]
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
