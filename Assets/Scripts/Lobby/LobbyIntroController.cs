using System.Collections;
using UnityEngine;

/// <summary>
/// Drives the lobby intro sequence:
///   1) Camera sits at the spawn point, screen shows the first boot image.
///   2) Enter -> camera lerps to the PC point while the screen cycles through
///      the boot image list (its own pace, set by bootImageInterval), ending
///      on the last frame.
///   3) Up/Down cycles the 3 menu images (Start / Options / Quit selector).
///   4) Enter on "Start" -> camera moves to the transition point, spawns the
///      prefab at spawnPoint immediately, waits waitAfterSpawn seconds, then
///      moves on to the final point. Once it has landed there, the PurrNet
///      canvas is enabled.
///
/// All positions/images/objects are wired up in the Inspector - nothing is
/// found by name at runtime, so it doesn't matter how the scene hierarchy
/// ends up being organised.
/// </summary>
public class LobbyIntroController : MonoBehaviour
{
    private enum State { Waiting, MovingToPc, AtMenu, Transitioning, Done }

    [Header("Camera")]
    [Tooltip("Camera that gets moved. Defaults to Camera.main if left empty.")]
    [SerializeField] private Transform cameraTransform;

    [Header("Camera - Spawn")]
    [SerializeField] private Vector3 spawnPosition = new Vector3(856.69f, -1.46f, -444.25f);
    [SerializeField] private Vector3 spawnRotation = new Vector3(19.062f, -169.424f, -2.213f);

    [Header("Camera - PC")]
    [SerializeField] private Vector3 pcPosition = new Vector3(856.432f, -2.217f, -448.21f);
    [SerializeField] private Vector3 pcRotation = new Vector3(6.985f, -181.619f, 0.858f);

    [Header("Camera - Transition (first pull-back after Start is confirmed)")]
    [SerializeField] private Vector3 transitionPosition = new Vector3(856.085f, -2.184f, -447.751f);
    [SerializeField] private Vector3 transitionRotation = new Vector3(6.974f, -196.364f, -0.943f);

    [Header("Camera - Final (reached after the wait, right before canvas/move)")]
    [SerializeField] private Vector3 finalPosition = new Vector3(860.97f, -2.41f, -448.87f);
    [SerializeField] private Vector3 finalRotation = new Vector3(1.506f, -266.344f, -6.875f);

    [Header("Camera - Timing")]
    [SerializeField] private float approachDuration = 2f;
    [SerializeField] private float transitionDuration = 1f;
    [Tooltip("Time to wait after the prefab spawns, before the camera moves on to the final point.")]
    [SerializeField] private float waitAfterSpawn = 2f;
    [SerializeField] private float finalTransitionDuration = 1f;
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

    [Header("Start Sequence (after Start is confirmed)")]
    [SerializeField] private GameObject purrNetCanvas;
    [Tooltip("Prefab instantiated once the camera has landed at the final point.")]
    [SerializeField] private GameObject prefabToSpawn;
    [Tooltip("Empty GameObject marking where/how the prefab should spawn (position + rotation).")]
    [SerializeField] private Transform spawnPoint;

    private State _state = State.Waiting;
    private int _selectedIndex;
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
        // 0 = Start, 1 = Options, 2 = Quit - matches menuImages order.
        switch (_selectedIndex)
        {
            case 0:
                _state = State.Transitioning;
                StartCoroutine(BeginGameSequence());
                break;
            case 1:
                OnOptionsSelected();
                break;
            case 2:
                Application.Quit();
                break;
        }
    }

    private void OnOptionsSelected()
    {
        Debug.Log("[LobbyIntroController] Options selected - hook up the options panel here.");
    }

    private IEnumerator ApproachPc()
    {
        _state = State.MovingToPc;

        // Camera move and boot-image cycling now run independently, each on
        // its own timing, and we wait for both to be done.
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

    private IEnumerator BeginGameSequence()
    {
        // 1) Move to the transition point.
        yield return MoveCamera(pcPosition, pcRotation, transitionPosition, transitionRotation, transitionDuration);

        // 2) Spawn the prefab as soon as it's positioned there.
        SpawnPrefab();

        // 3) Wait a bit before moving on.
        if (waitAfterSpawn > 0f)
            yield return new WaitForSeconds(waitAfterSpawn);

        // 4) Move on to the final point.
        yield return MoveCamera(transitionPosition, transitionRotation, finalPosition, finalRotation, finalTransitionDuration);

        // 5) Activate the canvas.
        if (purrNetCanvas != null)
            purrNetCanvas.SetActive(true);
        else
            Debug.LogWarning("[LobbyIntroController] purrNetCanvas is not assigned in the Inspector.");

        _state = State.Done;
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

        Instantiate(prefabToSpawn, spawnPoint.position, spawnPoint.rotation);
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
