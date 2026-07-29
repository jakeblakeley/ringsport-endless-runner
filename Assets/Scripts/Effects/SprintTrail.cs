using UnityEngine;
using RingSport.Level;

namespace RingSport.Effects
{
    /// <summary>
    /// White speed-line streaks around the dog while sprinting: stretched-
    /// billboard particles blown backward past the body, line length scaling
    /// with the level's scroll speed. PlayerController toggles it from the same
    /// place it picks the sprint gait tier. Wired into the Player by
    /// Tools > RingSport > Setup Particle Polish.
    /// </summary>
    public class SprintTrail : MonoBehaviour
    {
        [SerializeField] private ParticleSystem lines;

        [Tooltip("Streaks spawned per second while sprinting.")]
        [SerializeField] private float linesPerSecond = 26f;
        [Tooltip("Line speed = scroll speed x this (they overtake the world slightly, reading as wind).")]
        [SerializeField] private float speedFactor = 1.5f;
        [Tooltip("Floor for line speed so the trail never looks limp on slow levels.")]
        [SerializeField] private float minLineSpeed = 14f;

        private bool sprinting;
        private ParticleSystem.EmissionModule emission;
        private ParticleSystem.MainModule main;

        private void Awake()
        {
            if (lines == null)
                lines = GetComponentInChildren<ParticleSystem>(true);

            if (lines == null)
            {
                Debug.LogWarning("SprintTrail: no ParticleSystem found. Run Tools > RingSport > Setup Particle Polish.");
                enabled = false;
                return;
            }

            emission = lines.emission;
            main = lines.main;
            emission.rateOverTimeMultiplier = 0f;
        }

        private void Update()
        {
            if (!sprinting)
                return;

            // Track the live scroll speed so the streaks always outrun the world
            float scrollSpeed = LevelScroller.Instance != null ? LevelScroller.Instance.GetScrollSpeed() : 0f;
            main.startSpeed = Mathf.Max(minLineSpeed, scrollSpeed * speedFactor);
        }

        /// <summary>Called by PlayerController alongside the sprint gait tier.</summary>
        public void SetSprinting(bool active)
        {
            if (sprinting == active || lines == null)
                return;

            sprinting = active;
            emission.rateOverTimeMultiplier = active ? linesPerSecond : 0f;
        }
    }
}
