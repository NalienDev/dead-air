using System.Collections.Generic;
using PurrNet;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Blind, server-authoritative stalker that hunts players entirely by sound through wander, inspect, chase, attack, and recover states.
/// </summary>
public class TheConductorAI : NetworkBehaviour
{
    private enum State { Inactive, Wander, Inspect, Chase, Attack, Recover }

    [Header("References")]
    [SerializeField] private GameObject _modelRoot;     // visuals and colliders, toggled on activate
    [SerializeField] private Animator _animator;        // gets bool "IsChasing", float "Speed"
    [SerializeField] private AudioSource _audioSource;

    [Header("Sounds")]
    [Tooltip("Footstep stomps. One is picked at random per step.")]
    [SerializeField] private List<AudioClip> _stepSounds = new();
    [Tooltip("Screech, played from an animation event on the shout clip.")]
    [SerializeField] private AudioClip _screechSound;
    [Tooltip("Played when it lands a hit.")]
    [SerializeField] private AudioClip _attackSound;
    [Tooltip("Quiet loop while wandering or inspecting.")]
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
    [Tooltip("It won't spawn within this radius of the dungeon entrance, though it can walk there.")]
    [SerializeField] private float _entranceNoSpawnRadius = 15f;

    [Header("Footsteps")]
    [Tooltip("Distance travelled between footstep stomps.")]
    [SerializeField] private float _stepDistance = 2f;

    [Header("Hearing")]
    [Tooltip("Loudness a noise must reach for the Conductor to investigate it.")]
    [SerializeField, Range(0f, 1f)] private float _hearingThreshold = 0.45f;
    [Tooltip("Loudness that counts as a spike and triggers an immediate chase.")]
    [SerializeField, Range(0f, 1f)] private float _chaseNoiseThreshold = 0.8f;
    [Tooltip("How far a maximum-loudness noise carries; quieter noises carry proportionally less.")]
    [SerializeField] private float _hearingRange = 30f;
    [Tooltip("How long a heard noise stays worth reacting to.")]
    [SerializeField] private float _noiseMemory = 1f;
    [Tooltip("A hearable noise made within this range triggers an immediate chase.")]
    [SerializeField] private float _closeChaseRadius = 5f;
    [Tooltip("Carry multiplier through a wall. 0 = deaf through walls, 1 = walls don't matter.")]
    [SerializeField, Range(0f, 1f)] private float _wallMuffle = 0.5f;
    [Tooltip("Layers that block or muffle sound. Do not include the Player layer.")]
    [SerializeField] private LayerMask _occluderMask = Physics.DefaultRaycastLayers & ~(1 << 6);

    [Header("Inspect")]
    [Tooltip("Seconds spent prowling a noise before giving up and wandering off.")]
    [SerializeField] private float _inspectDuration = 10f;
    [Tooltip("A player this close during inspect gets chased.")]
    [SerializeField] private float _detectRadius = 3f;

    [Header("Chase")]
    [SerializeField] private float _attackRange = 1.6f;
    [Tooltip("Damage dealt by the single hit that ends a chase.")]
    [SerializeField] private int _attackDamage = 20;
    [Tooltip("Seconds it stands still and listens after landing a hit.")]
    [SerializeField] private float _postAttackPause = 2f;
    [Tooltip("Give up the chase if this long passes without landing a hit or hearing the target.")]
    [SerializeField] private float _chaseGiveUpTime = 8f;
    [Tooltip("Give up the swing and recover if the attack hit event never fires within this time.")]
    [SerializeField] private float _attackTimeout = 2f;

    private static readonly int AnimIsChasing = Animator.StringToHash("IsChasing");
    private static readonly int AnimSpeed = Animator.StringToHash("Speed");
    private static readonly int AnimAttack = Animator.StringToHash("Attack");
    private static readonly int AnimRechase = Animator.StringToHash("Rechase");

    private NavMeshAgent _agent;
    private CrossfadeLoopPlayer _loopPlayer;

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
    private float _attackTimeoutAt;
    private float _recoverEndTime;

    private float _entryTime = -1f;   // when the first player entered the dungeon
    private Transform _entrance;

    private float _nextLogTime;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.enabled = false; // no NavMesh until the dungeon bakes

        if (TryGetComponent(out Rigidbody rb))
            rb.isKinematic = true; // the agent drives all movement

        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.spatialBlend = 1f;

