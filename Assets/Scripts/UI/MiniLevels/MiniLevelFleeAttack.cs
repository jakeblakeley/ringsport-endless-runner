using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;
using System.Collections.Generic;
using RingSport.Effects;
using RingSport.Level;
using RingSport.Core;
using RingSport.Player;

namespace RingSport.UI
{
    /// <summary>
    /// Flee Attack mini level. Unlike the arena mini levels this one plays
    /// IN-RUN, during the Playing state, over the last stretch of its level:
    /// a decoy appears fleeing ahead of the dog, dodging barrels (one or two
    /// barrels across the 3 lanes per row) and dropping coins behind it
    /// while the gap slowly closes. For the final step the decoy commits to
    /// one lane and tall un-jumpable walls seal the other two - manual
    /// jumping locks, the dog auto-pounces at the decoy (animation sequence
    /// hook), and picking the decoy's lane catches it in the dog's mouth for
    /// a short carry over the finish line. Picking wrong is a standard death
    /// (which retries just the chase, not the whole run - see GameManager's
    /// MiniLevel-state reroute).
    ///
    /// LevelManager drives entry (BeginChase near the end of flee-attack
    /// levels) and exit (NotifyLevelEndReached at the finish line).
    /// The decoy is the human Decoy prefab (Tools > RingSport > Setup Decoy):
    /// it sprints and leans into lane changes, topples forward when the dog
    /// pounces, and on the catch its chest snaps to the jaw while the rest of
    /// the body ragdolls for the carry. A placeholder sphere remains as the
    /// fallback when the prefab isn't wired.
    /// </summary>
    public class MiniLevelFleeAttack : InRunMiniLevel
    {
        public static MiniLevelFleeAttack Instance { get; private set; }

        public override MiniLevelType MiniLevelType => MiniLevelType.FleeAttack;

        /// <summary>The wired Barlow banner font - the other in-run mini levels on this object use it as a fallback.</summary>
        public TMP_FontAsset BannerFontAsset => bannerFont;

        private enum ChasePhase { Inactive, Intro, Approach, Finale, Carry }

        // ---- Difficulty tables, indexed by flee-attack ordinal across the
        // ---- run (level 3 = 0, level 5 = 1, level 7 = 2). Later chases get
        // ---- longer, denser and twitchier but every row still fits the
        // ---- validated ~400ms mobile swipe budget (see fairness notes below).
        private static readonly float[] ChaseDurationSeconds = { 14f, 17f, 20f };
        private static readonly float[] ObstacleIntervalSeconds = { 1.8f, 1.5f, 1.25f };
        private static readonly float[] VoluntaryHopIntervalSeconds = { 2.4f, 1.9f, 1.5f };
        private static readonly float[] DoubleRowChance = { 0.55f, 0.7f, 0.85f };
        private static readonly float[] WallLeadSeconds = { 2.1f, 1.95f, 1.8f };

        // ---- Fairness model (mirrors ObstacleSpawner's action model):
        // one dodge needs ~0.4s swipe latency + 0.2s input cooldown + ~0.3s
        // lane lerp = ~0.9s. Barrels are dodge-only (Avoid type), so the
        // hard row-spacing floor (1.1s) is the true minimum action window and
        // keeps ~0.2s of slack at the tightest cadence; chase rows spawn
        // ReactionAheadSeconds ahead of the player, and the finale walls
        // telegraph for WallLeadSeconds >= 1.8s because the worst case there
        // is TWO lane changes (~1.55s). Double rows (two barrels, one open
        // lane) keep their open lane within one hop of the decoy's lane, so
        // consecutive rows never demand more than one change per 1.1s window
        // from a player on the decoy's tail.
        private const float ReactionAheadSeconds = 2.4f;   // row spawn distance, in time
        private const float DodgeLeadSeconds = 1.1f;       // decoy dodges this far ahead of a row
        private const float CalmTailSeconds = 2.5f;        // no voluntary hops at the end of approach
        private const float ObstacleStopTailSeconds = 3f;  // no new rows at the end of approach
        private const float MinRowSpacingSeconds = 1.1f;   // hard floor between consecutive rows

