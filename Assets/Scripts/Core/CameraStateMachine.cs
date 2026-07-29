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

        private CameraStateType currentState;
        private float currentDistanceScale = 1f;
        private float currentHeightOffset = 0f;
        private Coroutine transitionCoroutine;
        private bool poseInitialized;

        public CameraStateType CurrentState => currentState;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
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
