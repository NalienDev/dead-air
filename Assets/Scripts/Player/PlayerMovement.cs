using PurrNet;
using UnityEngine;

public class PlayerMovement : NetworkIdentity
{
    [Header("Movement")]
    [SerializeField] private float _speed = 3f;
    [Tooltip("Speed multiplier while holding the sprint key.")]
    [SerializeField] private float _sprintMultiplier = 1.8f;
    [SerializeField] private KeyCode _sprintKey = KeyCode.LeftShift;

    [Header("Footstep Noise (heard by the Conductor)")]
    [Tooltip("Seconds between footstep noises while walking.")]
    [SerializeField] private float _walkStepInterval = 0.5f;
    [Tooltip("Seconds between footstep noises while sprinting.")]
    [SerializeField] private float _sprintStepInterval = 0.3f;
    [Tooltip("How loud a walking step is to the Conductor (0..1). Usually below its " +
             "hearing threshold — walking should be reasonably safe.")]
    [SerializeField, Range(0f, 1f)] private float _walkNoise = 0.25f;
    [Tooltip("How loud a sprinting step is (0..1). Loud enough to draw an investigation.")]
    [SerializeField, Range(0f, 1f)] private float _sprintNoise = 0.55f;

    private PlayerManager _playerManager;
    private float _stepTimer;

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
        float speed = sprinting ? _speed * _sprintMultiplier : _speed;

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
}
