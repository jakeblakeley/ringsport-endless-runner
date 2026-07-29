using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using RingSport.Level;
using RingSport.Core;
using RingSport.Player;

namespace RingSport.UI
{
    /// <summary>
    /// Stop Attack mini level (the ringsport stopped attack). Like the flee
    /// attack this plays IN-RUN over the last stretch of its level: a decoy
    /// flees ahead of the dog, dodging barrels and dropping coins - but where
    /// the flee attack slowly closes the gap for a catch, here the decoy HOLDS
    /// a slightly longer gap. Then the beat flips: a big red "STOP!" banner
    /// fires, the decoy turns to face the dog, a red line appears across all
    /// three lanes, and the world eases into a slow-motion charge timed so the
    /// dog reaches the line exactly when the stop window expires. Tapping the
    /// on-screen whistle inside the window (4s on Ring 2-1, 2.5s on Ring 3-1)
    /// halts the dog short of the line and completes the level; missing it is
    /// a standard mini-level failure (which retries just this sequence, not
    /// the whole run - see GameManager's in-run MiniLevel-state reroute).
    ///
    /// LevelManager drives entry (BeginChase near the end of stop-attack
    /// levels); on success this completes the level directly via
    /// LevelManager.CompleteInRunMiniLevel (the dog never reaches the finish
    /// line - it stopped). The decoy is the same human Decoy prefab as the
    /// flee attack, wired by Tools > RingSport > Setup Stop Attack.
    /// </summary>
    public class MiniLevelStopAttack : InRunMiniLevel
    {
        public static MiniLevelStopAttack Instance { get; private set; }

        public override MiniLevelType MiniLevelType => MiniLevelType.StopAttack;

        private enum StopPhase { Inactive, Intro, Approach, StopWindow, Stopped, Failed }

        // ---- Difficulty tables, indexed by stop-attack ordinal across the
        // ---- run (level 4 "Ring 2-1" = 0, level 6 "Ring 3-1" = 1). The
        // ---- approach reuses the flee attack's validated fairness cadences;
        // ---- the stop window is the real difficulty knob: 4s, then 2.5s.
        private static readonly float[] ChaseDurationSeconds = { 8f, 12f };
        private static readonly float[] ObstacleIntervalSeconds = { 2.1f, 1.6f };
        private static readonly float[] VoluntaryHopIntervalSeconds = { 3.0f, 2.0f };
        private static readonly float[] StopWindowSeconds = { 4f, 2.5f };

        // ---- Fairness model: same action model as the flee attack chase
        // (one dodge ~0.9s; barrels are dodge-only; hard 1.1s row floor).
        private const float ReactionAheadSeconds = 2.4f;   // row spawn distance, in time
        private const float DodgeLeadSeconds = 1.1f;       // decoy dodges this far ahead of a row
        private const float CalmTailSeconds = 2.5f;        // no voluntary hops at the end of approach
        private const float ObstacleStopTailSeconds = 3f;  // no new rows at the end of approach (rows arrive 2.4s after spawn, so the track is clean before the stop)
        private const float MinRowSpacingSeconds = 1.1f;   // hard floor between consecutive rows

        // ---- Chase geometry / pacing
        private const float LaneDistance = 3f;
        private const float DecoyLaneLerpSpeed = 9f;
        private const float IntroSeconds = 1.6f;
        private const float IntroStartGap = 8f;    // decoy appears here, then flees...
        private const float FarGap = 24f;          // ...out to here...
        private const float HoldGap = 28f;         // ...and settles a little further (never closes like the flee attack)
        private const float DecoyBeyondLineGap = 5f;  // the turned decoy stands this far past the line
        private const float LineFailGap = 1.2f;    // the line reaches here (the dog's nose) exactly at window expiry
        private const float SlowdownRampSeconds = 0.4f;  // ease into the slow-motion charge
        private const float StopRampSeconds = 0.35f;     // ease from the slow charge to a full halt on success
        private const float DecoyTurnSeconds = 0.5f;
        private const float SuccessHoldSeconds = 1.8f;   // beat between the stop and the reward screen
        private const float PostWindowBufferSeconds = 3f; // lead slack so the window resolves before the level timer ends
        private const float CoinIntervalSeconds = 0.5f;
        private const float DespawnBehind = 12f;
        private const int StopBonusPoints = 150;

        [Header("Decoy")]
        [Tooltip("Human decoy prefab (same as the flee attack), wired by Tools > RingSport > Setup Stop Attack. Falls back to a placeholder sphere when missing.")]
        [SerializeField] private GameObject decoyPrefab;
        [Tooltip("Fallback sphere / trail color.")]
        [SerializeField] private Color decoyColor = new Color(1f, 0.42f, 0.05f);
        [Tooltip("Font for the banners (STOP! etc.) and the whistle label, wired by Tools > RingSport > Setup Stop Attack. TMP default when missing.")]
        [SerializeField] private TMP_FontAsset bannerFont;
        [Tooltip("The STOP! banner, the stop line and the whistle button all use this red.")]
        [SerializeField] private Color stopRed = new Color(0.9f, 0.11f, 0.11f);

