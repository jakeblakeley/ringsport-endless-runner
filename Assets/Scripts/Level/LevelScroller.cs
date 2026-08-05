using System.Collections.Generic;
using UnityEngine;
using RingSport.Core;
using RingSport.Player;

namespace RingSport.Level
{
    public class LevelScroller : MonoBehaviour
    {
        public static LevelScroller Instance { get; private set; }

        [SerializeField] private PlayerController player;

        [Header("Speed Sensation")]
        [Tooltip("Extra camera FOV per unit of scroll speed above the base speed - subtle sense of speed (sprint widens naturally). 0 disables.")]
        [SerializeField] private float fovPerSpeedUnit = 0.6f;
        [SerializeField] private float fovBaseSpeed = 10f;
        [SerializeField] private float fovMaxOffset = 7f;

        private float scrollSpeed = 0f;
        private bool isPaused = false;

        // Scripted sequences (the stop attack's slow-motion charge) can pin the
        // scroll speed, overriding the player-derived speed; < 0 = no override
        private float speedOverride = -1f;

        // All live ScrollableObjects, moved in one loop per frame. Static so
        // pooled objects can register from OnEnable before the scene's
        // LevelScroller has run Awake. Removal is O(1) swap-remove using the
        // index stored on each ScrollableObject.
        private static readonly List<ScrollableObject> scrollables = new List<ScrollableObject>(256);

        internal static void Register(ScrollableObject s)
        {
            if (s.RegIndex >= 0)
                return;
            s.RegIndex = scrollables.Count;
            scrollables.Add(s);
        }

        internal static void Unregister(ScrollableObject s)
        {
            int index = s.RegIndex;
            if (index < 0)
                return;
            int last = scrollables.Count - 1;
            ScrollableObject moved = scrollables[last];
            scrollables[index] = moved;
            moved.RegIndex = index;
            scrollables.RemoveAt(last);
            s.RegIndex = -1;
        }

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
            {
                CameraStateMachine.Instance?.SetSpeedFov(0f);
                return;
            }

            if (speedOverride >= 0f)
            {
                scrollSpeed = speedOverride;
                LevelManager.Instance?.AddDistance(scrollSpeed * Time.deltaTime);
                ScrollAll();
                ApplySpeedFov();
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
            ScrollAll();
            ApplySpeedFov();
        }

        /// <summary>Move every registered object toward the player in one pass.</summary>
        private void ScrollAll()
        {
            float dz = scrollSpeed * Time.deltaTime;
            if (dz == 0f)
                return;

            Vector3 delta = new Vector3(0f, 0f, -dz);
            for (int i = 0; i < scrollables.Count; i++)
                scrollables[i].CachedTransform.position += delta;
        }

        /// <summary>Camera widens slightly with speed (sprint and fast levels feel faster).</summary>
        private void ApplySpeedFov()
        {
            float offset = Mathf.Clamp((scrollSpeed - fovBaseSpeed) * fovPerSpeedUnit, 0f, fovMaxOffset);
            CameraStateMachine.Instance?.SetSpeedFov(offset);
        }

        public float GetScrollSpeed()
        {
            return scrollSpeed;
        }

        /// <summary>True while a scripted sequence is pinning the scroll speed.</summary>
        public bool HasSpeedOverride => speedOverride >= 0f;

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
