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

            // Get current speed from player (includes sprint)
            scrollSpeed = player.ForwardSpeed;

            // Apply level speed multiplier
            LevelConfig currentConfig = LevelGenerator.Instance?.GetCurrentConfig();
            if (currentConfig != null)
            {
                scrollSpeed *= currentConfig.SpeedMultiplier;
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
