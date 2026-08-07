using UnityEngine;
using System.Collections.Generic;
using RingSport.Core;

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

        /// <summary>
        /// Contact with this obstacle is game over at ANY height - there is no
        /// "cleared it high enough" grace the way a hurdle has. The dog can
        /// still hop clean over both (barrel top 1.0u, pylon 0.9u, both under
        /// the trigger sphere at the top of the jump arc), but the window is
        /// tighter and a graze kills. Callers use this to keep flat coin lines
        /// out of these lanes.
        /// </summary>
        public bool IsInstantDeath()
        {
            return obstacleType == PoolTags.ObstacleAvoid || obstacleType == PoolTags.ObstaclePylon;
        }

        /// <summary>
        /// Whether a coin arc may be drawn over this obstacle. Every type
        /// qualifies: the arc traces the DOG's jump, not the obstacle, so a
        /// short barrel gets the same shape as a hurdle. The spawner gates how
        /// often the instant-death types actually get one.
        /// </summary>
        public bool CanHaveCoinArc()
        {
            return obstacleType == PoolTags.ObstacleJump ||
                   obstacleType == PoolTags.ObstaclePalisade ||
                   obstacleType == PoolTags.ObstacleBroadJump ||
                   obstacleType == PoolTags.ObstacleAvoid ||
                   obstacleType == PoolTags.ObstaclePylon;
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
        /// Z of the furthest-along palisade tracked in this lane, or
        /// float.MinValue if the lane holds none.
        ///
        /// FAIRNESS: a palisade is 2.7u of solid wall - twice the height of any
        /// other obstacle - so from the chase camera it casts a sight shadow
        /// down its own lane and hides whatever is parked behind it until the
        /// player is over the top. The spawner reserves a longer clear run
        /// behind a palisade than behind anything else; this is the query it
        /// measures that reservation from.
        /// </summary>
        public float FrontmostPalisadeZ(int lane)
        {
            float frontmost = float.MinValue;

            foreach (ObstacleData obstacle in obstaclePositions)
            {
                if (obstacle.lane == lane &&
                    obstacle.obstacleType == PoolTags.ObstaclePalisade &&
                    obstacle.zPosition > frontmost)
                {
                    frontmost = obstacle.zPosition;
                }
            }

            return frontmost;
        }

        /// <summary>
        /// Z of the furthest-along obstacle tracked in this lane IF it is a
        /// jumpable one, or float.MinValue otherwise (including an empty lane).
        ///
        /// FAIRNESS: a hurdle is answered by jumping it, and the player who
        /// does that is committed to its lane from takeoff to touchdown. An
        /// instant-death obstacle parked in the same lane right behind it has
        /// to be far enough out that they can land and still get clear; this
        /// is the query the spawner measures that reservation from.
        ///
        /// Only the FRONTMOST obstacle counts, and only if it is jumpable: a
        /// barrel already standing further along drove the player out of this
        /// lane, so nobody is left in it to owe the reservation to.
        /// </summary>
        public float FrontmostJumpableZ(int lane)
        {
            float frontmost = float.MinValue;
            bool jumpable = false;

            foreach (ObstacleData obstacle in obstaclePositions)
            {
                if (obstacle.lane == lane && obstacle.zPosition > frontmost)
                {
                    frontmost = obstacle.zPosition;
                    jumpable = !obstacle.IsInstantDeath();
                }
            }

            return jumpable ? frontmost : float.MinValue;
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
        /// Nearest obstacle ahead that could carry a coin arc, skipping any
        /// whose arc has already been decided (drawn OR declined).
        ///
        /// Nearest rather than first-in-list, and the exclusion is a parameter
        /// rather than a check on the result, because a declined obstacle stays
        /// in front of the spawn cursor: matching it again every frame would
        /// starve everything behind it of an arc.
        /// </summary>
        public ObstacleData? GetUpcomingArcObstacle(float zPosition, HashSet<float> alreadyDecided, float lookAheadDistance = 8f)
        {
            ObstacleData? nearest = null;
            float nearestDistance = float.MaxValue;

            foreach (ObstacleData obstacle in obstaclePositions)
            {
                // Check if obstacle is ahead of current position
                float distanceAhead = obstacle.zPosition - zPosition;

                // Only consider obstacles within the look-ahead range
                if (distanceAhead <= 0 || distanceAhead > lookAheadDistance)
                    continue;

                if (!obstacle.CanHaveCoinArc())
                    continue;

                if (alreadyDecided != null && alreadyDecided.Contains(obstacle.zPosition))
                    continue;

                if (distanceAhead < nearestDistance)
                {
                    nearestDistance = distanceAhead;
                    nearest = obstacle;
                }
            }

            return nearest;
        }

        /// <summary>
        /// Whether a barrel or pylon sits in this lane within the given Z
        /// window. A flat coin line placed here would be uncollectable without
        /// dying, so the collectible spawner routes around it.
        /// </summary>
        public bool HasDeadlyObstacleInLane(int lane, float zPosition, float radius)
        {
            foreach (ObstacleData obstacle in obstaclePositions)
            {
                if (obstacle.lane == lane &&
                    obstacle.IsInstantDeath() &&
                    Mathf.Abs(zPosition - obstacle.zPosition) < radius)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Clean up obstacle data that is no longer needed
        /// FAIRNESS: More aggressive cleanup to prevent unbounded growth
        /// </summary>
        public void Cleanup(float virtualDistance)
        {
            // Remove obstacles that are far behind the current position.
            // In-place compaction: RemoveAll with a capturing lambda allocated
            // a closure + delegate every frame (this runs unconditionally from
            // LevelGenerator.Update).
            RemoveBelow(virtualDistance - 10f);

            // Periodic deep cleanup every 100 virtual units to prevent unbounded growth
            if (virtualDistance % 100f < 1f && obstaclePositions.Count > 50)
            {
                // Keep only obstacles within 30 units of current position
                RemoveBelow(virtualDistance - 30f);
                GameLog.Info($"Deep cleanup performed: obstacle list size = {obstaclePositions.Count}");
            }
        }

        /// <summary>Drop every tracked obstacle behind the threshold, allocation-free.</summary>
        private void RemoveBelow(float threshold)
        {
            int write = 0;
            for (int read = 0; read < obstaclePositions.Count; read++)
            {
                if (obstaclePositions[read].zPosition >= threshold)
                {
                    if (write != read)
                        obstaclePositions[write] = obstaclePositions[read];
                    write++;
                }
            }
            if (write < obstaclePositions.Count)
                obstaclePositions.RemoveRange(write, obstaclePositions.Count - write);
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
