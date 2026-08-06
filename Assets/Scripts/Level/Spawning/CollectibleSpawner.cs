using UnityEngine;
using System.Collections.Generic;
using RingSport.Core;

namespace RingSport.Level.Spawning
{
    /// <summary>
    /// Handles collectible spawning including coin trains and coin arcs
    /// </summary>
    public class CollectibleSpawner
    {
        private float nextCollectibleSpawnZ;
        private int collectiblesSpawned;
        private int previousCollectibleLane = 0; // Track previous collectible lane for line bias

        // Coin train tracking (Subway Surfers style)
        private bool isInCoinTrain = false;
        private int coinTrainRemaining = 0;
        private int coinTrainLane = 0;

        // Obstacles whose coin arc has been decided - drawn OR rolled against -
        // so each one is only considered once
        private HashSet<float> arcDecidedObstacles = new HashSet<float>();

        private SpawnContext context;
        private ObstacleTracker obstacleTracker;
        private DespawnManager despawnManager;

        public CollectibleSpawner(
            SpawnContext context,
            ObstacleTracker obstacleTracker,
            DespawnManager despawnManager)
        {
            this.context = context;
            this.obstacleTracker = obstacleTracker;
            this.despawnManager = despawnManager;
        }

        /// <summary>
        /// Initialize collectible spawning for a new level
        /// </summary>
        public void Initialize()
        {
            nextCollectibleSpawnZ = 15f;
            collectiblesSpawned = 0;
            previousCollectibleLane = 0; // Reset to center lane

            // Reset coin train state
            isInCoinTrain = false;
            coinTrainRemaining = 0;
            coinTrainLane = 0;

            // Reset coin arc tracking
            arcDecidedObstacles.Clear();
        }

        /// <summary>
        /// Attempt to spawn collectibles
        /// </summary>
        public void SpawnCollectibles()
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

            if (context.VirtualDistance + context.SpawnDistance > nextCollectibleSpawnZ)
            {
                // Try to spawn coin arc for jumpable obstacles first
                if (TrySpawnCoinArcForObstacle())
                {
                    return; // Coin arc spawned, skip regular collectible this frame
                }

                // Determine lane and height for regular collectible
                DetermineLaneAndHeight(out int lane, out float spawnHeight);

                // Spawn the collectible
                SpawnSingleCollectible(lane, spawnHeight);
            }
        }

        /// <summary>
        /// Tries to spawn a coin arc over the next obstacle that can carry one
        /// Returns true if a coin arc was spawned
        /// </summary>
        private bool TrySpawnCoinArcForObstacle()
        {
            // Nearest obstacle ahead that hasn't had its arc decided yet
            ObstacleData? upcoming = obstacleTracker.GetUpcomingArcObstacle(nextCollectibleSpawnZ, arcDecidedObstacles);

            if (!upcoming.HasValue)
            {
                return false; // Nothing in range
            }

            ObstacleData obstacle = upcoming.Value;

            // Whatever we decide, this obstacle is settled - don't reconsider it
            arcDecidedObstacles.Add(obstacle.zPosition);

            // Barrels and pylons arc only sometimes. The jump over them is real
            // but tight, and the generator prices them as lane changes: a pylon
            // slalom down the outside lanes is meant to be free to a player
            // sitting in the centre, so arcing every one would drag them out of
            // the safe lane for a coin. Passable obstacles always arc.
            if (obstacle.IsInstantDeath() && Random.value >= context.CurrentConfig.CollectibleAboveObstacleChance)
            {
                return false; // Declined - fall through to a regular collectible
            }

            SpawnCoinArc(obstacle);

            // Advance spawn position past the arc (obstacle position + 3.5 units after + a small buffer)
            nextCollectibleSpawnZ = obstacle.zPosition + 4.5f;

            // End any coin train that might be active
            isInCoinTrain = false;
            coinTrainRemaining = 0;

            return true; // Coin arc spawned
        }

