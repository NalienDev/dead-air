using System.Collections;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RoverManager : NetworkBehaviour
{
    public static RoverManager Instance { get; private set; }

    private readonly List<NetworkIdentity> _cargo = new();

    private GameObject _rover;
    private Sucker _sucker;

    public int CargoCount => _cargo.Count;
    public Sucker Sucker => _sucker;

    protected override void OnSpawned(bool asServer)
    {
        base.OnSpawned(asServer);

        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        SceneManager.sceneLoaded += OnSceneLoaded;
        FindRover();
        if (_cargo.Count > 0)
            ReleaseAllCargo();
    }

    protected override void OnDespawned(bool asServer)
    {
        base.OnDespawned(asServer);
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        FindRover();
        if (_cargo.Count > 0)
            ReleaseAllCargo();
    }

    private void FindRover()
    {
        _rover = GameObject.FindWithTag("Rover");

        if (_rover == null)
        {
            Debug.LogWarning("[RoverManager] No GameObject with tag 'Rover' found in scene.");
            return;
        }

        _sucker = _rover.GetComponentInChildren<Sucker>(includeInactive: true);

        if (_sucker != null)
            _sucker.Initialise(this);
    }

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
            if (identity.TryGetComponent(out EnergyCell _))
                energyCells++;
            else if (identity.TryGetComponent(out BandwidthObject bw))
                bandwidth += bw.BandwidthValue;
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
}