        [Header("Audio")]
        [SerializeField] private AudioClip whistleSound;

        // Runtime state
        private StopPhase phase = StopPhase.Inactive;
        private float phaseTimer;
        private int difficulty;
        private bool chaseActive;
        private PlayerController playerController;
        private Transform playerTransform;

        // Decoy
        private GameObject decoyRoot;
        private Transform decoySphere;   // fallback sphere only
        private DecoyController decoyHuman;
        private int decoyLane;
        private float decoyX;
        private float gap;
        private float bobPhase;
        private float lastDodgeTime;
        private float decoyTurnTimer;

        // Spawned chase objects (self-managed; deliberately NOT registered with
        // DespawnManager so the end-of-level despawn sweeps can't eat them)
        private class ChaseObstacle
        {
            public GameObject go;
            public int lane;
            public bool decoyHandled;
        }
        private readonly List<ChaseObstacle> chaseObstacles = new List<ChaseObstacle>();
        private readonly List<GameObject> droppedCoins = new List<GameObject>();

        private float obstacleTimer;
        private float voluntaryHopTimer;
        private float coinTimer;

        // Stop window: the world eases down to slowChargeSpeed so the red line
        // arrives at the dog exactly at window expiry, whatever the level's
        // scroll speed was
        private GameObject stopLine;
        private float scrollOverride = -1f;
        private float slowChargeSpeed;
        private float slowdownRampRate;   // m/s^2 toward slowChargeSpeed (or 0 after the stop)
        private float scrollTarget;
        private bool controlLockActive;   // manual jump + lane input disabled during the window
        private bool movementPausedByStop;
        private bool completionNotified;

        // Retry scoring: the run score present when the chase first began, so a
        // retry can be re-seeded with it (same pattern as the flee attack)
        private int preChaseScore = -1;
        private bool retryEntryPending;

        // Banner UI
        private Canvas bannerCanvas;
        private CanvasGroup bannerGroup;
        private TextMeshProUGUI bannerText;
        private Coroutine bannerRoutine;

        // Whistle UI
        private Canvas whistleCanvas;
        private CanvasGroup whistleGroup;
        private RectTransform whistleButtonRect;
        private float whistlePopTimer;    // early-tap feedback bump
        private const float WhistlePopSeconds = 0.18f;

        // Cached bits
        private Material decoyMaterial;
        private Material lineMaterial;
        private static Sprite circleSprite;

        private void Awake()
        {
            if (Instance != null && Instance != this)
                return;
            Instance = this;
            Register();
        }

        private void OnDestroy()
        {
            Unregister();
            if (Instance == this)
                Instance = null;
        }

        // ------------------------------------------------------------------
        // MiniLevelBase contract - only reachable if something starts this via
        // the arena mini-level flow (it shouldn't: GameManager reroutes in-run
        // types back into the Playing state). Complete immediately so the game
        // can never soft-lock.
        // ------------------------------------------------------------------
        public override void StartGame()
        {
            Debug.LogWarning("[MiniLevelStopAttack] StartGame called via arena flow - stop attack runs in-run. Completing immediately.");
            CompleteGame();
        }

        public override void StopGame()
        {
            Cleanup();
        }

        // ------------------------------------------------------------------
        // InRunMiniLevel contract, used by LevelManager / GameManager
        // ------------------------------------------------------------------

        public override float GetLeadSeconds(int difficultyIndex)
        {
            int d = ClampDifficulty(difficultyIndex);
            return IntroSeconds + ChaseDurationSeconds[d] + StopWindowSeconds[d] + PostWindowBufferSeconds;
        }

        public override void OnRunLevelStarted(bool isStopAttackLevel, bool isRetryEntry)
        {
            Cleanup();
            retryEntryPending = isStopAttackLevel && isRetryEntry;
            if (!isRetryEntry)
                preChaseScore = -1;
        }

