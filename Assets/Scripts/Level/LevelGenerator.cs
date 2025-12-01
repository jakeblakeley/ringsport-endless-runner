using UnityEngine;
using RingSport.Core;
using RingSport.Level.Spawning;

namespace RingSport.Level
{
    /// <summary>
    /// Coordinates all level generation systems for procedural endless runner gameplay
    /// Refactored to follow SOLID principles and Unity best practices
    /// </summary>
    public class LevelGenerator : MonoBehaviour
    {
        public static LevelGenerator Instance { get; private set; }

        [Header("Level Configuration")]
        [SerializeField] private LevelConfig[] levelConfigs;

        [Header("Pattern Library")]
        [Tooltip("Hand-crafted obstacle patterns for more memorable gameplay")]
        [SerializeField] private ObstaclePattern[] obstaclePatterns;

        [Header("Spawn Settings")]
        [SerializeField] private float spawnDistance = 50f;
        [SerializeField] private float despawnDistance = -10f;
        [SerializeField] private Transform player;

        [Header("Floor Settings")]
        [SerializeField] private float floorTileLength = 10f;
        [SerializeField] private float floorTileSpacing = 10f; // Distance between tile start positions

        // Core systems
        private LevelConfig currentConfig;
        private float virtualDistance = 0f; // Tracks how far the level has scrolled

        // Spawning and management systems
        private SpawnContext spawnContext;
        private ObstacleTracker obstacleTracker;
        private RecoveryZoneManager recoveryZoneManager;
        private DespawnManager despawnManager;
        private FloorSpawner floorSpawner;
        private ObstacleSpawner obstacleSpawner;
        private CollectibleSpawner collectibleSpawner;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Initialize all systems
            InitializeSystems();
        }

        /// <summary>
        /// Initialize all subsystems
        /// </summary>
        private void InitializeSystems()
        {
            // Create spawn context
            spawnContext = new SpawnContext(spawnDistance);

            // Create tracking and management systems
            obstacleTracker = new ObstacleTracker();
            recoveryZoneManager = new RecoveryZoneManager();
            despawnManager = new DespawnManager(despawnDistance);

            // Create spawning systems
            floorSpawner = new FloorSpawner(spawnContext, despawnManager, floorTileLength, floorTileSpacing);
            obstacleSpawner = new ObstacleSpawner(
                spawnContext,
                obstacleTracker,
                recoveryZoneManager,
                despawnManager,
                obstaclePatterns,
                levelConfigs);
            collectibleSpawner = new CollectibleSpawner(spawnContext, obstacleTracker, despawnManager);
        }

        private void Update()
        {
            if (currentConfig == null || player == null)
                return;

            // Update virtual distance based on scroll speed
            if (LevelScroller.Instance != null && GameManager.Instance?.CurrentState == GameState.Playing)
            {
                virtualDistance += LevelScroller.Instance.GetScrollSpeed() * Time.deltaTime;
            }

            // Update spawn context with current frame data
            spawnContext.Update(virtualDistance, player.position, currentConfig);

            // Delegate to spawning systems
            floorSpawner.SpawnFloor();
            obstacleSpawner.SpawnObstacles();
            collectibleSpawner.SpawnCollectibles();

            // Delegate to management systems
            despawnManager.DespawnBehindPlayer(player.position);
            obstacleTracker.Cleanup(virtualDistance);
        }

        /// <summary>
        /// Generate a new level with the specified configuration
        /// </summary>
        public void GenerateLevel(int levelNumber)
        {
            // Clear previous level
            ObjectPooler.Instance?.ClearAllPools();

            // Reset player to starting position and state
            if (player != null)
            {
                var playerController = player.GetComponent<RingSport.Player.PlayerController>();
                if (playerController != null)
                {
                    playerController.ResetPosition();
                    Debug.Log("Player position and velocity reset for new level");
                }
            }

            // Get config for this level (1-9)
            int configIndex = Mathf.Clamp(levelNumber - 1, 0, levelConfigs.Length - 1);
            currentConfig = levelConfigs[configIndex];

            if (currentConfig == null)
            {
                Debug.LogError($"LevelConfig is null for level {levelNumber}! Make sure LevelConfigs array is assigned in inspector.");
                return;
            }

            Debug.Log($"Generating Level {levelNumber} - Max Obstacles: {currentConfig.MaxObstacles}, Max Collectibles: {currentConfig.MaxCollectibles}");
            Debug.Log($"Floor settings - Tile Length: {floorTileLength}, Tile Spacing: {floorTileSpacing}");

            // Reset virtual distance
            virtualDistance = 0f;

            // Reset all systems
            obstacleTracker.Clear();
            recoveryZoneManager.Reset();
            despawnManager.Clear();
            floorSpawner.Initialize();
            obstacleSpawner.Initialize();
            collectibleSpawner.Initialize();
        }

        /// <summary>
        /// Set the player transform reference
        /// </summary>
        public void SetPlayer(Transform playerTransform)
        {
            player = playerTransform;
        }

        /// <summary>
        /// Get the current level configuration
        /// </summary>
        public LevelConfig GetCurrentConfig()
        {
            return currentConfig;
        }

        /// <summary>
        /// Get a specific level configuration by level number
        /// </summary>
        public LevelConfig GetLevelConfig(int levelNumber)
        {
            if (levelConfigs == null || levelConfigs.Length == 0)
                return null;

            int configIndex = Mathf.Clamp(levelNumber - 1, 0, levelConfigs.Length - 1);
            return levelConfigs[configIndex];
        }

        /// <summary>
        /// Called when a palisade minigame is completed
        /// Creates a recovery zone (no obstacles) for fairness
        /// </summary>
        public void OnPalisadeCompleted()
        {
            recoveryZoneManager.StartRecoveryZone(virtualDistance);
        }

        /// <summary>
        /// Called when level is ending - despawns all obstacles for fairness
        /// FAIRNESS: Prevents unfair hits from obstacles that spawned earlier
        /// </summary>
        public void OnLevelEnding()
        {
            despawnManager.DespawnAllObstacles();
            obstacleTracker.Clear();
        }

        /// <summary>
        /// Get current virtual distance (for external systems to check)
        /// </summary>
        public float GetVirtualDistance()
        {
            return virtualDistance;
        }

        // Debug methods for inspector visibility
        #if UNITY_EDITOR
        [ContextMenu("Debug: Print System Stats")]
        private void PrintSystemStats()
        {
            Debug.Log($"=== Level Generator Stats ===");
            Debug.Log($"Virtual Distance: {virtualDistance:F2}");
            Debug.Log($"Obstacles Spawned: {obstacleSpawner.GetObstaclesSpawned()}");
            Debug.Log($"Collectibles Spawned: {collectibleSpawner.GetCollectiblesSpawned()}");
            Debug.Log($"Active Obstacles: {despawnManager.GetActiveObstacleCount()}");
            Debug.Log($"Active Collectibles: {despawnManager.GetActiveCollectibleCount()}");
            Debug.Log($"Active Floor Tiles: {despawnManager.GetActiveFloorTileCount()}");
            Debug.Log($"Tracked Obstacles: {obstacleTracker.Count}");
        }
        #endif
    }
}
