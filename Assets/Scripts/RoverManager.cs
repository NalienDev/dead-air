using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RoverManager : NetworkBehaviour
{
    public static RoverManager Instance { get; private set; }


    // ── Inspector ──────────────────────────────────────────────────────────

    [SerializeField] private string _sceneA = "GameLobby";
    [SerializeField] private string _sceneB = "TestLevel";

    // ── Private refs ───────────────────────────────────────────────────────

    private NetworkedSceneButton _sceneButton;
    private Sucker _sucker;

    // ── Unity lifecycle ────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        _sceneButton = GetComponentInChildren<NetworkedSceneButton>();
        _sucker = GetComponentInChildren<Sucker>();
    }

    private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
    private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

    // ── Scene handling ─────────────────────────────────────────────────────

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RepositionAtSpawn();
        UpdateSceneButton(scene.name);
        _sucker.canSuck = false;
        // Release stored objects now that the destination scene is ready
        if (_sucker != null && _sucker.StoredCount > 0)
            _sucker.ReleaseAll();
    }

    private void RepositionAtSpawn()
    {
        RoverSpawnLocation roverSpawn = FindFirstObjectByType<RoverSpawnLocation>();
        if (roverSpawn != null)
            transform.position = roverSpawn.transform.position;
    }

    private void UpdateSceneButton(string loadedScene)
    {
        if (_sceneButton == null) return;
        _sceneButton.setSceneName(loadedScene == _sceneB ? _sceneA : _sceneB);
    }
}