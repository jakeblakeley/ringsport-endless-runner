using UnityEngine;
using RingSport.Core;
using System.Collections.Generic;

namespace RingSport.Level
{
    /// <summary>
    /// Holds data about spawned obstacles for collectible placement logic
    /// </summary>
    public struct ObstacleData
    {
        public float zPosition;
        public int lane; // -1, 0, or 1 for left, center, right
        public string obstacleType; // "ObstacleJump", "ObstacleAvoid", "ObstaclePalisade", "ObstaclePylon", "ObstacleBroadJump"

        public ObstacleData(float z, int lane, string type)
        {
            zPosition = z;
            this.lane = lane;
            obstacleType = type;
        }

        public bool CanHaveCollectibleAbove()
        {
            // Collectibles can spawn above jumps and palisades
            return obstacleType == "ObstacleJump" || obstacleType == "ObstaclePalisade";
        }
    }

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

        private LevelConfig currentConfig;
        private float nextObstacleSpawnZ;
        private float nextCollectibleSpawnZ;
        private float nextFloorSpawnZ;
        private int obstaclesSpawned;
        private int collectiblesSpawned;
        private float virtualDistance = 0f; // Tracks how far the level has scrolled
        private List<ObstacleData> obstaclePositions = new List<ObstacleData>(); // Track spawned obstacles
        private int previousCollectibleLane = 0; // Track previous collectible lane for line bias

        // Coin train tracking (Subway Surfers style)
        private bool isInCoinTrain = false;
        private int coinTrainRemaining = 0;
        private int coinTrainLane = 0;

        // Recovery zone tracking (fairness after palisade minigames)
        private bool inRecoveryZone = false;
        private float recoveryZoneEndVirtualZ = 0f;
        private const float RECOVERY_ZONE_DURATION = 15f; // 15 units = ~1-1.5 seconds at normal speed

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
            if (currentConfig == null || player == null)
                return;

            // Update virtual distance based on scroll speed
            if (LevelScroller.Instance != null && GameManager.Instance?.CurrentState == GameState.Playing)
            {
                virtualDistance += LevelScroller.Instance.GetScrollSpeed() * Time.deltaTime;
            }

