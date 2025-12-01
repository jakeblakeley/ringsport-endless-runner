using UnityEngine;
using System.Collections.Generic;
using RingSport.Core;

namespace RingSport.Level
{
    /// <summary>
    /// Manages despawning of pooled objects behind the player
    /// </summary>
    public class DespawnManager
    {
        private List<GameObject> activeObstacles = new List<GameObject>();
        private List<GameObject> activeCollectibles = new List<GameObject>();
        private List<GameObject> activeFloorTiles = new List<GameObject>();

        private float despawnDistance;

        public DespawnManager(float despawnDistance)
        {
            this.despawnDistance = despawnDistance;
        }

        /// <summary>
        /// Register a spawned obstacle
        /// </summary>
        public void RegisterObstacle(GameObject obstacle)
        {
            if (obstacle != null && !activeObstacles.Contains(obstacle))
            {
                activeObstacles.Add(obstacle);
            }
        }

        /// <summary>
        /// Register a spawned collectible
        /// </summary>
        public void RegisterCollectible(GameObject collectible)
        {
            if (collectible != null && !activeCollectibles.Contains(collectible))
            {
                activeCollectibles.Add(collectible);
            }
        }

        /// <summary>
        /// Register a spawned floor tile
        /// </summary>
        public void RegisterFloorTile(GameObject floorTile)
        {
            if (floorTile != null && !activeFloorTiles.Contains(floorTile))
            {
                activeFloorTiles.Add(floorTile);
            }
        }

        /// <summary>
        /// Despawn objects behind the player
        /// </summary>
        public void DespawnBehindPlayer(Vector3 playerPosition)
        {
            if (ObjectPooler.Instance == null)
                return;

            float despawnZ = playerPosition.z + despawnDistance;

            // Despawn obstacles
            for (int i = activeObstacles.Count - 1; i >= 0; i--)
            {
                if (activeObstacles[i] != null && activeObstacles[i].transform.position.z < despawnZ)
                {
                    ObjectPooler.Instance.ReturnToPool(activeObstacles[i]);
                    activeObstacles.RemoveAt(i);
                }
            }

            // Despawn collectibles
            for (int i = activeCollectibles.Count - 1; i >= 0; i--)
            {
                if (activeCollectibles[i] != null && activeCollectibles[i].transform.position.z < despawnZ)
                {
                    ObjectPooler.Instance.ReturnToPool(activeCollectibles[i]);
                    activeCollectibles.RemoveAt(i);
                }
            }

            // Despawn floor tiles
            for (int i = activeFloorTiles.Count - 1; i >= 0; i--)
            {
                if (activeFloorTiles[i] != null && activeFloorTiles[i].transform.position.z < despawnZ)
                {
                    ObjectPooler.Instance.ReturnToPool(activeFloorTiles[i]);
                    activeFloorTiles.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Despawn all obstacles (used when level is ending)
        /// FAIRNESS: Prevents unfair hits from obstacles that spawned earlier
        /// </summary>
        public void DespawnAllObstacles()
        {
            if (ObjectPooler.Instance == null)
                return;

            Debug.Log("Level ending - despawning all obstacles");

            // Despawn all active obstacles
            foreach (GameObject obj in activeObstacles)
            {
                if (obj != null)
                {
                    ObjectPooler.Instance.ReturnToPool(obj);
                }
            }

            activeObstacles.Clear();
        }

        /// <summary>
        /// Clear all tracked objects (used on level reset)
        /// </summary>
        public void Clear()
        {
            activeObstacles.Clear();
            activeCollectibles.Clear();
            activeFloorTiles.Clear();
        }

        /// <summary>
        /// Get count of active objects (for debugging)
        /// </summary>
        public int GetActiveObstacleCount() => activeObstacles.Count;
        public int GetActiveCollectibleCount() => activeCollectibles.Count;
        public int GetActiveFloorTileCount() => activeFloorTiles.Count;
    }
}
