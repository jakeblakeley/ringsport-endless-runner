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

        [Header("End Game Settings")]
        [Tooltip("Time before level end to trigger end game behavior (despawn distant obstacles)")]
        [SerializeField] private float endGameWarningTime = 5f;

        [Header("Retry Settings")]
        [SerializeField] private int maxRetries = 3;

        private int currentLevel = 1;
        private float levelTimer = 0f;
        private float distanceTraveled = 0f;
        private LevelConfig currentLevelConfig;
        private bool hasCalledLevelEnding = false; // Track if we've already called OnLevelEnding
        private bool hasReachedFinishLine = false; // Track if player has reached finish line
        private int retriesRemaining = 3;

        public int CurrentLevel => currentLevel;
        public int MaxLevels => maxLevels;
        public float LevelProgress => currentLevelConfig != null ? Mathf.Clamp01(levelTimer / currentLevelConfig.LevelDuration) : 0f;
        public float DistanceTraveled => distanceTraveled;
        public int RetriesRemaining => retriesRemaining;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Debug.Log($"[LevelManager] Initialized. Initial retries: {retriesRemaining}");
        }

        private void Update()
        {
            // Only run timer during Playing state
            if (GameManager.Instance?.CurrentState != GameState.Playing)
                return;

            if (currentLevelConfig == null)
                return;

            levelTimer += Time.deltaTime;

            // FAIRNESS: Despawn obstacles before level ends
            if (!hasCalledLevelEnding && levelTimer >= currentLevelConfig.LevelDuration - endGameWarningTime)
            {
                LevelGenerator.Instance?.OnLevelEnding();
                hasCalledLevelEnding = true;
            }
        }

        public void StartLevel()
        {
            levelTimer = 0f;
            distanceTraveled = 0f;
            hasCalledLevelEnding = false; // Reset for new level
            hasReachedFinishLine = false; // Reset for new level

            // Start tracking score for this level
            ScoreManager.Instance?.StartLevel(currentLevel);

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
            // Finalize the score for this level (updates best score if this attempt was better)
            ScoreManager.Instance?.FinalizeLevelScore();

            int levelScore = ScoreManager.Instance?.CurrentScore ?? 0;

            // Get next level information if not at final level
            string nextLevelName = "";
            string nextLevelLocation = "";

            if (currentLevel < maxLevels)
            {
                int nextLevelNumber = currentLevel + 1;
                LevelConfig nextLevelConfig = LevelGenerator.Instance?.GetLevelConfig(nextLevelNumber);

                if (nextLevelConfig != null)
                {
                    nextLevelName = nextLevelConfig.LevelName;
                    nextLevelLocation = nextLevelConfig.Location.ToString();
                }

                GameManager.Instance?.CompleteLevel();
                UIManager.Instance?.ShowRewardScreen(currentLevel, levelScore, nextLevelName, nextLevelLocation);
            }
            else
            {
                // All levels completed
                UIManager.Instance?.ShowRewardScreen(currentLevel, levelScore, nextLevelName, nextLevelLocation);
            }
        }

        /// <summary>
        /// Called when player reaches the finish line floor
        /// </summary>
        public void OnFinishLineReached()
        {
            // Only trigger if we're in the Playing state and haven't already finished
            if (hasReachedFinishLine || GameManager.Instance?.CurrentState != GameState.Playing)
            {
                return;
            }

            hasReachedFinishLine = true;
            Debug.Log("Finish line reached - completing level!");
            EndLevel();
        }

        public void AddScore(int points)
        {
            ScoreManager.Instance?.AddScore(points);
            UIManager.Instance?.UpdateScore(ScoreManager.Instance?.CurrentScore ?? 0);
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
                Debug.Log($"[LevelManager] Advancing to level {currentLevel}. Retries NOT reset: {retriesRemaining} remaining");
                // Don't call StartGame() as it resets progress including retries
                // Instead, directly transition to Playing state
                GameManager.Instance?.SetState(GameState.Playing);
            }
            else
            {
                // Game complete - all levels finished!
                // Save high score if achieved
                ScoreManager.Instance?.CheckAndSaveHighScore();
                GameManager.Instance?.ReturnToHome();
            }
        }

        public void ResetProgress()
        {
            // Reset scores via ScoreManager (handles high score save if applicable)
            ScoreManager.Instance?.ResetForNewRun();

            currentLevel = 1;
            levelTimer = 0f;
            distanceTraveled = 0f;
            currentLevelConfig = null;
            retriesRemaining = maxRetries;
            Debug.Log($"[LevelManager] Progress reset. Retries reset to {retriesRemaining}");
        }

        public bool UseRetry()
        {
            if (retriesRemaining > 0)
            {
                retriesRemaining--;
                Debug.Log($"[LevelManager] Death occurred. Retry consumed. Retries remaining: {retriesRemaining}");

                // If this was the last retry, save high score before showing game over
                if (retriesRemaining == 0)
                {
                    ScoreManager.Instance?.CheckAndSaveHighScore();
                    Debug.Log("[LevelManager] Out of retries! High score checked and saved.");
                }

                return true;
            }

            Debug.Log("[LevelManager] Death occurred but out of retries!");
            ScoreManager.Instance?.CheckAndSaveHighScore();
            return false;
        }

        public float GetLevelDuration()
        {
            return currentLevelConfig != null ? currentLevelConfig.LevelDuration : 60f;
        }
    }
}
