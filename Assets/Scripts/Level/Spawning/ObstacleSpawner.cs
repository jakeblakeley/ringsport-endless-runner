using UnityEngine;
using System.Collections.Generic;
using RingSport.Core;

namespace RingSport.Level.Spawning
{
    /// <summary>
    /// Handles obstacle spawning logic including patterns, rows, and clearance validation
    /// </summary>
    public class ObstacleSpawner
    {
        private float nextObstacleSpawnZ;
        private int obstaclesSpawned;

        private SpawnContext context;
        private ObstacleTracker obstacleTracker;
        private RecoveryZoneManager recoveryZoneManager;
        private DespawnManager despawnManager;

        private ObstaclePattern[] obstaclePatterns;
        private LevelConfig[] levelConfigs;

        public ObstacleSpawner(
            SpawnContext context,
            ObstacleTracker obstacleTracker,
            RecoveryZoneManager recoveryZoneManager,
            DespawnManager despawnManager,
            ObstaclePattern[] obstaclePatterns,
            LevelConfig[] levelConfigs)
        {
            this.context = context;
            this.obstacleTracker = obstacleTracker;
            this.recoveryZoneManager = recoveryZoneManager;
            this.despawnManager = despawnManager;
            this.obstaclePatterns = obstaclePatterns;
            this.levelConfigs = levelConfigs;
        }

        /// <summary>
        /// Initialize obstacle spawning for a new level
        /// </summary>
        public void Initialize()
        {
            nextObstacleSpawnZ = 20f; // Spawn first obstacle 20 units ahead
            obstaclesSpawned = 0;
        }

