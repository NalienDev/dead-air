using System.Collections.Generic;
using PurrNet;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The Conductor. A blind, server-authoritative stalker that orients ONLY by sound.
///
/// It forces players to whisper and to keep the ambient noise down: talk loudly, run,
/// or grab a noisy item and it comes to investigate; a real sound spike — a shout or a
/// scream — sends it charging. It never uses sight.
///
/// Three states:
/// <list type="bullet">
/// <item><b>Wander</b> — roams the dungeon on the NavMesh, landing heavy footsteps.
/// Every step plays a (randomised) stomp and rattles the camera of any nearby player,
/// harder the closer it is.</item>
/// <item><b>Inspect</b> — a noise it could hear pulls it to that spot, where it prowls
/// the area for a few seconds. If it senses a player up close (it is blind, so this is
/// proximity, not sight) it starts chasing; otherwise it gives up and wanders off.</item>
/// <item><b>Chase</b> — runs the player down and lands ONE hit, then drops into
/// Recover.</item>
/// <item><b>Recover</b> — stands still for a couple of seconds, listening. Any noise it
/// can hear in that window (a sprint, a shout, a noisy grab) re-triggers the chase;
/// stay silent and it loses interest and wanders off.</item>
/// </list>
///
/// Hearing is attenuated by distance (a sprint is only audible up close, a scream
/// carries across the dungeon) and muffled by walls — it barely hears through a wall
/// unless the noise happens right on the other side of it.
///
/// Needs on this GameObject: NavMeshAgent, NetworkTransform, a child model root.
/// The dungeon NavMesh is baked at runtime by DungeonNavMeshBaker, exactly like TheEcho.
/// </summary>
public class TheConductorAI : NetworkBehaviour
{
    private enum State { Inactive, Wander, Inspect, Chase, Recover }

    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private GameObject _modelRoot;     // visuals + colliders, toggled on activate
    [SerializeField] private Animator _animator;        // optional; gets bool "IsChasing", float "Speed"
    [SerializeField] private AudioSource _audioSource;  // one-shots (steps, screech, attack)

    [Header("Sounds")]
    [Tooltip("Footstep stomps. One is picked at random per step — add several for variety.")]
    [SerializeField] private List<AudioClip> _stepSounds = new();
    [Tooltip("Played once when it locks onto a player and starts a chase.")]
    [SerializeField] private AudioClip _screechSound;
    [Tooltip("Played when it lands a hit.")]
    [SerializeField] private AudioClip _attackSound;
    [Tooltip("Quiet loop while wandering/inspecting (breathing, dragging chains…).")]
    [SerializeField] private AudioClip _idleLoop;
    [Tooltip("Loud loop while chasing.")]
    [SerializeField] private AudioClip _chaseLoop;

    [Header("Movement")]
    [SerializeField] private float _wanderSpeed = 1.6f;
    [SerializeField] private float _inspectSpeed = 1.9f;
    [SerializeField] private float _chaseSpeed = 3.4f;
    [Tooltip("Radius it picks its next wander/inspect point within.")]
    [SerializeField] private float _wanderRadius = 12f;
    [SerializeField] private float _inspectRoamRadius = 5f;

    [Header("Spawning")]
    [Tooltip("Seconds after the first player enters the dungeon before the Conductor appears.")]
    [SerializeField] private float _spawnDelayAfterEntry = 20f;
    [Tooltip("It will not appear within this radius of the dungeon entrance (tagged " +
             "'ExpeditionSpawn') — it can walk there, it just won't spawn there.")]
    [SerializeField] private float _entranceNoSpawnRadius = 15f;

    [Header("Footsteps & Camera Shake")]
    [Tooltip("Distance travelled between footstep stomps.")]
    [SerializeField] private float _stepDistance = 2f;
    [Tooltip("Camera-shake trauma at the footstep's position (0..1).")]
    [SerializeField, Range(0f, 1f)] private float _stepShakeIntensity = 0.6f;
    [Tooltip("Beyond this distance a footstep no longer shakes the camera.")]
    [SerializeField] private float _stepShakeRadius = 12f;
    [Tooltip("Extra shake multiplier while chasing (steps hit harder).")]
    [SerializeField] private float _chaseShakeMultiplier = 1.4f;

