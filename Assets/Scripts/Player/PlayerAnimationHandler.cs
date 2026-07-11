using PurrNet;
using StarterAssets;
using UnityEngine;

/// <summary>
/// Drives a character model's Animator from the owner's movement state, broadcasting the third-person body's triggers.
/// </summary>
public class PlayerAnimationHandler : NetworkBehaviour
{
    [Header("Blend Tree Smoothing")]
    [SerializeField] private float _animationDampTime = 0.1f;

    [Header("Jump / Fall")]
    [SerializeField] private float _spawnGraceTime = 0.3f;

    private static readonly int AnimX = Animator.StringToHash("X");
    private static readonly int AnimY = Animator.StringToHash("Y");
    private static readonly int AnimJump = Animator.StringToHash("Jump");
    private static readonly int AnimLand = Animator.StringToHash("Land");
    private static readonly int AnimIsGrounded = Animator.StringToHash("IsGrounded");
    private static readonly int AnimIsMoving = Animator.StringToHash("IsMoving");

    private const string BlendTreeStateName = "Blend Tree";

    private Animator _animator;
    private bool _isThirdPersonBody;   // the model others see (has HideRenderersForOwner)
    private StarterAssetsInputs _input;
    private FirstPersonController _controller;
    private CharacterController _characterController;

    private float _currentX;
    private float _currentY;
    private float _velocityX;
    private float _velocityY;

    private bool _wasGrounded;       // owner: controller grounded, for take-off
    private bool _wasGroundedAnim;   // any client: animator IsGrounded, for landing
    private float _spawnTimer;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        if (_animator == null) _animator = GetComponentInChildren<Animator>(true);

        // The third-person body is the one flagged with HideRenderersForOwner, the only
        // model others see, so it's the one whose triggers are broadcast.
        _isThirdPersonBody = GetComponent<HideRenderersForOwner>() != null;
    }

    private void Start()
    {
        _input = GetComponentInParent<StarterAssetsInputs>();
        _controller = GetComponentInParent<FirstPersonController>();
        _characterController = GetComponentInParent<CharacterController>();

        _spawnTimer = _spawnGraceTime;
        _wasGrounded = true;
        _wasGroundedAnim = true;
    }

    private void Update()
    {
        // Only the owner drives locomotion and take-off.
        if (isOwner)
        {
            UpdateLocomotion();
            UpdateGrounded();
        }

        // Land is derived from the synced IsGrounded parameter, so it fires in step with
        // the grounded state instead of racing a separately-sent trigger.
        UpdateLand();
    }

    private void UpdateLocomotion()
    {
        bool inBlendTree = IsInBlendTree();
        bool isMoving = _input.move != Vector2.zero;

        _animator.SetBool(AnimIsMoving, isMoving);

        float targetX = inBlendTree ? _input.move.x * GetSprintMultiplier() : 0f;
        float targetY = inBlendTree ? _input.move.y * GetSprintMultiplier() : 0f;

        _currentX = Mathf.SmoothDamp(_currentX, targetX, ref _velocityX, _animationDampTime);
        _currentY = Mathf.SmoothDamp(_currentY, targetY, ref _velocityY, _animationDampTime);

        _animator.SetFloat(AnimX, _currentX);
        _animator.SetFloat(AnimY, _currentY);
    }

    // Owner only: sets the synced IsGrounded bool and fires Jump on take-off.
    private void UpdateGrounded()
    {
        if (_spawnTimer > 0f)
        {
            _spawnTimer -= Time.deltaTime;
            _animator.SetBool(AnimIsGrounded, true);
            return;
        }

        bool grounded = _controller.Grounded;
        _animator.SetBool(AnimIsGrounded, grounded);

        if (_wasGrounded && !grounded && _characterController.velocity.y > 0.1f)
        {
            _controller.SetCanJump(false);
            TriggerOnAllClients(AnimJump);
        }

        _wasGrounded = grounded;

        if (!_controller.CanJump && IsInBlendTree())
            _controller.SetCanJump(true);
    }

    // Every client: keeps Jump/Land in step with the synced IsGrounded bool, clearing a
    // trigger that arrived out of order so it can't linger and mis-fire.
    private void UpdateLand()
    {
        bool grounded = _animator.GetBool(AnimIsGrounded);

        if (_wasGroundedAnim && !grounded)
        {
            _animator.ResetTrigger(AnimLand);
        }
        else if (!_wasGroundedAnim && grounded)
        {
            _animator.ResetTrigger(AnimJump);
            if (isOwner) _controller.SetCanJump(false);
            _animator.SetTrigger(AnimLand);
        }

        _wasGroundedAnim = grounded;
    }

    // Fires a trigger locally, and also broadcasts it from the third-person body.
    private void TriggerOnAllClients(int hash)
    {
        _animator.SetTrigger(hash);

        if (_isThirdPersonBody)
            ServerSyncTrigger(hash);
    }

    [ServerRpc(requireOwnership: true)]
    private void ServerSyncTrigger(int hash)
    {
        SyncTriggerOnClients(hash);
    }

    [ObserversRpc]
    private void SyncTriggerOnClients(int hash)
    {
        if (isOwner) return; // owner already set it locally
        if (!_animator) return;
        _animator.SetTrigger(hash);
    }

    // Animation event at the end of the land clip.
    public void OnLandAnimationEnd()
    {
        if (!isOwner) return;
        _controller.SetCanJump(true);
    }

    private bool IsInBlendTree()
    {
        return _animator.GetCurrentAnimatorStateInfo(0).IsName(BlendTreeStateName);
    }

    private float GetSprintMultiplier()
    {
        return (_input.move != Vector2.zero && _input.sprint) ? 2f : 1f;
    }
}
