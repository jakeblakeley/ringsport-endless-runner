using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RingSport.Effects
{
    /// <summary>
    /// Tiny hand-rolled tween kit for the juice layer - no external tween
    /// library in this project, and most juice plays while Time.timeScale is 0
    /// (countdowns, reward screens, mini-levels), so everything here runs on
    /// unscaled time. Static entry points; coroutines run on a lazily created
    /// scene host so call sites don't need to be MonoBehaviours.
    /// </summary>
    public static class Juice
    {
        // Easing functions (normalized t in 0..1)
        public static float InQuad(float t) => t * t;
        public static float OutQuad(float t) => 1f - (1f - t) * (1f - t);
        public static float OutCubic(float t)
        {
            float u = 1f - t;
            return 1f - u * u * u;
        }
        public static float OutExpo(float t) => t >= 1f ? 1f : 1f - Mathf.Pow(2f, -10f * t);
        public static float OutBack(float t)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            t -= 1f;
            return 1f + c3 * t * t * t + c1 * t * t;
        }

        /// <summary>
        /// Counter "pop": scales the target up to rest*(1+punch) and settles
        /// back to its rest size. Re-punching mid-pop restarts from the stored
        /// rest scale instead of compounding, so rapid coin trains can hammer
        /// it safely.
        /// </summary>
        public static void PunchScale(Transform target, float punch = 0.2f, float duration = 0.18f)
        {
            if (target == null)
                return;
            JuiceRunner.Instance.PunchScale(target, punch, duration);
        }

        /// <summary>
        /// Damped-sine rotation wiggle around Z (counter fly-in flourish).
        /// Non-compounding like PunchScale.
        /// </summary>
        public static void PunchRotation(Transform target, float degrees = 10f, float duration = 0.4f)
        {
            if (target == null)
                return;
            JuiceRunner.Instance.PunchRotation(target, degrees, duration);
        }

        /// <summary>Run a coroutine on the shared juice host (survives screen swaps).</summary>
        public static Coroutine Run(IEnumerator routine)
        {
            return JuiceRunner.Instance.StartCoroutine(routine);
        }
    }

    /// <summary>Hidden host MonoBehaviour that owns Juice's coroutines.</summary>
    public class JuiceRunner : MonoBehaviour
    {
        private static JuiceRunner instance;

        public static JuiceRunner Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("JuiceRunner");
                    instance = go.AddComponent<JuiceRunner>();
                }
                return instance;
            }
        }

        private class PunchState
        {
            public Transform target;
            public Vector3 restScale;
            public float punch;
            public float duration;
            public float elapsed;
        }

        private class RotationState
        {
            public Quaternion restRotation;
            public Coroutine routine;
        }

        private readonly Dictionary<Transform, PunchState> punches = new Dictionary<Transform, PunchState>();
        private readonly List<PunchState> activePunches = new List<PunchState>();
        private readonly Dictionary<Transform, RotationState> rotations = new Dictionary<Transform, RotationState>();

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
        }

        public void PunchScale(Transform target, float punch, float duration)
        {
            if (!isActiveAndEnabled)
                return;

            // Punches run from the single Update below rather than one
            // coroutine per call - coin trains fire 3-6 of these a second and
            // each StartCoroutine allocated an enumerator + handle (perf
            // audit fix #5). Semantics preserved: a re-punch mid-pop restarts
            // from the ORIGINAL rest scale (recapturing would bake the punch
            // in), and a finished punch leaves no state behind.
            if (!punches.TryGetValue(target, out var state))
            {
                state = new PunchState { target = target, restScale = target.localScale };
                punches[target] = state;
                activePunches.Add(state);
            }

            state.punch = punch;
            state.duration = Mathf.Max(duration, 0.0001f);
            state.elapsed = 0f;
        }

        private void Update()
        {
            const float attackPortion = 0.3f; // quick swell, longer settle
            for (int i = activePunches.Count - 1; i >= 0; i--)
            {
                PunchState state = activePunches[i];
                if (state.target == null)
                {
                    punches.Remove(state.target);
                    RemovePunchAt(i);
                    continue;
                }

                state.elapsed += Time.unscaledDeltaTime;
                float n = Mathf.Clamp01(state.elapsed / state.duration);
                float k = n < attackPortion
                    ? Juice.OutQuad(n / attackPortion)
                    : 1f - Juice.OutQuad((n - attackPortion) / (1f - attackPortion));
                state.target.localScale = state.restScale * (1f + state.punch * k);

                if (n >= 1f)
                {
                    state.target.localScale = state.restScale;
                    punches.Remove(state.target);
                    RemovePunchAt(i);
                }
            }
        }

        private void RemovePunchAt(int index)
        {
            int last = activePunches.Count - 1;
            activePunches[index] = activePunches[last];
            activePunches.RemoveAt(last);
        }

        public void PunchRotation(Transform target, float degrees, float duration)
        {
            if (!isActiveAndEnabled)
                return;

            if (rotations.TryGetValue(target, out var state))
            {
                if (state.routine != null)
                    StopCoroutine(state.routine);
            }
            else
            {
                state = new RotationState { restRotation = target.localRotation };
                rotations[target] = state;
            }

            state.routine = StartCoroutine(RotationRoutine(target, state, degrees, duration));
        }

        private IEnumerator RotationRoutine(Transform target, RotationState state, float degrees, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (target == null)
                {
                    rotations.Remove(target);
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                float n = Mathf.Clamp01(elapsed / duration);
                float decay = (1f - n) * (1f - n);
                float z = degrees * Mathf.Sin(n * Mathf.PI * 3f) * decay;
                target.localRotation = state.restRotation * Quaternion.Euler(0f, 0f, z);
                yield return null;
            }

            if (target != null)
                target.localRotation = state.restRotation;
            rotations.Remove(target);
        }
    }
}
