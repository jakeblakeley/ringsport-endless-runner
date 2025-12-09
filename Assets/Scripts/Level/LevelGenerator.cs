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

        [Header("End Game Settings")]
        [Tooltip("Distance ahead of player at which obstacles are despawned when level is ending")]
        [SerializeField] private float endGameDespawnDistance = 10f;

        [Header("Floor Settings")]
        [SerializeField] private float floorTileLength = 10f;
        [SerializeField] private float floorTileSpacing = 10f; // Distance between tile start positions
        [Tooltip("Scale multiplier for floor tiles (also scales length and spacing)")]
        [SerializeField] private float floorScale = 1f;
        [SerializeField] private GameObject finishLineFloorPrefab;

        // Core systems
        private LevelConfig currentConfig;
        private float virtualDistance = 0f; // Tracks how far the level has scrolled
        private bool isLevelEnding = false; // Tracks if we're in the end game phase

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
            despawnManager = new DespawnManager(despawnDistance, endGameDespawnDistance);

            // Create spawning systems (apply scale to floor dimensions)
            float scaledTileLength = floorTileLength * floorScale;
            float scaledTileSpacing = floorTileSpacing * floorScale;
            floorSpawner = new FloorSpawner(spawnContext, despawnManager, scaledTileLength, scaledTileSpacing, floorScale, finishLineFloorPrefab);
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

            // During end game, also despawn obstacles and collectibles too far ahead
            if (isLevelEnding)
            {
                despawnManager.DespawnObstaclesAheadOfPlayer(player.position);
                despawnManager.DespawnCollectiblesAheadOfPlayer(player.position);
            }

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

            // Set floor prefabs from location config
            if (currentConfig.LocationConfig != null)
            {
                floorSpawner.SetMainFloorPrefab(currentConfig.LocationConfig.MainFloorPrefab);
                floorSpawner.SetSideFloorPrefab(currentConfig.LocationConfig.SideFloorPrefab);
                floorSpawner.SetFinishLineFloorPrefab(currentConfig.LocationConfig.FinishLineFloorPrefab);
                floorSpawner.ConfigureScenery(currentConfig.LocationConfig);
                Debug.Log($"Location: {currentConfig.Location}, Floor prefabs set from LocationConfig");
            }
            else
            {
                floorSpawner.SetMainFloorPrefab(null);
                floorSpawner.SetSideFloorPrefab(null);
                floorSpawner.SetFinishLineFloorPrefab(null);
                floorSpawner.ConfigureScenery(null);
                Debug.LogWarning($"Level {levelNumber} has no LocationConfig assigned - using fallback floor spawning");
            }

            // Reset virtual distance and ending flag
            virtualDistance = 0f;
            isLevelEnding = false;

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
        /// Called when level is ending - starts despawning distant obstacles for fairness
        /// FAIRNESS: Prevents unfair hits from obstacles too far ahead
        /// </summary>
        public void OnLevelEnding()
        {
            isLevelEnding = true;
            floorSpawner.SetFinishLinePosition(endGameDespawnDistance);
            Debug.Log("Level ending - will despawn obstacles beyond " + endGameDespawnDistance + " units ahead");
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
            Debug.Log($"Active Scenery: {despawnManager.GetActiveSceneryCount()}");
            Debug.Log($"Tracked Obstacles: {obstacleTracker.Count}");
        }
        #endif
    }
}
