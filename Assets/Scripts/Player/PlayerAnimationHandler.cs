using PurrNet;
using StarterAssets;
using UnityEngine;

/// <summary>
/// Drives one character model's Animator from the owner's movement state.
///
/// This runs on every model the player has (FP hands, FP body, TP body), but only
/// ONE model — the third-person body — is networked. That model is identified by
/// its <see cref="HideRenderersForOwner"/> component:
///   • Its sibling NetworkAnimator (auto-sync) replicates the parameters this
///     script writes to the Animator, so observers see the same locomotion.
///   • Its trigger events (Jump/Land) are broadcast over the network.
///   • HideRenderersForOwner hides it on the owning client (who sees their FP
///     models instead) while the model stays ACTIVE so the animator keeps syncing.
///
/// The first-person models are local-only: their NetworkAnimator auto-sync is off
/// and their triggers are set locally, because no one else ever sees them.
/// </summary>
public class PlayerAnimationHandler : NetworkBehaviour
{
    [Header("Blend Tree Smoothing")]
    [SerializeField] private float _animationDampTime = 0.1f;

    [Header("Jump / Fall")]
    [SerializeField] private float _spawnGraceTime = 0.3f;
    [SerializeField] private float _minAirtimeBeforeLand = 0.15f;

    [Header("Landing Prediction")]
    [SerializeField] private float _landPredictionDistance = 0.6f;
    [SerializeField] private LayerMask _groundLayers;

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

    private bool _wasGrounded;
    private bool _isAirborne;
    private bool _landTriggered;
    private float _airTimer;
    private float _spawnTimer;

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        if (_animator == null) _animator = GetComponentInChildren<Animator>(true);

        // The third-person body is the one flagged with HideRenderersForOwner —
        // that's the only model other players see, so it's the one whose triggers
        // need to be broadcast. Its sibling NetworkAnimator syncs the rest.
        _isThirdPersonBody = GetComponent<HideRenderersForOwner>() != null;
    }

    private void Start()
    {
        _input = GetComponentInParent<StarterAssetsInputs>();
        _controller = GetComponentInParent<FirstPersonController>();
        _characterController = GetComponentInParent<CharacterController>();

        _spawnTimer = _spawnGraceTime;
        _wasGrounded = true;
    }

    private void Update()
    {
        // Only the owner drives animation logic
        if (!isOwner) return;

        UpdateLocomotion();
        UpdateAirborne();
    }

    // ── Locomotion ────────────────────────────────────────────────────────────

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

    // ── Airborne ──────────────────────────────────────────────────────────────

    private void UpdateAirborne()
    {
        if (_spawnTimer > 0f)
        {
            _spawnTimer -= Time.deltaTime;
            _wasGrounded = _controller.Grounded;
            _animator.SetBool(AnimIsGrounded, true);
            return;
        }

        bool grounded = _controller.Grounded;
        _animator.SetBool(AnimIsGrounded, grounded);

        // Lift-off
        if (_wasGrounded && !grounded)
        {
            _isAirborne = true;
            _landTriggered = false;
            _airTimer = 0f;

            if (_characterController.velocity.y > 0.1f)
            {
                _controller.SetCanJump(false);
                TriggerOnAllClients(AnimJump);
            }
        }

        // While airborne
        if (_isAirborne)
        {
            _airTimer += Time.deltaTime;

            bool canTriggerLand = _airTimer >= _minAirtimeBeforeLand && !_landTriggered;

            if (canTriggerLand && _characterController.velocity.y < 0f && IsGroundClose())
                TriggerLand();

            if (grounded)
            {
                if (!_landTriggered)
                    TriggerLand();

                _controller.SetCanJump(false);
                _isAirborne = false;
            }
        }

        _wasGrounded = grounded;

        if (!_controller.CanJump && IsInBlendTree())
            _controller.SetCanJump(true);
    }

    // ── Trigger Sync ──────────────────────────────────────────────────────────

    /// <summary>
    /// Fires a trigger locally. On the third-person body it is also broadcast so
    /// other clients play it; the FP models set it locally only (nobody else sees them).
    /// </summary>
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
        // Owner already set it locally
        if (isOwner) return;
        if (!_animator) return;
        _animator.SetTrigger(hash);
    }

    // ── Animation Events ──────────────────────────────────────────────────────

    public void OnLandAnimationEnd()
    {
        if (!isOwner) return;
        _controller.SetCanJump(true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void TriggerLand()
    {
        _landTriggered = true;
        TriggerOnAllClients(AnimLand);
    }

    private bool IsInBlendTree()
    {
        return _animator.GetCurrentAnimatorStateInfo(0).IsName(BlendTreeStateName);
    }

    private float GetSprintMultiplier()
    {
        return (_input.move != Vector2.zero && _input.sprint) ? 2f : 1f;
    }

    private bool IsGroundClose()
    {
        return Physics.Raycast(
            transform.position,
            Vector3.down,
            _landPredictionDistance,
            _groundLayers,
            QueryTriggerInteraction.Ignore);
    }
}