        public override void BeginChase(int difficultyIndex)
        {
            if (chaseActive)
                return;

            playerController = Object.FindAnyObjectByType<PlayerController>();
            if (playerController == null)
            {
                Debug.LogError("[MiniLevelStopAttack] No PlayerController found - cannot start chase");
                return;
            }
            playerTransform = playerController.transform;

            difficulty = ClampDifficulty(difficultyIndex);
            chaseActive = true;
            controlLockActive = false;
            movementPausedByStop = false;
            completionNotified = false;
            obstacleTimer = 0f;
            voluntaryHopTimer = 0f;
            coinTimer = 0f;
            lastDodgeTime = -10f;
            decoyTurnTimer = 0f;
            scrollOverride = -1f;

            // Idempotent safety - LevelManager already wound spawning down
            LevelGenerator.Instance?.SetRunnerSpawningSuppressed(true);

            // Bank the running-section score and flip the mini-level context so
            // a failure here retries the stop attack, not the whole run
            GameManager.Instance?.NotifyInRunMiniLevelStarted();

            if (retryEntryPending && preChaseScore > 0)
            {
                LevelManager.Instance?.AddScore(preChaseScore);
                Debug.Log($"[MiniLevelStopAttack] Retry entry - re-seeded pre-chase score: {preChaseScore}");
            }
            else
            {
                preChaseScore = ScoreManager.Instance?.CurrentScore ?? 0;
            }
            retryEntryPending = false;

            SpawnDecoy();
            ShowBanner("TAP THE WHISTLE\nTO STOP", Color.white, 2.2f, 76f);
            ShowWhistle(true);

            phase = StopPhase.Intro;
            phaseTimer = 0f;

            Debug.Log($"[MiniLevelStopAttack] Chase started (difficulty {difficulty}, window {StopWindowSeconds[difficulty]}s)");
        }

        public override void NotifyLevelEndReached()
        {
            if (!chaseActive)
                return;

            Debug.Log("[MiniLevelStopAttack] Level end reached - cleaning up");
            Cleanup();
        }

        public override bool IsChaseActive => chaseActive;

        // ------------------------------------------------------------------
        // Simulation
        // ------------------------------------------------------------------

        private void Update()
        {
            if (!chaseActive)
                return;

            GameState state = GameManager.Instance != null ? GameManager.Instance.CurrentState : GameState.Home;

            // A failure freeze-frames the sequence; the retry/restart paths
            // clean up. The whistle must not float over the game over screen.
            if (state == GameState.GameOver)
            {
                ShowWhistle(false);
                return;
            }

            if (state != GameState.Playing)
            {
                Cleanup();
                return;
            }

            float dt = Time.deltaTime;
            if (dt <= 0f)
                return; // pre-run countdown freeze

            phaseTimer += dt;

            switch (phase)
            {
                case StopPhase.Intro:
                    UpdateIntro(dt);
                    break;
                case StopPhase.Approach:
                    UpdateApproach(dt);
                    break;
                case StopPhase.StopWindow:
                    UpdateStopWindow(dt);
                    break;
                case StopPhase.Stopped:
                    UpdateStopped(dt);
                    break;
            }

            UpdateDecoyTransform(dt);
            UpdateWhistle(dt);
            CleanupPassedObjects();
        }

        private void UpdateIntro(float dt)
        {
            // The decoy bolts away from the dog: gap grows with an ease-out
            float t = Mathf.Clamp01(phaseTimer / IntroSeconds);
            float ease = 1f - (1f - t) * (1f - t);
            gap = Mathf.Lerp(IntroStartGap, FarGap, ease);

            DropCoins(dt);

            if (phaseTimer >= IntroSeconds)
            {
                phase = StopPhase.Approach;
                phaseTimer = 0f;
            }
        }

        private void UpdateApproach(float dt)
        {
            float chaseDuration = ChaseDurationSeconds[difficulty];
            float t = Mathf.Clamp01(phaseTimer / chaseDuration);
            // Unlike the flee attack the gap never closes - it drifts a little
            // FURTHER out while the decoy leads the dog through the barrels
            gap = Mathf.SmoothStep(FarGap, HoldGap, t);

            float remaining = chaseDuration - phaseTimer;

            // Obstacle rows: at most ONE obstacle across the 3 lanes per row
            obstacleTimer += dt;
            if (remaining > ObstacleStopTailSeconds && obstacleTimer >= ObstacleIntervalSeconds[difficulty])
            {
                obstacleTimer = 0f;
                TrySpawnChaseObstacle();
            }

            UpdateDecoyDodging();

            // Voluntary lane hops for liveliness - suppressed near the end so
            // the decoy settles into its lane before the turn
            voluntaryHopTimer += dt;
            if (remaining > CalmTailSeconds &&
                voluntaryHopTimer >= VoluntaryHopIntervalSeconds[difficulty] &&
                Time.time - lastDodgeTime > 0.8f)
            {
                voluntaryHopTimer = Random.Range(-0.6f, 0.4f); // jitter the cadence
                TryVoluntaryHop();
            }

            DropCoins(dt);

            if (phaseTimer >= chaseDuration)
                EnterStopWindow();
        }

