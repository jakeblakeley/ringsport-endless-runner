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
        private bool isRunnerSpawningSuppressed = false; // Flee attack owns spawning while true

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

            // Delegate to spawning systems. During a flee attack's wind-down
            // and chase the mini level owns obstacle/coin generation; floors
            // keep flowing.
            floorSpawner.SpawnFloor();
            if (!isRunnerSpawningSuppressed)
            {
                obstacleSpawner.SpawnObstacles();
                collectibleSpawner.SpawnCollectibles();
            }

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
                    GameLog.Info("Player position and velocity reset for new level");
                }
            }

            // Get config for this level (1-9)
            int configIndex = Mathf.Clamp(levelNumber - 1, 0, levelConfigs.Length - 1);
            currentConfig = levelConfigs[configIndex];

            if (currentConfig == null)
            {
                GameLog.Error($"LevelConfig is null for level {levelNumber}! Make sure LevelConfigs array is assigned in inspector.");
                return;
            }

            GameLog.Info($"Generating Level {levelNumber} - Max Obstacles: {currentConfig.MaxObstacles}, Max Collectibles: {currentConfig.MaxCollectibles}");
            GameLog.Info($"Floor settings - Tile Length: {floorTileLength}, Tile Spacing: {floorTileSpacing}");

            // Set floor prefabs from location config
            if (currentConfig.LocationConfig != null)
            {
                floorSpawner.SetMainFloorPrefab(currentConfig.LocationConfig.MainFloorPrefab);
                floorSpawner.SetSideFloorPrefab(currentConfig.LocationConfig.SideFloorPrefab);
                floorSpawner.SetFinishLineFloorPrefab(currentConfig.LocationConfig.FinishLineFloorPrefab);
                floorSpawner.ConfigureScenery(currentConfig.LocationConfig);
                ApplyLocationAtmosphere(currentConfig.LocationConfig);
                GameLog.Info($"Location: {currentConfig.Location}, Floor prefabs set from LocationConfig");
            }
            else
            {
                floorSpawner.SetMainFloorPrefab(null);
                floorSpawner.SetSideFloorPrefab(null);
                floorSpawner.SetFinishLineFloorPrefab(null);
                floorSpawner.ConfigureScenery(null);
                GameLog.Warn($"Level {levelNumber} has no LocationConfig assigned - using fallback floor spawning");
            }

            // Reset virtual distance and ending flag
            virtualDistance = 0f;
            isLevelEnding = false;
            isRunnerSpawningSuppressed = false;

            // Reset all systems
            obstacleTracker.Clear();
            recoveryZoneManager.Reset();
            despawnManager.Clear();
            floorSpawner.Initialize();
            obstacleSpawner.Initialize();
            collectibleSpawner.Initialize();

            // Update spawn context with virtualDistance = 0 and spawn initial floors immediately
            // This ensures floors spawn at exact grid positions before any distance accumulates
            spawnContext.Update(virtualDistance, player.position, currentConfig);
            floorSpawner.SpawnFloor();

            // Spawn start scene if configured in location
            if (currentConfig.LocationConfig?.StartScenePrefab != null)
            {
                GameObject startScene = Object.Instantiate(currentConfig.LocationConfig.StartScenePrefab, Vector3.zero, Quaternion.identity);
                despawnManager.RegisterStartScene(startScene);
                GameLog.Info($"Start scene instantiated at origin: {currentConfig.LocationConfig.StartScenePrefab.name}");
            }
        }

        /// <summary>
        /// Apply the location's fog and skybox (each world has its own light mood)
        /// </summary>
        private void ApplyLocationAtmosphere(LocationConfig locationConfig)
        {
            if (locationConfig == null || !locationConfig.OverrideAtmosphere)
                return;

            RenderSettings.fogColor = locationConfig.FogColor;
            RenderSettings.fogDensity = locationConfig.FogDensity;

            if (locationConfig.SkyboxMaterial != null && RenderSettings.skybox != locationConfig.SkyboxMaterial)
            {
                RenderSettings.skybox = locationConfig.SkyboxMaterial;
                // Ambient comes from the skybox; rebuild the ambient probe once per swap
                DynamicGI.UpdateEnvironment();
            }

            GameLog.Info($"Atmosphere applied for {locationConfig.Location}: fog {locationConfig.FogColor} d={locationConfig.FogDensity}, skybox {(locationConfig.SkyboxMaterial != null ? locationConfig.SkyboxMaterial.name : "unchanged")}");
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
        /// Suspends/restores the normal obstacle and collectible spawners.
        /// The flee attack turns this on a few seconds BEFORE its chase so the
        /// already-spawned course scrolls past the player naturally, leaving a
        /// clean gap of empty track - nothing visible is ever despawned.
        /// </summary>
        public void SetRunnerSpawningSuppressed(bool suppressed)
        {
            if (isRunnerSpawningSuppressed == suppressed)
                return;

            isRunnerSpawningSuppressed = suppressed;
            GameLog.Info(suppressed
                ? "Runner spawning suspended (flee attack wind-down)"
                : "Runner spawning restored");
        }

        /// <summary>
        /// First level number whose config uses the given mini level type,
        /// or -1 if none does. Used to pick a host level for in-run mini
        /// levels launched from the debug menu.
        /// </summary>
        public int FindFirstLevelWithMiniLevel(MiniLevelType type)
        {
            if (levelConfigs == null)
                return -1;

            for (int i = 0; i < levelConfigs.Length; i++)
            {
                if (levelConfigs[i] != null && levelConfigs[i].MiniLevelType == type)
                    return i + 1;
            }
            return -1;
        }

        /// <summary>
        /// Called when level is ending - starts despawning distant obstacles for fairness
        /// FAIRNESS: Prevents unfair hits from obstacles too far ahead
        /// </summary>
        public void OnLevelEnding()
        {
            isLevelEnding = true;
            floorSpawner.SetFinishLinePosition(endGameDespawnDistance);
            GameLog.Info("Level ending - will despawn obstacles beyond " + endGameDespawnDistance + " units ahead");
        }

        /// <summary>
        /// Get current virtual distance (for external systems to check)
        /// </summary>
        public float GetVirtualDistance()
        {
            return virtualDistance;
        }

        /// <summary>
        /// Load a level's location for the home / level intro screen
        /// Spawns initial floors and start scene without starting gameplay
        /// </summary>
        /// <param name="levelNumber">Level whose location is used as the backdrop (defaults to level 1)</param>
        public void LoadHomeScene(int levelNumber = 1)
        {
            // Clear any previous level content
            ObjectPooler.Instance?.ClearAllPools();

            if (levelConfigs == null || levelConfigs.Length == 0)
            {
                GameLog.Warn("No level configs available for home scene");
                return;
            }

            currentConfig = GetLevelConfig(levelNumber);

            if (currentConfig == null)
            {
                GameLog.Error($"LevelConfig for level {levelNumber} is null! Make sure LevelConfigs array is assigned in inspector.");
                return;
            }

            GameLog.Info($"Loading home scene with location: {currentConfig.Location}");

            // Set floor prefabs from location config
            if (currentConfig.LocationConfig != null)
            {
                floorSpawner.SetMainFloorPrefab(currentConfig.LocationConfig.MainFloorPrefab);
                floorSpawner.SetSideFloorPrefab(currentConfig.LocationConfig.SideFloorPrefab);
                floorSpawner.SetFinishLineFloorPrefab(null); // No finish line for home screen
                floorSpawner.ConfigureScenery(currentConfig.LocationConfig);
                ApplyLocationAtmosphere(currentConfig.LocationConfig);
            }
            else
            {
                floorSpawner.SetMainFloorPrefab(null);
                floorSpawner.SetSideFloorPrefab(null);
                floorSpawner.SetFinishLineFloorPrefab(null);
                floorSpawner.ConfigureScenery(null);
                GameLog.Warn($"Level {levelNumber} has no LocationConfig - home scene may look empty");
            }

            // Reset virtual distance and ending flag
            virtualDistance = 0f;
            isLevelEnding = false;
            isRunnerSpawningSuppressed = false;

            // Reset all systems
            obstacleTracker.Clear();
            recoveryZoneManager.Reset();
            despawnManager.Clear();
            floorSpawner.Initialize();
            obstacleSpawner.Initialize();
            collectibleSpawner.Initialize();

            // Update spawn context and spawn initial floors
            if (player != null)
            {
                spawnContext.Update(virtualDistance, player.position, currentConfig);
                floorSpawner.SpawnFloor();
            }

            // Spawn start scene if configured
            if (currentConfig.LocationConfig?.StartScenePrefab != null)
            {
                GameObject startScene = Object.Instantiate(currentConfig.LocationConfig.StartScenePrefab, Vector3.zero, Quaternion.identity);
                despawnManager.RegisterStartScene(startScene);
                GameLog.Info($"Home scene: Start scene instantiated at origin: {currentConfig.LocationConfig.StartScenePrefab.name}");
            }
        }

        // Debug methods for inspector visibility
        #if UNITY_EDITOR
        [ContextMenu("Debug: Print System Stats")]
        private void PrintSystemStats()
        {
            GameLog.Info($"=== Level Generator Stats ===");
            GameLog.Info($"Virtual Distance: {virtualDistance:F2}");
            GameLog.Info($"Obstacles Spawned: {obstacleSpawner.GetObstaclesSpawned()}");
            GameLog.Info($"Collectibles Spawned: {collectibleSpawner.GetCollectiblesSpawned()}");
            GameLog.Info($"Active Obstacles: {despawnManager.GetActiveObstacleCount()}");
            GameLog.Info($"Active Collectibles: {despawnManager.GetActiveCollectibleCount()}");
            GameLog.Info($"Active Floor Tiles: {despawnManager.GetActiveFloorTileCount()}");
            GameLog.Info($"Active Scenery: {despawnManager.GetActiveSceneryCount()}");
            GameLog.Info($"Tracked Obstacles: {obstacleTracker.Count}");
        }
        #endif
    }
}
