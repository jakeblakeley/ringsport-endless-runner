using UnityEngine;
using RingSport.Core;
using RingSport.Level.Spawning;
using RingSport.UI;

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

        [Header("Mini Level Order")]
        [Tooltip("Re-roll which mini level the opening levels end in at the start of every run, so a run doesn't always go food refusal -> positions -> flee attack.")]
        [SerializeField] private bool randomizeEarlyMiniLevels = true;
        [Tooltip("Levels 1..this swap mini levels among themselves. The set is unchanged - each of those mini levels still plays exactly once, just not always on the same level.")]
        [SerializeField] private int randomizedMiniLevelCount = 3;
        [Tooltip("Shortest running section a level may be left with when it hosts an in-run mini level (flee/stop attack), which eats the end of its run. Levels too short for the chase are skipped when shuffling.")]
        [SerializeField] private float minRunSecondsBeforeChase = 12f;

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
        private int currentLevelNumber = 1;
        private float virtualDistance = 0f; // Tracks how far the level has scrolled
        private bool isLevelEnding = false; // Tracks if we're in the end game phase
        private bool isRunnerSpawningSuppressed = false; // Flee attack owns spawning while true

        // Which mini level each level ends in THIS run (index = level - 1).
        // Seeded from the LevelConfig assets, then shuffled per run - the
        // assets themselves are never written to (a runtime write to a
        // ScriptableObject sticks in the editor and would rewrite the level
        // data on disk).
        private MiniLevelType[] miniLevelOrder;

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
            currentLevelNumber = configIndex + 1;

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
                ApplyAmbientForSkybox(locationConfig);
            }

            GameLog.Info($"Atmosphere applied for {locationConfig.Location}: fog {locationConfig.FogColor} d={locationConfig.FogDensity}, skybox {(locationConfig.SkyboxMaterial != null ? locationConfig.SkyboxMaterial.name : "unchanged")}");
        }

        /// <summary>
        /// Ambient light for the new skybox. DynamicGI.UpdateEnvironment() derives it
        /// by rendering the skybox to a cubemap and reading it back to the CPU, which
        /// WebGPU does not support ("Texture Readback is not supported by WebGPU") -
        /// it leaves the ambient probe garbage and every lit surface renders wrong,
        /// while the skybox and UI stay correct because neither is ambient-lit.
        /// The probe is baked per location in the editor instead; the live path is
        /// kept only as a fallback for a location that has not been baked yet.
        /// </summary>
        private void ApplyAmbientForSkybox(LocationConfig locationConfig)
        {
            if (locationConfig.HasBakedAmbientProbe)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Custom;
                RenderSettings.ambientProbe = locationConfig.BakedAmbientProbe;
                return;
            }

            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.WebGPU)
            {
                // Keeping the previous ambient is wrong, but it is a mood mismatch
                // rather than a frame of red garbage plus per-object error spam.
                GameLog.Warn($"No baked ambient probe for {locationConfig.Location} and WebGPU cannot compute one at runtime - " +
                             "run Tools/RingSport/Bake Location Ambient. Keeping the previous ambient.");
                return;
            }

            // Skybox mode, or UpdateEnvironment has nothing to derive ambient from
            // once an earlier location has switched the mode to Custom.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
            DynamicGI.UpdateEnvironment();
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
        /// First level number that runs the given mini level THIS run, or -1
        /// if none does. Used to pick a host level for in-run mini levels
        /// launched from the debug menu.
        /// </summary>
        public int FindFirstLevelWithMiniLevel(MiniLevelType type)
        {
            EnsureMiniLevelOrder();
            if (miniLevelOrder == null)
                return -1;

            for (int i = 0; i < miniLevelOrder.Length; i++)
            {
                if (miniLevelOrder[i] == type)
                    return i + 1;
            }
            return -1;
        }

        /// <summary>
        /// The mini level a given level ends in this run. Always go through
        /// this rather than LevelConfig.MiniLevelType - the config holds the
        /// authored order, this holds the shuffled one actually being played.
        /// </summary>
        public MiniLevelType GetMiniLevelType(int levelNumber)
        {
            EnsureMiniLevelOrder();
            if (miniLevelOrder == null || miniLevelOrder.Length == 0)
                return MiniLevelType.PositionsSimonSays;

            int index = Mathf.Clamp(levelNumber - 1, 0, miniLevelOrder.Length - 1);
            return miniLevelOrder[index];
        }

        /// <summary>Mini level the level currently loaded ends in.</summary>
        public MiniLevelType CurrentMiniLevelType => GetMiniLevelType(currentLevelNumber);

        /// <summary>
        /// Re-rolls the opening mini levels for a new run. The first
        /// randomizedMiniLevelCount levels keep the same SET of mini levels the
        /// configs authored - only which level ends in which changes - so a run
        /// still plays one of each, just not always in the same order.
        /// Everything past that window keeps its authored mini level.
        /// Called once per run from LevelManager.ResetProgress, so retries and
        /// level-to-level progression all see the same order.
        /// </summary>
        public void ShuffleMiniLevelOrder()
        {
            miniLevelOrder = null;
            EnsureMiniLevelOrder();

            if (!randomizeEarlyMiniLevels || miniLevelOrder == null)
                return;

            int count = Mathf.Min(randomizedMiniLevelCount, miniLevelOrder.Length);
            if (count < 2)
                return;

            var pool = new MiniLevelType[count];
            System.Array.Copy(miniLevelOrder, pool, count);

            // Reject orders a level can't host - an in-run chase eats the end
            // of its level, so a short level would be left with no running
            // section in front of it - and fall back to the authored order
            // rather than shipping an unfair one.
            for (int attempt = 0; attempt < 32; attempt++)
            {
                ShuffleInPlace(pool);
                if (!IsHostableMiniLevelOrder(pool))
                    continue;

                System.Array.Copy(pool, miniLevelOrder, count);
                LogMiniLevelOrder(count);
                return;
            }

            GameLog.Warn("[LevelGenerator] No hostable shuffle of the opening mini levels found - keeping the authored order");
        }

        /// <summary>
        /// Builds the per-level mini level order from the configs if it hasn't
        /// been built (or the config array changed) since the last shuffle.
        /// </summary>
        private void EnsureMiniLevelOrder()
        {
            if (levelConfigs == null || levelConfigs.Length == 0)
                return;

            if (miniLevelOrder != null && miniLevelOrder.Length == levelConfigs.Length)
                return;

            miniLevelOrder = new MiniLevelType[levelConfigs.Length];
            for (int i = 0; i < levelConfigs.Length; i++)
            {
                miniLevelOrder[i] = levelConfigs[i] != null
                    ? levelConfigs[i].MiniLevelType
                    : MiniLevelType.PositionsSimonSays;
            }
        }

        private static void ShuffleInPlace(MiniLevelType[] items)
        {
            for (int i = items.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                MiniLevelType swap = items[i];
                items[i] = items[j];
                items[j] = swap;
            }
        }

        private bool IsHostableMiniLevelOrder(MiniLevelType[] candidate)
        {
            for (int i = 0; i < candidate.Length; i++)
            {
                if (!CanHostMiniLevel(i + 1, candidate, i))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Whether a level is long enough to run the mini level the candidate
        /// order gave it. Arena mini levels play after the run, so any level
        /// hosts those; an in-run chase begins GetLeadSeconds before the level
        /// timer ends with spawning suppressed for a wind-down before that, so
        /// the level must still leave minRunSecondsBeforeChase of real running
        /// in front of it. Level 1 is short enough that the flee attack fails
        /// this and stays on a later level.
        /// </summary>
        private bool CanHostMiniLevel(int levelNumber, MiniLevelType[] candidate, int index)
        {
            InRunMiniLevel controller = InRunMiniLevel.GetController(candidate[index]);
            if (controller == null)
                return true;

            LevelConfig config = GetLevelConfig(levelNumber);
            if (config == null)
                return true;

            // Difficulty ordinal within the shuffled window, matching
            // LevelManager.ComputeInRunDifficulty (later repeats run harder,
            // and harder rows need a longer lead)
            int difficultyIndex = 0;
            for (int i = 0; i < index; i++)
            {
                if (candidate[i] == candidate[index])
                    difficultyIndex++;
            }

            float windDown = LevelManager.Instance != null ? LevelManager.Instance.InRunWindDownSeconds : 7.5f;
            float runSeconds = config.LevelDuration - controller.GetLeadSeconds(difficultyIndex) - windDown;
            return runSeconds >= minRunSecondsBeforeChase;
        }

        private void LogMiniLevelOrder(int count)
        {
            var order = new System.Text.StringBuilder();
            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                    order.Append(", ");
                order.Append($"L{i + 1}={miniLevelOrder[i]}");
            }
            GameLog.Info($"[LevelGenerator] Mini level order this run: {order}");
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
            currentLevelNumber = Mathf.Clamp(levelNumber, 1, levelConfigs.Length);

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
