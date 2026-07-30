using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RingSport.Core;
using RingSport.Effects;
using RingSport.Player;
using RingSport.Level;
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

        [Header("Love Notes")]
        [SerializeField] private Button loveNotesButton;
        [SerializeField] private TextMeshProUGUI loveNotesCountText;
        [SerializeField] private GameObject loveNotesNewBadge;
        [SerializeField] private LoveNotesPanel loveNotesPanel;
        [SerializeField] private GameObject loveNoteHudCounter;
        [SerializeField] private TextMeshProUGUI loveNoteHudCountText;
        [SerializeField] private GameObject gameOverLoveNoteCounter;
        [SerializeField] private TextMeshProUGUI gameOverLoveNoteCountText;

        [Header("Secret Note")]
        [SerializeField] private SecretNotePanel secretNotePanel;

        [Header("Game HUD")]
        [SerializeField] private TextMeshProUGUI scoreText;
        [SerializeField] private TextMeshProUGUI levelText;
        [SerializeField] private TextMeshProUGUI livesText;
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
        [Tooltip("The 'YOU DID IT!' banner - hidden when the screen is reused as a level intro")]
        [SerializeField] private GameObject rewardCompleteBanner;

        [Header("Game Over Screen")]
        [SerializeField] private Button retryButton;
        [SerializeField] private TextMeshProUGUI retryButtonText;
        [SerializeField] private TextMeshProUGUI gameOverText;
        [SerializeField] private TextMeshProUGUI gameOverTotalScoreText;
        [SerializeField] private TextMeshProUGUI gameOverHighScoreText;
        [SerializeField] private TextMeshProUGUI gameOverLivesText;
        [SerializeField] private GameObject gameOverNewHighScoreIndicator;
        [SerializeField] private Button homeButton;

        [Header("Minigames")]
        [SerializeField] private PalisadeMinigame palisadeMinigame;

        [Header("Countdown")]
        [SerializeField] private GameObject countdownPanel;
        [SerializeField] private TextMeshProUGUI countdownText;
        [SerializeField] private string[] countdownNumbers = { "3", "2", "1" };
        [SerializeField] private AnimationCurve countdownScaleAnimation = AnimationCurve.EaseInOut(0f, 1.5f, 1f, 1f);

        [Header("Juice")]
        [Tooltip("Tick per countdown digit (temporary clip - see SOUND_EFFECTS.md).")]
        [SerializeField] private AudioClip countdownTickSound;
        [Tooltip("The GO! beat that starts the run (temporary clip - see SOUND_EFFECTS.md).")]
        [SerializeField] private AudioClip countdownGoSound;
        [SerializeField] [Range(0f, 1f)] private float uiSfxVolume = 0.9f;
        [Tooltip("Seconds for the HUD score to roll up to its target after a pickup.")]
        [SerializeField] private float scoreRollSeconds = 0.35f;

        private Coroutine countdownCoroutine;
        private AudioSource uiAudioSource;
        private float displayedScore;
        private int targetScore;
        private bool scoreRolling;
        private CanvasGroup gameOverGroup;
        private Coroutine gameOverFadeRoutine;
        private int lastLoveNoteCount;

        // Reward screen is reused as a level intro (location + level name + start).
        // While set, the "Next Level" button starts the current level instead of
        // advancing to the next one.
        private bool isLevelIntro;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            uiAudioSource = gameObject.AddComponent<AudioSource>();
            uiAudioSource.playOnAwake = false;
        }

        private void Start()
        {
            SetupButtons();
        }

        private void Update()
        {
            // HUD score rolls toward its target instead of snapping. Rate is
            // proportional to the remaining gap (OutExpo feel) with a floor so
            // the tail lands briskly. Unscaled - the HUD lives through
            // timeScale-0 moments.
            if (scoreRolling && scoreText != null)
            {
                float remaining = Mathf.Abs(targetScore - displayedScore);
                float rate = Mathf.Max(remaining / Mathf.Max(0.05f, scoreRollSeconds), 25f);
                displayedScore = Mathf.MoveTowards(displayedScore, targetScore, rate * Time.unscaledDeltaTime);
                scoreText.text = $"{Mathf.RoundToInt(displayedScore)}";

                if (Mathf.Approximately(displayedScore, targetScore))
                    scoreRolling = false;
            }
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

            if (loveNotesButton != null)
                loveNotesButton.onClick.AddListener(OnLoveNotesButtonClicked);
        }

        public void HideAllScreens()
        {
            isLevelIntro = false;
            if (homeScreen != null) homeScreen.SetActive(false);
            if (gameHUD != null) gameHUD.SetActive(false);
            if (rewardScreen != null) rewardScreen.SetActive(false);
            if (gameOverScreen != null) gameOverScreen.SetActive(false);

            // The secret note overlay never lingers across a screen change
            if (secretNotePanel != null) secretNotePanel.Close();
        }

        public void ShowHomeScreen()
        {
            HideAllScreens();
            if (homeScreen != null)
            {
                homeScreen.SetActive(true);

                // Display high score (number only - the "High Score" label is a
                // separate static text below it, like the in-game score column)
                if (highScoreText != null && ScoreManager.Instance != null)
                {
                    int highScore = ScoreManager.Instance.HighScore;
                    highScoreText.text = highScore > 0 ? $"{highScore}" : "-";
                }

                RefreshHomeLoveNotes();

                // Notes grid should never linger open across screen changes
                if (loveNotesPanel != null)
                    loveNotesPanel.Close();
            }
        }

        /// <summary>
        /// Updates the home screen love notes button: "collected/total" count
        /// and the NEW badge when unseen notes are waiting.
        /// </summary>
        public void RefreshHomeLoveNotes()
        {
            if (loveNotesCountText != null)
                loveNotesCountText.text = $"{LoveNoteManager.UnlockedCount}/{LoveNoteManager.TotalCount}";

            if (loveNotesNewBadge != null)
                loveNotesNewBadge.SetActive(LoveNoteManager.HasUnseenNotes);
        }

        /// <summary>
        /// Shows "[icon] xN" under the score while at least one love note has
        /// been collected this run; hidden at zero. Mirrored on the game over
        /// screen so collected notes stay visible on retry.
        /// </summary>
        public void UpdateLoveNoteCounter(int count)
        {
            bool show = count > 0;
            bool increased = count > lastLoveNoteCount;
            lastLoveNoteCount = count;

            if (loveNoteHudCounter != null)
                loveNoteHudCounter.SetActive(show);

            if (loveNoteHudCountText != null)
                loveNoteHudCountText.text = $"x{count}";

            if (increased && loveNoteHudCounter != null && loveNoteHudCounter.activeInHierarchy)
                Juice.PunchScale(loveNoteHudCounter.transform, 0.28f, 0.22f);

            if (gameOverLoveNoteCounter != null)
                gameOverLoveNoteCounter.SetActive(show);

            if (gameOverLoveNoteCountText != null)
                gameOverLoveNoteCountText.text = $"x{count}";
        }

        /// <summary>
        /// Reveals the finale secret note: a big love note with the secret word
        /// under a confetti shower. Shown over the reward screen when the last
        /// level's finish line is crossed; the debug menu opens it from the
        /// home screen.
        /// </summary>
        public void ShowSecretNote()
        {
            if (secretNotePanel != null)
                secretNotePanel.Open();
            else
                Debug.LogWarning("[UIManager] secretNotePanel not assigned - run Tools/RingSport/Setup Secret Note.");
        }

        /// <summary>True while the secret note overlay is up (DebugMenu hides its IMGUI behind it).</summary>
        public bool IsSecretNoteOpen => secretNotePanel != null && secretNotePanel.IsOpen;

        public void ShowGameHUD()
        {
            Debug.Log("[UIManager] ShowGameHUD called");
            HideAllScreens();
            if (gameHUD != null)
            {
                gameHUD.SetActive(true);
                SnapScore(ScoreManager.Instance?.DisplayScore ?? 0);

                // Get level name from config, fallback to "Level X" format
                var levelConfig = LevelGenerator.Instance?.GetCurrentConfig();
                string levelName = !string.IsNullOrEmpty(levelConfig?.LevelName)
                    ? levelConfig.LevelName
                    : $"Level {LevelManager.Instance?.CurrentLevel ?? 1}";
                UpdateLevel(levelName);

                UpdateLives(LevelManager.Instance?.TotalRetries ?? 3f);
                UpdateSprintBar(1f, false); // Reset sprint bar to full
                UpdateLoveNoteCounter(LoveNoteManager.CollectedThisRun);
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

        /// <summary>
        /// Shows the reward screen as a level intro: just the upcoming level's
        /// location and name plus a start button, with the level-complete score
        /// summary hidden. Used by the debug menu to jump to a level's start screen.
        /// </summary>
        public void ShowLevelIntro(string levelName, string levelLocation)
        {
            HideAllScreens();
            if (rewardScreen == null)
            {
                Debug.LogError("[UIManager] ShowLevelIntro called but rewardScreen is NULL!");
                return;
            }

            isLevelIntro = true;
            rewardScreen.SetActive(true);

            if (nextLevelNameText != null)
                nextLevelNameText.text = levelName ?? "";

            if (nextLevelLocationText != null)
                nextLevelLocationText.text = levelLocation ?? "";

            // Nothing has been scored yet - hide the level-complete summary
            if (rewardCompleteBanner != null) rewardCompleteBanner.SetActive(false);
            if (rewardScoreText != null) rewardScoreText.gameObject.SetActive(false);
            if (rewardTotalScoreText != null) rewardTotalScoreText.gameObject.SetActive(false);
            if (rewardHighScoreText != null) rewardHighScoreText.gameObject.SetActive(false);
            if (newHighScoreIndicator != null) newHighScoreIndicator.SetActive(false);

            SetNextLevelButtonText("START");
        }

        public void ShowRewardScreen(int level, int score, string nextLevelName = "", string nextLevelLocation = "")
        {
            HideAllScreens();
            if (rewardScreen != null)
            {
                rewardScreen.SetActive(true);

                // Undo anything the level intro hid
                if (rewardCompleteBanner != null) rewardCompleteBanner.SetActive(true);
                if (rewardTotalScoreText != null) rewardTotalScoreText.gameObject.SetActive(true);
                if (rewardHighScoreText != null) rewardHighScoreText.gameObject.SetActive(true);
                SetNextLevelButtonText("NEXT LEVEL");

                // Display next level in "X/9" format
                if (rewardLevelText != null)
                {
                    int maxLevels = LevelManager.Instance?.MaxLevels ?? 8;
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

                // Hide level score - only show total
                if (rewardScoreText != null)
                    rewardScoreText.gameObject.SetActive(false);

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
                        rewardTotalScoreText.text = $"{totalScore}";

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

                // Panel fades in over the ragdoll instead of hard-cutting
                if (gameOverGroup == null)
                {
                    gameOverGroup = gameOverScreen.GetComponent<CanvasGroup>();
                    if (gameOverGroup == null)
                        gameOverGroup = gameOverScreen.AddComponent<CanvasGroup>();
                }
                gameOverGroup.alpha = 0f;
                if (gameOverFadeRoutine != null)
                    StopCoroutine(gameOverFadeRoutine);
                gameOverFadeRoutine = StartCoroutine(FadeInGameOverPanel(0.3f));

                // Love notes collected this run stay visible on the retry screen
                UpdateLoveNoteCounter(LoveNoteManager.CollectedThisRun);

                // Check if player has retries remaining (use floored TotalRetries)
                int flooredRetries = LevelManager.Instance != null
                    ? Mathf.FloorToInt(LevelManager.Instance.TotalRetries)
                    : 0;
                bool hasRetries = flooredRetries > 0;

                // Update lives text on game over screen
                if (gameOverLivesText != null && LevelManager.Instance != null)
                {
                    float lives = LevelManager.Instance.TotalRetries;
                    if (lives == Mathf.Floor(lives))
                        gameOverLivesText.text = $"{(int)lives}";
                    else
                        gameOverLivesText.text = $"{lives:F1}";
                }

                if (hasRetries)
                {
                    // Player still has retries - show retry button and "Try Again" text
                    if (retryButton != null)
                        retryButton.gameObject.SetActive(true);

                    if (gameOverText != null)
                    {
                        gameOverText.gameObject.SetActive(true);
                        gameOverText.text = "Try Again";
                    }

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
                        gameOverText.text = "Game Over";
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
                        gameOverTotalScoreText.text = $"{totalScore}";

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
            if (scoreText == null)
                return;

            // Pickup pop on the counter, then Update() rolls the number up
            if (score > targetScore && gameHUD != null && gameHUD.activeInHierarchy)
                Juice.PunchScale(scoreText.transform, 0.18f, 0.16f);

            targetScore = score;
            scoreRolling = true;
        }

        /// <summary>Set the score display instantly (screen shows, resets) - no roll, no punch.</summary>
        private void SnapScore(int score)
        {
            targetScore = score;
            displayedScore = score;
            scoreRolling = false;
            if (scoreText != null)
                scoreText.text = $"{score}";
        }

        public void UpdateLevel(string levelName)
        {
            if (levelText != null)
                levelText.text = levelName;
        }

        public void UpdateLives(float lives)
        {
            if (livesText != null)
            {
                if (lives == Mathf.Floor(lives))
                    livesText.text = $"{(int)lives}"; // No decimal for whole numbers
                else
                    livesText.text = $"{lives:F1}"; // Show one decimal place
            }
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
                // Floor the total retries to show whole number (e.g., 4.5 -> 4)
                int retriesLeft = Mathf.FloorToInt(LevelManager.Instance.TotalRetries);
                retryButtonText.text = $"Retry ({retriesLeft})";
                Debug.Log($"[UIManager] Retry button text updated: Retry ({retriesLeft})");
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
            Debug.Log($"[UIManager] StartCountdown called. Duration: {duration}, Panel: {(countdownPanel != null ? countdownPanel.name : "NULL")}, Text: {(countdownText != null ? countdownText.name : "NULL")}");

            if (countdownPanel == null || countdownText == null)
            {
                Debug.LogWarning("Countdown UI not assigned, skipping countdown");
                onComplete?.Invoke();
                return;
            }

            if (countdownCoroutine != null)
            {
                Debug.Log("[UIManager] Stopping existing countdown coroutine");
                StopCoroutine(countdownCoroutine);
            }

            Debug.Log("[UIManager] Starting countdown coroutine");
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
            Debug.Log($"[UIManager] CountdownRoutine started. Panel active before: {countdownPanel.activeSelf}, Parent active: {countdownPanel.transform.parent?.gameObject.activeInHierarchy ?? true}");
            countdownPanel.SetActive(true);
            countdownText.alpha = 1f; // a stopped GO fade may have left it faded
            countdownText.transform.localScale = Vector3.one;

            float timePerNumber = totalDuration / countdownNumbers.Length;

            for (int i = 0; i < countdownNumbers.Length; i++)
            {
                countdownText.text = countdownNumbers[i];
                PlayUiSound(countdownTickSound, 1f + 0.06f * i); // ticks climb slightly

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

            // GO! - the run starts on this beat; the text pops (OutBack) and
            // fades out over the first moments of gameplay
            countdownText.text = "GO!";
            PlayUiSound(countdownGoSound, 1.15f);
            onComplete?.Invoke();

            const float goSeconds = 0.45f;
            float goElapsed = 0f;
            while (goElapsed < goSeconds)
            {
                goElapsed += Time.unscaledDeltaTime;
                float n = Mathf.Clamp01(goElapsed / goSeconds);
                float pop = Juice.OutBack(Mathf.Clamp01(n / 0.45f));
                countdownText.transform.localScale = Vector3.one * Mathf.LerpUnclamped(0.5f, 1f, pop);
                countdownText.alpha = 1f - Mathf.Clamp01((n - 0.5f) / 0.5f);
                yield return null;
            }

            countdownText.alpha = 1f;
            countdownText.transform.localScale = Vector3.one;
            countdownPanel.SetActive(false);
            countdownCoroutine = null;
        }

        private void PlayUiSound(AudioClip clip, float pitch = 1f)
        {
            if (clip == null || uiAudioSource == null)
                return;

            uiAudioSource.pitch = pitch;
            uiAudioSource.PlayOneShot(clip, uiSfxVolume);
        }

        private IEnumerator FadeInGameOverPanel(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                if (gameOverGroup != null)
                    gameOverGroup.alpha = Juice.OutQuad(Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            if (gameOverGroup != null)
                gameOverGroup.alpha = 1f;
            gameOverFadeRoutine = null;
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

        private void OnLoveNotesButtonClicked()
        {
            // Only browsable from the home screen
            if (GameManager.Instance?.CurrentState != GameState.Home)
                return;

            loveNotesPanel?.Open();
        }

        private void SetNextLevelButtonText(string label)
        {
            if (nextLevelButton == null)
                return;

            var text = nextLevelButton.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text != null)
                text.text = label;
        }

        private void OnNextLevelButtonClicked()
        {
            if (isLevelIntro)
            {
                Debug.Log("[UIManager] OnNextLevelButtonClicked - starting previewed level");
                isLevelIntro = false;
                GameManager.Instance?.TransitionToState(GameState.Playing);
                return;
            }

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
            bool inMiniLevelContext = GameManager.Instance?.IsInMiniLevelContext ?? false;
            Debug.Log($"[UIManager] OnRetryButtonClicked - IsInMiniLevelContext: {inMiniLevelContext}");

            // Check if we're retrying from a mini-level failure
            if (inMiniLevelContext)
            {
                Debug.Log("[UIManager] Retrying mini-level only");
                GameManager.Instance?.RestartMiniLevel();
            }
            else
            {
                Debug.Log("[UIManager] Retrying full level");
                GameManager.Instance?.RestartLevel();
            }
        }
    }
}