    [Header("Hearing")]
    [Tooltip("Loudness (0..1) a noise must reach for the Conductor to investigate it. " +
             "Whispers and normal walking should sit below this.")]
    [SerializeField, Range(0f, 1f)] private float _hearingThreshold = 0.45f;
    [Tooltip("Loudness (0..1) that counts as a spike — a shout/scream — and triggers an " +
             "immediate chase instead of a calm investigation.")]
    [SerializeField, Range(0f, 1f)] private float _chaseNoiseThreshold = 0.8f;
    [Tooltip("How far a MAXIMUM loudness (1.0) noise carries. Quieter noises carry " +
             "proportionally less — e.g. a 0.55 sprint carries 55% of this range.")]
    [SerializeField] private float _hearingRange = 30f;
    [Tooltip("How long (seconds) a heard noise stays worth reacting to.")]
    [SerializeField] private float _noiseMemory = 1f;
    [Tooltip("A hearable noise made within this range triggers an immediate chase — " +
             "sprinting right past the Conductor is not survivable.")]
    [SerializeField] private float _closeChaseRadius = 5f;
    [Tooltip("Multiplier on the carry distance of a noise heard through a wall (0 = " +
             "fully deaf through walls, 1 = walls don't matter). A noise right on the " +
             "other side of a wall still gets through; a distant one won't.")]
    [SerializeField, Range(0f, 1f)] private float _wallMuffle = 0.5f;
    [Tooltip("Layers that block/muffle sound (walls, rooms). Do NOT include the Player layer.")]
    [SerializeField] private LayerMask _occluderMask = Physics.DefaultRaycastLayers & ~(1 << 6);

    [Header("Inspect")]
    [Tooltip("Seconds spent prowling a noise before giving up and wandering off.")]
    [SerializeField] private float _inspectDuration = 10f;
    [Tooltip("Blind proximity 'sense' — a player this close during inspect gets chased.")]
    [SerializeField] private float _detectRadius = 3f;

    [Header("Chase")]
    [SerializeField] private float _attackRange = 1.6f;
    [Tooltip("Damage dealt by the single hit that ends a chase.")]
    [SerializeField] private int _attackDamage = 20;
    [Tooltip("Seconds it stands still, listening, after landing a hit. Any noise it can " +
             "hear in this window re-triggers the chase; silence sends it wandering.")]
    [SerializeField] private float _postAttackPause = 2f;
    [Tooltip("Give up the chase if this long passes without landing a hit or hearing the target.")]
    [SerializeField] private float _chaseGiveUpTime = 8f;

    private static readonly int AnimIsChasing = Animator.StringToHash("IsChasing");
    private static readonly int AnimSpeed = Animator.StringToHash("Speed");

    // ── Server state ───────────────────────────────────────────────────────

    private NavMeshAgent _agent;
    private AudioSource _loopSource;

    private State _state = State.Inactive;
    private PlayerManager _chaseTarget;

    private Vector3 _heardPos;
    private float _heardLoudness;   // effective (attenuated) loudness
    private bool _heardUrgent;      // heard close enough to warrant an instant chase
    private float _heardTime = -999f;

    private Vector3 _inspectOrigin;
    private float _inspectEndTime;

    private float _stepAccum;
    private Vector3 _lastStepPos;

    private float _chaseGiveUpAt;
    private float _recoverEndTime;

    private float _nextLogTime;

    // ── Lifecycle ──────────────────────────────────────────────────────────

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.enabled = false; // no NavMesh until the dungeon bakes

        if (TryGetComponent(out Rigidbody rb))
            rb.isKinematic = true; // the agent drives all movement

        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.spatialBlend = 1f;

        _loopSource = gameObject.AddComponent<AudioSource>();
        _loopSource.loop = true;
        _loopSource.spatialBlend = 1f;