        // Loops go through a crossfader so starts, stops, and the idle/chase switch don't click.
        _loopPlayer = GetComponent<CrossfadeLoopPlayer>();
        if (_loopPlayer == null) _loopPlayer = gameObject.AddComponent<CrossfadeLoopPlayer>();

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
        // New layout: drop everything and re-place on the fresh NavMesh next tick.
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
            case State.Attack:   TickAttack();   break;
            case State.Recover:  TickRecover();  break;
        }

        UpdateAnimatorSpeed();
    }

    // Waits for a baked NavMesh and players inside, then spawns and starts wandering.
    private void TickInactive()
    {
        if (DungeonGenerator.Instance == null || !DungeonGenerator.Instance.IsGenerated())
        {
            _entryTime = -1f;
            return;
        }

        // Only count down once players are actually inside the dungeon.
        List<PlayerManager> players = GetDungeonPlayers();
        if (players.Count == 0)
        {
            _entryTime = -1f;
            return;
        }

        if (_entryTime < 0f) _entryTime = Time.time;
        float wait = _spawnDelayAfterEntry - (Time.time - _entryTime);
        if (wait > 0f)
        {
            Status($"waiting — appears {wait:F0}s after entry.");
            return;
        }

        // Appear near a random player but never on top of the entrance.
        Vector3 anchor = players[Random.Range(0, players.Count)].transform.position;
        if (!TryFindSpawnPoint(anchor, out Vector3 spawn))
        {
            Status("waiting — no valid spawn point away from the entrance.");
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

    private void EnterWander()
    {
        _state = State.Wander;
        _agent.speed = _wanderSpeed;
        PickWanderDestination();
        SetIdleLoop();
    }

    private void TickWander()
    {
        TickSteps();

        if (ConsumeHeardNoise(out Vector3 pos, out float loud, out bool urgent))
        {
            // A spike, or any hearable noise right next to it, escalates straight to a chase.
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
        TickSteps();

        // A blind sense of a body up close locks on.
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

        TickSteps();
        _agent.SetDestination(_chaseTarget.transform.position);

        float dist = Vector3.Distance(transform.position, _chaseTarget.transform.position);

        // In range: wind up the swing. The hit lands on the animation's slicing frame, not here.
        if (dist <= _attackRange)
        {
            EnterAttack();
            return;
        }

        // Stay locked while the target keeps making noise.
        if (_chaseTarget.CurrentVoiceLoudness >= _hearingThreshold)
            _chaseGiveUpAt = Time.time + _chaseGiveUpTime;

        if (Time.time >= _chaseGiveUpAt)
        {
            Debug.Log("[TheConductorAI] Lost the target, back to wandering.");
            EnterWander();
        }
    }

    private void EnterAttack()
    {
        _state = State.Attack;

        // Plant it and face the target while it swings.
        _agent.ResetPath();
        _agent.isStopped = true;

        Vector3 look = _chaseTarget.transform.position - transform.position;
        look.y = 0f;
        if (look.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(look);

        _attackTimeoutAt = Time.time + _attackTimeout;
        // Leave IsChasing true; clearing it here would race the Chase-to-Wander transition.
        // It's cleared later in EnterRecover.
        RpcTriggerAttack();
        Debug.Log("[TheConductorAI] Swinging.");
    }

    private void TickAttack()
    {
        // Safety net: OnAttackHit normally lands the hit and recovers, so this only fires
        // if that animation event never does, to stop the Conductor freezing mid-swing.
        if (Time.time >= _attackTimeoutAt)
        {
            Debug.LogWarning("[TheConductorAI] Attack hit event never fired, recovering.");
            EnterRecover();
        }
    }

    // Animation event on the attack clip's slicing frame; only the server applies damage.
    public void OnAttackHit()
    {
        if (_attackSound != null) _audioSource.PlayOneShot(_attackSound);

        if (!isServer) return;
        if (_state != State.Attack) return; // ignore a stray or late event

        if (IsValidTarget(_chaseTarget) &&
            Vector3.Distance(transform.position, _chaseTarget.transform.position) <= _attackRange)
        {
            _chaseTarget.Damage(_attackDamage);
        }

        EnterRecover();
    }

    // Animation event on the shout clip that plays the screech on each client.
    public void OnShout()
    {
        if (_screechSound != null) _audioSource.PlayOneShot(_screechSound);
    }

    private void EnterRecover()
    {
        _state = State.Recover;
        _chaseTarget = null;
        _recoverEndTime = Time.time + _postAttackPause;
        _heardTime = -999f; // only fresh noise should count, not the hit itself

        _agent.ResetPath();
        _agent.isStopped = true;

        RpcSetChasing(false);
        SetIdleLoop();
        Debug.Log($"[TheConductorAI] Struck, standing still and listening for {_postAttackPause:F0}s.");
    }

    private void TickRecover()
    {
        // Any noise it can hear during the pause sends it right back on the hunt.
        if (ConsumeHeardNoise(out Vector3 pos, out float _, out bool _))
        {
            _agent.isStopped = false;
            if (TryStartChaseNear(pos))
            {
                // Kick the animator out of Shout back into Chase once the swing finishes.
                RpcTriggerRechase();
                return;
            }
        }

        if (Time.time >= _recoverEndTime)
        {
            Debug.Log("[TheConductorAI] Pause over, back to wandering.");
            _agent.isStopped = false;
            EnterWander();
        }
    }

    private void TickSteps()
    {
        if (!_agent.enabled || !_agent.isOnNavMesh) return;

        _stepAccum += Vector3.Distance(transform.position, _lastStepPos);
        _lastStepPos = transform.position;

        if (_stepAccum < _stepDistance) return;
        _stepAccum = 0f;

        if (_stepSounds.Count == 0) return;
        int index = Random.Range(0, _stepSounds.Count);
        RpcStep(index);
    }

    private void OnHeardNoise(Vector3 pos, float loudness)
    {
        if (_state == State.Inactive) return;

        // Filter on raw loudness: whispers and walking are never interesting, no matter how close.
        if (loudness < _hearingThreshold) return;

        // Louder noises carry farther, scaled by loudness.
        float carry = _hearingRange * loudness;

        // Walls reduce how far the noise carries.
        Vector3 ear = transform.position + Vector3.up * 1.5f;
        Vector3 source = pos + Vector3.up * 1f;
        if (Physics.Linecast(ear, source, _occluderMask, QueryTriggerInteraction.Ignore))
            carry *= _wallMuffle;

        float dist = Vector3.Distance(transform.position, pos);
        if (dist > carry) return;

        // Keep the most recent qualifying noise; the state ticks decide what to do with it.
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

    // The return gather zone is a safe area: a player inside it can't be sensed or chased.
    private static bool IsValidTarget(PlayerManager p)
        => p != null && !p.IsDead && p.IsInsideDungeon() && !ReturnGatherZone.IsInside(p);

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

    // Like TryRandomNavPoint but rejects points near the entrance so it never spawns on the players.
    private bool TryFindSpawnPoint(Vector3 anchor, out Vector3 result)
    {
        if (_entrance == null)
        {
            GameObject go = GameObject.FindGameObjectWithTag("ExpeditionSpawn");
            if (go != null) _entrance = go.transform;
        }

        float noSpawnSq = _entranceNoSpawnRadius * _entranceNoSpawnRadius;

        for (int i = 0; i < 20; i++)
        {
            Vector3 candidate = anchor + Random.insideUnitSphere * _wanderRadius;
            if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                continue;
            if (_entrance != null && (hit.position - _entrance.position).sqrMagnitude < noSpawnSq)
                continue;
            result = hit.position;
            return true;
        }

        result = anchor;
        return false;
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

    [ObserversRpc(runLocally: true, bufferLast: true)]
    private void RpcSetVisible(bool visible)
    {
        if (_modelRoot != null) _modelRoot.SetActive(visible);
        if (!visible) _loopPlayer.StopLoop(); // fade out so it doesn't snap when it vanishes
    }

    [ObserversRpc(runLocally: true)]
    private void RpcStep(int clipIndex)
    {
        if (_stepSounds.Count > 0 && clipIndex >= 0 && clipIndex < _stepSounds.Count)
        {
            AudioClip clip = _stepSounds[clipIndex];
            if (clip != null) _audioSource.PlayOneShot(clip);
        }
    }

    [ObserversRpc(runLocally: true)]
    private void RpcSetChasing(bool chasing)
    {
        if (_animator == null) return;
        // Clear any leftover Attack trigger so it can't fire the instant Chase re-enters.
        _animator.ResetTrigger(AnimAttack);
        _animator.SetBool(AnimIsChasing, chasing);
    }

    // Plays the attack animation; its slicing frame fires the OnAttackHit event.
    [ObserversRpc(runLocally: true)]
    private void RpcTriggerAttack()
    {
        if (_animator != null) _animator.SetTrigger(AnimAttack);
    }

    // Drives the shout-to-chase transition when it hears something during the pause.
    [ObserversRpc(runLocally: true)]
    private void RpcTriggerRechase()
    {
        if (_animator != null) _animator.SetTrigger(AnimRechase);
    }

    private void SetIdleLoop() => RpcPlayLoop(false);
    private void SetChaseLoop() => RpcPlayLoop(true);

    [ObserversRpc(runLocally: true)]
    private void RpcPlayLoop(bool chasing)
    {
        AudioClip clip = chasing ? _chaseLoop : _idleLoop;
        if (clip == null) { _loopPlayer.StopLoop(); return; }
        _loopPlayer.PlayLoop(clip);
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
