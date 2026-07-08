using System.Collections.Generic;
using PurrNet;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The Echo. Server-authoritative stalker that lures players with stolen voices.
///
/// Loop: stays hidden on a cooldown → picks a random target inside the dungeon →
/// waits until they're alone → spawns nearby on the NavMesh, out of everyone's
/// sight → repeats the voice of a DIFFERENT player → if someone comes close (with
/// line of sight) it screeches and chases them, hits for damage and vanishes.
/// If nobody takes the bait it despawns and returns later for another target.
///
/// Needs on this GameObject: NavMeshAgent, NetworkTransform, a child model root.
/// The dungeon NavMesh is baked at runtime by DungeonNavMeshBaker.
/// </summary>
public class TheEchoAI : NetworkBehaviour
{
    private enum State { Hidden, Lure, Chase }

    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private AudioSource _audioSource;   // voice playback
    [SerializeField] private GameObject _modelRoot;      // visuals + colliders, toggled on spawn/despawn
    [SerializeField] private Animator _animator;         // optional; gets bool "IsChasing"

    [Header("Sounds")]
    [SerializeField] private AudioClip _breathingSound;  // loop while lurking
    [SerializeField] private AudioClip _screechSound;    // one-shot when the chase starts
    [SerializeField] private AudioClip _runningSound;    // loop while chasing
    [SerializeField] private AudioClip _scratchSound;    // one-shot on hit

    [Header("Spawning")]
    [SerializeField] private float _respawnCooldown = 60f;
    [SerializeField] private float _minSpawnDistance = 8f;
    [SerializeField] private float _maxSpawnDistance = 14f;
    [Tooltip("Target only counts as alone when no other player is within this range.")]
    [SerializeField] private float _aloneRadius = 20f;
    [Tooltip("A spawn point inside this view cone of any player is rejected.")]
    [SerializeField] private float _playerViewAngle = 110f;

    [Header("Luring")]
    [Tooltip("A player this close, with line of sight, triggers the chase.")]
    [SerializeField] private float _triggerRadius = 5f;
    [Tooltip("Seconds after the voice finishes before giving up and despawning.")]
    [SerializeField] private float _lureTimeout = 10f;
    [Tooltip("Repeat the target's own voice instead of another player's.")]
    [SerializeField] private bool _repeatTargetVoice = false;

    [Header("Chasing")]
    [SerializeField] private float _attackRange = 1.5f;
    [SerializeField] private int _attackDamage = 20;
    [Tooltip("Give up if the chase lasts longer than this.")]
    [SerializeField] private float _chaseTimeout = 15f;

    [Header("Vision")]
    [Tooltip("Layers that block sight (walls/rooms). Do NOT include the Player layer.")]
    [SerializeField] private LayerMask _visionBlockers = Physics.DefaultRaycastLayers;

    private static readonly int AnimIsChasing = Animator.StringToHash("IsChasing");
    private const float HeadHeight = 1.6f;

    // ── State (server) ─────────────────────────────────────────────────────

    private NavMeshAgent _agent;
    private AudioSource _loopSource;

    private State _state = State.Hidden;
    private PlayerManager _target;       // player we spawn next to
    private PlayerManager _chaseTarget;  // player who took the bait
    private float _nextSpawnTime;
    private float _lureDeadline;
    private float _chaseDeadline;
    private float _nextLogTime;
    private Vector3 _restPosition;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.enabled = false; // no NavMesh exists until the dungeon bakes

        _restPosition = transform.position; // parked here while hidden

        // The agent drives all movement — a live rigidbody would just add gravity
        // and make the hidden Echo fall forever.
        if (TryGetComponent(out Rigidbody rb))
            rb.isKinematic = true;

        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();

        _loopSource = gameObject.AddComponent<AudioSource>();
        _loopSource.loop = true;
        _loopSource.spatialBlend = 1f;

