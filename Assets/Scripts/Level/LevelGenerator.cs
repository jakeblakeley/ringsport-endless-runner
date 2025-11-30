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

            if (virtualDistance + spawnDistance > nextObstacleSpawnZ)
            {
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
                    Debug.LogWarning($"Failed to spawn {poolTag}!");
                }
            }
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
                // Skip this spawn if clearance check fails
                nextObstacleSpawnZ += Random.Range(currentConfig.MinObstacleSpacing, currentConfig.MaxObstacleSpacing);
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
        /// </summary>
        private void SpawnSingleLaneRow()
        {
            // Check clearance for all 3 lanes
            if (HasObstacleInLaneBehind(-1, nextObstacleSpawnZ, 4f) ||
                HasObstacleInLaneBehind(0, nextObstacleSpawnZ, 4f) ||
                HasObstacleInLaneBehind(1, nextObstacleSpawnZ, 4f))
            {
                // Skip this spawn if any lane has clearance issues
                nextObstacleSpawnZ += Random.Range(currentConfig.MinObstacleSpacing, currentConfig.MaxObstacleSpacing);
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

                bool spawnAboveObstacle = false;
                float spawnHeight = 1f; // Default collectible height
                int lane = 0;

                // Determine lane based on coin train or obstacle proximity
                if (isInCoinTrain && coinTrainRemaining > 0)
                {
                    // Continue the coin train in the same lane
                    lane = coinTrainLane;
                    coinTrainRemaining--;

                    if (coinTrainRemaining == 0)
                    {
                        isInCoinTrain = false;
                    }
                }
                else if (nearbyObstacle.HasValue)
                {
                    // Near an obstacle - check if we should spawn above it
                    if (nearbyObstacle.Value.CanHaveCollectibleAbove() && Random.value < currentConfig.CollectibleAboveObstacleChance)
                    {
                        // Spawn above the jump or palisade obstacle
                        spawnAboveObstacle = true;

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

            // Clean up obstacle tracking data for objects that have been despawned
            // Remove obstacles that are far behind the player
            float cleanupThreshold = virtualDistance - 20f; // Keep some history
            obstaclePositions.RemoveAll(obstacle => obstacle.zPosition < cleanupThreshold);

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
    }
}
