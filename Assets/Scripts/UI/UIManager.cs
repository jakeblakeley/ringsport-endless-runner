using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RingSport.Core;
using RingSport.Player;
using System;
using System.Collections;

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
        [SerializeField] private TextMeshProUGUI highScoreText;

        [Header("Game HUD")]
        [SerializeField] private TextMeshProUGUI scoreText;
        [SerializeField] private TextMeshProUGUI levelText;
        [SerializeField] private Image sprintBarFill;
        [SerializeField] private RectTransform sprintBarFillRect;
        [SerializeField] private Color sprintBarNormalColor = new Color(0.29f, 0.56f, 0.89f, 1f); // Blue
        [SerializeField] private Color sprintBarExhaustedColor = new Color(0.91f, 0.30f, 0.24f, 1f); // Red

        [Header("Reward Screen")]
        [SerializeField] private TextMeshProUGUI rewardLevelText;
        [SerializeField] private TextMeshProUGUI rewardScoreText;
        [SerializeField] private TextMeshProUGUI rewardTotalScoreText;
        [SerializeField] private TextMeshProUGUI rewardHighScoreText;
        [SerializeField] private GameObject newHighScoreIndicator;
        [SerializeField] private Button nextLevelButton;
        [SerializeField] private Button returnHomeButton;
        [SerializeField] private TextMeshProUGUI nextLevelNameText;
        [SerializeField] private TextMeshProUGUI nextLevelLocationText;

        [Header("Game Over Screen")]
        [SerializeField] private Button retryButton;
        [SerializeField] private TextMeshProUGUI retryButtonText;
        [SerializeField] private TextMeshProUGUI gameOverText;
        [SerializeField] private TextMeshProUGUI gameOverTotalScoreText;
        [SerializeField] private TextMeshProUGUI gameOverHighScoreText;
        [SerializeField] private GameObject gameOverNewHighScoreIndicator;
        [SerializeField] private Button homeButton;

        [Header("Minigames")]
        [SerializeField] private PalisadeMinigame palisadeMinigame;

        [Header("Countdown")]
        [SerializeField] private GameObject countdownPanel;
        [SerializeField] private TextMeshProUGUI countdownText;
        [SerializeField] private string[] countdownNumbers = { "3", "2", "1" };
        [SerializeField] private AnimationCurve countdownScaleAnimation = AnimationCurve.EaseInOut(0f, 1.5f, 1f, 1f);

        private Coroutine countdownCoroutine;

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
            {
                homeScreen.SetActive(true);

                // Display high score
                if (highScoreText != null && ScoreManager.Instance != null)
                {
                    int highScore = ScoreManager.Instance.HighScore;
                    highScoreText.text = highScore > 0 ? $"High Score: {highScore}" : "High Score: -";
                }
            }
        }

        public void ShowGameHUD()
        {
            Debug.Log("[UIManager] ShowGameHUD called");
            HideAllScreens();
            if (gameHUD != null)
            {
                gameHUD.SetActive(true);
                UpdateScore(0);
                UpdateLevel(LevelManager.Instance?.CurrentLevel ?? 1);
                UpdateSprintBar(1f, false); // Reset sprint bar to full
            }
        }

        public void HideGameHUD()
        {
            if (gameHUD != null)
            {
                Debug.Log($"[UIManager] HideGameHUD - hiding {gameHUD.name}, currently active: {gameHUD.activeSelf}");
                gameHUD.SetActive(false);
            }
            else
            {
                Debug.LogError("[UIManager] HideGameHUD called but gameHUD is NULL!");
            }
        }

        public void ShowRewardScreen(int level, int score, string nextLevelName = "", string nextLevelLocation = "")
        {
            HideAllScreens();
            if (rewardScreen != null)
            {
                rewardScreen.SetActive(true);

                // Display next level in "X/9" format
                if (rewardLevelText != null)
                {
                    int maxLevels = LevelManager.Instance?.MaxLevels ?? 9;
                    int nextLevel = level + 1;

                    // if (nextLevel <= maxLevels)
                    // {
                    //     rewardLevelText.text = $"{nextLevel}/{maxLevels}";
                    // }
                    // else
                    // {
                    //     rewardLevelText.text = "All Levels Complete!";
                    // }
                }

                if (rewardScoreText != null)
                    rewardScoreText.text = $"Level Score: {score}";

                // Display next level information
                if (nextLevelNameText != null)
                {
                    if (!string.IsNullOrEmpty(nextLevelName))
                        nextLevelNameText.text = nextLevelName;
                    else
                        nextLevelNameText.text = "";
                }

                if (nextLevelLocationText != null)
                {
                    if (!string.IsNullOrEmpty(nextLevelLocation))
                        nextLevelLocationText.text = nextLevelLocation;
                    else
                        nextLevelLocationText.text = "";
                }

                // Display total score and high score
                if (ScoreManager.Instance != null)
                {
                    int totalScore = ScoreManager.Instance.TotalScore;
                    int highScore = ScoreManager.Instance.HighScore;
                    bool isNewHighScore = ScoreManager.Instance.IsNewHighScore();

                    Debug.Log($"[UIManager] RewardScreen - Total: {totalScore}, High: {highScore}, IsNew: {isNewHighScore}");

                    if (rewardTotalScoreText != null)
                        rewardTotalScoreText.text = $"Total: {totalScore}";

                    if (rewardHighScoreText != null)
                        rewardHighScoreText.text = $"High Score: {highScore}";

                    // Show "NEW HIGH SCORE!" indicator if applicable
                    if (newHighScoreIndicator != null)
                    {
                        newHighScoreIndicator.SetActive(isNewHighScore);
                        Debug.Log($"[UIManager] Setting newHighScoreIndicator active to: {isNewHighScore}");
                    }
                    else
                    {
                        Debug.LogWarning("[UIManager] newHighScoreIndicator is NULL! Please assign it in the Inspector.");
                    }
                }
            }
        }

        public void ShowGameOver()
        {
            HideAllScreens();
            if (gameOverScreen != null)
            {
                gameOverScreen.SetActive(true);

                // Check if player has retries remaining (retry was already consumed in TriggerGameOver)
                bool hasRetries = LevelManager.Instance != null && LevelManager.Instance.RetriesRemaining > 0;

                if (hasRetries)
                {
                    // Player still has retries - show retry button and hide game over text
                    if (retryButton != null)
                        retryButton.gameObject.SetActive(true);

                    if (gameOverText != null)
                        gameOverText.gameObject.SetActive(false);

                    UpdateRetryButtonText();
                }
                else
                {
                    // Player is out of retries - show "Game Over" and hide retry button
                    if (retryButton != null)
                        retryButton.gameObject.SetActive(false);

                    if (gameOverText != null)
                    {
                        gameOverText.gameObject.SetActive(true);
                    }

                    Debug.Log("[UIManager] Out of retries - showing Game Over message");
                }

                // Display total score and high score
                if (ScoreManager.Instance != null)
                {
                    int totalScore = ScoreManager.Instance.TotalScore;
                    int highScore = ScoreManager.Instance.HighScore;
                    bool isNewHighScore = ScoreManager.Instance.IsNewHighScore();

                    Debug.Log($"[UIManager] GameOverScreen - Total: {totalScore}, High: {highScore}, IsNew: {isNewHighScore}, HasRetries: {hasRetries}");

                    if (gameOverTotalScoreText != null)
                        gameOverTotalScoreText.text = $"Total Score: {totalScore}";

                    if (gameOverHighScoreText != null)
                        gameOverHighScoreText.text = $"High Score: {highScore}";

                    // Show "NEW HIGH SCORE!" indicator if applicable (only when out of retries)
                    if (gameOverNewHighScoreIndicator != null)
                    {
                        bool shouldShow = !hasRetries && isNewHighScore;
                        gameOverNewHighScoreIndicator.SetActive(shouldShow);
                        Debug.Log($"[UIManager] Setting gameOverNewHighScoreIndicator active to: {shouldShow}");
                    }
                    else
                    {
                        Debug.LogWarning("[UIManager] gameOverNewHighScoreIndicator is NULL! Please assign it in the Inspector.");
                    }
                }
            }
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

        public void UpdateSprintBar(float fillAmount, bool isExhausted)
        {
            if (sprintBarFillRect != null)
            {
                // Scale width using anchorMax for 9-slice compatibility
                sprintBarFillRect.anchorMax = new Vector2(fillAmount, sprintBarFillRect.anchorMax.y);
            }

            if (sprintBarFill != null)
            {
                sprintBarFill.color = isExhausted ? sprintBarExhaustedColor : sprintBarNormalColor;
            }
        }

        public void UpdateRetryButtonText()
        {
            if (retryButtonText != null && LevelManager.Instance != null)
            {
                int retriesLeft = LevelManager.Instance.RetriesRemaining;
                retryButtonText.text = $"Retry ({retriesLeft} left)";
                Debug.Log($"[UIManager] Retry button text updated: Retry ({retriesLeft} left)");
            }
        }

        public void ShowPalisadeMinigame(int requiredTaps, Vector3 obstaclePosition, float obstacleHeight, PlayerController player)
        {
            Debug.Log($"UIManager.ShowPalisadeMinigame called - requiredTaps: {requiredTaps}, palisadeMinigame: {(palisadeMinigame != null ? "assigned" : "NULL")}");

            if (palisadeMinigame != null)
            {
                palisadeMinigame.StartMinigame(requiredTaps, obstaclePosition, obstacleHeight, player);
            }
            else
            {
                Debug.LogError("PalisadeMinigame reference not set in UIManager!");
            }
        }

        public void StartCountdown(float duration, Action onComplete)
        {
            if (countdownPanel == null || countdownText == null)
            {
                Debug.LogWarning("Countdown UI not assigned, skipping countdown");
                onComplete?.Invoke();
                return;
            }

            if (countdownCoroutine != null)
            {
                StopCoroutine(countdownCoroutine);
            }

            countdownCoroutine = StartCoroutine(CountdownRoutine(duration, onComplete));
        }

        /// <summary>
        /// Stops any running countdown without invoking the callback
        /// </summary>
        public void StopCountdown()
        {
            Debug.Log("[UIManager] StopCountdown called");
            if (countdownCoroutine != null)
            {
                Debug.Log("[UIManager] Stopping active countdown coroutine");
                StopCoroutine(countdownCoroutine);
                countdownCoroutine = null;
            }

            if (countdownPanel != null)
                countdownPanel.SetActive(false);
        }

        private IEnumerator CountdownRoutine(float totalDuration, Action onComplete)
        {
            countdownPanel.SetActive(true);

            float timePerNumber = totalDuration / countdownNumbers.Length;

            for (int i = 0; i < countdownNumbers.Length; i++)
            {
                countdownText.text = countdownNumbers[i];

                float elapsed = 0f;
                while (elapsed < timePerNumber)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float normalizedTime = elapsed / timePerNumber;
                    float scale = countdownScaleAnimation.Evaluate(normalizedTime);
                    countdownText.transform.localScale = Vector3.one * scale;

                    yield return null;
                }
            }

            countdownPanel.SetActive(false);
            countdownCoroutine = null;
            onComplete?.Invoke();
        }

        private void OnStartButtonClicked()
        {
            Debug.Log($"[UIManager] OnStartButtonClicked called. Current state: {GameManager.Instance?.CurrentState}");

            // Only start game if we're on the home screen
            var currentState = GameManager.Instance?.CurrentState;
            if (currentState != GameState.Home)
            {
                Debug.Log($"[UIManager] OnStartButtonClicked BLOCKED - not in Home state (current: {currentState})");
                return;
            }

            Debug.Log("[UIManager] OnStartButtonClicked - calling StartGame (resets progress)");
            GameManager.Instance?.StartGame();
        }

        private void OnNextLevelButtonClicked()
        {
            Debug.Log("[UIManager] OnNextLevelButtonClicked - calling NextLevel");
            LevelManager.Instance?.NextLevel();
        }

        private void OnReturnHomeButtonClicked()
        {
            Debug.Log("[UIManager] OnReturnHomeButtonClicked");
            GameManager.Instance?.ReturnToHome();
        }

        private void OnRetryButtonClicked()
        {
            Debug.Log("[UIManager] OnRetryButtonClicked");
            GameManager.Instance?.RestartLevel();
        }
    }
}
