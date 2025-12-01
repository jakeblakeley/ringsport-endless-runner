using UnityEngine;

namespace RingSport.Level
{
    /// <summary>
    /// Shared data class that provides spawn parameters to all spawning systems
    /// </summary>
    public class SpawnContext
    {
        public float VirtualDistance { get; set; }
        public Vector3 PlayerPosition { get; set; }
        public LevelConfig CurrentConfig { get; set; }
        public float SpawnDistance { get; set; }

        public SpawnContext(float spawnDistance)
        {
            SpawnDistance = spawnDistance;
        }

        /// <summary>
        /// Update the context with current frame data
        /// </summary>
        public void Update(float virtualDistance, Vector3 playerPosition, LevelConfig config)
        {
            VirtualDistance = virtualDistance;
            PlayerPosition = playerPosition;
            CurrentConfig = config;
        }
    }
}
