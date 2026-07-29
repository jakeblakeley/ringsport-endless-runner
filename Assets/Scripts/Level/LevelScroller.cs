using UnityEngine;
using RingSport.Core;
using RingSport.Player;

namespace RingSport.Level
{
    public class LevelScroller : MonoBehaviour
    {
        public static LevelScroller Instance { get; private set; }

        [SerializeField] private PlayerController player;

        private float scrollSpeed = 0f;
        private bool isPaused = false;

        // Scripted sequences (the stop attack's slow-motion charge) can pin the
        // scroll speed, overriding the player-derived speed; < 0 = no override
        private float speedOverride = -1f;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Update()
        {
            if (isPaused || GameManager.Instance?.CurrentState != GameState.Playing || player == null)
                return;

            if (speedOverride >= 0f)
            {
                scrollSpeed = speedOverride;
                LevelManager.Instance?.AddDistance(scrollSpeed * Time.deltaTime);
                return;
            }

            // Get current speed from player (includes sprint)
            scrollSpeed = player.ForwardSpeed;

            // Apply level speed multiplier
            LevelConfig currentConfig = LevelGenerator.Instance?.GetCurrentConfig();
            if (currentConfig != null)
            {
                scrollSpeed *= currentConfig.SpeedMultiplier;

                // FAIRNESS: Cap speed to prevent impossible reaction times
                scrollSpeed = Mathf.Min(scrollSpeed, currentConfig.MaxEffectiveSpeed);
            }

            // Track distance for level progress
            LevelManager.Instance?.AddDistance(scrollSpeed * Time.deltaTime);
        }

        public void ScrollObject(Transform obj)
        {
            if (isPaused || GameManager.Instance?.CurrentState != GameState.Playing)
                return;

            // Move objects toward player (negative Z direction)
            obj.position += Vector3.back * scrollSpeed * Time.deltaTime;
        }

        public float GetScrollSpeed()
        {
            return scrollSpeed;
        }

        /// <summary>Pin the world scroll to an exact speed (sprint and level multipliers ignored).</summary>
        public void SetSpeedOverride(float speed)
        {
            speedOverride = Mathf.Max(0f, speed);
        }

        public void ClearSpeedOverride()
        {
            speedOverride = -1f;
        }

        public void Pause()
        {
            isPaused = true;
        }

        public void Resume()
        {
            isPaused = false;
        }

        public void SetPlayer(PlayerController playerController)
        {
            player = playerController;
        }
    }
}