        /// <summary>
        /// Determines the lane and spawn height for a collectible based on obstacles and coin trains
        /// </summary>
        private void DetermineLaneAndHeight(out int lane, out float spawnHeight)
        {
            spawnHeight = 1f; // Default collectible height
            lane = 0; // Default lane

            // Check if this position is near any obstacle (within 3 units before or after)
            ObstacleData? nearbyObstacle = obstacleTracker.GetNearbyObstacle(nextCollectibleSpawnZ, 3f);

            // Determine lane based on coin train or obstacle proximity
            if (isInCoinTrain && coinTrainRemaining > 0)
            {
                HandleCoinTrainLogic(out lane);
            }
            else if (nearbyObstacle.HasValue)
            {
                HandleNearbyObstacleLogic(nearbyObstacle.Value, out lane, out spawnHeight);
            }
            else
            {
                HandleOpenSpaceLogic(out lane);
            }
        }

        /// <summary>
        /// Handles lane selection logic when in a coin train
        /// </summary>
        private void HandleCoinTrainLogic(out int lane)
        {
            // Continue the coin train in the same lane
            lane = coinTrainLane;

            // FAIRNESS CHECK: Lookahead to prevent coin train leading into obstacle
            float lookaheadDistance = 2.5f * coinTrainRemaining; // Estimate remaining train length
            if (obstacleTracker.HasObstacleInLaneAhead(coinTrainLane, nextCollectibleSpawnZ, lookaheadDistance))
            {
                // Obstacle detected ahead - end coin train early for safety
                GameLog.Info($"Coin train lookahead detected obstacle in lane {coinTrainLane}, ending train early");
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

        /// <summary>
        /// Handles lane and height selection logic when near an obstacle
        /// </summary>
        private void HandleNearbyObstacleLogic(ObstacleData obstacle, out int lane, out float spawnHeight)
        {
            spawnHeight = 1f; // Default

            // Check if we should spawn above the obstacle
            if (obstacle.CanHaveCollectibleAbove() && Random.value < context.CurrentConfig.CollectibleAboveObstacleChance)
            {
                // Spawn above the jump or palisade obstacle
                lane = obstacle.lane;

                // Palisades are 2m tall, so spawn collectibles higher above them
                if (obstacle.obstacleType == PoolTags.ObstaclePalisade)
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
                int[] otherLanes = GetLanesExcept(obstacle.lane);
                lane = PickSafestLane(otherLanes);

                // Maybe start a coin train (40% chance)
                if (!isInCoinTrain && Random.value < 0.4f)
                {
                    StartCoinTrain(lane);
                }
            }
        }

        /// <summary>
        /// Handles lane selection logic when in open space (no obstacles nearby)
        /// </summary>
        private void HandleOpenSpaceLogic(out int lane)
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

        /// <summary>
        /// Distance either side of a barrel or pylon that a flat coin line is
        /// kept out of its lane. Roughly the depth of the prop plus the coin's
        /// own pickup radius, so a coin never reads as sitting on top of one.
        /// </summary>
        private const float DeadlyObstacleCoinClearance = 2f;

        /// <summary>
        /// Pick from the candidate lanes, preferring any that doesn't have a
        /// barrel or pylon sitting at this Z. Falls back to a plain random pick
        /// when they're all occupied - the arc path handles the "over it" case.
        /// </summary>
        private int PickSafestLane(int[] candidates)
        {
            int safeCount = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (!obstacleTracker.HasDeadlyObstacleInLane(candidates[i], nextCollectibleSpawnZ, DeadlyObstacleCoinClearance))
                    safeCount++;
            }

            if (safeCount == 0)
            {
                return candidates[Random.Range(0, candidates.Length)];
            }

            int pick = Random.Range(0, safeCount);
            for (int i = 0; i < candidates.Length; i++)
            {
                if (obstacleTracker.HasDeadlyObstacleInLane(candidates[i], nextCollectibleSpawnZ, DeadlyObstacleCoinClearance))
                    continue;
                if (pick == 0)
                    return candidates[i];
                pick--;
            }

            return candidates[0]; // Unreachable
        }

        /// <summary>
        /// Spawns a single collectible at the specified lane and height
        /// </summary>
        private void SpawnSingleCollectible(int lane, float spawnHeight)
        {
            // Last line of defence for every path into here (coin train, lane
            // bias, open space): never park a flat coin on a barrel or pylon.
            // Skip the coin rather than jog the lane - a one-coin hole in a
            // train reads as nothing, a sideways kink reads as a bug.
            if (obstacleTracker.HasDeadlyObstacleInLane(lane, nextCollectibleSpawnZ, DeadlyObstacleCoinClearance))
            {
                nextCollectibleSpawnZ += Random.Range(context.CurrentConfig.MinCollectibleSpacing, context.CurrentConfig.MaxCollectibleSpacing);
                return;
            }

            float xPosition = lane * 3f;

            // Anchor to world origin (0,0,0) for grid alignment
            float spawnZ = nextCollectibleSpawnZ - context.VirtualDistance;
            Vector3 spawnPosition = new Vector3(xPosition, spawnHeight, spawnZ);

            // Randomly choose between regular and mega collectible
            bool isMega = Random.value < context.CurrentConfig.MegaCollectibleSpawnRatio;

            // A large coin can spawn as a love note instead while locked notes remain
            bool isLoveNote = isMega && LoveNoteManager.RollMegaCoinReplace();

            string poolTag = isLoveNote ? PoolTags.LoveNote
                : isMega ? PoolTags.MegaCollectible
                : PoolTags.Collectible;

            GameObject collectible = ObjectPooler.Instance?.SpawnFromPool(poolTag, spawnPosition, Quaternion.identity);

            if (collectible != null)
            {
                // Love notes and mega collectibles are worth the same points
                if (isLoveNote)
                {
                    var loveNote = collectible.GetComponent<LoveNoteCollectible>();
                    if (loveNote != null)
                    {
                        loveNote.SetPointValue(context.CurrentConfig.MegaCollectiblePointValue);
                    }
                }
                else if (isMega)
                {
                    var collectibleComponent = collectible.GetComponent<Collectible>();
                    if (collectibleComponent != null)
                    {
                        collectibleComponent.SetPointValue(context.CurrentConfig.MegaCollectiblePointValue);
                    }
                }

                collectiblesSpawned++;
                previousCollectibleLane = lane; // Remember this lane for next spawn

                // Register with despawn manager
                despawnManager.RegisterCollectible(collectible);

                // Adjust spacing: tighter for coin trains, normal otherwise
                float spacing;
                if (isInCoinTrain && coinTrainRemaining > 0)
                {
                    spacing = 2.5f; // Tight spacing for coin trains
                }
                else
                {
                    spacing = Random.Range(context.CurrentConfig.MinCollectibleSpacing, context.CurrentConfig.MaxCollectibleSpacing);
                }

                nextCollectibleSpawnZ += spacing;
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
        /// Get the next collectible lane with bias towards staying in the same lane
        /// </summary>
        private int GetNextCollectibleLane(int previousLane)
        {
            // Apply lane bias - higher chance to stay in same lane (Subway Surfers pattern)
            if (Random.value < context.CurrentConfig.CollectibleLineBias)
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

        /// <summary>
        /// Spawn a coin arc pattern over a jumpable obstacle
        /// Creates a parabolic arc of coins to hint to the player to jump
        /// </summary>
        private void SpawnCoinArc(ObstacleData obstacle)
        {
            // Determine number of coins (5-7)
            int coinCount = Random.Range(5, 8);

            // Arc parameters
            float arcStartOffset = -3.5f; // Start 3.5 units before obstacle
            float arcEndOffset = 3.5f; // End 3.5 units after obstacle
            float arcLength = arcEndOffset - arcStartOffset;

            // Determine peak height based on obstacle type
            float peakHeight;
            if (obstacle.obstacleType == PoolTags.ObstaclePalisade)
            {
                peakHeight = 3.5f; // Higher arc for tall palisades
            }
            else if (obstacle.obstacleType == PoolTags.ObstacleBroadJump)
            {
                peakHeight = 2.5f; // Medium arc for broad jumps
            }
            else // ObstacleJump, and the barrels and pylons
            {
                // Barrels (top 1.0u) and pylons (0.9u) are SHORTER than a
                // hurdle (1.35u) but share its peak: the arc is a picture of
                // the dog's jump, and that jump is the same one either way.
                // Scaling the peak to the obstacle would flatten these two into
                // a coin line that no longer reads as "hop this".
                peakHeight = 2.0f; // Standard arc for regular jumps
            }

            float baseHeight = 1f; // Starting/ending height

            // Spawn coins along the arc
            for (int i = 0; i < coinCount; i++)
            {
                // Calculate position along the arc (0 to 1)
                float t = i / (float)(coinCount - 1);

                // Z position: interpolate from start to end of arc
                float zOffset = Mathf.Lerp(arcStartOffset, arcEndOffset, t);
                float spawnVirtualZ = obstacle.zPosition + zOffset;

                // Y position: parabolic arc (peaks in the middle)
                // Use inverted parabola: height = -a * (t - 0.5)^2 + peakHeight
                // where a controls the curve steepness
                float centerOffset = t - 0.5f; // -0.5 to 0.5
                float heightMultiplier = 1f - (centerOffset * centerOffset * 4f); // Parabola: 0 at edges, 1 at center
                float spawnHeight = Mathf.Lerp(baseHeight, peakHeight, heightMultiplier);

                // X position: same lane as obstacle
                float xPosition = obstacle.lane * 3f;

                // Calculate world position - anchor to world origin (0,0,0) for grid alignment
                float spawnZ = spawnVirtualZ - context.VirtualDistance;
                Vector3 spawnPosition = new Vector3(xPosition, spawnHeight, spawnZ);

                // Randomly choose between regular and mega collectible
                bool isMega = Random.value < context.CurrentConfig.MegaCollectibleSpawnRatio;

                // A large coin can spawn as a love note instead while locked notes remain
                bool isLoveNote = isMega && LoveNoteManager.RollMegaCoinReplace();

                string poolTag = isLoveNote ? PoolTags.LoveNote
                    : isMega ? PoolTags.MegaCollectible
                    : PoolTags.Collectible;

                GameObject collectible = ObjectPooler.Instance?.SpawnFromPool(poolTag, spawnPosition, Quaternion.identity);

                if (collectible != null)
                {
                    // Love notes and mega collectibles are worth the same points
                    if (isLoveNote)
                    {
                        var loveNote = collectible.GetComponent<LoveNoteCollectible>();
                        if (loveNote != null)
                        {
                            loveNote.SetPointValue(context.CurrentConfig.MegaCollectiblePointValue);
                        }
                    }
                    else if (isMega)
                    {
                        var collectibleComponent = collectible.GetComponent<Collectible>();
                        if (collectibleComponent != null)
                        {
                            collectibleComponent.SetPointValue(context.CurrentConfig.MegaCollectiblePointValue);
                        }
                    }

                    collectiblesSpawned++;
                    despawnManager.RegisterCollectible(collectible);
                }
            }

            GameLog.Info($"Spawned coin arc with {coinCount} coins over {obstacle.obstacleType} at lane {obstacle.lane}, peak height {peakHeight}");
        }

        /// <summary>
        /// Get the next collectible spawn Z position (for debugging)
        /// </summary>
        public float GetNextCollectibleSpawnZ() => nextCollectibleSpawnZ;

        /// <summary>
        /// Get the number of collectibles spawned (for debugging)
        /// </summary>
        public int GetCollectiblesSpawned() => collectiblesSpawned;
    }
}