        /// <summary>
        /// The beat flip: STOP! banner, the decoy wheels around, the red line
        /// materializes across the track, and the world eases into a slowed
        /// charge tuned so the line arrives at the dog exactly when the window
        /// expires - the crossing IS the failure moment.
        /// </summary>
        private void EnterStopWindow()
        {
            phase = StopPhase.StopWindow;
            phaseTimer = 0f;
            decoyTurnTimer = 0f;

            float window = StopWindowSeconds[difficulty];
            float lineStartGap = gap - DecoyBeyondLineGap;
            float lineTravel = lineStartGap - LineFailGap;

            // Solve the slow-charge speed from the travel budget: a linear ramp
            // from the current scroll speed over SlowdownRampSeconds, then
            // constant until the window ends, must cover exactly lineTravel
            float startSpeed = CurrentScrollSpeed();
            slowChargeSpeed = Mathf.Max(
                (lineTravel - startSpeed * SlowdownRampSeconds * 0.5f) / (window - SlowdownRampSeconds * 0.5f),
                1f);
            slowdownRampRate = (startSpeed - slowChargeSpeed) / SlowdownRampSeconds;
            scrollOverride = startSpeed;
            scrollTarget = slowChargeSpeed;
            LevelScroller.Instance?.SetSpeedOverride(scrollOverride);

            stopLine = BuildStopLine(new Vector3(0f, 0f, PlayerZ() + lineStartGap));

            // The window is a pure tap-or-not beat on a clean track - manual
            // jumping and lane switching would only break the staging
            playerController?.SetJumpEnabled(false);
            playerController?.SetLaneChangeEnabled(false);
            controlLockActive = true;

            ShowBanner("STOP!", stopRed, window - 0.5f, 150f);

            Debug.Log($"[MiniLevelStopAttack] Stop window open: {window}s, line at gap {lineStartGap:F1}, slow charge {slowChargeSpeed:F1} m/s");
        }

        private void UpdateStopWindow(float dt)
        {
            UpdateScrollOverride(dt);

            // The decoy is world-fixed now (it stopped fleeing): it rides the
            // scroll toward the dog like everything else
            gap -= CurrentScrollSpeed() * dt;
            decoyTurnTimer += dt;

            if (phaseTimer >= StopWindowSeconds[difficulty])
            {
                // The dog crosses the line - standard mini-level failure, which
                // retries just this sequence (mini-level context is armed)
                phase = StopPhase.Failed;
                ShowWhistle(false);
                Debug.Log("[MiniLevelStopAttack] Window expired - dog crossed the line");
                GameManager.Instance?.TriggerMiniLevelGameOver();
            }
        }

        private void UpdateStopped(float dt)
        {
            UpdateScrollOverride(dt);
            gap -= CurrentScrollSpeed() * dt;
            decoyTurnTimer += dt;

            if (!completionNotified && phaseTimer >= SuccessHoldSeconds)
            {
                completionNotified = true;
                LevelManager.Instance?.CompleteInRunMiniLevel();
            }
        }

        private void UpdateScrollOverride(float dt)
        {
            if (scrollOverride < 0f)
                return;

            scrollOverride = Mathf.MoveTowards(scrollOverride, scrollTarget, slowdownRampRate * dt);
            LevelScroller.Instance?.SetSpeedOverride(scrollOverride);
        }

        /// <summary>Whistle tapped inside the window: the dog pulls up short of the line.</summary>
        private void DoSuccessfulStop()
        {
            phase = StopPhase.Stopped;
            phaseTimer = 0f;

            // Ease the world (and the dog's run) to a complete halt
            scrollTarget = 0f;
            slowdownRampRate = Mathf.Max(scrollOverride, 1f) / StopRampSeconds;

            // Movement pause drops the locomotion blend to idle - the dog
            // visibly plants in front of the line
            playerController?.PauseMovement();
            movementPausedByStop = true;

            LevelManager.Instance?.AddScore(StopBonusPoints);
            if (whistleSound != null)
                LevelManager.Instance?.PlayCollectSound(whistleSound);

            ShowBanner("GOOD STOP!", Color.white, 1.2f, 110f);
            ShowWhistle(false);

            Debug.Log("[MiniLevelStopAttack] Stopped in time!");
        }

        private void OnWhistleTapped()
        {
            if (!chaseActive)
                return;

            if (phase == StopPhase.StopWindow)
            {
                DoSuccessfulStop();
            }
            else if (phase == StopPhase.Intro || phase == StopPhase.Approach)
            {
                // Too early - just a little feedback bump, no penalty
                whistlePopTimer = WhistlePopSeconds;
            }
        }

        // ------------------------------------------------------------------
        // Decoy movement + dressing (approach mirrors the flee attack)
        // ------------------------------------------------------------------

