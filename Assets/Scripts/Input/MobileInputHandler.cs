using UnityEngine;
using UnityEngine.InputSystem;
using System;

namespace RingSport.Input
{
    /// <summary>
    /// Handles mobile touch input and bridges it to the game's Input System.
    /// Automatically activates when touchscreen is detected.
    /// </summary>
    [RequireComponent(typeof(SwipeDetector))]
    public class MobileInputHandler : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private SwipeDetector swipeDetector;

        [Header("Settings")]
        [SerializeField] private bool autoDetectTouchscreen = true;
        [SerializeField] private bool forceEnableOnWebGL = true;
        [SerializeField] private bool enableInEditorForTesting = true; // Allow testing with mouse in Editor
        [SerializeField] private bool showDebugLogs = false; // Show detailed debug logs

        // Public properties for PlayerController to read
        public Vector2 MoveInput { get; private set; }
        public bool JumpPressed { get; private set; }

        // Events for input
        public event Action OnJumpTriggered;
        public event Action OnLaneChangeLeft;
        public event Action OnLaneChangeRight;
        public event Action OnPressTriggered; // Immediate press (for palisade)
        public event Action OnSprintStarted; // Sprint hold began
        public event Action OnSprintEnded; // Sprint hold ended

        // State
        private bool isActive = false;
        private float moveInputResetTime = 0f;
        private const float MOVE_INPUT_DURATION = 0.1f; // How long move input stays active

        private void Awake()
        {
            // Get or add SwipeDetector
            if (swipeDetector == null)
            {
                swipeDetector = GetComponent<SwipeDetector>();
                if (swipeDetector == null)
                {
                    swipeDetector = gameObject.AddComponent<SwipeDetector>();
                }
            }

            // Add WebGL touch handler for iOS Safari compatibility
#if UNITY_WEBGL
            if (GetComponent<WebGLTouchHandler>() == null)
            {
                gameObject.AddComponent<WebGLTouchHandler>();
                Debug.Log("WebGLTouchHandler added for iOS Safari optimization");
            }
#endif
        }

        private void OnEnable()
        {
            // Subscribe to swipe events
            if (swipeDetector != null)
            {
                swipeDetector.OnSwipeLeft += HandleSwipeLeft;
                swipeDetector.OnSwipeRight += HandleSwipeRight;
                swipeDetector.OnSwipeUp += HandleSwipeUp;
                swipeDetector.OnPress += HandlePress;
                swipeDetector.OnHoldStarted += HandleHoldStarted;
                swipeDetector.OnHoldEnded += HandleHoldEnded;
            }

            // Determine if we should activate mobile input
            DetermineActivation();
        }

        private void OnDisable()
        {
            // Unsubscribe from swipe events
            if (swipeDetector != null)
            {
                swipeDetector.OnSwipeLeft -= HandleSwipeLeft;
                swipeDetector.OnSwipeRight -= HandleSwipeRight;
                swipeDetector.OnSwipeUp -= HandleSwipeUp;
                swipeDetector.OnPress -= HandlePress;
                swipeDetector.OnHoldStarted -= HandleHoldStarted;
                swipeDetector.OnHoldEnded -= HandleHoldEnded;
            }
        }

        private void Update()
        {
            // Reset move input after duration (simulates button release)
            if (MoveInput != Vector2.zero && Time.time >= moveInputResetTime)
            {
                MoveInput = Vector2.zero;
            }

            // Reset jump pressed after one frame
            if (JumpPressed)
            {
                JumpPressed = false;
            }

            // Check for control scheme changes
            if (autoDetectTouchscreen)
            {
                CheckForControlSchemeChange();
            }
        }

        /// <summary>
        /// Determine if mobile input should be active based on platform and input devices
        /// </summary>
        private void DetermineActivation()
        {
            // Enable in Editor for testing (mouse simulation)
            if (Application.isEditor && enableInEditorForTesting)
            {
                SetActive(true);
                Debug.Log("MobileInputHandler: Activated in Editor for testing (mouse simulation enabled)");
                return;
            }

            if (forceEnableOnWebGL && Application.platform == RuntimePlatform.WebGLPlayer)
            {
                SetActive(true);
                Debug.Log("MobileInputHandler: Activated for WebGL");
                return;
            }

            if (autoDetectTouchscreen)
            {
                bool hasTouchscreen = Touchscreen.current != null;
                SetActive(hasTouchscreen);
                Debug.Log($"MobileInputHandler: Touchscreen detected = {hasTouchscreen}, Active = {isActive}");
            }
        }

