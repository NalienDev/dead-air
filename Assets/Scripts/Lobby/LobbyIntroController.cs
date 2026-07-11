using System.Collections;
using PurrLobby;
using UnityEngine;

/// <summary>
/// Drives the lobby intro sequence:
///   1) Camera sits at the spawn point, screen shows the first boot image.
///   2) Enter -> camera lerps to the PC point while the screen cycles through
///      the boot image list (its own pace, set by bootImageInterval), ending
///      on the last frame.
///   3) Up/Down cycles the 3 menu images (Start / Options / Quit selector).
///   4) Enter on any option -> camera moves PC -> point 1 -> the option's own
///      target. Every time an option is confirmed, a fresh prefab is spawned
///      at point 1 (the previous instance, if any, is destroyed first), and
///      the camera waits waitAfterSpawn seconds before continuing on to the
///      target.
///        - Start   -> playPosition/playRotation, then lobbyCanvasRoot + mainScreenCanvas
///                     are activated and screenOn is turned off.
///        - Options -> optionsPosition/optionsRotation, then lobbyCanvasRoot + optionsScreenCanvas
///                     are activated and screenOn is turned off.
///        - Quit    -> quitPosition/quitRotation, then Application.Quit().
///   5) Escape while resting at the Start/Options target reverses the same
///      trip: the canvas is deactivated, screenOn is turned back on, and the
///      camera moves target -> point 1 -> PC, landing back at the menu.
///   6) From the MainScreen (after Start), Browse moves the camera straight
///      away (OnBrowseClicked -> playPosition -> browsePosition), since it
///      doesn't depend on any network call.
///      Create Lobby / Join are different: PurrLobby's own buttons already
///      show the CreatingRoomOverlay/LoadingRoomOverlay and kick off the
///      network call directly. Instead of moving the camera on click, this
///      script waits for LobbyManager's "On Room Joined (Lobby)" event
///      (wire it to OnRoomJoinedFromLobby, NOT to ViewManager.OnRoomJoined
///      directly) - only once the lobby actually exists does the camera move
///      playPosition -> createJoinPosition, and only then is lobbyScreenCanvas
///      shown (via viewManager.OnRoomJoined()).
///   7) Leaving mirrors this: wire LobbyManager's "On Room Left ()" event to
///      OnRoomLeftFromLobby (NOT to ViewManager.OnRoomLeft directly). The
///      camera moves createJoinPosition -> playPosition first, and only then
///      is mainScreenCanvas shown (via viewManager.OnRoomLeft()).
///
/// All positions/images/objects are wired up in the Inspector - nothing is
/// found by name at runtime, so it doesn't matter how the scene hierarchy
/// ends up being organised.
/// </summary>
public class LobbyIntroController : MonoBehaviour
{
    private enum State { Waiting, MovingToPc, AtMenu, Busy, AtTarget, AtMainScreen, AtSubScreen }

    [Header("Camera")]
    [Tooltip("Camera that gets moved. Defaults to Camera.main if left empty.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Camera - Spawn")]
    [SerializeField] private Vector3 spawnPosition = new Vector3(856.69f, -1.46f, -444.25f);
    [SerializeField] private Vector3 spawnRotation = new Vector3(19.062f, -169.424f, -2.213f);

    [Header("Camera - PC (menu)")]
    [SerializeField] private Vector3 pcPosition = new Vector3(856.432f, -2.217f, -448.21f);
    [SerializeField] private Vector3 pcRotation = new Vector3(6.985f, -181.619f, 0.858f);

    [Header("Camera - Point 1 (shared hub after the first selection)")]
    [SerializeField] private Vector3 point1Position = new Vector3(856.422f, -2.174f, -447.862f);
    [SerializeField] private Vector3 point1Rotation = new Vector3(6.985f, -181.619f, 0.858f);

    [Header("Camera - Start/Play target (also the MainScreen hub)")]
    [SerializeField] private Vector3 playPosition = new Vector3(856.418f, -1.74f, -448.167f);
    [SerializeField] private Vector3 playRotation = new Vector3(-40.382f, -182.448f, 1.118f);