        private void SpawnDecoy()
        {
            DestroyDecoy();

            decoyLane = 0;
            decoyX = 0f;
            gap = IntroStartGap;
            bobPhase = 0f;

            // Human decoy prefab (Steve + DecoyController); the model has no
            // collider, so it can never block the CharacterController
            if (decoyPrefab != null)
            {
                decoyRoot = Object.Instantiate(decoyPrefab);
                decoyRoot.name = "StopAttackDecoy";
                decoyHuman = decoyRoot.GetComponent<DecoyController>();
            }

            if (decoyHuman == null)
            {
                // Fallback: the placeholder sphere
                if (decoyRoot == null)
                    decoyRoot = new GameObject("StopAttackDecoy");

                var sphereGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphereGO.name = "DecoyBody";
                Object.Destroy(sphereGO.GetComponent<Collider>());
                sphereGO.transform.SetParent(decoyRoot.transform, false);
                sphereGO.transform.localScale = Vector3.one * 1.2f;
                sphereGO.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                decoySphere = sphereGO.transform;

                var renderer = sphereGO.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.sharedMaterial = GetDecoyMaterial();
            }

            decoyRoot.transform.position = new Vector3(0f, 0f, PlayerZ() + gap);
        }

        private void UpdateDecoyTransform(float dt)
        {
            if (decoyRoot == null)
                return;

            bool running = phase == StopPhase.Intro || phase == StopPhase.Approach;

            decoyX = Mathf.Lerp(decoyX, decoyLane * LaneDistance, DecoyLaneLerpSpeed * dt);
            decoyRoot.transform.position = new Vector3(decoyX, 0f, PlayerZ() + gap);

            // The turn-around: the decoy wheels 180 to face down the track at
            // the dog while its locomotion damps to a standing idle
            float turnT = running ? 0f : Mathf.Clamp01(decoyTurnTimer / DecoyTurnSeconds);
            float yaw = Mathf.SmoothStep(0f, 180f, turnT);
            decoyRoot.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            if (decoyHuman != null)
            {
                float speedMultiplier = LevelGenerator.Instance?.GetCurrentConfig()?.SpeedMultiplier ?? 1f;
                if (running)
                {
                    // Full-tilt sprint, leaning into lane changes
                    float strafe = Mathf.Clamp((decoyLane * LaneDistance - decoyX) / LaneDistance, -1f, 1f);
                    decoyHuman.UpdateLocomotion(2f, strafe, speedMultiplier, Time.unscaledDeltaTime);
                }
                else
                {
                    // Damped down to a defiant stand
                    decoyHuman.UpdateLocomotion(0f, 0f, 1f, Time.unscaledDeltaTime);
                }
            }
            else if (decoySphere != null && running)
            {
                bobPhase += dt * 3.2f;
                float bob = Mathf.Abs(Mathf.Sin(bobPhase * Mathf.PI)) * 0.22f;
                decoySphere.localPosition = new Vector3(0f, 0.6f + bob, 0f);
            }
        }

        // ------------------------------------------------------------------
        // Obstacles + dodging + coins (same fairness rules as the flee attack)
        // ------------------------------------------------------------------

        private void TrySpawnChaseObstacle()
        {
            float scrollSpeed = CurrentScrollSpeed();
            float spawnZ = PlayerZ() + Mathf.Max(ReactionAheadSeconds * scrollSpeed, gap + 12f);

            // Hard spacing floor between rows, even across sprint speed spikes
            foreach (var obst in chaseObstacles)
            {
                if (obst.go != null && spawnZ - obst.go.transform.position.z < MinRowSpacingSeconds * scrollSpeed)
                    return; // too soon - skip this tick, the interval timer will retry
            }

            // ~45% of rows target the decoy's lane so it visibly dodges and
            // leads the player; the rest threaten the side lanes
            int lane;
            if (Random.value < 0.45f)
            {
                lane = decoyLane;
            }
            else
            {
                int[] others = OtherLanes(decoyLane);
                lane = others[Random.Range(0, others.Length)];
            }

            GameObject go = ObjectPooler.Instance?.SpawnFromPool(
                PoolTags.ObstacleAvoid,
                new Vector3(lane * LaneDistance, 0f, spawnZ),
                Quaternion.identity);

            if (go != null)
                chaseObstacles.Add(new ChaseObstacle { go = go, lane = lane });
        }

        private void UpdateDecoyDodging()
        {
            float dodgeLead = DodgeLeadSeconds * CurrentScrollSpeed();
            float decoyZ = PlayerZ() + gap;

            foreach (var obst in chaseObstacles)
            {
                if (obst.decoyHandled || obst.go == null || obst.lane != decoyLane)
                    continue;

                float dz = obst.go.transform.position.z - decoyZ;
                if (dz > 0f && dz < dodgeLead)
                {
                    obst.decoyHandled = true;
                    int target = PickDodgeLane();
                    if (target != decoyLane)
                    {
                        decoyLane = target;
                        lastDodgeTime = Time.time;
                    }
                }
            }
        }

        private int PickDodgeLane()
        {
            int[] candidates = decoyLane == 0 ? new[] { -1, 1 } : new[] { 0 };

            var clear = new List<int>();
            foreach (int lane in candidates)
            {
                if (IsLaneClearAheadOfDecoy(lane))
                    clear.Add(lane);
            }

            if (clear.Count > 0)
                return clear[Random.Range(0, clear.Count)];
            return candidates[Random.Range(0, candidates.Length)];
        }

