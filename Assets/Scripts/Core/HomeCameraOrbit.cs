using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using RingSport.Effects;
using RingSport.Player;

namespace RingSport.Core
{
    /// <summary>
    /// Home-screen drag: orbits the camera around the idle dog, up to +/-60
    /// degrees of rig yaw from wherever the finger went down, snapping back
    /// to centre on release. Gameplay gestures are gated off outside runs
    /// (MobileInputHandler), so the drag has the screen to itself; drags that
    /// start on UI (START, the hat selector arrows, love notes) are ignored.
    /// Self-instantiates like DebugMenu - no scene setup required.
    /// </summary>
    public class HomeCameraOrbit : MonoBehaviour
    {
        private const float MaxYawDegrees = 60f;
        private const float YawPerScreenWidth = 140f; // full-width drag = 140 degrees of intent, clamped to +/-60
        private const float SnapBackSeconds = 0.3f;

        private static HomeCameraOrbit instance;
        private static readonly List<RaycastResult> uiHits = new List<RaycastResult>(8);

        private bool dragging;
        private bool snapping;
        private float downX;
        private float baseYaw;
        private float currentYaw;
        private float snapFromYaw;
        private float snapElapsed;
        private Transform playerTransform; // orbit pivot - the dog, not the rig origin

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (instance != null)
                return;

            var go = new GameObject("HomeCameraOrbit");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<HomeCameraOrbit>();
        }

        private void Update()
        {
            var camera = CameraStateMachine.Instance;
            var gameManager = GameManager.Instance;
            if (camera == null || gameManager == null || gameManager.CurrentState != GameState.Home)
            {
                // Leaving home mid-gesture: drop it and let the state
                // transition own the rig again
                dragging = false;
                snapping = false;
                currentYaw = 0f;
                return;
            }

            ReadPointer(out bool isPressed, out bool pressedThisFrame, out Vector2 position);

            if (!dragging && pressedThisFrame && !IsPointerOverUI(position))
            {
                dragging = true;
                snapping = false;
                downX = position.x;
                baseYaw = currentYaw; // grabbing mid-snap continues from where it is
            }

            if (dragging)
            {
                if (isPressed)
                {
                    float dragFraction = (position.x - downX) / Mathf.Max(1f, Screen.width);
                    // Finger right spins the dog right (the camera orbits the other way)
                    currentYaw = Mathf.Clamp(baseYaw - dragFraction * YawPerScreenWidth, -MaxYawDegrees, MaxYawDegrees);
                    camera.SetHomeOrbitYaw(currentYaw, OrbitPivot());
                }
                else
                {
                    dragging = false;
                    snapping = true;
                    snapFromYaw = currentYaw;
                    snapElapsed = 0f;
                }
            }
            else if (snapping)
            {
                snapElapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(snapElapsed / SnapBackSeconds);
                currentYaw = Mathf.Lerp(snapFromYaw, 0f, Juice.OutCubic(k));
                camera.SetHomeOrbitYaw(currentYaw, OrbitPivot());
                if (k >= 1f)
                {
                    snapping = false;
                    currentYaw = 0f;
                }
            }
        }

        /// <summary>Vertical axis the orbit swings around: the dog's position.</summary>
        private Vector3 OrbitPivot()
        {
            if (playerTransform == null)
                playerTransform = FindAnyObjectByType<PlayerController>()?.transform;
            return playerTransform != null ? playerTransform.position : Vector3.zero;
        }

        private static void ReadPointer(out bool isPressed, out bool pressedThisFrame, out Vector2 position)
        {
            var touchscreen = Touchscreen.current;
            if (touchscreen != null && touchscreen.primaryTouch.press.isPressed)
            {
                isPressed = true;
                pressedThisFrame = touchscreen.primaryTouch.press.wasPressedThisFrame;
                position = touchscreen.primaryTouch.position.ReadValue();
                return;
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                isPressed = mouse.leftButton.isPressed;
                pressedThisFrame = mouse.leftButton.wasPressedThisFrame;
                position = mouse.position.ReadValue();
                return;
            }

            isPressed = false;
            pressedThisFrame = false;
            position = Vector2.zero;
        }

        private static bool IsPointerOverUI(Vector2 screenPosition)
        {
            var eventSystem = EventSystem.current;
            if (eventSystem == null)
                return false;

            var pointerData = new PointerEventData(eventSystem) { position = screenPosition };
            uiHits.Clear();
            eventSystem.RaycastAll(pointerData, uiHits);
            return uiHits.Count > 0;
        }
    }
}
