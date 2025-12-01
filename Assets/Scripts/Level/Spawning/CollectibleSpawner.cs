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

        // Coin arc tracking (to prevent duplicate arcs for same obstacle)
        private HashSet<float> obstaclesWithArcs = new HashSet<float>();

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
            obstaclesWithArcs.Clear();
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
                // First, check if there's an upcoming jumpable obstacle that needs a coin arc
                ObstacleData? upcomingObstacle = obstacleTracker.GetUpcomingJumpableObstacle(nextCollectibleSpawnZ);

                if (upcomingObstacle.HasValue && !obstaclesWithArcs.Contains(upcomingObstacle.Value.zPosition))
                {
                    // Spawn coin arc for this obstacle
                    SpawnCoinArc(upcomingObstacle.Value);

                    // Mark this obstacle as having an arc
                    obstaclesWithArcs.Add(upcomingObstacle.Value.zPosition);

                    // Advance spawn position past the arc (obstacle position + 3.5 units after + a small buffer)
                    nextCollectibleSpawnZ = upcomingObstacle.Value.zPosition + 4.5f;

                    // End any coin train that might be active
                    isInCoinTrain = false;
                    coinTrainRemaining = 0;

                    return; // Skip regular collectible spawning this frame
                }

                // Check if this position is near any obstacle (within 3 units before or after)
                ObstacleData? nearbyObstacle = obstacleTracker.GetNearbyObstacle(nextCollectibleSpawnZ, 3f);

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
                    if (obstacleTracker.HasObstacleInLaneAhead(coinTrainLane, nextCollectibleSpawnZ, lookaheadDistance))
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
                    if (nearbyObstacle.Value.CanHaveCollectibleAbove() && Random.value < context.CurrentConfig.CollectibleAboveObstacleChance)
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
                float spawnZ = context.PlayerPosition.z + (nextCollectibleSpawnZ - context.VirtualDistance);
                Vector3 spawnPosition = new Vector3(xPosition, spawnHeight, spawnZ);

                // Randomly choose between regular and mega collectible
                bool isMega = Random.value < context.CurrentConfig.MegaCollectibleSpawnRatio;
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
            if (obstacle.obstacleType == "ObstaclePalisade")
            {
                peakHeight = 3.5f; // Higher arc for tall palisades
            }
            else if (obstacle.obstacleType == "ObstacleBroadJump")
            {
                peakHeight = 2.5f; // Medium arc for broad jumps
            }
            else // ObstacleJump
            {
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

                // Calculate world position
                float spawnZ = context.PlayerPosition.z + (spawnVirtualZ - context.VirtualDistance);
                Vector3 spawnPosition = new Vector3(xPosition, spawnHeight, spawnZ);

                // Randomly choose between regular and mega collectible
                bool isMega = Random.value < context.CurrentConfig.MegaCollectibleSpawnRatio;
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
                            collectibleComponent.SetPointValue(context.CurrentConfig.MegaCollectiblePointValue);
                        }
                    }

                    collectiblesSpawned++;
                    despawnManager.RegisterCollectible(collectible);
                }
            }

            Debug.Log($"Spawned coin arc with {coinCount} coins over {obstacle.obstacleType} at lane {obstacle.lane}, peak height {peakHeight}");
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