            SpawnFloor();
            SpawnObstacles();
            SpawnCollectibles();
            DespawnObjects();
        }

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

            // Reset virtual distance and spawn tracking
            virtualDistance = 0f;
            nextObstacleSpawnZ = 20f; // Spawn first obstacle 20 units ahead
            nextCollectibleSpawnZ = 15f;

            // Start floor at 0, so first tile spawns at world Z = 0
            nextFloorSpawnZ = 0f;
            obstaclesSpawned = 0;
            collectiblesSpawned = 0;
            obstaclePositions.Clear();
            previousCollectibleLane = 0; // Reset to center lane

            // Reset coin train state
            isInCoinTrain = false;
            coinTrainRemaining = 0;
            coinTrainLane = 0;

            // Reset recovery zone
            inRecoveryZone = false;
            recoveryZoneEndVirtualZ = 0f;

            Debug.Log($"Virtual distance reset to 0. Floor will start spawning from Virtual Z: {nextFloorSpawnZ}");
            Debug.Log($"With floorTileSpacing={floorTileSpacing}, floors should spawn at: 0, {floorTileSpacing}, {floorTileSpacing*2}, {floorTileSpacing*3}, etc.");
        }

        private void SpawnFloor()
        {
            // Keep spawning floor tiles ahead based on virtual distance
            int spawnAttempts = 0;
            int maxAttempts = 10; // Prevent infinite loops
            int floorsSpawnedThisFrame = 0;

            while (nextFloorSpawnZ < virtualDistance + spawnDistance && spawnAttempts < maxAttempts)
            {
                // Offset spawn position by half tile length so tiles are edge-to-edge
                // Note: Using floorTileLength for visual offset, floorTileSpacing for actual spacing
                float spawnZ = player.position.z + (nextFloorSpawnZ - virtualDistance) + (floorTileLength / 2f);
                Vector3 spawnPosition = new Vector3(0f, -0.5f, spawnZ);

                GameObject floorTile = ObjectPooler.Instance?.SpawnFromPool("Floor", spawnPosition, Quaternion.identity);

                if (floorTile != null)
                {
                    // Scale the floor tile to match the tile length (visual size)
                    floorTile.transform.localScale = new Vector3(floorTileLength, 1f, floorTileLength);

                    Debug.Log($"Floor spawned at World Z: {spawnZ:F2}, Virtual Z: {nextFloorSpawnZ:F2}, TileLength: {floorTileLength}, Spacing: {floorTileSpacing}, extends from {spawnZ - floorTileLength/2f:F2} to {spawnZ + floorTileLength/2f:F2}");

                    // Increment by spacing distance (not tile length) for next floor
                    nextFloorSpawnZ += floorTileSpacing;
                    spawnAttempts = 0;
                    floorsSpawnedThisFrame++;

                    Debug.Log($"Next floor will spawn at Virtual Z: {nextFloorSpawnZ:F2}");
                }
                else
                {
                    // Pool exhausted, stop trying
                    Debug.LogWarning($"Floor pool exhausted at spawn attempt {spawnAttempts}");
                    spawnAttempts++;
                }
            }

            if (floorsSpawnedThisFrame > 0 && Time.frameCount % 60 == 0)
            {
                Debug.Log($"Spawned {floorsSpawnedThisFrame} floor tiles. Virtual distance: {virtualDistance:F2}");
            }
        }

        private void SpawnObstacles()
        {
            // Check if we should stop spawning (last 3 seconds of level)
            if (LevelManager.Instance != null)
            {
                float levelDuration = currentConfig.LevelDuration;
                float currentTime = LevelManager.Instance.LevelProgress * levelDuration;

                if (currentTime >= levelDuration - 3f)
                {
                    return; // Stop spawning in last 3 seconds
                }
            }

            // FAIRNESS: Check if we're in a recovery zone (after palisade minigame)
            if (inRecoveryZone)
            {
                if (virtualDistance >= recoveryZoneEndVirtualZ)
                {
                    // Recovery zone ended
                    inRecoveryZone = false;
                    Debug.Log("Recovery zone ended, resuming obstacle spawning");
                }
                else
                {
                    // Still in recovery zone - don't spawn obstacles
                    return;
                }
            }

            if (virtualDistance + spawnDistance > nextObstacleSpawnZ)
            {
                // Decide between pattern-based and random generation using LevelConfig ratio
                bool usePattern = obstaclePatterns != null &&
                                  obstaclePatterns.Length > 0 &&
                                  Random.value < currentConfig.PatternUsageRatio;

                if (usePattern)
                {
                    // Try to spawn a pattern
                    ObstaclePattern selectedPattern = SelectRandomPattern(currentConfig);
                    if (selectedPattern != null && TrySpawnPattern(selectedPattern))
                    {
                        // Pattern spawned successfully
                        return;
                    }
                    // If pattern spawn failed, fall through to random generation
                    Debug.Log("Pattern spawn failed, using random generation as fallback");
                }

                // Random generation (original logic)
                // 40% chance to spawn a row of obstacles instead of a single obstacle
                if (Random.value < 0.4f)
                {
                    SpawnObstacleRow();
                    return;
                }

                // Randomly choose obstacle type (5 types: Avoid, Jump, Palisade, Pylon, Broad Jump)
                float randomValue = Random.value;
                string poolTag;

                if (randomValue < 0.2f)
                {
                    poolTag = "ObstacleAvoid";
                }
                else if (randomValue < 0.4f)
                {
                    poolTag = "ObstacleJump";
                }
                else if (randomValue < 0.6f)
                {
                    poolTag = "ObstaclePalisade";
                }
                else if (randomValue < 0.8f)
                {
                    poolTag = "ObstaclePylon";
                }
                else
                {
                    poolTag = "ObstacleBroadJump";
                }

                // Random lane
                int lane = Random.Range(-1, 2);

                // Apply clearance check to all obstacles: ensure no obstacles within 4 units behind in the same lane
                bool hasObstacleBehind = HasObstacleInLaneBehind(lane, nextObstacleSpawnZ, 4f);

                if (hasObstacleBehind)
                {
                    // Try to find a clear lane
                    int[] lanes = { -1, 0, 1 };
                    bool foundClearLane = false;

                    foreach (int testLane in lanes)
                    {
                        if (!HasObstacleInLaneBehind(testLane, nextObstacleSpawnZ, 4f))
                        {
                            lane = testLane;
                            foundClearLane = true;
                            break;
                        }
                    }

                    // If no clear lane, skip this spawn and try again later
                    if (!foundClearLane)
                    {
                        nextObstacleSpawnZ += Random.Range(currentConfig.MinObstacleSpacing, currentConfig.MaxObstacleSpacing);
                        return;
                    }
                }

                float xPosition = lane * 3f;

                // Spawn at player position + offset
                float spawnZ = player.position.z + (nextObstacleSpawnZ - virtualDistance);
                Vector3 spawnPosition = new Vector3(xPosition, 0f, spawnZ);

                Debug.Log($"Attempting to spawn {poolTag} at {spawnPosition}, virtual:{virtualDistance}, count: {obstaclesSpawned}");

                GameObject obstacle = ObjectPooler.Instance?.SpawnFromPool(poolTag, spawnPosition, Quaternion.identity);

                if (obstacle != null)
                {
                    obstaclesSpawned++;
                    // Track this obstacle's position, lane, and type
                    obstaclePositions.Add(new ObstacleData(nextObstacleSpawnZ, lane, poolTag));
                    nextObstacleSpawnZ += Random.Range(currentConfig.MinObstacleSpacing, currentConfig.MaxObstacleSpacing);
                    Debug.Log($"Successfully spawned {poolTag}. Next spawn at virtual: {nextObstacleSpawnZ}");
                }
                else
                {
                    // Pool exhausted - don't advance spawn position, will retry next frame
                    Debug.LogWarning($"Pool exhausted for {poolTag}, will retry next frame");
                }
            }
        }

        /// <summary>
        /// Select a random pattern that's valid for the current level
        /// Uses LevelConfig's min/max pattern difficulty settings
        /// </summary>
        private ObstaclePattern SelectRandomPattern(LevelConfig config)
        {
            if (obstaclePatterns == null || obstaclePatterns.Length == 0)
                return null;

            // Get current level number (1-9)
            int currentLevelNum = System.Array.IndexOf(levelConfigs, config) + 1;

            // Filter patterns valid for this level (by level range AND difficulty range)
            var validPatterns = new System.Collections.Generic.List<ObstaclePattern>();
            foreach (var pattern in obstaclePatterns)
            {
                if (pattern != null &&
                    pattern.IsValidForLevel(currentLevelNum) &&
                    pattern.difficultyRating >= config.MinPatternDifficulty &&
                    pattern.difficultyRating <= config.MaxPatternDifficulty)
                {
                    validPatterns.Add(pattern);
                }
            }

            if (validPatterns.Count == 0)
            {
                Debug.LogWarning($"No valid patterns found for level {currentLevelNum} (difficulty {config.MinPatternDifficulty}-{config.MaxPatternDifficulty})");
                return null;
            }

            // Select random pattern from valid ones
            return validPatterns[Random.Range(0, validPatterns.Count)];
        }

        /// <summary>
        /// Attempt to spawn an obstacle pattern
        /// Returns true if successful, false if failed (clearance issues, etc.)
        /// </summary>
        private bool TrySpawnPattern(ObstaclePattern pattern)
        {
            if (pattern == null || pattern.obstacles == null || pattern.obstacles.Length == 0)
                return false;

            // Validate pattern is solvable
            if (!pattern.IsSolvable())
            {
                Debug.LogWarning($"Pattern '{pattern.patternName}' is not solvable, skipping");
                return false;
            }

            // Check clearance for all obstacles in the pattern
            foreach (var obstacleDef in pattern.obstacles)
            {
                float obstacleZ = nextObstacleSpawnZ + obstacleDef.zOffset;

                // Check if this position has clearance issues
                if (HasObstacleInLaneBehind(obstacleDef.lane, obstacleZ, 4f))
                {
                    Debug.Log($"Pattern '{pattern.patternName}' failed clearance check at lane {obstacleDef.lane}, Z offset {obstacleDef.zOffset}");
                    return false;
                }
            }

            // All checks passed - spawn the pattern
            Debug.Log($"Spawning pattern: {pattern.patternName} (difficulty {pattern.difficultyRating})");

            foreach (var obstacleDef in pattern.obstacles)
            {
                float obstacleZ = nextObstacleSpawnZ + obstacleDef.zOffset;
                SpawnObstacleAtLane(obstacleDef.obstacleType, obstacleDef.lane, obstacleZ);
            }

            // Advance spawn position by pattern length
            nextObstacleSpawnZ += pattern.patternLength;

            return true;
        }

        /// <summary>
        /// Spawn a row of obstacles instead of a single obstacle
        /// </summary>
        private void SpawnObstacleRow()
        {
            // Decide between 2 obstacles in 2 lanes (50%) or 3 obstacles in 1 lane (50%)
            bool isTwoLaneRow = Random.value < 0.5f;

            if (isTwoLaneRow)
            {
                SpawnTwoLaneRow();
            }
            else
            {
                SpawnSingleLaneRow();
            }
        }

        /// <summary>
        /// Spawn 2 identical obstacles in 2 of the 3 lanes (at same Z position)
        /// FAIRNESS: Retries with single obstacle if clearance fails
        /// </summary>
        private void SpawnTwoLaneRow()
        {
            // Pick a random obstacle type
            string obstacleType = GetRandomObstacleType();

            // Pick 2 of the 3 lanes
            List<int> availableLanes = new List<int> { -1, 0, 1 };
            int lane1Index = Random.Range(0, 3);
            int lane1 = availableLanes[lane1Index];
            availableLanes.RemoveAt(lane1Index);
            int lane2 = availableLanes[Random.Range(0, 2)];

            // Check clearance for both lanes
            if (HasObstacleInLaneBehind(lane1, nextObstacleSpawnZ, 4f) ||
                HasObstacleInLaneBehind(lane2, nextObstacleSpawnZ, 4f))
            {
                // Clearance failed for row - try spawning a single obstacle instead
                Debug.Log("Two-lane row clearance failed, retrying with single obstacle");
                SpawnSingleObstacleWithRetry();
                return;
            }

            // Spawn in both lanes at the same Z position
            SpawnObstacleAtLane(obstacleType, lane1, nextObstacleSpawnZ);
            SpawnObstacleAtLane(obstacleType, lane2, nextObstacleSpawnZ);

            // Update next spawn position
            nextObstacleSpawnZ += Random.Range(currentConfig.MinObstacleSpacing, currentConfig.MaxObstacleSpacing);
        }

        /// <summary>
        /// Spawn 3 obstacles across all 3 lanes (at same Z position, at least 2 of same type)
        /// FAIRNESS GUARANTEE: Ensures at least 1 lane is passable
        /// FAIRNESS: Retries with two-lane or single obstacle if clearance fails
        /// </summary>
        private void SpawnSingleLaneRow()
        {
            // Check clearance for all 3 lanes
            if (HasObstacleInLaneBehind(-1, nextObstacleSpawnZ, 4f) ||
                HasObstacleInLaneBehind(0, nextObstacleSpawnZ, 4f) ||
                HasObstacleInLaneBehind(1, nextObstacleSpawnZ, 4f))
            {
                // Clearance failed for 3-lane row - try a simpler two-lane row instead
                Debug.Log("Three-lane row clearance failed, retrying with two-lane row");
                SpawnTwoLaneRow();
                return;
            }

            // Generate 3 obstacles with at least 2 being the same type
            string type1 = GetRandomObstacleType();
            string type2 = type1; // Ensure at least 2 are the same
            string type3;

            // 50% chance to make all 3 the same, 50% chance to have 1 different
            if (Random.value < 0.5f)
            {
                type3 = type1; // All same (AAA)
            }
            else
            {
                // One is different - pick a different type
                do {
                    type3 = GetRandomObstacleType();
                } while (type3 == type1);
            }

            // Assign types to lanes (can result in AAB, ABA, or BAA patterns across lanes)
            string[] types = { type1, type2, type3 };

            // FAIRNESS CHECK: Ensure at least one obstacle is passable (not all instant-death)
            if (!HasAtLeastOnePassableObstacle(types))
            {
                // Replace one obstacle with a passable type
                string[] passableTypes = { "ObstacleJump", "ObstaclePalisade", "ObstacleBroadJump" };
                types[Random.Range(0, 3)] = passableTypes[Random.Range(0, passableTypes.Length)];
                Debug.Log($"Prevented impossible 3-lane row! Replaced one instant-death obstacle with passable type.");
            }

            ShuffleArray(types);

            // Spawn all 3 at the same Z position, one in each lane
            int[] lanes = { -1, 0, 1 }; // Left, center, right
            for (int i = 0; i < 3; i++)
            {
                SpawnObstacleAtLane(types[i], lanes[i], nextObstacleSpawnZ);
            }

            // Update next spawn position
            nextObstacleSpawnZ += Random.Range(currentConfig.MinObstacleSpacing, currentConfig.MaxObstacleSpacing);
        }

        /// <summary>
        /// Get a random obstacle type based on uniform distribution
        /// </summary>
        private string GetRandomObstacleType()
        {
            float randomValue = Random.value;

            if (randomValue < 0.2f)
                return "ObstacleAvoid";
            else if (randomValue < 0.4f)
                return "ObstacleJump";
            else if (randomValue < 0.6f)
                return "ObstaclePalisade";
            else if (randomValue < 0.8f)
                return "ObstaclePylon";
            else
                return "ObstacleBroadJump";
        }

        /// <summary>
        /// Spawn a single obstacle with retry logic for clearance
        /// FAIRNESS: Only skips if all lanes are blocked (very rare)
        /// </summary>
        private void SpawnSingleObstacleWithRetry()
        {
            string poolTag = GetRandomObstacleType();
            int lane = Random.Range(-1, 2);

            // Try to find a clear lane
            if (HasObstacleInLaneBehind(lane, nextObstacleSpawnZ, 4f))
            {
                int[] lanes = { -1, 0, 1 };
                bool foundClearLane = false;

                foreach (int testLane in lanes)
                {
                    if (!HasObstacleInLaneBehind(testLane, nextObstacleSpawnZ, 4f))
                    {
                        lane = testLane;
                        foundClearLane = true;
                        break;
                    }
                }

                // Only skip if all lanes are blocked (very rare edge case)
                if (!foundClearLane)
                {
                    Debug.LogWarning("All lanes blocked, skipping spawn (rare edge case)");
                    nextObstacleSpawnZ += Random.Range(currentConfig.MinObstacleSpacing, currentConfig.MaxObstacleSpacing);
                    return;
                }
            }

            float xPosition = lane * 3f;
            float spawnZ = player.position.z + (nextObstacleSpawnZ - virtualDistance);
            Vector3 spawnPosition = new Vector3(xPosition, 0f, spawnZ);

            GameObject obstacle = ObjectPooler.Instance?.SpawnFromPool(poolTag, spawnPosition, Quaternion.identity);

            if (obstacle != null)
            {
                obstaclesSpawned++;
                obstaclePositions.Add(new ObstacleData(nextObstacleSpawnZ, lane, poolTag));
                nextObstacleSpawnZ += Random.Range(currentConfig.MinObstacleSpacing, currentConfig.MaxObstacleSpacing);
            }
            else
            {
                // Pool exhausted - don't advance spawn position, will retry next frame
                Debug.LogWarning($"Pool exhausted for {poolTag}, will retry next frame");
            }
        }

        /// <summary>
        /// Spawn a single obstacle at the specified lane and virtual Z position
        /// </summary>
        private void SpawnObstacleAtLane(string poolTag, int lane, float virtualZ)
        {
            float xPosition = lane * 3f;
            float spawnZ = player.position.z + (virtualZ - virtualDistance);
            Vector3 spawnPosition = new Vector3(xPosition, 0f, spawnZ);

            Debug.Log($"Attempting to spawn {poolTag} at lane {lane}, virtual Z: {virtualZ}");

            GameObject obstacle = ObjectPooler.Instance?.SpawnFromPool(poolTag, spawnPosition, Quaternion.identity);

            if (obstacle != null)
            {
                obstaclesSpawned++;
                obstaclePositions.Add(new ObstacleData(virtualZ, lane, poolTag));
                Debug.Log($"Successfully spawned {poolTag} at lane {lane}");
            }
            else
            {
                Debug.LogWarning($"Failed to spawn {poolTag} at lane {lane}!");
            }
        }

        /// <summary>
        /// Check if an obstacle type is passable (not instant death)
        /// </summary>
        private bool IsObstaclePassable(string obstacleType)
        {
            // Jump, Palisade, and BroadJump can be passed by player actions
            // Avoid and Pylon are instant death
            return obstacleType == "ObstacleJump" ||
                   obstacleType == "ObstaclePalisade" ||
                   obstacleType == "ObstacleBroadJump";
        }

        /// <summary>
        /// Validate that at least one obstacle in the array is passable
        /// This prevents impossible 3-lane rows (e.g., 3 avoid obstacles)
        /// </summary>
        private bool HasAtLeastOnePassableObstacle(string[] obstacleTypes)
        {
            foreach (string obstacleType in obstacleTypes)
            {
                if (IsObstaclePassable(obstacleType))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Shuffle an array using Fisher-Yates algorithm
        /// </summary>
        private void ShuffleArray(string[] array)
        {
            for (int i = array.Length - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                string temp = array[i];
                array[i] = array[j];
                array[j] = temp;
            }
        }

        /// <summary>
        /// Check if there are any obstacles in the specified lane behind the given Z position
        /// </summary>
        private bool HasObstacleInLaneBehind(int lane, float zPosition, float behindDistance)
        {
            foreach (ObstacleData obstacle in obstaclePositions)
            {
                // Check if obstacle is in the same lane
                if (obstacle.lane == lane)
                {
                    // Check if obstacle is within the distance behind this position
                    float distanceBehind = zPosition - obstacle.zPosition;
                    if (distanceBehind > 0 && distanceBehind <= behindDistance)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Check if there are any obstacles in the specified lane ahead of the given Z position
        /// FAIRNESS: Used to prevent coin trains from leading into obstacles
        /// </summary>
        private bool HasObstacleInLaneAhead(int lane, float zPosition, float aheadDistance)
        {
            foreach (ObstacleData obstacle in obstaclePositions)
            {
                // Check if obstacle is in the same lane
                if (obstacle.lane == lane)
                {
                    // Check if obstacle is within the distance ahead of this position
                    float distanceAhead = obstacle.zPosition - zPosition;
                    if (distanceAhead > 0 && distanceAhead <= aheadDistance)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void SpawnCollectibles()
        {
            // Check if we should stop spawning (last 3 seconds of level)
            if (LevelManager.Instance != null)
            {
                float levelDuration = currentConfig.LevelDuration;
                float currentTime = LevelManager.Instance.LevelProgress * levelDuration;

                if (currentTime >= levelDuration - 3f)
                {
                    return; // Stop spawning in last 3 seconds
                }
            }

            if (virtualDistance + spawnDistance > nextCollectibleSpawnZ)
            {
                // Check if this position is near any obstacle (within 3 units before or after)
                ObstacleData? nearbyObstacle = GetNearbyObstacle(nextCollectibleSpawnZ, 3f);

                float spawnHeight = 1f; // Default collectible height
                int lane = 0;

                // Determine lane based on coin train or obstacle proximity
                if (isInCoinTrain && coinTrainRemaining > 0)
                {
                    // Continue the coin train in the same lane
                    lane = coinTrainLane;

                    // FAIRNESS CHECK: Lookahead to prevent coin train leading into obstacle
                    // Check if there's an obstacle ahead in this lane within the coin train distance
                    float lookaheadDistance = 2.5f * coinTrainRemaining; // Estimate remaining train length
                    if (HasObstacleInLaneAhead(coinTrainLane, nextCollectibleSpawnZ, lookaheadDistance))
                    {
                        // Obstacle detected ahead - end coin train early for safety
                        Debug.Log($"Coin train lookahead detected obstacle in lane {coinTrainLane}, ending train early");
                        isInCoinTrain = false;
                        coinTrainRemaining = 0;

                        // Use biased lane selection instead
                        lane = GetNextCollectibleLane(previousCollectibleLane);
                    }
                    else
                    {
                        // Safe to continue train
                        coinTrainRemaining--;

                        if (coinTrainRemaining == 0)
                        {
                            isInCoinTrain = false;
                        }
                    }
                }
                else if (nearbyObstacle.HasValue)
                {
                    // Near an obstacle - check if we should spawn above it
                    if (nearbyObstacle.Value.CanHaveCollectibleAbove() && Random.value < currentConfig.CollectibleAboveObstacleChance)
                    {
                        // Spawn above the jump or palisade obstacle
                        // Use the same lane as the obstacle
                        lane = nearbyObstacle.Value.lane;

                        // Palisades are 2m tall, so spawn collectibles higher above them
                        if (nearbyObstacle.Value.obstacleType == "ObstaclePalisade")
                        {
                            spawnHeight = 3f; // Above 2m tall palisade
                        }
                        else
                        {
                            spawnHeight = 1.5f; // Above regular jump obstacles
                        }
                    }
                    else
                    {
                        // Near an obstacle but not spawning above it
                        // Spawn in a different lane than the obstacle
                        int[] otherLanes = GetLanesExcept(nearbyObstacle.Value.lane);
                        lane = otherLanes[Random.Range(0, otherLanes.Length)];

                        // Maybe start a coin train (40% chance)
                        if (!isInCoinTrain && Random.value < 0.4f)
                        {
                            StartCoinTrain(lane);
                        }
                    }
                }
                else
                {
                    // No nearby obstacle - decide whether to start a coin train
                    if (!isInCoinTrain && Random.value < 0.5f)
                    {
                        // Start a new coin train
                        lane = Random.Range(-1, 2);
                        StartCoinTrain(lane);
                    }
                    else
                    {
                        // Use biased lane selection (Subway Surfers style)
                        lane = GetNextCollectibleLane(previousCollectibleLane);
                    }
                }

                float xPosition = lane * 3f;

                // Spawn at player position + offset
                float spawnZ = player.position.z + (nextCollectibleSpawnZ - virtualDistance);
                Vector3 spawnPosition = new Vector3(xPosition, spawnHeight, spawnZ);

                // Randomly choose between regular and mega collectible
                bool isMega = Random.value < currentConfig.MegaCollectibleSpawnRatio;
                string poolTag = isMega ? "MegaCollectible" : "Collectible";

                GameObject collectible = ObjectPooler.Instance?.SpawnFromPool(poolTag, spawnPosition, Quaternion.identity);

                if (collectible != null)
                {
                    // If mega collectible, set its point value
                    if (isMega)
                    {
                        var collectibleComponent = collectible.GetComponent<Collectible>();
                        if (collectibleComponent != null)
                        {
                            collectibleComponent.SetPointValue(currentConfig.MegaCollectiblePointValue);
                        }
                    }

                    collectiblesSpawned++;
                    previousCollectibleLane = lane; // Remember this lane for next spawn

                    // Adjust spacing: tighter for coin trains, normal otherwise
                    float spacing;
                    if (isInCoinTrain && coinTrainRemaining > 0)
                    {
                        spacing = 2.5f; // Tight spacing for coin trains
                    }
                    else
                    {
                        spacing = Random.Range(currentConfig.MinCollectibleSpacing, currentConfig.MaxCollectibleSpacing);
                    }

                    nextCollectibleSpawnZ += spacing;
                }
            }
        }

        /// <summary>
        /// Start a new coin train with random length (3-10 coins)
        /// </summary>
        private void StartCoinTrain(int lane)
        {
            isInCoinTrain = true;
            coinTrainRemaining = Random.Range(3, 11); // 3 to 10 coins
            coinTrainLane = lane;
        }

        /// <summary>
        /// Get array of lanes excluding the specified lane
        /// </summary>
        private int[] GetLanesExcept(int excludeLane)
        {
            List<int> lanes = new List<int>();
            for (int i = -1; i <= 1; i++)
            {
                if (i != excludeLane)
                {
                    lanes.Add(i);
                }
            }
            return lanes.ToArray();
        }

        /// <summary>
        /// Check if a collectible spawn position is within the specified distance of any obstacle
        /// Returns the obstacle data if close, null otherwise
        /// </summary>
        private ObstacleData? GetNearbyObstacle(float zPosition, float maxDistance = -1f)
        {
            // Use config distance if not specified
            if (maxDistance < 0)
            {
                maxDistance = currentConfig.MinCollectibleObstacleDistance;
            }

            foreach (ObstacleData obstacle in obstaclePositions)
            {
                if (Mathf.Abs(zPosition - obstacle.zPosition) < maxDistance)
                {
                    return obstacle;
                }
            }

            return null;
        }

        /// <summary>
        /// Get the next collectible lane with bias towards staying in the same lane
        /// </summary>
        private int GetNextCollectibleLane(int previousLane)
        {
            // Apply lane bias - higher chance to stay in same lane (Subway Surfers pattern)
            if (Random.value < currentConfig.CollectibleLineBias)
            {
                return previousLane;
            }

            // Otherwise, randomly pick a different lane
            int newLane;
            do
            {
                newLane = Random.Range(-1, 2); // -1, 0, or 1
            } while (newLane == previousLane);

            return newLane;
        }

        private void DespawnObjects()
        {
            if (ObjectPooler.Instance == null || player == null)
                return;

            // FAIRNESS: More aggressive cleanup for obstacle tracking data
            // Remove obstacles that are far behind the player (more aggressive than before)
            float cleanupThreshold = virtualDistance - 10f; // Reduced from 20f for tighter memory usage
            int removedCount = obstaclePositions.RemoveAll(obstacle => obstacle.zPosition < cleanupThreshold);

            // Periodic deep cleanup every 100 virtual units to prevent unbounded growth
            if (virtualDistance % 100f < 1f && obstaclePositions.Count > 50)
            {
                // Keep only obstacles within 30 units of current position
                obstaclePositions.RemoveAll(obstacle => obstacle.zPosition < virtualDistance - 30f);
                Debug.Log($"Deep cleanup performed: obstacle list size = {obstaclePositions.Count}");
            }

            // Find all active pooled objects behind the player and return them
            GameObject[] allObjects = GameObject.FindGameObjectsWithTag("Obstacle");
            foreach (GameObject obj in allObjects)
            {
                if (obj.transform.position.z < player.position.z + despawnDistance)
                {
                    ObjectPooler.Instance.ReturnToPool(obj);
                }
            }

            GameObject[] collectibles = GameObject.FindGameObjectsWithTag("Collectible");
            foreach (GameObject obj in collectibles)
            {
                if (obj.transform.position.z < player.position.z + despawnDistance)
                {
                    ObjectPooler.Instance.ReturnToPool(obj);
                }
            }

            GameObject[] floorTiles = GameObject.FindGameObjectsWithTag("Floor");
            foreach (GameObject obj in floorTiles)
            {
                if (obj.transform.position.z < player.position.z + despawnDistance)
                {
                    ObjectPooler.Instance.ReturnToPool(obj);
                }
            }
        }

        public void SetPlayer(Transform playerTransform)
        {
            player = playerTransform;
        }

        public LevelConfig GetCurrentConfig()
        {
            return currentConfig;
        }

        /// <summary>
        /// Called when a palisade minigame is completed
        /// Creates a recovery zone (no obstacles) for fairness
        /// </summary>
        public void OnPalisadeCompleted()
        {
            if (!inRecoveryZone)
            {
                inRecoveryZone = true;
                recoveryZoneEndVirtualZ = virtualDistance + RECOVERY_ZONE_DURATION;
                Debug.Log($"Palisade completed! Recovery zone active until virtual Z: {recoveryZoneEndVirtualZ:F2}");
            }
        }

        /// <summary>
        /// Called when level is ending - despawns all obstacles for fairness
        /// FAIRNESS: Prevents unfair hits from obstacles that spawned earlier
        /// </summary>
        public void OnLevelEnding()
        {
            if (ObjectPooler.Instance == null)
                return;

            Debug.Log("Level ending - despawning all obstacles");

            // Despawn all active obstacles
            GameObject[] allObjects = GameObject.FindGameObjectsWithTag("Obstacle");
            foreach (GameObject obj in allObjects)
            {
                ObjectPooler.Instance.ReturnToPool(obj);
            }

            // Clear tracking data
            obstaclePositions.Clear();
        }

        /// <summary>
        /// Get current virtual distance (for external systems to check)
        /// </summary>
        public float GetVirtualDistance()
        {
            return virtualDistance;
        }
    }
}
