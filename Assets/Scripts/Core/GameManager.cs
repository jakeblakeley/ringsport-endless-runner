using UnityEngine;
using RingSport.UI;

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
        }

        private void HandlePlayingState()
        {
            Time.timeScale = 1f;
            UIManager.Instance?.ShowGameHUD();
            LevelManager.Instance?.StartLevel();
        }

        private void HandleLevelCompleteState()
        {
            Time.timeScale = 0f;
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
            SetState(GameState.Playing);
        }

        public void ReturnToHome()
        {
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
