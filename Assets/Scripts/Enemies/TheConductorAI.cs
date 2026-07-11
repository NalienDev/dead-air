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
/// Every step plays a (randomised) stomp.</item>
/// <item><b>Inspect</b> — a noise it could hear pulls it to that spot, where it prowls
/// the area for a few seconds. If it senses a player up close (it is blind, so this is
/// proximity, not sight) it starts chasing; otherwise it gives up and wanders off.</item>
/// <item><b>Chase</b> — runs the player down. Once in range it swings.</item>
/// <item><b>Attack</b> — plays the attack animation; the hit lands on the clip's
/// "slicing" frame (an animation event calling OnAttackHit), then drops into Recover.</item>
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
    private enum State { Inactive, Wander, Inspect, Chase, Attack, Recover }

    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private GameObject _modelRoot;     // visuals + colliders, toggled on activate
    [SerializeField] private Animator _animator;        // optional; gets bool "IsChasing", float "Speed"
    [SerializeField] private AudioSource _audioSource;  // one-shots (steps, screech, attack)

    [Header("Sounds")]
    [Tooltip("Footstep stomps. One is picked at random per step — add several for variety.")]
    [SerializeField] private List<AudioClip> _stepSounds = new();
    [Tooltip("Screech. Played from an animation event on the Shout (post-attack) clip.")]
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

    [Header("Footsteps")]
    [Tooltip("Distance travelled between footstep stomps.")]
    [SerializeField] private float _stepDistance = 2f;

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
    [Tooltip("Safety net: if the attack animation's hit event never fires within this many " +
             "seconds (e.g. the event wasn't added to the clip), give up the swing and recover.")]
    [SerializeField] private float _attackTimeout = 2f;

    private static readonly int AnimIsChasing = Animator.StringToHash("IsChasing");
    private static readonly int AnimSpeed = Animator.StringToHash("Speed");
    private static readonly int AnimAttack = Animator.StringToHash("Attack");
    private static readonly int AnimRechase = Animator.StringToHash("Rechase");

    // ── Server state ───────────────────────────────────────────────────────

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

        // Loops (idle/chase) go through a crossfader so starts, stops and the
        // idle↔chase switch never click. Add the component to the prefab to tune the
        // fade times; it's created with defaults if missing.
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
            case State.Attack:   TickAttack();   break;
            case State.Recover:  TickRecover();  break;
        }

        UpdateAnimatorSpeed();
    }

    // ── Inactive: wait for a NavMesh, then take the stage ──────────────────

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
        TickSteps();

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
        TickSteps();

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

        TickSteps();
        _agent.SetDestination(_chaseTarget.transform.position);

        float dist = Vector3.Distance(transform.position, _chaseTarget.transform.position);

        // In range → wind up the swing. The hit itself lands on the animation's
        // "slicing" frame (see OnAttackHit), not here.
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
            Debug.Log("[TheConductorAI] Lost the target — back to wandering.");
            EnterWander();
        }
    }

    // ── Attack: play the swing, land the hit on the animation's slicing frame ──

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
        // Leave IsChasing true — the Attack trigger drives Chase → Attack. Flipping
        // IsChasing false here would race the Chase → Wander transition and send him
        // back to wandering instead of attacking. It's cleared later in EnterRecover.
        RpcTriggerAttack();
        Debug.Log("[TheConductorAI] Swinging.");
    }

    private void TickAttack()
    {
        // OnAttackHit (an animation event on the slicing frame) does the real work:
        // it applies the hit and drops into Recover. This is only a safety net in
        // case that event never fires so the Conductor can't freeze mid-swing.
        if (Time.time >= _attackTimeoutAt)
        {
            Debug.LogWarning("[TheConductorAI] Attack hit event never fired — recovering.");
            EnterRecover();
        }
    }

    /// <summary>
    /// Animation event. Add this to the attack clip on the "slicing" pose frame —
    /// select the frame, add an Animation Event, and set its Function to
    /// <c>OnAttackHit</c> (no parameters). Fires on every client; only the server
    /// applies damage, and only if the target is still within attack range.
    /// </summary>
    public void OnAttackHit()
    {
        if (_attackSound != null) _audioSource.PlayOneShot(_attackSound);

        if (!isServer) return;
        if (_state != State.Attack) return; // ignore a stray/late event

        if (IsValidTarget(_chaseTarget) &&
            Vector3.Distance(transform.position, _chaseTarget.transform.position) <= _attackRange)
        {
            _chaseTarget.Damage(_attackDamage);
        }

        EnterRecover();
    }

    /// <summary>
    /// Animation event. Put this on the Shout (post-attack) clip to play the screech.
    /// Routed through <see cref="ConductorAnimationRelay"/>; fires on every client, so
    /// no RPC is needed — each client plays its own copy.
    /// </summary>
    public void OnShout()
    {
        if (_screechSound != null) _audioSource.PlayOneShot(_screechSound);
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
        // Interruptible: any noise it can hear during the recharge pause — a sprint
        // away, a cry for help — and it goes right back on the hunt.
        if (ConsumeHeardNoise(out Vector3 pos, out float _, out bool _))
        {
            _agent.isStopped = false;
            if (TryStartChaseNear(pos))
            {
                // Kick the animator out of Shout and back into Chase. This trigger
                // lingers until the attack clip finishes and the animator reaches
                // Shout, so he only visibly resumes chasing once the swing is done.
                RpcTriggerRechase();
                return;
            }
        }

        if (Time.time >= _recoverEndTime)
        {
            Debug.Log("[TheConductorAI] Pause over — back to wandering.");
            _agent.isStopped = false;
            EnterWander();
        }
    }

    // ── Footsteps ─────────────────────────────────────────────────────────────

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

    // The return gather zone is a safe area — a player standing in it can't be sensed
    // or chased, and a chase target reaching it sends the Conductor straight back to
    // wandering (TickChase's validity check).
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

    // Like TryRandomNavPoint but rejects points near the dungeon entrance so the
    // Conductor never appears where the players spawn in.
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

    // ── Presentation (all clients) ─────────────────────────────────────────────

    [ObserversRpc(runLocally: true, bufferLast: true)]
    private void RpcSetVisible(bool visible)
    {
        if (_modelRoot != null) _modelRoot.SetActive(visible);
        if (!visible) _loopPlayer.StopLoop(); // fade out — no snap when it vanishes
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
        // Clear any pending Attack trigger left over from a previous swing so it can't
        // fire the instant he (re-)enters Chase — he only attacks when EnterAttack sets
        // it again once in range.
        _animator.ResetTrigger(AnimAttack);
        _animator.SetBool(AnimIsChasing, chasing);
    }

    // Plays the attack animation on every client. The clip's "slicing" frame carries
    // an animation event that calls OnAttackHit, which lands the hit and plays the sound.
    [ObserversRpc(runLocally: true)]
    private void RpcTriggerAttack()
    {
        if (_animator != null) _animator.SetTrigger(AnimAttack);
    }

    // Drives the Shout (post-attack recharge) → Chase transition when it hears
    // something during the pause. Set as a trigger; it waits for the attack clip to
    // finish and the animator to enter Shout before the transition consumes it.
    [ObserversRpc(runLocally: true)]
    private void RpcTriggerRechase()
    {
        if (_animator != null) _animator.SetTrigger(AnimRechase);
    }

    // ── Loops / animator (server-side helpers call these locally too) ──────────

    private void SetIdleLoop() => RpcPlayLoop(false);
    private void SetChaseLoop() => RpcPlayLoop(true);

    [ObserversRpc(runLocally: true)]
    private void RpcPlayLoop(bool chasing)
    {
        // Crossfades idle ↔ chase (and fades in/out at the edges) so the switch never
        // clicks. Fade times are configurable on the CrossfadeLoopPlayer component.
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