    [Header("Camera - Options target")]
    [SerializeField] private Vector3 optionsPosition = new Vector3(858.273f, -0.653f, -448.815f);
    [SerializeField] private Vector3 optionsRotation = new Vector3(-36.216f, -256.644f, 0.595f);

    [Header("Camera - Quit target")]
    [SerializeField] private Vector3 quitPosition = new Vector3(860.97f, -2.41f, -448.87f);
    [SerializeField] private Vector3 quitRotation = new Vector3(1.506f, -266.344f, -6.875f);

    [Header("Camera - Create Lobby / Join target")]
    [SerializeField] private Vector3 createJoinPosition = new Vector3(855.23f, -1.34f, -448.93f);
    [SerializeField] private Vector3 createJoinRotation = new Vector3(-25.265f, -150.633f, -1.073f);

    [Header("Camera - Browse target")]
    [SerializeField] private Vector3 browsePosition = new Vector3(857.109f, -1.384f, -449.157f);
    [SerializeField] private Vector3 browseRotation = new Vector3(-40.705f, -201.044f, 1.655f);

    [Header("Camera - Timing")]
    [SerializeField] private float approachDuration = 2f;
    [Tooltip("PC -> point 1 (happens every time an option is confirmed).")]
    [SerializeField] private float toPoint1Duration = 1f;
    [Tooltip("Point 1 -> the selected option's target.")]
    [SerializeField] private float toTargetDuration = 1f;
    [Tooltip("Target -> point 1, when Escape is pressed.")]
    [SerializeField] private float returnToPoint1Duration = 1f;
    [Tooltip("Point 1 -> PC, when Escape is pressed (mirrors toPoint1Duration).")]
    [SerializeField] private float returnToPcDuration = 1f;
    [Tooltip("Time to wait after the prefab spawns (first selection only), before moving on to the target.")]
    [SerializeField] private float waitAfterSpawn = 2f;
    [Tooltip("Current camera position -> playPosition, when Create Lobby/Join/Browse is clicked from the MainScreen.")]
    [SerializeField] private float toMainScreenHubDuration = 0.5f;
    [Tooltip("playPosition -> the Create Lobby/Join/Browse target.")]
    [SerializeField] private float toSubScreenDuration = 1f;
    [SerializeField] private AnimationCurve moveCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Camera - Sub-screen transition waypoints")]
    [Tooltip("Lobby -> MainMenu: camera passes through this point first.")]
    [SerializeField] private Vector3 lobbyToMainWaypointPosition = new Vector3(855.736f, -1.827f, -448.031f);
    [SerializeField] private Vector3 lobbyToMainWaypointRotation = new Vector3(-25.265f, -150.633f, -1.073f);
    [Tooltip("MainMenu -> Lobby (Create/Join) or Browse: camera passes through this point first.")]
    [SerializeField] private Vector3 mainToSubWaypointPosition = new Vector3(856.42f, -1.65f, -448.27f);
    [SerializeField] private Vector3 mainToSubWaypointRotation = new Vector3(-40.382f, -182.448f, 1.118f);
    [Tooltip("Browse -> MainMenu: camera passes through this point first.")]
    [SerializeField] private Vector3 browseToMainWaypointPosition = new Vector3(856.95f, -1.77f, -448.73f);
    [SerializeField] private Vector3 browseToMainWaypointRotation = new Vector3(-40.705f, -201.044f, 1.655f);
    [Tooltip("Duration for the leg into the waypoint above, before continuing on to the real target.")]
    [SerializeField] private float toWaypointDuration = 0.4f;

