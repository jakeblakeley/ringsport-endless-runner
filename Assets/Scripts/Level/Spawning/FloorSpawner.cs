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

        // Floors are pooled per prefab (like scenery): ~10 rows live at once,
        // three Instantiates per row for a whole level leaked hundreds of dead
        // GameObjects into the pooler and cost a Destroy burst per level start.
        private const int MainFloorPoolSize = 20;
        private const int SideFloorPoolSize = 40;
        private const int FinishFloorPoolSize = 2;
        private string mainFloorTag;
        private string sideFloorTag;
        private string finishFloorTag;

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
            this.sideFloorTag = EnsureFloorPool(prefab, SideFloorPoolSize);
        }

        /// <summary>
        /// Set the main floor prefab (can be changed per level based on location)
        /// </summary>
        public void SetMainFloorPrefab(GameObject prefab)
        {
            this.mainFloorPrefab = prefab;
            this.mainFloorTag = EnsureFloorPool(prefab, MainFloorPoolSize);
        }

        /// <summary>
        /// Set the finish line floor prefab (can be changed per level based on location)
        /// </summary>
        public void SetFinishLineFloorPrefab(GameObject prefab)
        {
            this.finishLineFloorPrefab = prefab;
            this.finishFloorTag = EnsureFloorPool(prefab, FinishFloorPoolSize);
        }

        /// <summary>
        /// Location-scoped pool per floor prefab (pools persist for the session,
        /// mirroring the scenery pools). Returns the pool tag, or null.
        /// </summary>
        private static string EnsureFloorPool(GameObject prefab, int size)
        {
            if (prefab == null || ObjectPooler.Instance == null)
                return null;
            string tag = "Floor_" + prefab.name;
            ObjectPooler.Instance.CreatePoolIfNeeded(tag, prefab, size);
            return tag;
        }

        /// <summary>
        /// Spawn a floor from its pool, falling back to Instantiate if the pool
        /// is missing or exhausted (fallback instances just park when despawned,
        /// matching the old behavior).
        /// </summary>
        private static GameObject SpawnFloorInstance(string tag, GameObject prefab, Vector3 position, Quaternion rotation)
        {
            GameObject instance = null;
            if (tag != null && ObjectPooler.Instance != null)
                instance = ObjectPooler.Instance.SpawnFromPool(tag, position, rotation);
            if (instance == null && prefab != null)
            {
                GameLog.Warn($"Floor pool '{tag}' unavailable/exhausted - instantiating {prefab.name}");
                instance = Object.Instantiate(prefab, position, rotation);
            }
            return instance;
        }

        /// <summary>
        /// Initialize floor spawning for a new level
        /// </summary>
        public void Initialize()
        {
            // Return the previous level's floors to their pools (idempotent:
            // GenerateLevel's ClearAllPools has usually done this already)
            if (finishLineFloorInstance != null)
            {
                ObjectPooler.Instance?.ReturnToPool(finishLineFloorInstance);
                finishLineFloorInstance = null;
            }

            foreach (var sideFloor in sideFloorInstances)
            {
                if (sideFloor != null)
                    ObjectPooler.Instance?.ReturnToPool(sideFloor);
            }
            sideFloorInstances.Clear();

            foreach (var mainFloor in mainFloorInstances)
            {
                if (mainFloor != null)
                    ObjectPooler.Instance?.ReturnToPool(mainFloor);
            }
            mainFloorInstances.Clear();

            // Start floor at 0, so first tile spawns at world Z = 0
            nextFloorSpawnZ = 0f;
            hasSpawnedFinishLine = false;
            finishLineSpawnZ = -1f;
            GameLog.Info($"Virtual distance reset to 0. Floor will start spawning from Virtual Z: {nextFloorSpawnZ}");
            GameLog.Info($"With floorTileSpacing={floorTileSpacing}, floors should spawn at: 0, {floorTileSpacing}, {floorTileSpacing*2}, {floorTileSpacing*3}, etc.");
        }

        /// <summary>
        /// Set the position where the finish line floor should spawn
        /// </summary>
        public void SetFinishLinePosition(float endGameDespawnDistance)
        {
            // Calculate the finish line spawn position based on current player position + distance
            finishLineSpawnZ = context.VirtualDistance + endGameDespawnDistance;
            GameLog.Info($"Finish line floor will spawn at Virtual Z: {finishLineSpawnZ:F2}");
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
                // Anchor to world origin (0,0,0) - first floor edge starts at Z=0
                float spawnZ = (nextFloorSpawnZ - context.VirtualDistance) + (floorTileLength / 2f);
                Vector3 spawnPosition = new Vector3(0f, 0f, spawnZ);

                GameObject floorTile = null;

                if (shouldSpawnFinishLine)
                {
                    // Spawn finish line floor from its pool, raised to avoid z-fighting.
                    // 0.03 rather than 0.01: the tile spawns ~80+ units out where the
                    // arc dips it heavily and mobile depth precision is at its worst.
                    if (finishLineFloorPrefab != null)
                    {
                        Vector3 finishLinePosition = new Vector3(spawnPosition.x, spawnPosition.y + 0.03f, spawnPosition.z);
                        floorTile = SpawnFloorInstance(finishFloorTag, finishLineFloorPrefab, finishLinePosition, Quaternion.identity);
                        floorTile.transform.localScale = Vector3.one * floorScale;
                        finishLineFloorInstance = floorTile; // Save reference for cleanup
                        hasSpawnedFinishLine = true;
                        GameLog.Info($"Finish line floor spawned at World Z: {spawnZ:F2}, Virtual Z: {nextFloorSpawnZ:F2}");
                    }
                    else
                    {
                        GameLog.Error("Finish line floor prefab is not assigned!");
                    }
                }
                else
                {
                    // Spawn regular floor from its pool
                    if (mainFloorPrefab != null)
                    {
                        floorTile = SpawnFloorInstance(mainFloorTag, mainFloorPrefab, spawnPosition, Quaternion.identity);
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

                    GameLog.Info($"Floor spawned at World Z: {spawnZ:F2}, Virtual Z: {nextFloorSpawnZ:F2}, TileLength: {floorTileLength}, Spacing: {floorTileSpacing}, extends from {spawnZ - floorTileLength/2f:F2} to {spawnZ + floorTileLength/2f:F2}");

                    // Increment by spacing distance (not tile length) for next floor
                    nextFloorSpawnZ += floorTileSpacing;
                    spawnAttempts = 0;
                    floorsSpawnedThisFrame++;

                    GameLog.Info($"Next floor will spawn at Virtual Z: {nextFloorSpawnZ:F2}");

                    // Stop spawning floors after finish line
                    if (hasSpawnedFinishLine)
                    {
                        break;
                    }
                }
                else
                {
                    // Pool exhausted, stop trying
                    GameLog.Warn($"Floor pool exhausted at spawn attempt {spawnAttempts}");
                    spawnAttempts++;
                }
            }

            if (floorsSpawnedThisFrame > 0 && Time.frameCount % 60 == 0)
            {
                GameLog.Info($"Spawned {floorsSpawnedThisFrame} floor tiles. Virtual distance: {context.VirtualDistance:F2}");
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
            GameObject leftFloor = SpawnFloorInstance(sideFloorTag, sideFloorPrefab, leftPosition, Quaternion.identity);
            leftFloor.transform.localScale = Vector3.one * floorScale;
            sideFloorInstances.Add(leftFloor);
            despawnManager.RegisterFloorTile(leftFloor);

            // Spawn scenery on left side floor
            scenerySpawner?.SpawnSceneryOnFloor(leftPosition, isRightSide: false);

            // Spawn right side floor (rotated 180 degrees on Y axis)
            Vector3 rightPosition = new Vector3(floorTileLength, mainFloorPosition.y, mainFloorPosition.z);
            Quaternion rightRotation = Quaternion.Euler(0f, 180f, 0f);
            GameObject rightFloor = SpawnFloorInstance(sideFloorTag, sideFloorPrefab, rightPosition, rightRotation);
            rightFloor.transform.localScale = Vector3.one * floorScale;
            sideFloorInstances.Add(rightFloor);
            despawnManager.RegisterFloorTile(rightFloor);

            // Spawn scenery on right side floor
            scenerySpawner?.SpawnSceneryOnFloor(rightPosition, isRightSide: true);
        }
    }
}
