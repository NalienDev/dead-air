using PurrNet;
using UnityEngine;

/// <summary>
/// Owner-driven player movement with sprinting and footstep noise reporting.
/// </summary>
public class PlayerMovement : NetworkIdentity
{
    [Header("Movement")]
    [SerializeField] private float _speed = 3f;
    [Tooltip("Speed multiplier while holding the sprint key.")]
    [SerializeField] private float _sprintMultiplier = 1.8f;
    [SerializeField] private KeyCode _sprintKey = KeyCode.LeftShift;

    [Header("Footstep Noise")]
    [Tooltip("Seconds between footstep noises while walking.")]
    [SerializeField] private float _walkStepInterval = 0.5f;
    [Tooltip("Seconds between footstep noises while sprinting.")]
    [SerializeField] private float _sprintStepInterval = 0.3f;
    [Tooltip("How loud a walking step is to the Conductor.")]
    [SerializeField, Range(0f, 1f)] private float _walkNoise = 0.25f;
    [Tooltip("How loud a sprinting step is to the Conductor.")]
    [SerializeField, Range(0f, 1f)] private float _sprintNoise = 0.55f;

    private PlayerManager _playerManager;
    private float _stepTimer;

    // Additive walk-speed bonus from upgrades. Set by PlayerUpgrades (server-synced).
    private float _bonusSpeed;

    // Hit-stun: multiplier applied on top of everything else (1 = normal, 0.3 = stunned).
    private float _stunMultiplier = 1f;
    private Coroutine _stunCoroutine;

    public void SetBonusSpeed(float bonus) => _bonusSpeed = Mathf.Max(0f, bonus);

    protected override void OnSpawned()
    {
        base.OnSpawned();
        enabled = isOwner;
        _playerManager = GetComponent<PlayerManager>();
    }

    private void Update()
    {
        Vector3 moveVector = new Vector3(Input.GetAxis("Horizontal"), 0, Input.GetAxis("Vertical"));

        bool sprinting = Input.GetKey(_sprintKey);
        float baseSpeed = (_speed + _bonusSpeed) * _stunMultiplier;
        float speed = sprinting ? baseSpeed * _sprintMultiplier : baseSpeed;

        transform.position += moveVector * (Time.deltaTime * speed);

        HandleFootstepNoise(moveVector.sqrMagnitude > 0.001f, sprinting);
    }

    private void HandleFootstepNoise(bool moving, bool sprinting)
    {
        if (!moving)
        {
            _stepTimer = 0f;
            return;
        }

        _stepTimer -= Time.deltaTime;
        if (_stepTimer > 0f) return;

        _stepTimer = sprinting ? _sprintStepInterval : _walkStepInterval;
        _playerManager?.ReportNoise(sprinting ? _sprintNoise : _walkNoise);
    }

    private void OnDisable()
    {
        if (_stunCoroutine != null)
        {
            StopCoroutine(_stunCoroutine);
            _stunCoroutine = null;
        }
        _stunMultiplier = 1f;
    }

    // Reduces speed to a fraction of normal for a duration; resets the timer if already stunned.
    public void ApplyHitStun(float multiplier = 0.3f, float duration = 2f)
    {
        if (_stunCoroutine != null) StopCoroutine(_stunCoroutine);
        _stunCoroutine = StartCoroutine(StunRoutine(multiplier, duration));
    }

    private System.Collections.IEnumerator StunRoutine(float multiplier, float duration)
    {
        _stunMultiplier = multiplier;
        yield return new WaitForSeconds(duration);
        _stunMultiplier = 1f;
        _stunCoroutine = null;
    }
}
