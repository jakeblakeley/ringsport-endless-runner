using UnityEngine;
using UnityEngine.InputSystem;
using RingSport.Core;
using RingSport.Level;

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
        private float lastInputTime = -1f;
        private float inputCooldown = 0.2f;

        public float ForwardSpeed => isSprinting ? forwardSpeed * sprintMultiplier : forwardSpeed;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            playerInput = GetComponent<PlayerInput>();

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
            if (GameManager.Instance?.CurrentState != GameState.Playing)
                return;

            HandleGroundCheck();
            HandleLaneMovement();
            HandleGravity();

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
            isSprinting = true;
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
        }
    }
}
