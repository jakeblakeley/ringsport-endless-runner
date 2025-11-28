using UnityEngine;
using RingSport.Core;

namespace RingSport.Level
{
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

        private LevelConfig currentConfig;
        private float nextObstacleSpawnZ;
        private float nextCollectibleSpawnZ;
        private float nextFloorSpawnZ;
        private int obstaclesSpawned;
        private int collectiblesSpawned;
        private float virtualDistance = 0f; // Tracks how far the level has scrolled

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

            // Reset virtual distance and spawn tracking
            virtualDistance = 0f;
            nextObstacleSpawnZ = 20f; // Spawn first obstacle 20 units ahead
            nextCollectibleSpawnZ = 15f;
            nextFloorSpawnZ = -10f; // Start floor behind player
            obstaclesSpawned = 0;
            collectiblesSpawned = 0;

            Debug.Log($"Virtual distance reset. Initial spawn positions - Obstacle: {nextObstacleSpawnZ}, Collectible: {nextCollectibleSpawnZ}, Floor: {nextFloorSpawnZ}");
        }

        private void SpawnFloor()
        {
            // Keep spawning floor tiles ahead based on virtual distance
            int spawnAttempts = 0;
            int maxAttempts = 10; // Prevent infinite loops

            while (nextFloorSpawnZ < virtualDistance + spawnDistance && spawnAttempts < maxAttempts)
            {
                // Spawn at player position + offset
                float spawnZ = player.position.z + (nextFloorSpawnZ - virtualDistance);
                Vector3 spawnPosition = new Vector3(0f, -0.5f, spawnZ);

                GameObject floorTile = ObjectPooler.Instance?.SpawnFromPool("Floor", spawnPosition, Quaternion.identity);

                if (floorTile != null)
                {
                    // Scale the floor tile to match the tile length
                    floorTile.transform.localScale = new Vector3(10f, 1f, floorTileLength);

                    nextFloorSpawnZ += floorTileLength;
                    spawnAttempts = 0; // Reset attempts on successful spawn

                    if (Time.frameCount % 60 == 0)
                    {
                        Debug.Log($"Spawned floor at Z:{spawnZ}, virtual:{virtualDistance}, nextFloor:{nextFloorSpawnZ}");
                    }
                }
                else
                {
                    // Pool exhausted, stop trying
                    spawnAttempts++;
                }
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
            if (collectiblesSpawned >= currentConfig.MaxCollectibles)
                return;

            if (virtualDistance + spawnDistance > nextCollectibleSpawnZ)
            {
                // Random lane
                int lane = Random.Range(-1, 2);
                float xPosition = lane * 3f;

                // Spawn at player position + offset
                float spawnZ = player.position.z + (nextCollectibleSpawnZ - virtualDistance);
                Vector3 spawnPosition = new Vector3(xPosition, 1f, spawnZ);

                GameObject collectible = ObjectPooler.Instance?.SpawnFromPool("Collectible", spawnPosition, Quaternion.identity);

                if (collectible != null)
                {
                    collectiblesSpawned++;
                    nextCollectibleSpawnZ += Random.Range(currentConfig.MinCollectibleSpacing, currentConfig.MaxCollectibleSpacing);
                }
            }
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