        // ---- Chase geometry / pacing
        private const float LaneDistance = 3f;
        private const float DecoyLaneLerpSpeed = 9f;
        private const float IntroSeconds = 1.6f;
        private const float IntroStartGap = 8f;    // decoy appears here, then flees...
        private const float FarGap = 24f;          // ...out to here
        private const float FinaleGap = 6f;        // gap held while the walls arrive
        private const float CatchGap = 0.9f;       // lunge target
        private const float LungeSeconds = 0.85f;
        private const float PounceTriggerGap = 3.5f; // auto-jump at the decoy from this gap (~0.45s before the catch)
        private const float MaxPounceSteer = 1.6f;   // cap on the dog-model steering offset toward the grab limb
        private const float PounceSteerRecoverSpeed = 3.5f; // m/s the model recenters at during the carry
        private const float CatchToEndBuffer = 3f; // catch lands this long before the level timer ends (short carry to the line)
        private const float CoinIntervalSeconds = 0.5f;
        private const float CoinLaneChangeHoldSeconds = 0.5f; // no coins right after a decoy hop - the first new-lane coin must land where the player can still react
        private const float DespawnBehind = 12f;
        private const int CatchBonusPoints = 150;

        [Header("Decoy")]
        [Tooltip("Human decoy prefab, wired by Tools > RingSport > Setup Decoy. Falls back to a placeholder sphere when missing.")]
        [SerializeField] private GameObject decoyPrefab;
        [Tooltip("Fallback sphere / trail color (banners are white).")]
        [SerializeField] private Color decoyColor = new Color(1f, 0.42f, 0.05f);
        [Tooltip("Font for the chase banners (FLEE ATTACK! etc.), wired by Tools > RingSport > Setup Decoy. TMP default when missing.")]
        [SerializeField] private TMP_FontAsset bannerFont;
        [SerializeField] private Color wallColor = new Color(0.55f, 0.08f, 0.08f);

        [Header("Audio")]
        [SerializeField] private AudioClip catchSound;
        [Tooltip("Decoy scream layered onto the catch (temporary clip - see SOUND_EFFECTS.md).")]
        [SerializeField] private AudioClip catchScreamSound;

        // Runtime state
        private ChasePhase phase = ChasePhase.Inactive;
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
        private readonly List<GameObject> finaleWalls = new List<GameObject>();

        private float obstacleTimer;
        private float voluntaryHopTimer;
        private float coinTimer;
        private float coinDropHold; // counts down after a decoy lane change

        // Finale
        private bool wallsCrossed;
        private float lungeTimer;
        private bool caught;
        private bool esquiveShown;       // "ESQUIVÉD!" fired for a finale (wrong lane) death
        private bool pounceDone;         // the scripted jump at the decoy has fired
        private bool controlLockActive;  // manual jump + lane input disabled for the catch sequence
        private Transform pounceMouth;   // jaw bone cached at the pounce, for steering
        private Vector3 pounceModelOffset; // current visual steering offset on the dog model

        // Retry scoring: the run score present when the chase first began, so a
        // chase-only retry can be re-seeded with it (mirrors how arena mini
        // levels keep the running-section score across retries)
        private int preChaseScore = -1;
        private bool retryEntryPending;

        // UI
        private Canvas bannerCanvas;
        private CanvasGroup bannerGroup;
        private TextMeshProUGUI bannerText;
        private Coroutine bannerRoutine;

        // Cached bits
        private Material decoyMaterial;
        private Material wallMaterial;

        // The decoy shows up on most levels eventually, so its assets are
        // always warmed at scene start (once per session)
        private static bool decoyAssetsWarmed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
                return;
            Instance = this;
            Register();
        }

        private void Start()
        {
            if (Instance == this && decoyPrefab != null && !decoyAssetsWarmed)
            {
                decoyAssetsWarmed = true;
                StartCoroutine(WarmUpDecoyAssets());
            }
        }

