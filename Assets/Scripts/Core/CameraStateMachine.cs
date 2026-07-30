using UnityEngine;
using System.Collections;

namespace RingSport.Core
{
    public enum CameraStateType
    {
        Start,
        Gameplay,
        Bite,
        MiniLevel,
        Home
    }

    [System.Serializable]
    public class CameraStateData
    {
        public string stateName;
        public Vector3 cameraLocalPosition;
        public Vector3 cameraLocalRotation;
        public Vector3 parentRotation;
        public float transitionDuration = 0.5f;
        public AnimationCurve easingCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    }

    public class CameraStateMachine : MonoBehaviour
    {
        public static CameraStateMachine Instance { get; private set; }

        [Header("Camera Rig")]
        [SerializeField] private Transform cameraRig;

        [Header("Camera States")]
        [SerializeField] private CameraStateData startState = new CameraStateData
        {
            stateName = "Start State",
            cameraLocalPosition = new Vector3(0f, 4f, 6f),
            cameraLocalRotation = new Vector3(35f, 0f, 0f),
            parentRotation = Vector3.zero,
            transitionDuration = 0.5f
        };

        // Close-up greeting shot (Simon Says-like distance): same -60 rig angle
        // as the Start podium shot but ~2.1m from the dog's chest, dropped to
        // its eye line, aimed so the camera-facing dog is centered
        [SerializeField] private CameraStateData homeState = new CameraStateData
        {
            stateName = "Home State",
            cameraLocalPosition = new Vector3(-1.49f, 1.53f, -2.42f),
            cameraLocalRotation = new Vector3(16f, 18f, 0f),
            parentRotation = new Vector3(0f, -60f, 0f),
            transitionDuration = 1.5f
        };

        [SerializeField] private CameraStateData gameplayState = new CameraStateData
        {
            stateName = "Gameplay State",
            cameraLocalPosition = new Vector3(0f, 4f, 6f),
            cameraLocalRotation = new Vector3(35f, 0f, 0f),
            parentRotation = Vector3.zero,
            transitionDuration = 0.5f
        };

        [SerializeField] private CameraStateData biteState = new CameraStateData
        {
            stateName = "Bite State",
            cameraLocalPosition = new Vector3(0f, 4f, 6f),
            cameraLocalRotation = new Vector3(35f, 0f, 0f),
            parentRotation = Vector3.zero,
            transitionDuration = 0.5f
        };

        [SerializeField] private CameraStateData miniLevelState = new CameraStateData
        {
            stateName = "Mini Level State",
            cameraLocalPosition = new Vector3(0f, 4f, 6f),
            cameraLocalRotation = new Vector3(35f, 0f, 0f),
            parentRotation = Vector3.zero,
            transitionDuration = 0.5f
        };

        [Header("Impulse Layer (shake / kicks, additive on top of states)")]
        [Tooltip("How fast accumulated shake trauma drains, per second. Shake amplitude is trauma squared, so the tail dies off quickly.")]
        [SerializeField] private float traumaDecayPerSecond = 1.6f;
        [SerializeField] private float shakePositionAmplitude = 0.2f;
        [SerializeField] private float shakeRollAmplitude = 2.2f;
        [SerializeField] private float shakeFrequency = 16f;

        private CameraStateType currentState;
        private float currentDistanceScale = 1f;
        private float currentHeightOffset = 0f;
        private Coroutine transitionCoroutine;
        private bool poseInitialized;

        // Impulse layer state. The layer rides on top of whatever pose the
        // state machine (or a scripted shot) authored: each LateUpdate it
        // detects external writes, adopts them as the new base, and re-applies
        // its decaying offset - so shakes compose with transitions instead of
        // fighting them.
        private Camera cameraComponent;
        private float trauma;
        private Vector3 kickOffset;
        private float kickDuration;
        private float kickTimer;
        private float fovKickAmount;
        private float fovKickDuration;
        private float fovKickTimer;
        private float speedFovTarget;
        private float speedFovCurrent;
        private float speedFovVelocity;
        private float baseFov;
        private bool fovApplied;
        private Vector3 impulseBasePos;
        private Quaternion impulseBaseRot = Quaternion.identity;
        private Vector3 lastWrittenPos;
        private Quaternion lastWrittenRot = Quaternion.identity;
        private bool impulseWasApplied;

        public CameraStateType CurrentState => currentState;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            cameraComponent = GetComponent<Camera>();
            if (cameraComponent != null)
                baseFov = cameraComponent.fieldOfView;
        }

