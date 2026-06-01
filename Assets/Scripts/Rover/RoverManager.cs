using System.Collections;
using System.Collections.Generic;
using PurrNet;
using UnityEngine;
using UnityEngine.SceneManagement;

public class RoverManager : NetworkBehaviour
{
    public static RoverManager Instance { get; private set; }

    [SerializeField] private Transform _lobbyDropPoint;
    [SerializeField] private Sucker _expeditionSucker;
    [SerializeField] private Sucker _lobbySucker;

    private readonly List<NetworkIdentity> _cargo = new();

    public int CargoCount => _cargo.Count;
    public Sucker Sucker => _expeditionSucker;

    protected override void OnSpawned(bool asServer)
    {
        base.OnSpawned(asServer);

        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        if (_expeditionSucker != null)
            _expeditionSucker.Initialise(this);

        if (_lobbySucker != null)
            _lobbySucker.Initialise(this);

        if (_cargo.Count > 0)
            ReleaseAllCargo();
    }

    protected override void OnDespawned(bool asServer)
    {
        base.OnDespawned(asServer);
        if (Instance == this) Instance = null;
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
        Transform releasePoint = _lobbyDropPoint != null ? _lobbyDropPoint : transform;

        for (int i = _cargo.Count - 1; i >= 0; i--)
        {
            NetworkIdentity identity = _cargo[i];
            if (identity == null) continue;

            if (identity.TryGetComponent(out SuckableObject suckable))
                suckable.EndAttraction();

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

    public void ReturnToLobby(Transform teleportPoint)
    {
        if (teleportPoint != null)
        {
            PlayerManager[] players = FindObjectsByType<PlayerManager>(FindObjectsSortMode.None);
            foreach (var player in players)
            {
                player.transform.SetPositionAndRotation(teleportPoint.position, teleportPoint.rotation);
            }
        }

        ReleaseAllCargo();
    }
}