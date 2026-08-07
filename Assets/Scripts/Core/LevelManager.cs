using System.Collections;
using TMPro;
using UnityEngine;
using RingSport.Effects;
using RingSport.Level;
using RingSport.Player;
using RingSport.UI;

namespace RingSport.Core
{
    public class LevelManager : MonoBehaviour
    {
        public static LevelManager Instance { get; private set; }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Perf harness only: makes the NEXT run begin at this level instead of 1,
        /// so a sample can target a specific world (Hawaii is level 4) without
        /// going through the debug intro panel, which needs a tap the probe can't
        /// give. Consumed on use.
        /// </summary>
        public static int PerfPendingStartLevel;
#endif

        [Header("Level Settings")]
        [SerializeField] private int maxLevels = 8;

        [Header("End Game Settings")]
        [Tooltip("Time before level end to trigger end game behavior (despawn distant obstacles)")]
        [SerializeField] private float endGameWarningTime = 5f;

        [Header("Retry Settings")]
        [SerializeField] private int maxRetries = 3;

        [Header("In-Run Mini Level Settings (flee attack, stop attack)")]
        [Tooltip("Seconds of normal running before the chase begins when retrying a failed in-run mini level (the retry skips the rest of the run)")]
        [SerializeField] private float fleeAttackRetryPreRoll = 4f;
        [Tooltip("Seconds before the chase at which normal spawning stops, so the generated course drains past the player and leaves a clean gap of track (must cover the longest pattern tail ~105u at base speed)")]
        [SerializeField] private float fleeAttackWindDownSeconds = 7.5f;

        [Header("Audio Settings")]
        [SerializeField] private AudioClip[] levelCompleteSounds;
        [SerializeField] private float sfxVolume = 1.0f;
        [Tooltip("Seconds between pickups that keeps the coin-train pitch ladder climbing; a longer gap resets it. Generous on purpose - at 0.6 the ladder dropped out mid-train on normally-spaced coins.")]
        [SerializeField] private float collectComboWindow = 1.5f;

        [Header("Juice Audio")]
        [Tooltip("Airy whoosh when the dog clears a hurdle - pitch and volume follow how tight the clearance was.")]
        [SerializeField] private AudioClip nearMissWhooshSound;
        [SerializeField] [Range(0f, 1f)] private float nearMissVolume = 0.5f;
        [Tooltip("Paper-pop layer played under every confetti burst (finish line, secret note).")]
        [SerializeField] private AudioClip confettiPopSound;
        [SerializeField] [Range(0f, 1f)] private float confettiPopVolume = 0.8f;

        [Header("Finish Line Moment")]
        [Tooltip("Banner font for FINISH! (BarlowCondensed, wired by Tools/RingSport/Setup Juice Polish).")]
        [SerializeField] private TMP_FontAsset bannerFont;
        [Tooltip("Seconds the run-out deceleration takes - world speed ramps to zero while the dog's gait blends down to idle.")]
        [SerializeField] private float finishStopSeconds = 0.9f;
        [Tooltip("Beat held at the full stop (idle pose + dust read) before the reward screen.")]
        [SerializeField] private float finishSettleSeconds = 0.45f;

        private AudioSource sfxAudioSource;
        private AudioSource collectAudioSource; // pitch-laddered, so stings on sfxAudioSource stay at pitch 1
        private AudioSource juiceAudioSource;   // whooshes/pops, pitched per play without disturbing the coin ladder
        private float lastCollectTime = float.NegativeInfinity;
        private int collectComboStep;
        private bool finishMomentActive;

        /// <summary>True during the finish-line celebration beat (blocks deaths).</summary>
        public bool FinishMomentActive => finishMomentActive;

        private int currentLevel = 1;
        private float levelTimer = 0f;
        private float distanceTraveled = 0f;
        private LevelConfig currentLevelConfig;
        private bool hasCalledLevelEnding = false; // Track if we've already called OnLevelEnding
        private bool hasReachedFinishLine = false; // Track if player has reached finish line
        private int retriesRemaining = 3;
        private float partialRetries = 0f; // Track partial lives (0.5 increments)

        // In-run mini level state (flee attack chase, stop attack). The
        // controller is the InRunMiniLevel handling this level's type, or null
        // when the level ends in a regular arena mini level.
        private InRunMiniLevel inRunMiniLevel;
        private bool inRunTriggered = false;
        private bool inRunWindDownStarted = false;
        private int inRunDifficultyIndex = 0;
        private bool pendingInRunEntry = false; // next StartLevel fast-forwards to the chase

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
            GameLog.Info($"[LevelManager] Initialized. Initial retries: {retriesRemaining}");

