using UnityEngine;
using RingSport.UI;
using RingSport.Level;
using RingSport.Player;

namespace RingSport.Core
{
    public enum GameState
    {
        Home,
        Playing,
        MiniLevel,
        LevelComplete,
        GameOver
    }

    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        [SerializeField] private GameState currentState = GameState.Home;

        private bool isInMiniLevelContext = false;
        private GameState previousState;

        [Header("Countdown Settings")]
        [SerializeField] private float countdownDuration = 3f;

        public GameState CurrentState => currentState;
        public bool IsInMiniLevelContext => isInMiniLevelContext;

        [Header("Audio Settings")]
        [SerializeField] private AudioClip gameOverSound;
        [SerializeField] [Range(0f, 1f)] private float musicVolume = 0.5f;
        [SerializeField] [Range(0f, 1f)] private float ambientVolume = 0.3f;
        [SerializeField] [Range(0f, 1f)] private float sfxVolume = 1.0f;

        private AudioSource musicAudioSource;
        private AudioSource ambientAudioSource;
        private AudioSource sfxAudioSource;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // When set, HandleMiniLevelState launches this mini level instead of the
        // current level config's. Persists across retries so the game over ->
        // retry flow restarts the same mini level; cleared on Home/Playing.
        private MiniLevelType? debugMiniLevelOverride;

        /// <summary>
        /// Debug menu: jump straight into a specific mini level.
        /// </summary>
        public void DebugStartMiniLevel(MiniLevelType type)
        {
            debugMiniLevelOverride = type;
            SetState(GameState.MiniLevel);
        }

        /// <summary>
        /// Debug menu: show a runner level's start screen (location + level name)
        /// instead of dropping straight into gameplay, so the intro UI can be
        /// checked per level. Stays in the Home state until the start button is
        /// pressed, which enters Playing on the level LevelManager is holding.
        /// </summary>
        public void DebugShowLevelIntro(int level)
        {
            Time.timeScale = 1f;
            isInMiniLevelContext = false;

            LevelConfig config = LevelGenerator.Instance?.GetLevelConfig(level);

            // Dress the backdrop with the previewed level's location
            LevelGenerator.Instance?.LoadHomeScene(level);
            CameraStateMachine.Instance?.SetState(CameraStateType.Start);
            Object.FindAnyObjectByType<PlayerController>()?.Animations?.SetFacing(false);
            StopLocationAudio();

            string levelName = config != null && !string.IsNullOrEmpty(config.LevelName)
                ? config.LevelName
                : $"Level {level}";

            UIManager.Instance?.ShowLevelIntro(levelName, config != null ? config.Location.ToString() : "");
        }
#endif

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            // Setup audio sources
            musicAudioSource = gameObject.AddComponent<AudioSource>();
            musicAudioSource.playOnAwake = false;
            musicAudioSource.loop = true;
            musicAudioSource.volume = musicVolume;

            ambientAudioSource = gameObject.AddComponent<AudioSource>();
            ambientAudioSource.playOnAwake = false;
            ambientAudioSource.loop = true;
            ambientAudioSource.volume = ambientVolume;

            sfxAudioSource = gameObject.AddComponent<AudioSource>();
            sfxAudioSource.playOnAwake = false;
            sfxAudioSource.volume = sfxVolume;

            DontDestroyOnLoad(gameObject);
        }

        private void Start()
        {
            SetState(GameState.Home);
        }

        public void SetState(GameState newState)
        {
            previousState = currentState;
            currentState = newState;

            switch (newState)
            {
                case GameState.Home:
                    HandleHomeState();
                    break;
                case GameState.Playing:
                    HandlePlayingState();
                    break;
                case GameState.MiniLevel:
                    HandleMiniLevelState();
                    break;
                case GameState.LevelComplete:
                    HandleLevelCompleteState();
                    break;
                case GameState.GameOver:
                    HandleGameOverState();
                    break;
            }
        }

        private void HandleHomeState()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            debugMiniLevelOverride = null;
#endif
            Time.timeScale = 1f;
            Object.FindAnyObjectByType<PlayerController>()?.Animations?.SetFacing(false);
            UIManager.Instance?.ShowHomeScreen();
            CameraStateMachine.Instance?.SetState(CameraStateType.Start);

            // Stop location audio when returning home
            StopLocationAudio();

            // Load first level's location and start scene for home screen visuals
            LevelGenerator.Instance?.LoadHomeScene();
        }

        private void HandlePlayingState()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            debugMiniLevelOverride = null;