        /// <summary>
        /// Renders a near-invisible decoy in front of the camera for a few
        /// frames at scene start so its shaders compile and textures reach the
        /// GPU during the home screen, instead of popping in when the first
        /// chase spawns. The ragdoll prefab is instantiated once under an
        /// inactive holder (no Awake, no physics) to pre-deserialize it too.
        /// </summary>
        private IEnumerator WarmUpDecoyAssets()
        {
            var warmRoot = new GameObject("DecoyWarmUp");
            Camera cam = Camera.main;
            warmRoot.transform.position = cam != null
                ? cam.transform.position + cam.transform.forward * 1.6f + Vector3.down * 0.4f
                : new Vector3(0f, -40f, 0f);

            var warmDecoy = Instantiate(decoyPrefab, warmRoot.transform);
            warmDecoy.transform.localScale = Vector3.one * 0.02f;

            var warmController = warmDecoy.GetComponent<DecoyController>();
            if (warmController != null && warmController.RagdollPrefab != null)
            {
                var ragdollHolder = new GameObject("RagdollWarm");
                ragdollHolder.transform.SetParent(warmRoot.transform, false);
                ragdollHolder.SetActive(false);
                Instantiate(warmController.RagdollPrefab, ragdollHolder.transform);
            }

            yield return null;
            yield return null;
            yield return null;

            Destroy(warmRoot);
        }

        private void OnDestroy()
        {
            Unregister();
            if (Instance == this)
                Instance = null;
        }

        // ------------------------------------------------------------------
        // MiniLevelBase contract - only reachable if something starts this via
        // the arena mini-level flow (it shouldn't: GameManager reroutes the
        // FleeAttack type back into the Playing state). Complete immediately
        // so the game can never soft-lock.
        // ------------------------------------------------------------------
        public override void StartGame()
        {
            GameLog.Warn("[MiniLevelFleeAttack] StartGame called via arena flow - flee attack runs in-run. Completing immediately.");
            CompleteGame();
        }

        public override void StopGame()
        {
            Cleanup();
        }

        // ------------------------------------------------------------------
        // Public API used by LevelManager / GameManager
        // ------------------------------------------------------------------

        /// <summary>
        /// Seconds before the end of the level timer at which the chase must
        /// begin so the catch resolves before the finish line spawns.
        /// </summary>
        public override float GetLeadSeconds(int difficultyIndex)
        {
            int d = ClampDifficulty(difficultyIndex);
            return IntroSeconds + ChaseDurationSeconds[d] + WallLeadSeconds[d] + LungeSeconds + CatchToEndBuffer;
        }

        /// <summary>
        /// Called by LevelManager whenever a running level starts. Resets any
        /// leftover chase state; on a chase retry entry keeps the banked
        /// pre-chase score so BeginChase can re-seed it.
        /// </summary>
        public override void OnRunLevelStarted(bool isFleeAttackLevel, bool isRetryEntry)
        {
            Cleanup();
            retryEntryPending = isFleeAttackLevel && isRetryEntry;
            if (!isRetryEntry)
                preChaseScore = -1;
        }

        /// <summary>
        /// Starts the chase. Called by LevelManager during the Playing state
        /// when the level timer enters the flee-attack window.
        /// </summary>
        public override void BeginChase(int difficultyIndex)
        {
            if (chaseActive)
                return;

            playerController = Object.FindAnyObjectByType<PlayerController>();
            if (playerController == null)
            {
                GameLog.Error("[MiniLevelFleeAttack] No PlayerController found - cannot start chase");
                return;
            }
            playerTransform = playerController.transform;

            difficulty = ClampDifficulty(difficultyIndex);
            chaseActive = true;
            caught = false;
            esquiveShown = false;
            wallsCrossed = false;
            lungeTimer = 0f;
            pounceDone = false;
            controlLockActive = false;
            obstacleTimer = 0f;
            voluntaryHopTimer = 0f;
            coinTimer = 0f;
            coinDropHold = 0f;
            lastDodgeTime = -10f;

            // The standard spawners were already wound down by LevelManager a
            // few seconds ago (so the old course drained past the player with
            // no despawn pop); this is just an idempotent safety for entries
            // that skipped the wind-down. The chase owns generation from here.
            LevelGenerator.Instance?.SetRunnerSpawningSuppressed(true);

            // Bank the running-section score and flip the mini-level context so
            // a death in the chase retries the chase, not the whole run
            GameManager.Instance?.NotifyInRunMiniLevelStarted();

            if (retryEntryPending && preChaseScore > 0)
            {
                // Chase-only retry: restore the run score from the first attempt
                LevelManager.Instance?.AddScore(preChaseScore);
                GameLog.Info($"[MiniLevelFleeAttack] Retry entry - re-seeded pre-chase score: {preChaseScore}");
            }
            else
            {
                preChaseScore = ScoreManager.Instance?.CurrentScore ?? 0;
            }
            retryEntryPending = false;

            SpawnDecoy();
            ShowBanner("FLEE ATTACK!", Color.white, 1.6f);

            phase = ChasePhase.Intro;
            phaseTimer = 0f;

            GameLog.Info($"[MiniLevelFleeAttack] Chase started (difficulty {difficulty})");
        }

