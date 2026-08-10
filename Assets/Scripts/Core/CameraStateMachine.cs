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
        // Home lens: a longer portrait focal length flattens the start-screen
        // dog; the camera dollies back along its view axis (anchored on the
        // dog) far enough that she reads HomeDogScale larger than the old wide
        // framing, and rides HomeHeightDrop lower than the authored height.
        // Plain constants on purpose - the scene-serialized versions of these
        // kept getting stomped by play-mode saves mid-tuning.
        private const float HomeFocalLengthMm = 50f;
        private const float HomeDogScale = 2f;
        private const float HomeHeightDrop = 0.15f;

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
        private float stateFov; // per-state base lens, blended through transitions
        private float frameOffset;
        private bool frameOffsetApplied;
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
            stateFov = baseFov;
        }

        /// <summary>
        /// Per-state base FOV: Home borrows a longer portrait lens so the idle
        /// dog reads clearly; every other state keeps the authored wide FOV.
        /// </summary>
        private float StateFovFor(CameraStateType stateType)
        {
            if (stateType != CameraStateType.Home)
                return baseFov;

            // Full-frame vertical FOV for the focal length (24mm sensor height)
            return 2f * Mathf.Atan(12f / HomeFocalLengthMm) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Dolly compensation for the home lens, as a parent-local offset to
        /// ADD to the state's authored camera position. The camera slides
        /// straight back along its own view axis - anchored on the dog, so the
        /// framing centre never moves - far enough that the longer lens leaves
        /// the dog only homePortraitZoom larger than the old wide shot.
        /// </summary>
        private Vector3 HomeLensDollyOffset(CameraStateType stateType, Vector3 localPos)
        {
            if (stateType != CameraStateType.Home)
                return Vector3.zero;

            float wide = Mathf.Tan(baseFov * 0.5f * Mathf.Deg2Rad);
            float portrait = Mathf.Tan(StateFovFor(CameraStateType.Home) * 0.5f * Mathf.Deg2Rad);
            if (portrait <= 0f)
                return Vector3.zero;
            float factor = wide / (portrait * HomeDogScale);

            CameraStateData home = GetStateData(CameraStateType.Home);
            Quaternion localRot = Quaternion.Euler(home.cameraLocalRotation);

            // Distance to the dog measured along the settled view axis, in
            // world space (using the authored parent rotation - at the moment
            // this runs the rig is usually still turned to the previous state)
            float focusDistance = 6f;
            Transform focus = HomeFocus();
            if (focus != null)
            {
                Quaternion parentRotation = transform.parent == null
                    ? Quaternion.identity
                    : (cameraRig == transform.parent ? Quaternion.Euler(home.parentRotation) : transform.parent.rotation);
                Vector3 parentPosition = transform.parent != null ? transform.parent.position : Vector3.zero;
                Vector3 settledWorldPos = parentPosition + parentRotation * localPos;
                Vector3 forwardWorld = parentRotation * (localRot * Vector3.forward);
                focusDistance = Mathf.Max(0.5f, Vector3.Dot(focus.position - settledWorldPos, forwardWorld));
            }

            return localRot * Vector3.back * ((factor - 1f) * focusDistance)
                   + Vector3.down * HomeHeightDrop;
        }

        private Transform homeFocus;

        /// <summary>The home shot's subject - the idle dog.</summary>
        private Transform HomeFocus()
        {
            if (homeFocus == null)
            {
                var player = FindAnyObjectByType<RingSport.Player.PlayerController>();
                homeFocus = player != null ? player.transform : null;
            }
            return homeFocus;
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
            frameOffset = 0f;
            stateFov = StateFovFor(newState);
            CameraStateData data = GetStateData(newState);
            ApplyStateImmediate(data, HomeLensDollyOffset(newState, data.cameraLocalPosition));
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
            // A frame offset belongs to the shot that asked for it, so asking for
            // a shot drops it - callers that want one re-apply after this returns
            frameOffset = 0f;

            if (currentState == newState &&
                Mathf.Approximately(currentDistanceScale, distanceScale) &&
                Mathf.Approximately(currentHeightOffset, heightOffset))
                return;

            currentState = newState;
            currentDistanceScale = distanceScale;
            currentHeightOffset = heightOffset;
            CameraStateData targetState = GetStateData(newState);

            Vector3 targetPos = targetState.cameraLocalPosition * distanceScale + Vector3.up * heightOffset;
            targetPos += HomeLensDollyOffset(newState, targetPos);
            Quaternion targetRot;
            if (lookAtWorldPoint.HasValue)
            {
                // Solve the look-at against the SETTLED frame, not the live
                // one: the transition slerps the rig onto the state's authored
                // rotation, so a rotation solved in the rig's current frame
                // would be applied in a different one, skewing the settled aim
                // by however far the rig still had to turn when asked.
                Vector3 settledPos = GetStateWorldPosition(newState, distanceScale, heightOffset);
                Quaternion settledParentRot = transform.parent == null
                    ? Quaternion.identity
                    : (cameraRig == transform.parent
                        ? Quaternion.Euler(targetState.parentRotation)
                        : transform.parent.rotation);
                targetRot = Quaternion.Inverse(settledParentRot) *
                    Quaternion.LookRotation(lookAtWorldPoint.Value - settledPos, Vector3.up);
            }
            else
            {
                targetRot = Quaternion.Euler(targetState.cameraLocalRotation);
            }

            if (transitionCoroutine != null)
            {
                StopCoroutine(transitionCoroutine);
            }

            transitionCoroutine = StartCoroutine(TransitionToState(targetState, targetPos, targetRot, StateFovFor(newState)));
        }

        /// <summary>
        /// World position the camera settles at for a state, before the impulse
        /// layer's offset. A caller working out framing has to measure against
        /// the settled pose, not the live one - at the moment it asks, the camera
        /// is usually still somewhere mid-transition.
        /// </summary>
        public Vector3 GetStateWorldPosition(CameraStateType stateType, float distanceScale = 1f, float heightOffset = 0f)
        {
            CameraStateData state = GetStateData(stateType);
            Vector3 local = state.cameraLocalPosition * distanceScale + Vector3.up * heightOffset;
            local += HomeLensDollyOffset(stateType, local);

            if (transform.parent == null)
                return local;

            // The rig lands on the state's authored rotation when the transition
            // finishes, so use that rather than whatever it is turned to now
            Quaternion parentRotation = cameraRig == transform.parent
                ? Quaternion.Euler(state.parentRotation)
                : transform.parent.rotation;
            return transform.parent.position + parentRotation * local;
        }

        /// <summary>
        /// World rotation the camera settles at for a state - the other half of
        /// <see cref="GetStateWorldPosition"/>.
        /// </summary>
        public Quaternion GetStateWorldRotation(CameraStateType stateType)
        {
            CameraStateData state = GetStateData(stateType);
            Quaternion local = Quaternion.Euler(state.cameraLocalRotation);

            if (transform.parent == null)
                return local;

            Quaternion parentRotation = cameraRig == transform.parent
                ? Quaternion.Euler(state.parentRotation)
                : transform.parent.rotation;
            return parentRotation * local;
        }

        /// <summary>Field of view the states rest at, with kick and speed widening excluded.</summary>
        public float BaseFieldOfView => baseFov;

        /// <summary>
        /// Home-screen drag orbit: swings the camera around a vertical axis
        /// through <paramref name="pivot"/> (the dog), as if the rig were
        /// parented there - position and aim both orbit, starting from the
        /// home state's settled pose. Driven every frame by HomeCameraOrbit
        /// while a drag or snap-back is live; ignored mid-transition, and
        /// state changes blend away smoothly because transitions capture
        /// their start from the live (orbited) transform.
        /// </summary>
        public void SetHomeOrbitYaw(float degrees, Vector3 pivot)
        {
            if (currentState != CameraStateType.Home || transitionCoroutine != null)
                return;

            Quaternion yaw = Quaternion.AngleAxis(degrees, Vector3.up);
            Vector3 basePosition = GetStateWorldPosition(CameraStateType.Home);
            Quaternion baseRotation = GetStateWorldRotation(CameraStateType.Home);
            transform.position = pivot + yaw * (basePosition - pivot);
            transform.rotation = yaw * baseRotation;
        }

        /// <summary>
        /// Slides the whole rendered image down the frame by <paramref name="halfFrames"/>
        /// (1 = half the screen height), leaving the camera pointed where it was.
        ///
        /// Turning the camera would also push a subject down the screen, but on a
        /// lens this wide anything shoved toward an edge leans away from the
        /// viewer. Offsetting the projection is a lens shift instead: every
        /// projected point moves by the same amount, so the subject is drawn
        /// exactly as square-on as it is dead centre - it just sits lower, with
        /// more of what is above it in frame. Pass 0 to clear.
        /// </summary>
        public void SetFrameOffset(float halfFrames)
        {
            frameOffset = halfFrames;
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
            ApplyFrameOffset();

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

        /// <summary>
        /// Rebuilds the off-axis projection every frame - FOV kicks and canvas
        /// resizes both feed into it, and neither is worth tracking separately.
        /// </summary>
        private void ApplyFrameOffset()
        {
            if (cameraComponent == null)
                return;

            if (Mathf.Abs(frameOffset) < 0.0001f)
            {
                if (frameOffsetApplied)
                {
                    cameraComponent.ResetProjectionMatrix();
                    frameOffsetApplied = false;
                }
                return;
            }

            Matrix4x4 projection = Matrix4x4.Perspective(
                cameraComponent.fieldOfView,
                cameraComponent.aspect,
                cameraComponent.nearClipPlane,
                cameraComponent.farClipPlane);

            // A projected point lands at clip.y / -view.z, so this row's z term
            // subtracts the same constant from every one of them - a slide down
            // the frame, with no perspective change at all
            projection.m12 += frameOffset;
            cameraComponent.projectionMatrix = projection;
            frameOffsetApplied = true;
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

            // Effects ride on the state lens, so the home portrait FOV and its
            // transition blend survive speed/kick writes
            float target = stateFov + speedFovCurrent + kick;
            if (!Mathf.Approximately(cameraComponent.fieldOfView, target))
                cameraComponent.fieldOfView = target;
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

        private void ApplyStateImmediate(CameraStateData state, Vector3 extraLocalOffset = default)
        {
            transform.localPosition = state.cameraLocalPosition + extraLocalOffset;
            transform.localRotation = Quaternion.Euler(state.cameraLocalRotation);

            if (cameraRig != null)
            {
                cameraRig.localRotation = Quaternion.Euler(state.parentRotation);
            }
        }

        private IEnumerator TransitionToState(CameraStateData targetState, Vector3 targetPos, Quaternion targetRot, float targetFov)
        {
            float elapsed = 0f;

            // Capture starting values
            Vector3 startPos = transform.localPosition;
            Quaternion startRot = transform.localRotation;
            Quaternion startParentRot = cameraRig != null ? cameraRig.localRotation : Quaternion.identity;
            float startFov = stateFov;

            Quaternion targetParentRot = Quaternion.Euler(targetState.parentRotation);

            while (elapsed < targetState.transitionDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float normalizedTime = elapsed / targetState.transitionDuration;
                float t = targetState.easingCurve.Evaluate(normalizedTime);

                transform.localPosition = Vector3.Lerp(startPos, targetPos, t);
                transform.localRotation = Quaternion.Slerp(startRot, targetRot, t);
                stateFov = Mathf.Lerp(startFov, targetFov, t);

                if (cameraRig != null)
                {
                    cameraRig.localRotation = Quaternion.Slerp(startParentRot, targetParentRot, t);
                }

                yield return null;
            }

            // Snap to final values
            transform.localPosition = targetPos;
            transform.localRotation = targetRot;
            stateFov = targetFov;
            if (cameraRig != null)
            {
                cameraRig.localRotation = targetParentRot;
            }
            transitionCoroutine = null;
        }
    }
}