#endif
            Time.timeScale = 0f;
            isInMiniLevelContext = false;

            // Reset any paused states from previous game over (e.g., palisade minigame failure)
            LevelScroller.Instance?.Resume();
            var player = Object.FindAnyObjectByType<PlayerController>();
            player?.ResetPosition();
            player?.ResumeMovement();
            // Running levels always face forward (facing persists through resets
            // so mini-level retries can keep the dog toward the camera)
            player?.Animations?.SetFacing(false);

            // Start level first (resets score) before showing HUD
            LevelManager.Instance?.StartLevel();
            CameraStateMachine.Instance?.SetState(CameraStateType.Gameplay);

            // Show HUD after score is reset
            UIManager.Instance?.ShowGameHUD();

            Debug.Log($"[GameManager] HandlePlayingState - About to start countdown. UIManager exists: {UIManager.Instance != null}");
            UIManager.Instance?.StartCountdown(countdownDuration, OnCountdownComplete);

            // Note: Location audio is started by LevelManager.StartLevel() after level is generated
        }

        private void OnCountdownComplete()
        {
            Time.timeScale = 1f;
        }

        private void HandleMiniLevelState()
        {
            // Flee attack is an IN-RUN mini level: it plays during the Playing
            // state at the end of its level, not in the arena flow. Reaching
            // this state with it pending (chase-death retry, or the debug
            // menu) reroutes into a short run that jumps straight to the chase.
            if (TryRouteFleeAttackEntry())
                return;

            Time.timeScale = 0f;
            isInMiniLevelContext = true;

            // Finalize the main level score before mini-level starts
            // This ensures the running section score is saved even if player fails mini-level
            ScoreManager.Instance?.FinalizeLevelScore();

            // Hide all UI screens (including game over screen if retrying)
            UIManager.Instance?.HideAllScreens();

            // Stop any running countdown
            UIManager.Instance?.StopCountdown();

            var player = Object.FindAnyObjectByType<PlayerController>();
            player?.ResetPosition();

            // On a mini-level retry (coming from game over) the mini-level camera
            // and the dog's camera-facing are already in place - the camera move,
            // turn-around and start panel only appear on the first entry; the
            // retry click stands in for the start click
            bool isRetry = previousState == GameState.GameOver;
            if (!isRetry)
                CameraStateMachine.Instance?.SetState(CameraStateType.Start);

            // Stop location audio during mini level
            StopLocationAudio();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugMiniLevelOverride.HasValue)
            {
                MiniLevelManager.Instance?.StartMiniLevel(debugMiniLevelOverride.Value, isRetry);
                return;
            }
#endif

            // Get current level config to determine mini level type
            LevelConfig currentConfig = LevelGenerator.Instance?.GetCurrentConfig();
            if (currentConfig != null)
            {
                MiniLevelManager.Instance?.StartMiniLevel(currentConfig.MiniLevelType, isRetry);
            }
            else
            {
                Debug.LogError("No current level config found for mini level!");
                // Fallback: skip directly to level complete
                SetState(GameState.LevelComplete);
            }
        }

        /// <summary>
        /// If the mini level about to start is the in-run flee attack, routes
        /// back into the Playing state fast-forwarded to the chase and returns
        /// true. Handles both the current level's config and the debug override.
        /// </summary>
        private bool TryRouteFleeAttackEntry()
        {
            if (MiniLevelFleeAttack.Instance == null || LevelManager.Instance == null)
                return false;

            LevelConfig config = LevelGenerator.Instance?.GetCurrentConfig();
            MiniLevelType effectiveType = config != null ? config.MiniLevelType : MiniLevelType.PositionsSimonSays;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugMiniLevelOverride.HasValue)
                effectiveType = debugMiniLevelOverride.Value;
