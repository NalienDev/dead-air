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
/// Once it's chasing, a player can scare it away by flashing the flashlight on any
/// visible part of it _flashlightScareFlashes times. _reactToAllPlayers decides
/// whether non-targets can trigger the chase / scare.
///
/// If a lure and a chase model are assigned (overlapping children, each with its
/// own Animator), the chase start dissolve-morphs one into the other (ModelMorpher
/// + Custom/EchoDissolve shader).
///
/// Needs on this GameObject: NavMeshAgent, NetworkTransform, and either a model root
/// or the two morph forms as child visuals. The dungeon NavMesh is baked at runtime
/// by DungeonNavMeshBaker.
/// </summary>
public class TheEchoAI : NetworkBehaviour
{
    private enum State { Hidden, Lure, Chase }

    // ── Inspector ──────────────────────────────────────────────────────────

    [Header("References")]
    [SerializeField] private AudioSource _audioSource;   // voice playback
    [Tooltip("Optional. Whole-model parent toggled on spawn/despawn. Leave empty if " +
             "you use the morph forms below — hiding those hides the Echo.")]
    [SerializeField] private GameObject _modelRoot;
    [SerializeField] private Animator _animator;         // optional; gets bool "IsChasing"

    [Header("Morph Forms (optional)")]
    [Tooltip("Form shown while luring. Child of the model root, with its own Animator.")]
    [SerializeField] private GameObject _lureModel;
    [Tooltip("Form it morphs into when the chase starts. Child of the model root, " +
             "overlapping the lure form, with its own Animator.")]
    [SerializeField] private GameObject _chaseModel;
    [Tooltip("Drives the dissolve crossfade between the two forms. Auto-found on " +
             "this GameObject if left empty.")]
    [SerializeField] private ModelMorpher _morpher;

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
    [Tooltip("Seconds after the last voice finishes before giving up and despawning.")]
    [SerializeField] private float _lureTimeout = 10f;
    [Tooltip("Gap between one stolen voice ending and the next one playing.")]
    [SerializeField] private float _voiceRepeatDelay = 3f;
    [Tooltip("Repeat the target's own voice instead of another player's.")]
    [SerializeField] private bool _repeatTargetVoice = false;

    [Header("Flashlight Scare (chase only)")]
    [Tooltip("Number of flashes (beam re-entering any visible part of the Echo) " +
             "needed to scare it away while it's chasing. Flick the light on/off.")]
    [SerializeField] private int _flashlightScareFlashes = 5;
    [Tooltip("One-shot when the flashlight drives it away.")]
    [SerializeField] private AudioClip _scaredSound;
    [Tooltip("When false, only the lured target can trigger the chase, and only the " +
             "chase target can scare it with the flashlight; others are ignored.")]
    [SerializeField] private bool _reactToAllPlayers = true;

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

    // Points sampled up the Echo's body when testing flashlight exposure, so the
    // scare still registers when only part of it (feet, torso, head) is lit/visible.
    private static readonly float[] ScareSampleHeights = { 0.2f, 0.7f, 1.2f, 1.6f };
    private const float ScareSampleRadius = 0.35f;

    // ── State (server) ─────────────────────────────────────────────────────

    private NavMeshAgent _agent;
    private AudioSource _loopSource;

    private State _state = State.Hidden;
    private PlayerManager _target;       // player we spawn next to
    private PlayerManager _chaseTarget;  // player who took the bait
    private float _nextSpawnTime;
    private float _lureDeadline;
    private float _nextVoiceTime;   // when the next stolen voice should play while luring
    private float _chaseDeadline;
    private float _nextLogTime;
    private Vector3 _restPosition;

    // Per-player flashlight-flash bookkeeping, reset on every appearance.
    private class FlashProgress
    {
        public int flashes;      // times the beam has re-entered the Echo
        public bool wasLit;      // beam state last tick, for flash edge detection
    }

    private readonly Dictionary<PlayerManager, FlashProgress> _flashProgress = new();
    private readonly Dictionary<PlayerManager, PlayerFlashlight> _flashlights = new();

    private Animator _lureAnimator;
    private Animator _chaseAnimator;

    private bool HasMorphForms => _lureModel != null && _chaseModel != null;

    // The Echo-voice-pitch upgrade is a PERSONAL upgrade: only the client that owns it
    // hears the pitched-up voice. Each client pitches its own playback by the local
    // player's upgrade value, so nothing about this is networked here.
    private float LocalVoicePitch =>
        PlayerUpgrades.Local != null ? PlayerUpgrades.Local.EchoVoicePitch : 1f;

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

        if (_morpher == null)
            _morpher = GetComponent<ModelMorpher>();

        if (_lureModel != null) _lureAnimator = _lureModel.GetComponentInChildren<Animator>(true);
        if (_chaseModel != null) _chaseAnimator = _chaseModel.GetComponentInChildren<Animator>(true);

