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

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
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
        }

        private void HandlePlayingState()
        {
            Time.timeScale = 0f;
            UIManager.Instance?.ShowGameHUD();

            // Reset any paused states from previous game over (e.g., palisade minigame failure)
            LevelScroller.Instance?.Resume();
            var player = Object.FindAnyObjectByType<PlayerController>();
            player?.ResumeMovement();

            LevelManager.Instance?.StartLevel();
            CameraStateMachine.Instance?.SetState(CameraStateType.Gameplay);
            UIManager.Instance?.StartCountdown(countdownDuration, OnCountdownComplete);
        }

        private void OnCountdownComplete()
        {
            Time.timeScale = 1f;
        }

        private void HandleLevelCompleteState()
        {
            Time.timeScale = 0f;
            CameraStateMachine.Instance?.SetState(CameraStateType.Start);
        }

        private void HandleGameOverState()
        {
            Time.timeScale = 0f;
            UIManager.Instance?.ShowGameOver();
        }

        public void StartGame()
        {
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

        public void TriggerGameOver()
        {
            // Consume a retry when the player dies
            if (LevelManager.Instance != null)
            {
                LevelManager.Instance.UseRetry();
            }

            SetState(GameState.GameOver);
        }
    }
}