        private bool IsLaneClearAheadOfDecoy(int lane)
        {
            float decoyZ = PlayerZ() + gap;
            float window = DodgeLeadSeconds * CurrentScrollSpeed() * 1.6f;

            foreach (var obst in chaseObstacles)
            {
                if (obst.go == null || obst.lane != lane)
                    continue;
                float dz = obst.go.transform.position.z - decoyZ;
                if (dz > -1f && dz < window)
                    return false;
            }
            return true;
        }

        private void TryVoluntaryHop()
        {
            int[] candidates = decoyLane == 0 ? new[] { -1, 1 } : new[] { 0 };
            int target = candidates[Random.Range(0, candidates.Length)];
            if (IsLaneClearAheadOfDecoy(target))
                decoyLane = target;
        }

        private void DropCoins(float dt)
        {
            coinTimer += dt;
            if (coinTimer < CoinIntervalSeconds)
                return;
            coinTimer = 0f;

            float decoyZ = PlayerZ() + gap;
            if (decoyZ - PlayerZ() < 3f)
                return; // never materialize a coin on top of the player

            GameObject coin = ObjectPooler.Instance?.SpawnFromPool(
                PoolTags.Collectible,
                new Vector3(decoyLane * LaneDistance, 1f, decoyZ - 0.5f),
                Quaternion.identity);

            if (coin != null)
                droppedCoins.Add(coin);
        }

        // ------------------------------------------------------------------
        // The stop line
        // ------------------------------------------------------------------

        private GameObject BuildStopLine(Vector3 position)
        {
            var root = new GameObject("StopAttackLine");
            root.transform.position = position;

            // A flat red band spanning all three lanes, flush with the ground.
            // No collider - the failure is the window timer; the line is the
            // visual it's synchronized to.
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "LineVisual";
            Object.Destroy(visual.GetComponent<Collider>());
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(10.8f, 0.06f, 0.45f);
            visual.transform.localPosition = new Vector3(0f, 0.03f, 0f);

            var renderer = visual.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = GetLineMaterial();

            // World-fixed: it rides the (slowed) scroll toward the dog
            root.AddComponent<ScrollableObject>();

            StartCoroutine(SweepInLine(visual.transform));
            return root;
        }

        /// <summary>The line sweeps out across the lanes rather than popping in.</summary>
        private IEnumerator SweepInLine(Transform visual)
        {
            Vector3 full = visual.localScale;
            float t = 0f;
            const float duration = 0.25f;
            while (t < duration && visual != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                k = k * k * (3f - 2f * k);
                visual.localScale = new Vector3(full.x * k, full.y, full.z);
                yield return null;
            }
            if (visual != null)
                visual.localScale = full;
        }

        // ------------------------------------------------------------------
        // Cleanup + lifecycle
        // ------------------------------------------------------------------

        private void CleanupPassedObjects()
        {
            float cutoff = PlayerZ() - DespawnBehind;

            for (int i = chaseObstacles.Count - 1; i >= 0; i--)
            {
                var obst = chaseObstacles[i];
                if (obst.go == null || obst.go.transform.position.z < cutoff)
                {
                    if (obst.go != null)
                        ObjectPooler.Instance?.ReturnToPool(obst.go);
                    chaseObstacles.RemoveAt(i);
                }
            }

            for (int i = droppedCoins.Count - 1; i >= 0; i--)
            {
                var coin = droppedCoins[i];
                if (coin == null || !coin.activeInHierarchy)
                {
                    droppedCoins.RemoveAt(i); // collected - Collectible returned itself
                }
                else if (coin.transform.position.z < cutoff)
                {
                    ObjectPooler.Instance?.ReturnToPool(coin);
                    droppedCoins.RemoveAt(i);
                }
            }
        }

        /// <summary>Tears down the whole sequence and hands the world back to the level.</summary>
        private void Cleanup()
        {
            bool wasActive = chaseActive;
            chaseActive = false;
            phase = StopPhase.Inactive;

            if (controlLockActive)
            {
                playerController?.SetJumpEnabled(true);
                playerController?.SetLaneChangeEnabled(true);
                controlLockActive = false;
            }

            if (movementPausedByStop)
            {
                playerController?.ResumeMovement();
                movementPausedByStop = false;
            }

            if (scrollOverride >= 0f)
            {
                LevelScroller.Instance?.ClearSpeedOverride();
                scrollOverride = -1f;
            }

            DestroyDecoy();

            if (stopLine != null)
            {
                Destroy(stopLine);
                stopLine = null;
            }

            foreach (var obst in chaseObstacles)
            {
                if (obst.go != null)
                    ObjectPooler.Instance?.ReturnToPool(obst.go);
            }
            chaseObstacles.Clear();

            foreach (var coin in droppedCoins)
            {
                if (coin != null && coin.activeInHierarchy)
                    ObjectPooler.Instance?.ReturnToPool(coin);
            }
            droppedCoins.Clear();

            if (bannerRoutine != null)
            {
                StopCoroutine(bannerRoutine);
                bannerRoutine = null;
            }
            if (bannerGroup != null)
                bannerGroup.alpha = 0f;

            ShowWhistle(false);

            LevelGenerator.Instance?.SetRunnerSpawningSuppressed(false);

            if (wasActive)
                Debug.Log("[MiniLevelStopAttack] Cleaned up");
        }

