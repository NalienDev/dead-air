using PurrNet;
using StarterAssets;
using UnityEngine;

/// <summary>
/// Reports walking/sprinting footstep noise to the server so the blind Conductor can
/// hear it. Reads movement + sprint straight from the StarterAssets FirstPersonController
/// rig used by the real player prefab (PlayerCapsule) — PlayerMovement's noise code only
/// exists on the PlayerTest prefab, which is why sprinting was inaudible in the game.
///
/// Attach next to PlayerManager on the PlayerCapsule prefab. Owner-only.
/// </summary>
[RequireComponent(typeof(PlayerManager))]
public class PlayerFootstepNoise : NetworkBehaviour
{
    [Tooltip("Seconds between footstep noises while walking.")]
    [SerializeField] private float _walkStepInterval = 0.5f;
    [Tooltip("Seconds between footstep noises while sprinting.")]
    [SerializeField] private float _sprintStepInterval = 0.3f;
    [Tooltip("How loud a walking step is to the Conductor (0..1). Usually below its " +
             "hearing threshold — walking should be reasonably safe.")]
    [SerializeField, Range(0f, 1f)] private float _walkNoise = 0.25f;
    [Tooltip("How loud a sprinting step is (0..1). Loud enough to draw the Conductor.")]
    [SerializeField, Range(0f, 1f)] private float _sprintNoise = 0.55f;

    private PlayerManager _playerManager;
    private StarterAssetsInputs _input;
    private FirstPersonController _controller;
    private float _stepTimer;

    private void Awake()
    {
        _playerManager = GetComponent<PlayerManager>();
        _input = GetComponent<StarterAssetsInputs>();
        _controller = GetComponent<FirstPersonController>();
    }

    private void Update()
    {
        if (!isOwner || _input == null) return;
        if (_playerManager.IsDead) return;

        bool grounded = _controller == null || _controller.Grounded;
        bool moving = grounded && _input.move != Vector2.zero;

        if (!moving)
        {
            _stepTimer = 0f;
            return;
        }

        _stepTimer -= Time.deltaTime;
        if (_stepTimer > 0f) return;

        bool sprinting = _input.sprint;
        _stepTimer = sprinting ? _sprintStepInterval : _walkStepInterval;
        _playerManager.ReportNoise(sprinting ? _sprintNoise : _walkNoise);
    }
}
