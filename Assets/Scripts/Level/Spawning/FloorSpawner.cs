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

        private SpawnContext context;
        private DespawnManager despawnManager;

        public FloorSpawner(SpawnContext context, DespawnManager despawnManager, float floorTileLength, float floorTileSpacing)
        {
            this.context = context;
            this.despawnManager = despawnManager;
            this.floorTileLength = floorTileLength;
            this.floorTileSpacing = floorTileSpacing;
        }

        /// <summary>
        /// Initialize floor spawning for a new level
        /// </summary>
        public void Initialize()
        {
            // Start floor at 0, so first tile spawns at world Z = 0
            nextFloorSpawnZ = 0f;
            Debug.Log($"Virtual distance reset to 0. Floor will start spawning from Virtual Z: {nextFloorSpawnZ}");
            Debug.Log($"With floorTileSpacing={floorTileSpacing}, floors should spawn at: 0, {floorTileSpacing}, {floorTileSpacing*2}, {floorTileSpacing*3}, etc.");
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
                // Offset spawn position by half tile length so tiles are edge-to-edge
                // Note: Using floorTileLength for visual offset, floorTileSpacing for actual spacing
                float spawnZ = context.PlayerPosition.z + (nextFloorSpawnZ - context.VirtualDistance) + (floorTileLength / 2f);
                Vector3 spawnPosition = new Vector3(0f, -0.5f, spawnZ);

                GameObject floorTile = ObjectPooler.Instance?.SpawnFromPool("Floor", spawnPosition, Quaternion.identity);

                if (floorTile != null)
                {
                    // Scale the floor tile to match the tile length (visual size)
                    floorTile.transform.localScale = new Vector3(floorTileLength, 1f, floorTileLength);

                    // Register with despawn manager
                    despawnManager.RegisterFloorTile(floorTile);

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
                Debug.Log($"Spawned {floorsSpawnedThisFrame} floor tiles. Virtual distance: {context.VirtualDistance:F2}");
            }
        }

        /// <summary>
        /// Get the next floor spawn Z position (for debugging)
        /// </summary>
        public float GetNextFloorSpawnZ() => nextFloorSpawnZ;
    }
}