        if (_modelRoot != null)
            _modelRoot.SetActive(false);
    }

    protected override void OnSpawned(bool asServer)
    {
        if (!isServer) return;

        _nextSpawnTime = Time.time + _respawnCooldown;
        Debug.Log($"[TheEchoAI] Server ready. First appearance allowed in {_respawnCooldown:F0}s.");

        // Map changed mid-appearance — retreat and wait for the new layout.
        if (DungeonGenerator.Instance != null)
            DungeonGenerator.Instance.OnGenerated += Despawn;
    }

    protected override void OnDespawned(bool asServer)
    {
        if (DungeonGenerator.Instance != null)
            DungeonGenerator.Instance.OnGenerated -= Despawn;
    }

    private void Update()
    {
        if (!isServer) return;

        switch (_state)
        {
            case State.Hidden: TickHidden(); break;
            case State.Lure: TickLure(); break;
            case State.Chase: TickChase(); break;
        }
    }

    // ── Hidden: wait out the cooldown, pick a target, appear ───────────────

    private void TickHidden()
    {
        if (Time.time < _nextSpawnTime) return;

        if (DungeonGenerator.Instance == null || !DungeonGenerator.Instance.IsGenerated())
        {
            Status("waiting — dungeon not generated yet.");
            return;
        }

        if (!IsValidTarget(_target))
            _target = PickRandomTarget();

        if (_target == null)
        {
            Status("waiting — no alive players flagged as inside the dungeon (isInsideDungeon).");
            return;
        }

        if (!IsAlone(_target))
        {
            Status($"waiting — target '{_target.name}' is not alone.");
            return;
        }

        if (!TryFindHiddenSpawnPoint(_target, out Vector3 spawnPos))
        {
            Status($"waiting — no hidden NavMesh spawn point found near '{_target.name}' " +
                   "(no NavMesh in range, or every candidate is visible).");
            return;
        }

        Debug.Log($"[TheEchoAI] Appearing near '{_target.name}' at {spawnPos}.");

        // Move BEFORE enabling — the agent can only be created on top of the
        // NavMesh, and while hidden we're parked far away from it.
        transform.position = spawnPos;
        _agent.enabled = true;

        if (!_agent.isOnNavMesh)
        {
            _agent.enabled = false;
            Status("waiting — spawn point found but the agent could not attach to the NavMesh there.");
            return;
        }

        _agent.Warp(spawnPos);

        // Face the target so the model isn't staring into its corner.
        Vector3 look = _target.transform.position - spawnPos;
        look.y = 0f;
        if (look != Vector3.zero) transform.rotation = Quaternion.LookRotation(look);

        _state = State.Lure;
        RpcSetVisible(true);

        float voiceLength = PlayStolenVoice();
        _lureDeadline = Time.time + voiceLength + _lureTimeout;
    }

    // ── Lure: wait for someone to take the bait ────────────────────────────

    private void TickLure()
    {
        foreach (PlayerManager player in GetDungeonPlayers())
        {
            bool close = (player.transform.position - transform.position).sqrMagnitude
                         <= _triggerRadius * _triggerRadius;
            if (close && HasLineOfSight(player))
            {
                StartChase(player);
                return;
            }
        }

        if (Time.time >= _lureDeadline)
            Despawn();
    }

    private void StartChase(PlayerManager player)
    {
        Debug.Log($"[TheEchoAI] '{player.name}' took the bait — chasing.");
        _chaseTarget = player;
        _state = State.Chase;
        _chaseDeadline = Time.time + _chaseTimeout;
        RpcSetChasing(true);
    }

    // ── Chase: run the player down, hit once, vanish ───────────────────────

    private void TickChase()
    {
        if (!IsValidTarget(_chaseTarget) || Time.time >= _chaseDeadline)
        {
            Despawn();
            return;
        }

        _agent.SetDestination(_chaseTarget.transform.position);

        float distSq = (_chaseTarget.transform.position - transform.position).sqrMagnitude;
        if (distSq <= _attackRange * _attackRange)
        {
            Debug.Log($"[TheEchoAI] Hit '{_chaseTarget.name}' for {_attackDamage}.");
            _chaseTarget.Damage(_attackDamage);
            RpcPlayScratch();
            Despawn();
        }
    }

    private void Despawn()
    {
        if (_state == State.Hidden) return;

        Debug.Log($"[TheEchoAI] Despawning ({_state}). Back in {_respawnCooldown:F0}s.");

        _state = State.Hidden;
        _target = null;
        _chaseTarget = null;
        _nextSpawnTime = Time.time + _respawnCooldown;

        if (_agent.enabled) _agent.ResetPath();
        _agent.enabled = false;

        transform.position = _restPosition; // park while hidden

        RpcSetChasing(false);
        RpcSetVisible(false);
    }

    // ── Targeting helpers (server) ─────────────────────────────────────────

    private static bool IsValidTarget(PlayerManager p)
        => p != null && !p.IsDead && p.IsInsideDungeon();

    private List<PlayerManager> GetDungeonPlayers()
    {
        var players = new List<PlayerManager>();
        foreach (PlayerManager p in FindObjectsByType<PlayerManager>(FindObjectsSortMode.None))
            if (IsValidTarget(p))
                players.Add(p);
        return players;
    }

    private PlayerManager PickRandomTarget()
    {
        List<PlayerManager> players = GetDungeonPlayers();
        return players.Count > 0 ? players[Random.Range(0, players.Count)] : null;
    }

    private bool IsAlone(PlayerManager target)
    {
        foreach (PlayerManager other in GetDungeonPlayers())
        {
            if (other == target) continue;
            if ((other.transform.position - target.transform.position).sqrMagnitude
                <= _aloneRadius * _aloneRadius)
                return false;
        }
        return true;
    }

    private bool TryFindHiddenSpawnPoint(PlayerManager target, out Vector3 result)
    {
        result = default;

        // The target's own position on the NavMesh — every candidate must be able
        // to actually walk here, or the Echo spawns on a disconnected island
        // (city ground, prop tops) and gets stuck behind "invisible walls".
        if (!NavMesh.SamplePosition(target.transform.position, out NavMeshHit targetHit, 2f, NavMesh.AllAreas))
            return false;

        NavMeshPath path = new NavMeshPath();

        for (int i = 0; i < 24; i++)
        {
            Vector2 dir = Random.insideUnitCircle.normalized;
            float dist = Random.Range(_minSpawnDistance, _maxSpawnDistance);
            Vector3 candidate = target.transform.position + new Vector3(dir.x, 0f, dir.y) * dist;

            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                continue;

            bool reachable = NavMesh.CalculatePath(hit.position, targetHit.position, NavMesh.AllAreas, path)
                             && path.status == NavMeshPathStatus.PathComplete;
            if (!reachable) continue;

            if (!VisibleToAnyPlayer(hit.position))
            {
                result = hit.position;
                return true;
            }
        }

        return false;
    }

    private bool VisibleToAnyPlayer(Vector3 point)
    {
        Vector3 pointHead = point + Vector3.up * HeadHeight;

        foreach (PlayerManager player in GetDungeonPlayers())
        {
            Vector3 playerHead = player.transform.position + Vector3.up * HeadHeight;
            Vector3 toPoint = pointHead - playerHead;

            bool inViewCone = Vector3.Angle(player.transform.forward, toPoint) < _playerViewAngle * 0.5f;
            if (inViewCone && !Physics.Linecast(playerHead, pointHead, _visionBlockers))
                return true;
        }
        return false;
    }

    private bool HasLineOfSight(PlayerManager player)
    {
        Vector3 echoHead = transform.position + Vector3.up * HeadHeight;
        Vector3 playerHead = player.transform.position + Vector3.up * HeadHeight;
        return !Physics.Linecast(echoHead, playerHead, _visionBlockers);
    }

    // ── Stolen voice playback ──────────────────────────────────────────────

    /// <summary>
    /// Plays a stolen voice clip — the target's own if _repeatTargetVoice is set,
    /// otherwise another player's. Returns the clip length.
    /// </summary>
    private float PlayStolenVoice()
    {
        VoiceRecordingStore store = VoiceRecordingStore.Instance;
        if (store == null || store.Count == 0) return 0f;

        CapturedVoiceClip clip = null;

        if (_repeatTargetVoice)
        {
            clip = store.DequeueFromPlayer(_target.owner.ToString());
        }
        else
        {
            List<PlayerManager> others = GetDungeonPlayers();
            others.Remove(_target);
            Shuffle(others);

            foreach (PlayerManager other in others)
            {
                clip = store.DequeueFromPlayer(other.owner.ToString());
                if (clip != null) break;
            }
        }

        clip ??= store.Dequeue();
        if (clip == null) return 0f;

        BroadcastClipToClients(clip.Clip);
        return clip.Clip.length;
    }

    private void BroadcastClipToClients(AudioClip clip)
    {
        float[] samples = new float[clip.samples * clip.channels];
        clip.GetData(samples, 0);
        RpcPlayClipOnClients(samples, clip.frequency, clip.channels);
    }

    [ObserversRpc]
    private void RpcPlayClipOnClients(float[] samples, int frequency, int channels)
    {
        AudioClip clip = AudioClip.Create("EchoPlayback",
            samples.Length / channels, channels, frequency, stream: false);
        clip.SetData(samples, 0);

        _audioSource.Stop();
        _audioSource.clip = clip;
        _audioSource.Play();
    }

    // ── Presentation (all clients) ─────────────────────────────────────────

    [ObserversRpc(runLocally: true, bufferLast: true)]
    private void RpcSetVisible(bool visible)
    {
        if (_modelRoot != null)
            _modelRoot.SetActive(visible);

        if (visible) PlayLoop(_breathingSound);
        else _loopSource.Stop();
    }

    [ObserversRpc(runLocally: true)]
    private void RpcSetChasing(bool chasing)
    {
        if (_animator != null)
            _animator.SetBool(AnimIsChasing, chasing);

        if (chasing)
        {
            if (_screechSound != null) _audioSource.PlayOneShot(_screechSound);
            PlayLoop(_runningSound);
        }
    }

    [ObserversRpc(runLocally: true)]
    private void RpcPlayScratch()
    {
        if (_scratchSound != null) _audioSource.PlayOneShot(_scratchSound);
    }

    private void PlayLoop(AudioClip clip)
    {
        if (clip == null) { _loopSource.Stop(); return; }
        _loopSource.clip = clip;
        _loopSource.Play();
    }

    // Throttled status log so the blocking condition is visible without spamming.
    private void Status(string message)
    {
        if (Time.time < _nextLogTime) return;
        _nextLogTime = Time.time + 3f;
        Debug.Log($"[TheEchoAI] {message}");
    }

    private static void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
