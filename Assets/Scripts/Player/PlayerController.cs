using UnityEngine;
using UnityEngine.InputSystem;
using RingSport.Core;
using RingSport.Level;
using RingSport.UI;

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

        private Vector3 velocity;
        private bool isGrounded;
        private float targetLaneX = 0f;
        private int currentLane = 0; // -1 = left, 0 = center, 1 = right
        private bool isSprinting = false;
        private bool isStopped = false;
        private bool isMovementPaused = false;
        private float lastInputTime = -1f;
        private float inputCooldown = 0.2f;

        // Sprint stamina tracking
        private float currentSprintStamina;
        private bool isSprintExhausted = false;

        public float ForwardSpeed => isSprinting ? forwardSpeed * sprintMultiplier : forwardSpeed;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            playerInput = GetComponent<PlayerInput>();

            // Initialize sprint stamina to full
            currentSprintStamina = maxSprintDuration;

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
        }

        private void Update()
        {
            if (GameManager.Instance?.CurrentState != GameState.Playing || isMovementPaused)
                return;

            HandleGroundCheck();
            HandleLaneMovement();
            HandleGravity();
            HandleSprintStamina();

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
            Vector2 moveInput = moveAction.ReadValue<Vector2>();

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
            velocity.x = (newX - currentX) / Time.deltaTime;
        }

        private void HandleGravity()
        {
            velocity.y += gravity * Time.deltaTime;
        }

        private void HandleSprintStamina()
        {
            if (isSprinting && !isSprintExhausted)
            {
                // Drain stamina while sprinting
                currentSprintStamina -= sprintDrainRate * Time.deltaTime;

                if (currentSprintStamina <= 0f)
                {
                    currentSprintStamina = 0f;
                    isSprintExhausted = true;
                    isSprinting = false; // Force stop sprinting
                }
            }
            else if (!isSprinting)
            {
                // Refill stamina when not sprinting
                currentSprintStamina += sprintRefillRate * Time.deltaTime;

                if (currentSprintStamina >= maxSprintDuration)
                {
                    currentSprintStamina = maxSprintDuration;

                    // Unlock sprint once fully refilled
                    if (isSprintExhausted)
                    {
                        isSprintExhausted = false;
                    }
                }
            }

            // Update UI
            if (UIManager.Instance != null)
            {
                float fillAmount = currentSprintStamina / maxSprintDuration;
                UIManager.Instance.UpdateSprintBar(fillAmount, isSprintExhausted);
            }
        }

        private void OnJump(InputAction.CallbackContext context)
        {
            Debug.Log($"Jump pressed! isGrounded: {isGrounded}, velocity.y: {velocity.y}");

            if (isGrounded)
            {
                velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
                Debug.Log($"Jumping! New velocity.y: {velocity.y}");
            }
        }

        private void OnSprintStarted(InputAction.CallbackContext context)
        {
            // Only allow sprinting if not exhausted
            if (!isSprintExhausted)
            {
                isSprinting = true;
            }
        }

        private void OnSprintCanceled(InputAction.CallbackContext context)
        {
            isSprinting = false;
        }

        public void Stop()
        {
            isStopped = true;
        }

        public void Resume()
        {
            isStopped = false;
        }

        public void ResetPosition()
        {
            currentLane = 0;
            targetLaneX = 0f;
            transform.position = new Vector3(0f, transform.position.y, transform.position.z);
            velocity = Vector3.zero;
            isSprinting = false;
            isStopped = false;

            // Reset sprint stamina to full
            currentSprintStamina = maxSprintDuration;
            isSprintExhausted = false;
        }

        public void PauseMovement()
        {
            isMovementPaused = true;

            // Force stop sprinting and unsubscribe from sprint events
            isSprinting = false;
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
