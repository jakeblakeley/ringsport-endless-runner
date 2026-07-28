using UnityEngine;
using UnityEngine.InputSystem;
using RingSport.Core;
using RingSport.Level;
using RingSport.UI;
using RingSport.Input;

namespace RingSport.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float forwardSpeed = 10f;
        [SerializeField] private float sprintMultiplier = 1.5f;
        [SerializeField] private float laneDistance = 3f;
        [SerializeField] private float laneChangeSpeed = 10f;

        [Header("Jump Settings")]
        [SerializeField] private float jumpHeight = 2f;
        [SerializeField] private float gravity = -20f;

        [Header("Sprint Stamina Settings")]
        [SerializeField] private float maxSprintDuration = 5f;
        [SerializeField] private float sprintDrainRate = 1f;
        [SerializeField] private float sprintRefillRate = 1f;

        private CharacterController characterController;
        private PlayerInput playerInput;
        private InputAction moveAction;
        private InputAction jumpAction;
        private InputAction sprintAction;
        private MobileInputHandler mobileInputHandler;
        private PlayerAnimator playerAnimator;
        private PlayerRagdoll playerRagdoll;

        private Vector3 velocity;
        private bool isGrounded;
        private float targetLaneX = 0f;
        private int currentLane = 0; // -1 = left, 0 = center, 1 = right
        private bool isMovementPaused = false;
        private float lastInputTime = -1f;
        private float inputCooldown = 0.2f;

        // Stamina system for sprint management
        private PlayerStaminaSystem staminaSystem;

        [Header("Audio Settings")]
        [SerializeField] private AudioClip jumpSound;
        [SerializeField] private AudioClip footstepLoop;
        [SerializeField] [Range(0f, 1f)] private float footstepVolume = 0.5f;
        [SerializeField] private float baseFootstepPitch = 1.0f;
        [SerializeField] private float sprintPitchMultiplier = 1.3f;

        private AudioSource sfxAudioSource;
        private AudioSource footstepAudioSource;

        // Cached manager references for performance
        private GameManager gameManager;
        private UIManager uiManager;

        public float ForwardSpeed => staminaSystem.IsSprinting ? forwardSpeed * sprintMultiplier : forwardSpeed;
        public bool IsGrounded => isGrounded;
        public PlayerAnimator Animations => playerAnimator;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            playerInput = GetComponent<PlayerInput>();
            playerAnimator = GetComponentInChildren<PlayerAnimator>(true);
            playerRagdoll = GetComponentInChildren<PlayerRagdoll>(true);

            // Get or add MobileInputHandler
            mobileInputHandler = GetComponent<MobileInputHandler>();
            if (mobileInputHandler == null)
            {
                mobileInputHandler = gameObject.AddComponent<MobileInputHandler>();
                Debug.Log("MobileInputHandler added to player");
            }

            // Initialize stamina system
            staminaSystem = new PlayerStaminaSystem(maxSprintDuration, sprintDrainRate, sprintRefillRate);

            // Setup audio sources
            sfxAudioSource = gameObject.AddComponent<AudioSource>();
            sfxAudioSource.playOnAwake = false;

            footstepAudioSource = gameObject.AddComponent<AudioSource>();
            footstepAudioSource.playOnAwake = false;
            footstepAudioSource.loop = true;
            footstepAudioSource.clip = footstepLoop;
            footstepAudioSource.volume = footstepVolume;

            if (playerInput == null)
            {
                Debug.LogWarning("PlayerInput component not found, adding one. Please add PlayerInput manually and assign the InputSystem_Actions asset!");
                playerInput = gameObject.AddComponent<PlayerInput>();
            }

            // Make sure we have the actions asset
            if (playerInput.actions == null)
            {
                Debug.LogError("PlayerInput.actions is null! Please assign InputSystem_Actions asset to PlayerInput component.");
                return;
            }

            SetupInputActions();
        }

        private void Start()
        {
            // Cache manager references for performance
            gameManager = GameManager.Instance;
            uiManager = UIManager.Instance;

            // Initialize stamina system with UI manager
            staminaSystem.Initialize(uiManager);
        }

        private void SetupInputActions()
        {
            var actionMap = playerInput.actions.FindActionMap("Player");

            if (actionMap == null)
            {
                Debug.LogError("Player action map not found!");
                return;
            }

            moveAction = actionMap.FindAction("Move");
            jumpAction = actionMap.FindAction("Jump");
            sprintAction = actionMap.FindAction("Sprint");

            if (jumpAction != null)
            {
                jumpAction.performed += OnJump;
                Debug.Log("Jump action registered successfully");
            }
            else
            {
                Debug.LogError("Jump action not found in Player action map!");
            }

            if (sprintAction != null)
            {
                sprintAction.performed += OnSprintStarted;
                sprintAction.canceled += OnSprintCanceled;
            }
        }

        private void OnEnable()
        {
            playerInput?.ActivateInput();

            // Subscribe to mobile input events
            if (mobileInputHandler != null)
            {
                mobileInputHandler.OnJumpTriggered += OnMobileJump;
                mobileInputHandler.OnSprintStarted += OnMobileSprint;
                mobileInputHandler.OnSprintEnded += OnMobileSprintEnded;
            }
        }

        private void OnDisable()
        {
            playerInput?.DeactivateInput();

            if (jumpAction != null)
            {
                jumpAction.performed -= OnJump;
            }

            if (sprintAction != null)
            {
                sprintAction.performed -= OnSprintStarted;
                sprintAction.canceled -= OnSprintCanceled;
            }

            // Unsubscribe from mobile input events
            if (mobileInputHandler != null)
            {
                mobileInputHandler.OnJumpTriggered -= OnMobileJump;
                mobileInputHandler.OnSprintStarted -= OnMobileSprint;
                mobileInputHandler.OnSprintEnded -= OnMobileSprintEnded;
            }
        }

        private void Update()
        {
            // Handle footsteps regardless of game state (so it can stop when not playing)
            HandleFootsteps();

            // Drive animation in every state (idle at home, death pose on game over, etc.)
            UpdateAnimation();

            // Allow movement during Playing and MiniLevel states
            bool isValidState = gameManager?.CurrentState == GameState.Playing ||
                               gameManager?.CurrentState == GameState.MiniLevel;

            if (!isValidState || isMovementPaused)
                return;

            // Use unscaled delta time during mini-levels (TimeScale may be 0)
            float deltaTime = gameManager?.CurrentState == GameState.MiniLevel
                ? Time.unscaledDeltaTime
                : Time.deltaTime;

            HandleGroundCheck();
            HandleLaneMovement(deltaTime);
            HandleGravity(deltaTime);
            staminaSystem.Update(deltaTime); // Delegate to stamina system

            // Only move in X (lanes) and Y (jump/gravity), not Z
            Vector3 movement = new Vector3(velocity.x, velocity.y, 0f);
            characterController.Move(movement * deltaTime);

            // Reset X velocity after moving
            velocity.x = 0f;
        }

        private void HandleGroundCheck()
        {
            isGrounded = characterController.isGrounded;

            if (isGrounded && velocity.y < 0)
            {
                velocity.y = -2f;
            }

            // Debug ground state occasionally
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"Ground check - isGrounded: {isGrounded}, position.y: {transform.position.y}, velocity.y: {velocity.y}");
            }
        }

        private void HandleLaneMovement(float deltaTime)
        {
            // Read input from keyboard/gamepad
            Vector2 moveInput = moveAction.ReadValue<Vector2>();

            // Touch takes priority when it has input
            if (mobileInputHandler != null)
            {
                Vector2 mobileMove = mobileInputHandler.MoveInput;
                if (mobileMove != Vector2.zero)
                {
                    moveInput = mobileMove;
                }
            }

            // Discrete lane switching with cooldown
            if (Time.unscaledTime - lastInputTime > inputCooldown)
            {
                if (moveInput.x > 0.5f && currentLane < 1)
                {
                    currentLane++;
                    targetLaneX = currentLane * laneDistance;
                    lastInputTime = Time.unscaledTime;
                    NotifyLaneChange(toRight: true);
                }
                else if (moveInput.x < -0.5f && currentLane > -1)
                {
                    currentLane--;
                    targetLaneX = currentLane * laneDistance;
                    lastInputTime = Time.unscaledTime;
                    NotifyLaneChange(toRight: false);
                }
            }

            // Smooth lane transition
            float currentX = transform.position.x;
            float newX = Mathf.Lerp(currentX, targetLaneX, laneChangeSpeed * deltaTime);

            // Prevent division by zero/very small deltaTime which can cause NaN
            if (deltaTime > 0.0001f)
            {
                velocity.x = (newX - currentX) / deltaTime;
            }
            else
            {
                velocity.x = 0f;
            }
        }

        private void HandleGravity(float deltaTime)
        {
            velocity.y += gravity * deltaTime;
        }

        private void NotifyLaneChange(bool toRight)
        {
            // During normal runs the locomotion strafe lean covers lane changes;
            // in mini-levels (dodging steaks) play a discrete sideways dodge.
            if (gameManager?.CurrentState == GameState.MiniLevel)
                playerAnimator?.TriggerDodge(toRight);
        }

        private void UpdateAnimation()
        {
            if (playerAnimator == null)
                return;

            // The animated model is hidden while the death ragdoll owns the body
            if (playerRagdoll != null && playerRagdoll.IsActive)
                return;

            // Time.timeScale is 0 during the pre-run countdown; don't run in place
            // while the world is frozen.
            bool isRunning = gameManager?.CurrentState == GameState.Playing &&
                             !isMovementPaused &&
                             Time.timeScale > 0f;

            // Signed -1..1 lean while chasing the target lane, matching the lerp
            // in HandleLaneMovement: full deflection on a lane switch, easing out
            // as the player converges on the lane.
            float strafe = laneDistance > 0f
                ? Mathf.Clamp((targetLaneX - transform.position.x) / laneDistance, -1f, 1f)
                : 0f;

            // Sprint is a distinct gait tier in the blend tree, not a faster run.
            // Mini-levels stay at idle; lane changes there play the dodge states
            // (see NotifyLaneChange) instead of the locomotion blend.
            float moveSpeed = isRunning ? (staminaSystem.IsSprinting ? 2f : 1f) : 0f;

            // Scale the cycle with level speed so feet stay in sync with the ground
            float levelSpeedMultiplier = LevelGenerator.Instance?.GetCurrentConfig()?.SpeedMultiplier ?? 1f;

            playerAnimator.UpdateLocomotion(
                moveSpeed,
                isRunning ? strafe : 0f,
                isRunning ? levelSpeedMultiplier : 1f,
                Time.unscaledDeltaTime);
            playerAnimator.SetGrounded(isGrounded);
        }

        private void HandleFootsteps()
        {
            if (footstepAudioSource == null || footstepLoop == null)
                return;

            // Determine if footsteps should play
            bool shouldPlay = gameManager?.CurrentState == GameState.Playing &&
                              !isMovementPaused &&
                              isGrounded;

            if (shouldPlay)
            {
                // Calculate pitch based on level speed multiplier and sprint state
                float levelSpeedMultiplier = LevelGenerator.Instance?.GetCurrentConfig()?.SpeedMultiplier ?? 1f;
                float sprintMultiplier = staminaSystem.IsSprinting ? sprintPitchMultiplier : 1f;
                float targetPitch = baseFootstepPitch * levelSpeedMultiplier * sprintMultiplier;

                footstepAudioSource.pitch = targetPitch;

                // Start playing if not already
                if (!footstepAudioSource.isPlaying)
                    footstepAudioSource.Play();
            }
            else
            {
                // Stop playing if currently playing
                if (footstepAudioSource.isPlaying)
                    footstepAudioSource.Stop();
            }
        }

        private void OnJump(InputAction.CallbackContext context)
        {
            TryJump();
        }

        private void OnMobileJump()
        {
            TryJump();
        }

        private void TryJump()
        {
            if (!isGrounded)
                return;

            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            Debug.Log($"Jumping! New velocity.y: {velocity.y}");

            playerAnimator?.TriggerJump();

            // Play jump sound
            if (jumpSound != null && sfxAudioSource != null)
                sfxAudioSource.PlayOneShot(jumpSound);
        }

        private void OnMobileSprint()
        {
            Debug.Log("Mobile sprint started!");

            // Delegate to stamina system
            if (staminaSystem.CanSprint())
            {
                staminaSystem.IsSprinting = true;
            }
        }

        private void OnMobileSprintEnded()
        {
            Debug.Log("Mobile sprint ended!");

            // Delegate to stamina system
            staminaSystem.IsSprinting = false;
        }

        private void OnSprintStarted(InputAction.CallbackContext context)
        {
            // Delegate to stamina system
            if (staminaSystem.CanSprint())
            {
                staminaSystem.IsSprinting = true;
            }
        }

        private void OnSprintCanceled(InputAction.CallbackContext context)
        {
            // Delegate to stamina system
            staminaSystem.IsSprinting = false;
        }

        public void Stop()
        {
        }

        public void Resume()
        {
        }

        public void ResetPosition()
        {
            currentLane = 0;
            targetLaneX = 0f;
            velocity = Vector3.zero;

            // Disable CharacterController to allow direct position change
            characterController.enabled = false;
            transform.position = new Vector3(0f, transform.position.y, transform.position.z);
            characterController.enabled = true;

            // Reset stamina system
            staminaSystem.Reset();

            // Clear death/clamber pose from the previous attempt
            playerRagdoll?.Clear();
            playerAnimator?.ResetToLocomotion();
        }

        /// <summary>Called by GameManager when the player fails a level.</summary>
        public void PlayDeathAnimation()
        {
            if (playerRagdoll != null && playerRagdoll.HasRagdoll)
            {
                playerRagdoll.ActivateRagdoll();
            }
            else
            {
                playerAnimator?.TriggerDeath();
            }
        }

        public void PauseMovement()
        {
            isMovementPaused = true;

            // Force stop sprinting and unsubscribe from sprint events
            staminaSystem.IsSprinting = false;
            if (sprintAction != null)
            {
                sprintAction.performed -= OnSprintStarted;
                sprintAction.canceled -= OnSprintCanceled;
            }

            // Stop footsteps immediately
            if (footstepAudioSource != null && footstepAudioSource.isPlaying)
                footstepAudioSource.Stop();
        }

        public void ResumeMovement()
        {
            isMovementPaused = false;

            // Resubscribe to sprint events
            if (sprintAction != null)
            {
                sprintAction.performed -= OnSprintStarted; // Remove first to avoid duplicates
                sprintAction.canceled -= OnSprintCanceled;
                sprintAction.performed += OnSprintStarted;
                sprintAction.canceled += OnSprintCanceled;
            }
        }

        public System.Collections.IEnumerator AnimateOverObstacle(Vector3 obstaclePosition, float obstacleHeight)
        {
            float duration = 0.2f;
            float elapsed = 0f;

            Vector3 startPosition = transform.position;

            // Clamber is done - leap over the top of the palisade
            playerAnimator?.SetClambering(false);
            playerAnimator?.TriggerVault();

            // Calculate arc height: distance from current player position to top of obstacle + clearance
            float clearanceHeight = 0.5f; // Small clearance above obstacle
            float obstacleTop = obstaclePosition.y + obstacleHeight;
            float arcHeight = (obstacleTop - startPosition.y) + clearanceHeight;

            // Ensure arc height is positive (in case player is already above obstacle)
            arcHeight = Mathf.Max(arcHeight, 0.5f);

            // Target is slightly past the obstacle
            Vector3 targetPosition = new Vector3(
                startPosition.x, // Keep current lane
                startPosition.y, // Return to original height
                startPosition.z  // Stay at same Z (world scrolls, not player)
            );

            Debug.Log($"Animating over obstacle - Start Y: {startPosition.y}, Obstacle Top: {obstacleTop}, Arc Height: {arcHeight}");

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime; // Use unscaled time for consistency
                float t = elapsed / duration;

                // Parabolic arc: y = -4 * height * t * (t - 1)
                float arcProgress = -4f * arcHeight * t * (t - 1f);

                // Interpolate position with arc
                Vector3 newPosition = Vector3.Lerp(startPosition, targetPosition, t);
                newPosition.y += arcProgress;

                transform.position = newPosition;

                yield return null;
            }

            // Ensure we end at exact target position
            transform.position = targetPosition;
        }
    }
}
