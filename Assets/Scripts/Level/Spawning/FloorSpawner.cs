using System.Collections.Generic;
using UnityEngine;
using RingSport.Core;

namespace RingSport.Level.Spawning
{
    /// <summary>
    /// Handles floor tile spawning ahead of the player
    /// </summary>
    public class FloorSpawner
    {
        private float nextFloorSpawnZ;
        private float floorTileLength;
        private float floorTileSpacing;
        private float floorScale;
        private GameObject finishLineFloorPrefab;
        private GameObject sideFloorPrefab;
        private GameObject mainFloorPrefab;
        private bool hasSpawnedFinishLine = false;
        private float finishLineSpawnZ = -1f;
        private GameObject finishLineFloorInstance = null; // Track the instantiated finish line floor
        private List<GameObject> sideFloorInstances = new List<GameObject>(); // Track side floor instances
        private List<GameObject> mainFloorInstances = new List<GameObject>(); // Track main floor instances

        private SpawnContext context;
        private DespawnManager despawnManager;
        private ScenerySpawner scenerySpawner;

        public FloorSpawner(SpawnContext context, DespawnManager despawnManager, float floorTileLength, float floorTileSpacing, float floorScale, GameObject finishLineFloorPrefab, GameObject sideFloorPrefab = null)
        {
            this.context = context;
            this.despawnManager = despawnManager;
            this.floorTileLength = floorTileLength;
            this.floorTileSpacing = floorTileSpacing;
            this.floorScale = floorScale;
            this.finishLineFloorPrefab = finishLineFloorPrefab;
            this.sideFloorPrefab = sideFloorPrefab;

            // Create scenery spawner with floor dimensions
            this.scenerySpawner = new ScenerySpawner(despawnManager, floorTileLength, floorTileLength);
        }

        /// <summary>
        /// Configure scenery spawning from location config
        /// </summary>
        public void ConfigureScenery(LocationConfig locationConfig)
        {
            scenerySpawner?.Configure(locationConfig);
        }

        /// <summary>
        /// Set the side floor prefab (can be changed per level based on location)
        /// </summary>
        public void SetSideFloorPrefab(GameObject prefab)
        {
            this.sideFloorPrefab = prefab;
        }

        /// <summary>
        /// Set the main floor prefab (can be changed per level based on location)
        /// </summary>
        public void SetMainFloorPrefab(GameObject prefab)
        {
            this.mainFloorPrefab = prefab;
        }

        /// <summary>
        /// Set the finish line floor prefab (can be changed per level based on location)
        /// </summary>
        public void SetFinishLineFloorPrefab(GameObject prefab)
        {
            this.finishLineFloorPrefab = prefab;
        }

        /// <summary>
        /// Initialize floor spawning for a new level
        /// </summary>
        public void Initialize()
        {
            // Destroy previous finish line floor if it exists
            if (finishLineFloorInstance != null)
            {
                Object.Destroy(finishLineFloorInstance);
                finishLineFloorInstance = null;
                Debug.Log("Destroyed previous finish line floor");
            }

            // Destroy all previous side floor instances
            foreach (var sideFloor in sideFloorInstances)
            {
                if (sideFloor != null)
                {
                    Object.Destroy(sideFloor);
                }
            }
            sideFloorInstances.Clear();

            // Destroy all previous main floor instances
            foreach (var mainFloor in mainFloorInstances)
            {
                if (mainFloor != null)
                {
                    Object.Destroy(mainFloor);
                }
            }
            mainFloorInstances.Clear();

            // Start floor at 0, so first tile spawns at world Z = 0
            nextFloorSpawnZ = 0f;
            hasSpawnedFinishLine = false;
            finishLineSpawnZ = -1f;
            Debug.Log($"Virtual distance reset to 0. Floor will start spawning from Virtual Z: {nextFloorSpawnZ}");
            Debug.Log($"With floorTileSpacing={floorTileSpacing}, floors should spawn at: 0, {floorTileSpacing}, {floorTileSpacing*2}, {floorTileSpacing*3}, etc.");
        }

        /// <summary>
        /// Set the position where the finish line floor should spawn
        /// </summary>
        public void SetFinishLinePosition(float endGameDespawnDistance)
        {
            // Calculate the finish line spawn position based on current player position + distance
            finishLineSpawnZ = context.VirtualDistance + endGameDespawnDistance;
            Debug.Log($"Finish line floor will spawn at Virtual Z: {finishLineSpawnZ:F2}");
        }

