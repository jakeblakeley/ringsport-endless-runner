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

        [Header("Flee Attack Settings")]
        [Tooltip("Seconds of normal running before the chase begins when retrying a failed flee attack (the retry skips the rest of the run)")]
        [SerializeField] private float fleeAttackRetryPreRoll = 4f;
        [Tooltip("Seconds before the chase at which normal spawning stops, so the generated course drains past the player and leaves a clean gap of track (must cover the longest pattern tail ~105u at base speed)")]
        [SerializeField] private float fleeAttackWindDownSeconds = 7.5f;

        [Header("Audio Settings")]
        [SerializeField] private AudioClip[] levelCompleteSounds;
        [SerializeField] private float sfxVolume = 1.0f;

        private AudioSource sfxAudioSource;

        private int currentLevel = 1;
        private float levelTimer = 0f;
        private float distanceTraveled = 0f;
        private LevelConfig currentLevelConfig;
        private bool hasCalledLevelEnding = false; // Track if we've already called OnLevelEnding
        private bool hasReachedFinishLine = false; // Track if player has reached finish line
        private int retriesRemaining = 3;
        private float partialRetries = 0f; // Track partial lives (0.5 increments)

        // Flee attack (in-run mini level) state
        private bool isFleeAttackLevel = false;
        private bool fleeAttackTriggered = false;
        private bool fleeAttackWindDownStarted = false;
        private int fleeAttackDifficultyIndex = 0;
        private bool pendingFleeAttackEntry = false; // next StartLevel fast-forwards to the chase

        public int CurrentLevel => currentLevel;
        public int MaxLevels => maxLevels;
        public float LevelProgress => currentLevelConfig != null ? Mathf.Clamp01(levelTimer / currentLevelConfig.LevelDuration) : 0f;
        public float DistanceTraveled => distanceTraveled;
        public int RetriesRemaining => retriesRemaining;
        public float TotalRetries => retriesRemaining + partialRetries;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            Debug.Log($"[LevelManager] Initialized. Initial retries: {retriesRemaining}");

            // Setup audio source
            sfxAudioSource = gameObject.AddComponent<AudioSource>();
            sfxAudioSource.playOnAwake = false;
            sfxAudioSource.volume = sfxVolume;
        }

        private void Update()
        {
            // Only run timer during Playing state
            if (GameManager.Instance?.CurrentState != GameState.Playing)
                return;

            if (currentLevelConfig == null)
                return;

            levelTimer += Time.deltaTime;

            // Flee attack levels hand the end of the run to the in-run chase.
            // Spawning stops a wind-down period EARLIER so the generated
            // course scrolls past the player and leaves a clean gap of empty
            // track before the decoy appears (nothing visible is despawned).
            if (isFleeAttackLevel && MiniLevelFleeAttack.Instance != null)
            {
                float chaseStartTime = currentLevelConfig.LevelDuration - MiniLevelFleeAttack.Instance.GetLeadSeconds(fleeAttackDifficultyIndex);

                if (!fleeAttackWindDownStarted && levelTimer >= chaseStartTime - fleeAttackWindDownSeconds)
                {
                    fleeAttackWindDownStarted = true;
                    LevelGenerator.Instance?.SetRunnerSpawningSuppressed(true);
                }

                if (!fleeAttackTriggered && levelTimer >= chaseStartTime)
                {
                    fleeAttackTriggered = true;
                    MiniLevelFleeAttack.Instance.BeginChase(fleeAttackDifficultyIndex);
                }
            }

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
                return;
            }

            // Flee attack setup: the chase plays in-run at the end of the
            // level. A pending flee-attack entry (chase retry or debug jump)
            // fast-forwards the timer so only a short pre-roll runs first.
            isFleeAttackLevel = currentLevelConfig.MiniLevelType == MiniLevelType.FleeAttack;
            fleeAttackTriggered = false;
            fleeAttackWindDownStarted = false;
            fleeAttackDifficultyIndex = isFleeAttackLevel ? ComputeFleeAttackDifficulty(currentLevel) : 0;

            bool fleeAttackRetryEntry = pendingFleeAttackEntry && isFleeAttackLevel && MiniLevelFleeAttack.Instance != null;
            pendingFleeAttackEntry = false;

            // Reset any leftover chase state BEFORE arming the retry entry -
            // the controller's cleanup releases the spawn suppression, which
            // must not undo the wind-down set below
            MiniLevelFleeAttack.Instance?.OnRunLevelStarted(isFleeAttackLevel, fleeAttackRetryEntry);

            if (fleeAttackRetryEntry)
            {
                float lead = MiniLevelFleeAttack.Instance.GetLeadSeconds(fleeAttackDifficultyIndex);
                levelTimer = Mathf.Max(0f, currentLevelConfig.LevelDuration - lead - fleeAttackRetryPreRoll);
                Debug.Log($"[LevelManager] Flee attack entry - fast-forwarding level timer to {levelTimer:F1}s");

                // Keep the mini-level context armed through the pre-roll so a
                // death before the chase re-begins still retries the chase,
                // not the whole run (HandlePlayingState just cleared it)
                GameManager.Instance?.NotifyInRunMiniLevelStarted();

                // The pre-roll is inside the wind-down window: keep it a clean,
                // empty approach (also avoids racing LevelGenerator's Update)
                fleeAttackWindDownStarted = true;
                LevelGenerator.Instance?.SetRunnerSpawningSuppressed(true);
            }

            // Update HUD with level name now that config is loaded
            string levelName = !string.IsNullOrEmpty(currentLevelConfig.LevelName)
                ? currentLevelConfig.LevelName
                : $"Level {currentLevel}";
            UIManager.Instance?.UpdateLevel(levelName);

            // Start location-specific audio (music and ambient)
            if (currentLevelConfig.LocationConfig != null)
            {
                GameManager.Instance?.PlayLocationAudio(
                    currentLevelConfig.LocationConfig.Music,
                    currentLevelConfig.LocationConfig.AmbientSound
                );
            }
        }

        public void EndLevel()
        {
            // Play random level complete sound
            if (levelCompleteSounds != null && levelCompleteSounds.Length > 0 && sfxAudioSource != null)
            {
                AudioClip randomClip = levelCompleteSounds[Random.Range(0, levelCompleteSounds.Length)];
                sfxAudioSource.PlayOneShot(randomClip);
            }

            // Flee attack levels already played their mini level in-run (the
            // chase ends just before the finish line), so skip the arena
            // mini-level state and complete directly.
            if (isFleeAttackLevel && fleeAttackTriggered)
            {
                MiniLevelFleeAttack.Instance?.NotifyLevelEndReached();
                ScoreManager.Instance?.FinalizeLevelScore();
                GameManager.Instance?.CompleteLevel();
                return;
            }

            // Note: Score finalization is deferred to MiniLevelManager.CompleteMiniLevel()
            // This allows any bonus points from mini levels to be included

            // Trigger mini level state (mini level will then transition to LevelComplete)
            GameManager.Instance?.SetState(GameState.MiniLevel);
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
            UIManager.Instance?.UpdateScore(ScoreManager.Instance?.DisplayScore ?? 0);
        }

        public void PlayCollectSound(AudioClip clip)
        {
            if (clip != null && sfxAudioSource != null)
                sfxAudioSource.PlayOneShot(clip, sfxVolume);
        }

        public void AddDistance(float distance)
        {
            distanceTraveled += distance;
        }

        public void NextLevel()
        {
            Debug.Log($"[LevelManager] NextLevel called. Current level: {currentLevel}, Max levels: {maxLevels}");
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

        /// <summary>
        /// Enters (or re-enters) a flee attack level fast-forwarded to just
        /// before the chase. Used when retrying a failed chase and by the
        /// debug menu - the retry replays only the chase plus a short
        /// pre-roll, not the whole run.
        /// </summary>
        public void StartAtFleeAttack(int level)
        {
            // Bank whatever score the failed attempt had before StartLevel resets it
            ScoreManager.Instance?.FinalizeLevelScore();

            currentLevel = Mathf.Clamp(level, 1, maxLevels);
            pendingFleeAttackEntry = true;
            Debug.Log($"[LevelManager] Starting level {currentLevel} at the flee attack chase");
            GameManager.Instance?.SetState(GameState.Playing);
        }

        /// <summary>
        /// Difficulty ordinal of the flee attack on the given level: how many
        /// earlier levels also run one (level 3 = 0, level 5 = 1, level 7 = 2).
        /// </summary>
        private int ComputeFleeAttackDifficulty(int level)
        {
            int index = 0;
            for (int i = 1; i < level; i++)
            {
                var config = LevelGenerator.Instance?.GetLevelConfig(i);
                if (config != null && config.MiniLevelType == MiniLevelType.FleeAttack)
                    index++;
            }
            return index;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Debug menu: start a fresh run at the given level, opening on that
        /// level's intro screen (location + level name) rather than dropping
        /// straight into gameplay.
        /// </summary>
        public void DebugStartAtLevel(int level)
        {
            ResetProgress();
            currentLevel = Mathf.Clamp(level, 1, maxLevels);
            Debug.Log($"[LevelManager] DEBUG: showing intro for level {currentLevel}");
            GameManager.Instance?.DebugShowLevelIntro(currentLevel);
        }
#endif

        public void ResetProgress()
        {
            Debug.Log("[LevelManager] ResetProgress called - resetting to level 1. Stack trace:");
            Debug.Log(System.Environment.StackTrace);

            // Reset scores via ScoreManager (handles high score save if applicable)
            ScoreManager.Instance?.ResetForNewRun();

            // Love note unlocks persist across runs; only the HUD counter resets
            LoveNoteManager.ResetRunCounter();

            currentLevel = 1;
            levelTimer = 0f;
            distanceTraveled = 0f;
            currentLevelConfig = null;
            retriesRemaining = maxRetries;
            partialRetries = 0f;
            Debug.Log($"[LevelManager] Progress reset. Retries reset to {retriesRemaining}");
        }

        public bool UseRetry()
        {
            if (retriesRemaining > 0)
            {
                retriesRemaining--;
                Debug.Log($"[LevelManager] Death occurred. Retry consumed. Retries remaining: {retriesRemaining}");

                // Update lives UI
                UIManager.Instance?.UpdateLives(TotalRetries);

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

        /// <summary>
        /// Add a partial retry (e.g., 0.5 from life pickup). Converts to full retry when >= 1.0
        /// </summary>
        public void AddPartialRetry(float amount)
        {
            partialRetries += amount;

            // Convert to full retry when we have enough
            while (partialRetries >= 1f)
            {
                partialRetries -= 1f;
                retriesRemaining++;
                Debug.Log($"[LevelManager] Gained a full retry! Retries: {retriesRemaining}");
            }

            Debug.Log($"[LevelManager] Added {amount} partial retry. Total: {TotalRetries}");
            UIManager.Instance?.UpdateLives(TotalRetries);
        }

        public float GetLevelDuration()
        {
            return currentLevelConfig != null ? currentLevelConfig.LevelDuration : 60f;
        }
    }
}
