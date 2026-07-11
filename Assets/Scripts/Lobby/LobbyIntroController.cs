using System.Collections;
using PurrLobby;
using UnityEngine;

/// <summary>
/// Drives the diegetic lobby intro: booting the in-world PC, the menu selector, and the camera moves between menu screens.
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
    [Tooltip("PC to point 1.")]
    [SerializeField] private float toPoint1Duration = 1f;
    [Tooltip("Point 1 to the selected option's target.")]
    [SerializeField] private float toTargetDuration = 1f;
    [Tooltip("Target to point 1, on Escape.")]
    [SerializeField] private float returnToPoint1Duration = 1f;
    [Tooltip("Point 1 to PC, on Escape.")]
    [SerializeField] private float returnToPcDuration = 1f;
    [Tooltip("Wait after the prefab spawns before moving on to the target.")]
    [SerializeField] private float waitAfterSpawn = 2f;
    [Tooltip("Current position to playPosition when a MainScreen button is clicked.")]
    [SerializeField] private float toMainScreenHubDuration = 0.5f;
    [Tooltip("playPosition to the sub-screen target.")]
    [SerializeField] private float toSubScreenDuration = 1f;
    [SerializeField] private AnimationCurve moveCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [Header("Camera - Sub-screen Waypoints")]
    [Tooltip("Waypoint the camera passes through returning Lobby to MainMenu.")]
    [SerializeField] private Vector3 lobbyToMainWaypointPosition = new Vector3(855.736f, -1.827f, -448.031f);
    [SerializeField] private Vector3 lobbyToMainWaypointRotation = new Vector3(-25.265f, -150.633f, -1.073f);
    [Tooltip("Waypoint the camera passes through from MainMenu to a sub-screen.")]
    [SerializeField] private Vector3 mainToSubWaypointPosition = new Vector3(856.42f, -1.65f, -448.27f);
    [SerializeField] private Vector3 mainToSubWaypointRotation = new Vector3(-40.382f, -182.448f, 1.118f);
    [Tooltip("Waypoint the camera passes through returning Browse to MainMenu.")]
    [SerializeField] private Vector3 browseToMainWaypointPosition = new Vector3(856.95f, -1.77f, -448.73f);
    [SerializeField] private Vector3 browseToMainWaypointRotation = new Vector3(-40.705f, -201.044f, 1.655f);
    [Tooltip("Duration of the leg into the waypoint before continuing to the target.")]
    [SerializeField] private float toWaypointDuration = 0.4f;

    [Header("Screen")]
    [SerializeField] private Renderer screenRenderer;
    [Tooltip("Applied to both Base Map and Emission Map so the screen glows.")]
    [SerializeField] private string texturePropertyName = "_BaseMap";
    [SerializeField] private string emissionTexturePropertyName = "_EmissionMap";
    [Tooltip("Boot sequence played while the camera travels to the PC. Last entry stays on screen.")]
    [SerializeField] private Texture2D[] bootImages;
    [Tooltip("Seconds each boot image stays on screen.")]
    [SerializeField] private float bootImageInterval = 0.2f;
    [Tooltip("Delay before the boot image cycling starts.")]
    [SerializeField] private float bootSequenceStartDelay = 1f;
    [Tooltip("Exactly 3 images: 0 = Start, 1 = Options, 2 = Quit.")]
    [SerializeField] private Texture2D[] menuImages;

    [Header("Menu")]
    [SerializeField] private bool wrapSelection = true;
    [SerializeField] private KeyCode confirmKey = KeyCode.Return;
    [SerializeField] private KeyCode confirmKeyAlt = KeyCode.KeypadEnter;

    [Header("PurrNet Lobby Canvas")]
    [Tooltip("The LobbyCanvas root. Activated when Start or Options is confirmed.")]
    [SerializeField] private GameObject lobbyCanvasRoot;
    [Tooltip("Panel shown after Start is confirmed.")]
    [SerializeField] private GameObject mainScreenCanvas;
    [Tooltip("Panel shown after Options is confirmed.")]
    [SerializeField] private GameObject optionsScreenCanvas;
    [Tooltip("Panel shown after Create Lobby or Join is clicked.")]
    [SerializeField] private GameObject lobbyScreenCanvas;
    [Tooltip("Panel shown after Browse is clicked.")]
    [SerializeField] private GameObject browseScreenCanvas;
    [Tooltip("PurrLobby's CreatingRoomOverlay. Must be active for ViewManager to show it.")]
    [SerializeField] private GameObject creatingRoomOverlay;
    [Tooltip("PurrLobby's LoadingRoomOverlay. Must be active for ViewManager to show it.")]
    [SerializeField] private GameObject loadingRoomOverlay;

    [Header("Start Sequence")]
    [Tooltip("Prefab instantiated when an option is confirmed. The previous instance is destroyed first.")]
    [SerializeField] private GameObject prefabToSpawn;
    [Tooltip("Marks where and how the prefab spawns.")]
    [SerializeField] private Transform spawnPoint;
    [Tooltip("The monitor the spawned bullet hits. Repaired before each spawn so it can shatter again.")]
    [SerializeField] private ExplodableMonitor explodableMonitor;

    [Header("PurrLobby ViewManager")]
    [Tooltip("PurrLobby's ViewManager, used to show panels once the matching camera move finishes.")]
    [SerializeField] private ViewManager viewManager;

    private State _state = State.Waiting;
    private int _selectedIndex;
    private GameObject _spawnedInstance;
    private MaterialPropertyBlock _mpb;

    private void Awake()
    {
        _mpb = new MaterialPropertyBlock();
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
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
    }

    private void ConfirmSelection()
    {
        _state = State.Busy;
        StartCoroutine(MoveToTargetSequence(_selectedIndex));
    }

    // Activates the children before the parent so every view's Awake() runs before
    // ViewManager.Start() tries to hide them; otherwise they'd all show up stacked.
    private void ActivateLobbyCanvas()
    {
        if (mainScreenCanvas != null) mainScreenCanvas.SetActive(true);
        if (lobbyScreenCanvas != null) lobbyScreenCanvas.SetActive(true);
        if (browseScreenCanvas != null) browseScreenCanvas.SetActive(true);
        if (creatingRoomOverlay != null) creatingRoomOverlay.SetActive(true);
        if (loadingRoomOverlay != null) loadingRoomOverlay.SetActive(true);
        if (lobbyCanvasRoot != null) lobbyCanvasRoot.SetActive(true);
    }

    private void ShowOptionsPanel()
    {
        ActivateLobbyCanvas();

        // ViewManager auto-shows MainScreen when it first activates; hide it so Options wins.
        HideView(mainScreenCanvas);

        if (optionsScreenCanvas != null) optionsScreenCanvas.SetActive(true);
    }

    // The panel is shown at the end of the coroutine, once the camera has arrived.
    public void OnBrowseClicked() => TryStartSubScreenNav(browsePosition, browseRotation);

    private void TryStartSubScreenNav(Vector3 targetPos, Vector3 targetRot)
    {
        if (_state != State.AtMainScreen) return;

        _state = State.Busy;
        StartCoroutine(MainScreenSubNav(targetPos, targetRot));
    }

    private IEnumerator MainScreenSubNav(Vector3 targetPos, Vector3 targetRot)
    {
        // Hide the full-screen MainScreen overlay so the camera move behind it is visible.
        HideView(mainScreenCanvas);

        // Make sure we're at the hub first (usually a no-op).
        Vector3 fromPos = cameraTransform.position;
        Vector3 fromRot = cameraTransform.rotation.eulerAngles;
        yield return MoveCamera(fromPos, fromRot, playPosition, playRotation, toMainScreenHubDuration);

        yield return MoveCameraViaWaypoint(playPosition, playRotation, mainToSubWaypointPosition, mainToSubWaypointRotation, targetPos, targetRot, toWaypointDuration, toSubScreenDuration);

        // Only now, with the camera in place, show BrowseScreen.
        ShowView(browseScreenCanvas);
        if (viewManager != null) viewManager.OnBrowseClicked();

        _state = State.AtSubScreen;
    }

    // Moves the camera back to playPosition before showing MainScreen.
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

    // Fired by LobbyManager once the lobby genuinely exists, so the camera move happens
    // at the right time instead of racing the network call.
    public void OnRoomJoinedFromLobby(Lobby lobby)
    {
        // Already in the lobby: this is just a later data update, panel already showing.
        if (_state == State.AtSubScreen)
        {
            if (viewManager != null) viewManager.OnRoomJoined();
            return;
        }

        // Any other state means we're mid-transition; ignore duplicate or late firings.
        if (_state != State.AtMainScreen) return;

        _state = State.Busy;
        StartCoroutine(EnterLobbyScreen());
    }

    private IEnumerator EnterLobbyScreen()
    {
        // Hide everything so the camera move travels with no UI on screen.
        HideView(mainScreenCanvas);
        HideView(creatingRoomOverlay);
        HideView(loadingRoomOverlay);

        Vector3 fromPos = cameraTransform.position;
        Vector3 fromRot = cameraTransform.rotation.eulerAngles;
        yield return MoveCamera(fromPos, fromRot, playPosition, playRotation, toMainScreenHubDuration);
        yield return MoveCameraViaWaypoint(playPosition, playRotation, mainToSubWaypointPosition, mainToSubWaypointRotation, createJoinPosition, createJoinRotation, toWaypointDuration, toSubScreenDuration);

        // Only now, with the camera in place, show LobbyScreen.
        ShowView(lobbyScreenCanvas);
        if (viewManager != null) viewManager.OnRoomJoined();

        _state = State.AtSubScreen;
    }

    // Fired by LobbyManager on leaving, so the camera returns before MainScreen shows.
    public void OnRoomLeftFromLobby()
    {
        // Already back on MainScreen: nothing to animate, just relay.
        if (_state == State.AtMainScreen)
        {
            if (viewManager != null) viewManager.OnRoomLeft();
            return;
        }

        // Any other state means we're mid-transition; ignore duplicate or late firings.
        if (_state != State.AtSubScreen) return;

        _state = State.Busy;
        StartCoroutine(ExitLobbyScreen());
    }

    private IEnumerator ExitLobbyScreen()
    {
        // Hide LobbyScreen so the camera move back is visible.
        HideView(lobbyScreenCanvas);

        Vector3 fromPos = cameraTransform.position;
        Vector3 fromRot = cameraTransform.rotation.eulerAngles;
        yield return MoveCameraViaWaypoint(fromPos, fromRot, lobbyToMainWaypointPosition, lobbyToMainWaypointRotation, playPosition, playRotation, toWaypointDuration, toSubScreenDuration);

        // Back at the hub, switch to MainScreen and reactivate the overlays HideView
        // disabled, so they're available the next time Create/Join is clicked.
        ShowView(mainScreenCanvas);
        ShowView(creatingRoomOverlay);
        ShowView(loadingRoomOverlay);
        if (viewManager != null) viewManager.OnRoomLeft();

        _state = State.AtMainScreen;
    }

    // SetActive(false) fully removes a view from rendering, more reliable than zeroing its alpha.
    private void HideView(GameObject viewObject)
    {
        if (viewObject == null) return;
        viewObject.SetActive(false);
    }

    // Reactivate a view before telling ViewManager to show it, since ShowView only sets its CanvasGroup.
    private void ShowView(GameObject viewObject)
    {
        if (viewObject == null) return;
        viewObject.SetActive(true);
    }

    // Passes through a fixed waypoint first, then eases into the real target.
    private IEnumerator MoveCameraViaWaypoint(Vector3 fromPos, Vector3 fromRot, Vector3 waypointPos, Vector3 waypointRot, Vector3 toPos, Vector3 toRot, float waypointDuration, float mainDuration)
    {
        yield return MoveCamera(fromPos, fromRot, waypointPos, waypointRot, waypointDuration);
        yield return MoveCamera(waypointPos, waypointRot, toPos, toRot, mainDuration);
    }

    private IEnumerator ApproachPc()
    {
        _state = State.MovingToPc;

        // Camera move and boot-image cycling run independently; wait for both.
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

    // optionIndex: 0 = Start, 1 = Options, 2 = Quit, matching menuImages order.
    private IEnumerator MoveToTargetSequence(int optionIndex)
    {
        // Always PC to point 1 first.
        yield return MoveCamera(pcPosition, pcRotation, point1Position, point1Rotation, toPoint1Duration);

        // Repair the monitor so it can shatter again, then spawn a fresh bullet.
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

    // Application.Quit() is a no-op in the Editor, so stop Play mode instead when testing.
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

        // Repair restores the screen and resets the shards; a plain SetActive would leave broken glass showing.
        if (explodableMonitor != null)
            explodableMonitor.Repair();
        else if (screenRenderer != null)
            screenRenderer.gameObject.SetActive(true);

        // Mirror the outward trip: target to point 1 to PC.
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
