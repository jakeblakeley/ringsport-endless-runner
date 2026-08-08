using UnityEngine;
using RingSport.Level;

namespace RingSport.Effects
{
    /// <summary>
    /// Shared pickup-burst VFX (Subway Surfers-style sparkle pop). ONE pair of
    /// world-space particle systems serves every pickup in the run: pickups call
    /// Emit() on the shared systems, so collecting a 10-coin train costs zero
    /// instantiations and the whole effect stays at two draw calls. Tier scales
    /// the burst: small coins get a modest pop, large coins more sparks over a
    /// wider radius, love notes the most. Scene object built by
    /// Tools > RingSport > Setup Particle Polish.
    /// </summary>
    public class CollectBurstVFX : MonoBehaviour
    {
        [System.Serializable]
        public class BurstTier
        {
            public int sparkCount = 10;
            public float speedMin = 2f;
            public float speedMax = 3.5f;
            public float sizeMin = 0.1f;
            public float sizeMax = 0.2f;
            public float lifeMin = 0.28f;
            public float lifeMax = 0.42f;
            [Tooltip("Random spawn offset radius - widens the burst origin for bigger pickups.")]
            public float originJitter = 0.05f;
            public float flashSize = 0.6f;
        }

        [Header("Systems (wired by setup)")]
        [SerializeField] private ParticleSystem sparks;
        [SerializeField] private ParticleSystem flash;

        [Header("Tiers")]
        [SerializeField] private BurstTier smallCoin = new BurstTier
        {
            sparkCount = 10, speedMin = 2.2f, speedMax = 3.6f,
            sizeMin = 0.09f, sizeMax = 0.17f, lifeMin = 0.25f, lifeMax = 0.4f,
            originJitter = 0.05f, flashSize = 0.55f
        };
        [SerializeField] private BurstTier largeCoin = new BurstTier
        {
            sparkCount = 18, speedMin = 2.8f, speedMax = 4.8f,
            sizeMin = 0.11f, sizeMax = 0.22f, lifeMin = 0.3f, lifeMax = 0.45f,
            originJitter = 0.16f, flashSize = 0.85f
        };
        [SerializeField] private BurstTier loveNote = new BurstTier
        {
            sparkCount = 28, speedMin = 3.2f, speedMax = 5.6f,
            sizeMin = 0.12f, sizeMax = 0.26f, lifeMin = 0.35f, lifeMax = 0.55f,
            originJitter = 0.22f, flashSize = 1.1f
        };
        [SerializeField] private BurstTier nearMiss = new BurstTier
        {
            sparkCount = 6, speedMin = 1.6f, speedMax = 2.8f,
            sizeMin = 0.07f, sizeMax = 0.13f, lifeMin = 0.2f, lifeMax = 0.32f,
            originJitter = 0.18f, flashSize = 0.4f
        };

        [Header("Colors")]
        [SerializeField] private Color coinColor = new Color(0.962f, 0.96f, 0.422f);     // matches TestCoin.mat yellow
        [SerializeField] private Color loveNoteColor = new Color(0.8f, 0.4745f, 0.047f); // #CC790C
        [SerializeField] private Color lifeColor = Color.white;

        [Header("Feel")]
        [Tooltip("Chance a spark is a white-hot glint instead of the pickup color - keeps back-to-back bursts from reading identical.")]
        [SerializeField] [Range(0f, 1f)] private float glintChance = 0.3f;
        [Tooltip("Fraction of the level scroll speed the burst drifts backward, so it sweeps past with the world instead of hanging mid-air.")]
        [SerializeField] private float scrollDriftFactor = 0.35f;
        [Tooltip("Upward kick added to every spark - pickups pop up-and-out, not straight down.")]
        [SerializeField] private float upwardBias = 0.8f;
        [SerializeField] private float flashLifetime = 0.16f;

        public static CollectBurstVFX Instance { get; private set; }

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

        /// <summary>Yellow burst for coins; large coins burst bigger and wider.</summary>
        public static void PlayCoin(Vector3 position, bool largeCoin)
        {
            if (Instance != null)
                Instance.PlayBurst(position, Instance.coinColor, largeCoin ? Instance.largeCoin : Instance.smallCoin);
        }

        /// <summary>Biggest burst, love-note orange (#CC790C).</summary>
        public static void PlayLoveNote(Vector3 position)
        {
            if (Instance != null)
                Instance.PlayBurst(position, Instance.loveNoteColor, Instance.loveNote);
        }

        /// <summary>Hat pickups share the love note's top-tier burst.</summary>
        public static void PlayHat(Vector3 position)
        {
            if (Instance != null)
                Instance.PlayBurst(position, Instance.loveNoteColor, Instance.loveNote);
        }

        /// <summary>White burst for life pickups.</summary>
        public static void PlayLife(Vector3 position)
        {
            if (Instance != null)
                Instance.PlayBurst(position, Instance.lifeColor, Instance.largeCoin);
        }

        /// <summary>Small white glint for clean near-miss obstacle clears.</summary>
        public static void PlayNearMiss(Vector3 position)
        {
            if (Instance != null)
                Instance.PlayBurst(position, Color.white, Instance.nearMiss);
        }

        private void PlayBurst(Vector3 position, Color color, BurstTier tier)
        {
            if (sparks == null || flash == null)
                return;

            // The camera is fixed and the WORLD scrolls, so world-space particles
            // would hang at the pickup point; a backward drift sells them as
            // streaming past with the track.
            float scrollSpeed = LevelScroller.Instance != null ? LevelScroller.Instance.GetScrollSpeed() : 0f;
            Vector3 drift = new Vector3(0f, 0f, -scrollSpeed * scrollDriftFactor);

            var emit = new ParticleSystem.EmitParams();
            for (int i = 0; i < tier.sparkCount; i++)
            {
                // Radial pop biased upward (firework, not rain)
                Vector3 direction = Random.onUnitSphere;
                direction.y = Mathf.Abs(direction.y);
                direction = (direction + Vector3.up * upwardBias).normalized;

                emit.position = position + Random.insideUnitSphere * tier.originJitter;
                emit.velocity = direction * Random.Range(tier.speedMin, tier.speedMax) + drift;
                emit.startSize = Random.Range(tier.sizeMin, tier.sizeMax);
                emit.startLifetime = Random.Range(tier.lifeMin, tier.lifeMax);
                emit.rotation = Random.Range(0f, 360f);
                emit.angularVelocity = Random.Range(-300f, 300f);

                // Most sparks carry the pickup color; occasional white-hot glints
                float whiten = Random.value < glintChance ? Random.Range(0.45f, 0.8f) : Random.Range(0f, 0.15f);
                emit.startColor = Color.Lerp(color, Color.white, whiten);

                sparks.Emit(emit, 1);
            }

            // One hot center pop that sells the grab moment
            var flashEmit = new ParticleSystem.EmitParams
            {
                position = position,
                velocity = drift * 0.5f,
                startSize = tier.flashSize,
                startLifetime = flashLifetime,
                startColor = Color.Lerp(color, Color.white, 0.55f)
            };
            flash.Emit(flashEmit, 1);
        }
    }
}
