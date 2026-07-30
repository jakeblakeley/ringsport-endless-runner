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
            public Vector3 restScale;
            public Coroutine routine;
        }

        private readonly Dictionary<Transform, PunchState> punches = new Dictionary<Transform, PunchState>();

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
            // Inactive hierarchies can't run coroutines from here either when
            // the target dies mid-pop - the routine self-heals on null.
            if (!isActiveAndEnabled)
                return;

            if (punches.TryGetValue(target, out var state))
            {
                // Restart from the ORIGINAL rest scale - the transform is
                // currently mid-pop, so recapturing would bake the punch in.
                if (state.routine != null)
                    StopCoroutine(state.routine);
            }
            else
            {
                state = new PunchState { restScale = target.localScale };
                punches[target] = state;
            }

            state.routine = StartCoroutine(PunchRoutine(target, state, punch, duration));
        }

        private IEnumerator PunchRoutine(Transform target, PunchState state, float punch, float duration)
        {
            float elapsed = 0f;
            const float attackPortion = 0.3f; // quick swell, longer settle
            while (elapsed < duration)
            {
                if (target == null)
                {
                    punches.Remove(target);
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                float n = Mathf.Clamp01(elapsed / duration);
                float k = n < attackPortion
                    ? Juice.OutQuad(n / attackPortion)
                    : 1f - Juice.OutQuad((n - attackPortion) / (1f - attackPortion));
                target.localScale = state.restScale * (1f + punch * k);
                yield return null;
            }

            if (target != null)
                target.localScale = state.restScale;
            punches.Remove(target);
        }
    }
}
