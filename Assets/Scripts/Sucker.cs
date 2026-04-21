using System.Collections.Generic;
using PurrNet;
using PurrNet.Modules;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Collects networked objects on trigger enter and holds them across scene loads
/// by moving them into DontDestroyOnLoad space.
/// Uses SyncList of NetworkIdentity so PurrNet tracks the references network-side.
/// </summary>
public class Sucker : NetworkBehaviour
{
    // ── Inspector ──────────────────────────────────────────────────────────

    public Transform objectSpawnTransform;
    public bool canSuck = false;

    // ── Networked State ────────────────────────────────────────────────────

    /// <summary>
    /// Synced list of stored networked identities.
    /// PurrNet resolves these across scenes automatically.
    /// </summary>
    private readonly SyncList<NetworkIdentity> _storedObjects = new();
    private SuctionZone _suctionZone;

    private void Start()
    {
        _suctionZone = GetComponentInChildren<SuctionZone>();
    }

    private void Update()
    {
        if (!canSuck)
        {
            _suctionZone.gameObject.SetActive(false);
        }
        else
        {
            _suctionZone.gameObject.SetActive(true);
        }
    }

    // ── Public API ─────────────────────────────────────────────────────────

    public int StoredCount => _storedObjects.Count;

    /// <summary>
    /// Releases all stored objects into the active scene at the spawn point.
    /// Safe to call from OnSceneLoaded after the destination scene is ready.
    /// </summary>
    public void ReleaseAll()
    {
        for (int i = _storedObjects.Count - 1; i >= 0; i--)
        {
            NetworkIdentity identity = _storedObjects[i];
            if (identity == null) continue;

            if (identity.TryGetComponent(out SuckableObject suckable))
                suckable.EndAttraction();

            SceneManager.MoveGameObjectToScene(
                identity.gameObject,
                SceneManager.GetActiveScene()
            );

            identity.transform.SetPositionAndRotation(
                objectSpawnTransform.position,
                objectSpawnTransform.rotation
            );

            if (identity.TryGetComponent(out Rigidbody rb))
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            identity.gameObject.SetActive(true);
        }

        _storedObjects.Clear();
    }

    // ── Trigger ────────────────────────────────────────────────────────────

    private void OnTriggerEnter(Collider other)
    {
        if (!canSuck) return;
        if (!other.TryGetComponent(out NetworkIdentity identity)) return;
        if (!identity.isSpawned) return;

        if (other.TryGetComponent(out SuckableObject suckable))
            suckable.EndAttraction();

        DontDestroyOnLoad(identity.gameObject);
        _storedObjects.Add(identity);
        identity.gameObject.SetActive(false);
    }
}