        private void Start()
        {
            // Initialize with start state (no transition) - unless the game flow
            // already snapped somewhere (GameManager.Start boots the home shot;
            // script Start order between the two is undefined)
            if (poseInitialized)
                return;

            ApplyStateImmediate(startState);
            currentState = CameraStateType.Start;
        }

        /// <summary>
        /// Snap straight to a state's authored pose with no transition (initial
        /// scene load, before the player has seen anything to transition from).
        /// </summary>
        public void SetStateImmediate(CameraStateType newState)
        {
            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
                transitionCoroutine = null;
            }

            currentState = newState;
            currentDistanceScale = 1f;
            currentHeightOffset = 0f;
            ApplyStateImmediate(GetStateData(newState));
            poseInitialized = true;
        }

        /// <summary>
        /// Marks the camera pose as externally driven (a scripted shot like
        /// the face attack standoff moved the camera directly). Stops any
        /// in-flight transition and forgets the last applied scale so the
        /// next SetState always re-transitions back from the scripted pose.
        /// </summary>
        public void NotifyExternalPose()
        {
            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
                transitionCoroutine = null;
            }
            currentDistanceScale = -1f;
        }

        public void SetState(CameraStateType newState)
        {
            SetState(newState, 1f, 0f, null);
        }

        public void SetState(CameraStateType newState, float distanceScale)
        {
            SetState(newState, distanceScale, 0f, null);
        }

        /// <summary>
        /// distanceScale scales the camera's offset from its rig (0.5 = twice as
        /// close); heightOffset raises/lowers it after scaling; lookAtWorldPoint,
        /// when given, aims the camera at that point instead of the state's
        /// authored rotation (used by Simon Says for its low, close framing).
        /// </summary>
        public void SetState(CameraStateType newState, float distanceScale, float heightOffset, Vector3? lookAtWorldPoint)
        {
            if (currentState == newState &&
                Mathf.Approximately(currentDistanceScale, distanceScale) &&
                Mathf.Approximately(currentHeightOffset, heightOffset))
                return;

            currentState = newState;
            currentDistanceScale = distanceScale;
            currentHeightOffset = heightOffset;
            CameraStateData targetState = GetStateData(newState);

            Vector3 targetPos = targetState.cameraLocalPosition * distanceScale + Vector3.up * heightOffset;
            Quaternion targetRot;
            if (lookAtWorldPoint.HasValue)
            {
                Vector3 focusLocal = transform.parent != null
                    ? transform.parent.InverseTransformPoint(lookAtWorldPoint.Value)
                    : lookAtWorldPoint.Value;
                targetRot = Quaternion.LookRotation(focusLocal - targetPos, Vector3.up);
            }
            else
            {
                targetRot = Quaternion.Euler(targetState.cameraLocalRotation);
            }

            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
            }

            transitionCoroutine = StartCoroutine(TransitionToState(targetState, targetPos, targetRot));
        }

        /// <summary>Add screenshake trauma (0..1, accumulates and clamps). Amplitude = trauma squared.</summary>
        public void AddShake(float amount)
        {
            trauma = Mathf.Clamp01(trauma + amount);
        }

        /// <summary>
        /// Directional camera punch in camera-local space (e.g. Vector3.down
        /// for a landing dip): applies the full offset instantly, then eases
        /// back to rest over the duration.
        /// </summary>
        public void AddKick(Vector3 localOffset, float duration = 0.3f)
        {
            kickOffset = localOffset;
            kickDuration = Mathf.Max(0.01f, duration);
            kickTimer = 0f;
        }

        /// <summary>FOV punch (degrees, +widens): instant pop, eased return.</summary>
        public void AddFovKick(float degrees, float duration = 0.5f)
        {
            fovKickAmount = degrees;
            fovKickDuration = Mathf.Max(0.01f, duration);
            fovKickTimer = 0f;
        }

        /// <summary>
        /// Continuous FOV widening for speed sensation (LevelScroller drives
        /// this from the scroll speed). Smoothed here; composes with kicks.
        /// </summary>
        public void SetSpeedFov(float offset)
        {
            speedFovTarget = Mathf.Max(0f, offset);
        }

        private void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;

            trauma = Mathf.Max(0f, trauma - traumaDecayPerSecond * dt);
            if (kickTimer < kickDuration)
                kickTimer += dt;

            UpdateFovKick(dt);

            bool active = trauma > 0f || kickTimer < kickDuration;
            if (!active && !impulseWasApplied)
                return;

            // Adopt any pose written since our last frame (a transition tick,
            // a scripted shot, SetStateImmediate) as the new base.
            Vector3 currentPos = transform.localPosition;
            Quaternion currentRot = transform.localRotation;
            if (!impulseWasApplied ||
                (currentPos - lastWrittenPos).sqrMagnitude > 1e-8f ||
                Quaternion.Angle(currentRot, lastWrittenRot) > 0.005f)
            {
                impulseBasePos = currentPos;
                impulseBaseRot = currentRot;
            }

            Vector3 offset = Vector3.zero;
            float roll = 0f;

            if (trauma > 0f)
            {
                float shake = trauma * trauma;
                float t = Time.unscaledTime * shakeFrequency;
                offset += new Vector3(
                    Mathf.PerlinNoise(t, 0.3f) * 2f - 1f,
                    Mathf.PerlinNoise(t, 7.7f) * 2f - 1f,
                    0f) * (shakePositionAmplitude * shake);
                roll = (Mathf.PerlinNoise(t, 13.1f) * 2f - 1f) * shakeRollAmplitude * shake;
            }

            if (kickTimer < kickDuration)
            {
                float remain = 1f - Mathf.Clamp01(kickTimer / kickDuration);
                offset += kickOffset * (remain * remain);
            }

            bool applying = offset.sqrMagnitude > 1e-10f || Mathf.Abs(roll) > 1e-4f;
            transform.localPosition = impulseBasePos + offset;
            transform.localRotation = impulseBaseRot * Quaternion.Euler(0f, 0f, roll);
            lastWrittenPos = transform.localPosition;
            lastWrittenRot = transform.localRotation;
            impulseWasApplied = applying;
        }

        private void UpdateFovKick(float dt)
        {
            if (cameraComponent == null)
                return;

            speedFovCurrent = Mathf.SmoothDamp(speedFovCurrent, speedFovTarget, ref speedFovVelocity, 0.3f, Mathf.Infinity, dt);

            float kick = 0f;
            if (fovKickTimer < fovKickDuration)
            {
                fovKickTimer += dt;
                float remain = 1f - Mathf.Clamp01(fovKickTimer / fovKickDuration);
                kick = fovKickAmount * (remain * remain);
            }

            float total = speedFovCurrent + kick;
            if (Mathf.Abs(total) < 0.01f)
            {
                if (fovApplied)
                {
                    cameraComponent.fieldOfView = baseFov;
                    fovApplied = false;
                }
                return;
            }

            cameraComponent.fieldOfView = baseFov + total;
            fovApplied = true;
        }

        private CameraStateData GetStateData(CameraStateType stateType)
        {
            return stateType switch
            {
                CameraStateType.Start => startState,
                CameraStateType.Gameplay => gameplayState,
                CameraStateType.Bite => biteState,
                CameraStateType.MiniLevel => miniLevelState,
                CameraStateType.Home => homeState,
                _ => startState
            };
        }

        private void ApplyStateImmediate(CameraStateData state)
        {
            transform.localPosition = state.cameraLocalPosition;
            transform.localRotation = Quaternion.Euler(state.cameraLocalRotation);

            if (cameraRig != null)
            {
                cameraRig.localRotation = Quaternion.Euler(state.parentRotation);
            }
        }

        private IEnumerator TransitionToState(CameraStateData targetState, Vector3 targetPos, Quaternion targetRot)
        {
            float elapsed = 0f;

            // Capture starting values
            Vector3 startPos = transform.localPosition;
            Quaternion startRot = transform.localRotation;
            Quaternion startParentRot = cameraRig != null ? cameraRig.localRotation : Quaternion.identity;

            Quaternion targetParentRot = Quaternion.Euler(targetState.parentRotation);

            while (elapsed < targetState.transitionDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalizedTime = elapsed / targetState.transitionDuration;
                float t = targetState.easingCurve.Evaluate(normalizedTime);

                transform.localPosition = Vector3.Lerp(startPos, targetPos, t);
                transform.localRotation = Quaternion.Slerp(startRot, targetRot, t);

                if (cameraRig != null)
                {
                    cameraRig.localRotation = Quaternion.Slerp(startParentRot, targetParentRot, t);
                }

                yield return null;
            }

            // Snap to final values
            transform.localPosition = targetPos;
            transform.localRotation = targetRot;
            if (cameraRig != null)
            {
                cameraRig.localRotation = targetParentRot;
            }
            transitionCoroutine = null;
        }
    }
}
