using System.Collections;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// DDOL singleton that owns the cargo list across scene transitions and
/// manages the Rover prefab lifecycle (destroy + respawn each scene).
///
/// The Rover prefab is instantiated as a plain GameObject — NOT via
/// PurrNet.Spawn() — because it must be recreated each scene. Any
/// NetworkIdentity components on the rover prefab must be removed; use
/// plain MonoBehaviours on the rover instead.
/// </summary>
public class RoverManager : NetworkBehaviour
{
    // ── Singleton ──────────────────────────────────────────────────────────
    public static RoverManager Instance { get; private set; }

    // ── Inspector ──────────────────────────────────────────────────────────
    [SerializeField] private string _lobbyScene = "GameLobby";
    [SerializeField] private string _gameScene = "TestLevel";
    [SerializeField] private GameObject _roverPrefab;

    // ── Cargo ──────────────────────────────────────────────────────────────
    private readonly List<NetworkIdentity> _cargo = new();

    // ── Rover Refs ─────────────────────────────────────────────────────────
    private GameObject _roverInstance;
    private NetworkedSceneButton _sceneButton;
    private Sucker _sucker;

    // ── Public API ─────────────────────────────────────────────────────────
    public int CargoCount => _cargo.Count;
    public Sucker Sucker => _sucker;

    public void AddCargo(NetworkIdentity identity)
    {
        if (!_cargo.Contains(identity))
            _cargo.Add(identity);
    }

    public void RemoveCargo(NetworkIdentity identity) => _cargo.Remove(identity);

    public void GetCargoValues(out int bandwidth, out int energyCells)
    {
        bandwidth = 0;
        energyCells = 0;

        foreach (NetworkIdentity identity in _cargo)
        {
            if (identity == null) continue;
            // EnergyCell extends GrabbableObject — check it first so it isn't
            // also double-counted as a BandwidthObject if it has that component.
            if (identity.TryGetComponent(out EnergyCell _))
                energyCells++;
            else if (identity.TryGetComponent(out BandwidthObject bw))
                bandwidth += bw.BandwidthValue;
        }
    }

    // ── PurrNet Lifecycle ──────────────────────────────────────────────────
    protected override void OnSpawned(bool asServer)
    {
        base.OnSpawned(asServer);

        if (Instance != null && Instance != this) { Destroy(gameObject); return; }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        StartCoroutine(SetupDeferred(SceneManager.GetActiveScene()));
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    protected override void OnDespawned(bool asServer)
    {
        base.OnDespawned(asServer);
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    // ── Scene Handling ─────────────────────────────────────────────────────
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        StartCoroutine(SetupDeferred(scene));
    }

    private IEnumerator SetupDeferred(Scene scene)
    {
        // Wait one frame for PurrNet to finish spawning scene objects.
        yield return null;

        bool inGameScene = scene.name == _gameScene;

        // Always rebuild the rover — it is scene-local and must not persist.
        DestroyRover();
        SpawnRover(scene);
        RepositionAtSpawn();
        UpdateSceneButton(scene.name);

        // Sucker starts OFF every scene — player must press ActivateVacuumButton.
        _sucker?.SetCanSuck(false);

        // Release cargo into this scene (energy cells return to lobby,
        // bandwidth objects return if quota wasn't fully processed, etc.)
        if (_cargo.Count > 0)
            ReleaseAllCargo();
    }

    // ── Rover Lifecycle ────────────────────────────────────────────────────
    private void SpawnRover(Scene scene)
    {
        if (_roverPrefab == null) { Debug.LogError("[RoverManager] _roverPrefab not assigned."); return; }

        // Instantiate into the active scene so it is scene-bound.
        // Do NOT call DontDestroyOnLoad on it — the rover is intentionally
        // recreated each scene. Remove any NetworkIdentity components from
        // the rover prefab; they cause PurrNet InternalOnSpawn spam when
        // the object is instantiated outside PurrNet's spawn pipeline.
        _roverInstance = Instantiate(_roverPrefab);
        SceneManager.MoveGameObjectToScene(_roverInstance, scene);

        _sceneButton = _roverInstance.GetComponentInChildren<NetworkedSceneButton>(includeInactive: true);
        _sucker = _roverInstance.GetComponentInChildren<Sucker>(includeInactive: true);

        _roverInstance.transform.position = transform.position;

        if (_sucker != null)
            _sucker.Initialise(this);
        else
            Debug.LogWarning("[RoverManager] Sucker not found on rover prefab.");
    }

    private void DestroyRover()
    {
        if (_roverInstance == null) return;
        Destroy(_roverInstance);
        _roverInstance = null;
        _sceneButton = null;
        _sucker = null;
    }

    // ── Cargo Release ──────────────────────────────────────────────────────
    private void ReleaseAllCargo()
    {
        RoverSpawnLocation spawnLocation = FindFirstObjectByType<RoverSpawnLocation>();
        Transform releasePoint = spawnLocation != null ? spawnLocation.transform : transform;

        for (int i = _cargo.Count - 1; i >= 0; i--)
        {
            NetworkIdentity identity = _cargo[i];
            if (identity == null) continue;

            if (identity.TryGetComponent(out SuckableObject suckable))
                suckable.EndAttraction();

            SceneManager.MoveGameObjectToScene(identity.gameObject, SceneManager.GetActiveScene());
            identity.transform.SetPositionAndRotation(releasePoint.position, releasePoint.rotation);

            if (identity.TryGetComponent(out Rigidbody rb))
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            identity.gameObject.SetActive(true);
        }

        _cargo.Clear();
    }

    // ── Helpers ────────────────────────────────────────────────────────────
    private void RepositionAtSpawn()
    {
        RoverSpawnLocation spawn = FindFirstObjectByType<RoverSpawnLocation>();
        if (spawn != null) transform.position = spawn.transform.position;
    }

    private void UpdateSceneButton(string currentScene)
    {
        if (_sceneButton == null) return;
        _sceneButton.setSceneName(currentScene == _gameScene ? _lobbyScene : _gameScene);
    }
}