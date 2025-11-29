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
        public bool isJumpObstacle; // true if ObstacleJump, false if ObstacleAvoid

        public ObstacleData(float z, bool isJump)
        {
            zPosition = z;
            isJumpObstacle = isJump;
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
            if (obstaclesSpawned >= currentConfig.MaxObstacles)
                return;

            if (virtualDistance + spawnDistance > nextObstacleSpawnZ)
            {
                // Randomly choose obstacle type
                bool spawnAvoid = Random.value > 0.5f;
                string poolTag = spawnAvoid ? "ObstacleAvoid" : "ObstacleJump";

                // Random lane
                int lane = Random.Range(-1, 2);
                float xPosition = lane * 3f;

                // Spawn at player position + offset
                float spawnZ = player.position.z + (nextObstacleSpawnZ - virtualDistance);
                Vector3 spawnPosition = new Vector3(xPosition, 0f, spawnZ);

                Debug.Log($"Attempting to spawn {poolTag} at {spawnPosition}, virtual:{virtualDistance}, count: {obstaclesSpawned}/{currentConfig.MaxObstacles}");

                GameObject obstacle = ObjectPooler.Instance?.SpawnFromPool(poolTag, spawnPosition, Quaternion.identity);

                if (obstacle != null)
                {
                    obstaclesSpawned++;
                    // Track this obstacle's position and type
                    bool isJumpObstacle = poolTag == "ObstacleJump";
                    obstaclePositions.Add(new ObstacleData(nextObstacleSpawnZ, isJumpObstacle));
                    nextObstacleSpawnZ += Random.Range(currentConfig.MinObstacleSpacing, currentConfig.MaxObstacleSpacing);
                    Debug.Log($"Successfully spawned {poolTag}. Next spawn at virtual: {nextObstacleSpawnZ}");
                }
                else
                {
                    Debug.LogWarning($"Failed to spawn {poolTag}!");
                }
            }
        }

        private void SpawnCollectibles()
        {
            // Calculate max collectibles based on obstacles spawned and ratio
            int maxCollectiblesForLevel = Mathf.RoundToInt(currentConfig.MaxObstacles * currentConfig.CollectibleToObstacleRatio);

            if (collectiblesSpawned >= maxCollectiblesForLevel)
                return;

            if (virtualDistance + spawnDistance > nextCollectibleSpawnZ)
            {
                // Check if this position is near any obstacle
                ObstacleData? nearbyObstacle = GetNearbyObstacle(nextCollectibleSpawnZ);

                bool spawnAboveObstacle = false;
                float spawnHeight = 1f; // Default collectible height

                if (nearbyObstacle.HasValue)
                {
                    // Near an obstacle - check if we should spawn above it
                    if (nearbyObstacle.Value.isJumpObstacle && Random.value < currentConfig.CollectibleAboveObstacleChance)
                    {
                        // Spawn above the jump obstacle
                        spawnAboveObstacle = true;
                        spawnHeight = 2.5f; // Higher position requiring a jump to collect
                    }
                    else
                    {
                        // Either it's an avoid obstacle or we didn't roll the chance - skip this position
                        nextCollectibleSpawnZ += Random.Range(currentConfig.MinCollectibleSpacing, currentConfig.MaxCollectibleSpacing);
                        return;
                    }
                }

                // Select lane with bias towards previous lane (Subway Surfers-style line pattern)
                int lane = GetNextCollectibleLane(previousCollectibleLane);
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
                    nextCollectibleSpawnZ += Random.Range(currentConfig.MinCollectibleSpacing, currentConfig.MaxCollectibleSpacing);
                }
            }
        }

        /// <summary>
        /// Check if a collectible spawn position is within the minimum distance of any obstacle
        /// Returns the obstacle data if close, null otherwise
        /// </summary>
        private ObstacleData? GetNearbyObstacle(float zPosition)
        {
            float minDistance = currentConfig.MinCollectibleObstacleDistance;

            foreach (ObstacleData obstacle in obstaclePositions)
            {
                if (Mathf.Abs(zPosition - obstacle.zPosition) < minDistance)
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
    }
}
