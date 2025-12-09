using System.Collections.Generic;
using UnityEngine;
using RingSport.Core;

namespace RingSport.Level.Spawning
{
    /// <summary>
    /// Handles spawning of scenery objects on side floors using Poisson Disk Sampling
    /// </summary>
    public class ScenerySpawner
    {
        private GameObject[] sceneryPrefabs;
        private int minSceneryPerFloor;
        private int maxSceneryPerFloor;
        private float minDistance;
        private int poolSize;
        private float floorWidth;
        private float floorLength;

        private DespawnManager despawnManager;
        private bool isInitialized = false;

        // Poisson Disk Sampling constants
        private const int MAX_ATTEMPTS_PER_POINT = 30;

        public ScenerySpawner(DespawnManager despawnManager, float floorWidth, float floorLength)
        {
            this.despawnManager = despawnManager;
            this.floorWidth = floorWidth;
            this.floorLength = floorLength;
        }

        /// <summary>
        /// Configure scenery settings from LocationConfig
        /// </summary>
        public void Configure(LocationConfig locationConfig)
        {
            if (locationConfig == null || locationConfig.SceneryPrefabs == null || locationConfig.SceneryPrefabs.Length == 0)
            {
                sceneryPrefabs = null;
                isInitialized = false;
                return;
            }

            sceneryPrefabs = locationConfig.SceneryPrefabs;
            minSceneryPerFloor = locationConfig.MinSceneryPerFloor;
            maxSceneryPerFloor = locationConfig.MaxSceneryPerFloor;
            minDistance = locationConfig.SceneryMinDistance;
            poolSize = locationConfig.SceneryPoolSize;

            // Create pools for each scenery prefab
            if (ObjectPooler.Instance != null)
            {
                for (int i = 0; i < sceneryPrefabs.Length; i++)
                {
                    if (sceneryPrefabs[i] != null)
                    {
                        string tag = GetSceneryPoolTag(i);
                        ObjectPooler.Instance.CreatePoolIfNeeded(tag, sceneryPrefabs[i], poolSize);
                    }
                }
            }

            isInitialized = true;
            Debug.Log($"ScenerySpawner configured with {sceneryPrefabs.Length} prefab types, {minSceneryPerFloor}-{maxSceneryPerFloor} per floor");
        }

        /// <summary>
        /// Spawn scenery on a side floor at the given position
        /// </summary>
        public void SpawnSceneryOnFloor(Vector3 floorCenter, bool isRightSide)
        {
            if (!isInitialized || sceneryPrefabs == null || sceneryPrefabs.Length == 0)
                return;

            if (ObjectPooler.Instance == null)
                return;

            int count = Random.Range(minSceneryPerFloor, maxSceneryPerFloor + 1);
            if (count <= 0)
                return;

            // Generate Poisson Disk Sampled positions
            List<Vector2> positions = GeneratePoissonDiskPoints(count, minDistance);

            foreach (Vector2 localPos in positions)
            {
                // Convert local position to world position
                // localPos is in range [0,1] for x and z within the floor bounds
                float offsetX = (localPos.x - 0.5f) * floorWidth;
                float offsetZ = (localPos.y - 0.5f) * floorLength;

                Vector3 worldPos = new Vector3(
                    floorCenter.x + offsetX,
                    floorCenter.y,
                    floorCenter.z + offsetZ
                );

                // Pick a random scenery prefab
                int prefabIndex = Random.Range(0, sceneryPrefabs.Length);
                string tag = GetSceneryPoolTag(prefabIndex);

                // Random rotation around Y axis
                Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

                GameObject sceneryObj = ObjectPooler.Instance.SpawnFromPool(tag, worldPos, rotation);
                if (sceneryObj != null)
                {
                    despawnManager.RegisterScenery(sceneryObj);
                }
            }
        }