        if (_modelRoot != null)
            _modelRoot.SetActive(false);
    }

    protected override void OnSpawned(bool asServer)
    {
        if (!isServer) return;

        NoiseEvents.OnNoise += OnHeardNoise;

        if (DungeonGenerator.Instance != null)
            DungeonGenerator.Instance.OnGenerated += OnDungeonRegenerated;
    }

    protected override void OnDespawned(bool asServer)
    {
        if (!asServer) return;

        NoiseEvents.OnNoise -= OnHeardNoise;

        if (DungeonGenerator.Instance != null)
            DungeonGenerator.Instance.OnGenerated -= OnDungeonRegenerated;
    }

    private void OnDungeonRegenerated()
    {
        // New layout — drop everything and re-place on the fresh NavMesh next tick.
        Deactivate();
    }

    private void Update()
    {
        if (!isServer) return;

        switch (_state)
        {
            case State.Inactive: TickInactive(); break;
            case State.Wander:   TickWander();   break;
            case State.Inspect:  TickInspect();  break;
            case State.Chase:    TickChase();    break;
            case State.Recover:  TickRecover();  break;
        }

        UpdateAnimatorSpeed();
    }

    // ── Inactive: wait for a NavMesh, then take the stage ──────────────────

    private void TickInactive()
    {
        if (DungeonGenerator.Instance == null || !DungeonGenerator.Instance.IsGenerated())
            return;

        // Anchor to the target of any player, or just the current position, so we
        // land on a connected part of the NavMesh.
        Vector3 anchor = transform.position;
        List<PlayerManager> players = GetDungeonPlayers();
        if (players.Count > 0) anchor = players[0].transform.position;

        if (!TryRandomNavPoint(anchor, _wanderRadius, out Vector3 spawn))
        {
            Status("waiting — no NavMesh point found to appear on.");
            return;
        }

        transform.position = spawn;
        _agent.enabled = true;
        if (!_agent.isOnNavMesh)
        {
            _agent.enabled = false;
            Status("waiting — could not attach agent to the NavMesh.");
            return;
        }
        _agent.Warp(spawn);
        _agent.isStopped = false; // may linger from a Recover pause before deactivation
        _lastStepPos = spawn;

        RpcSetVisible(true);
        EnterWander();
        Debug.Log("[TheConductorAI] Active — wandering the dungeon.");
    }

    private void Deactivate()
    {
        _state = State.Inactive;
        _chaseTarget = null;
        _heardTime = -999f;

        if (_agent.enabled)
        {
            _agent.ResetPath();
            _agent.enabled = false;
        }

        RpcSetChasing(false);
        RpcSetVisible(false);
    }

    // ── Wander ──────────────────────────────────────────────────────────────

    private void EnterWander()
    {
        _state = State.Wander;
        _agent.speed = _wanderSpeed;
        PickWanderDestination();
        SetIdleLoop();
    }

    private void TickWander()
    {
        TickSteps(chasing: false);

        if (ConsumeHeardNoise(out Vector3 pos, out float loud, out bool urgent))
        {
            // A spike, or any hearable noise made right next to it, means blood.
            if ((urgent || loud >= _chaseNoiseThreshold) && TryStartChaseNear(pos)) return;
            EnterInspect(pos);
            return;
        }

        if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.2f)
            PickWanderDestination();
    }

    private void PickWanderDestination()
    {
        if (TryRandomNavPoint(transform.position, _wanderRadius, out Vector3 dest))
            _agent.SetDestination(dest);
    }

    // ── Inspect ──────────────────────────────────────────────────────────────

    private void EnterInspect(Vector3 origin)
    {
        _state = State.Inspect;
        _agent.speed = _inspectSpeed;
        _inspectOrigin = origin;
        _inspectEndTime = Time.time + _inspectDuration;
        _agent.SetDestination(origin);
        SetIdleLoop();
        Debug.Log($"[TheConductorAI] Investigating a noise at {origin}.");
    }

    private void TickInspect()
    {
        TickSteps(chasing: false);

        // A blind "sense" of a body up close → lock on.
        PlayerManager near = NearestDungeonPlayer(transform.position, _detectRadius);
        if (near != null && TryStartChase(near)) return;

        // A fresh noise re-routes (or escalates) the investigation.
        if (ConsumeHeardNoise(out Vector3 pos, out float loud, out bool urgent))
        {
            if ((urgent || loud >= _chaseNoiseThreshold) && TryStartChaseNear(pos)) return;
            _inspectOrigin = pos;
            _inspectEndTime = Time.time + _inspectDuration;
            _agent.SetDestination(pos);
        }

        if (Time.time >= _inspectEndTime)
        {
            EnterWander();
            return;
        }

        // Prowl around the noise origin.
        if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.2f)
        {
            if (TryRandomNavPoint(_inspectOrigin, _inspectRoamRadius, out Vector3 dest))
                _agent.SetDestination(dest);
        }
    }

    // ── Chase ─────────────────────────────────────────────────────────────────

    private bool TryStartChaseNear(Vector3 noisePos)
    {
        PlayerManager target = NearestDungeonPlayer(noisePos, _hearingRange);
        return target != null && TryStartChase(target);
    }

    private bool TryStartChase(PlayerManager target)
    {
        if (!IsValidTarget(target)) return false;

        _chaseTarget = target;
        _state = State.Chase;
        _agent.speed = _chaseSpeed;
        _agent.isStopped = false;
        _chaseGiveUpAt = Time.time + _chaseGiveUpTime;

        RpcSetChasing(true);
        SetChaseLoop();
        Debug.Log($"[TheConductorAI] Chasing '{target.name}'.");
        return true;
    }

    private void TickChase()
    {
        if (!IsValidTarget(_chaseTarget))
        {
            EnterWander();
            return;
        }

        TickSteps(chasing: true);
        _agent.SetDestination(_chaseTarget.transform.position);

        float dist = Vector3.Distance(transform.position, _chaseTarget.transform.position);

        // One hit ends the chase — then it stands still and listens.
        if (dist <= _attackRange)
        {
            _chaseTarget.Damage(_attackDamage);
            RpcAttack(transform.position);
            EnterRecover();
            return;
        }

        // Stay locked while the target keeps making noise.
        if (_chaseTarget.CurrentVoiceLoudness >= _hearingThreshold)
            _chaseGiveUpAt = Time.time + _chaseGiveUpTime;

        if (Time.time >= _chaseGiveUpAt)
        {
            Debug.Log("[TheConductorAI] Lost the target — back to wandering.");
            EnterWander();
        }
    }

    // ── Recover: stand still after a hit, listening for a reason to go again ──

    private void EnterRecover()
    {
        _state = State.Recover;
        _chaseTarget = null;
        _recoverEndTime = Time.time + _postAttackPause;
        _heardTime = -999f; // the hit itself shouldn't count — only fresh noise

        _agent.ResetPath();
        _agent.isStopped = true;

        RpcSetChasing(false);
        SetIdleLoop();
        Debug.Log($"[TheConductorAI] Struck — standing still and listening for {_postAttackPause:F0}s.");
    }

    private void TickRecover()
    {
        // Any noise it can hear during the pause — a sprint away, a cry for help —
        // and it goes right back on the hunt.
        if (ConsumeHeardNoise(out Vector3 pos, out float _, out bool _))
        {
            _agent.isStopped = false;
            if (TryStartChaseNear(pos)) return;
        }

        if (Time.time >= _recoverEndTime)
        {
            Debug.Log("[TheConductorAI] Silence — losing interest.");
            _agent.isStopped = false;
            EnterWander();
        }
    }

    // ── Footsteps ─────────────────────────────────────────────────────────────

    private void TickSteps(bool chasing)
    {
        if (!_agent.enabled || !_agent.isOnNavMesh) return;

        _stepAccum += Vector3.Distance(transform.position, _lastStepPos);
        _lastStepPos = transform.position;

        if (_stepAccum < _stepDistance) return;
        _stepAccum = 0f;

        if (_stepSounds.Count == 0) return;
        int index = Random.Range(0, _stepSounds.Count);
        float intensity = _stepShakeIntensity * (chasing ? _chaseShakeMultiplier : 1f);
        RpcStep(transform.position, index, intensity);
    }

    // ── Noise intake (server) ─────────────────────────────────────────────────

    private void OnHeardNoise(Vector3 pos, float loudness)
    {
        if (_state == State.Inactive) return;

        // Type filter on the RAW loudness: whispers and walking are simply never
        // interesting, no matter how close. (Attenuating before this check made the
        // margin between sprint 0.55 and the threshold collapse within ~2 m.)
        if (loudness < _hearingThreshold) return;

        // Louder noises carry farther: a max-loudness scream carries the full
        // hearing range, a sprint (0.55) roughly half of it.
        float carry = _hearingRange * loudness;

        // Walls halve the carry: a noise right on the other side of a wall still
        // gets through; a distant one doesn't.
        Vector3 ear = transform.position + Vector3.up * 1.5f;
        Vector3 source = pos + Vector3.up * 1f;
        if (Physics.Linecast(ear, source, _occluderMask, QueryTriggerInteraction.Ignore))
            carry *= _wallMuffle;

        float dist = Vector3.Distance(transform.position, pos);
        if (dist > carry) return;

        // Keep the most recent qualifying noise; the state ticks decide what to do.
        _heardPos = pos;
        _heardLoudness = loudness;
        _heardUrgent = dist <= _closeChaseRadius;
        _heardTime = Time.time;
    }

    private bool ConsumeHeardNoise(out Vector3 pos, out float loudness, out bool urgent)
    {
        pos = _heardPos;
        loudness = _heardLoudness;
        urgent = _heardUrgent;
        if (Time.time - _heardTime > _noiseMemory) return false;

        _heardTime = -999f; // consumed
        return true;
    }

    // ── Targeting helpers ─────────────────────────────────────────────────────

    private static bool IsValidTarget(PlayerManager p)
        => p != null && !p.IsDead && p.IsInsideDungeon();

    private List<PlayerManager> GetDungeonPlayers()
    {
        var players = new List<PlayerManager>();
        foreach (PlayerManager p in FindObjectsByType<PlayerManager>(FindObjectsSortMode.None))
            if (IsValidTarget(p)) players.Add(p);
        return players;
    }

    private PlayerManager NearestDungeonPlayer(Vector3 point, float maxRange)
    {
        PlayerManager best = null;
        float bestSq = maxRange * maxRange;
        foreach (PlayerManager p in GetDungeonPlayers())
        {
            float sq = (p.transform.position - point).sqrMagnitude;
            if (sq <= bestSq) { bestSq = sq; best = p; }
        }
        return best;
    }

    private static bool TryRandomNavPoint(Vector3 center, float radius, out Vector3 result)
    {
        for (int i = 0; i < 12; i++)
        {
            Vector3 candidate = center + Random.insideUnitSphere * radius;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                result = hit.position;
                return true;
            }
        }
        result = center;
        return false;
    }

    // ── Presentation (all clients) ─────────────────────────────────────────────

    [ObserversRpc(runLocally: true, bufferLast: true)]
    private void RpcSetVisible(bool visible)
    {
        if (_modelRoot != null) _modelRoot.SetActive(visible);
        if (!visible) _loopSource.Stop();
    }

    [ObserversRpc(runLocally: true)]
    private void RpcStep(Vector3 pos, int clipIndex, float shakeIntensity)
    {
        if (_stepSounds.Count > 0 && clipIndex >= 0 && clipIndex < _stepSounds.Count)
        {
            AudioClip clip = _stepSounds[clipIndex];
            if (clip != null) _audioSource.PlayOneShot(clip);
        }
        CameraShake.ShakeFromWorld(pos, shakeIntensity, _stepShakeRadius);
    }

    [ObserversRpc(runLocally: true)]
    private void RpcSetChasing(bool chasing)
    {
        if (_animator != null) _animator.SetBool(AnimIsChasing, chasing);
        if (chasing && _screechSound != null) _audioSource.PlayOneShot(_screechSound);
    }

    [ObserversRpc(runLocally: true)]
    private void RpcAttack(Vector3 pos)
    {
        if (_attackSound != null) _audioSource.PlayOneShot(_attackSound);
        CameraShake.ShakeFromWorld(pos, 1f, _attackRange * 3f);
    }

    // ── Loops / animator (server-side helpers call these locally too) ──────────

    private void SetIdleLoop() => RpcPlayLoop(false);
    private void SetChaseLoop() => RpcPlayLoop(true);

    [ObserversRpc(runLocally: true)]
    private void RpcPlayLoop(bool chasing)
    {
        AudioClip clip = chasing ? _chaseLoop : _idleLoop;
        if (clip == null) { _loopSource.Stop(); return; }
        if (_loopSource.clip == clip && _loopSource.isPlaying) return;
        _loopSource.clip = clip;
        _loopSource.Play();
    }

    private void UpdateAnimatorSpeed()
    {
        if (_animator == null || !_agent.enabled) return;
        _animator.SetFloat(AnimSpeed, _agent.velocity.magnitude);
    }

    private void Status(string message)
    {
        if (Time.time < _nextLogTime) return;
        _nextLogTime = Time.time + 3f;
        Debug.Log($"[TheConductorAI] {message}");
    }
}
