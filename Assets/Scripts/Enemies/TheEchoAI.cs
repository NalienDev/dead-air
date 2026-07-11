using System.Collections.Generic;
using PurrNet;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Server-authoritative stalker that lures players with stolen voices, then chases and strikes if they take the bait.
/// </summary>
public class TheEchoAI : NetworkBehaviour
{
    private enum State { Hidden, Lure, Chase }

    [Header("References")]
    [SerializeField] private AudioSource _audioSource;
    [Tooltip("Whole-model parent toggled on spawn. Leave empty when using the morph forms below.")]
    [SerializeField] private GameObject _modelRoot;
    [SerializeField] private Animator _animator;         // gets bool "IsChasing"

    [Header("Morph Forms")]
    [Tooltip("Form shown while luring, with its own Animator.")]
    [SerializeField] private GameObject _lureModel;
    [Tooltip("Form it morphs into when the chase starts, overlapping the lure form.")]
    [SerializeField] private GameObject _chaseModel;
    [Tooltip("Drives the dissolve crossfade between the two forms. Auto-found if empty.")]
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

    [Header("Flashlight Scare")]
    [Tooltip("Flashes needed to scare it away while chasing.")]
    [SerializeField] private int _flashlightScareFlashes = 5;
    [Tooltip("One-shot played whenever the Echo disappears.")]
    [SerializeField] private AudioClip _scaredSound;
    [Tooltip("When false, only the lured target can trigger or scare it; others are ignored.")]
    [SerializeField] private bool _reactToAllPlayers = true;

    [Header("Chasing")]
    [SerializeField] private float _attackRange = 1.5f;
    [SerializeField] private int _attackDamage = 20;
    [Tooltip("Give up if the chase lasts longer than this.")]
    [SerializeField] private float _chaseTimeout = 15f;

    [Header("Vision")]
    [Tooltip("Layers that block sight. Do not include the Player layer.")]
    [SerializeField] private LayerMask _visionBlockers = Physics.DefaultRaycastLayers;

    private static readonly int AnimIsChasing = Animator.StringToHash("IsChasing");
    private const float HeadHeight = 1.6f;

    // Points sampled up the body for the flashlight scare, so partial exposure still counts.
    private static readonly float[] ScareSampleHeights = { 0.2f, 0.7f, 1.2f, 1.6f };
    private const float ScareSampleRadius = 0.35f;

    private NavMeshAgent _agent;
    private CrossfadeLoopPlayer _loopPlayer;

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

    // Personal upgrade: each client pitches its own playback by the local player's value.
    private float LocalVoicePitch =>
        PlayerUpgrades.Local != null ? PlayerUpgrades.Local.EchoVoicePitch : 1f;

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.enabled = false; // no NavMesh exists until the dungeon bakes

        _restPosition = transform.position; // parked here while hidden

        // The agent drives all movement; a live rigidbody would make the hidden Echo fall.
        if (TryGetComponent(out Rigidbody rb))
            rb.isKinematic = true;

        if (_audioSource == null)
            _audioSource = gameObject.AddComponent<AudioSource>();

        // Loops go through a crossfader so starts, stops, and the switch don't click.
        _loopPlayer = GetComponent<CrossfadeLoopPlayer>();
        if (_loopPlayer == null) _loopPlayer = gameObject.AddComponent<CrossfadeLoopPlayer>();

        if (_morpher == null)
            _morpher = GetComponent<ModelMorpher>();

        if (_lureModel != null) _lureAnimator = _lureModel.GetComponentInChildren<Animator>(true);
        if (_chaseModel != null) _chaseAnimator = _chaseModel.GetComponentInChildren<Animator>(true);

