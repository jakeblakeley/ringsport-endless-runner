using UnityEngine;

namespace RingSport.Level
{
    [CreateAssetMenu(fileName = "LevelConfig", menuName = "RingSport/Level Config", order = 1)]
    public class LevelConfig : ScriptableObject
    {
        [Header("Level Info")]
        [SerializeField] private int levelNumber = 1;

        [Header("Obstacle Settings")]
        [SerializeField] private int maxObstacles = 20;
        [SerializeField] private float minObstacleSpacing = 10f;
        [SerializeField] private float maxObstacleSpacing = 20f;

        [Header("Collectible Settings")]
        [SerializeField] private int maxCollectibles = 30;
        [SerializeField] private float minCollectibleSpacing = 5f;
        [SerializeField] private float maxCollectibleSpacing = 15f;

        [Header("Difficulty")]
        [Tooltip("Higher values = more avoid obstacles vs jump obstacles")]
        [Range(0f, 1f)]
        [SerializeField] private float avoidObstacleProbability = 0.5f;

        public int LevelNumber => levelNumber;
        public int MaxObstacles => maxObstacles;
        public float MinObstacleSpacing => minObstacleSpacing;
        public float MaxObstacleSpacing => maxObstacleSpacing;
        public int MaxCollectibles => maxCollectibles;
        public float MinCollectibleSpacing => minCollectibleSpacing;
        public float MaxCollectibleSpacing => maxCollectibleSpacing;
        public float AvoidObstacleProbability => avoidObstacleProbability;

        private void OnValidate()
        {
            // Ensure valid values
            maxObstacles = Mathf.Max(1, maxObstacles);
            maxCollectibles = Mathf.Max(1, maxCollectibles);
            minObstacleSpacing = Mathf.Max(1f, minObstacleSpacing);
            maxObstacleSpacing = Mathf.Max(minObstacleSpacing, maxObstacleSpacing);
            minCollectibleSpacing = Mathf.Max(1f, minCollectibleSpacing);
            maxCollectibleSpacing = Mathf.Max(minCollectibleSpacing, maxCollectibleSpacing);
        }
    }
}