        /// <summary>
        /// Called by LevelManager when the finish line is reached on a flee
        /// attack level: the decoy disappears and everything cleans up.
        /// </summary>
        public override void NotifyLevelEndReached()
        {
            if (!chaseActive)
                return;

            GameLog.Info($"[MiniLevelFleeAttack] Finish line reached (caught: {caught})");
            Cleanup();
        }

        public override bool IsChaseActive => chaseActive;

        // ------------------------------------------------------------------
        // Chase simulation
        // ------------------------------------------------------------------

        private void Update()
        {
            if (!chaseActive)
                return;

            GameState state = GameManager.Instance != null ? GameManager.Instance.CurrentState : GameState.Home;

            // Death freeze-frames the chase; the retry/restart paths clean up.
            if (state == GameState.GameOver)
            {
                // A finale death is the wall row - the decoy juked the dog out
                // of its lane. Same callout as the face attack's wrong lane.
                if (!caught && !esquiveShown && phase == ChasePhase.Finale)
                {
                    esquiveShown = true;
                    ShowBanner("ESQUIVÉD!", new Color(0.85f, 0.09f, 0.09f), 1.4f);
                }
                return;
            }

            // Any other state (home, level complete reached without our
            // notify, etc.) means the run is over - tear down.
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
                case ChasePhase.Intro:
                    UpdateIntro(dt);
                    break;
                case ChasePhase.Approach:
                    UpdateApproach(dt);
                    break;
                case ChasePhase.Finale:
                    UpdateFinale(dt);
                    break;
                case ChasePhase.Carry:
                    // The body hangs from the jaw; ease the pounce-steering
                    // offset back out so the dog model recenters, carrying the
                    // bitten limb (and the dangling body) with it
                    EasePounceSteeringOut(dt);
                    break;
            }

