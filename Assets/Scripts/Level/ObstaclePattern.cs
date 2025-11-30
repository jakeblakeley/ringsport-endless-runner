using UnityEngine;

namespace RingSport.Level
{
    /// <summary>
    /// Defines a single obstacle in a pattern
    /// </summary>
    [System.Serializable]
    public struct ObstacleDefinition
    {
        public string obstacleType; // "ObstacleJump", "ObstacleAvoid", "ObstaclePalisade", "ObstaclePylon", "ObstacleBroadJump"
        public int lane; // -1 (left), 0 (center), 1 (right)
        public float zOffset; // Offset from pattern start position

        public ObstacleDefinition(string type, int lane, float zOffset)
        {
            this.obstacleType = type;
            this.lane = lane;
            this.zOffset = zOffset;
        }
    }

    /// <summary>
    /// Hand-crafted obstacle pattern for level generation
    /// Patterns ensure fair, solvable, and memorable obstacle sequences
    /// </summary>
    [CreateAssetMenu(fileName = "ObstaclePattern", menuName = "RingSport/Obstacle Pattern", order = 1)]
    public class ObstaclePattern : ScriptableObject
    {
        [Header("Pattern Info")]
        [Tooltip("Descriptive name for this pattern (e.g., 'Easy Zigzag', 'Jump Sequence')")]
        public string patternName = "New Pattern";

        [Tooltip("Difficulty rating from 1 (easiest) to 10 (hardest)")]
        [Range(1, 10)]
        public int difficultyRating = 5;

        [Header("Pattern Data")]
        [Tooltip("List of obstacles in this pattern with their positions and types")]
        public ObstacleDefinition[] obstacles;

        [Tooltip("Total length of this pattern in units (used to determine next spawn position)")]
        public float patternLength = 20f;

        [Header("Usage Restrictions")]
        [Tooltip("Minimum level this pattern can appear in (1-9)")]
        [Range(1, 9)]
        public int minLevel = 1;

        [Tooltip("Maximum level this pattern can appear in (1-9)")]
        [Range(1, 9)]
        public int maxLevel = 9;

        /// <summary>
        /// Check if this pattern is valid for the given level
        /// </summary>
        public bool IsValidForLevel(int level)
        {
            return level >= minLevel && level <= maxLevel;
        }

        /// <summary>
        /// Validate that this pattern is solvable (at least one clear path exists)
        /// </summary>
        public bool IsSolvable()
        {
            // Group obstacles by their Z offset to find rows
            var obstaclesByZ = new System.Collections.Generic.Dictionary<float, System.Collections.Generic.List<ObstacleDefinition>>();

            foreach (var obstacle in obstacles)
            {
                if (!obstaclesByZ.ContainsKey(obstacle.zOffset))
                {
                    obstaclesByZ[obstacle.zOffset] = new System.Collections.Generic.List<ObstacleDefinition>();
                }
                obstaclesByZ[obstacle.zOffset].Add(obstacle);
            }

            // Check each Z position to ensure at least one lane is passable
            foreach (var kvp in obstaclesByZ)
            {
                var obstaclesAtZ = kvp.Value;

                // If 3 obstacles at same Z (blocking all lanes), check if at least one is passable
                if (obstaclesAtZ.Count == 3)
                {
                    bool hasPassableLane = false;
                    foreach (var obs in obstaclesAtZ)
                    {
                        // Jump, Palisade, and BroadJump are passable; Avoid and Pylon are instant death
                        if (obs.obstacleType == "ObstacleJump" ||
                            obs.obstacleType == "ObstaclePalisade" ||
                            obs.obstacleType == "ObstacleBroadJump")
                        {
                            hasPassableLane = true;
                            break;
                        }
                    }

                    if (!hasPassableLane)
                    {
                        Debug.LogWarning($"Pattern '{patternName}' is UNSOLVABLE at Z={kvp.Key}: All 3 lanes blocked with instant-death obstacles!");
                        return false;
                    }
                }
            }

            return true;
        }

        private void OnValidate()
        {
            // Auto-validate in editor
            if (obstacles != null && obstacles.Length > 0)
            {
                IsSolvable();
            }

            // Ensure min/max levels are valid
            if (minLevel > maxLevel)
            {
                maxLevel = minLevel;
            }
        }
    }
}
