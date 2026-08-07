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

        // Fairness action model. Every distance here is a TIME budget converted
        // to units at the level's run speed, so a faster level gets a longer
        // gap rather than a shorter reaction window.
        //
        // ReactionTime is the whole swipe input path - read the obstacle, flick,
        // gesture recognition - and dominates every budget below. Each action
        // time is that reaction plus the mechanical cost of the action itself.
        private const float ReactionTime = 0.6f;

        // Mechanical cost of each gesture once it lands. One swipe moves ONE
        // lane (PlayerController), so crossing two lanes is two gestures, and
        // consecutive gestures have to wait out its 0.2s input cooldown.
        private const float LaneChangeMechanic = 0.30f;  // lerp clear of the old lane
        private const float JumpMechanic = 0.05f;        // takeoff, landing-buffered
        private const float GestureCooldown = 0.2f;      // PlayerController.inputCooldown
        // The scripted leap off the top of a palisade. Control is off for its
        // whole length and the world scrolls at full speed underneath.
        private const float VaultTime = 0.2f;            // PlayerController.AnimateOverObstacle

        private static readonly float SameLaneActionTime = GestureChainTime(0, true);    // 0.65 jump where they stand
        private static readonly float LaneChangeActionTime = GestureChainTime(1, false); // 0.90 step one lane over
        private static readonly float ForcingRowLeadTime = GestureChainTime(1, true);    // 1.15 reposition, then jump
        // A pattern's tail hands off to whatever spawns next, in a lane nobody
        // has picked yet, so it has to buy the worst case: a lane change
        private static readonly float PatternTailTime = LaneChangeActionTime;            // 0.90
        // Below this gap a two-lane crossing is not affordable, so consecutive
        // forced lanes have to stay adjacent
        private static readonly float PinChainTime = GestureChainTime(2, true);          // 1.65
        // A hurdle is answered by JUMPING it - that is the whole read of the
        // shape - and the player who does that is committed to its lane from
        // takeoff to touchdown. An instant-death obstacle behind it in the same
        // lane is the one same-lane case the land-and-rejump distance
        // under-prices: a barrel can be hopped, but its window is tight and a
        // graze kills, so the honest answer is a dodge, and the gap has to buy
        // the jump plus a lane change out. Same arithmetic as a forcing row's
        // lead, read in the other order: act, then reposition.
        private static readonly float DodgeAfterJumpTime = GestureChainTime(1, true);    // 1.15
        // A palisade is the only obstacle that takes the player's EYES off the
        // road: the tap minigame owns the screen, and control comes back only
        // once the vault has already carried them past the wall. Two budgets
        // follow from that, and both are longer than any ordinary gap.
        //
        // Every lane pays the vault plus reposition-and-act room. The player
        // could read the rest of the course while they ran up to the wall, but
        // they get it back a blind vault later than they last looked.
        private static readonly float PalisadeRecoveryTime = VaultTime + ForcingRowLeadTime;   // 1.35
        // The palisade's OWN lane pays the blind worst case. The wall is 2.7u
        // of solid geometry, twice the height of anything else, so it shadows
        // its own lane on the whole approach - an obstacle parked behind it
        // there is never seen coming - and the vault then lands the player in
        // exactly that lane. Assume they read it cold and have to cross the
        // full width of the track to answer it.
        private static readonly float PalisadeLaneRecoveryTime = VaultTime + PinChainTime;     // 1.85

        // Floor on the gap between consecutive obstacle spawns, whatever the
        // level config asks for. A lone obstacle blocks one lane, so an
        // adjacent lane is always safe: one gesture, never two.
        private static readonly float MinObstacleGapTime = LaneChangeActionTime;

        /// <summary>
        /// Time to chain a set of gestures: one reaction to read the situation,
        /// the mechanical cost of each gesture, and the input cooldown between
        /// them. This is the whole action model - every budget above is a call
        /// to it, so raising ReactionTime moves all of them together.
        /// </summary>
        private static float GestureChainTime(int laneChanges, bool needsJump)
        {
            int gestures = laneChanges + (needsJump ? 1 : 0);
            if (gestures == 0)
                return 0f;

            return ReactionTime
                 + laneChanges * LaneChangeMechanic
                 + (needsJump ? JumpMechanic : 0f)
                 + (gestures - 1) * GestureCooldown;
        }

        private float nextObstacleSpawnZ;
        private int obstaclesSpawned;

        // Fairness rule state
        private float lastObstacleZ;
        private float lastForcingRowZ;
        private float lastPalisadeZ;
        private int? pinnedLane;      // lane the player was last forced into
        private float pinnedLaneZ;
        // Exact set of lanes the player can be sitting in once everything
        // spawned so far is behind them. The pin above is a single lane and so
        // can only approximate this; RowLeadTime consumes the real thing.
        private int reachableLanes;

        // Row handed to RowLeadTime for an about-to-spawn obstacle. Spawning is
        // single-threaded, so one reused buffer avoids a per-spawn allocation.
        private readonly List<ObstacleDefinition> scratchRow = new List<ObstacleDefinition>(3);

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
            reachableLanes = AllLanes;
        }

        /// <summary>
        /// Earliest Z a row of obstacles may go down: far enough past the last
        /// obstacle that the player can actually get from a lane they could be
        /// in to one that survives this row. Consumes scratchRow, and advances
        /// the reachable set to wherever this row leaves them.
        /// </summary>
        private float PlaceRow(float earliestZ)
        {
            float lead = RowLeadTime(reachableLanes, scratchRow, out int next) * RunSpeed;
            reachableLanes = next;
            return Mathf.Max(earliestZ, lastObstacleZ + lead);
        }

        /// <summary>Loads scratchRow with a single obstacle and places it.</summary>
        private float PlaceSingle(string poolTag, int lane, float earliestZ)
        {
            scratchRow.Clear();
            scratchRow.Add(new ObstacleDefinition(poolTag, lane, 0f));
            return PlaceRow(earliestZ);
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
        private float PalisadeLaneGap => PalisadeLaneRecoveryTime * RunSpeed;
        private float DodgeAfterJumpGap => DodgeAfterJumpTime * RunSpeed;

        /// <summary>
        /// Earliest Z this lane may take an obstacle of the given type on
        /// account of what is already standing in it, or float.MinValue if
        /// nothing reserves it. Two reservations, both longer than the ordinary
        /// same-lane gap:
        /// - a palisade's sight shadow, which applies to anything at all;
        /// - a jumpable obstacle, which only reserves the run behind it against
        ///   an instant-death follower - the player is committed to the lane
        ///   through the jump and then has to leave it.
        /// </summary>
        private float LaneFloorFor(string poolTag, int lane)
        {
            float floor = float.MinValue;

            float palisadeZ = obstacleTracker.FrontmostPalisadeZ(lane);
            if (palisadeZ > float.MinValue)
                floor = palisadeZ + PalisadeLaneGap;

            if (!IsObstaclePassable(poolTag))
            {
                float jumpableZ = obstacleTracker.FrontmostJumpableZ(lane);
                if (jumpableZ > float.MinValue)
                    floor = Mathf.Max(floor, jumpableZ + DodgeAfterJumpGap);
            }

            return floor;
        }

        /// <summary>
        /// Clearance test every spawn path shares: the same-lane land-and-rejump
        /// distance, plus the longer per-lane reservations above.
        /// </summary>
        private bool LaneBlockedAt(int lane, float z, string poolTag)
        {
            return obstacleTracker.HasObstacleInLaneBehind(lane, z, SameLaneClearance)
                || z < LaneFloorFor(poolTag, lane);
        }

        /// <summary>
        /// Distance to advance after placing an obstacle: the level config's
        /// authored spacing, never tighter than the reaction budget allows at
        /// this level's speed. The configs are in units and the levels are not
        /// all the same speed, so without this floor the cadence silently
        /// tightens every time SpeedMultiplier goes up.
        /// </summary>
        private float NextObstacleGap()
        {
            float authored = Random.Range(context.CurrentConfig.MinObstacleSpacing,
                                          context.CurrentConfig.MaxObstacleSpacing);
            return Mathf.Max(authored, MinObstacleGapTime * RunSpeed);
        }

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
                    GameLog.Info("Pattern spawn failed, using random generation as fallback");
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
            if (!TryFindClearLane(poolTag, ref lane))
            {
                // No clear lane available, skip this spawn and try again later
                nextObstacleSpawnZ += NextObstacleGap();
                return;
            }

            // FAIRNESS: now the lane and type are known, buy the time it takes
            // to get from where the player can be to a lane that survives it
            nextObstacleSpawnZ = PlaceSingle(poolTag, lane, nextObstacleSpawnZ);

            // Spawn the obstacle
            SpawnSingleObstacleAtPosition(poolTag, lane);
        }

        /// <summary>
        /// Tries to find a clear lane for obstacle spawning
        /// Returns true if a clear lane was found, false otherwise
        /// </summary>
        private bool TryFindClearLane(string poolTag, ref int lane)
        {
            // Check if current lane has clearance
            if (!LaneBlockedAt(lane, nextObstacleSpawnZ, poolTag))
            {
                return true; // Current lane is clear
            }

            // Try to find a clear lane
            int[] lanes = { -1, 0, 1 };
            foreach (int testLane in lanes)
            {
                if (!LaneBlockedAt(testLane, nextObstacleSpawnZ, poolTag))
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

            GameLog.Info($"Attempting to spawn {poolTag} at {spawnPosition}, virtual:{context.VirtualDistance}, count: {obstaclesSpawned}");

            GameObject obstacle = ObjectPooler.Instance?.SpawnFromPool(poolTag, spawnPosition, Quaternion.identity);

            if (obstacle != null)
            {
                obstaclesSpawned++;
                // Track this obstacle's position, lane, and type
                obstacleTracker.AddObstacle(new ObstacleData(nextObstacleSpawnZ, lane, poolTag));
                despawnManager.RegisterObstacle(obstacle);
                OnSingleSpawned(poolTag, lane, nextObstacleSpawnZ);
                nextObstacleSpawnZ += NextObstacleGap();
                GameLog.Info($"Successfully spawned {poolTag}. Next spawn at virtual: {nextObstacleSpawnZ}");
            }
            else
            {
                // Pool exhausted - don't advance spawn position, will retry next frame
                GameLog.Warn($"Pool exhausted for {poolTag}, will retry next frame");
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
                GameLog.Warn($"No valid patterns found for level {currentLevelNum} (difficulty {config.MinPatternDifficulty}-{config.MaxPatternDifficulty})");
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
                GameLog.Warn($"Pattern '{pattern.patternName}' is not solvable, skipping");
                return false;
            }

            // Patterns author their offsets in units, so re-space them for this
            // level's speed before deciding where anything goes. Seeding from
            // reachableLanes matters: a pattern that opens on the one lane the
            // last row left blocked has to start further out.
            int startLanes = reachableLanes;
            List<PatternRow> rows = StretchRows(pattern, startLanes, out float patternLength, out int endLanes);

            // FAIRNESS: the player has to be able to MEET the opening row from
            // wherever they are, and if a forcing row (2+ obstacles at one Z)
            // sits deeper in, reach that too - shift the whole pattern out
            float openingLead = RowLeadTime(startLanes, rows[0].Obstacles, out _);
            float startZ = Mathf.Max(nextObstacleSpawnZ, SpawnFloor);
            startZ = Mathf.Max(startZ, lastObstacleZ + openingLead * RunSpeed);

            float firstForcingOffset = FirstForcingRowOffset(rows);
            if (firstForcingOffset >= 0f)
                startZ = Mathf.Max(startZ, lastObstacleZ + ForcingRowGap - firstForcingOffset);

            // FAIRNESS: an obstacle already standing in one of the pattern's
            // lanes may have reserved the run behind it - a palisade against
            // anything, a hurdle against instant death. Shift the whole pattern
            // clear of that instead of throwing it away over a lane it only
            // touches deep in - the reservations are long, so rejecting here
            // would starve the pattern mix for the rest of the shadow.
            foreach (var row in rows)
            {
                foreach (var obstacleDef in row.Obstacles)
                {
                    float laneFloor = LaneFloorFor(obstacleDef.obstacleType, obstacleDef.lane);
                    if (laneFloor > float.MinValue)
                        startZ = Mathf.Max(startZ, laneFloor - row.Offset);
                }
            }

            // Check clearance for all obstacles in the pattern
            foreach (var row in rows)
            {
                foreach (var obstacleDef in row.Obstacles)
                {
                    // Check if this position has clearance issues
                    if (LaneBlockedAt(obstacleDef.lane, startZ + row.Offset, obstacleDef.obstacleType))
                    {
                        GameLog.Info($"Pattern '{pattern.patternName}' failed clearance check at lane {obstacleDef.lane}, Z offset {row.Offset}");
                        return false;
                    }
                }
            }

            // All checks passed - spawn the pattern
            GameLog.Info($"Spawning pattern: {pattern.patternName} (difficulty {pattern.difficultyRating})");

            foreach (var row in rows)
            {
                foreach (var obstacleDef in row.Obstacles)
                    SpawnObstacleAtLane(obstacleDef.obstacleType, obstacleDef.lane, startZ + row.Offset);
            }

            UpdatePinFromPattern(rows, startZ);
            reachableLanes = endLanes;

            // Advance by pattern length, but always leave breathing room after
            // the pattern's LAST obstacle (patternLength alone can end 6u short)
            float tailOffset = rows[rows.Count - 1].Offset;
            nextObstacleSpawnZ = Mathf.Max(startZ + patternLength, startZ + tailOffset + PatternTailGap);

            return true;
        }

        /// <summary>One rank of a pattern: everything sharing a single Z offset.</summary>
        private struct PatternRow
        {
            public float Offset;
            public List<ObstacleDefinition> Obstacles;

            /// <summary>2+ lanes blocked: the row dictates where the player must be.</summary>
            public bool IsForcing => Obstacles.Count >= 2;
        }

        /// <summary>
        /// Groups a pattern into rows and re-spaces them for the current level.
        /// Authored offsets are fixed units, so the same pattern gets tighter in
        /// TIME on every faster level - Easy Zigzag's 8u lane changes are 533ms
        /// apart at level 1 speed, already under the reaction budget. Each gap is
        /// widened to whatever the action it demands costs at this level's speed;
        /// authored gaps that are already generous are left alone, so a pattern
        /// only ever stretches and its shape survives.
        /// </summary>
        private List<PatternRow> StretchRows(ObstaclePattern pattern, int startLanes, out float stretchedLength, out int endLanes)
        {
            var byOffset = new SortedDictionary<float, List<ObstacleDefinition>>();
            foreach (var def in pattern.obstacles)
            {
                float key = Mathf.Round(def.zOffset * 100f) / 100f;
                if (!byOffset.TryGetValue(key, out var list))
                    byOffset[key] = list = new List<ObstacleDefinition>();
                list.Add(def);
            }

            var rows = new List<PatternRow>(byOffset.Count);
            float authoredPrev = 0f;
            float stretchedPrev = 0f;
            bool first = true;
            int reachable = startLanes;
            // Palisade row still owing recovery room to the rows behind it
            float palisadeOffset = float.MinValue;
            int palisadeLanes = 0;
            // Per lane (-1/0/1 -> 0/1/2): offset of the last obstacle the
            // pattern put there, and of the last JUMPABLE one. The row-to-row
            // lead cannot see either - it prices the cheapest way to MEET a
            // row, which says nothing about a lane the player is already
            // standing in - so a pattern is free to stack its own lane as
            // tightly as it was authored. Easy Straight Line's 10u re-jumps
            // are fine; Medium Double Jump's barrel 8u behind its second
            // hurdle is not.
            var laneLastOffset = new[] { float.MinValue, float.MinValue, float.MinValue };
            var laneLastJumpable = new[] { float.MinValue, float.MinValue, float.MinValue };

            foreach (var kv in byOffset)
            {
                float required = RowLeadTime(reachable, kv.Value, out reachable) * RunSpeed;

                float offset = kv.Key;
                if (!first)
                    offset = stretchedPrev + Mathf.Max(kv.Key - authoredPrev, required);

                // A palisade earlier in the pattern still owes this row its
                // recovery room. The row-to-row lead above cannot see that debt:
                // it prices the gesture the row demands, and the wall is not
                // what the player is reacting to by the time they reach here.
                if (palisadeOffset > float.MinValue)
                {
                    offset = Mathf.Max(offset, palisadeOffset + PalisadeRecoveryGap);
                    if (RowOccupiesAny(kv.Value, palisadeLanes))
                        offset = Mathf.Max(offset, palisadeOffset + PalisadeLaneGap);
                }

                // Same-lane debts owed by the pattern's own earlier rows
                foreach (var def in kv.Value)
                {
                    int slot = def.lane + 1;
                    if (laneLastOffset[slot] > float.MinValue)
                        offset = Mathf.Max(offset, laneLastOffset[slot] + SameLaneClearance);
                    if (!IsObstaclePassable(def.obstacleType) && laneLastJumpable[slot] > float.MinValue)
                        offset = Mathf.Max(offset, laneLastJumpable[slot] + DodgeAfterJumpGap);
                }

                rows.Add(new PatternRow { Offset = offset, Obstacles = kv.Value });

                foreach (var def in kv.Value)
                {
                    int slot = def.lane + 1;
                    laneLastOffset[slot] = offset;
                    // A lethal obstacle CLEARS the debt rather than inheriting
                    // it: the player it drove out of this lane is not standing
                    // in it any more, so whatever follows starts fresh here.
                    laneLastJumpable[slot] = IsObstaclePassable(def.obstacleType) ? offset : float.MinValue;
                }

                // From here on, this row's palisades own the recovery clock
                int lanesHere = PalisadeLaneMask(kv.Value);
                if (lanesHere != 0)
                {
                    palisadeOffset = offset;
                    palisadeLanes = lanesHere;
                }

                authoredPrev = kv.Key;
                stretchedPrev = offset;
                first = false;
            }

            // patternLength is the authored trailing room; it stretches with the body
            stretchedLength = pattern.patternLength + (stretchedPrev - authoredPrev);
            endLanes = reachable;
            return rows;
        }

        private const int AllLanes = 0b111;

        /// <summary>Bit for a lane in a reachable-lane mask (-1/0/1 -> 0/1/2).</summary>
        private static int Bit(int lane) => 1 << (lane + 1);

        /// <summary>Lanes in this row holding a palisade, as a lane mask.</summary>
        private static int PalisadeLaneMask(List<ObstacleDefinition> row)
        {
            int mask = 0;
            foreach (var def in row)
            {
                if (def.obstacleType == PoolTags.ObstaclePalisade)
                    mask |= Bit(def.lane);
            }
            return mask;
        }

        /// <summary>Anything in this row sits in one of the masked lanes.</summary>
        private static bool RowOccupiesAny(List<ObstacleDefinition> row, int laneMask)
        {
            foreach (var def in row)
            {
                if ((laneMask & Bit(def.lane)) != 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Cheapest time the player needs to meet this row from any lane they
        /// could currently be in, and (out) the lanes that leaves them in.
        ///
        /// The point is that a row only costs what it actually forces: a slalom
        /// of pylons down the outside lanes is free to a player sitting in the
        /// centre, and stretching it would just break its rhythm. Only three
        /// lanes exist, so carrying the reachable set forward as a bitmask is
        /// exact rather than a guess.
        /// </summary>
        private static float RowLeadTime(int fromMask, List<ObstacleDefinition> row, out int toMask)
        {
            // Cheapest gesture chain from a lane they could be in to one that
            // survives the row
            float best = float.MaxValue;
            foreach (int from in LaneOrder)
            {
                if ((fromMask & Bit(from)) == 0)
                    continue;

                foreach (int to in LaneOrder)
                {
                    if (LaneSurvivable(row, to))
                        best = Mathf.Min(best, TransitionCost(from, to, row));
                }
            }

            if (best == float.MaxValue)
            {
                // No survivable lane at all; IsSolvable() already warned
                toMask = AllLanes;
                return ForcingRowLeadTime;
            }

            // A row blocking 2+ lanes dictates where the player must be, so it
            // has to be read and repositioned for even if they happen to be
            // standing in the lane it leaves open
            float lead = row.Count >= 2 ? Mathf.Max(best, ForcingRowLeadTime) : best;

            // Everywhere they can actually get to in the time that lead buys
            toMask = 0;
            foreach (int from in LaneOrder)
            {
                if ((fromMask & Bit(from)) == 0)
                    continue;

                foreach (int to in LaneOrder)
                {
                    if (LaneSurvivable(row, to) && TransitionCost(from, to, row) <= lead + 0.0001f)
                        toMask |= Bit(to);
                }
            }

            return lead;
        }

        /// <summary>
        /// Cost of meeting a row in lane <paramref name="to"/> starting from
        /// lane <paramref name="from"/>: one swipe per lane crossed, plus a
        /// jump if something jumpable is sitting in the destination.
        /// </summary>
        private static float TransitionCost(int from, int to, List<ObstacleDefinition> row)
        {
            return GestureChainTime(Mathf.Abs(to - from), !LaneFree(row, to));
        }

        private static readonly int[] LaneOrder = { -1, 0, 1 };

        /// <summary>Nothing occupies this lane in the row.</summary>
        private static bool LaneFree(List<ObstacleDefinition> row, int lane)
        {
            foreach (var def in row)
            {
                if (def.lane == lane)
                    return false;
            }
            return true;
        }

        /// <summary>The player can hold this lane through the row - free, or jumpable.</summary>
        private static bool LaneSurvivable(List<ObstacleDefinition> row, int lane)
        {
            foreach (var def in row)
            {
                if (def.lane == lane)
                    return IsObstaclePassable(def.obstacleType);
            }
            return true;
        }

        /// <summary>
        /// Z offset of the pattern's first row with 2+ obstacles (a row that
        /// forces the player's lane and usually an action), or -1 if none.
        /// </summary>
        private static float FirstForcingRowOffset(List<PatternRow> rows)
        {
            foreach (var row in rows)
            {
                if (row.IsForcing)
                    return row.Offset;
            }
            return -1f;
        }

        /// <summary>
        /// After spawning a pattern, updates the pinned lane from its forcing
        /// rows (free lane first, else a passable lane), and keeps the pin
        /// anchored at the pattern tail: obstacles after the pin block the
        /// crossing corridor, so the escape clock starts at the tail.
        /// </summary>
        private void UpdatePinFromPattern(List<PatternRow> rows, float startZ)
        {
            foreach (var row in rows)
            {
                if (!row.IsForcing)
                    continue;

                lastForcingRowZ = Mathf.Max(lastForcingRowZ, startZ + row.Offset);

                var occupied = new HashSet<int>();
                foreach (var def in row.Obstacles)
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
                    foreach (var def in row.Obstacles)
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
                    pinnedLaneZ = startZ + row.Offset;
                }
            }

            float tail = rows[rows.Count - 1].Offset;
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
            if (LaneBlockedAt(lane1, nextObstacleSpawnZ, obstacleType) ||
                LaneBlockedAt(lane2, nextObstacleSpawnZ, obstacleType))
            {
                // Clearance failed for row - try spawning a single obstacle instead
                GameLog.Info("Two-lane row clearance failed, retrying with single obstacle");
                SpawnSingleObstacleWithRetry();
                return;
            }

            // FAIRNESS: buy the time it takes to reach the one lane this leaves
            scratchRow.Clear();
            scratchRow.Add(new ObstacleDefinition(obstacleType, lane1, 0f));
            scratchRow.Add(new ObstacleDefinition(obstacleType, lane2, 0f));
            nextObstacleSpawnZ = PlaceRow(nextObstacleSpawnZ);

            // Spawn in both lanes at the same Z position
            SpawnObstacleAtLane(obstacleType, lane1, nextObstacleSpawnZ);
            SpawnObstacleAtLane(obstacleType, lane2, nextObstacleSpawnZ);

            pinnedLane = freeLane;
            pinnedLaneZ = nextObstacleSpawnZ;
            lastForcingRowZ = Mathf.Max(lastForcingRowZ, nextObstacleSpawnZ);

            // Update next spawn position
            nextObstacleSpawnZ += NextObstacleGap();
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
                GameLog.Info($"Prevented impossible 3-lane row! Replaced one instant-death obstacle with passable type.");
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

            // Check clearance for all 3 lanes. This waits until the types are
            // settled because the reservations are type-dependent: dropping a
            // barrel into the lane of a hurdle just behind costs more room than
            // dropping another hurdle there.
            for (int i = 0; i < 3; i++)
            {
                if (!LaneBlockedAt(lanes[i], nextObstacleSpawnZ, types[i]))
                    continue;

                // Clearance failed for 3-lane row - try a simpler two-lane row instead
                GameLog.Info("Three-lane row clearance failed, retrying with two-lane row");
                SpawnTwoLaneRow();
                return;
            }

            // FAIRNESS: buy the time it takes to reach the passable lane
            scratchRow.Clear();
            for (int i = 0; i < 3; i++)
                scratchRow.Add(new ObstacleDefinition(types[i], lanes[i], 0f));
            nextObstacleSpawnZ = PlaceRow(nextObstacleSpawnZ);

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
            nextObstacleSpawnZ += NextObstacleGap();
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
            if (LaneBlockedAt(lane, nextObstacleSpawnZ, poolTag))
            {
                int[] lanes = { -1, 0, 1 };
                bool foundClearLane = false;

                foreach (int testLane in lanes)
                {
                    if (!LaneBlockedAt(testLane, nextObstacleSpawnZ, poolTag))
                    {
                        lane = testLane;
                        foundClearLane = true;
                        break;
                    }
                }

                // Only skip if all lanes are blocked (very rare edge case)
                if (!foundClearLane)
                {
                    GameLog.Warn("All lanes blocked, skipping spawn (rare edge case)");
                    nextObstacleSpawnZ += NextObstacleGap();
                    return;
                }
            }

            // FAIRNESS: buy the time to reach a lane that survives this obstacle
            nextObstacleSpawnZ = PlaceSingle(poolTag, lane, nextObstacleSpawnZ);

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
                nextObstacleSpawnZ += NextObstacleGap();
            }
            else
            {
                // Pool exhausted - don't advance spawn position, will retry next frame
                GameLog.Warn($"Pool exhausted for {poolTag}, will retry next frame");
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

            GameLog.Info($"Attempting to spawn {poolTag} at lane {lane}, virtual Z: {virtualZ}");

            GameObject obstacle = ObjectPooler.Instance?.SpawnFromPool(poolTag, spawnPosition, Quaternion.identity);

            if (obstacle != null)
            {
                obstaclesSpawned++;
                obstacleTracker.AddObstacle(new ObstacleData(virtualZ, lane, poolTag));
                despawnManager.RegisterObstacle(obstacle);
                lastObstacleZ = Mathf.Max(lastObstacleZ, virtualZ);
                if (poolTag == PoolTags.ObstaclePalisade)
                    NotePalisade(lane, virtualZ);
                GameLog.Info($"Successfully spawned {poolTag} at lane {lane}");
            }
            else
            {
                GameLog.Warn($"Failed to spawn {poolTag} at lane {lane}!");
            }
        }

        /// <summary>
        /// Check if an obstacle type is passable (not instant death)
        /// </summary>
        private static bool IsObstaclePassable(string obstacleType)
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