        /// <summary>
        /// Generate points using Poisson Disk Sampling for natural distribution
        /// Returns points in normalized [0,1] space
        /// </summary>
        private List<Vector2> GeneratePoissonDiskPoints(int targetCount, float minDist)
        {
            List<Vector2> points = new List<Vector2>();
            List<Vector2> activeList = new List<Vector2>();

            // Normalize min distance to [0,1] space
            float normalizedMinDist = Mathf.Min(minDist / floorWidth, minDist / floorLength);

            // Cell size for spatial grid (minDist / sqrt(2) ensures at most one point per cell)
            float cellSize = normalizedMinDist / Mathf.Sqrt(2f);
            int gridWidth = Mathf.CeilToInt(1f / cellSize);
            int gridHeight = Mathf.CeilToInt(1f / cellSize);

            // Grid to track which cells have points (-1 = empty)
            int[,] grid = new int[gridWidth, gridHeight];
            for (int x = 0; x < gridWidth; x++)
                for (int y = 0; y < gridHeight; y++)
                    grid[x, y] = -1;

            // Start with a random point
            Vector2 firstPoint = new Vector2(Random.Range(0.1f, 0.9f), Random.Range(0.1f, 0.9f));
            points.Add(firstPoint);
            activeList.Add(firstPoint);

            int gx = Mathf.FloorToInt(firstPoint.x / cellSize);
            int gy = Mathf.FloorToInt(firstPoint.y / cellSize);
            if (gx >= 0 && gx < gridWidth && gy >= 0 && gy < gridHeight)
                grid[gx, gy] = 0;

            // Generate additional points
            while (activeList.Count > 0 && points.Count < targetCount)
            {
                int randomIndex = Random.Range(0, activeList.Count);
                Vector2 point = activeList[randomIndex];
                bool foundValid = false;

                for (int attempt = 0; attempt < MAX_ATTEMPTS_PER_POINT; attempt++)
                {
                    // Generate random point in annulus between minDist and 2*minDist
                    float angle = Random.Range(0f, Mathf.PI * 2f);
                    float distance = Random.Range(normalizedMinDist, normalizedMinDist * 2f);

                    Vector2 newPoint = new Vector2(
                        point.x + distance * Mathf.Cos(angle),
                        point.y + distance * Mathf.Sin(angle)
                    );

                    // Check bounds (with margin to avoid edge spawning)
                    if (newPoint.x < 0.05f || newPoint.x > 0.95f ||
                        newPoint.y < 0.05f || newPoint.y > 0.95f)
                        continue;

                    // Check if too close to any existing point using grid
                    if (IsValidPoint(newPoint, normalizedMinDist, points, grid, cellSize, gridWidth, gridHeight))
                    {
                        points.Add(newPoint);
                        activeList.Add(newPoint);

                        int ngx = Mathf.FloorToInt(newPoint.x / cellSize);
                        int ngy = Mathf.FloorToInt(newPoint.y / cellSize);
                        if (ngx >= 0 && ngx < gridWidth && ngy >= 0 && ngy < gridHeight)
                            grid[ngx, ngy] = points.Count - 1;

                        foundValid = true;
                        break;
                    }
                }

                if (!foundValid)
                {
                    activeList.RemoveAt(randomIndex);
                }

                // Stop if we have enough points
                if (points.Count >= targetCount)
                    break;
            }

            return points;
        }

        /// <summary>
        /// Check if a point is valid (not too close to existing points)
        /// </summary>
        private bool IsValidPoint(Vector2 point, float minDist, List<Vector2> points,
            int[,] grid, float cellSize, int gridWidth, int gridHeight)
        {
            int gx = Mathf.FloorToInt(point.x / cellSize);
            int gy = Mathf.FloorToInt(point.y / cellSize);

            // Check neighboring cells (in a 5x5 area to be safe)
            int searchRadius = 2;
            for (int dx = -searchRadius; dx <= searchRadius; dx++)
            {
                for (int dy = -searchRadius; dy <= searchRadius; dy++)
                {
                    int nx = gx + dx;
                    int ny = gy + dy;

                    if (nx < 0 || nx >= gridWidth || ny < 0 || ny >= gridHeight)
                        continue;

                    int pointIndex = grid[nx, ny];
                    if (pointIndex >= 0 && pointIndex < points.Count)
                    {
                        float sqrDist = (points[pointIndex] - point).sqrMagnitude;
                        if (sqrDist < minDist * minDist)
                            return false;
                    }
                }
            }

            return true;
        }

        private string GetSceneryPoolTag(int prefabIndex)
        {
            return $"Scenery_{prefabIndex}";
        }
    }
}