        private void DestroyDecoy()
        {
            if (decoyRoot != null)
            {
                Destroy(decoyRoot);
                decoyRoot = null;
                decoySphere = null;
                decoyHuman = null;
            }
        }

        // ------------------------------------------------------------------
        // Banner UI (built in code - no scene wiring required)
        // ------------------------------------------------------------------

        private void ShowBanner(string message, Color color, float holdSeconds, float fontSize)
        {
            EnsureBannerCanvas();
            if (bannerText == null)
                return;

            if (bannerRoutine != null)
                StopCoroutine(bannerRoutine);
            bannerRoutine = StartCoroutine(BannerRoutine(message, color, holdSeconds, fontSize));
        }

        private IEnumerator BannerRoutine(string message, Color color, float holdSeconds, float fontSize)
        {
            bannerText.text = message;
            bannerText.color = color;
            bannerText.fontSize = fontSize;

            var rt = bannerText.rectTransform;
            float t = 0f;
            const float popDuration = 0.14f;
            while (t < popDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / popDuration);
                bannerGroup.alpha = k;
                rt.localScale = Vector3.one * Mathf.Lerp(1.45f, 1f, k);
                yield return null;
            }
            bannerGroup.alpha = 1f;
            rt.localScale = Vector3.one;

            yield return new WaitForSecondsRealtime(holdSeconds);

            t = 0f;
            const float fadeDuration = 0.4f;
            while (t < fadeDuration)
            {
                t += Time.unscaledDeltaTime;
                bannerGroup.alpha = 1f - Mathf.Clamp01(t / fadeDuration);
                yield return null;
            }
            bannerGroup.alpha = 0f;
            bannerRoutine = null;
        }

