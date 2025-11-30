using UnityEngine;
using RingSport.Level;
using RingSport.UI;

namespace RingSport.Core
{
    public class LevelManager : MonoBehaviour
    {
        public static LevelManager Instance { get; private set; }

        [Header("Level Settings")]
        [SerializeField] private int maxLevels = 9;

        private int currentLevel = 1;
        private int currentScore = 0;
        private float levelTimer = 0f;
        private float distanceTraveled = 0f;
        private LevelConfig currentLevelConfig;

        public int CurrentLevel => currentLevel;
        public int CurrentScore => currentScore;
        public float LevelProgress => currentLevelConfig != null ? Mathf.Clamp01(levelTimer / currentLevelConfig.LevelDuration) : 0f;
        public float DistanceTraveled => distanceTraveled;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Update()
        {
            // Only run timer during Playing state
            if (GameManager.Instance?.CurrentState != GameState.Playing)
                return;

            if (currentLevelConfig == null)
                return;

            levelTimer += Time.deltaTime;

            // Check if level duration has been reached (level complete)
            if (levelTimer >= currentLevelConfig.LevelDuration)
            {
                EndLevel();
            }
        }

        public void StartLevel()
        {
            levelTimer = 0f;
            distanceTraveled = 0f;
            currentScore = 0;

            // Generate the level and get its config
            LevelGenerator.Instance?.GenerateLevel(currentLevel);

            // Get the current level config from LevelGenerator
            currentLevelConfig = LevelGenerator.Instance?.GetCurrentConfig();

            if (currentLevelConfig == null)
            {
                Debug.LogError("Failed to get level config!");
            }
        }

        public void EndLevel()
        {
            if (currentLevel < maxLevels)
            {
                GameManager.Instance?.CompleteLevel();
                UIManager.Instance?.ShowRewardScreen(currentLevel, currentScore);
            }
            else
            {
                // All levels completed
                UIManager.Instance?.ShowRewardScreen(currentLevel, currentScore);
            }
        }

        public void AddScore(int points)
        {
            currentScore += points;
            UIManager.Instance?.UpdateScore(currentScore);
        }

        public void AddDistance(float distance)
        {
            distanceTraveled += distance;
        }

        public void NextLevel()
        {
            if (currentLevel < maxLevels)
            {
                currentLevel++;
                GameManager.Instance?.StartGame();
            }
            else
            {
                // Game complete
                GameManager.Instance?.ReturnToHome();
            }
        }

        public void ResetProgress()
        {
            currentLevel = 1;
            currentScore = 0;
            levelTimer = 0f;
            distanceTraveled = 0f;
            currentLevelConfig = null;
        }

        public float GetLevelDuration()
        {
            return currentLevelConfig != null ? currentLevelConfig.LevelDuration : 60f;
        }
    }
}
