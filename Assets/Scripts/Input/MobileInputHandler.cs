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
        }

        /// <summary>
        /// Determine if mobile input should be active based on platform and input devices
        /// </summary>
        private void DetermineActivation()
        {
            // Always activate - we now support simultaneous keyboard and touch input
            // Touch gestures will take priority when they have input
            bool hasTouchscreen = Touchscreen.current != null;
            bool isWebGL = Application.platform == RuntimePlatform.WebGLPlayer;
            bool isEditor = Application.isEditor && enableInEditorForTesting;

            if (hasTouchscreen || isWebGL || isEditor)
            {
                SetActive(true);
                Debug.Log($"MobileInputHandler: Activated (touchscreen={hasTouchscreen}, webgl={isWebGL}, editor={isEditor})");
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
            JumpPressed = true;
            OnJumpTriggered?.Invoke();

            Debug.Log("Mobile Input: JUMP");
        }

        /// <summary>
        /// Handle immediate press - for palisade taps
        /// </summary>
        private void HandlePress()
        {
            OnPressTriggered?.Invoke();

            if (showDebugLogs)
                Debug.Log("Mobile Input: PRESS (tap)");
        }

        /// <summary>
        /// Handle hold started - begin sprinting
        /// </summary>
        private void HandleHoldStarted()
        {
            OnSprintStarted?.Invoke();

            Debug.Log("Mobile Input: SPRINT STARTED");
        }

        /// <summary>
        /// Handle hold ended - stop sprinting
        /// </summary>
        private void HandleHoldEnded()
        {
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
