using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// On-screen upgrade picker driven by the UpgradeMachine on the interacting player's screen.
/// </summary>
public class UpgradeMachineHud : MonoBehaviour
{
    public static UpgradeMachineHud Instance { get; private set; }

    public static bool IsOpen { get; private set; }

    [Header("Layout")]
    [SerializeField] private GameObject _root;
    [SerializeField] private Transform _cardContainer;
    [SerializeField] private UpgradeOptionCard _cardPrefab;

    [Header("Labels")]
    [SerializeField] private TMP_Text _titleText;
    [SerializeField] private TMP_Text _creditsText;
    [SerializeField] private TMP_Text _resultText;
    [SerializeField] private Button _closeButton;

    [Header("Behaviour")]
    [SerializeField] private string _title = "SELECT UPGRADE";
    [SerializeField] private string _debugTitle = "DEBUG — PICK ANY UPGRADE";
    [SerializeField] private float _resultVisibleSeconds = 3f;

    private readonly List<UpgradeOptionCard> _spawned = new();
    private UpgradeMachine _machine;

    private CursorLockMode _prevLock;
    private bool _prevCursorVisible;
    private float _hideResultAt = -1f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (_closeButton != null) _closeButton.onClick.AddListener(Hide);
        if (_root != null) _root.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (_hideResultAt > 0f && Time.unscaledTime >= _hideResultAt)
        {
            _hideResultAt = -1f;
            if (_resultText != null) _resultText.gameObject.SetActive(false);
        }
    }

    public void Show(UpgradeMachine machine, UpgradeDatabase db, int[] options, bool debug, int credits)
    {
        _machine = machine;
        ClearCards();

        if (_cardPrefab == null || _cardContainer == null)
        {
            Debug.LogWarning("[UpgradeMachineHud] Card prefab / container not assigned.");
            return;
        }

        foreach (int index in options)
        {
            UpgradeDefinition def = db.Get(index);
            if (def == null) continue;

            UpgradeOptionCard card = Instantiate(_cardPrefab, _cardContainer);
            int captured = index; // avoid closure over the loop variable
            card.Setup(def, () => OnCardChosen(captured));
            _spawned.Add(card);
        }

        if (_titleText != null) _titleText.text = debug ? _debugTitle : _title;
        if (_creditsText != null)
            _creditsText.text = debug ? "DEBUG" : $"CREDITS: {credits}";
        if (_resultText != null) _resultText.gameObject.SetActive(false);

        SetOpen(true);
    }

    public void ShowNoUpgrades()
    {
        if (_resultText != null)
        {
            ShowResultText("NO UPGRADES AVAILABLE — feed the dampener more cells.");
        }
        else
        {
            Debug.Log("[UpgradeMachineHud] No upgrades available.");
        }
    }

    public void ShowResult(UpgradeDefinition def, float rolledValue, int creditsLeft)
    {
        if (_creditsText != null) _creditsText.text = $"CREDITS: {creditsLeft}";
        string msg = def != null ? $"ACQUIRED: {def.ResultDescription(rolledValue)}" : "UPGRADE ACQUIRED";
        ShowResultText(msg);
    }

    private void OnCardChosen(int defIndex)
    {
        if (_machine != null) _machine.ChooseUpgrade(defIndex);
        Hide(); // optimistic close; the result toast still shows via ShowResult
    }

    public void Hide()
    {
        ClearCards();
        SetOpen(false);
    }

    private void SetOpen(bool open)
    {
        if (_root != null) _root.SetActive(open);

        if (open && !IsOpen)
        {
            _prevLock = Cursor.lockState;
            _prevCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.Confined;
            Cursor.visible = true;
        }
        else if (!open && IsOpen)
        {
            Cursor.lockState = _prevLock;
            Cursor.visible = _prevCursorVisible;
        }

        IsOpen = open;
    }

    private void ShowResultText(string msg)
    {
        if (_resultText == null) return;
        _resultText.gameObject.SetActive(true);
        _resultText.text = msg;
        _hideResultAt = Time.unscaledTime + _resultVisibleSeconds;
    }

    private void ClearCards()
    {
        foreach (UpgradeOptionCard card in _spawned)
            if (card != null) Destroy(card.gameObject);
        _spawned.Clear();
    }
}