        /// <summary>
        /// Attempt to spawn obstacles
        /// </summary>
        public void SpawnObstacles()
        {
            // Check if we should stop spawning (last 3 seconds of level)
            if (LevelManager.Instance != null)
            {
                float levelDuration = context.CurrentConfig.LevelDuration;
                float currentTime = LevelManager.Instance.LevelProgress * levelDuration;

                if (currentTime >= levelDuration - 3f)
                {
                    return; // Stop spawning in last 3 seconds
                }
            }

            // FAIRNESS: Check if we're in a recovery zone (after palisade minigame)
            if (recoveryZoneManager.IsInRecoveryZone(context.VirtualDistance))
            {
                // Still in recovery zone - don't spawn obstacles
                return;
            }

            if (context.VirtualDistance + context.SpawnDistance > nextObstacleSpawnZ)
            {
                // Decide between pattern-based and random generation using LevelConfig ratio
                bool usePattern = obstaclePatterns != null &&
                                  obstaclePatterns.Length > 0 &&
                                  Random.value < context.CurrentConfig.PatternUsageRatio;

                if (usePattern)
                {
                    // Try to spawn a pattern
                    ObstaclePattern selectedPattern = SelectRandomPattern(context.CurrentConfig);
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

                // Spawn single random obstacle
                SpawnRandomSingleObstacle();
            }
        }

        /// <summary>
        /// Spawns a single random obstacle with clearance checking
        /// </summary>
        private void SpawnRandomSingleObstacle()
        {
            // Select obstacle type
            string poolTag = GetRandomObstacleType();

            // Select initial random lane
            int lane = Random.Range(-1, 2);

            // Try to find a clear lane if the random one is blocked
            if (!TryFindClearLane(ref lane))
            {
                // No clear lane available, skip this spawn and try again later
                nextObstacleSpawnZ += Random.Range(context.CurrentConfig.MinObstacleSpacing, context.CurrentConfig.MaxObstacleSpacing);
                return;
            }

            // Spawn the obstacle
            SpawnSingleObstacleAtPosition(poolTag, lane);
        }

        /// <summary>
        /// Tries to find a clear lane for obstacle spawning
        /// Returns true if a clear lane was found, false otherwise
        /// </summary>
        private bool TryFindClearLane(ref int lane)
        {
            // Check if current lane has clearance
            if (!obstacleTracker.HasObstacleInLaneBehind(lane, nextObstacleSpawnZ, 4f))
            {
                return true; // Current lane is clear
            }

            // Try to find a clear lane
            int[] lanes = { -1, 0, 1 };
            foreach (int testLane in lanes)
            {
                if (!obstacleTracker.HasObstacleInLaneBehind(testLane, nextObstacleSpawnZ, 4f))
                {
                    lane = testLane;
                    return true;
                }
            }

            // No clear lane found
            return false;
        }

        /// <summary>
        /// Spawns a single obstacle at the specified lane
        /// </summary>
        private void SpawnSingleObstacleAtPosition(string poolTag, int lane)
        {
            float xPosition = lane * 3f;

            // Anchor to world origin (0,0,0) for grid alignment
            float spawnZ = nextObstacleSpawnZ - context.VirtualDistance;
            Vector3 spawnPosition = new Vector3(xPosition, 0f, spawnZ);

            Debug.Log($"Attempting to spawn {poolTag} at {spawnPosition}, virtual:{context.VirtualDistance}, count: {obstaclesSpawned}");

            GameObject obstacle = ObjectPooler.Instance?.SpawnFromPool(poolTag, spawnPosition, Quaternion.identity);

            if (obstacle != null)
            {
                obstaclesSpawned++;
                // Track this obstacle's position, lane, and type
                obstacleTracker.AddObstacle(new ObstacleData(nextObstacleSpawnZ, lane, poolTag));
                despawnManager.RegisterObstacle(obstacle);
                nextObstacleSpawnZ += Random.Range(context.CurrentConfig.MinObstacleSpacing, context.CurrentConfig.MaxObstacleSpacing);
                Debug.Log($"Successfully spawned {poolTag}. Next spawn at virtual: {nextObstacleSpawnZ}");
            }
            else
            {
                // Pool exhausted - don't advance spawn position, will retry next frame
                Debug.LogWarning($"Pool exhausted for {poolTag}, will retry next frame");
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
                if (obstacleTracker.HasObstacleInLaneBehind(obstacleDef.lane, obstacleZ, 4f))
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
            if (obstacleTracker.HasObstacleInLaneBehind(lane1, nextObstacleSpawnZ, 4f) ||
                obstacleTracker.HasObstacleInLaneBehind(lane2, nextObstacleSpawnZ, 4f))
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
            nextObstacleSpawnZ += Random.Range(context.CurrentConfig.MinObstacleSpacing, context.CurrentConfig.MaxObstacleSpacing);
        }

        /// <summary>
        /// Spawn 3 obstacles across all 3 lanes (at same Z position, at least 2 of same type)
        /// FAIRNESS GUARANTEE: Ensures at least 1 lane is passable
        /// FAIRNESS: Retries with two-lane or single obstacle if clearance fails
        /// </summary>
        private void SpawnSingleLaneRow()
        {
            // Check clearance for all 3 lanes
            if (obstacleTracker.HasObstacleInLaneBehind(-1, nextObstacleSpawnZ, 4f) ||
                obstacleTracker.HasObstacleInLaneBehind(0, nextObstacleSpawnZ, 4f) ||
                obstacleTracker.HasObstacleInLaneBehind(1, nextObstacleSpawnZ, 4f))
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
                string[] passableTypes = { PoolTags.ObstacleJump, PoolTags.ObstaclePalisade, PoolTags.ObstacleBroadJump };
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
            nextObstacleSpawnZ += Random.Range(context.CurrentConfig.MinObstacleSpacing, context.CurrentConfig.MaxObstacleSpacing);
        }

        /// <summary>
        /// Get a random obstacle type based on uniform distribution
        /// </summary>
        private string GetRandomObstacleType()
        {
            float randomValue = Random.value;

            if (randomValue < 0.2f)
                return PoolTags.ObstacleAvoid;
            else if (randomValue < 0.4f)
                return PoolTags.ObstacleJump;
            else if (randomValue < 0.6f)
                return PoolTags.ObstaclePalisade;
            else if (randomValue < 0.8f)
                return PoolTags.ObstaclePylon;
            else
                return PoolTags.ObstacleBroadJump;
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
            if (obstacleTracker.HasObstacleInLaneBehind(lane, nextObstacleSpawnZ, 4f))
            {
                int[] lanes = { -1, 0, 1 };
                bool foundClearLane = false;

                foreach (int testLane in lanes)
                {
                    if (!obstacleTracker.HasObstacleInLaneBehind(testLane, nextObstacleSpawnZ, 4f))
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
                    nextObstacleSpawnZ += Random.Range(context.CurrentConfig.MinObstacleSpacing, context.CurrentConfig.MaxObstacleSpacing);
                    return;
                }
            }

            float xPosition = lane * 3f;
            // Anchor to world origin (0,0,0) for grid alignment
            float spawnZ = nextObstacleSpawnZ - context.VirtualDistance;
            Vector3 spawnPosition = new Vector3(xPosition, 0f, spawnZ);

            GameObject obstacle = ObjectPooler.Instance?.SpawnFromPool(poolTag, spawnPosition, Quaternion.identity);

            if (obstacle != null)
            {
                obstaclesSpawned++;
                obstacleTracker.AddObstacle(new ObstacleData(nextObstacleSpawnZ, lane, poolTag));
                despawnManager.RegisterObstacle(obstacle);
                nextObstacleSpawnZ += Random.Range(context.CurrentConfig.MinObstacleSpacing, context.CurrentConfig.MaxObstacleSpacing);
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
            // Anchor to world origin (0,0,0) for grid alignment
            float spawnZ = virtualZ - context.VirtualDistance;
            Vector3 spawnPosition = new Vector3(xPosition, 0f, spawnZ);

            Debug.Log($"Attempting to spawn {poolTag} at lane {lane}, virtual Z: {virtualZ}");

            GameObject obstacle = ObjectPooler.Instance?.SpawnFromPool(poolTag, spawnPosition, Quaternion.identity);

            if (obstacle != null)
            {
                obstaclesSpawned++;
                obstacleTracker.AddObstacle(new ObstacleData(virtualZ, lane, poolTag));
                despawnManager.RegisterObstacle(obstacle);
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
            return obstacleType == PoolTags.ObstacleJump ||
                   obstacleType == PoolTags.ObstaclePalisade ||
                   obstacleType == PoolTags.ObstacleBroadJump;
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
        /// Get the next obstacle spawn Z position (for debugging)
        /// </summary>
        public float GetNextObstacleSpawnZ() => nextObstacleSpawnZ;

        /// <summary>
        /// Get the number of obstacles spawned (for debugging)
        /// </summary>
        public int GetObstaclesSpawned() => obstaclesSpawned;
    }
}
