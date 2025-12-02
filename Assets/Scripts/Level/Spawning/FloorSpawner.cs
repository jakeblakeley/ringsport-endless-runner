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
        private GameObject finishLineFloorPrefab;
        private bool hasSpawnedFinishLine = false;
        private float finishLineSpawnZ = -1f;
        private GameObject finishLineFloorInstance = null; // Track the instantiated finish line floor

        private SpawnContext context;
        private DespawnManager despawnManager;

        public FloorSpawner(SpawnContext context, DespawnManager despawnManager, float floorTileLength, float floorTileSpacing, GameObject finishLineFloorPrefab)
        {
            this.context = context;
            this.despawnManager = despawnManager;
            this.floorTileLength = floorTileLength;
            this.floorTileSpacing = floorTileSpacing;
            this.finishLineFloorPrefab = finishLineFloorPrefab;
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
                Vector3 spawnPosition = new Vector3(0f, -0.5f, spawnZ);

                GameObject floorTile = null;

                if (shouldSpawnFinishLine)
                {
                    // Spawn finish line floor from prefab
                    if (finishLineFloorPrefab != null)
                    {
                        floorTile = Object.Instantiate(finishLineFloorPrefab, spawnPosition, Quaternion.identity);
                        floorTile.transform.localScale = new Vector3(floorTileLength, 1f, floorTileLength);
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
                    // Spawn regular floor from pool
                    floorTile = ObjectPooler.Instance?.SpawnFromPool("Floor", spawnPosition, Quaternion.identity);
                }

                if (floorTile != null)
                {
                    // Scale the floor tile to match the tile length (visual size) if not finish line
                    if (!shouldSpawnFinishLine)
                    {
                        floorTile.transform.localScale = new Vector3(floorTileLength, 1f, floorTileLength);
                    }

                    // Register with despawn manager (only regular floors, finish line stays)
                    if (!shouldSpawnFinishLine)
                    {
                        despawnManager.RegisterFloorTile(floorTile);
                    }

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
    }
}
