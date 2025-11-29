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
        [Tooltip("Ratio of collectibles to obstacles (e.g., 3.0 = 3 collectibles per obstacle)")]
        [SerializeField] private float collectibleToObstacleRatio = 3.0f;
        [Tooltip("Minimum distance (in Z-axis units) between collectibles and obstacles")]
        [SerializeField] private float minCollectibleObstacleDistance = 2.0f;
        [Tooltip("Probability (0-1) that a collectible spawns in the same lane as the previous one")]
        [Range(0f, 1f)]
        [SerializeField] private float collectibleLineBias = 0.65f;
        [Tooltip("Probability (0-1) that a collectible spawns above an ObstacleJump instead of avoiding it")]
        [Range(0f, 1f)]
        [SerializeField] private float collectibleAboveObstacleChance = 0.3f;

        [Header("Mega Collectible Settings")]
        [Tooltip("Probability (0-1) that a collectible spawns as a mega collectible")]
        [Range(0f, 1f)]
        [SerializeField] private float megaCollectibleSpawnRatio = 0.05f;
        [Tooltip("Point value for mega collectibles")]
        [SerializeField] private int megaCollectiblePointValue = 50;

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
        public float CollectibleToObstacleRatio => collectibleToObstacleRatio;
        public float MinCollectibleObstacleDistance => minCollectibleObstacleDistance;
        public float CollectibleLineBias => collectibleLineBias;
        public float CollectibleAboveObstacleChance => collectibleAboveObstacleChance;
        public float MegaCollectibleSpawnRatio => megaCollectibleSpawnRatio;
        public int MegaCollectiblePointValue => megaCollectiblePointValue;
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
            collectibleToObstacleRatio = Mathf.Max(0.1f, collectibleToObstacleRatio);
            minCollectibleObstacleDistance = Mathf.Max(0f, minCollectibleObstacleDistance);
            megaCollectiblePointValue = Mathf.Max(1, megaCollectiblePointValue);
        }
    }
}
