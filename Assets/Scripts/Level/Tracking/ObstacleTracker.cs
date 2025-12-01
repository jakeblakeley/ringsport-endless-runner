using UnityEngine;
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

    /// <summary>
    /// Tracks spawned obstacles for spatial queries and clearance validation
    /// </summary>
    public class ObstacleTracker
    {
        private List<ObstacleData> obstaclePositions = new List<ObstacleData>();

        /// <summary>
        /// Add a newly spawned obstacle to the tracker
        /// </summary>
        public void AddObstacle(ObstacleData obstacle)
        {
            obstaclePositions.Add(obstacle);
        }

        /// <summary>
        /// Check if there are any obstacles in the specified lane behind the given Z position
        /// </summary>
        public bool HasObstacleInLaneBehind(int lane, float zPosition, float behindDistance)
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

        /// <summary>
        /// Check if there are any obstacles in the specified lane ahead of the given Z position
        /// FAIRNESS: Used to prevent coin trains from leading into obstacles
        /// </summary>
        public bool HasObstacleInLaneAhead(int lane, float zPosition, float aheadDistance)
        {
            foreach (ObstacleData obstacle in obstaclePositions)
            {
                // Check if obstacle is in the same lane
                if (obstacle.lane == lane)
                {
                    // Check if obstacle is within the distance ahead of this position
                    float distanceAhead = obstacle.zPosition - zPosition;
                    if (distanceAhead > 0 && distanceAhead <= aheadDistance)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Check if a collectible spawn position is within the specified distance of any obstacle
        /// Returns the obstacle data if close, null otherwise
        /// </summary>
        public ObstacleData? GetNearbyObstacle(float zPosition, float maxDistance)
        {
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
        /// Check for upcoming jumpable obstacles within arc range
        /// Returns the obstacle data if found, null otherwise
        /// </summary>
        public ObstacleData? GetUpcomingJumpableObstacle(float zPosition, float lookAheadDistance = 8f)
        {
            foreach (ObstacleData obstacle in obstaclePositions)
            {
                // Check if obstacle is ahead of current position
                float distanceAhead = obstacle.zPosition - zPosition;

                // Only consider obstacles within the look-ahead range
                if (distanceAhead > 0 && distanceAhead <= lookAheadDistance)
                {
                    // Check if it's a jumpable obstacle type
                    if (obstacle.obstacleType == "ObstacleJump" ||
                        obstacle.obstacleType == "ObstaclePalisade" ||
                        obstacle.obstacleType == "ObstacleBroadJump")
                    {
                        return obstacle;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Clean up obstacle data that is no longer needed
        /// FAIRNESS: More aggressive cleanup to prevent unbounded growth
        /// </summary>
        public void Cleanup(float virtualDistance)
        {
            // Remove obstacles that are far behind the current position
            float cleanupThreshold = virtualDistance - 10f;
            int removedCount = obstaclePositions.RemoveAll(obstacle => obstacle.zPosition < cleanupThreshold);

            // Periodic deep cleanup every 100 virtual units to prevent unbounded growth
            if (virtualDistance % 100f < 1f && obstaclePositions.Count > 50)
            {
                // Keep only obstacles within 30 units of current position
                obstaclePositions.RemoveAll(obstacle => obstacle.zPosition < virtualDistance - 30f);
                Debug.Log($"Deep cleanup performed: obstacle list size = {obstaclePositions.Count}");
            }
        }

        /// <summary>
        /// Clear all tracked obstacles (used on level reset)
        /// </summary>
        public void Clear()
        {
            obstaclePositions.Clear();
        }

        /// <summary>
        /// Get the number of tracked obstacles (for debugging)
        /// </summary>
        public int Count => obstaclePositions.Count;
    }
}
