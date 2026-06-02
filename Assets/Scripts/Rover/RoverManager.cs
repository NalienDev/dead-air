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
        {
            Transform releasePoint = _lobbyDropPoint != null ? _lobbyDropPoint : transform;
            ServerReleaseAllCargo(releasePoint.position, releasePoint.rotation);
        }
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

    [ServerRpc(requireOwnership: false)]
    private void ServerReleaseAllCargo(Vector3 releasePos, Quaternion releaseRot)
    {
        for (int i = _cargo.Count - 1; i >= 0; i--)
        {
            NetworkIdentity identity = _cargo[i];
            if (identity == null) continue;

            if (identity.TryGetComponent(out SuckableObject suckable))
                suckable.EndAttraction();

            identity.transform.SetPositionAndRotation(releasePos, releaseRot);

            if (identity.TryGetComponent(out Rigidbody rb))
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            identity.gameObject.SetActive(true);
            RpcReleaseCargo(identity, releasePos, releaseRot);
        }

        _cargo.Clear();
    }

    [ObserversRpc(runLocally: false)]
    private void RpcReleaseCargo(NetworkIdentity identity, Vector3 releasePos, Quaternion releaseRot)
    {
        if (identity == null) return;
        identity.transform.SetPositionAndRotation(releasePos, releaseRot);
        identity.gameObject.SetActive(true);
    }

    [ServerRpc(requireOwnership: false)]
    public void ServerRequestReturnToBase(Vector3 teleportPos, Quaternion teleportRot)
    {
        GetCargoValues(out int bandwidth, out int energyCells);
        Debug.Log($"[RoverManager] Submitting cargo — bandwidth: {bandwidth}, energy cells: {energyCells}");

        if (QuotaManager.Instance != null)
        {
            QuotaManager.Instance.ServerProcessItems(bandwidth, energyCells);
            QuotaManager.Instance.ServerCheckQuotaAndProceed();
        }

        PlayerManager[] players = FindObjectsByType<PlayerManager>(FindObjectsSortMode.None);
        foreach (var player in players)
        {
            player.TeleportToPosition(teleportPos, teleportRot);
        }

        Transform releasePoint = _lobbyDropPoint != null ? _lobbyDropPoint : transform;
        ServerReleaseAllCargo(releasePoint.position, releasePoint.rotation);
    }
}