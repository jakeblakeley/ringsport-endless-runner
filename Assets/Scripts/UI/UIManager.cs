using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RingSport.Core;

namespace RingSport.UI
{
    public class UIManager : MonoBehaviour
    {
        public static UIManager Instance { get; private set; }

        [Header("Screens")]
        [SerializeField] private GameObject homeScreen;
        [SerializeField] private GameObject gameHUD;
        [SerializeField] private GameObject rewardScreen;
        [SerializeField] private GameObject gameOverScreen;

        [Header("Home Screen")]
        [SerializeField] private Button startButton;

        [Header("Game HUD")]
        [SerializeField] private TextMeshProUGUI scoreText;
        [SerializeField] private TextMeshProUGUI levelText;

        [Header("Reward Screen")]
        [SerializeField] private TextMeshProUGUI rewardLevelText;
        [SerializeField] private TextMeshProUGUI rewardScoreText;
        [SerializeField] private Button nextLevelButton;
        [SerializeField] private Button returnHomeButton;

        [Header("Game Over Screen")]
        [SerializeField] private Button retryButton;
        [SerializeField] private Button homeButton;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
        }

        private void Start()
        {
            SetupButtons();
            HideAllScreens();
        }

        private void SetupButtons()
        {
            if (startButton != null)
                startButton.onClick.AddListener(OnStartButtonClicked);

            if (nextLevelButton != null)
                nextLevelButton.onClick.AddListener(OnNextLevelButtonClicked);

            if (returnHomeButton != null)
                returnHomeButton.onClick.AddListener(OnReturnHomeButtonClicked);

            if (retryButton != null)
                retryButton.onClick.AddListener(OnRetryButtonClicked);

            if (homeButton != null)
                homeButton.onClick.AddListener(OnReturnHomeButtonClicked);
        }

        private void HideAllScreens()
        {
            if (homeScreen != null) homeScreen.SetActive(false);
            if (gameHUD != null) gameHUD.SetActive(false);
            if (rewardScreen != null) rewardScreen.SetActive(false);
            if (gameOverScreen != null) gameOverScreen.SetActive(false);
        }

        public void ShowHomeScreen()
        {
            HideAllScreens();
            if (homeScreen != null)
                homeScreen.SetActive(true);
        }

        public void ShowGameHUD()
        {
            HideAllScreens();
            if (gameHUD != null)
            {
                gameHUD.SetActive(true);
                UpdateScore(0);
                UpdateLevel(LevelManager.Instance?.CurrentLevel ?? 1);
            }
        }

        public void ShowRewardScreen(int level, int score)
        {
            HideAllScreens();
            if (rewardScreen != null)
            {
                rewardScreen.SetActive(true);

                if (rewardLevelText != null)
                    rewardLevelText.text = $"Level {level} Complete!";

                if (rewardScoreText != null)
                    rewardScoreText.text = $"Score: {score}";
            }
        }

        public void ShowGameOver()
        {
            HideAllScreens();
            if (gameOverScreen != null)
                gameOverScreen.SetActive(true);
        }

        public void UpdateScore(int score)
        {
            if (scoreText != null)
                scoreText.text = $"Score: {score}";
        }

        public void UpdateLevel(int level)
        {
            if (levelText != null)
                levelText.text = $"Level: {level}";
        }

        private void OnStartButtonClicked()
        {
            GameManager.Instance?.StartGame();
        }

        private void OnNextLevelButtonClicked()
        {
            LevelManager.Instance?.NextLevel();
        }

        private void OnReturnHomeButtonClicked()
        {
            GameManager.Instance?.ReturnToHome();
        }

        private void OnRetryButtonClicked()
        {
            GameManager.Instance?.RestartLevel();
        }
    }
}
