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
        private List<GameObject> activeScenery = new List<GameObject>();

        private float despawnDistance;
        private float endGameDespawnDistance;

        public DespawnManager(float despawnDistance, float endGameDespawnDistance)
        {
            this.despawnDistance = despawnDistance;
            this.endGameDespawnDistance = endGameDespawnDistance;
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
        /// Register a spawned scenery object
        /// </summary>
        public void RegisterScenery(GameObject scenery)
        {
            if (scenery != null && !activeScenery.Contains(scenery))
            {
                activeScenery.Add(scenery);
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

            // Despawn scenery
            for (int i = activeScenery.Count - 1; i >= 0; i--)
            {
                if (activeScenery[i] != null && activeScenery[i].transform.position.z < despawnZ)
                {
                    ObjectPooler.Instance.ReturnToPool(activeScenery[i]);
                    activeScenery.RemoveAt(i);
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
        /// Despawn obstacles ahead of the player beyond a certain distance
        /// FAIRNESS: Prevents unfair hits from obstacles too far ahead during level ending
        /// </summary>
        public void DespawnObstaclesAheadOfPlayer(Vector3 playerPosition)
        {
            if (ObjectPooler.Instance == null)
                return;

            float maxObstacleZ = playerPosition.z + endGameDespawnDistance;

            // Despawn obstacles beyond the max distance ahead
            for (int i = activeObstacles.Count - 1; i >= 0; i--)
            {
                if (activeObstacles[i] != null && activeObstacles[i].transform.position.z > maxObstacleZ)
                {
                    ObjectPooler.Instance.ReturnToPool(activeObstacles[i]);
                    activeObstacles.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Despawn collectibles ahead of the player beyond a certain distance
        /// Used during level ending to create smooth transition
        /// </summary>
        public void DespawnCollectiblesAheadOfPlayer(Vector3 playerPosition)
        {
            if (ObjectPooler.Instance == null)
                return;

            float maxCollectibleZ = playerPosition.z + endGameDespawnDistance;

            // Despawn collectibles beyond the max distance ahead
            for (int i = activeCollectibles.Count - 1; i >= 0; i--)
            {
                if (activeCollectibles[i] != null && activeCollectibles[i].transform.position.z > maxCollectibleZ)
                {
                    ObjectPooler.Instance.ReturnToPool(activeCollectibles[i]);
                    activeCollectibles.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Clear all tracked objects (used on level reset)
        /// </summary>
        public void Clear()
        {
            activeObstacles.Clear();
            activeCollectibles.Clear();
            activeFloorTiles.Clear();
            activeScenery.Clear();
        }

        /// <summary>
        /// Get count of active objects (for debugging)
        /// </summary>
        public int GetActiveObstacleCount() => activeObstacles.Count;
        public int GetActiveCollectibleCount() => activeCollectibles.Count;
        public int GetActiveFloorTileCount() => activeFloorTiles.Count;
        public int GetActiveSceneryCount() => activeScenery.Count;
    }
}
