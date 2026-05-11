using System.Collections.Generic;
using PurrNet;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Singleton that persists across scenes (DontDestroyOnLoad).
/// Owns the list of sucked-up objects so it survives scene transitions.
///
/// Flow per scene transition:
///   PurrNet loads new scene → despawns old networked objects → spawns
///   RoverManager.OnSpawned fires → we process cargo, release it, build
///   a fresh rover for the new scene.
///
/// The Rover prefab is NOT networked at the root level — it is a plain
/// GameObject child of RoverManager so PurrNet doesn't fight us over it
/// during scene changes.
/// </summary>
public class RoverManager : NetworkBehaviour
{
    // ── Singleton ──────────────────────────────────────────────────────────
    public static RoverManager Instance { get; private set; }

    // ── Inspector ──────────────────────────────────────────────────────────
    [SerializeField] private string _lobbyScene = "GameLobby";
    [SerializeField] private string _gameScene = "TestLevel";
    [SerializeField] private GameObject _roverPrefab;

    // ── Stored Cargo ───────────────────────────────────────────────────────
    // Plain List on a DDOL object — survives every scene transition.
    // Only the server mutates this; no SyncList required.
    private readonly List<NetworkIdentity> _cargo = new();

    // ── Private Refs ───────────────────────────────────────────────────────
    private GameObject _roverInstance;
    private NetworkedSceneButton _sceneButton;
    private Sucker _sucker;

    // ── Public API ─────────────────────────────────────────────────────────
    public int CargoCount => _cargo.Count;

    /// <summary>Called by Sucker when an object enters its trigger.</summary>
    public void AddCargo(NetworkIdentity identity)
    {
        if (!_cargo.Contains(identity))
            _cargo.Add(identity);
    }

    /// <summary>Called by Sucker when an object is manually ejected.</summary>
    public void RemoveCargo(NetworkIdentity identity) => _cargo.Remove(identity);

    public void GetCargoValues(out int bandwidth, out int energyCells)
    {
        bandwidth = 0;
        energyCells = 0;

        foreach (NetworkIdentity identity in _cargo)
        {
            if (identity == null) continue;

            if (identity.TryGetComponent(out BandwidthObject bw))
                bandwidth += bw.BandwidthValue;

            if (identity.TryGetComponent(out EnergyCell _))
                energyCells++;
        }
    }

    // ── PurrNet Lifecycle ──────────────────────────────────────────────────
    /// <summary>
    /// Fired by PurrNet each time this NetworkBehaviour is spawned — which
    /// happens once per scene load because PurrNet re-spawns DDOL network
    /// objects into each new scene context.
    /// This is the single entry-point for all per-scene setup.
    /// </summary>
    protected override void OnSpawned(bool asServer)
    {
        base.OnSpawned(asServer);

        // ── Singleton guard ────────────────────────────────────────────────
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        // ── Determine current scene ────────────────────────────────────────
        Scene currentScene = SceneManager.GetActiveScene();
        bool inLobby = currentScene.name == _lobbyScene;
        bool inGame = currentScene.name == _gameScene;

        Debug.Log($"[RoverManager] OnSpawned — scene: {currentScene.name}, cargo: {_cargo.Count}");

        // ── Step 1: Process cargo if we just returned to the lobby ─────────
        if (inLobby && _cargo.Count > 0)
            ProcessCargoOnLobbyReturn();

        // ── Step 2: Release held objects into the now-active scene ─────────
        if (_cargo.Count > 0)
            ReleaseAllCargo();

        // ── Step 3: Tear down the old rover (from the previous scene) ──────
        DestroyRover();

        // ── Step 4: Build a fresh rover for this scene ─────────────────────
        SpawnRover();

        // ── Step 5: Position rover at the scene's designated spawn point ───
        RepositionAtSpawn();

        // ── Step 6: Wire up the scene-switch button ────────────────────────
        UpdateSceneButton(currentScene.name);

        // ── Step 7: Sucker only active during the game scene ───────────────
        if (_sucker != null)
            _sucker.SetCanSuck(inGame);
    }

    protected override void OnDespawned(bool asServer)
    {
        base.OnDespawned(asServer);

        if (Instance == this)
            Instance = null;
    }

    // ── Rover Lifecycle ────────────────────────────────────────────────────
    private void SpawnRover()
    {
        if (_roverPrefab == null)
        {
            Debug.LogError("[RoverManager] _roverPrefab is not assigned in the Inspector.");
            return;
        }

        _roverInstance = Instantiate(_roverPrefab, transform);

        _sceneButton = _roverInstance.GetComponentInChildren<NetworkedSceneButton>(includeInactive: true);
        _sucker = _roverInstance.GetComponentInChildren<Sucker>(includeInactive: true);

        if (_sucker != null)
            _sucker.Initialise(this);
        else
            Debug.LogWarning("[RoverManager] No Sucker found on rover prefab.");
    }

    private void DestroyRover()
    {
        if (_roverInstance == null) return;

        Destroy(_roverInstance);
        _roverInstance = null;
        _sceneButton = null;
        _sucker = null;
    }

    // ── Cargo Helpers ──────────────────────────────────────────────────────
    private void ProcessCargoOnLobbyReturn()
    {
        GetCargoValues(out int bw, out int ec);
        Debug.Log($"[RoverManager] Cargo tallied — bandwidth: {bw}, energy cells: {ec}");

        if (QuotaManager.Instance != null)
        {
            QuotaManager.Instance.ServerProcessItems(bw, ec);
            QuotaManager.Instance.ServerCheckQuotaAndProceed();
        }
        else
        {
            Debug.LogWarning("[RoverManager] QuotaManager.Instance is null — quota not processed.");
        }
    }

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

    // ── Scene Helpers ──────────────────────────────────────────────────────
    private void RepositionAtSpawn()
    {
        RoverSpawnLocation spawn = FindFirstObjectByType<RoverSpawnLocation>();
        if (spawn != null)
            transform.position = spawn.transform.position;
    }

    private void UpdateSceneButton(string currentScene)
    {
        if (_sceneButton == null) return;
        _sceneButton.setSceneName(currentScene == _gameScene ? _lobbyScene : _gameScene);
    }
}