        /// <summary>
        /// Spawn floor tiles ahead based on virtual distance
        /// </summary>
        public void SpawnFloor()
        {
            // Keep spawning floor tiles ahead based on virtual distance
            int spawnAttempts = 0;
            int maxAttempts = 10; // Prevent infinite loops
            int floorsSpawnedThisFrame = 0;

            while (nextFloorSpawnZ < context.VirtualDistance + context.SpawnDistance && spawnAttempts < maxAttempts)
            {
                // Check if we should spawn the finish line floor instead
                bool shouldSpawnFinishLine = !hasSpawnedFinishLine &&
                                            finishLineSpawnZ > 0 &&
                                            nextFloorSpawnZ >= finishLineSpawnZ;

                // Offset spawn position by half tile length so tiles are edge-to-edge
                // Note: Using floorTileLength for visual offset, floorTileSpacing for actual spacing
                float spawnZ = context.PlayerPosition.z + (nextFloorSpawnZ - context.VirtualDistance) + (floorTileLength / 2f);
                Vector3 spawnPosition = new Vector3(0f, 0f, spawnZ);

                GameObject floorTile = null;

                if (shouldSpawnFinishLine)
                {
                    // Spawn finish line floor from prefab (slightly higher to avoid z-fighting)
                    if (finishLineFloorPrefab != null)
                    {
                        Vector3 finishLinePosition = new Vector3(spawnPosition.x, spawnPosition.y + 0.01f, spawnPosition.z);
                        floorTile = Object.Instantiate(finishLineFloorPrefab, finishLinePosition, Quaternion.identity);
                        floorTile.transform.localScale = Vector3.one * floorScale;
                        finishLineFloorInstance = floorTile; // Save reference for cleanup
                        hasSpawnedFinishLine = true;
                        Debug.Log($"Finish line floor spawned at World Z: {spawnZ:F2}, Virtual Z: {nextFloorSpawnZ:F2}");
                    }
                    else
                    {
                        Debug.LogError("Finish line floor prefab is not assigned!");
                    }
                }
                else
                {
                    // Spawn regular floor from prefab
                    if (mainFloorPrefab != null)
                    {
                        floorTile = Object.Instantiate(mainFloorPrefab, spawnPosition, Quaternion.identity);
                        floorTile.transform.localScale = Vector3.one * floorScale;
                        mainFloorInstances.Add(floorTile);
                    }
                    else
                    {
                        // Fallback to object pooler if no prefab set
                        floorTile = ObjectPooler.Instance?.SpawnFromPool("Floor", spawnPosition, Quaternion.identity);
                    }
                }

                if (floorTile != null)
                {
                    // Register with despawn manager (only regular floors, finish line stays)
                    if (!shouldSpawnFinishLine)
                    {
                        despawnManager.RegisterFloorTile(floorTile);
                    }

                    // Spawn side floors (visual only) for all floors including finish line
                    SpawnSideFloors(spawnPosition);

                    Debug.Log($"Floor spawned at World Z: {spawnZ:F2}, Virtual Z: {nextFloorSpawnZ:F2}, TileLength: {floorTileLength}, Spacing: {floorTileSpacing}, extends from {spawnZ - floorTileLength/2f:F2} to {spawnZ + floorTileLength/2f:F2}");

                    // Increment by spacing distance (not tile length) for next floor
                    nextFloorSpawnZ += floorTileSpacing;
                    spawnAttempts = 0;
                    floorsSpawnedThisFrame++;

                    Debug.Log($"Next floor will spawn at Virtual Z: {nextFloorSpawnZ:F2}");

                    // Stop spawning floors after finish line
                    if (hasSpawnedFinishLine)
                    {
                        break;
                    }
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
                Debug.Log($"Spawned {floorsSpawnedThisFrame} floor tiles. Virtual distance: {context.VirtualDistance:F2}");
            }
        }

        /// <summary>
        /// Get the next floor spawn Z position (for debugging)
        /// </summary>
        public float GetNextFloorSpawnZ() => nextFloorSpawnZ;

        /// <summary>
        /// Spawn visual side floors to the left and right of the main floor
        /// </summary>
        private void SpawnSideFloors(Vector3 mainFloorPosition)
        {
            if (sideFloorPrefab == null)
                return;

            // Spawn left side floor (no rotation)
            Vector3 leftPosition = new Vector3(-floorTileLength, mainFloorPosition.y, mainFloorPosition.z);
            GameObject leftFloor = Object.Instantiate(sideFloorPrefab, leftPosition, Quaternion.identity);
            leftFloor.transform.localScale = Vector3.one * floorScale;
            sideFloorInstances.Add(leftFloor);
            despawnManager.RegisterFloorTile(leftFloor);

            // Spawn scenery on left side floor
            scenerySpawner?.SpawnSceneryOnFloor(leftPosition, isRightSide: false);

            // Spawn right side floor (rotated 180 degrees on Y axis)
            Vector3 rightPosition = new Vector3(floorTileLength, mainFloorPosition.y, mainFloorPosition.z);
            Quaternion rightRotation = Quaternion.Euler(0f, 180f, 0f);
            GameObject rightFloor = Object.Instantiate(sideFloorPrefab, rightPosition, rightRotation);
            rightFloor.transform.localScale = Vector3.one * floorScale;
            sideFloorInstances.Add(rightFloor);
            despawnManager.RegisterFloorTile(rightFloor);

            // Spawn scenery on right side floor
            scenerySpawner?.SpawnSceneryOnFloor(rightPosition, isRightSide: true);
        }
    }
}