            if (phase != ChasePhase.Carry)
                UpdateDecoyTransform(dt);

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
                phase = ChasePhase.Approach;
                phaseTimer = 0f;
            }
        }

        private void UpdateApproach(float dt)
        {
            float chaseDuration = ChaseDurationSeconds[difficulty];
            float t = Mathf.Clamp01(phaseTimer / chaseDuration);
            gap = Mathf.SmoothStep(FarGap, FinaleGap, t);

            float remaining = chaseDuration - phaseTimer;

            // Obstacle rows: one barrel, or two with a single open lane
            obstacleTimer += dt;
            if (remaining > ObstacleStopTailSeconds && obstacleTimer >= ObstacleIntervalSeconds[difficulty])
            {
                obstacleTimer = 0f;
                TrySpawnChaseObstacle();
            }

            // Decoy dodges rows in its lane (forced, telegraphed)
            UpdateDecoyDodging();

            // Voluntary lane hops for liveliness - suppressed near the end so
            // the decoy settles down before the finale
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
                EnterFinale();
        }

        private void EnterFinale()
        {
            phase = ChasePhase.Finale;
            phaseTimer = 0f;
            wallsCrossed = false;
            lungeTimer = 0f;

            // The decoy commits to its current lane; the other two lanes get
            // tall walls the dog can't jump. WallLeadSeconds of scroll time is
            // the reaction budget (>= worst case of two lane changes ~1.55s).
            float scrollSpeed = CurrentScrollSpeed();
            float wallZ = PlayerZ() + Mathf.Max(WallLeadSeconds[difficulty] * scrollSpeed, gap + 10f);

            foreach (int lane in new[] { -1, 0, 1 })
            {
                if (lane == decoyLane)
                    continue;
                finaleWalls.Add(BuildWall(new Vector3(lane * LaneDistance, 0f, wallZ)));
            }

            ShowBanner("CATCH HIM!", Color.white, 1.2f);
            GameLog.Info($"[MiniLevelFleeAttack] Finale - decoy locked lane {decoyLane}, walls at z {wallZ:F1}");
        }

        private void UpdateFinale(float dt)
        {
            // Coins keep dropping until the decoy threads the wall row
            if (!wallsCrossed)
                DropCoins(dt);

            // Hold the gap while the walls approach; the decoy visibly slips
            // through its open lane about half a second before the player
            if (!wallsCrossed)
            {
                gap = FinaleGap;

                bool crossed = false;
                foreach (var wall in finaleWalls)
                {
                    if (wall != null && wall.transform.position.z < PlayerZ() - 1.2f)
                        crossed = true;
                }

                if (crossed)
                {
                    wallsCrossed = true;

                    // The scripted catch sequence owns the dog from here -
                    // manual jumping AND lane switching are locked until the
                    // chase tears down (a lane hop mid-pounce breaks the beat;
                    // the wall row already guaranteed the player is in the
                    // decoy's lane)
                    playerController?.SetJumpEnabled(false);
                    playerController?.SetLaneChangeEnabled(false);
                    controlLockActive = true;
                }
                return;
            }

            // Player survived the wall row - the final lunge
            lungeTimer += dt;
            float t = Mathf.Clamp01(lungeTimer / LungeSeconds);
            gap = Mathf.SmoothStep(FinaleGap, CatchGap, t);

            // Scripted pounce: the dog leaps at the decoy, and the decoy
            // topples into its forward fall so the catch lands mid-collapse
            if (!pounceDone && gap <= PounceTriggerGap &&
                PlayerLane() == decoyLane && playerController != null && playerController.IsGrounded)
            {
                playerController.ForceJump();
                decoyHuman?.TriggerFall(); // locks in the grab limb
                pounceMouth = FindChildByName(playerTransform, "Jaw")
                              ?? FindChildByName(playerTransform, "Head");
                pounceModelOffset = Vector3.zero;
                pounceDone = true;
            }

            // The decoy holds its line while the DOG steers: the model blends
            // onto the chosen grab limb through the lunge so the mouth arrives
            // on the limb exactly as the bite connects
            if (pounceDone && !caught)
                UpdatePounceSteering();

            if (gap <= CatchGap + 0.15f)
            {
                if (PlayerLane() == decoyLane)
                {
                    DoCatch();
                }
                else
                {
                    // Failsafe only - lane input is locked once the walls are
                    // passed, so the lanes should always already match. If
                    // they somehow don't, the decoy dangles just out of reach
                    // until they do (the finish line force-completes at worst).
                    gap = CatchGap + 0.15f;
                }
            }
        }

        /// <summary>
        /// Blends the dog model onto the decoy's chosen grab limb across the
        /// lunge (weight 0 at the pounce trigger gap, 1 at the catch gap) so
        /// the mouth lands on the limb the moment the bite connects. Purely
        /// visual - the CharacterController is untouched.
        /// </summary>
        private void UpdatePounceSteering()
        {
            var animations = playerController != null ? playerController.Animations : null;
            if (decoyHuman == null || pounceMouth == null || animations == null)
                return;

            // Mouth position with the current steering offset removed, so the
            // correction doesn't feed back on itself (the jaw rides the model)
            Vector3 mouthBase = pounceMouth.position - pounceModelOffset;

            float p = Mathf.InverseLerp(PounceTriggerGap, CatchGap, gap);
            float w = p * p * (3f - 2f * p);

            pounceModelOffset = Vector3.ClampMagnitude(
                (decoyHuman.GetGrabLimbPosition() - mouthBase) * w, MaxPounceSteer);
            animations.SetModelOffset(pounceModelOffset);
        }

        private void EasePounceSteeringOut(float dt)
        {
            if (pounceModelOffset == Vector3.zero)
                return;

            var animations = playerController != null ? playerController.Animations : null;
            if (animations == null)
            {
                pounceModelOffset = Vector3.zero;
                return;
            }

            pounceModelOffset = Vector3.MoveTowards(pounceModelOffset, Vector3.zero, PounceSteerRecoverSpeed * dt);
            animations.SetModelOffset(pounceModelOffset);
        }

        private void DoCatch()
        {
            caught = true;
            phase = ChasePhase.Carry;
            phaseTimer = 0f;

            LevelManager.Instance?.AddScore(CatchBonusPoints);
            if (catchSound != null)
                LevelManager.Instance?.PlayCollectSound(catchSound);
            if (catchScreamSound != null)
                LevelManager.Instance?.PlayCollectSound(catchScreamSound);
            if (decoyRoot != null)
                ImpactVFX.PlayDust(decoyRoot.transform.position + Vector3.up * 1.1f, 12);
            CameraStateMachine.Instance?.AddShake(0.3f);
            ShowBanner("CAUGHT!", Color.white, 1.4f);

            AttachDecoyToMouth();

            GameLog.Info("[MiniLevelFleeAttack] Decoy caught! Carrying to finish line.");
        }

        /// <summary>
        /// Attaches the decoy to the dog's Jaw bone (Malbers Wolf rig) so it
        /// rides the head animation until the finish line. The human decoy's
        /// chest snaps to the jaw while the rest of the body ragdolls (see
        /// DecoyController.AttachToMouth); the fallback sphere just parents on.
        /// </summary>
        private void AttachDecoyToMouth()
        {
            if (decoyRoot == null)
                return;

            Transform model = playerController.Animations != null ? playerController.Animations.transform : playerTransform;
            Transform mouth = FindChildByName(playerTransform, "Jaw")
                              ?? FindChildByName(playerTransform, "Head")
                              ?? model;

            Vector3 forward = model != null ? model.forward : Vector3.forward;

            if (decoyHuman != null)
            {
                decoyHuman.AttachToMouth(mouth, forward, playerTransform);
                return;
            }

            // Fallback sphere: shrink to "held in mouth" size and kill the
            // chase dressing
            if (decoySphere != null)
            {
                decoySphere.localScale = Vector3.one * 0.63f;
                decoySphere.localPosition = Vector3.zero;
                var trail = decoySphere.GetComponent<TrailRenderer>();
                if (trail != null)
                    trail.emitting = false;
            }

            decoyRoot.transform.position = mouth.position + forward * 0.45f + Vector3.up * 0.03f;
            decoyRoot.transform.SetParent(mouth, true);
        }

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root == null)
                return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name)
                    return t;
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Decoy movement + dressing
        // ------------------------------------------------------------------

        private void SpawnDecoy()
        {
            DestroyDecoy();

            decoyLane = 0;
            decoyX = 0f;
            gap = IntroStartGap;
            bobPhase = 0f;

            // Human decoy prefab (Steve + DecoyController); the model has no
            // collider, so it can never block the CharacterController - the
            // catch stays distance-based
            if (decoyPrefab != null)
            {
                decoyRoot = Object.Instantiate(decoyPrefab);
                decoyRoot.name = "FleeAttackDecoy";
                decoyHuman = decoyRoot.GetComponent<DecoyController>();
            }

            if (decoyHuman == null)
            {
                // Fallback: the original placeholder sphere
                if (decoyRoot == null)
                    decoyRoot = new GameObject("FleeAttackDecoy");

                var sphereGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                sphereGO.name = "DecoyBody";
                Object.Destroy(sphereGO.GetComponent<Collider>()); // catch is distance-based; never block the CharacterController
                sphereGO.transform.SetParent(decoyRoot.transform, false);
                sphereGO.transform.localScale = Vector3.one * 1.2f;
                sphereGO.transform.localPosition = new Vector3(0f, 0.6f, 0f);
                decoySphere = sphereGO.transform;

                var renderer = sphereGO.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.sharedMaterial = GetDecoyMaterial();

                // Motion trail so the flee reads at a glance (best-effort; skipped
                // if the sprite shader isn't in the build)
                Shader trailShader = Shader.Find("Sprites/Default");
                if (trailShader != null)
                {
                    var trail = sphereGO.AddComponent<TrailRenderer>();
                    trail.time = 0.28f;
                    trail.startWidth = 0.45f;
                    trail.endWidth = 0f;
                    trail.minVertexDistance = 0.08f;
                    trail.material = new Material(trailShader);
                    trail.startColor = new Color(decoyColor.r, decoyColor.g, decoyColor.b, 0.55f);
                    trail.endColor = new Color(decoyColor.r, decoyColor.g, decoyColor.b, 0f);
                }
            }

            decoyRoot.transform.position = new Vector3(0f, 0f, PlayerZ() + gap);
        }

        private void UpdateDecoyTransform(float dt)
        {
            if (decoyRoot == null)
                return;

            decoyX = Mathf.Lerp(decoyX, decoyLane * LaneDistance, DecoyLaneLerpSpeed * dt);
            decoyRoot.transform.position = new Vector3(decoyX, 0f, PlayerZ() + gap);

            if (decoyHuman != null)
            {
                // Full-tilt sprint, leaning into lane changes with the same
                // signed lean the dog uses; the cycle tracks the level speed
                float strafe = Mathf.Clamp((decoyLane * LaneDistance - decoyX) / LaneDistance, -1f, 1f);
                float speedMultiplier = LevelGenerator.Instance?.GetCurrentConfig()?.SpeedMultiplier ?? 1f;
                decoyHuman.UpdateLocomotion(2f, strafe, speedMultiplier, Time.unscaledDeltaTime);
            }
            else if (decoySphere != null)
            {
                // Little bounding hops sell "running" on the fallback sphere
                bobPhase += dt * 3.2f;
                float bob = Mathf.Abs(Mathf.Sin(bobPhase * Mathf.PI)) * 0.22f;
                decoySphere.localPosition = new Vector3(0f, 0.6f + bob, 0f);
            }
        }

        // ------------------------------------------------------------------
        // Obstacles + dodging + coins
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

            // Double rows: two lanes barreled, one left open. The open lane
            // is always the decoy's lane or adjacent to it (see the fairness
            // notes above), so threading the row is at most one hop
            if (Random.value < DoubleRowChance[difficulty])
            {
                int openLane = PickDoubleRowOpenLane();
                foreach (int lane in new[] { -1, 0, 1 })
                {
                    if (lane != openLane)
                        SpawnBarrel(lane, spawnZ);
                }
                return;
            }

            // Single rows: ~45% target the decoy's lane so it visibly dodges
            // and leads the player; the rest threaten the side lanes
            int singleLane;
            if (Random.value < 0.45f)
            {
                singleLane = decoyLane;
            }
            else
            {
                int[] others = OtherLanes(decoyLane);
                singleLane = others[Random.Range(0, others.Length)];
            }
            SpawnBarrel(singleLane, spawnZ);
        }

        /// <summary>
        /// Half the time the open lane IS the decoy's lane (the barrels flank
        /// it); otherwise it's adjacent, and the decoy's dodge logic leads the
        /// player through it.
        /// </summary>
        private int PickDoubleRowOpenLane()
        {
            if (Random.value < 0.5f)
                return decoyLane;
            int[] adjacent = decoyLane == 0 ? new[] { -1, 1 } : new[] { 0 };
            return adjacent[Random.Range(0, adjacent.Length)];
        }

        private void SpawnBarrel(int lane, float spawnZ)
        {
            // Chase obstacles are always barrels (the ObstacleAvoid pool's
            // prefab) - dodge-only, so following the decoy is the whole game
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
                        coinDropHold = CoinLaneChangeHoldSeconds;
                    }
                }
            }
        }

        private int PickDodgeLane()
        {
            int[] candidates = decoyLane == 0 ? new[] { -1, 1 } : new[] { 0 };

            // Prefer a lane with no obstacle looming ahead of the decoy
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
            {
                decoyLane = target;
                coinDropHold = CoinLaneChangeHoldSeconds;
            }
        }

        private void DropCoins(float dt)
        {
            coinTimer += dt;

            // Right after a lane change the trail pauses: a coin dropped at
            // the hop point arrives before the player can follow the decoy
            // over, so the first new-lane coin waits until it can land with
            // reaction room to spare
            if (coinDropHold > 0f)
            {
                coinDropHold -= dt;
                return;
            }

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
        // Finale walls
        // ------------------------------------------------------------------

        private GameObject BuildWall(Vector3 position)
        {
            var root = new GameObject("FleeAttackWall");
            root.transform.position = position;
            root.tag = "Obstacle";

            var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "WallVisual";
            Object.Destroy(visual.GetComponent<Collider>());
            visual.transform.SetParent(root.transform, false);
            visual.transform.localScale = new Vector3(2.7f, 4.2f, 0.6f);
            visual.transform.localPosition = new Vector3(0f, 2.1f, 0f);

            var renderer = visual.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.sharedMaterial = GetWallMaterial();

            var trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(2.7f, 4.2f, 0.6f);
            trigger.center = new Vector3(0f, 2.1f, 0f);

            // Standard Avoid obstacle handling (game over on touch, at any
            // height - the wall is far taller than the 1.7 jump)
            root.AddComponent<Obstacle>().Configure(ObstacleType.Avoid);
            root.AddComponent<ScrollableObject>();

            StartCoroutine(ScaleInWall(visual.transform));
            return root;
        }

        private IEnumerator ScaleInWall(Transform visual)
        {
            Vector3 full = visual.localScale;
            float t = 0f;
            const float duration = 0.18f;
            while (t < duration && visual != null)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                visual.localScale = new Vector3(full.x, full.y * (0.15f + 0.85f * k), full.z);
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

            for (int i = finaleWalls.Count - 1; i >= 0; i--)
            {
                var wall = finaleWalls[i];
                if (wall == null)
                {
                    finaleWalls.RemoveAt(i);
                }
                else if (wall.transform.position.z < cutoff)
                {
                    Destroy(wall);
                    finaleWalls.RemoveAt(i);
                }
            }
        }

        /// <summary>Tears down every chase object and hands spawning back to the level.</summary>
        private void Cleanup()
        {
            bool wasActive = chaseActive;
            chaseActive = false;
            phase = ChasePhase.Inactive;

            // Release the catch-sequence control locks (ResetPosition also
            // does this on the retry/home paths; this covers the finish line)
            if (controlLockActive)
            {
                playerController?.SetJumpEnabled(true);
                playerController?.SetLaneChangeEnabled(true);
                controlLockActive = false;
            }

            // Recenter the dog model if a chase ended mid-steer
            if (pounceModelOffset != Vector3.zero)
            {
                playerController?.Animations?.SetModelOffset(Vector3.zero);
                pounceModelOffset = Vector3.zero;
            }
            pounceMouth = null;

            DestroyDecoy();

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

            foreach (var wall in finaleWalls)
            {
                if (wall != null)
                    Destroy(wall);
            }
            finaleWalls.Clear();

            if (bannerRoutine != null)
            {
                StopCoroutine(bannerRoutine);
                bannerRoutine = null;
            }
            if (bannerGroup != null)
                bannerGroup.alpha = 0f;

            LevelGenerator.Instance?.SetRunnerSpawningSuppressed(false);

            if (wasActive)
                GameLog.Info("[MiniLevelFleeAttack] Chase cleaned up");
        }

        private void DestroyDecoy()
        {
            if (decoyRoot != null)
            {
                // Works even while attached to the jaw - DecoyController's
                // OnDestroy tears down its ragdoll and mouth anchor with it
                Destroy(decoyRoot);
                decoyRoot = null;
                decoySphere = null;
                decoyHuman = null;
            }
        }

        // ------------------------------------------------------------------
        // Banner UI (built in code - no scene wiring required)
        // ------------------------------------------------------------------

        private void ShowBanner(string message, Color color, float holdSeconds)
        {
            EnsureBannerCanvas();
            if (bannerText == null)
                return;

            if (bannerRoutine != null)
                StopCoroutine(bannerRoutine);
            bannerRoutine = StartCoroutine(BannerRoutine(message, color, holdSeconds));
        }

        private IEnumerator BannerRoutine(string message, Color color, float holdSeconds)
        {
            bannerText.text = message;
            bannerText.color = color;

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

            var canvasGO = new GameObject("FleeAttackBannerCanvas");
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
            rt.sizeDelta = new Vector2(1000f, 220f);
            rt.anchoredPosition = Vector2.zero;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static int ClampDifficulty(int index)
        {
            return Mathf.Clamp(index, 0, ChaseDurationSeconds.Length - 1);
        }

        private float PlayerZ()
        {
            return playerTransform != null ? playerTransform.position.z : 0f;
        }

        private int PlayerLane()
        {
            if (playerController != null)
                return playerController.CurrentLane;
            float x = playerTransform != null ? playerTransform.position.x : 0f;
            return Mathf.Clamp(Mathf.RoundToInt(x / LaneDistance), -1, 1);
        }

        private float CurrentScrollSpeed()
        {
            float speed = LevelScroller.Instance != null ? LevelScroller.Instance.GetScrollSpeed() : 0f;
            return Mathf.Max(speed, 10f);
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

        private Material GetWallMaterial()
        {
            if (wallMaterial == null)
                wallMaterial = CreateLitMaterial(wallColor);
            return wallMaterial;
        }

        private static Material CreateLitMaterial(Color color)
        {
            // URP/Lit is stripped from builds (no shipping material references
            // it), so gameplay props use the game's own ArcEffect shader: it
            // always ships, SRP-batches with the world, and curves with the
            // arc like everything else.
            Shader shader = Shader.Find("Custom/Mobile/ArcEffect");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit"); // editor safety net
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            return mat;
        }
    }
}
