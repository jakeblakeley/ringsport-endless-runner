using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RingSport.Core;
using RingSport.Effects;
using RingSport.Player;
using RingSport.Level;
using System;
using System.Collections;
using System.Collections.Generic;

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
        [Tooltip("NEW HIGH SCORE reveal sting (temporary clip - see SOUND_EFFECTS.md).")]
        [SerializeField] private AudioClip newHighScoreSound;

        private Coroutine countdownCoroutine;
        private AudioSource uiAudioSource;
        private float displayedScore;
        private int targetScore;
        private bool scoreRolling;
        private CanvasGroup gameOverGroup;
        private Coroutine gameOverFadeRoutine;
        private int lastLoveNoteCount;

        // Reward-screen entrance choreography + game-over retry pulse
        private readonly List<Coroutine> rewardEntranceRoutines = new List<Coroutine>();
        private readonly Dictionary<RectTransform, Vector2> entranceBasePositions = new Dictionary<RectTransform, Vector2>();
        private readonly Dictionary<RectTransform, Vector3> entranceBaseScales = new Dictionary<RectTransform, Vector3>();
        private Coroutine rewardCountUpRoutine;
        private Coroutine highScorePulseRoutine;
        private Vector3 newHighScoreBaseScale;

        // Sprint bar smoothing / urgency
        private float sprintFillTarget = 1f;
        private float sprintFillDisplayed = 1f;
        private float sprintFillVelocity;
        private bool sprintExhausted;
        private Color sprintBarCurrentColor;
        private RectTransform sprintBarContainer;
        private Vector2 sprintBarBasePos;
        private bool sprintBarBaseCaptured;
        private float sprintJitterTimer = float.MaxValue;

        // Lives flash + home-screen idle life
        private float lastLivesShown = float.NaN;
        private Color livesBaseColor;
        private bool livesColorCaptured;
        private Coroutine livesFlashRoutine;
        private Coroutine homeIdleRoutine;
        private bool lastBadgeShown;

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

            sprintBarCurrentColor = sprintBarNormalColor;
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

            if (gameHUD != null && gameHUD.activeInHierarchy)
                AnimateSprintBar();
        }

        /// <summary>
        /// Per-frame sprint bar polish: SmoothDamped fill, color crossfade,
        /// low-stamina alpha pulse, and a brief container jitter on exhaustion.
        /// </summary>
        private void AnimateSprintBar()
        {
            float dt = Time.unscaledDeltaTime;

            sprintFillDisplayed = Mathf.SmoothDamp(sprintFillDisplayed, sprintFillTarget, ref sprintFillVelocity, 0.08f, Mathf.Infinity, dt);
            if (sprintBarFillRect != null)
                sprintBarFillRect.anchorMax = new Vector2(sprintFillDisplayed, sprintBarFillRect.anchorMax.y);

            if (sprintBarFill != null)
            {
                Color target = sprintExhausted ? sprintBarExhaustedColor : sprintBarNormalColor;
                sprintBarCurrentColor = Color.Lerp(sprintBarCurrentColor, target, Mathf.Clamp01(dt * 5f));

                float alpha = 1f;
                if (!sprintExhausted && sprintFillTarget < 0.25f)
                    alpha = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 4f); // running-low pulse

                Color final = sprintBarCurrentColor;
                final.a *= alpha;
                sprintBarFill.color = final;
            }

            if (sprintJitterTimer < 0.35f && sprintBarContainer != null)
            {
                sprintJitterTimer += dt;
                float n = Mathf.Clamp01(sprintJitterTimer / 0.35f);
                float x = 4f * Mathf.Sin(sprintJitterTimer * 55f) * (1f - n);
                sprintBarContainer.anchoredPosition = sprintBarBasePos + new Vector2(x, 0f);
                if (n >= 1f)
                    sprintBarContainer.anchoredPosition = sprintBarBasePos;
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
            StopRewardEntrance();
            if (homeIdleRoutine != null)
            {
                StopCoroutine(homeIdleRoutine);
                homeIdleRoutine = null;
            }
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

                if (homeIdleRoutine != null)
                    StopCoroutine(homeIdleRoutine);
                homeIdleRoutine = StartCoroutine(HomeIdleRoutine());
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
            {
                bool show = LoveNoteManager.HasUnseenNotes;
                loveNotesNewBadge.SetActive(show);

                // Bounce the NEW badge in when it first appears
                if (show && !lastBadgeShown && homeScreen != null && homeScreen.activeInHierarchy)
                    Juice.PunchScale(loveNotesNewBadge.transform, 0.45f, 0.3f);
                lastBadgeShown = show;
            }
        }

        /// <summary>
        /// Home-screen idle life: while unseen notes are waiting, the love
        /// notes button wiggles for attention every few seconds. (Button
        /// SCALE belongs to JuicyButton - only rotation is touched here.)
        /// </summary>
        private IEnumerator HomeIdleRoutine()
        {
            while (homeScreen != null && homeScreen.activeInHierarchy)
            {
                yield return new WaitForSecondsRealtime(4f);

                if (homeScreen == null || !homeScreen.activeInHierarchy)
                    break;

                if (loveNotesNewBadge != null && loveNotesNewBadge.activeSelf && loveNotesButton != null)
                    Juice.PunchRotation(loveNotesButton.transform, 6f, 0.5f);
            }
            homeIdleRoutine = null;
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
            {
                Juice.PunchScale(loveNoteHudCounter.transform, 0.28f, 0.22f);
                Juice.PunchRotation(loveNoteHudCounter.transform, 10f, 0.4f);
            }

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

                SnapLives(LevelManager.Instance?.TotalRetries ?? 3f);
                SnapSprintBar();
                UpdateLoveNoteCounter(LoveNoteManager.CollectedThisRun);

                PlayHudEntrance();
            }
        }

        /// <summary>Staggered pop-in for the HUD elements on show.</summary>
        private void PlayHudEntrance()
        {
            EnsureSprintBarContainer();

            float delay = 0.02f;
            AnimateEntrance(levelText != null ? levelText.gameObject : null, delay, popScale: true);
            AnimateEntrance(scoreText != null ? scoreText.gameObject : null, delay += 0.05f, popScale: true);
            AnimateEntrance(livesText != null ? livesText.gameObject : null, delay += 0.05f, popScale: true);
            AnimateEntrance(sprintBarContainer != null ? sprintBarContainer.gameObject : null, delay += 0.05f, popScale: true);
            AnimateEntrance(loveNoteHudCounter, delay += 0.05f, popScale: true);
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

                    // NEW HIGH SCORE reveals after the count-up lands
                    if (newHighScoreIndicator != null)
                        newHighScoreIndicator.SetActive(false);

                    // Total rolls up from what it was before this level banked
                    if (rewardTotalScoreText != null)
                        rewardCountUpRoutine = StartCoroutine(RollRewardScore(
                            Mathf.Max(0, totalScore - score), totalScore, isNewHighScore));
                    else if (isNewHighScore)
                        RevealNewHighScore();

                    if (rewardHighScoreText != null)
                        rewardHighScoreText.text = $"High Score: {highScore}";
                }

                PlayRewardEntrance();
            }
        }

        /// <summary>Staggered entrance for the reward screen's elements.</summary>
        private void PlayRewardEntrance()
        {
            float delay = 0.08f;
            AnimateEntrance(rewardCompleteBanner, delay, popScale: true);
            AnimateEntrance(rewardTotalScoreText != null ? rewardTotalScoreText.gameObject : null, delay += 0.09f);
            AnimateEntrance(rewardHighScoreText != null ? rewardHighScoreText.gameObject : null, delay += 0.09f);
            AnimateEntrance(nextLevelLocationText != null ? nextLevelLocationText.gameObject : null, delay += 0.09f);
            AnimateEntrance(nextLevelNameText != null ? nextLevelNameText.gameObject : null, delay += 0.05f);
            AnimateEntrance(returnHomeButton != null ? returnHomeButton.gameObject : null, delay += 0.09f);
            AnimateEntrance(nextLevelButton != null ? nextLevelButton.gameObject : null, delay += 0.06f);
        }

        private void AnimateEntrance(GameObject go, float delay, bool popScale = false)
        {
            if (go == null || !go.activeInHierarchy)
                return;

            var rt = go.transform as RectTransform;
            if (rt == null)
                return;

            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null)
                cg = go.AddComponent<CanvasGroup>();

            if (!entranceBasePositions.TryGetValue(rt, out Vector2 basePos))
            {
                basePos = rt.anchoredPosition;
                entranceBasePositions[rt] = basePos;
                entranceBaseScales[rt] = rt.localScale;
            }

            rewardEntranceRoutines.Add(StartCoroutine(
                EntranceRoutine(rt, cg, basePos, entranceBaseScales[rt], delay, popScale)));
        }

        private IEnumerator EntranceRoutine(RectTransform rt, CanvasGroup cg, Vector2 basePos, Vector3 baseScale, float delay, bool popScale)
        {
            const float riseDistance = 26f;
            const float duration = 0.28f;
            Vector2 fromPos = basePos - new Vector2(0f, riseDistance);

            cg.alpha = 0f;
            rt.anchoredPosition = fromPos;
            if (popScale)
                rt.localScale = baseScale * 0.85f;

            float wait = 0f;
            while (wait < delay)
            {
                wait += Time.unscaledDeltaTime;
                yield return null;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float n = Mathf.Clamp01(elapsed / duration);
                cg.alpha = n;
                rt.anchoredPosition = Vector2.Lerp(fromPos, basePos, Juice.OutCubic(n));
                if (popScale)
                    rt.localScale = baseScale * Mathf.LerpUnclamped(0.85f, 1f, Juice.OutBack(n));
                yield return null;
            }

            cg.alpha = 1f;
            rt.anchoredPosition = basePos;
            rt.localScale = baseScale;
        }

        private void StopRewardEntrance()
        {
            foreach (var routine in rewardEntranceRoutines)
            {
                if (routine != null)
                    StopCoroutine(routine);
            }
            rewardEntranceRoutines.Clear();

            if (rewardCountUpRoutine != null)
            {
                StopCoroutine(rewardCountUpRoutine);
                rewardCountUpRoutine = null;
            }
            if (highScorePulseRoutine != null)
            {
                StopCoroutine(highScorePulseRoutine);
                highScorePulseRoutine = null;
            }

            // Restore anything an interrupted entrance left mid-flight
            foreach (var pair in entranceBasePositions)
            {
                if (pair.Key == null)
                    continue;
                pair.Key.anchoredPosition = pair.Value;
                if (entranceBaseScales.TryGetValue(pair.Key, out Vector3 baseScale))
                    pair.Key.localScale = baseScale;
                var cg = pair.Key.GetComponent<CanvasGroup>();
                if (cg != null)
                    cg.alpha = 1f;
            }

            if (newHighScoreIndicator != null && newHighScoreBaseScale != Vector3.zero)
                newHighScoreIndicator.transform.localScale = newHighScoreBaseScale;
        }

        private IEnumerator RollRewardScore(int from, int to, bool revealHighScore)
        {
            const float duration = 0.8f;
            int milestone = 0;
            float elapsed = 0f;

            rewardTotalScoreText.text = $"{from}";
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Juice.OutExpo(Mathf.Clamp01(elapsed / duration));
                rewardTotalScoreText.text = $"{Mathf.RoundToInt(Mathf.Lerp(from, to, k))}";

                // Subtle rising ticks at the quarter marks
                while (milestone < 4 && k >= (milestone + 1) * 0.25f)
                {
                    milestone++;
                    PlayUiSound(countdownTickSound, 0.9f + 0.12f * milestone, 0.35f);
                }
                yield return null;
            }

            rewardTotalScoreText.text = $"{to}";
            rewardCountUpRoutine = null;

            if (revealHighScore)
                RevealNewHighScore();
        }

        private void RevealNewHighScore()
        {
            if (newHighScoreIndicator == null)
            {
                Debug.LogWarning("[UIManager] newHighScoreIndicator is NULL! Please assign it in the Inspector.");
                return;
            }

            newHighScoreIndicator.SetActive(true);
            PlayUiSound(newHighScoreSound);

            if (highScorePulseRoutine != null)
                StopCoroutine(highScorePulseRoutine);
            highScorePulseRoutine = StartCoroutine(HighScorePulse());
        }

        private IEnumerator HighScorePulse()
        {
            var target = newHighScoreIndicator.transform;
            if (newHighScoreBaseScale == Vector3.zero)
                newHighScoreBaseScale = target.localScale;

            // OutBack pop-in, then a celebratory breathe while visible
            const float introSeconds = 0.35f;
            float elapsed = 0f;
            while (elapsed < introSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Juice.OutBack(Mathf.Clamp01(elapsed / introSeconds));
                target.localScale = newHighScoreBaseScale * Mathf.LerpUnclamped(0.2f, 1f, k);
                yield return null;
            }

            while (newHighScoreIndicator != null && newHighScoreIndicator.activeInHierarchy)
            {
                float pulse = 1f + 0.05f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * 1.5f);
                target.localScale = newHighScoreBaseScale * pulse;
                yield return null;
            }

            target.localScale = newHighScoreBaseScale;
            highScorePulseRoutine = null;
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

                // The Retry attention pulse lives on its JuicyButton (idlePulse)
            }
        }

        public void UpdateScore(int score)
        {
            if (scoreText == null)
                return;

            // Pickup pop on the counter, then Update() rolls the number up
            if (score > targetScore && gameHUD != null && gameHUD.activeInHierarchy)
                Juice.PunchScale(scoreText.transform, 0.32f, 0.2f);

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
            if (livesText == null)
                return;

            SetLivesText(lives);

            // Flash on real changes while the HUD is up: red shake-down on a
            // lost life, green bounce on a gained one
            bool isRealChange = !float.IsNaN(lastLivesShown) && !Mathf.Approximately(lives, lastLivesShown);
            if (isRealChange && gameHUD != null && gameHUD.activeInHierarchy)
            {
                bool gained = lives > lastLivesShown;
                Juice.PunchScale(livesText.transform, gained ? 0.25f : 0.35f, 0.2f);
                FlashLives(gained ? new Color(0.42f, 0.85f, 0.42f) : new Color(0.91f, 0.3f, 0.24f));
            }
            lastLivesShown = lives;
        }

        /// <summary>Set the lives display without flash (HUD show).</summary>
        private void SnapLives(float lives)
        {
            if (livesText == null)
                return;
            SetLivesText(lives);
            lastLivesShown = lives;
        }

        private void SetLivesText(float lives)
        {
            if (lives == Mathf.Floor(lives))
                livesText.text = $"{(int)lives}"; // No decimal for whole numbers
            else
                livesText.text = $"{lives:F1}"; // Show one decimal place
        }

        private void FlashLives(Color flashColor)
        {
            if (!livesColorCaptured)
            {
                livesBaseColor = livesText.color;
                livesColorCaptured = true;
            }

            if (livesFlashRoutine != null)
                StopCoroutine(livesFlashRoutine);
            livesFlashRoutine = StartCoroutine(LivesFlashRoutine(flashColor));
        }

        private IEnumerator LivesFlashRoutine(Color flashColor)
        {
            const float duration = 0.35f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Juice.OutQuad(Mathf.Clamp01(elapsed / duration));
                if (livesText != null)
                    livesText.color = Color.Lerp(flashColor, livesBaseColor, k);
                yield return null;
            }
            if (livesText != null)
                livesText.color = livesBaseColor;
            livesFlashRoutine = null;
        }

        public void UpdateSprintBar(float fillAmount, bool isExhausted)
        {
            sprintFillTarget = fillAmount;

            // Exhaustion moment: jitter the whole bar (pant SFX pending - see SOUND_EFFECTS.md)
            if (isExhausted && !sprintExhausted)
            {
                EnsureSprintBarContainer();
                sprintJitterTimer = 0f;
            }
            sprintExhausted = isExhausted;
            // Fill, color and pulse are applied per-frame in AnimateSprintBar()
        }

        /// <summary>Snap the bar to full (HUD show) - no visible refill lerp.</summary>
        private void SnapSprintBar()
        {
            sprintFillTarget = 1f;
            sprintFillDisplayed = 1f;
            sprintFillVelocity = 0f;
            sprintExhausted = false;
            sprintBarCurrentColor = sprintBarNormalColor;
            sprintJitterTimer = float.MaxValue;
            EnsureSprintBarContainer();
            if (sprintBarContainer != null && sprintBarBaseCaptured)
                sprintBarContainer.anchoredPosition = sprintBarBasePos;
        }

        private void EnsureSprintBarContainer()
        {
            if (sprintBarContainer != null || sprintBarFillRect == null)
                return;
            sprintBarContainer = sprintBarFillRect.parent as RectTransform;
            if (sprintBarContainer != null && !sprintBarBaseCaptured)
            {
                sprintBarBasePos = sprintBarContainer.anchoredPosition;
                sprintBarBaseCaptured = true;
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

        private void PlayUiSound(AudioClip clip, float pitch = 1f, float volumeScale = 1f)
        {
            if (clip == null || uiAudioSource == null)
                return;

            uiAudioSource.pitch = pitch;
            uiAudioSource.PlayOneShot(clip, uiSfxVolume * volumeScale);
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
