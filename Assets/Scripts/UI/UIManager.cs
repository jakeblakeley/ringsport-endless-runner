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
        [Tooltip("\"Tap to sprint / Swipe to move\" helper shown above the sprint bar during the first countdown of every run - the home screen no longer shows it. Lives on the HUD canvas beside the sprint bar, so this script (not the countdown panel) owns its lifetime (wired by Tools/RingSport/Setup Hats).")]
        [SerializeField] private GameObject countdownInstructions;
        // How long the instructions take to ease away, starting on the "1".
        // Code-owned rather than a [SerializeField]: the scene's stale
        // serialized copy would silently win over the value here.
        private const float InstructionsFadeSeconds = 1.2f;

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
        [Tooltip("Slow attention breathe on the home screen's NEW love-note badge while unseen notes wait.")]
        [SerializeField] private float badgePulseAmount = 0.08f;
        [SerializeField] private float badgePulseHz = 0.7f;

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

        // "Tap to sprint / Swipe to move" line: shown for a run's first
        // countdown, then faded out over the opening seconds of the run
        private CanvasGroup instructionsGroup;
        private Coroutine instructionsFadeRoutine;

        // Lives flash + home-screen idle life
        private float lastLivesShown = float.NaN;
        private Color livesBaseColor;
        private bool livesColorCaptured;
        private Coroutine livesFlashRoutine;
        private Coroutine homeIdleRoutine;
        private Coroutine badgePulseRoutine;
        private Vector3 badgeBaseScale;
        private bool lastBadgeShown;

        // Reward screen is reused as a level intro (location + level name + start).
        // While set, the "Next Level" button starts the current level instead of
        // advancing to the next one.
        private bool isLevelIntro;

        /// <summary>
        /// A styled scene button (the home screen's START) for code-built
        /// overlays to copy their look from - the pill background and the label
        /// font live in the scene, not in anything loadable at runtime.
        /// </summary>
        public Button ButtonStyleTemplate => startButton;

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

        // Last values actually pushed to the UI - writes are skipped while
        // unchanged so a static HUD stops dirtying its canvas every frame
        private int lastShownScore = int.MinValue;
        private float lastAppliedSprintFill = float.MinValue;
        private Color lastAppliedSprintColor = new Color(-1f, -1f, -1f, -1f);

        private static bool ColorChanged(Color a, Color b)
        {
            const float e = 0.002f; // ~half a step at 8 bits per channel
            return Mathf.Abs(a.r - b.r) > e || Mathf.Abs(a.g - b.g) > e ||
                   Mathf.Abs(a.b - b.b) > e || Mathf.Abs(a.a - b.a) > e;
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

                // Only touch TMP when the visible integer changes - each set is
                // a string alloc + mesh rebuild + canvas rebuild
                int rounded = Mathf.RoundToInt(displayedScore);
                if (rounded != lastShownScore)
                {
                    lastShownScore = rounded;
                    scoreText.text = rounded.ToString();
                }

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
            // Skip the RectTransform write once converged - a changed anchorMax
            // dirties the whole HUD canvas every frame otherwise
            if (sprintBarFillRect != null && Mathf.Abs(sprintFillDisplayed - lastAppliedSprintFill) > 0.0005f)
            {
                lastAppliedSprintFill = sprintFillDisplayed;
                sprintBarFillRect.anchorMax = new Vector2(sprintFillDisplayed, sprintBarFillRect.anchorMax.y);
            }

            if (sprintBarFill != null)
            {
                Color target = sprintExhausted ? sprintBarExhaustedColor : sprintBarNormalColor;
                sprintBarCurrentColor = Color.Lerp(sprintBarCurrentColor, target, Mathf.Clamp01(dt * 5f));

                float alpha = 1f;
                if (!sprintExhausted && sprintFillTarget < 0.25f)
                    alpha = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * Mathf.PI * 4f); // running-low pulse

                Color final = sprintBarCurrentColor;
                final.a *= alpha;
                // Same idea for the Graphic color - only write real changes
                if (ColorChanged(final, lastAppliedSprintColor))
                {
                    lastAppliedSprintColor = final;
                    sprintBarFill.color = final;
                }
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

                if (show)
                {
                    // Slow attention breathe while unseen notes wait; OutBack
                    // bounce-in the first time the badge appears
                    bool introPop = !lastBadgeShown && homeScreen != null && homeScreen.activeInHierarchy;
                    StartBadgePulse(introPop);
                }
                else
                {
                    StopBadgePulse();
                }
                lastBadgeShown = show;
            }
        }

        private void StartBadgePulse(bool introPop)
        {
            if (badgePulseRoutine != null)
                StopCoroutine(badgePulseRoutine);
            badgePulseRoutine = StartCoroutine(BadgePulseRoutine(introPop));
        }

        private void StopBadgePulse()
        {
            if (badgePulseRoutine != null)
            {
                StopCoroutine(badgePulseRoutine);
                badgePulseRoutine = null;
            }
            if (loveNotesNewBadge != null && badgeBaseScale != Vector3.zero)
                loveNotesNewBadge.transform.localScale = badgeBaseScale;
        }

        private IEnumerator BadgePulseRoutine(bool introPop)
        {
            var target = loveNotesNewBadge.transform;
            if (badgeBaseScale == Vector3.zero)
                badgeBaseScale = target.localScale;

            if (introPop)
            {
                const float introSeconds = 0.35f;
                float elapsed = 0f;
                while (elapsed < introSeconds)
                {
                    elapsed += Time.unscaledDeltaTime;
                    float k = Juice.OutBack(Mathf.Clamp01(elapsed / introSeconds));
                    target.localScale = badgeBaseScale * Mathf.LerpUnclamped(0.2f, 1f, k);
                    yield return null;
                }
            }

            // Slow breathe, phase-aligned so it grows from rest first
            float pulseTime = 0f;
            while (loveNotesNewBadge != null && loveNotesNewBadge.activeInHierarchy)
            {
                pulseTime += Time.unscaledDeltaTime;
                float pulse = 1f + badgePulseAmount * Mathf.Sin(pulseTime * Mathf.PI * 2f * badgePulseHz);
                target.localScale = badgeBaseScale * pulse;
                yield return null;
            }

            target.localScale = badgeBaseScale;
            badgePulseRoutine = null;
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
                GameLog.Warn("[UIManager] secretNotePanel not assigned - run Tools/RingSport/Setup Secret Note.");
        }

        /// <summary>True while the secret note overlay is up (DebugMenu hides its IMGUI behind it).</summary>
        public bool IsSecretNoteOpen => secretNotePanel != null && secretNotePanel.IsOpen;

        public void ShowGameHUD()
        {
            GameLog.Info("[UIManager] ShowGameHUD called");
            HideAllScreens();
            // The instructions live under the HUD now, so a run that ended
            // mid-fade would otherwise bring its leftover alpha back with it
            HideInstructions();
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

                SnapLives(LevelManager.Instance?.TotalRetries ?? 4f);
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
                GameLog.Info($"[UIManager] HideGameHUD - hiding {gameHUD.name}, currently active: {gameHUD.activeSelf}");
                gameHUD.SetActive(false);
            }
            else
            {
                GameLog.Error("[UIManager] HideGameHUD called but gameHUD is NULL!");
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
                GameLog.Error("[UIManager] ShowLevelIntro called but rewardScreen is NULL!");
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

                    GameLog.Info($"[UIManager] RewardScreen - Total: {totalScore}, High: {highScore}, IsNew: {isNewHighScore}");

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
            // The location, level name and both buttons live under layout groups
            // ("Bottom Text" / "Level Buttons"). SetActive only queues a layout
            // rebuild for the end of the frame, so without this the entrance
            // would capture - and then pin them at - their stale serialised
            // positions, dropping the text off the left edge and stacking the
            // two buttons on top of each other.
            if (rewardScreen != null)
            {
                // ForceUpdateCanvases first: on the screen's very first show the
                // canvas has never been enabled, so its rect - and therefore the
                // width every layout group divides up - is not valid yet.
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)rewardScreen.transform);
            }

            float delay = 0.08f;
            AnimateEntrance(rewardCompleteBanner, delay, popScale: true, recaptureBase: true);
            AnimateEntrance(rewardTotalScoreText != null ? rewardTotalScoreText.gameObject : null, delay += 0.09f, recaptureBase: true);
            AnimateEntrance(rewardHighScoreText != null ? rewardHighScoreText.gameObject : null, delay += 0.09f, recaptureBase: true);
            AnimateEntrance(nextLevelLocationText != null ? nextLevelLocationText.gameObject : null, delay += 0.09f, recaptureBase: true);
            AnimateEntrance(nextLevelNameText != null ? nextLevelNameText.gameObject : null, delay += 0.05f, recaptureBase: true);
            AnimateEntrance(returnHomeButton != null ? returnHomeButton.gameObject : null, delay += 0.09f, recaptureBase: true);
            AnimateEntrance(nextLevelButton != null ? nextLevelButton.gameObject : null, delay += 0.06f, recaptureBase: true);
        }

        /// <param name="recaptureBase">
        /// Re-read the rest position instead of trusting the cached one. Layout
        /// groups recompute their children whenever the screen is shown, so a
        /// position cached on an earlier show can be out of date.
        /// </param>
        private void AnimateEntrance(GameObject go, float delay, bool popScale = false, bool recaptureBase = false)
        {
            if (go == null || !go.activeInHierarchy)
                return;

            var rt = go.transform as RectTransform;
            if (rt == null)
                return;

            var cg = go.GetComponent<CanvasGroup>();
            if (cg == null)
                cg = go.AddComponent<CanvasGroup>();

            // Safe to re-read: HideAllScreens stops any in-flight entrance and
            // restores every rect to its base position before we get here.
            if (recaptureBase || !entranceBasePositions.TryGetValue(rt, out Vector2 basePos))
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
            int shownValue = from;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Juice.OutExpo(Mathf.Clamp01(elapsed / duration));
                // Write-on-change like the HUD roll: a per-frame string +
                // TMP re-tessellation for an unchanged number is pure waste
                int value = Mathf.RoundToInt(Mathf.Lerp(from, to, k));
                if (value != shownValue)
                {
                    shownValue = value;
                    rewardTotalScoreText.text = value.ToString();
                }

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
                GameLog.Warn("[UIManager] newHighScoreIndicator is NULL! Please assign it in the Inspector.");
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

                    GameLog.Info("[UIManager] Out of retries - showing Game Over message");
                }

                // Display total score and high score
                if (ScoreManager.Instance != null)
                {
                    int totalScore = ScoreManager.Instance.TotalScore;
                    int highScore = ScoreManager.Instance.HighScore;
                    bool isNewHighScore = ScoreManager.Instance.IsNewHighScore();

                    GameLog.Info($"[UIManager] GameOverScreen - Total: {totalScore}, High: {highScore}, IsNew: {isNewHighScore}, HasRetries: {hasRetries}");

                    if (gameOverTotalScoreText != null)
                        gameOverTotalScoreText.text = $"{totalScore}";

                    if (gameOverHighScoreText != null)
                        gameOverHighScoreText.text = $"High Score: {highScore}";

                    // Show "NEW HIGH SCORE!" indicator if applicable (only when out of retries)
                    if (gameOverNewHighScoreIndicator != null)
                    {
                        bool shouldShow = !hasRetries && isNewHighScore;
                        gameOverNewHighScoreIndicator.SetActive(shouldShow);
                        GameLog.Info($"[UIManager] Setting gameOverNewHighScoreIndicator active to: {shouldShow}");
                    }
                    else
                    {
                        GameLog.Warn("[UIManager] gameOverNewHighScoreIndicator is NULL! Please assign it in the Inspector.");
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
            lastShownScore = score;
            if (scoreText != null)
                scoreText.text = score.ToString();
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
                GameLog.Info($"[UIManager] Retry button text updated: Retry ({retriesLeft})");
            }
        }

        public void ShowPalisadeMinigame(int requiredTaps, Vector3 obstacleContactPoint, float obstacleHeight, PlayerController player)
        {
            GameLog.Info($"UIManager.ShowPalisadeMinigame called - requiredTaps: {requiredTaps}, palisadeMinigame: {(palisadeMinigame != null ? "assigned" : "NULL")}");

            if (palisadeMinigame != null)
            {
                palisadeMinigame.StartMinigame(requiredTaps, obstacleContactPoint, obstacleHeight, player);
            }
            else
            {
                GameLog.Error("PalisadeMinigame reference not set in UIManager!");
            }
        }

        /// <summary>
        /// Runs the 3-2-1-GO countdown, calling back on GO. <paramref name="startDelay"/>
        /// holds the panel hidden first - the run countdown is kicked off while the
        /// level transition is still fading, so it waits for the level to be visible.
        /// </summary>
        public void StartCountdown(float duration, float startDelay, Action onComplete, bool showInstructions = false)
        {
            GameLog.Info($"[UIManager] StartCountdown called. Duration: {duration}, Delay: {startDelay}, Panel: {(countdownPanel != null ? countdownPanel.name : "NULL")}, Text: {(countdownText != null ? countdownText.name : "NULL")}");

            if (countdownPanel == null || countdownText == null)
            {
                GameLog.Warn("Countdown UI not assigned, skipping countdown");
                onComplete?.Invoke();
                return;
            }

            if (countdownCoroutine != null)
            {
                GameLog.Info("[UIManager] Stopping existing countdown coroutine");
                StopCoroutine(countdownCoroutine);
            }

            GameLog.Info("[UIManager] Starting countdown coroutine");
            countdownCoroutine = StartCoroutine(CountdownRoutine(duration, startDelay, onComplete, showInstructions));
        }

        /// <summary>Countdown with no lead-in (mini levels, which start from a visible screen).</summary>
        public void StartCountdown(float duration, Action onComplete) => StartCountdown(duration, 0f, onComplete);

        /// <summary>
        /// Stops any running countdown without invoking the callback
        /// </summary>
        public void StopCountdown()
        {
            GameLog.Info("[UIManager] StopCountdown called");
            if (countdownCoroutine != null)
            {
                GameLog.Info("[UIManager] Stopping active countdown coroutine");
                StopCoroutine(countdownCoroutine);
                countdownCoroutine = null;
            }

            if (countdownPanel != null)
                countdownPanel.SetActive(false);

            HideInstructions();
        }

        /// <summary>
        /// Show or hide the "how to play" line instantly. It sits on the HUD
        /// canvas next to the sprint bar rather than inside the countdown
        /// panel, so nothing hides it on the panel's way out - every path that
        /// takes the countdown down has to come through here.
        /// </summary>
        private void HideInstructions()
        {
            if (instructionsFadeRoutine != null)
            {
                StopCoroutine(instructionsFadeRoutine);
                instructionsFadeRoutine = null;
            }

            if (countdownInstructions != null)
                countdownInstructions.SetActive(false);
        }

        private void ShowInstructions()
        {
            if (countdownInstructions == null)
                return;

            if (instructionsFadeRoutine != null)
            {
                StopCoroutine(instructionsFadeRoutine);
                instructionsFadeRoutine = null;
            }

            if (instructionsGroup == null)
            {
                instructionsGroup = countdownInstructions.GetComponent<CanvasGroup>();
                if (instructionsGroup == null)
                    instructionsGroup = countdownInstructions.AddComponent<CanvasGroup>();
            }

            // A run cut short mid-fade must not leave the next one faded
            instructionsGroup.alpha = 1f;
            countdownInstructions.SetActive(true);
        }

        /// <summary>
        /// Eases the line away from the moment the countdown hits its last
        /// number - the player has had it in front of them since the level
        /// faded in, so it clears out as the run begins rather than sitting
        /// over the opening seconds of it. Unscaled, like the countdown.
        /// </summary>
        private IEnumerator FadeOutInstructions()
        {
            float elapsed = 0f;
            while (elapsed < InstructionsFadeSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                if (instructionsGroup != null)
                    instructionsGroup.alpha = 1f - Juice.OutQuad(Mathf.Clamp01(elapsed / InstructionsFadeSeconds));
                yield return null;
            }

            instructionsFadeRoutine = null;
            HideInstructions();
        }

        private IEnumerator CountdownRoutine(float totalDuration, float startDelay, Action onComplete, bool showInstructions)
        {
            GameLog.Info($"[UIManager] CountdownRoutine started. Panel active before: {countdownPanel.activeSelf}, Parent active: {countdownPanel.transform.parent?.gameObject.activeInHierarchy ?? true}");

            // Sit out the level transition first - the coroutine lives on the
            // UIManager, so hiding the panel here doesn't stop it
            if (startDelay > 0f)
            {
                countdownPanel.SetActive(false);
                yield return new WaitForSecondsRealtime(startDelay);
            }

            countdownPanel.SetActive(true);
            if (showInstructions)
                ShowInstructions();
            else
                HideInstructions();
            countdownText.alpha = 1f; // a stopped GO fade may have left it faded
            countdownText.transform.localScale = Vector3.one;

            float timePerNumber = totalDuration / countdownNumbers.Length;

            for (int i = 0; i < countdownNumbers.Length; i++)
            {
                countdownText.text = countdownNumbers[i];
                PlayUiSound(countdownTickSound, 1f + 0.06f * i); // ticks climb slightly

                // The line starts easing away on the last number, so it clears
                // out just as the run begins
                if (i == countdownNumbers.Length - 1 && showInstructions
                    && countdownInstructions != null && countdownInstructions.activeSelf)
                    instructionsFadeRoutine = StartCoroutine(FadeOutInstructions());

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
            GameLog.Info($"[UIManager] OnStartButtonClicked called. Current state: {GameManager.Instance?.CurrentState}");

            // Only start game if we're on the home screen
            var currentState = GameManager.Instance?.CurrentState;
            if (currentState != GameState.Home)
            {
                GameLog.Info($"[UIManager] OnStartButtonClicked BLOCKED - not in Home state (current: {currentState})");
                return;
            }

            GameLog.Info("[UIManager] OnStartButtonClicked - calling StartGame (resets progress)");
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
                GameLog.Info("[UIManager] OnNextLevelButtonClicked - starting previewed level");
                isLevelIntro = false;
                GameManager.Instance?.TransitionToState(GameState.Playing);
                return;
            }

            GameLog.Info("[UIManager] OnNextLevelButtonClicked - calling NextLevel");
            LevelManager.Instance?.NextLevel();
        }

        private void OnReturnHomeButtonClicked()
        {
            GameLog.Info("[UIManager] OnReturnHomeButtonClicked");
            GameManager.Instance?.ReturnToHome();
        }

        private void OnRetryButtonClicked()
        {
            bool inMiniLevelContext = GameManager.Instance?.IsInMiniLevelContext ?? false;
            GameLog.Info($"[UIManager] OnRetryButtonClicked - IsInMiniLevelContext: {inMiniLevelContext}");

            // Check if we're retrying from a mini-level failure
            if (inMiniLevelContext)
            {
                GameLog.Info("[UIManager] Retrying mini-level only");
                GameManager.Instance?.RestartMiniLevel();
            }
            else
            {
                GameLog.Info("[UIManager] Retrying full level");
                GameManager.Instance?.RestartLevel();
            }
        }
    }
}
