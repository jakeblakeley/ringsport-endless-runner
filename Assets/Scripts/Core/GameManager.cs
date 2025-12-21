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

        [Header("Countdown Settings")]
        [SerializeField] private float countdownDuration = 3f;

        public GameState CurrentState => currentState;

        [Header("Audio Settings")]
        [SerializeField] private AudioClip gameOverSound;
        [SerializeField] [Range(0f, 1f)] private float musicVolume = 0.5f;
        [SerializeField] [Range(0f, 1f)] private float ambientVolume = 0.3f;
        [SerializeField] [Range(0f, 1f)] private float sfxVolume = 1.0f;

        private AudioSource musicAudioSource;
        private AudioSource ambientAudioSource;
        private AudioSource sfxAudioSource;

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
            Time.timeScale = 1f;
            UIManager.Instance?.ShowHomeScreen();
            CameraStateMachine.Instance?.SetState(CameraStateType.Start);

            // Stop location audio when returning home
            StopLocationAudio();

            // Load first level's location and start scene for home screen visuals
            LevelGenerator.Instance?.LoadHomeScene();
        }

        private void HandlePlayingState()
        {
            Time.timeScale = 0f;
            UIManager.Instance?.ShowGameHUD();

            // Reset any paused states from previous game over (e.g., palisade minigame failure)
            LevelScroller.Instance?.Resume();
            var player = Object.FindAnyObjectByType<PlayerController>();
            player?.ResetPosition();
            player?.ResumeMovement();

            LevelManager.Instance?.StartLevel();
            CameraStateMachine.Instance?.SetState(CameraStateType.Gameplay);
            UIManager.Instance?.StartCountdown(countdownDuration, OnCountdownComplete);

            // Note: Location audio is started by LevelManager.StartLevel() after level is generated
        }

        private void OnCountdownComplete()
        {
            Time.timeScale = 1f;
        }

        private void HandleMiniLevelState()
        {
            Time.timeScale = 0f;

            // Hide the game HUD and stop any running countdown
            UIManager.Instance?.HideGameHUD();
            UIManager.Instance?.StopCountdown();

            var player = Object.FindAnyObjectByType<PlayerController>();
            player?.ResetPosition();

            CameraStateMachine.Instance?.SetState(CameraStateType.Start);

            // Stop location audio during mini level
            StopLocationAudio();

            // Get current level config to determine mini level type
            LevelConfig currentConfig = LevelGenerator.Instance?.GetCurrentConfig();
            if (currentConfig != null)
            {
                MiniLevelManager.Instance?.StartMiniLevel(currentConfig.MiniLevelType);
            }
            else
            {
                Debug.LogError("No current level config found for mini level!");
                // Fallback: skip directly to level complete
                SetState(GameState.LevelComplete);
            }
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
            Time.timeScale = 0f;
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