        /// <summary>
        /// Check if the control scheme has changed (e.g., user connected a gamepad)
        /// </summary>
        private void CheckForControlSchemeChange()
        {
            // Don't auto-disable in Editor when testing with mouse
            if (Application.isEditor && enableInEditorForTesting)
            {
                return;
            }

            // Disable mobile input if keyboard or gamepad input is detected
            if (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame)
            {
                if (isActive)
                {
                    Debug.Log("MobileInputHandler: Keyboard input detected, deactivating");
                    SetActive(false);
                }
            }
            else if (Gamepad.current != null && Gamepad.current.wasUpdatedThisFrame)
            {
                if (isActive)
                {
                    Debug.Log("MobileInputHandler: Gamepad input detected, deactivating");
                    SetActive(false);
                }
            }
            // Reactivate if touchscreen input is detected
            else if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            {
                if (!isActive)
                {
                    Debug.Log("MobileInputHandler: Touch input detected, activating");
                    SetActive(true);
                }
            }
        }

        /// <summary>
        /// Set whether mobile input is active
        /// </summary>
        private void SetActive(bool active)
        {
            isActive = active;

            if (swipeDetector != null)
            {
                swipeDetector.enabled = active;
            }

            // Reset input when deactivating
            if (!active)
            {
                MoveInput = Vector2.zero;
                JumpPressed = false;
            }
        }

        /// <summary>
        /// Handle swipe left gesture - change lane left
        /// </summary>
        private void HandleSwipeLeft()
        {
            if (!isActive) return;

            MoveInput = new Vector2(-1f, 0f);
            moveInputResetTime = Time.time + MOVE_INPUT_DURATION;
            OnLaneChangeLeft?.Invoke();

            Debug.Log("Mobile Input: Lane Change LEFT");
        }

        /// <summary>
        /// Handle swipe right gesture - change lane right
        /// </summary>
        private void HandleSwipeRight()
        {
            if (!isActive) return;

            MoveInput = new Vector2(1f, 0f);
            moveInputResetTime = Time.time + MOVE_INPUT_DURATION;
            OnLaneChangeRight?.Invoke();

            Debug.Log("Mobile Input: Lane Change RIGHT");
        }

        /// <summary>
        /// Handle swipe up gesture - jump
        /// </summary>
        private void HandleSwipeUp()
        {
            if (!isActive) return;

            JumpPressed = true;
            OnJumpTriggered?.Invoke();

            Debug.Log("Mobile Input: JUMP");
        }

        /// <summary>
        /// Handle immediate press - for palisade taps
        /// </summary>
        private void HandlePress()
        {
            if (!isActive) return;

            OnPressTriggered?.Invoke();

            if (showDebugLogs)
                Debug.Log("Mobile Input: PRESS (tap)");
        }

        /// <summary>
        /// Handle hold started - begin sprinting
        /// </summary>
        private void HandleHoldStarted()
        {
            if (!isActive) return;

            OnSprintStarted?.Invoke();

            Debug.Log("Mobile Input: SPRINT STARTED");
        }

        /// <summary>
        /// Handle hold ended - stop sprinting
        /// </summary>
        private void HandleHoldEnded()
        {
            if (!isActive) return;

            OnSprintEnded?.Invoke();

            Debug.Log("Mobile Input: SPRINT ENDED");
        }

        /// <summary>
        /// Manually enable or disable mobile input
        /// </summary>
        public void SetMobileInputEnabled(bool enabled)
        {
            autoDetectTouchscreen = false;
            SetActive(enabled);
        }

        /// <summary>
        /// Check if mobile input is currently active
        /// </summary>
        public bool IsActive()
        {
            return isActive;
        }

        /// <summary>
        /// Reset all input state
        /// </summary>
        public void ResetInput()
        {
            MoveInput = Vector2.zero;
            JumpPressed = false;

            if (swipeDetector != null)
            {
                swipeDetector.ResetState();
            }
        }
    }
}