        private void EnsureBannerCanvas()
        {
            if (bannerCanvas != null)
                return;

            var canvasGO = new GameObject("StopAttackBannerCanvas");
            canvasGO.transform.SetParent(transform, false);
            bannerCanvas = canvasGO.AddComponent<Canvas>();
            bannerCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            bannerCanvas.sortingOrder = 400;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            bannerGroup = canvasGO.AddComponent<CanvasGroup>();
            bannerGroup.alpha = 0f;
            bannerGroup.blocksRaycasts = false;
            bannerGroup.interactable = false;

            var textGO = new GameObject("BannerText");
            textGO.transform.SetParent(canvasGO.transform, false);
            bannerText = textGO.AddComponent<TextMeshProUGUI>();
            bannerText.alignment = TextAlignmentOptions.Center;
            bannerText.fontSize = 96f;
            bannerText.raycastTarget = false;

            // Barlow Bold carries its own weight - faux-bold on top muddies it
            if (bannerFont != null)
            {
                bannerText.font = bannerFont;
                bannerText.fontStyle = FontStyles.Italic;
            }
            else
            {
                bannerText.fontStyle = FontStyles.Bold | FontStyles.Italic;
            }

            var rt = bannerText.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.74f);
            rt.sizeDelta = new Vector2(1000f, 360f);
            rt.anchoredPosition = Vector2.zero;
        }

        // ------------------------------------------------------------------
        // Whistle UI (built in code - no scene wiring required)
        // ------------------------------------------------------------------

        private void ShowWhistle(bool visible)
        {
            if (!visible && whistleGroup == null)
                return;

            EnsureWhistleCanvas();
            whistleGroup.alpha = visible ? 1f : 0f;
            whistleGroup.blocksRaycasts = visible;
            whistleGroup.interactable = visible;
            if (whistleButtonRect != null)
                whistleButtonRect.localScale = Vector3.one;
        }

        private void UpdateWhistle(float dt)
        {
            if (whistleButtonRect == null || whistleGroup == null || whistleGroup.alpha <= 0f)
                return;

            float scale = 1f;

            // Urgency pulse while the window is open
            if (phase == StopPhase.StopWindow)
                scale += 0.1f * Mathf.Sin(phaseTimer * Mathf.PI * 2f * 2.2f);

            // Early-tap feedback bump
            if (whistlePopTimer > 0f)
            {
                whistlePopTimer -= dt;
                scale += 0.12f * Mathf.Clamp01(whistlePopTimer / WhistlePopSeconds);
            }

            whistleButtonRect.localScale = Vector3.one * scale;
        }

        private void EnsureWhistleCanvas()
        {
            if (whistleCanvas != null)
                return;

            var canvasGO = new GameObject("StopAttackWhistleCanvas");
            canvasGO.transform.SetParent(transform, false);
            whistleCanvas = canvasGO.AddComponent<Canvas>();
            whistleCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            whistleCanvas.sortingOrder = 401;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            whistleGroup = canvasGO.AddComponent<CanvasGroup>();
            whistleGroup.alpha = 0f;
            whistleGroup.blocksRaycasts = false;
            whistleGroup.interactable = false;

            // The button: a red disc in thumb reach, bottom center
            var buttonGO = new GameObject("WhistleButton");
            buttonGO.transform.SetParent(canvasGO.transform, false);
            var buttonImage = buttonGO.AddComponent<Image>();
            buttonImage.sprite = GetCircleSprite();
            buttonImage.color = stopRed;
            whistleButtonRect = buttonImage.rectTransform;
            whistleButtonRect.anchorMin = whistleButtonRect.anchorMax = new Vector2(0.5f, 0.17f);
            whistleButtonRect.sizeDelta = new Vector2(230f, 230f);
            whistleButtonRect.anchoredPosition = Vector2.zero;

            var button = buttonGO.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick.AddListener(OnWhistleTapped);

            // Whistle glyph from primitives: round body + angled mouthpiece +
            // the pea hole punched back out in the button color
            BuildGlyphImage(buttonGO.transform, "WhistleBody", GetCircleSprite(), Color.white,
                new Vector2(104f, 104f), new Vector2(-16f, -14f), 0f);
            BuildGlyphImage(buttonGO.transform, "WhistleMouth", null, Color.white,
                new Vector2(78f, 32f), new Vector2(42f, 34f), 35f);
            BuildGlyphImage(buttonGO.transform, "WhistlePea", GetCircleSprite(), stopRed,
                new Vector2(36f, 36f), new Vector2(-16f, -14f), 0f);

            var labelGO = new GameObject("WhistleLabel");
            labelGO.transform.SetParent(buttonGO.transform, false);
            var label = labelGO.AddComponent<TextMeshProUGUI>();
            label.text = "WHISTLE";
            label.alignment = TextAlignmentOptions.Center;
            label.fontSize = 40f;
            label.color = Color.white;
            label.raycastTarget = false;
            if (bannerFont != null)
                label.font = bannerFont;
            var labelRT = label.rectTransform;
            labelRT.anchorMin = labelRT.anchorMax = new Vector2(0.5f, 0f);
            labelRT.sizeDelta = new Vector2(400f, 60f);
            labelRT.anchoredPosition = new Vector2(0f, -55f);
        }

        private static void BuildGlyphImage(Transform parent, string name, Sprite sprite, Color color,
            Vector2 size, Vector2 position, float rotationZ)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            var rt = image.rectTransform;
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
            rt.localEulerAngles = new Vector3(0f, 0f, rotationZ);
        }

        /// <summary>Procedural anti-aliased white disc, so no sprite asset wiring is needed.</summary>
        private static Sprite GetCircleSprite()
        {
            if (circleSprite != null)
                return circleSprite;

            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            float center = (size - 1) * 0.5f;
            float radius = size * 0.5f - 2f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(radius - dist + 0.5f));
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return circleSprite;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static int ClampDifficulty(int index)
        {
            return Mathf.Clamp(index, 0, StopWindowSeconds.Length - 1);
        }

        private float PlayerZ()
        {
            return playerTransform != null ? playerTransform.position.z : 0f;
        }

        private float CurrentScrollSpeed()
        {
            float speed = LevelScroller.Instance != null ? LevelScroller.Instance.GetScrollSpeed() : 0f;
            return Mathf.Max(speed, scrollOverride >= 0f ? 0f : 10f);
        }

        private static int[] OtherLanes(int lane)
        {
            switch (lane)
            {
                case -1: return new[] { 0, 1 };
                case 1: return new[] { -1, 0 };
                default: return new[] { -1, 1 };
            }
        }

        private Material GetDecoyMaterial()
        {
            if (decoyMaterial == null)
            {
                decoyMaterial = CreateLitMaterial(decoyColor);
                decoyMaterial.EnableKeyword("_EMISSION");
                decoyMaterial.SetColor("_EmissionColor", decoyColor * 0.6f);
            }
            return decoyMaterial;
        }

        private Material GetLineMaterial()
        {
            if (lineMaterial == null)
            {
                lineMaterial = CreateLitMaterial(stopRed);
                lineMaterial.EnableKeyword("_EMISSION");
                lineMaterial.SetColor("_EmissionColor", stopRed * 1.4f);
            }
            return lineMaterial;
        }

        private static Material CreateLitMaterial(Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");
            var mat = new Material(shader);
            mat.color = color;
            return mat;
        }
    }
}
