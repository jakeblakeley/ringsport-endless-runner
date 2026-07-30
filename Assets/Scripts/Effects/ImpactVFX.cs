using RingSport.Level;
using UnityEngine;

namespace RingSport.Effects
{
    /// <summary>
    /// Shared world-space impact particles, following the CollectBurstVFX
    /// pattern: one pair of always-playing systems that everything Emit()s
    /// into. Dust puffs sell landings and impacts; the confetti burst is the
    /// finish-line celebration. Scene object built by
    /// Tools > RingSport > Setup Juice Polish.
    /// </summary>
    public class ImpactVFX : MonoBehaviour
    {
        [Header("Systems (wired by setup)")]
        [SerializeField] private ParticleSystem dust;
        [SerializeField] private ParticleSystem confetti;

        [Header("Dust")]
        [SerializeField] private Color dustColor = new Color(0.76f, 0.68f, 0.55f, 0.6f);
        [Tooltip("Fraction of the level scroll speed the puff drifts backward so it streams past with the track.")]
        [SerializeField] private float scrollDriftFactor = 0.5f;

        [Header("Confetti")]
        [SerializeField] private Color[] confettiColors =
        {
            new Color(0.98f, 0.36f, 0.36f),
            new Color(0.99f, 0.63f, 0.25f),
            new Color(0.99f, 0.86f, 0.31f),
            new Color(0.45f, 0.85f, 0.42f),
            new Color(0.33f, 0.71f, 0.98f),
            new Color(0.65f, 0.5f, 0.98f),
            new Color(0.98f, 0.55f, 0.83f),
            Color.white,
        };

        public static ImpactVFX Instance { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Soft ground puff for landings and body impacts.</summary>
        public static void PlayDust(Vector3 position, int count = 10, float scale = 1f)
        {
            if (Instance != null)
                Instance.EmitDust(position, count, scale);
        }

        /// <summary>Firework-style confetti pop (finish line).</summary>
        public static void PlayConfettiBurst(Vector3 position, int count = 70)
        {
            if (Instance != null)
                Instance.EmitConfetti(position, count);
        }

        private void EmitDust(Vector3 position, int count, float scale)
        {
            if (dust == null)
                return;

            float scrollSpeed = LevelScroller.Instance != null ? LevelScroller.Instance.GetScrollSpeed() : 0f;
            Vector3 drift = new Vector3(0f, 0f, -scrollSpeed * scrollDriftFactor);

            var emit = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                // Low, wide pancake puff - out along the ground with a little lift
                Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(0.5f, 1f);
                Vector3 direction = new Vector3(ring.x, Random.Range(0.25f, 0.7f), ring.y).normalized;

                emit.position = position + new Vector3(ring.x, 0f, ring.y) * 0.12f;
                emit.velocity = direction * Random.Range(0.7f, 1.7f) * scale + drift;
                emit.startSize = Random.Range(0.2f, 0.45f) * scale;
                emit.startLifetime = Random.Range(0.3f, 0.55f);
                emit.rotation = Random.Range(0f, 360f);
                emit.angularVelocity = Random.Range(-90f, 90f);
                emit.startColor = dustColor * new Color(1f, 1f, 1f, Random.Range(0.6f, 1f));

                dust.Emit(emit, 1);
            }
        }

        private void EmitConfetti(Vector3 position, int count)
        {
            if (confetti == null)
                return;

            var emit = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                // Upward firework cone; the system's gravity brings it raining down
                Vector3 direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y) + 1.1f;
                direction.Normalize();

                emit.position = position + Random.insideUnitSphere * 0.25f;
                emit.velocity = direction * Random.Range(3.5f, 7.5f);
                emit.startSize = Random.Range(0.08f, 0.16f);
                emit.startLifetime = Random.Range(1.1f, 1.9f);
                emit.rotation = Random.Range(0f, 360f);
                emit.angularVelocity = Random.Range(-420f, 420f);
                emit.startColor = confettiColors[Random.Range(0, confettiColors.Length)];

                confetti.Emit(emit, 1);
            }
        }
    }
}
