using UnityEngine;

namespace RingSport.Level
{
    [CreateAssetMenu(fileName = "LevelConfig", menuName = "RingSport/Level Config", order = 1)]
    public class LevelConfig : ScriptableObject
    {
        [Header("Level Info")]
        [SerializeField] private int levelNumber = 1;
        [Tooltip("Duration in seconds to complete this level")]
        [SerializeField] private float levelDuration = 60f;
        [Tooltip("Display name of the level (e.g., 'Brevet', 'Ring 1 Leg 1')")]
        [SerializeField] private string levelName = "";
        [Tooltip("Location where the level takes place")]
        [SerializeField] private Location location = Location.Arizona;

        [Tooltip("Location configuration defining floor prefabs for this location")]
        [SerializeField] private LocationConfig locationConfig;

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
        [Tooltip("Speed multiplier for the level (1.0 = normal, 1.5 = 50% faster, etc.)")]
        [Range(0.5f, 3f)]
        [SerializeField] private float speedMultiplier = 1.0f;
        [Tooltip("Maximum effective speed (sprint × multiplier capped at this value for fairness)")]
        [Range(15f, 45f)]
        [SerializeField] private float maxEffectiveSpeed = 30f;

        [Header("Pattern Difficulty")]
        [Tooltip("Minimum pattern difficulty rating for this level (1-10)")]
        [Range(1, 10)]
        [SerializeField] private int minPatternDifficulty = 1;
        [Tooltip("Maximum pattern difficulty rating for this level (1-10)")]
        [Range(1, 10)]
        [SerializeField] private int maxPatternDifficulty = 5;
        [Tooltip("Percentage of obstacle spawns that should use patterns vs random (0-1)")]
        [Range(0f, 1f)]
        [SerializeField] private float patternUsageRatio = 0.7f;

        public int LevelNumber => levelNumber;
        public float LevelDuration => levelDuration;
        public string LevelName => levelName;
        public Location Location => location;
        public LocationConfig LocationConfig => locationConfig;
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
        public float SpeedMultiplier => speedMultiplier;
        public float MaxEffectiveSpeed => maxEffectiveSpeed;
        public int MinPatternDifficulty => minPatternDifficulty;
        public int MaxPatternDifficulty => maxPatternDifficulty;
        public float PatternUsageRatio => patternUsageRatio;

        private void OnValidate()
        {
            // Ensure valid values
            levelDuration = Mathf.Max(1f, levelDuration);
            maxObstacles = Mathf.Max(1, maxObstacles);
            maxCollectibles = Mathf.Max(1, maxCollectibles);
            minObstacleSpacing = Mathf.Max(1f, minObstacleSpacing);
            maxObstacleSpacing = Mathf.Max(minObstacleSpacing, maxObstacleSpacing);
            minCollectibleSpacing = Mathf.Max(1f, minCollectibleSpacing);
            maxCollectibleSpacing = Mathf.Max(minCollectibleSpacing, maxCollectibleSpacing);
            collectibleToObstacleRatio = Mathf.Max(0.1f, collectibleToObstacleRatio);
            minCollectibleObstacleDistance = Mathf.Max(0f, minCollectibleObstacleDistance);
            megaCollectiblePointValue = Mathf.Max(1, megaCollectiblePointValue);
            speedMultiplier = Mathf.Clamp(speedMultiplier, 0.5f, 3f);
            maxEffectiveSpeed = Mathf.Clamp(maxEffectiveSpeed, 15f, 45f);
            minPatternDifficulty = Mathf.Clamp(minPatternDifficulty, 1, 10);
            maxPatternDifficulty = Mathf.Clamp(maxPatternDifficulty, minPatternDifficulty, 10);
        }
    }
}
