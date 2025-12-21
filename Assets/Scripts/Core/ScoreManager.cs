using UnityEngine;

namespace RingSport.Core
{
    /// <summary>
    /// Manages all score tracking including current level score, total run score,
    /// and persistent high score. Handles PlayerPrefs persistence for cross-platform support.
    /// Tracks best score per level - retrying a level only counts the highest score achieved.
    /// </summary>
    public class ScoreManager : MonoBehaviour
    {
        public static ScoreManager Instance { get; private set; }

        private const int MAX_LEVELS = 9;

        private int currentScore = 0;           // Score for current level attempt only
        private int[] levelBestScores;          // Best score achieved per level in this run (indices 0-8 for levels 1-9)
        private int currentLevel = 1;           // Current level being played
        private int highScore = 0;              // Best total score ever (persisted via PlayerPrefs)
        private bool achievedNewHighScore = false; // Track if a new high score was achieved this run
        private int miniLevelScore = 0;         // Score earned specifically during current mini-level

        // Public properties for read-only access
        public int CurrentScore => currentScore;
        public int TotalScore => CalculateTotalScore();
        public int HighScore => highScore;
        public int MiniLevelScore => miniLevelScore;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            levelBestScores = new int[MAX_LEVELS];
            LoadHighScore();
            Debug.Log($"[ScoreManager] Initialized. High score loaded: {highScore}");
        }

        /// <summary>
        /// Adds points to the current level score.
        /// </summary>
        public void AddScore(int points)
        {
            currentScore += points;
            Debug.Log($"[ScoreManager] Added {points} points. Level {currentLevel} current score: {currentScore}, Total run score: {TotalScore}");
        }

        /// <summary>
        /// Starts tracking score specifically for a mini-level.
        /// Call at the start of a mini-level.
        /// </summary>
        public void StartMiniLevelScoring()
        {
            miniLevelScore = 0;
            Debug.Log("[ScoreManager] Mini-level scoring started");
        }

        /// <summary>
        /// Adds points to mini-level score AND current score.
        /// Use during mini-level gameplay.
        /// </summary>
        public void AddMiniLevelScore(int points)
        {
            miniLevelScore += points;
            currentScore += points;
            Debug.Log($"[ScoreManager] Added {points} mini-level points. Mini-level: {miniLevelScore}, Level total: {currentScore}");
        }

        /// <summary>
        /// Resets only the mini-level score (for game over within mini-level).
        /// Subtracts mini-level score from current score.
        /// </summary>
        public void ResetMiniLevelScore()
        {
            currentScore -= miniLevelScore;
            currentScore = Mathf.Max(0, currentScore);
            Debug.Log($"[ScoreManager] Mini-level score reset. Removed {miniLevelScore} points. Level score now: {currentScore}");
            miniLevelScore = 0;
        }

        /// <summary>
        /// Starts tracking score for a new level. Resets current score to 0.
        /// </summary>
        public void StartLevel(int level)
        {
            currentLevel = level;
            currentScore = 0;
            Debug.Log($"[ScoreManager] Started level {level}. Best score for this level so far: {GetLevelBestScore(level)}");
        }

        /// <summary>
        /// Finalizes the current level score. Updates the best score for this level if current score is higher.
        /// Call this when level completes or when player dies/retries.
        /// </summary>
        public void FinalizeLevelScore()
        {
            int levelIndex = currentLevel - 1;
            if (levelIndex >= 0 && levelIndex < MAX_LEVELS)
            {
                int previousBest = levelBestScores[levelIndex];
                if (currentScore > previousBest)
                {
                    levelBestScores[levelIndex] = currentScore;
                    Debug.Log($"[ScoreManager] New best for level {currentLevel}! {currentScore} (was {previousBest}). Total: {TotalScore}");
                }
                else
                {
                    Debug.Log($"[ScoreManager] Level {currentLevel} attempt: {currentScore}. Best remains: {previousBest}. Total: {TotalScore}");
                }
            }
        }

        /// <summary>
        /// Gets the best score achieved for a specific level in the current run.
        /// </summary>
        public int GetLevelBestScore(int level)
        {
            int levelIndex = level - 1;
            if (levelIndex >= 0 && levelIndex < MAX_LEVELS)
                return levelBestScores[levelIndex];
            return 0;
        }

        /// <summary>
        /// Calculates total score as sum of best scores from all levels.
        /// </summary>
        private int CalculateTotalScore()
        {
            int total = 0;
            for (int i = 0; i < MAX_LEVELS; i++)
            {
                total += levelBestScores[i];
            }
            return total;
        }

        /// <summary>
        /// Resets all scores for a new game run.
        /// </summary>
        public void ResetForNewRun()
        {
            currentScore = 0;
            currentLevel = 1;
            achievedNewHighScore = false; // Reset the flag for new run
            for (int i = 0; i < MAX_LEVELS; i++)
            {
                levelBestScores[i] = 0;
            }
            Debug.Log($"[ScoreManager] Scores reset for new run.");
        }

        /// <summary>
        /// Checks and saves high score if current total score is higher.
        /// Called when run ends (game over, quit, or completion).
        /// </summary>
        public void CheckAndSaveHighScore()
        {
            int currentTotal = TotalScore;
            if (currentTotal > highScore)
            {
                achievedNewHighScore = true; // Mark that we achieved a new high score
                highScore = currentTotal;
                SaveHighScore();
                Debug.Log($"[ScoreManager] New high score saved! {highScore}");
            }
            else
            {
                Debug.Log($"[ScoreManager] Run ended. Score: {currentTotal}, High Score: {highScore} (no new record)");
            }
        }

        /// <summary>
        /// Checks if the current run achieved a new high score.
        /// Returns true if a new high score was set during this run.
        /// </summary>
        public bool IsNewHighScore()
        {
            Debug.Log($"[ScoreManager] IsNewHighScore check: achievedNewHighScore={achievedNewHighScore}");
            return achievedNewHighScore;
        }

        /// <summary>
        /// Loads high score from PlayerPrefs. Works cross-platform (WebGL: localStorage, iOS: NSUserDefaults).
        /// </summary>
        private void LoadHighScore()
        {
            highScore = PlayerPrefs.GetInt("HighScore", 0);
            Debug.Log($"[ScoreManager] Loaded high score from PlayerPrefs: {highScore}");
        }

        /// <summary>
        /// Saves high score to PlayerPrefs. Works cross-platform (WebGL: localStorage, iOS: NSUserDefaults).
        /// </summary>
        private void SaveHighScore()
        {
            PlayerPrefs.SetInt("HighScore", highScore);
            PlayerPrefs.Save();
            Debug.Log($"[ScoreManager] Saved high score to PlayerPrefs: {highScore}");
        }

        /// <summary>
        /// Clears the saved high score. Useful for testing.
        /// </summary>
        [ContextMenu("Clear High Score")]
        public void ClearHighScore()
        {
            highScore = 0;
            achievedNewHighScore = false;
            PlayerPrefs.DeleteKey("HighScore");
            PlayerPrefs.Save();
            Debug.Log("[ScoreManager] High score cleared.");
        }
    }
}
