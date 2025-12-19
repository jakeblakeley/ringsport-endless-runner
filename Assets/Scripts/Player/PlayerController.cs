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
        [SerializeField] private AudioClip footstepSound;
        [SerializeField] private float footstepInterval = 0.3f;

        private AudioSource sfxAudioSource;
        private AudioSource footstepAudioSource;
        private float footstepTimer = 0f;

        // Cached manager references for performance
        private GameManager gameManager;
        private UIManager uiManager;

        public float ForwardSpeed => staminaSystem.IsSprinting ? forwardSpeed * sprintMultiplier : forwardSpeed;
        public bool IsGrounded => isGrounded;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            playerInput = GetComponent<PlayerInput>();

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
            if (gameManager?.CurrentState != GameState.Playing || isMovementPaused)
                return;

            HandleGroundCheck();
            HandleLaneMovement();
            HandleGravity();
            HandleFootsteps();
            staminaSystem.Update(Time.deltaTime); // Delegate to stamina system

            // Only move in X (lanes) and Y (jump/gravity), not Z
            Vector3 movement = new Vector3(velocity.x, velocity.y, 0f);
            characterController.Move(movement * Time.deltaTime);

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

        private void HandleLaneMovement()
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
            if (Time.time - lastInputTime > inputCooldown)
            {
                if (moveInput.x > 0.5f && currentLane < 1)
                {
                    currentLane++;
                    targetLaneX = currentLane * laneDistance;
                    lastInputTime = Time.time;
                }
                else if (moveInput.x < -0.5f && currentLane > -1)
                {
                    currentLane--;
                    targetLaneX = currentLane * laneDistance;
                    lastInputTime = Time.time;
                }
            }

            // Smooth lane transition
            float currentX = transform.position.x;
            float newX = Mathf.Lerp(currentX, targetLaneX, laneChangeSpeed * Time.deltaTime);

            // Prevent division by zero/very small deltaTime which can cause NaN
            if (Time.deltaTime > 0.0001f)
            {
                velocity.x = (newX - currentX) / Time.deltaTime;
            }
            else
            {
                velocity.x = 0f;
            }
        }

        private void HandleGravity()
        {
            velocity.y += gravity * Time.deltaTime;
        }

        private void HandleFootsteps()
        {
            // Only play footsteps when grounded and game is active
            if (!isGrounded || footstepSound == null || footstepAudioSource == null)
            {
                footstepTimer = 0f;
                return;
            }

            footstepTimer += Time.deltaTime;

            // Adjust footstep rate based on sprint state (faster footsteps when sprinting)
            float currentInterval = staminaSystem.IsSprinting ? footstepInterval * 0.6f : footstepInterval;

            if (footstepTimer >= currentInterval)
            {
                footstepAudioSource.PlayOneShot(footstepSound);
                footstepTimer = 0f;
            }
        }

        private void OnJump(InputAction.CallbackContext context)
        {
            Debug.Log($"Jump pressed! isGrounded: {isGrounded}, velocity.y: {velocity.y}");

            if (isGrounded)
            {
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
                Debug.Log($"Jumping! New velocity.y: {velocity.y}");

                // Play jump sound
                if (jumpSound != null && sfxAudioSource != null)
                    sfxAudioSource.PlayOneShot(jumpSound);
            }
        }

        private void OnMobileJump()
        {
            Debug.Log($"Mobile jump triggered! isGrounded: {isGrounded}, velocity.y: {velocity.y}");

            if (isGrounded)
            {
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
                Debug.Log($"Mobile jumping! New velocity.y: {velocity.y}");

                // Play jump sound
                if (jumpSound != null && sfxAudioSource != null)
                    sfxAudioSource.PlayOneShot(jumpSound);
            }
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