        // Start hidden. With morph forms the two models ARE the visuals, so hiding
        // them is enough even when no separate model root is assigned.
        if (_modelRoot != null) _modelRoot.SetActive(false);
        if (_lureModel != null) _lureModel.SetActive(false);
        if (_chaseModel != null) _chaseModel.SetActive(false);
    }

    protected override void OnSpawned(bool asServer)
    {
        if (!isServer) return;

        _nextSpawnTime = Time.time + _respawnCooldown;
        Debug.Log($"[TheEchoAI] Server ready. First appearance allowed in {_respawnCooldown:F0}s.");

        // Map changed mid-appearance — retreat and wait for the new layout.
        if (DungeonGenerator.Instance != null)
            DungeonGenerator.Instance.OnGenerated += Retreat;
    }

    protected override void OnDespawned(bool asServer)
    {
        if (DungeonGenerator.Instance != null)
            DungeonGenerator.Instance.OnGenerated -= Retreat;
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

        // While luring it stands still and turns by hand — let the agent steer
        // rotation only during the chase, or it overrides our manual facing.
        _agent.updateRotation = false;

        // Face the target so the model isn't staring into its corner.
        Vector3 look = _target.transform.position - spawnPos;
        look.y = 0f;
        if (look != Vector3.zero) transform.rotation = Quaternion.LookRotation(look);

        _state = State.Lure;
        _flashProgress.Clear();

        RpcSetVisible(true);

        float voiceLength = PlayStolenVoice();
        _lureDeadline = Time.time + voiceLength + _lureTimeout;
        _nextVoiceTime = Time.time + voiceLength + _voiceRepeatDelay;
    }

    // ── Lure: wait for someone to take the bait ────────────────────────────

    private void TickLure()
    {
        foreach (PlayerManager player in GetDungeonPlayers())
        {
            bool canReact = _reactToAllPlayers || player == _target;
            float distSq = (player.transform.position - transform.position).sqrMagnitude;

            if (distSq <= _triggerRadius * _triggerRadius && canReact && HasLineOfSight(player))
            {
                StartChase(player);
                return;
            }
        }

        // Keep repeating a (different) stolen voice a few seconds after each ends.
        if (Time.time >= _nextVoiceTime)
        {
            float voiceLength = PlayStolenVoice();
            if (voiceLength > 0f)
            {
                _nextVoiceTime = Time.time + voiceLength + _voiceRepeatDelay;
                _lureDeadline = Time.time + voiceLength + _lureTimeout; // keep luring while it talks
            }
            else
            {
                // Nothing left to steal — retry soon, but let the lure time out.
                _nextVoiceTime = Time.time + _voiceRepeatDelay;
            }
        }

        if (Time.time >= _lureDeadline)
            Retreat();
    }

    /// <summary>
    /// Counts flashes (the beam re-entering the Echo) for every player allowed to
    /// scare it. Chase-only, so the beam reaches at any distance. Returns true
    /// (after fleeing) once someone has flashed it enough.
    /// </summary>
    private bool UpdateFlashlightScare()
    {
        foreach (PlayerManager player in GetDungeonPlayers())
        {
            bool canReact = _reactToAllPlayers || player == _chaseTarget;
            FlashProgress progress = GetFlashProgress(player);

            bool lit = canReact && IsLitBy(player);
            if (lit && !progress.wasLit) progress.flashes++; // beam just (re)entered — one flash
            progress.wasLit = lit;

            if (canReact && _flashlightScareFlashes > 0
                && progress.flashes >= _flashlightScareFlashes)
            {
                ScareAway(player);
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// True if the player's flashlight is aimed at any visible part of the Echo.
    /// Samples several points up the body so partial cover (only the legs, only
    /// the head...) still counts, and ignores beam range so distance is no limit.
    /// </summary>
    private bool IsLitBy(PlayerManager player)
    {
        PlayerFlashlight flashlight = GetFlashlight(player);
        if (flashlight == null || !flashlight.IsOn) return false;

        Vector3 playerHead = player.transform.position + Vector3.up * HeadHeight;

        // Offset perpendicular to the line of approach so peeking round a corner
        // (only one side exposed) still lands a sample on the exposed side.
        Vector3 toEcho = transform.position - player.transform.position;
        toEcho.y = 0f;
        Vector3 right = Vector3.Cross(Vector3.up, toEcho).normalized * ScareSampleRadius;

        foreach (float h in ScareSampleHeights)
        {
            Vector3 mid = transform.position + Vector3.up * h;
            for (int side = -1; side <= 1; side++)
            {
                Vector3 sample = mid + right * side;
                if (!flashlight.IsIlluminating(sample, ignoreRange: true)) continue;
                if (!Physics.Linecast(playerHead, sample, _visionBlockers))
                    return true; // this part is both lit and visible
            }
        }
        return false;
    }

    private void ScareAway(PlayerManager player)
    {
        Debug.Log($"[TheEchoAI] Scared away by '{player.name}'s flashlight.");
        RpcPlayScared(transform.position);
        Retreat();
    }

    private FlashProgress GetFlashProgress(PlayerManager player)
    {
        if (!_flashProgress.TryGetValue(player, out FlashProgress progress))
        {
            progress = new FlashProgress();
            _flashProgress[player] = progress;
        }
        return progress;
    }

    private PlayerFlashlight GetFlashlight(PlayerManager player)
    {
        if (!_flashlights.TryGetValue(player, out PlayerFlashlight flashlight) || flashlight == null)
        {
            flashlight = player.GetComponentInChildren<PlayerFlashlight>();
            if (flashlight == null) flashlight = player.GetComponentInParent<PlayerFlashlight>();
            _flashlights[player] = flashlight;
        }
        return flashlight;
    }

    private void StartChase(PlayerManager player)
    {
        Debug.Log($"[TheEchoAI] '{player.name}' took the bait — chasing.");
        _chaseTarget = player;
        _state = State.Chase;
        _chaseDeadline = Time.time + _chaseTimeout;
        _agent.updateRotation = true; // hand rotation back to the agent for the chase
        RpcSetChasing(true);
    }

    // ── Chase: run the player down, hit once, vanish ───────────────────────

    private void TickChase()
    {
        if (!IsValidTarget(_chaseTarget) || Time.time >= _chaseDeadline)
        {
            Retreat();
            return;
        }

        // Mid-hunt the flashlight scares it off from any distance.
        if (UpdateFlashlightScare()) return;

        _agent.SetDestination(_chaseTarget.transform.position);

        float distSq = (_chaseTarget.transform.position - transform.position).sqrMagnitude;
        if (distSq <= _attackRange * _attackRange)
        {
            Debug.Log($"[TheEchoAI] Hit '{_chaseTarget.name}' for {_attackDamage}.");
            _chaseTarget.Damage(_attackDamage);
            RpcPlayScratch();
            Retreat();
        }
    }

    // Named Retreat (not Despawn) to avoid shadowing NetworkIdentity.Despawn — the
    // Echo only hides and parks itself; the network object stays alive.
    private void Retreat()
    {
        if (_state == State.Hidden) return;

        Debug.Log($"[TheEchoAI] Retreating ({_state}). Back in {_respawnCooldown:F0}s.");

        _state = State.Hidden;
        _target = null;
        _chaseTarget = null;
        _flashProgress.Clear();
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

    private PlayerManager GetClosestPlayer(List<PlayerManager> players)
    {
        PlayerManager closest = null;
        float best = float.MaxValue;
        foreach (PlayerManager p in players)
        {
            float d = (p.transform.position - transform.position).sqrMagnitude;
            if (d < best) { best = d; closest = p; }
        }
        return closest;
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

            // Don't mimic whoever's closest — a voice coming from right next to a
            // player gives the trick away. Only drop them if someone else is left.
            PlayerManager closest = GetClosestPlayer(others);
            if (others.Count > 1 && closest != null)
                others.Remove(closest);

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
        _audioSource.pitch = LocalVoicePitch; // personal upgrade — only this client hears it
        _audioSource.Play();
    }

    // ── Presentation (all clients) ─────────────────────────────────────────

    [ObserversRpc(runLocally: true, bufferLast: true)]
    private void RpcSetVisible(bool visible)
    {
        if (_morpher != null)
            _morpher.CancelMorph();

        if (_modelRoot != null)
            _modelRoot.SetActive(visible);

        // Come back in the lure form; the chase form only appears mid-hunt. When
        // hiding, both forms switch off — this is what hides the Echo when there's
        // no separate model root assigned.
        if (HasMorphForms)
        {
            _lureModel.SetActive(visible);
            _chaseModel.SetActive(false);
        }

        if (visible) PlayLoop(_breathingSound);
        else _loopSource.Stop();
    }

    [ObserversRpc(runLocally: true)]
    private void RpcSetChasing(bool chasing)
    {
        // Morph first so the chase model (and its Animator) is active before we
        // try to set parameters on it.
        if (chasing && HasMorphForms && _morpher != null)
            _morpher.Morph(_lureModel, _chaseModel);

        SetChaseAnim(_animator, chasing);
        SetChaseAnim(_lureAnimator, chasing);
        SetChaseAnim(_chaseAnimator, chasing);

        if (chasing)
        {
            if (_screechSound != null) _audioSource.PlayOneShot(_screechSound);
            PlayLoop(_runningSound);
        }
    }

    // Sets IsChasing only where the controller actually has that bool — the two
    // forms have different animators and not all of them need the parameter.
    private static void SetChaseAnim(Animator animator, bool chasing)
    {
        if (animator == null || !animator.isActiveAndEnabled
            || animator.runtimeAnimatorController == null)
            return;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Bool
                && parameter.nameHash == AnimIsChasing)
            {
                animator.SetBool(AnimIsChasing, chasing);
                return;
            }
        }
    }

    [ObserversRpc(runLocally: true)]
    private void RpcPlayScratch()
    {
        if (_scratchSound != null) _audioSource.PlayOneShot(_scratchSound);
    }

    // Played at the spot it fled from — by the time the RPC lands, the Echo's own
    // transform has already been parked back at the rest position.
    [ObserversRpc(runLocally: true)]
    private void RpcPlayScared(Vector3 position)
    {
        if (_scaredSound != null)
            AudioSource.PlayClipAtPoint(_scaredSound, position);
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