    [Header("Screen (screenOn renderer)")]
    [SerializeField] private Renderer screenRenderer;
    [Tooltip("Same image is applied to both Base Map and Emission Map so the screen actually glows.")]
    [SerializeField] private string texturePropertyName = "_BaseMap";
    [SerializeField] private string emissionTexturePropertyName = "_EmissionMap";
    [Tooltip("Boot-up sequence played while the camera travels from spawn to the PC. Last entry stays on screen when it finishes.")]
    [SerializeField] private Texture2D[] bootImages;
    [Tooltip("Seconds each boot image stays on screen. Independent from the camera's approachDuration - tune this to slow the boot sequence down.")]
    [SerializeField] private float bootImageInterval = 0.2f;
    [Tooltip("Delay before the boot image cycling starts (after Enter is pressed).")]
    [SerializeField] private float bootSequenceStartDelay = 1f;
    [Tooltip("Exactly 3 images: index 0 = selector on Start, 1 = Options, 2 = Quit.")]
    [SerializeField] private Texture2D[] menuImages;

    [Header("Menu")]
    [SerializeField] private bool wrapSelection = true;
    [SerializeField] private KeyCode confirmKey = KeyCode.Return;
    [SerializeField] private KeyCode confirmKeyAlt = KeyCode.KeypadEnter;

    [Header("PurrNet Lobby Canvas")]
    [Tooltip("The LobbyCanvas root. Activated whenever Start or Options is confirmed.")]
    [SerializeField] private GameObject lobbyCanvasRoot;
    [Tooltip("Child panel shown after Start is confirmed.")]
    [SerializeField] private GameObject mainScreenCanvas;
    [Tooltip("Child panel shown after Options is confirmed.")]
    [SerializeField] private GameObject optionsScreenCanvas;
    [Tooltip("Child panel shown after Create Lobby or Join is clicked.")]
    [SerializeField] private GameObject lobbyScreenCanvas;
    [Tooltip("Child panel shown after Browse is clicked.")]
    [SerializeField] private GameObject browseScreenCanvas;
    [Tooltip("PurrLobby's CreatingRoomOverlay (CreatingRoomView). Must be active for ViewManager to show it without erroring.")]
    [SerializeField] private GameObject creatingRoomOverlay;
    [Tooltip("PurrLobby's LoadingRoomOverlay (LoadingRoomView). Must be active for ViewManager to show it without erroring.")]
    [SerializeField] private GameObject loadingRoomOverlay;

    [Header("Start Sequence (first selection only)")]
    [Tooltip("Prefab instantiated every time an option is confirmed. The previous instance is destroyed first.")]
    [SerializeField] private GameObject prefabToSpawn;
    [Tooltip("Empty GameObject marking where/how the prefab should spawn (position + rotation).")]
    [SerializeField] private Transform spawnPoint;
    [Tooltip("The monitor the spawned bullet hits. Repaired right before each spawn so it can shatter again every time.")]
    [SerializeField] private ExplodableMonitor explodableMonitor;

    [Header("PurrLobby ViewManager")]
    [Tooltip("PurrLobby's ViewManager component (same GameObject the panels live under). Used to show LobbyScreen/MainScreen ourselves, only once the matching camera move has finished.")]
    [SerializeField] private ViewManager viewManager;

    [Header("Sound")]
    [Tooltip("Auto-added if left empty.")]
    [SerializeField] private AudioSource sfxSource;
    [Tooltip("Played on every Up/Down while at the menu (image switch). Leave empty to use the shared default at Resources/Sounds/UI/button-press.")]
    [SerializeField] private AudioClip navigateClip;
    [Tooltip("Played on Enter/confirm. Leave empty to use the shared default at Resources/Sounds/UI/button-press.")]
    [SerializeField] private AudioClip selectClip;

    private static AudioClip s_defaultUiClip;

    private State _state = State.Waiting;
    private int _selectedIndex;
    private GameObject _spawnedInstance;
    private MaterialPropertyBlock _mpb;

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (sfxSource == null) sfxSource = GetComponent<AudioSource>();
        if (sfxSource == null) sfxSource = gameObject.AddComponent<AudioSource>();
        sfxSource.playOnAwake = false;
        sfxSource.spatialBlend = 0f; // menu SFX, not positional

