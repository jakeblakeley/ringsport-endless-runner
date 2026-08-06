using System.Collections;
using UnityEngine;
using RingSport.UI;
using RingSport.Level;
using RingSport.Player;
using RingSport.Effects;

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

        [Header("Home Screen")]
        [Tooltip("Model yaw for the dog greeting the player on the home screen - aims it at the angled Start camera (180 would face straight back down the track).")]
        [SerializeField] private float homeDogFacingYaw = 138f;

        public GameState CurrentState => currentState;
        public bool IsInMiniLevelContext => isInMiniLevelContext;

        [Header("Audio Settings")]
        [SerializeField] private AudioClip gameOverSound;
        [Tooltip("Impact thud at the exact moment of a run death (temporary clip - see SOUND_EFFECTS.md).")]
        [SerializeField] private AudioClip deathImpactSound;
        [SerializeField] [Range(0f, 1f)] private float musicVolume = 0.5f;
        [SerializeField] [Range(0f, 1f)] private float ambientVolume = 0.3f;
        [SerializeField] [Range(0f, 1f)] private float sfxVolume = 1.0f;

        [Header("Death Feel")]
        [Tooltip("Seconds the world freezes on the hit (scroll pinned, dog frozen mid-pose) before the ragdoll launches.")]
        [SerializeField] private float deathHitStopSeconds = 0.09f;
        [Tooltip("Seconds before the game over panel fades in, letting the ragdoll (run deaths) or the fail banner (mini levels) read first.")]
        [SerializeField] private float gameOverPanelDelay = 1.0f;

        private AudioSource musicAudioSource;
        private AudioSource ambientAudioSource;
        private AudioSource sfxAudioSource;
        private Coroutine musicFadeRoutine;
        private Coroutine duckRoutine;
        private bool deathSequenceRunning;

        /// <summary>True during the run-death impact beat (guards competing scroll overrides).</summary>
        public bool DeathSequenceRunning => deathSequenceRunning;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // When set, HandleMiniLevelState launches this mini level instead of the
        // current level config's. Persists across retries so the game over ->
        // retry flow restarts the same mini level; cleared on Home/Playing.
        private MiniLevelType? debugMiniLevelOverride;

        /// <summary>
        /// Debug menu: jump straight into a specific mini level, hosted on the
        /// first level that runs it. Adopting that level is what lets the run
        /// carry on forward afterwards - the reward screen names the right
        /// level and NEXT LEVEL walks into level N+1 - so a mini level whose
        /// own end beat is hard to reach (the face attack) is still a usable
        /// jumping-off point for testing the levels after it.
        /// </summary>
        public void DebugStartMiniLevel(MiniLevelType type)
        {
            debugMiniLevelOverride = type;

            int hostLevel = LevelGenerator.Instance?.FindFirstLevelWithMiniLevel(type) ?? -1;
            if (hostLevel >= 1)
                LevelManager.Instance?.DebugAdoptLevel(hostLevel);
            else
                GameLog.Warn($"[GameManager] DEBUG: no level runs the {type} mini level - the run will continue from level {LevelManager.Instance?.CurrentLevel ?? 1}");

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
            CameraStateMachine.Instance?.SetState(CameraStateType.Home);
            // Match the real home screen: dog idles facing the home camera
            var introPlayer = Object.FindAnyObjectByType<PlayerController>();
            introPlayer?.Animations?.SetFacing(true, homeDogFacingYaw);
            introPlayer?.Animations?.SetIdleFlourishes(true);
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

        /// <summary>
        /// SetState wrapped in a quick fade-to-black, hiding the hard screen
        /// swap and the world resets behind it (player teleport, camera snap).
        /// If a fade is already covering the screen, the swap runs immediately
        /// under it. Death does NOT come through here - TriggerGameOver has
        /// its own impact beat.
        /// </summary>
        public void TransitionToState(GameState newState)
        {
            ScreenFader.Instance.FadeSwap(() => SetState(newState));
        }

        public void SetState(GameState newState)
        {
            previousState = currentState;
            currentState = newState;

            // Idle flourishes (bark, shake...) only ever run on the home screen
            Object.FindAnyObjectByType<PlayerController>()?.Animations
                ?.SetIdleFlourishes(newState == GameState.Home);

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

            // The dog greets the player on the home screen: clear any death
            // ragdoll or mid-run pose, then idle facing the close-up home
            // camera. On scene load both the facing and the camera snap;
            // returning home (quit / game over) plays the walk-turn and the
            // camera transition instead.
            var player = Object.FindAnyObjectByType<PlayerController>();
            player?.ResetPosition();
            if (previousState == GameState.Home)
            {
                player?.Animations?.SetFacingImmediate(true, homeDogFacingYaw);
                CameraStateMachine.Instance?.SetStateImmediate(CameraStateType.Home);
            }
            else
            {
                player?.Animations?.SetFacing(true, homeDogFacingYaw);
                CameraStateMachine.Instance?.SetState(CameraStateType.Home);
            }

            UIManager.Instance?.ShowHomeScreen();

            // Stop location audio when returning home
            StopLocationAudio(0.3f);

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
            LevelScroller.Instance?.ClearSpeedOverride();
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

            GameLog.Info($"[GameManager] HandlePlayingState - About to start countdown. UIManager exists: {UIManager.Instance != null}");
            UIManager.Instance?.StartCountdown(countdownDuration, OnCountdownComplete);

            // Note: Location audio is started by LevelManager.StartLevel() after level is generated
        }

        private void OnCountdownComplete()
        {
            Time.timeScale = 1f;
        }

        private void HandleMiniLevelState()
        {
            // Flee attack and stop attack are IN-RUN mini levels: they play
            // during the Playing state at the end of their level, not in the
            // arena flow. Reaching this state with one pending (chase-death
            // retry, or the debug menu) reroutes into a short run that jumps
            // straight to the chase.
            if (TryRouteInRunMiniLevelEntry())
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
            StopLocationAudio(0.3f);

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
                GameLog.Error("No current level config found for mini level!");
                // Fallback: skip directly to level complete
                SetState(GameState.LevelComplete);
            }
        }

        /// <summary>
        /// If the mini level about to start is an in-run one (flee attack,
        /// stop attack), routes back into the Playing state fast-forwarded to
        /// the chase and returns true. Handles both the current level's config
        /// and the debug override.
        /// </summary>
        private bool TryRouteInRunMiniLevelEntry()
        {
            if (LevelManager.Instance == null)
                return false;

            LevelConfig config = LevelGenerator.Instance?.GetCurrentConfig();
            MiniLevelType effectiveType = config != null ? config.MiniLevelType : MiniLevelType.PositionsSimonSays;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (debugMiniLevelOverride.HasValue)
                effectiveType = debugMiniLevelOverride.Value;
#endif
            if (InRunMiniLevel.GetController(effectiveType) == null)
                return false;

            // Retry on a level of this type re-enters that level's chase; a
            // debug jump hosts the chase on the first level that runs this mini
            // level (NOT the stale config's level - the type can appear twice,
            // and DebugStartMiniLevel has already adopted the first one)
            bool isDebugJump = false;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            isDebugJump = debugMiniLevelOverride.HasValue;
#endif
            int targetLevel = (!isDebugJump && config != null && config.MiniLevelType == effectiveType)
                ? LevelManager.Instance.CurrentLevel
                : LevelGenerator.Instance?.FindFirstLevelWithMiniLevel(effectiveType) ?? -1;

            if (targetLevel < 1)
            {
                GameLog.Warn($"[GameManager] No level uses the {effectiveType} mini level - cannot route in-run entry");
                return false;
            }

            GameLog.Info($"[GameManager] Routing {effectiveType} mini level into in-run chase on level {targetLevel}");
            LevelManager.Instance.StartAtInRunMiniLevel(targetLevel);
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
            StopLocationAudio(0.3f);

            // Show reward screen
            int level = LevelManager.Instance?.CurrentLevel ?? 1;
            int levelScore = ScoreManager.Instance?.CurrentScore ?? 0;
            int maxLevels = LevelManager.Instance?.MaxLevels ?? 8;

            GameLog.Info($"[GameManager] HandleLevelCompleteState - Level: {level}, LevelScore: {levelScore}");

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

            // The last level's finish line ends the game: reveal the secret
            // note (big love note + confetti) on top of the reward screen
            if (level >= maxLevels)
                UIManager.Instance?.ShowSecretNote();
        }

        private void HandleGameOverState()
        {
            // Keep time running so the death ragdoll can simulate; gameplay is
            // already frozen by state checks (LevelScroller, PlayerController,
            // spawners all gate on GameState.Playing).
            Time.timeScale = 1f;
            GameLog.Info($"[GameManager] HandleGameOverState - isInMiniLevelContext: {isInMiniLevelContext}");

            var player = Object.FindAnyObjectByType<PlayerController>();
            player?.PlayDeathAnimation();

            // Music ducks out instead of cutting; the panel holds back so the
            // ragdoll (run deaths) or the fail banner (mini levels) can read,
            // then fades in together with the game-over sting.
            StopLocationAudio(0.35f);
            StartCoroutine(ShowGameOverDelayed());
        }

        private IEnumerator ShowGameOverDelayed()
        {
            yield return new WaitForSecondsRealtime(gameOverPanelDelay);

            // The state may have moved on (debug jumps) while we waited
            if (currentState != GameState.GameOver)
                yield break;

            UIManager.Instance?.ShowGameOver();

            if (gameOverSound != null && sfxAudioSource != null)
                sfxAudioSource.PlayOneShot(gameOverSound);
        }

        public void StartGame()
        {
            // Only allow starting game from Home state
            if (currentState != GameState.Home)
            {
                GameLog.Info($"[GameManager] StartGame BLOCKED - not in Home state (current: {currentState})");
                return;
            }

            GameLog.Info("[GameManager] StartGame called - this resets progress!");
            LevelManager.Instance?.ResetProgress();
            TransitionToState(GameState.Playing);
        }

        public void RestartLevel()
        {
            GameLog.Info("[GameManager] RestartLevel called");
            // Finalize score from the failed attempt before restarting
            ScoreManager.Instance?.FinalizeLevelScore();
            TransitionToState(GameState.Playing);
        }

        public void ReturnToHome()
        {
            // Finalize current level score before quitting
            ScoreManager.Instance?.FinalizeLevelScore();
            // Save high score before returning home (if player quits mid-run)
            ScoreManager.Instance?.CheckAndSaveHighScore();
            TransitionToState(GameState.Home);
        }

        public void CompleteLevel()
        {
            TransitionToState(GameState.LevelComplete);
        }

        /// <summary>
        /// Called when mini level is complete, transitions to LevelComplete state
        /// </summary>
        public void CompleteMiniLevel()
        {
            TransitionToState(GameState.LevelComplete);
        }

        public void TriggerGameOver()
        {
            // A hit during an already-running death (or the finish-line
            // celebration beat) can't kill the player twice
            if (currentState == GameState.GameOver || deathSequenceRunning)
                return;
            if (LevelManager.Instance != null && LevelManager.Instance.FinishMomentActive)
                return;

            // Consume a retry when the player dies
            LevelManager.Instance?.UseRetry();

            StartCoroutine(DeathImpactSequence());
        }

        /// <summary>
        /// The impact beat of a run death: shake + red flash + thud, then a
        /// short hit-stop before the ragdoll takes over. Time.timeScale is NOT
        /// used - the animator runs on unscaled time - so the freeze is the
        /// scroll-override + movement-pause + animator-pause trio (same
        /// pattern as the face attack's bullet time).
        /// </summary>
        private IEnumerator DeathImpactSequence()
        {
            deathSequenceRunning = true;

            CameraStateMachine.Instance?.AddShake(0.55f);
            ScreenFader.Instance.Flash(new Color(0.85f, 0.12f, 0.1f), 0.35f, 0.05f, 0.32f);
            if (deathImpactSound != null && sfxAudioSource != null)
                sfxAudioSource.PlayOneShot(deathImpactSound);

            var player = Object.FindAnyObjectByType<PlayerController>();
            LevelScroller.Instance?.SetSpeedOverride(0f);
            player?.PauseMovement();
            player?.Animations?.SetAnimatorPaused(true);

            yield return new WaitForSecondsRealtime(deathHitStopSeconds);

            player?.Animations?.SetAnimatorPaused(false);
            LevelScroller.Instance?.ClearSpeedOverride();

            deathSequenceRunning = false;
            SetState(GameState.GameOver);
        }

        /// <summary>
        /// Called when player fails a mini-level. Consumes a retry and shows game over.
        /// </summary>
        public void TriggerMiniLevelGameOver()
        {
            if (currentState == GameState.GameOver || deathSequenceRunning)
                return;

            GameLog.Info($"[GameManager] TriggerMiniLevelGameOver called - isInMiniLevelContext before: {isInMiniLevelContext}");

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
            GameLog.Info("[GameManager] RestartMiniLevel called");
            // Does NOT finalize score - player keeps their running section score
            TransitionToState(GameState.MiniLevel);
        }

        /// <summary>
        /// Play location-specific music and ambient sounds (0.5s fade-in
        /// instead of a hard start).
        /// </summary>
        public void PlayLocationAudio(AudioClip music, AudioClip ambient)
        {
            if (musicFadeRoutine != null)
            {
                StopCoroutine(musicFadeRoutine);
                musicFadeRoutine = null;
            }
            if (duckRoutine != null)
            {
                StopCoroutine(duckRoutine);
                duckRoutine = null;
            }

            bool anyStarted = false;

            if (music != null && musicAudioSource != null)
            {
                musicAudioSource.clip = music;
                musicAudioSource.volume = 0f;
                musicAudioSource.Play();
                anyStarted = true;
                GameLog.Info($"Playing music: {music.name} at volume {musicVolume}");
            }

            if (ambient != null && ambientAudioSource != null)
            {
                ambientAudioSource.clip = ambient;
                ambientAudioSource.volume = 0f;
                ambientAudioSource.Play();
                anyStarted = true;
            }

            if (anyStarted)
                musicFadeRoutine = StartCoroutine(FadeLocationAudioIn(0.5f));
        }

        private IEnumerator FadeLocationAudioIn(float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float f = Mathf.Clamp01(elapsed / duration);
                if (musicAudioSource != null && musicAudioSource.isPlaying)
                    musicAudioSource.volume = musicVolume * f;
                if (ambientAudioSource != null && ambientAudioSource.isPlaying)
                    ambientAudioSource.volume = ambientVolume * f;
                yield return null;
            }

            if (musicAudioSource != null && musicAudioSource.isPlaying)
                musicAudioSource.volume = musicVolume;
            if (ambientAudioSource != null && ambientAudioSource.isPlaying)
                ambientAudioSource.volume = ambientVolume;
            musicFadeRoutine = null;
        }

        /// <summary>
        /// Stop all location audio (music and ambient). fadeSeconds > 0 fades
        /// out instead of hard-cutting.
        /// </summary>
        public void StopLocationAudio(float fadeSeconds = 0f)
        {
            if (musicFadeRoutine != null)
            {
                StopCoroutine(musicFadeRoutine);
                musicFadeRoutine = null;
            }

            if (fadeSeconds <= 0f)
            {
                if (musicAudioSource != null && musicAudioSource.isPlaying)
                    musicAudioSource.Stop();
                if (ambientAudioSource != null && ambientAudioSource.isPlaying)
                    ambientAudioSource.Stop();
                RestoreLocationVolumes();
                return;
            }

            musicFadeRoutine = StartCoroutine(FadeLocationAudioOut(fadeSeconds));
        }

        private IEnumerator FadeLocationAudioOut(float duration)
        {
            float startMusic = musicAudioSource != null ? musicAudioSource.volume : 0f;
            float startAmbient = ambientAudioSource != null ? ambientAudioSource.volume : 0f;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                float f = 1f - Mathf.Clamp01(elapsed / duration);
                if (musicAudioSource != null)
                    musicAudioSource.volume = startMusic * f;
                if (ambientAudioSource != null)
                    ambientAudioSource.volume = startAmbient * f;
                yield return null;
            }

            if (musicAudioSource != null)
                musicAudioSource.Stop();
            if (ambientAudioSource != null)
                ambientAudioSource.Stop();
            RestoreLocationVolumes();
            musicFadeRoutine = null;
        }

        private void RestoreLocationVolumes()
        {
            if (musicAudioSource != null)
                musicAudioSource.volume = musicVolume;
            if (ambientAudioSource != null)
                ambientAudioSource.volume = ambientVolume;
        }

        /// <summary>
        /// Duck the location music/ambient to a whisper (the face attack's
        /// bullet-time window) and back. Bows out whenever a location-audio
        /// fade owns the volume.
        /// </summary>
        public void SetMusicDuck(bool ducked)
        {
            if (duckRoutine != null)
            {
                StopCoroutine(duckRoutine);
                duckRoutine = null;
            }
            duckRoutine = StartCoroutine(DuckRoutine(ducked ? 0.25f : 1f));
        }

        private IEnumerator DuckRoutine(float targetFactor)
        {
            const float duration = 0.18f;
            float startMusic = musicAudioSource != null ? musicAudioSource.volume : 0f;
            float startAmbient = ambientAudioSource != null ? ambientAudioSource.volume : 0f;
            float targetMusic = musicVolume * targetFactor;
            float targetAmbient = ambientVolume * targetFactor;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (musicFadeRoutine != null)
                {
                    duckRoutine = null;
                    yield break;
                }

                elapsed += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(elapsed / duration);
                if (musicAudioSource != null)
                    musicAudioSource.volume = Mathf.Lerp(startMusic, targetMusic, k);
                if (ambientAudioSource != null)
                    ambientAudioSource.volume = Mathf.Lerp(startAmbient, targetAmbient, k);
                yield return null;
            }

            duckRoutine = null;
        }
    }
}
