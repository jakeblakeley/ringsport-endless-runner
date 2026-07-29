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
        // Must match PlayerController.forwardSpeed (Player.prefab); the run
        // speed at which fairness distances are converted from seconds to units
        private const float BasePlayerForwardSpeed = 10f;

        // Fairness action model (validated against the swipe-input audit):
        // gestures assume up to ~400ms user delay; jump air time is ~0.42s with
        // a 0.2s landing buffer in PlayerController.
        private const float SameLaneActionTime = 0.45f;   // re-jump in same lane (buffered)
        private const float ForcingRowLeadTime = 0.9f;    // reposition + jump before a row
        private const float PatternTailTime = 0.55f;      // breathing room after a pattern
        private const float PinChainTime = 1.4f;          // window where forced lanes must stay adjacent
        // A player who ENGAGES a palisade (the designed path) stops for the
        // minigame, rides the scripted vault, and regains control in the
        // palisade's lane - nothing may need an action for this long after it
        private const float PalisadeRecoveryTime = 1.1f;

        private float nextObstacleSpawnZ;
        private int obstaclesSpawned;

        // Fairness rule state
        private float lastObstacleZ;
        private float lastForcingRowZ;
        private float lastPalisadeZ;
        private int? pinnedLane;      // lane the player was last forced into
        private float pinnedLaneZ;

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
            lastObstacleZ = -100f;
            lastForcingRowZ = -100f;
            lastPalisadeZ = -100f;
            pinnedLane = null;
            pinnedLaneZ = -100f;
        }

        /// <summary>Base (non-sprint) scroll speed for the current level, u/s.</summary>
        private float RunSpeed
        {
            get
            {
                var config = context.CurrentConfig;
                if (config == null)
                    return BasePlayerForwardSpeed;
                return Mathf.Min(BasePlayerForwardSpeed * config.SpeedMultiplier, config.MaxEffectiveSpeed);
            }
        }

        // FAIRNESS: consecutive obstacles in the SAME lane must be far enough
        // apart to land and re-jump; converts the action time to units at the
        // level's speed (faster levels need more room, not less).
        private float SameLaneClearance => Mathf.Max(4f, SameLaneActionTime * RunSpeed);

        private float ForcingRowGap => ForcingRowLeadTime * RunSpeed;
        private float PatternTailGap => PatternTailTime * RunSpeed;
        private float PinChainReach => PinChainTime * RunSpeed;
        private float PalisadeRecoveryGap => PalisadeRecoveryTime * RunSpeed;

        /// <summary>
        /// Earliest Z at which anything may spawn: keeps reposition room after
        /// forcing rows and full recovery room after every palisade.
        /// </summary>
        private float SpawnFloor => Mathf.Max(lastForcingRowZ + ForcingRowGap, lastPalisadeZ + PalisadeRecoveryGap);

        /// <summary>
        /// A spawned palisade anchors both the recovery gap and the pin: the
        /// player who takes it comes out of the vault in ITS lane.
        /// </summary>
        private void NotePalisade(int lane, float virtualZ)
        {
            lastPalisadeZ = Mathf.Max(lastPalisadeZ, virtualZ);
            pinnedLane = lane;
            pinnedLaneZ = Mathf.Max(pinnedLaneZ, virtualZ);
        }

        private bool PinActive(float atZ) => pinnedLane.HasValue && atZ - pinnedLaneZ < PinChainReach;

        /// <summary>A lane adjacent to the given one (random side where both exist).</summary>
        private static int AdjacentLane(int lane)
        {
            if (lane == 0)
                return Random.value < 0.5f ? -1 : 1;
            return 0;
        }

        /// <summary>Lane within +-1 of the pinned lane, chosen at random.</summary>
        private int RandomLaneNearPin()
        {
            int pin = pinnedLane.Value;
            int min = Mathf.Max(-1, pin - 1);
            int max = Mathf.Min(1, pin + 1);
            return Random.Range(min, max + 1);
        }

        /// <summary>
        /// Bookkeeping shared by every single-obstacle spawn: an obstacle near
        /// an active pin blocks the crossing corridor (so the pin persists from
        /// here), and a lethal single ON the pinned lane pushes the player to
        /// an adjacent lane.
        /// </summary>
        private void OnSingleSpawned(string poolTag, int lane, float virtualZ)
        {
            lastObstacleZ = Mathf.Max(lastObstacleZ, virtualZ);
            if (poolTag == PoolTags.ObstaclePalisade)
                NotePalisade(lane, virtualZ);
            if (PinActive(virtualZ))
            {
                if (lane == pinnedLane.Value && !IsObstaclePassable(poolTag))
                    pinnedLane = AdjacentLane(pinnedLane.Value);
                pinnedLaneZ = virtualZ;
            }
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
            // FAIRNESS: keep breathing room after forcing rows and palisades
            nextObstacleSpawnZ = Mathf.Max(nextObstacleSpawnZ, SpawnFloor);

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
            if (!obstacleTracker.HasObstacleInLaneBehind(lane, nextObstacleSpawnZ, SameLaneClearance))
            {
                return true; // Current lane is clear
            }

            // Try to find a clear lane
            int[] lanes = { -1, 0, 1 };
            foreach (int testLane in lanes)
            {
                if (!obstacleTracker.HasObstacleInLaneBehind(testLane, nextObstacleSpawnZ, SameLaneClearance))
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
                OnSingleSpawned(poolTag, lane, nextObstacleSpawnZ);
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

            // FAIRNESS: if the pattern contains a forcing row (2+ obstacles at
            // one Z), the player needs reposition-and-act room between the last
            // spawned obstacle and that row - shift the whole pattern out
            float startZ = Mathf.Max(nextObstacleSpawnZ, SpawnFloor);
            float firstForcingOffset = FirstForcingRowOffset(pattern);
            if (firstForcingOffset >= 0f)
                startZ = Mathf.Max(startZ, lastObstacleZ + ForcingRowGap - firstForcingOffset);

            // Check clearance for all obstacles in the pattern
            foreach (var obstacleDef in pattern.obstacles)
            {
                float obstacleZ = startZ + obstacleDef.zOffset;

                // Check if this position has clearance issues
                if (obstacleTracker.HasObstacleInLaneBehind(obstacleDef.lane, obstacleZ, SameLaneClearance))
                {
                    Debug.Log($"Pattern '{pattern.patternName}' failed clearance check at lane {obstacleDef.lane}, Z offset {obstacleDef.zOffset}");
                    return false;
                }
            }

            // All checks passed - spawn the pattern
            Debug.Log($"Spawning pattern: {pattern.patternName} (difficulty {pattern.difficultyRating})");

            float tailOffset = 0f;
            foreach (var obstacleDef in pattern.obstacles)
            {
                float obstacleZ = startZ + obstacleDef.zOffset;
                SpawnObstacleAtLane(obstacleDef.obstacleType, obstacleDef.lane, obstacleZ);
                tailOffset = Mathf.Max(tailOffset, obstacleDef.zOffset);
            }

            UpdatePinFromPattern(pattern, startZ);

            // Advance by pattern length, but always leave breathing room after
            // the pattern's LAST obstacle (patternLength alone can end 6u short)
            nextObstacleSpawnZ = Mathf.Max(startZ + pattern.patternLength, startZ + tailOffset + PatternTailGap);

            return true;
        }

        /// <summary>
        /// Z offset of the pattern's first row with 2+ obstacles (a row that
        /// forces the player's lane and usually an action), or -1 if none.
        /// </summary>
        private static float FirstForcingRowOffset(ObstaclePattern pattern)
        {
            float best = -1f;
            foreach (var a in pattern.obstacles)
            {
                int count = 0;
                foreach (var b in pattern.obstacles)
                {
                    if (Mathf.Abs(a.zOffset - b.zOffset) < 0.01f)
                        count++;
                }
                if (count >= 2 && (best < 0f || a.zOffset < best))
                    best = a.zOffset;
            }
            return best;
        }

        /// <summary>
        /// After spawning a pattern, updates the pinned lane from its forcing
        /// rows (free lane first, else a passable lane), and keeps the pin
        /// anchored at the pattern tail: obstacles after the pin block the
        /// crossing corridor, so the escape clock starts at the tail.
        /// </summary>
        private void UpdatePinFromPattern(ObstaclePattern pattern, float startZ)
        {
            var rows = new SortedDictionary<float, List<ObstacleDefinition>>();
            float tail = 0f;
            foreach (var def in pattern.obstacles)
            {
                float key = Mathf.Round(def.zOffset * 100f) / 100f;
                if (!rows.TryGetValue(key, out var list))
                    rows[key] = list = new List<ObstacleDefinition>();
                list.Add(def);
                tail = Mathf.Max(tail, def.zOffset);
            }

            foreach (var kv in rows)
            {
                if (kv.Value.Count < 2)
                    continue;

                lastForcingRowZ = Mathf.Max(lastForcingRowZ, startZ + kv.Key);

                var occupied = new HashSet<int>();
                foreach (var def in kv.Value)
                    occupied.Add(def.lane);

                int newPin = int.MinValue;
                foreach (int lane in new[] { -1, 0, 1 })
                {
                    if (!occupied.Contains(lane))
                    {
                        newPin = lane;
                        break;
                    }
                }
                if (newPin == int.MinValue)
                {
                    foreach (var def in kv.Value)
                    {
                        if (IsObstaclePassable(def.obstacleType))
                        {
                            newPin = def.lane;
                            break;
                        }
                    }
                }
                if (newPin != int.MinValue)
                {
                    pinnedLane = newPin;
                    pinnedLaneZ = startZ + kv.Key;
                }
            }

            if (pinnedLane.HasValue && startZ + tail - pinnedLaneZ < PinChainReach)
                pinnedLaneZ = Mathf.Max(pinnedLaneZ, startZ + tail);
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
            // FAIRNESS: a 2-lane row pins the player into the one free lane -
            // require reposition-and-act room after whatever came before
            nextObstacleSpawnZ = Mathf.Max(nextObstacleSpawnZ, lastObstacleZ + ForcingRowGap, SpawnFloor);

            // Pick a random obstacle type
            string obstacleType = GetRandomObstacleType();

            // Pick 2 of the 3 lanes
            List<int> availableLanes = new List<int> { -1, 0, 1 };
            int lane1Index = Random.Range(0, 3);
            int lane1 = availableLanes[lane1Index];
            availableLanes.RemoveAt(lane1Index);
            int lane2 = availableLanes[Random.Range(0, 2)];
            int freeLane = -(lane1 + lane2); // lanes sum to 0

            // FAIRNESS: while a pin chain is active, the free lane must stay
            // adjacent to the lane the player was last forced into
            if (PinActive(nextObstacleSpawnZ) && Mathf.Abs(freeLane - pinnedLane.Value) > 1)
            {
                freeLane = RandomLaneNearPin();
                lane1 = int.MinValue; // reassign below
                foreach (int l in new[] { -1, 0, 1 })
                {
                    if (l == freeLane)
                        continue;
                    if (lane1 == int.MinValue)
                        lane1 = l;
                    else
                        lane2 = l;
                }
            }

            // Check clearance for both lanes
            if (obstacleTracker.HasObstacleInLaneBehind(lane1, nextObstacleSpawnZ, SameLaneClearance) ||
                obstacleTracker.HasObstacleInLaneBehind(lane2, nextObstacleSpawnZ, SameLaneClearance))
            {
                // Clearance failed for row - try spawning a single obstacle instead
                Debug.Log("Two-lane row clearance failed, retrying with single obstacle");
                SpawnSingleObstacleWithRetry();
                return;
            }

            // Spawn in both lanes at the same Z position
            SpawnObstacleAtLane(obstacleType, lane1, nextObstacleSpawnZ);
            SpawnObstacleAtLane(obstacleType, lane2, nextObstacleSpawnZ);

            pinnedLane = freeLane;
            pinnedLaneZ = nextObstacleSpawnZ;
            lastForcingRowZ = Mathf.Max(lastForcingRowZ, nextObstacleSpawnZ);

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
            // FAIRNESS: a full row always forces an action - require
            // reposition-and-act room after whatever came before
            nextObstacleSpawnZ = Mathf.Max(nextObstacleSpawnZ, lastObstacleZ + ForcingRowGap, SpawnFloor);

            // Check clearance for all 3 lanes
            if (obstacleTracker.HasObstacleInLaneBehind(-1, nextObstacleSpawnZ, SameLaneClearance) ||
                obstacleTracker.HasObstacleInLaneBehind(0, nextObstacleSpawnZ, SameLaneClearance) ||
                obstacleTracker.HasObstacleInLaneBehind(1, nextObstacleSpawnZ, SameLaneClearance))
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

            int[] lanes = { -1, 0, 1 }; // Left, center, right

            // FAIRNESS: while a pin chain is active, the row's passable lane
            // must be reachable (within one lane) from the last forced lane
            if (PinActive(nextObstacleSpawnZ))
            {
                bool passableNearPin = false;
                for (int i = 0; i < 3; i++)
                {
                    if (IsObstaclePassable(types[i]) && Mathf.Abs(lanes[i] - pinnedLane.Value) <= 1)
                        passableNearPin = true;
                }

                if (!passableNearPin)
                {
                    int passableIndex = System.Array.FindIndex(types, IsObstaclePassable);
                    int targetIndex = RandomLaneNearPin() + 1; // lane -1/0/1 -> index 0/1/2
                    (types[passableIndex], types[targetIndex]) = (types[targetIndex], types[passableIndex]);
                }
            }

            // Spawn all 3 at the same Z position, one in each lane
            for (int i = 0; i < 3; i++)
            {
                SpawnObstacleAtLane(types[i], lanes[i], nextObstacleSpawnZ);
            }

            // The player is now forced into (one of) the passable lane(s):
            // remember the one nearest the previous pin
            int newPin = int.MinValue;
            for (int i = 0; i < 3; i++)
            {
                if (!IsObstaclePassable(types[i]))
                    continue;
                if (newPin == int.MinValue ||
                    (pinnedLane.HasValue && Mathf.Abs(lanes[i] - pinnedLane.Value) < Mathf.Abs(newPin - pinnedLane.Value)))
                    newPin = lanes[i];
            }
            if (newPin != int.MinValue)
            {
                pinnedLane = newPin;
                pinnedLaneZ = nextObstacleSpawnZ;
            }
            lastForcingRowZ = Mathf.Max(lastForcingRowZ, nextObstacleSpawnZ);

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
            // FAIRNESS: keep breathing room after forcing rows and palisades
            nextObstacleSpawnZ = Mathf.Max(nextObstacleSpawnZ, SpawnFloor);

            string poolTag = GetRandomObstacleType();
            int lane = Random.Range(-1, 2);

            // Try to find a clear lane
            if (obstacleTracker.HasObstacleInLaneBehind(lane, nextObstacleSpawnZ, SameLaneClearance))
            {
                int[] lanes = { -1, 0, 1 };
                bool foundClearLane = false;

                foreach (int testLane in lanes)
                {
                    if (!obstacleTracker.HasObstacleInLaneBehind(testLane, nextObstacleSpawnZ, SameLaneClearance))
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
                OnSingleSpawned(poolTag, lane, nextObstacleSpawnZ);
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
                lastObstacleZ = Mathf.Max(lastObstacleZ, virtualZ);
                if (poolTag == PoolTags.ObstaclePalisade)
                    NotePalisade(lane, virtualZ);
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