        if (s_defaultUiClip == null)
            s_defaultUiClip = Resources.Load<AudioClip>("Sounds/UI/button-press");
        if (navigateClip == null) navigateClip = s_defaultUiClip;
        if (selectClip == null) selectClip = s_defaultUiClip;
    }

    private void Start()
    {
        SetCameraTo(spawnPosition, spawnRotation);

        if (bootImages != null && bootImages.Length > 0)
            SetScreenTexture(bootImages[0]);
    }

    private void Update()
    {
        switch (_state)
        {
            case State.Waiting:
                if (IsConfirmPressed())
                    StartCoroutine(ApproachPc());
                break;

            case State.AtMenu:
                if (Input.GetKeyDown(KeyCode.UpArrow)) ChangeSelection(-1);
                else if (Input.GetKeyDown(KeyCode.DownArrow)) ChangeSelection(1);
                else if (IsConfirmPressed()) ConfirmSelection();
                break;

            case State.AtTarget:
            case State.AtMainScreen:
                if (Input.GetKeyDown(KeyCode.Escape))
                    StartCoroutine(ReturnToMenu());
                break;
        }
    }

    private bool IsConfirmPressed()
    {
        return Input.GetKeyDown(confirmKey) || Input.GetKeyDown(confirmKeyAlt);
    }

    private void ChangeSelection(int dir)
    {
        if (menuImages == null || menuImages.Length == 0) return;

        int count = menuImages.Length;
        _selectedIndex += dir;

        _selectedIndex = wrapSelection
            ? ((_selectedIndex % count) + count) % count
            : Mathf.Clamp(_selectedIndex, 0, count - 1);

        SetScreenTexture(menuImages[_selectedIndex]);
        PlaySfx(navigateClip);
    }

    private void ConfirmSelection()
    {
        PlaySfx(selectClip);
        _state = State.Busy;
        StartCoroutine(MoveToTargetSequence(_selectedIndex));
    }

    private void PlaySfx(AudioClip clip)
    {
        if (clip != null && sfxSource != null) sfxSource.PlayOneShot(clip);
    }

    // mainScreenCanvas/lobbyScreenCanvas/browseScreenCanvas/creatingRoomOverlay/
    // loadingRoomOverlay are all PurrLobby "View" components managed by
    // ViewManager through a CanvasGroup (alpha/interactable/blocksRaycasts).
    // If we SetActive(false) their GameObject, their Awake() never runs and
    // ViewManager.ShowView<T>() throws a NullReferenceException the first
    // time it tries to show one (view.canvasGroup is still null). So we only
    // ever SetActive(true) them once and never touch them again - ViewManager
    // (already wired to these same buttons) owns showing/hiding from here on.
    //
    // Order matters here: the children are activated BEFORE the parent.
    // ViewManager.Start() only runs once (the first time its GameObject
    // becomes active) and hides every non-default view via CanvasGroup - but
    // it can only hide a view whose Awake() has already run. If the parent
    // were activated first, ViewManager.Start() would fire before the other
    // panels were active, so they'd never get hidden and would all show up
    // stacked on top of each other. Activating every child first means they
    // all become active in the same batch as the parent, so every view's
    // Awake() has already run by the time ViewManager.Start() hides them.
    private void ActivateLobbyCanvas()
    {
        if (mainScreenCanvas != null) mainScreenCanvas.SetActive(true);
        if (lobbyScreenCanvas != null) lobbyScreenCanvas.SetActive(true);
        if (browseScreenCanvas != null) browseScreenCanvas.SetActive(true);
        if (creatingRoomOverlay != null) creatingRoomOverlay.SetActive(true);
        if (loadingRoomOverlay != null) loadingRoomOverlay.SetActive(true);
        if (lobbyCanvasRoot != null) lobbyCanvasRoot.SetActive(true);
    }

    // optionsScreenCanvas is a plain panel (no PurrLobby View component), so
    // it's safe to toggle directly.
    private void ShowOptionsPanel()
    {
        ActivateLobbyCanvas();

        // ActivateLobbyCanvas() is the first time lobbyCanvasRoot (and
        // therefore ViewManager) ever becomes active - regardless of
        // whether Start or Options was picked from the PC menu. ViewManager
        // reacts to that by showing its Default View (MainScreen) once,
        // automatically. That's correct for Start, but wrong here: without
        // hiding it, MainScreen sits on top of/behind optionsScreenCanvas
        // and is what actually ends up visible instead of Options.
        HideView(mainScreenCanvas);

        if (optionsScreenCanvas != null) optionsScreenCanvas.SetActive(true);
    }

    // ── MainScreen buttons (Create Lobby / Join / Browse) ────────────────

    // Browse doesn't depend on any network call, but the panel must still
    // wait for the camera - it's shown ourselves, at the end of the
    // coroutine, once the camera has actually arrived. In the Inspector,
    // the Browse button's OnClick() should call ONLY
    // LobbyIntroController.OnBrowseClicked() - remove ViewManager.OnBrowseClicked()
    // from that button, otherwise BrowseScreen shows immediately on click
    // instead of waiting for the camera.
    public void OnBrowseClicked() => TryStartSubScreenNav(browsePosition, browseRotation);

    private void TryStartSubScreenNav(Vector3 targetPos, Vector3 targetRot)
    {
        if (_state != State.AtMainScreen) return;

        _state = State.Busy;
        StartCoroutine(MainScreenSubNav(targetPos, targetRot));
    }

    private IEnumerator MainScreenSubNav(Vector3 targetPos, Vector3 targetRot)
    {
        // MainScreen is a full-screen UI (Screen Space Overlay) - it covers
        // the whole viewport, so the camera move behind it is completely
        // invisible until we hide it ourselves first. SetActive(false) - not
        // just alpha - so it's guaranteed to stop rendering/blocking no
        // matter what else is going on in the canvas.
        HideView(mainScreenCanvas);

        // Make sure we're at the hub first (usually a no-op, we should
        // already be sitting at playPosition).
        Vector3 fromPos = cameraTransform.position;
        Vector3 fromRot = cameraTransform.rotation.eulerAngles;
        yield return MoveCamera(fromPos, fromRot, playPosition, playRotation, toMainScreenHubDuration);

        yield return MoveCameraViaWaypoint(playPosition, playRotation, mainToSubWaypointPosition, mainToSubWaypointRotation, targetPos, targetRot, toWaypointDuration, toSubScreenDuration);

        // Only now, with the camera in place, show BrowseScreen ourselves.
        ShowView(browseScreenCanvas);
        if (viewManager != null) viewManager.OnBrowseClicked();

        _state = State.AtSubScreen;
    }

    // BrowseScreen's Back/Leave button should call this instead of
    // ViewManager.OnLeaveBrowseClicked() directly, same reasoning as Leave
    // Lobby below: move the camera back to playPosition first, then show
    // MainScreen.
    public void OnLeaveBrowseClicked()
    {
        if (_state != State.AtSubScreen) return;

        _state = State.Busy;
        StartCoroutine(ExitBrowseScreen());
    }

    private IEnumerator ExitBrowseScreen()
    {
        HideView(browseScreenCanvas);

        Vector3 fromPos = cameraTransform.position;
        Vector3 fromRot = cameraTransform.rotation.eulerAngles;
        yield return MoveCameraViaWaypoint(fromPos, fromRot, browseToMainWaypointPosition, browseToMainWaypointRotation, playPosition, playRotation, toWaypointDuration, toSubScreenDuration);

        ShowView(mainScreenCanvas);
        if (viewManager != null) viewManager.OnLeaveBrowseClicked();

        _state = State.AtMainScreen;
    }

    // ── Create Lobby / Join (gated by the actual network call) ───────────
    // Do NOT wire the Create Lobby/Join buttons to anything here - leave
    // them exactly as they are (PurrLobby's own click handling shows the
    // CreatingRoomOverlay/LoadingRoomOverlay and starts the network call).
    // Instead, in the Inspector, rewire LobbyManager's "On Room Joined
    // (Lobby)" event: remove ViewManager.OnRoomJoined() from it and add
    // LobbyIntroController.OnRoomJoinedFromLobby(Lobby) instead. That event
    // only fires once the lobby genuinely exists, so this is what lets the
    // camera move happen at the right time instead of racing the network call.
    public void OnRoomJoinedFromLobby(Lobby lobby)
    {
        // AtSubScreen covers two different screens - Browse AND the lobby
        // screen itself - and they need opposite handling here. Whether
        // BrowseScreen is currently active is what tells them apart: if it
        // is, this join came from picking a room in the browse list and
        // still needs the full camera trip + LobbyScreen reveal below, even
        // though we're technically already "AtSubScreen". If it's not
        // active, we're already sitting in the lobby and this is just a
        // later data update (a player joined/left, etc.) - the panel's
        // already showing, nothing to animate.
        bool comingFromBrowse = browseScreenCanvas != null && browseScreenCanvas.activeSelf;

        if (_state == State.AtSubScreen && !comingFromBrowse)
        {
            if (viewManager != null) viewManager.OnRoomJoined();
            return;
        }

        // Anything other than AtMainScreen (or AtSubScreen-via-Browse) here
        // means we're already mid transition (e.g. a second OnRoomJoined for
        // this same creation arrived a frame later, while EnterLobbyScreen
        // is still running) - ignore it instead of prematurely revealing
        // LobbyScreen. The coroutine already in flight will show it itself
        // once it's done.
        if (_state != State.AtMainScreen && !comingFromBrowse) return;

        _state = State.Busy;
        StartCoroutine(EnterLobbyScreen());
    }

    private IEnumerator EnterLobbyScreen()
    {
        // Hide everything that could currently be showing - MainScreen (the
        // normal path), BrowseScreen (if this join came from picking a room
        // in the browse list instead), and the CreatingRoomOverlay/
        // LoadingRoomOverlay (shown separately by ViewManager with
        // hideOthers:false) - so the camera move is fully visible with no
        // UI on screen at all while it travels.
        HideView(mainScreenCanvas);
        HideView(browseScreenCanvas);
        HideView(creatingRoomOverlay);
        HideView(loadingRoomOverlay);

        Vector3 fromPos = cameraTransform.position;
        Vector3 fromRot = cameraTransform.rotation.eulerAngles;
        yield return MoveCamera(fromPos, fromRot, playPosition, playRotation, toMainScreenHubDuration);
        yield return MoveCameraViaWaypoint(playPosition, playRotation, mainToSubWaypointPosition, mainToSubWaypointRotation, createJoinPosition, createJoinRotation, toWaypointDuration, toSubScreenDuration);

        // Only now, with the camera in place, actually show LobbyScreen.
        ShowView(lobbyScreenCanvas);
        if (viewManager != null) viewManager.OnRoomJoined();

        _state = State.AtSubScreen;
    }

    // ── Leave (gated by the camera returning first) ───────────────────────
    // In the Inspector, rewire LobbyManager's "On Room Left ()" event:
    // remove ViewManager.OnRoomLeft() from it and add
    // LobbyIntroController.OnRoomLeftFromLobby() instead.
    public void OnRoomLeftFromLobby()
    {
        // Already back on MainScreen - nothing to animate, just relay.
        if (_state == State.AtMainScreen)
        {
            if (viewManager != null) viewManager.OnRoomLeft();
            return;
        }

        // Anything other than AtSubScreen means we're mid-transition
        // already - ignore duplicate/late firings the same way as above.
        if (_state != State.AtSubScreen) return;

        _state = State.Busy;
        StartCoroutine(ExitLobbyScreen());
    }

    private IEnumerator ExitLobbyScreen()
    {
        // Hide LobbyScreen so the camera move back is actually visible.
        HideView(lobbyScreenCanvas);

        Vector3 fromPos = cameraTransform.position;
        Vector3 fromRot = cameraTransform.rotation.eulerAngles;
        yield return MoveCameraViaWaypoint(fromPos, fromRot, lobbyToMainWaypointPosition, lobbyToMainWaypointRotation, playPosition, playRotation, toWaypointDuration, toSubScreenDuration);

        // Only now, back at the hub, switch the panel back to MainScreen.
        // Also reactivate the Creating/Loading overlays - EnterLobbyScreen
        // deactivated them via HideView, and ViewManager only ever touches
        // their CanvasGroup, never their GameObject, so without this they'd
        // stay inactive (and therefore invisible) the next time Create/Join
        // is clicked.
        ShowView(mainScreenCanvas);
        ShowView(creatingRoomOverlay);
        ShowView(loadingRoomOverlay);
        if (viewManager != null) viewManager.OnRoomLeft();

        _state = State.AtMainScreen;
    }

    // Views are PurrLobby "View" components. SetActive(false) fully removes
    // one from rendering/raycasting - more reliable than only zeroing its
    // CanvasGroup alpha, since it doesn't depend on nothing else in the
    // canvas ignoring that CanvasGroup. Safe to toggle post-startup: Awake()
    // (which caches the CanvasGroup reference ViewManager uses) already ran
    // the first time these objects were activated, and only runs once.
    private void HideView(GameObject viewObject)
    {
        if (viewObject == null) return;
        viewObject.SetActive(false);
    }

    // Reactivate a view right before telling ViewManager to show it - if we
    // hid it earlier with HideView, ViewManager's ShowView<T>() only sets
    // CanvasGroup alpha/interactable and does nothing for an inactive
    // GameObject, so it must be made active again first.
    private void ShowView(GameObject viewObject)
    {
        if (viewObject == null) return;
        viewObject.SetActive(true);
    }

    // Passes through a fixed waypoint first, then eases into the real
    // target - used for every MainScreen <-> sub-screen swing (Browse,
    // Create/Join, Leave), each with its own waypoint set in the Inspector.
    private IEnumerator MoveCameraViaWaypoint(Vector3 fromPos, Vector3 fromRot, Vector3 waypointPos, Vector3 waypointRot, Vector3 toPos, Vector3 toRot, float waypointDuration, float mainDuration)
    {
        yield return MoveCamera(fromPos, fromRot, waypointPos, waypointRot, waypointDuration);
        yield return MoveCamera(waypointPos, waypointRot, toPos, toRot, mainDuration);
    }

    private IEnumerator ApproachPc()
    {
        _state = State.MovingToPc;

        // Camera move and boot-image cycling run independently, each on its
        // own timing, and we wait for both to be done.
        Coroutine cameraCo = StartCoroutine(MoveCamera(spawnPosition, spawnRotation, pcPosition, pcRotation, approachDuration));
        Coroutine bootCo = StartCoroutine(PlayBootSequence());
        yield return cameraCo;
        yield return bootCo;

        _selectedIndex = 0;
        if (menuImages != null && menuImages.Length > 0)
            SetScreenTexture(menuImages[0]);

        _state = State.AtMenu;
    }

    private IEnumerator PlayBootSequence()
    {
        if (bootImages == null || bootImages.Length == 0) yield break;

        if (bootSequenceStartDelay > 0f)
            yield return new WaitForSeconds(bootSequenceStartDelay);

        for (int i = 0; i < bootImages.Length; i++)
        {
            SetScreenTexture(bootImages[i]);
            yield return new WaitForSeconds(bootImageInterval);
        }
    }

    // 0 = Start, 1 = Options, 2 = Quit - matches menuImages order.
    private IEnumerator MoveToTargetSequence(int optionIndex)
    {
        // Always PC -> point 1 first.
        yield return MoveCamera(pcPosition, pcRotation, point1Position, point1Rotation, toPoint1Duration);

        // Repair the monitor first so it can shatter again, then spawn a
        // fresh bullet (replacing the previous one) every time.
        if (explodableMonitor != null)
            explodableMonitor.Repair();

        SpawnPrefab();

        if (waitAfterSpawn > 0f)
            yield return new WaitForSeconds(waitAfterSpawn);

        Vector3 toPos;
        Vector3 toRot;
        switch (optionIndex)
        {
            case 0: toPos = playPosition; toRot = playRotation; break;
            case 1: toPos = optionsPosition; toRot = optionsRotation; break;
            default: toPos = quitPosition; toRot = quitRotation; break;
        }

        yield return MoveCamera(point1Position, point1Rotation, toPos, toRot, toTargetDuration);

        switch (optionIndex)
        {
            case 0:
                ActivateLobbyCanvas();

                if (screenRenderer != null)
                    screenRenderer.gameObject.SetActive(false);

                _state = State.AtMainScreen;
                break;

            case 1:
                ShowOptionsPanel();

                if (screenRenderer != null)
                    screenRenderer.gameObject.SetActive(false);

                _state = State.AtTarget;
                break;

            case 2:
                QuitGame();
                break;
        }
    }

    // Application.Quit() is a no-op while running inside the Editor (Play
    // mode just keeps going) - it only actually closes anything in a real
    // build. Stopping Play mode directly gives the same "the game ended"
    // result when testing.
    private void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private IEnumerator ReturnToMenu()
    {
        _state = State.Busy;

        if (lobbyCanvasRoot != null)
            lobbyCanvasRoot.SetActive(false);

        // Repair puts screenOn back on AND hides/resets the shards - a plain
        // SetActive(true) on the renderer would leave the broken glass showing.
        if (explodableMonitor != null)
            explodableMonitor.Repair();
        else if (screenRenderer != null)
            screenRenderer.gameObject.SetActive(true);

        // Mirror the outward trip: target -> point 1 -> PC.
        Vector3 fromPos = cameraTransform.position;
        Vector3 fromRot = cameraTransform.rotation.eulerAngles;
        yield return MoveCamera(fromPos, fromRot, point1Position, point1Rotation, returnToPoint1Duration);
        yield return MoveCamera(point1Position, point1Rotation, pcPosition, pcRotation, returnToPcDuration);

        _selectedIndex = 0;
        if (menuImages != null && menuImages.Length > 0)
            SetScreenTexture(menuImages[0]);

        _state = State.AtMenu;
    }

    private IEnumerator MoveCamera(Vector3 fromPos, Vector3 fromEuler, Vector3 toPos, Vector3 toEuler, float duration)
    {
        if (cameraTransform == null)
            yield break;

        Quaternion startRot = Quaternion.Euler(fromEuler);
        Quaternion endRot = Quaternion.Euler(toEuler);

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float p = moveCurve.Evaluate(Mathf.Clamp01(t / duration));

            cameraTransform.position = Vector3.LerpUnclamped(fromPos, toPos, p);
            cameraTransform.rotation = Quaternion.SlerpUnclamped(startRot, endRot, p);

            yield return null;
        }

        cameraTransform.position = toPos;
        cameraTransform.rotation = endRot;
    }

    private void SpawnPrefab()
    {
        if (prefabToSpawn == null)
        {
            Debug.LogWarning("[LobbyIntroController] prefabToSpawn is not assigned in the Inspector.");
            return;
        }

        if (spawnPoint == null)
        {
            Debug.LogWarning("[LobbyIntroController] spawnPoint is not assigned in the Inspector.");
            return;
        }

        if (_spawnedInstance != null)
            Destroy(_spawnedInstance);

        _spawnedInstance = Instantiate(prefabToSpawn, spawnPoint.position, spawnPoint.rotation);
    }

    private void SetCameraTo(Vector3 pos, Vector3 euler)
    {
        if (cameraTransform == null) return;
        cameraTransform.position = pos;
        cameraTransform.rotation = Quaternion.Euler(euler);
    }

    private void SetScreenTexture(Texture2D tex)
    {
        if (screenRenderer == null || tex == null) return;
        screenRenderer.GetPropertyBlock(_mpb);
        _mpb.SetTexture(texturePropertyName, tex);
        _mpb.SetTexture(emissionTexturePropertyName, tex);
        screenRenderer.SetPropertyBlock(_mpb);
    }
}