            // Setup audio source
            sfxAudioSource = gameObject.AddComponent<AudioSource>();
            sfxAudioSource.playOnAwake = false;
            sfxAudioSource.volume = sfxVolume;

            collectAudioSource = gameObject.AddComponent<AudioSource>();
            collectAudioSource.playOnAwake = false;
            collectAudioSource.volume = sfxVolume;

            juiceAudioSource = gameObject.AddComponent<AudioSource>();
            juiceAudioSource.playOnAwake = false;
            juiceAudioSource.volume = sfxVolume;
        }

        private void Update()
        {
            // Only run timer during Playing state
            if (GameManager.Instance?.CurrentState != GameState.Playing)
                return;

            if (currentLevelConfig == null)
                return;

            levelTimer += Time.deltaTime;

            // In-run mini levels (flee attack, stop attack) hand the end of the
            // run to their chase. Spawning stops a wind-down period EARLIER so
            // the generated course scrolls past the player and leaves a clean
            // gap of empty track before the decoy appears (nothing visible is
            // despawned).
            if (inRunMiniLevel != null)
            {
                float chaseStartTime = currentLevelConfig.LevelDuration - inRunMiniLevel.GetLeadSeconds(inRunDifficultyIndex);

                if (!inRunWindDownStarted && levelTimer >= chaseStartTime - fleeAttackWindDownSeconds)
                {
                    inRunWindDownStarted = true;
                    LevelGenerator.Instance?.SetRunnerSpawningSuppressed(true);
                }

                if (!inRunTriggered && levelTimer >= chaseStartTime)
                {
                    inRunTriggered = true;
                    inRunMiniLevel.BeginChase(inRunDifficultyIndex);
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
                GameLog.Error("Failed to get level config!");
                return;
            }

            // In-run mini level setup (flee attack, stop attack): the chase
            // plays in-run at the end of the level. A pending in-run entry
            // (chase retry or debug jump) fast-forwards the timer so only a
            // short pre-roll runs first.
            inRunMiniLevel = InRunMiniLevel.GetController(currentLevelConfig.MiniLevelType);
            inRunTriggered = false;
            inRunWindDownStarted = false;
            inRunDifficultyIndex = inRunMiniLevel != null
                ? ComputeInRunDifficulty(currentLevel, currentLevelConfig.MiniLevelType)
                : 0;

            bool inRunRetryEntry = pendingInRunEntry && inRunMiniLevel != null;
            pendingInRunEntry = false;

            // Reset any leftover chase state on EVERY in-run controller BEFORE
            // arming the retry entry - a controller's cleanup releases the
            // spawn suppression, which must not undo the wind-down set below
            foreach (var controller in InRunMiniLevel.Controllers)
            {
                if (controller != null)
                    controller.OnRunLevelStarted(controller == inRunMiniLevel, inRunRetryEntry && controller == inRunMiniLevel);
            }

            if (inRunRetryEntry)
            {
                float lead = inRunMiniLevel.GetLeadSeconds(inRunDifficultyIndex);
                levelTimer = Mathf.Max(0f, currentLevelConfig.LevelDuration - lead - fleeAttackRetryPreRoll);
                GameLog.Info($"[LevelManager] In-run mini level entry - fast-forwarding level timer to {levelTimer:F1}s");

                // Keep the mini-level context armed through the pre-roll so a
                // death before the chase re-begins still retries the chase,
                // not the whole run (HandlePlayingState just cleared it)
                GameManager.Instance?.NotifyInRunMiniLevelStarted();

                // The pre-roll is inside the wind-down window: keep it a clean,
                // empty approach (also avoids racing LevelGenerator's Update)
                inRunWindDownStarted = true;
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

        public void EndLevel(bool playSting = true)
        {
            // Play random level complete sound (the finish moment already
            // played it when the line was crossed)
            if (playSting)
                PlayLevelCompleteSting();

            // In-run mini level levels already played their mini level in-run
            // (the chase ends just before the finish line), so skip the arena
            // mini-level state and complete directly.
            if (inRunMiniLevel != null && inRunTriggered)
            {
                inRunMiniLevel.NotifyLevelEndReached();
                ScoreManager.Instance?.FinalizeLevelScore();
                GameManager.Instance?.CompleteLevel();
                return;
            }

            // Note: Score finalization is deferred to MiniLevelManager.CompleteMiniLevel()
            // This allows any bonus points from mini levels to be included

            // Trigger mini level state (mini level will then transition to LevelComplete)
            GameManager.Instance?.TransitionToState(GameState.MiniLevel);
        }

        private void PlayLevelCompleteSting()
        {
            if (levelCompleteSounds != null && levelCompleteSounds.Length > 0 && sfxAudioSource != null)
            {
                // Duck the location music under the sting; it stops moments
                // later on LevelComplete and the next level's fade-in resets it
                GameManager.Instance?.SetMusicDuck(true);
                AudioClip randomClip = levelCompleteSounds[Random.Range(0, levelCompleteSounds.Length)];
                sfxAudioSource.PlayOneShot(randomClip);
            }
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
            GameLog.Info("Finish line reached - completing level!");

            // Chase levels end on their own choreography (the catch/stop beat
            // already played) - no extra celebration on top
            if (inRunMiniLevel != null && inRunTriggered)
            {
                EndLevel();
                return;
            }

            StartCoroutine(FinishMomentRoutine());
        }

        /// <summary>
        /// The finish-line beat: FINISH! banner + confetti + FOV pop, then a
        /// run-out stop. There's no slide clip in the Wolf Lite set, so this
        /// is the stop attack's recipe: the world speed ramps to zero while
        /// PauseMovement blends the gait run -> trot -> idle, with a dust puff
        /// and a short settle at the stop. Never Time.timeScale (the animator
        /// runs unscaled); deaths are blocked while this plays.
        /// </summary>
        private IEnumerator FinishMomentRoutine()
        {
            finishMomentActive = true;
            PlayLevelCompleteSting();

            var player = Object.FindAnyObjectByType<PlayerController>();

            ScreenBanner.Show("FINISH!", new Color(1f, 0.84f, 0.25f), 0.8f, 150f, bannerFont);
            if (player != null)
            {
                ImpactVFX.PlayConfettiBurst(player.transform.position + Vector3.up * 1.6f);
                PlayConfettiPops(); // pops ride under the level-complete sting, not instead of it
            }
            CameraStateMachine.Instance?.AddFovKick(6f, 0.6f);

            float startSpeed = LevelScroller.Instance != null ? LevelScroller.Instance.GetScrollSpeed() : 0f;
            float elapsed = 0f;
            bool gaitDropped = false;
            while (elapsed < finishStopSeconds)
            {
                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / finishStopSeconds);
                LevelScroller.Instance?.SetSpeedOverride(startSpeed * (1f - Juice.OutQuad(k)));

                // Drop the gait toward idle partway through the brake - but
                // only once grounded, so a finish-line jump never freezes the
                // dog mid-air
                if (!gaitDropped && k >= 0.3f && player != null && player.IsGrounded)
                {
                    player.PauseMovement();
                    gaitDropped = true;
                }
                yield return null;
            }
            LevelScroller.Instance?.SetSpeedOverride(0f);

            // Still airborne? Let gravity land the dog first, then settle
            float groundWait = 0f;
            while (!gaitDropped && player != null && groundWait < 0.6f)
            {
                if (player.IsGrounded)
                {
                    player.PauseMovement();
                    gaitDropped = true;
                    break;
                }
                groundWait += Time.unscaledDeltaTime;
                yield return null;
            }
            if (!gaitDropped && player != null)
                player.PauseMovement();

            if (player != null)
                ImpactVFX.PlayDust(player.FeetPosition, 7, 0.8f);

            yield return new WaitForSecondsRealtime(finishSettleSeconds);

            // Never leak the pause into the next state (Food Refusal needs movement)
            player?.ResumeMovement();
            LevelScroller.Instance?.ClearSpeedOverride();
            finishMomentActive = false;

            // Bail if something yanked the state mid-beat (debug jumps)
            if (GameManager.Instance?.CurrentState == GameState.Playing)
                EndLevel(playSting: false);
        }

        public void AddScore(int points)
        {
            ScoreManager.Instance?.AddScore(points);
            UIManager.Instance?.UpdateScore(ScoreManager.Instance?.DisplayScore ?? 0);
        }

        /// <summary>
        /// Plays a pickup/feedback clip. comboPitch=true (coins, love notes)
        /// climbs a semitone per quick successive pickup - coin trains sing -
        /// resetting after collectComboWindow. Mini-level stings pass false and
        /// stay at normal pitch on a separate source.
        /// </summary>
        public void PlayCollectSound(AudioClip clip, bool comboPitch = false)
        {
            if (clip == null)
                return;

            if (!comboPitch)
            {
                if (sfxAudioSource != null)
                    sfxAudioSource.PlayOneShot(clip, sfxVolume);
                return;
            }

            if (collectAudioSource == null)
                return;

            if (Time.unscaledTime - lastCollectTime <= collectComboWindow)
                collectComboStep = Mathf.Min(collectComboStep + 1, 7);
            else
                collectComboStep = 0;
            lastCollectTime = Time.unscaledTime;

            collectAudioSource.pitch = Mathf.Pow(2f, collectComboStep / 12f);
            collectAudioSource.PlayOneShot(clip, sfxVolume);
        }

        /// <summary>
        /// Whoosh over a cleared hurdle. clearance01 is 0 for a shave and 1 for
        /// a comfortable leap - a tight clear is higher and louder, so the
        /// near-misses are the ones that sing. Own source, so it never shifts
        /// the coin-train pitch ladder mid-run.
        /// </summary>
        public void PlayNearMissWhoosh(float clearance01)
        {
            if (nearMissWhooshSound == null || juiceAudioSource == null)
                return;

            float tightness = 1f - Mathf.Clamp01(clearance01);
            juiceAudioSource.pitch = Mathf.Lerp(0.92f, 1.18f, tightness);
            juiceAudioSource.PlayOneShot(nearMissWhooshSound, sfxVolume * nearMissVolume * Mathf.Lerp(0.55f, 1f, tightness));
        }

        /// <summary>
        /// The paper-pop layer under a confetti burst - a short stagger of
        /// pitched pops rather than one hit, so it reads as a shower. Shared by
        /// the finish line and the secret-note reveal.
        /// </summary>
        public void PlayConfettiPops()
        {
            if (confettiPopSound == null || juiceAudioSource == null)
                return;

            StartCoroutine(ConfettiPopsRoutine());
        }

        private IEnumerator ConfettiPopsRoutine()
        {
            // Slightly detuned and quieter each time: one burst, three claps
            var pitches = new[] { 1f, 1.14f, 0.9f };
            var volumes = new[] { 1f, 0.7f, 0.5f };
            var delays = new[] { 0f, 0.07f, 0.16f };

            for (int i = 0; i < pitches.Length; i++)
            {
                if (delays[i] > 0f)
                    yield return new WaitForSecondsRealtime(delays[i] - delays[i - 1]);

                if (juiceAudioSource == null)
                    yield break;

                juiceAudioSource.pitch = pitches[i];
                juiceAudioSource.PlayOneShot(confettiPopSound, sfxVolume * confettiPopVolume * volumes[i]);
            }
        }

        /// <summary>
        /// One-shot at an explicit pitch (mini-level urgency ticks). Shares the
        /// pickup source, so pitch is set per play.
        /// </summary>
        public void PlayPitchedSound(AudioClip clip, float pitch, float volumeScale = 1f)
        {
            if (clip == null || collectAudioSource == null)
                return;

            collectAudioSource.pitch = pitch;
            collectAudioSource.PlayOneShot(clip, sfxVolume * volumeScale);
        }

        public void AddDistance(float distance)
        {
            distanceTraveled += distance;
        }

        public void NextLevel()
        {
            GameLog.Info($"[LevelManager] NextLevel called. Current level: {currentLevel}, Max levels: {maxLevels}");
            if (currentLevel < maxLevels)
            {
                currentLevel++;
                GameLog.Info($"[LevelManager] Advancing to level {currentLevel}. Retries NOT reset: {retriesRemaining} remaining");
                // Don't call StartGame() as it resets progress including retries
                // Instead, directly transition to Playing state
                GameManager.Instance?.TransitionToState(GameState.Playing);
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
        /// Completes the current level directly from an in-run mini level that
        /// resolves WITHOUT reaching the finish line (the stop attack: the dog
        /// stopped, so the run is over where it stands). Mirrors EndLevel's
        /// in-run branch: complete sound, chase teardown, score finalize.
        /// </summary>
        public void CompleteInRunMiniLevel()
        {
            PlayLevelCompleteSting();

            inRunMiniLevel?.NotifyLevelEndReached();
            ScoreManager.Instance?.FinalizeLevelScore();
            GameManager.Instance?.CompleteLevel();
        }

        /// <summary>
        /// Enters (or re-enters) an in-run mini level's level fast-forwarded
        /// to just before the chase. Used when retrying a failed chase and by
        /// the debug menu - the retry replays only the chase plus a short
        /// pre-roll, not the whole run.
        /// </summary>
        public void StartAtInRunMiniLevel(int level)
        {
            // Bank whatever score the failed attempt had before StartLevel resets it
            ScoreManager.Instance?.FinalizeLevelScore();

            currentLevel = Mathf.Clamp(level, 1, maxLevels);
            pendingInRunEntry = true;
            GameLog.Info($"[LevelManager] Starting level {currentLevel} at its in-run mini level");
            GameManager.Instance?.TransitionToState(GameState.Playing);
        }

        /// <summary>
        /// Difficulty ordinal of an in-run mini level on the given level: how
        /// many earlier levels run the same type (flee attack: level 3 = 0,
        /// level 5 = 1, level 7 = 2; stop attack: level 4 = 0, level 6 = 1).
        /// </summary>
        private int ComputeInRunDifficulty(int level, MiniLevelType type)
        {
            int index = 0;
            for (int i = 1; i < level; i++)
            {
                var config = LevelGenerator.Instance?.GetLevelConfig(i);
                if (config != null && config.MiniLevelType == type)
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
            GameLog.Info($"[LevelManager] DEBUG: showing intro for level {currentLevel}");
            GameManager.Instance?.DebugShowLevelIntro(currentLevel);
        }

        /// <summary>
        /// Debug menu: host a mini-level jump on the level that actually runs
        /// that mini level, so the reward screen and NEXT LEVEL continue the
        /// run forward from there (N -> N+1 ...) instead of from whatever level
        /// the manager was last left standing on. Starts a fresh run's worth of
        /// retries, matching the level jumps.
        /// </summary>
        public void DebugAdoptLevel(int level)
        {
            ResetProgress();
            currentLevel = Mathf.Clamp(level, 1, maxLevels);
            // Arena mini-level jumps never run StartLevel, so point the score
            // bookkeeping at the hosting level by hand
            ScoreManager.Instance?.StartLevel(currentLevel);
            GameLog.Info($"[LevelManager] DEBUG: hosting mini level jump on level {currentLevel}");
        }
#endif

        public void ResetProgress()
        {
            GameLog.Info("[LevelManager] ResetProgress called - resetting to level 1. Stack trace:");
            GameLog.Info(System.Environment.StackTrace);

            // Reset scores via ScoreManager (handles high score save if applicable)
            ScoreManager.Instance?.ResetForNewRun();

            // Love note unlocks persist across runs; only the HUD counter resets
            LoveNoteManager.ResetRunCounter();

            currentLevel = 1;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (PerfPendingStartLevel > 1)
            {
                currentLevel = Mathf.Clamp(PerfPendingStartLevel, 1, maxLevels);
                PerfPendingStartLevel = 0;
                GameLog.Info($"[LevelManager] Perf harness override: run starts at level {currentLevel}");
            }
#endif
            levelTimer = 0f;
            distanceTraveled = 0f;
            currentLevelConfig = null;
            retriesRemaining = maxRetries;
            partialRetries = 0f;
            GameLog.Info($"[LevelManager] Progress reset. Retries reset to {retriesRemaining}");
        }

        public bool UseRetry()
        {
            if (retriesRemaining > 0)
            {
                retriesRemaining--;
                GameLog.Info($"[LevelManager] Death occurred. Retry consumed. Retries remaining: {retriesRemaining}");

                // Update lives UI
                UIManager.Instance?.UpdateLives(TotalRetries);

                // If this was the last retry, save high score before showing game over
                if (retriesRemaining == 0)
                {
                    ScoreManager.Instance?.CheckAndSaveHighScore();
                    GameLog.Info("[LevelManager] Out of retries! High score checked and saved.");
                }

                return true;
            }

            GameLog.Info("[LevelManager] Death occurred but out of retries!");
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
                GameLog.Info($"[LevelManager] Gained a full retry! Retries: {retriesRemaining}");
            }

            GameLog.Info($"[LevelManager] Added {amount} partial retry. Total: {TotalRetries}");
            UIManager.Instance?.UpdateLives(TotalRetries);
        }

        public float GetLevelDuration()
        {
            return currentLevelConfig != null ? currentLevelConfig.LevelDuration : 60f;
        }
    }
}
