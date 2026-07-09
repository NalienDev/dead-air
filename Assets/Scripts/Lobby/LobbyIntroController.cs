using System.Collections;
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
///        - Start   -> playPosition/playRotation, then the PurrNet canvas is enabled
///                     and screenOn is turned off.
///        - Options -> optionsPosition/optionsRotation, then OnOptionsSelected() runs
///                     and screenOn is turned off.
///        - Quit    -> quitPosition/quitRotation, then Application.Quit().
///   5) Escape while resting at the Start/Options target reverses the same
///      trip: screenOn is turned back on, and the camera moves target ->
///      point 1 -> PC, landing back at the menu.
///
/// All positions/images/objects are wired up in the Inspector - nothing is
/// found by name at runtime, so it doesn't matter how the scene hierarchy
/// ends up being organised.
/// </summary>
public class LobbyIntroController : MonoBehaviour
{
    private enum State { Waiting, MovingToPc, AtMenu, Busy, AtTarget }

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

    [Header("Camera - Start/Play target")]
    [SerializeField] private Vector3 playPosition = new Vector3(856.472f, -0.676f, -449.417f);
    [SerializeField] private Vector3 playRotation = new Vector3(-40.382f, -182.448f, 1.118f);

    [Header("Camera - Options target")]
    [SerializeField] private Vector3 optionsPosition = new Vector3(858.273f, -0.653f, -448.815f);
    [SerializeField] private Vector3 optionsRotation = new Vector3(-36.216f, -256.644f, 0.595f);

    [Header("Camera - Quit target")]
    [SerializeField] private Vector3 quitPosition = new Vector3(860.97f, -2.41f, -448.87f);
    [SerializeField] private Vector3 quitRotation = new Vector3(1.506f, -266.344f, -6.875f);

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
    [SerializeField] private AnimationCurve moveCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

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

    [Header("Start Sequence (first selection only)")]
    [SerializeField] private GameObject purrNetCanvas;
    [Tooltip("Prefab instantiated every time an option is confirmed. The previous instance is destroyed first.")]
    [SerializeField] private GameObject prefabToSpawn;
    [Tooltip("Empty GameObject marking where/how the prefab should spawn (position + rotation).")]
    [SerializeField] private Transform spawnPoint;
    [Tooltip("The monitor the spawned bullet hits. Repaired right before each spawn so it can shatter again every time.")]
    [SerializeField] private ExplodableMonitor explodableMonitor;

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

    private void OnOptionsSelected()
    {
        Debug.Log("[LobbyIntroController] Options selected - hook up the options panel here.");
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
                if (purrNetCanvas != null)
                    purrNetCanvas.SetActive(true);
                else
                    Debug.LogWarning("[LobbyIntroController] purrNetCanvas is not assigned in the Inspector.");

                if (screenRenderer != null)
                    screenRenderer.gameObject.SetActive(false);

                _state = State.AtTarget;
                break;

            case 1:
                OnOptionsSelected();

                if (screenRenderer != null)
                    screenRenderer.gameObject.SetActive(false);

                _state = State.AtTarget;
                break;

            case 2:
                Application.Quit();
                break;
        }
    }

    private IEnumerator ReturnToMenu()
    {
        _state = State.Busy;

        if (purrNetCanvas != null)
            purrNetCanvas.SetActive(false);

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