        // Start hidden. With morph forms, hiding the two models is enough to hide the Echo.
        if (_modelRoot != null) _modelRoot.SetActive(false);
        if (_lureModel != null) _lureModel.SetActive(false);
        if (_chaseModel != null) _chaseModel.SetActive(false);
    }

    protected override void OnSpawned(bool asServer)
    {
        if (!isServer) return;

        _nextSpawnTime = Time.time + _respawnCooldown;
        Debug.Log($"[TheEchoAI] Server ready. First appearance allowed in {_respawnCooldown:F0}s.");

        // Retreat and wait for the new layout if the map regenerates mid-appearance.
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

    // Waits out the cooldown, picks an isolated target, and appears out of sight near them.
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

        // Move before enabling: the agent can only attach on top of the NavMesh.
        transform.position = spawnPos;
        _agent.enabled = true;

        if (!_agent.isOnNavMesh)
        {
            _agent.enabled = false;
            Status("waiting — spawn point found but the agent could not attach to the NavMesh there.");
            return;
        }

        _agent.Warp(spawnPos);

        // While luring it stands still and turns by hand; the agent only steers rotation during the chase.
        _agent.updateRotation = false;

        // Face the target.
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

    private void TickLure()
    {
        // Give up if the target died, left the dungeon, or reached the safe return zone.
        if (!IsValidTarget(_target))
        {
            Retreat();
            return;
        }

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

        // Keep repeating a different stolen voice a few seconds after each ends.
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
                // Nothing left to steal; retry soon but let the lure time out.
                _nextVoiceTime = Time.time + _voiceRepeatDelay;
            }
        }

        if (Time.time >= _lureDeadline)
            Retreat();
    }

    // Counts flashes for every player allowed to scare it, and flees once one flashes it enough.
    private bool UpdateFlashlightScare()
    {
        foreach (PlayerManager player in GetDungeonPlayers())
        {
            bool canReact = _reactToAllPlayers || player == _chaseTarget;
            FlashProgress progress = GetFlashProgress(player);

            bool lit = canReact && IsLitBy(player);
            if (lit && !progress.wasLit) progress.flashes++; // beam just re-entered: one flash
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

    // True if the player's flashlight is aimed at any visible part of the Echo, sampling
    // up the body so partial cover still counts.
    private bool IsLitBy(PlayerManager player)
    {
        PlayerFlashlight flashlight = GetFlashlight(player);
        if (flashlight == null || !flashlight.IsOn) return false;

        Vector3 playerHead = player.transform.position + Vector3.up * HeadHeight;

        // Offset perpendicular to the approach so peeking round a corner still samples the exposed side.
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
        Debug.Log($"[TheEchoAI] '{player.name}' took the bait, chasing.");
        _chaseTarget = player;
        _state = State.Chase;
        _chaseDeadline = Time.time + _chaseTimeout;
        _agent.updateRotation = true; // hand rotation back to the agent for the chase
        RpcSetChasing(true);
    }

    private void TickChase()
    {
        if (!IsValidTarget(_chaseTarget) || Time.time >= _chaseDeadline)
        {
            Retreat();
            return;
        }

        // The flashlight scares it off from any distance mid-hunt.
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

    // Named Retreat (not Despawn) to avoid shadowing NetworkIdentity.Despawn; the network
    // object stays alive while the Echo hides and parks itself.
    private void Retreat()
    {
        if (_state == State.Hidden) return;

        Debug.Log($"[TheEchoAI] Retreating ({_state}). Back in {_respawnCooldown:F0}s.");

        // Scared cry on every disappearance, played at the spot it fled from.
        RpcPlayScared(transform.position);

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

    // The return gather zone is a safe area: a player inside it can't be lured or chased.
    private static bool IsValidTarget(PlayerManager p)
        => p != null && !p.IsDead && p.IsInsideDungeon() && !ReturnGatherZone.IsInside(p);

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

        // Every candidate must be able to walk to the target, or the Echo spawns on a
        // disconnected island and gets stuck behind invisible walls.
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

    // Plays a stolen voice clip and returns its length; the target's own if _repeatTargetVoice, else another player's.
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

            // Don't mimic the closest player, since a voice right next to them gives the trick away.
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
        _audioSource.pitch = LocalVoicePitch; // personal upgrade, only this client hears it
        _audioSource.Play();
    }

    [ObserversRpc(runLocally: true, bufferLast: true)]
    private void RpcSetVisible(bool visible)
    {
        if (_morpher != null)
            _morpher.CancelMorph();

        if (_modelRoot != null)
            _modelRoot.SetActive(visible);

        // Come back in the lure form; the chase form only appears mid-hunt. Hiding both
        // forms is what hides the Echo when there's no separate model root.
        if (HasMorphForms)
        {
            _lureModel.SetActive(visible);
            _chaseModel.SetActive(false);
        }

        if (visible) PlayLoop(_breathingSound);
        else _loopPlayer.StopLoop(); // fade out so it doesn't snap when it vanishes
    }

    [ObserversRpc(runLocally: true)]
    private void RpcSetChasing(bool chasing)
    {
        // Morph first so the chase model's Animator is active before we set parameters on it.
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

    // Sets IsChasing only where the controller actually has that bool.
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

    // Plays at the spot it fled from, since its transform is parked away by the time this lands.
    [ObserversRpc(runLocally: true)]
    private void RpcPlayScared(Vector3 position)
    {
        if (_scaredSound != null)
            AudioSource.PlayClipAtPoint(_scaredSound, position);
    }

    // Crossfades between loops so changes don't click.
    private void PlayLoop(AudioClip clip)
    {
        if (clip == null) { _loopPlayer.StopLoop(); return; }
        _loopPlayer.PlayLoop(clip);
    }

    // Throttled status log so blocking conditions are visible without spamming.
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
