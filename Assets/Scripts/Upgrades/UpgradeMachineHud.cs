using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The on-screen upgrade picker. One per scene, on a Canvas. The <see cref="UpgradeMachine"/>
/// drives it via TargetRpcs so it only ever shows on the interacting player's screen.
///
/// Setup: a root panel (assigned to <see cref="_root"/>, starts disabled), a container for
/// the cards, and an <see cref="UpgradeOptionCard"/> prefab. Optionally a title label, a
/// credits label, a result/toast label, and a close button.
/// </summary>
public class UpgradeMachineHud : MonoBehaviour
{
    public static UpgradeMachineHud Instance { get; private set; }

    /// <summary>True while the picker is open — other systems can poll this.</summary>
    public static bool IsOpen { get; private set; }

    [Header("Layout")]
    [SerializeField] private GameObject _root;
    [SerializeField] private Transform _cardContainer;
    [SerializeField] private UpgradeOptionCard _cardPrefab;

    [Header("Scene takeover (optional)")]
    [Tooltip("The top-level BalatroFeel object (Canvas + CardsGroup + VisualHandler + " +
             "CRT live under it). It starts disabled in the scene, so it must be turned " +
             "on explicitly — SetActive on a child does nothing while this is off. " +
             "Activated BEFORE cards are spawned, deactivated after they're cleared.")]
    [SerializeField] private GameObject _balatroFeelRoot;
    [Tooltip("The HorizontalCardHolder on CardsGroup. Its Start() spawns its own " +
             "CardsToSpawn slots one frame after BalatroFeel activates — after our own " +
             "cards are already in — which is where the extra 3 kept coming from. We " +
             "disable the COMPONENT (not the CardsToSpawn field, which stays whatever " +
             "you need it at) before that Start() gets a chance to run, so it does " +
             "nothing while the picker is using CardsGroup, and re-enable it on close.")]
    [SerializeField] private HorizontalCardHolder _cardsGroupHolder;
    [Tooltip("Disabled while the picker is open, re-enabled when it closes. Typically " +
             "the player's first-person/gameplay camera.")]
    [SerializeField] private Camera _playerCamera;
    [Tooltip("Enabled while the picker is open, disabled when it closes (e.g. the " +
             "BalatroFeel arcade camera that renders the card canvas).")]
    [SerializeField] private Camera _pickerCamera;
    [Tooltip("The player's StarterAssetsInputs. Its OnApplicationFocus re-locks the " +
             "cursor to cursorLocked's value whenever the game window regains focus " +
             "(including clicking back into the Game view in the Editor), which fights " +
             "the picker's own Cursor calls. We flip cursorLocked itself — not the " +
             "component's enabled state — so a focus change while the picker is open " +
             "re-applies 'unlocked' instead of undoing us.")]
    [SerializeField] private StarterAssets.StarterAssetsInputs _playerInputs;
    [Tooltip("Any other world objects that should be hidden while the picker is open " +
             "(e.g. objects that would otherwise be visible/audible behind the canvas).")]
    [SerializeField] private GameObject[] _objectsToHideWhileOpen;

    [Header("Labels (optional)")]
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

        // Re-assert every frame while open, not just once when it opens. Clicking back
        // into the Game view in the Editor (or any other focus change) makes Unity
        // silently re-lock the cursor to whatever it was last set to, outside of any
        // script's Update — setting it once in SetOpen() isn't enough to survive that.
        if (IsOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    // ── Shown by the machine (TargetRpc → here) ───────────────────────────────

    public void Show(UpgradeMachine machine, UpgradeDatabase db, int[] options, bool debug, int credits)
    {
        _machine = machine;
        ClearCards();

        // Must happen before anything is instantiated into _cardContainer: BalatroFeel
        // (and the VisualCardsHandler/CardsGroup inside it) starts disabled, and cards
        // rely on finding an active VisualCardsHandler at spawn time to parent their
        // flying CardVisual copies into.
        if (_balatroFeelRoot != null) _balatroFeelRoot.SetActive(true);

        // Must happen in this same frame, before Unity gets around to calling this
        // component's own Start() — disabling it here still prevents that first-ever
        // Start() from running at all, which is what stops its own slots from being
        // spawned on top of ours a frame later.
        if (_cardsGroupHolder != null) _cardsGroupHolder.enabled = false;

        if (_cardPrefab == null || _cardContainer == null)
        {
            Debug.LogWarning("[UpgradeMachineHud] Card prefab / container not assigned.");
            return;
        }

        // Defensive: hide anything already sitting in the container that we didn't put
        // there ourselves (e.g. a HorizontalCardHolder on the same object spawning its
        // own demo slots, or leftover cards from the BalatroFeel scene this was copied
        // from). We only track/clean up what WE spawn via _spawned, so this catches
        // everything else regardless of where it came from.
        for (int i = 0; i < _cardContainer.childCount; i++)
            _cardContainer.GetChild(i).gameObject.SetActive(false);

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

        // Newly instantiated children don't get positioned by the Layout Group until
        // the next layout pass, which only happens once something else forces a
        // rebuild (resizing the window, re-enabling the object...). Force it now so
        // the cards land in place immediately instead of stacked at (0,0).
        if (_cardContainer is RectTransform cardRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(cardRect);
    }

    public void ShowNoUpgrades()
    {
        // Nothing to spend — flash a short message if we have a result label, else log.
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

    // ── Internals ────────────────────────────────────────────────────────────

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

        // Always force None/visible — both opening AND closing. We used to restore
        // whatever Cursor.lockState was before opening, but that "previous" value is
        // whatever StarterAssetsInputs set once at scene load (Locked/hidden) and this
        // game never actually wants the cursor hidden, so restoring it was reproducing
        // the exact bug: cursor vanishing the moment the picker closes after a pick.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        ApplySceneTakeover(open);

        IsOpen = open;
    }

    /// <summary>
    /// Hands the screen over to the picker: player camera off / picker camera on,
    /// extra map objects hidden, player HUD suspended. Called symmetrically on
    /// open and close so everything is restored exactly when a card is picked or
    /// the picker is closed any other way.
    /// </summary>
    private void ApplySceneTakeover(bool open)
    {
        // Only ever deactivate here (on close) — activation on open already happened
        // in Show(), before the cards were spawned. Re-activating here would be too
        // late for that first frame.
        if (!open && _balatroFeelRoot != null) _balatroFeelRoot.SetActive(false);

        // Re-enable on close, in case CardsGroup is used for something else outside
        // the upgrade picker. If this is its first-ever enable, Start() (and its own
        // spawn) will finally run at that point — harmless, since BalatroFeel is about
        // to go inactive anyway (or already did, right above).
        if (!open && _cardsGroupHolder != null) _cardsGroupHolder.enabled = true;

        if (_playerCamera != null) _playerCamera.gameObject.SetActive(!open);
        if (_pickerCamera != null) _pickerCamera.gameObject.SetActive(open);
        if (_playerInputs != null) _playerInputs.cursorLocked = !open;

        if (_objectsToHideWhileOpen != null)
            foreach (GameObject obj in _objectsToHideWhileOpen)
                if (obj != null) obj.SetActive(!open);

        if (LocalPlayerUI.Instance != null)
            LocalPlayerUI.Instance.SetSuspended(open);
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