#endif
            if (effectiveType != MiniLevelType.FleeAttack)
                return false;

            // Retry on a flee level re-enters that level's chase; a debug jump
            // from elsewhere hosts the chase on the first flee attack level
            int targetLevel = (config != null && config.MiniLevelType == MiniLevelType.FleeAttack)
                ? LevelManager.Instance.CurrentLevel
                : LevelGenerator.Instance?.FindFirstLevelWithMiniLevel(MiniLevelType.FleeAttack) ?? -1;

            if (targetLevel < 1)
            {
                Debug.LogWarning("[GameManager] No level uses the FleeAttack mini level - cannot route in-run entry");
                return false;
            }

            Debug.Log($"[GameManager] Routing FleeAttack mini level into in-run chase on level {targetLevel}");
            LevelManager.Instance.StartAtFleeAttack(targetLevel);
            return true;
        }

        /// <summary>
        /// Called when an in-run mini level (the flee attack chase) begins
        /// during the Playing state. Banks the running-section score
        /// (mirroring the arena flow's finalize-on-entry) and marks the
        /// mini-level context so a death during the chase retries just the
        /// chase instead of the whole run.
        /// </summary>
        public void NotifyInRunMiniLevelStarted()
        {
            ScoreManager.Instance?.FinalizeLevelScore();
            isInMiniLevelContext = true;
        }

        private void HandleLevelCompleteState()
        {
            Time.timeScale = 0f;

            var player = Object.FindAnyObjectByType<PlayerController>();
            player?.ResetPosition();

            CameraStateMachine.Instance?.SetState(CameraStateType.Start);

            // Stop location audio on level complete
            StopLocationAudio();

            // Show reward screen
            int level = LevelManager.Instance?.CurrentLevel ?? 1;
            int levelScore = ScoreManager.Instance?.CurrentScore ?? 0;
            int maxLevels = LevelManager.Instance?.MaxLevels ?? 9;

            Debug.Log($"[GameManager] HandleLevelCompleteState - Level: {level}, LevelScore: {levelScore}");

            string nextLevelName = "";
            string nextLevelLocation = "";

            if (level < maxLevels)
            {
                int nextLevelNumber = level + 1;
                LevelConfig nextLevelConfig = LevelGenerator.Instance?.GetLevelConfig(nextLevelNumber);

                if (nextLevelConfig != null)
                {
                    nextLevelName = nextLevelConfig.LevelName;
                    nextLevelLocation = nextLevelConfig.Location.ToString();
                }
            }

            UIManager.Instance?.ShowRewardScreen(level, levelScore, nextLevelName, nextLevelLocation);
        }

        private void HandleGameOverState()
        {
            // Keep time running so the death ragdoll can simulate; gameplay is
            // already frozen by state checks (LevelScroller, PlayerController,
            // spawners all gate on GameState.Playing).
            Time.timeScale = 1f;
            Debug.Log($"[GameManager] HandleGameOverState - isInMiniLevelContext: {isInMiniLevelContext}");

            var player = Object.FindAnyObjectByType<PlayerController>();
            player?.PlayDeathAnimation();

            UIManager.Instance?.ShowGameOver();

            // Stop location audio and play game over sound
            StopLocationAudio();

            if (gameOverSound != null && sfxAudioSource != null)
                sfxAudioSource.PlayOneShot(gameOverSound);
        }

        public void StartGame()
        {
            // Only allow starting game from Home state
            if (currentState != GameState.Home)
            {
                Debug.Log($"[GameManager] StartGame BLOCKED - not in Home state (current: {currentState})");
                return;
            }

            Debug.Log("[GameManager] StartGame called - this resets progress!");
            LevelManager.Instance?.ResetProgress();
            SetState(GameState.Playing);
        }

        public void RestartLevel()
        {
            Debug.Log("[GameManager] RestartLevel called");
            // Finalize score from the failed attempt before restarting
            ScoreManager.Instance?.FinalizeLevelScore();
            SetState(GameState.Playing);
        }

        public void ReturnToHome()
        {
            // Finalize current level score before quitting
            ScoreManager.Instance?.FinalizeLevelScore();
            // Save high score before returning home (if player quits mid-run)
            ScoreManager.Instance?.CheckAndSaveHighScore();
            SetState(GameState.Home);
        }

        public void CompleteLevel()
        {
            SetState(GameState.LevelComplete);
        }

        /// <summary>
        /// Called when mini level is complete, transitions to LevelComplete state
        /// </summary>
        public void CompleteMiniLevel()
        {
            SetState(GameState.LevelComplete);
        }

        public void TriggerGameOver()
        {
            // Consume a retry when the player dies
            if (LevelManager.Instance != null)
            {
                LevelManager.Instance.UseRetry();
            }

            SetState(GameState.GameOver);
        }

        /// <summary>
        /// Called when player fails a mini-level. Consumes a retry and shows game over.
        /// </summary>
        public void TriggerMiniLevelGameOver()
        {
            Debug.Log($"[GameManager] TriggerMiniLevelGameOver called - isInMiniLevelContext before: {isInMiniLevelContext}");

            // Consume a retry when the player fails mini-level
            if (LevelManager.Instance != null)
            {
                LevelManager.Instance.UseRetry();
            }

            // Keep the mini-level context flag so retry knows to restart mini-level
            SetState(GameState.GameOver);
        }

        /// <summary>
        /// Restarts the current mini-level (does not restart the running section)
        /// </summary>
        public void RestartMiniLevel()
        {
            Debug.Log("[GameManager] RestartMiniLevel called");
            // Does NOT finalize score - player keeps their running section score
            SetState(GameState.MiniLevel);
        }

        /// <summary>
        /// Play location-specific music and ambient sounds
        /// </summary>
        public void PlayLocationAudio(AudioClip music, AudioClip ambient)
        {
            if (music != null && musicAudioSource != null)
            {
                musicAudioSource.clip = music;
                musicAudioSource.volume = musicVolume;
                musicAudioSource.Play();
                Debug.Log($"Playing music: {music.name} at volume {musicVolume}");
            }

            if (ambient != null && ambientAudioSource != null)
            {
                ambientAudioSource.clip = ambient;
                ambientAudioSource.volume = ambientVolume;
                ambientAudioSource.Play();
                Debug.Log($"Playing ambient: {ambient.name} at volume {ambientVolume}");
            }
            else if (ambient == null)
            {
                Debug.Log("No ambient sound assigned for this location");
            }
        }

        /// <summary>
        /// Stop all location audio (music and ambient)
        /// </summary>
        public void StopLocationAudio()
        {
            if (musicAudioSource != null && musicAudioSource.isPlaying)
                musicAudioSource.Stop();

            if (ambientAudioSource != null && ambientAudioSource.isPlaying)
                ambientAudioSource.Stop();
        }
    }
}
