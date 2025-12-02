using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using System;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace RingSport.Input
{
    /// <summary>
    /// Detects swipe gestures on touchscreen devices using the new Input System.
    /// Optimized for iOS Safari and WebGL compatibility.
    /// </summary>
    public class SwipeDetector : MonoBehaviour
    {
        [Header("Swipe Settings")]
        [SerializeField] private float minSwipeDistance = 100f; // Moderate sensitivity
        [SerializeField] private float maxSwipeTime = 1f; // Maximum duration for a swipe
        [SerializeField] private float directionThreshold = 0.5f; // Determines swipe direction clarity (0-1)

        [Header("Hold Settings")]
        [SerializeField] private float holdDuration = 0.2f; // Duration to register as a hold (matches Sprint action)
        [SerializeField] private float maxHoldMovement = 30f; // Max movement allowed during hold (pixels)

        [Header("Testing")]
        [SerializeField] private bool enableMouseSimulation = true; // For Unity Editor testing

        // Events for gesture detection
        public event Action OnSwipeLeft;
        public event Action OnSwipeRight;
        public event Action OnSwipeUp;
        public event Action OnSwipeDown;
        public event Action<Vector2> OnTap; // Tap position
        public event Action OnPress; // Immediate press (for palisade taps)
        public event Action OnHoldStarted; // Hold began (for sprint)
        public event Action OnHoldEnded; // Hold released (for sprint)

        // Touch tracking
        private Vector2 touchStartPos;
        private float touchStartTime;
        private bool isTouching = false;
        private bool isHolding = false; // Track if hold event has been triggered

        // Debug
        [SerializeField] private bool showDebugLogs = false;

        private void OnEnable()
        {
            // Enable Enhanced Touch support for the new Input System
            EnhancedTouchSupport.Enable();
        }

        private void OnDisable()
        {
            // Disable Enhanced Touch support when not needed
            EnhancedTouchSupport.Disable();
        }

        private void Update()
        {
            DetectTouchInput();
            CheckForHold();
        }

        /// <summary>
        /// Check if current touch/click meets hold duration requirement
        /// </summary>
        private void CheckForHold()
        {
            if (isTouching && !isHolding)
            {
                float holdTime = Time.time - touchStartTime;
                if (holdTime >= holdDuration)
                {
                    // Check if user has moved significantly (if so, it's a swipe not a hold)
                    Vector2 currentPos = GetCurrentTouchPosition();
                    float movementDistance = Vector2.Distance(touchStartPos, currentPos);

                    if (movementDistance <= maxHoldMovement)
                    {
                        isHolding = true;
                        OnHoldStarted?.Invoke();

                        if (showDebugLogs)
                            Debug.Log($"Hold started after {holdTime}s (movement: {movementDistance}px)");
                    }
                    else
                    {
                        if (showDebugLogs)
                            Debug.Log($"Hold cancelled - too much movement ({movementDistance}px > {maxHoldMovement}px)");
                    }
                }
            }
        }

        /// <summary>
        /// Get current touch/mouse position
        /// </summary>
        private Vector2 GetCurrentTouchPosition()
        {
            // Check for touch input
            if (Touch.activeTouches.Count > 0)
            {
                return Touch.activeTouches[0].screenPosition;
            }
            // Fallback to mouse
            else if (enableMouseSimulation && Mouse.current != null)
            {
                return Mouse.current.position.ReadValue();
            }

            return touchStartPos; // Fallback to start position
        }

        /// <summary>
        /// Detect and process touch input for swipe gestures
        /// </summary>
        private void DetectTouchInput()
        {
            // Check for touch input using new Input System
            if (Touch.activeTouches.Count > 0)
            {
                Touch touch = Touch.activeTouches[0]; // Always use primary touch

                if (touch.phase == UnityEngine.InputSystem.TouchPhase.Began)
                {
                    HandleTouchBegan(touch.screenPosition);
                }
                else if (touch.phase == UnityEngine.InputSystem.TouchPhase.Ended ||
                         touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled)
                {
                    HandleTouchEnded(touch.screenPosition);
                }
            }
            // Fallback to mouse input for testing in editor
            else if (enableMouseSimulation && Mouse.current != null)
            {
                DetectMouseInput();
            }
        }

        /// <summary>
        /// Handle touch start
        /// </summary>
        private void HandleTouchBegan(Vector2 position)
        {
            if (!isTouching)
            {
                isTouching = true;
                isHolding = false; // Reset hold state
                touchStartPos = position;
                touchStartTime = Time.time;

                // Fire immediate press event (for palisade taps)
                OnPress?.Invoke();

                if (showDebugLogs)
                    Debug.Log($"Touch began at {touchStartPos}, Press event fired");
            }
        }

        /// <summary>
        /// Handle touch end and determine if it was a swipe
        /// </summary>
        private void HandleTouchEnded(Vector2 position)
        {
            if (isTouching)
            {
                // Check if we were holding
                bool wasHolding = isHolding;

                isTouching = false;
                isHolding = false;

                // If we were holding, trigger hold ended event
                if (wasHolding)
                {
                    OnHoldEnded?.Invoke();

                    if (showDebugLogs)
                        Debug.Log("Hold ended");
                }
                else
                {
                    // Only process swipe/tap if we weren't holding
                    Vector2 touchEndPos = position;
                    float touchDuration = Time.time - touchStartTime;

                    ProcessGesture(touchStartPos, touchEndPos, touchDuration);
                }
            }
        }

        /// <summary>
        /// Mouse input fallback for Unity Editor testing using new Input System
        /// </summary>
        private void DetectMouseInput()
        {
            if (Mouse.current.leftButton.wasPressedThisFrame)
            {
                isTouching = true;
                isHolding = false; // Reset hold state
                touchStartPos = Mouse.current.position.ReadValue();
                touchStartTime = Time.time;

                // Fire immediate press event (for palisade taps)
                OnPress?.Invoke();

                if (showDebugLogs)
                    Debug.Log($"Mouse began at {touchStartPos}, Press event fired");
            }
            else if (Mouse.current.leftButton.wasReleasedThisFrame && isTouching)
            {
                // Check if we were holding
                bool wasHolding = isHolding;

                isTouching = false;
                isHolding = false;

                // If we were holding, trigger hold ended event
                if (wasHolding)
                {
                    OnHoldEnded?.Invoke();

                    if (showDebugLogs)
                        Debug.Log("Mouse hold ended");
                }
                else
                {
                    // Only process swipe/tap if we weren't holding
                    Vector2 mouseEndPos = Mouse.current.position.ReadValue();
                    float touchDuration = Time.time - touchStartTime;

                    ProcessGesture(touchStartPos, mouseEndPos, touchDuration);
                }
            }
        }

        /// <summary>
        /// Process the gesture and determine if it's a swipe or tap
        /// </summary>
        private void ProcessGesture(Vector2 startPos, Vector2 endPos, float duration)
        {
            Vector2 swipeDelta = endPos - startPos;
            float swipeDistance = swipeDelta.magnitude;

            if (showDebugLogs)
                Debug.Log($"Gesture: distance={swipeDistance}, duration={duration}, delta={swipeDelta}");

            // Check if this is a swipe (meets distance and time requirements)
            if (swipeDistance >= minSwipeDistance && duration <= maxSwipeTime)
            {
                // Normalize the swipe direction
                Vector2 swipeDirection = swipeDelta.normalized;
                DetectSwipeDirection(swipeDirection);
            }
            else if (swipeDistance < minSwipeDistance)
            {
                // This was a tap, not a swipe
                OnTap?.Invoke(startPos);

                if (showDebugLogs)
                    Debug.Log($"Tap detected at {startPos}");
            }
        }

        /// <summary>
        /// Determine the primary swipe direction based on normalized direction vector
        /// </summary>
        private void DetectSwipeDirection(Vector2 direction)
        {
            // Determine if this is primarily a horizontal or vertical swipe
            float absX = Mathf.Abs(direction.x);
            float absY = Mathf.Abs(direction.y);

            // Horizontal swipe
            if (absX > absY && absX > directionThreshold)
            {
                if (direction.x > 0)
                {
                    if (showDebugLogs)
                        Debug.Log("Swipe RIGHT detected");
                    OnSwipeRight?.Invoke();
                }
                else
                {
                    if (showDebugLogs)
                        Debug.Log("Swipe LEFT detected");
                    OnSwipeLeft?.Invoke();
                }
            }
            // Vertical swipe
            else if (absY > absX && absY > directionThreshold)
            {
                if (direction.y > 0)
                {
                    if (showDebugLogs)
                        Debug.Log("Swipe UP detected");
                    OnSwipeUp?.Invoke();
                }
                else
                {
                    if (showDebugLogs)
                        Debug.Log("Swipe DOWN detected");
                    OnSwipeDown?.Invoke();
                }
            }
            else
            {
                if (showDebugLogs)
                    Debug.Log($"Ambiguous swipe direction: {direction}");
            }
        }

        /// <summary>
        /// Reset the detector state (useful when pausing/resuming)
        /// </summary>
        public void ResetState()
        {
            // If we were holding, trigger hold ended
            if (isHolding)
            {
                OnHoldEnded?.Invoke();
            }

            isTouching = false;
            isHolding = false;
        }
    }
}
