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
    /// Face Attack mini level - the third IN-RUN chase and the most staged
    /// one. It opens exactly like the flee attack (a decoy sprints ahead,
    /// dodging barrels and dropping coins), but instead of one catch the
    /// sequence runs THREE encounters. Each encounter: the barrels dry up,
    /// the decoy squares up in a fresh lane ("GET READY!"), wheels around to
    /// face the incoming dog while the gap collapses, and the dog LEAPS.
    /// Mid-pounce - airborne, right in the decoy's face - time freezes: the
    /// animator holds the leap pose, the world scroll snaps to a halt, the
    /// camera dives into a tight two-shot (dog left, decoy right) and four
    /// tap targets appear in screen space over the decoy's four limbs under
    /// "ATTACK THE RIGHT LIMB!". A moment later three of them are struck out
    /// with permanent-marker X's; the player has a short window to tap the
    /// one clean target. On the first two encounters even a correct read
    /// only forces a dodge - time resumes, the decoy twists away and the dog
    /// lands empty as the camera snaps back to the overhead run. On the
    /// third the bite lands: the dog takes the exact limb the player picked
    /// and ragdoll-drags the decoy over the finish line (the same carry as
    /// the flee attack). Wrong lane, wrong limb or too slow is a standard
    /// mini-level failure, which retries from the encounter that failed -
    /// not the whole run (GameManager's in-run reroute plus a resume index
    /// kept across the retry).
    ///
    /// Levels: 5 "Ring 2 - 2" (ordinal 0, easy - 1.6s tap window) and
    /// 8 "Ring 3 Finale" (ordinal 1, hard - shorter window, denser barrels).
    /// LevelManager drives entry (BeginChase near the end of the level);
    /// assets are wired by Tools > RingSport > Setup Face Attack.
    /// </summary>
    public class MiniLevelFaceAttack : InRunMiniLevel
    {
        public static MiniLevelFaceAttack Instance { get; private set; }

        public override MiniLevelType MiniLevelType => MiniLevelType.FaceAttack;

        private enum FacePhase
        {
            Inactive,
            Intro,        // decoy bolts out to the far gap
            Segment,      // flee-attack-style chase (barrels, coins, hops)
            Align,        // "GET READY!" - obstacles stopped, gap closes
            Charge,       // decoy wheels around to face the incoming dog
            Pounce,       // the dog leaps; a beat later time freezes
            Reveal,       // frozen mid-air: four targets + "ATTACK THE RIGHT LIMB!"
            Window,       // three X's stamped, tap the clean one
            DodgeResolve, // encounters 1-2: time resumes, the decoy slips away
            CatchLunge,   // encounter 3: the leap completes onto the picked limb
            Carry,        // ragdoll drag to the finish line
            FailEscape,   // wrong lane: the decoy plants and scrolls out behind the dog
            Failed        // frozen mid-pounce under the game over screen
        }

        private const int EncounterCount = 3;

        // ---- Difficulty tables, indexed by face-attack ordinal across the
        // ---- run (level 5 "Ring 2 - 2" = 0, level 8 "Ring 3 Finale" = 1).
        // ---- The chase segments reuse the flee attack's validated fairness
        // ---- cadences; the real difficulty knob is the tap window.
        private static readonly float[][] SegmentSeconds =
        {
            new[] { 7f, 6f, 6f },
            new[] { 8f, 7f, 7f },
        };
        private static readonly float[] ObstacleIntervalSeconds = { 1.8f, 1.3f };
        private static readonly float[] VoluntaryHopIntervalSeconds = { 2.4f, 1.7f };
        private static readonly float[] DoubleRowChance = { 0.55f, 0.85f };
        private static readonly float[] AlignSeconds = { 2.5f, 2.2f };
        private static readonly float[] TapWindowSeconds = { 1.6f, 1.1f };

        // ---- Fairness model (same action model as the flee/stop chases):
        // barrels are dodge-only, one or two per row (a double row's open
        // lane stays within one hop of the decoy's lane), rows spawn 2.4s
        // ahead with a hard 1.1s spacing floor. The align beat runs on a clean
        // track (rows stop 3s before the segment ends and arrive 2.4s after
        // spawn) and its 2.2-2.5s comfortably covers the worst case of two
        // lane changes (~1.55s at 400ms mobile latency). The tap windows are
        // reactions to a fully telegraphed reveal, like the stop whistle.
        private const float ReactionAheadSeconds = 2.4f;
        private const float DodgeLeadSeconds = 1.1f;
        private const float CalmTailSeconds = 2.5f;
        private const float ObstacleStopTailSeconds = 3f;
        private const float MinRowSpacingSeconds = 1.1f;

        // ---- Chase geometry / pacing
        private const float LaneDistance = 3f;
        private const float DecoyLaneLerpSpeed = 9f;
        private const float IntroSeconds = 1.6f;
        private const float IntroStartGap = 8f;   // decoy appears here, then flees...
        private const float FarGap = 24f;         // ...out to here for the chase segments
        private const float ChargeGap = 5f;       // the align beat closes to here...
        private const float PounceGap = 3.2f;     // ...the charge to here (the leap fires)...
        private const float FreezeGap = 1.8f;     // ...and time freezes here, mid-air in his face
        private const float EscapeGapRecoverSeconds = 2.5f; // post-dodge sprint back out to FarGap
        private const float DodgeEscapeGap = 11f;      // the dodge burst opens the gap to here...
        private const float DodgeEscapeSeconds = 1.0f; // ...this fast (ease-out: quickest at the start)
        private const float FailEscapeBehindGap = -6f;   // wrong lane: the stopped decoy scrolls to here behind the dog...
        private const float FailEscapeMinSeconds = 1.1f; // ...and the failure lands only after this beat has read
        private const float FailEscapeMaxSeconds = 1.8f;
        private const float ChargeSeconds = 0.6f; // decoy wheels around while the gap collapses
        private const float PounceFreezeDelaySeconds = 0.22f; // nominal leap runtime to the apex (drives the gap lerp; the freeze itself gates on the real jump state)
        private const float PounceMinAirSeconds = 0.05f;      // the freeze never lands the same instant as the takeoff
        private const float PounceApexFreezeVelocity = 2.5f;  // rising slower than this reads as the apex (~97% of full jump height)
        private const float PounceRelaunchDeadlineSeconds = 0.66f; // grounded this long after the leap fired = the jump was lost, fire it again
        private const int MaxPounceRelaunches = 2;
        private const float FreezeRampSeconds = 0.12f; // world scroll snaps (almost) instantly to the halt
        private const float RevealSeconds = 0.9f; // targets readable before the X's stamp
        private const float DodgeResolveSeconds = 1.6f;
        private const float DecoyTurnSeconds = 0.5f;
        private const float SpeedRestoreRampSeconds = 0.4f; // ease back up to run speed
        private const float CatchCloseSeconds = 0.35f; // rest of the leap: FreezeGap -> CatchGap
        private const float CatchGap = 0.9f;
        private const float MaxPounceSteer = 1.6f;   // cap on the dog-model steering toward the limb
        private const float PounceSteerRecoverSpeed = 3.5f;
        private const float CatchToEndBuffer = 3f;   // catch lands this long before the level timer ends
        private const float DecoyChestHeight = 1.55f; // camera aim point above the decoy's feet
        private const float CoinIntervalSeconds = 0.5f;
        private const float CoinLaneChangeHoldSeconds = 0.5f; // no coins right after a decoy hop - the first new-lane coin must land where the player can still react
        private const float DespawnBehind = 12f;
        private const int TapBonusPoints = 100;
        private const int CatchBonusPoints = 250;

        // The four QTE targets, index-matched to the widgets. All four map to
        // dedicated ragdoll rigidbodies (validated by DecoySetup), so the
        // final catch can pin whichever one the player picked.
        private static readonly DecoyLimb[] QteLimbs =
        {
            DecoyLimb.RightForearm,
            DecoyLimb.LeftForearm,
            DecoyLimb.RightCalf,
            DecoyLimb.LeftCalf,
        };

        [Header("Decoy")]
        [Tooltip("Human decoy prefab (same as the flee attack), wired by Tools > RingSport > Setup Face Attack. Falls back to a placeholder sphere when missing.")]
        [SerializeField] private GameObject decoyPrefab;
        [Tooltip("Fallback sphere color.")]
        [SerializeField] private Color decoyColor = new Color(1f, 0.42f, 0.05f);

        [Header("Quick Time Event")]
        [Tooltip("Font for the banners (FACE ATTACK!, ATTACK THE RIGHT LIMB!...), wired by Tools > RingSport > Setup Face Attack. TMP default when missing.")]
        [SerializeField] private TMP_FontAsset bannerFont;
        [Tooltip("Permanent Marker font for the X strike-outs on the three wrong limbs, wired by Tools > RingSport > Setup Face Attack.")]
        [SerializeField] private TMP_FontAsset markerFont;
        [Tooltip("The marker X's, the fail banners and the wrong-lane flash all use this red.")]
        [SerializeField] private Color xColor = new Color(0.85f, 0.09f, 0.09f);
        [Tooltip("Neutral tap-target ring color while all four limbs are still candidates. Once the X's stamp, the clean target becomes a solid white TAP disc with a radiating pulse.")]
        [SerializeField] private Color targetRingColor = Color.white;

        [Header("Freeze Camera (mid-pounce two-shot: dog left, decoy right)")]
        [Tooltip("Meters behind the airborne dog along the dog->decoy axis.")]
        [SerializeField] private float pounceCamBehindDog = 1f;
        [Tooltip("Lateral offset over the dog's right shoulder - this is what splits the frame (dog left, decoy right).")]
        [SerializeField] private float pounceCamSideOffset = 0.85f;
        [Tooltip("Camera height above the airborne dog's body center.")]
        [SerializeField] private float pounceCamHeight = 0.15f;
        [Tooltip("Aim point between the two: 0 = the dog, 1 = the decoy's chest.")]
        [Range(0f, 1f)]
        [SerializeField] private float pounceAimBias = 0.5f;
        [Tooltip("Slow dolly-in (m/s) that keeps creeping toward the decoy through the frozen QTE until it resolves or fails. The beat is short - 1.6s on the easy pass, 1.1s on the finale - so this is most of what decides how far the shot pushes in: roughly this many meters per second out of a ~3m starting distance.")]
        [SerializeField] private float pounceCamCreepSpeed = 0.45f;
        [Tooltip("The creep stops this far from the decoy's chest.")]
        [SerializeField] private float pounceCamMinDecoyDistance = 1.2f;

        [Header("Audio")]
        [SerializeField] private AudioClip tapSound;
        [SerializeField] private AudioClip catchSound;
        [Tooltip("Accelerating urgency tick while the frozen tap window drains (temporary clip - see SOUND_EFFECTS.md).")]
        [SerializeField] private AudioClip windowTickSound;
        [Tooltip("Decoy scream layered onto the catch (temporary clip - see SOUND_EFFECTS.md).")]
        [SerializeField] private AudioClip catchScreamSound;
        [Tooltip("Riser that hits as the world freezes mid-pounce and decays under the limb QTE. Plays on its own source so it can be faded.")]
        [SerializeField] private AudioClip freezeRiserSound;
        [Tooltip("Riser volume at the freeze, before the decay.")]
        [Range(0f, 1f)]
        [SerializeField] private float freezeRiserVolume = 1f;
        [Tooltip("Fraction of the riser left by the time the tap window runs out - it bleeds off across the whole frozen beat so the tick and the tap can breathe.")]
        [Range(0f, 1f)]
        [SerializeField] private float freezeRiserTailVolume = 0.3f;
        [Tooltip("Fast fade out when the QTE resolves - a correct tap, a dodge or any failure.")]
        [SerializeField] private float freezeRiserCutSeconds = 0.18f;

        // Runtime state
        private FacePhase phase = FacePhase.Inactive;
        private float phaseTimer;
        private float nextWindowTickIn;
        private int difficulty;
        private bool chaseActive;
        private PlayerController playerController;
        private Transform playerTransform;

        // Encounters
        private int currentEncounter;   // 0..2 while the chase runs
        private int resumeEncounter;    // where a chase retry re-enters (survives the retry like preChaseScore)
        private int correctTargetIndex;
        private float segmentStartGap;
        private float dodgeStartGap;
        private int lastStandoffLane = int.MinValue; // the decoy never squares up in the same lane twice in a row

        // Decoy
        private GameObject decoyRoot;
        private Transform decoySphere;  // fallback sphere only
        private DecoyController decoyHuman;
        private int decoyLane;
        private float decoyX;
        private float gap;
        private float bobPhase;
        private float lastDodgeTime;
        private float decoyYaw;         // 0 = fleeing, 180 = facing the dog

        // Standoff: the world brakes to a halt via the scroll override, the
        // dog's movement pauses (idle stance) and inputs lock for the QTE
        private float scrollOverride = -1f;
        private float scrollTarget;
        private float scrollRampRate;
        private bool restoreAfterRamp;
        private float qteEntrySpeed;
        private bool inputLocked;
        private bool movementPausedByQte;

        // Standoff camera: the shot is driven directly (the state machine's
        // scale/lookAt overload can't do the lateral two-shot offset), after
        // telling CameraStateMachine the pose is external so the restoring
        // SetState always transitions back
        private bool cameraZoomed;
        private bool drivingCamera;
        private Transform cameraTransform;
        private float camMoveTimer;
        private Vector3 camStartPos;
        private Quaternion camStartRot;
        private Vector3 camTargetPos;
        private Quaternion camTargetRot;
        private const float PounceCamMoveSeconds = 0.45f; // dive-in runs inside the frozen reveal beat

        // Pounce / carry (mirrors the flee attack catch)
        private bool caught;
        private bool animatorFrozenByQte;
        private Transform pounceMouth;
        private Vector3 pounceModelOffset;
        private int pounceRelaunches;

        // Spawned chase objects (self-managed; deliberately NOT registered
        // with DespawnManager so end-of-level sweeps can't eat them)
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
        private float coinDropHold; // counts down after a decoy lane change

        // Retry scoring (same pattern as the flee/stop attacks)
        private int preChaseScore = -1;
        private bool retryEntryPending;

        // Freeze riser: its own source, so the clip can decay under the QTE
        // and be cut short the instant the beat resolves
        private AudioSource riserSource;
        private Coroutine riserRoutine;

        // Banner UI
        private Canvas bannerCanvas;
        private CanvasGroup bannerGroup;
        private TextMeshProUGUI bannerText;
        private Coroutine bannerRoutine;

        // QTE UI: four buttons tracking the decoy's limbs in screen space.
        // Reveal shows four neutral rings; once the X's stamp, the one clean
        // target becomes a solid white disc reading "TAP" with a white ring
        // pulsing outward from it (scale up, fade out, fast loop).
        private class TargetWidget
        {
            public RectTransform root;
            public CanvasGroup group;
            public Image ring;          // neutral outline; dims under the X on wrong limbs
            public Image disc;          // solid white fill on THE limb once armed
            public Image pulse;         // radiating ring: scales up + fades out, looping
            public TextMeshProUGUI tap; // "TAP" on the disc
            public TextMeshProUGUI x;
        }
        private const float PulseCycleSeconds = 0.45f;
        private const float PulseMaxScale = 2f;
        private Canvas qteCanvas;
        private RectTransform qteCanvasRect;
        private CanvasGroup qteGroup;
        private TargetWidget[] targets;
        private bool targetsVisible;

        // Cached bits
        private Material decoyMaterial;
        private static Sprite ringSprite;
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
            GameLog.Warn("[MiniLevelFaceAttack] StartGame called via arena flow - face attack runs in-run. Completing immediately.");
            CompleteGame();
        }

        public override void StopGame()
        {
            Cleanup();
        }

        // ------------------------------------------------------------------
        // InRunMiniLevel contract, used by LevelManager / GameManager
        // ------------------------------------------------------------------

        /// <summary>
        /// Seconds before the end of the level timer at which the chase must
        /// begin. A retry that resumes at a later encounter needs less runway,
        /// so the lead is computed from the resume point (set before
        /// LevelManager fast-forwards the level timer).
        /// </summary>
        public override float GetLeadSeconds(int difficultyIndex)
        {
            int d = ClampDifficulty(difficultyIndex);
            float lead = IntroSeconds + CatchToEndBuffer;
            for (int k = Mathf.Clamp(resumeEncounter, 0, EncounterCount - 1); k < EncounterCount; k++)
                lead += EncounterBlockSeconds(d, k);
            return lead;
        }

        /// <summary>
        /// Scheduled length of one encounter, assuming the tap window runs to
        /// its end (an early tap just buys extra plain running before the next
        /// encounter's align beat - the finish line math never moves).
        /// </summary>
        private float EncounterBlockSeconds(int d, int encounter)
        {
            float resolve = encounter == EncounterCount - 1 ? CatchCloseSeconds : DodgeResolveSeconds;
            return SegmentSeconds[d][encounter] + AlignSeconds[d] + ChargeSeconds + PounceFreezeDelaySeconds +
                   RevealSeconds + TapWindowSeconds[d] + resolve;
        }

        public override void OnRunLevelStarted(bool isFaceAttackLevel, bool isRetryEntry)
        {
            Cleanup();
            retryEntryPending = isFaceAttackLevel && isRetryEntry;
            if (!isRetryEntry)
            {
                preChaseScore = -1;
                resumeEncounter = 0;
            }
        }

        public override void BeginChase(int difficultyIndex)
        {
            if (chaseActive)
                return;

            playerController = Object.FindAnyObjectByType<PlayerController>();
            if (playerController == null)
            {
                GameLog.Error("[MiniLevelFaceAttack] No PlayerController found - cannot start chase");
                return;
            }
            playerTransform = playerController.transform;

            difficulty = ClampDifficulty(difficultyIndex);
            chaseActive = true;
            currentEncounter = Mathf.Clamp(resumeEncounter, 0, EncounterCount - 1);
            caught = false;
            animatorFrozenByQte = false;
            inputLocked = false;
            movementPausedByQte = false;
            cameraZoomed = false;
            drivingCamera = false;
            scrollOverride = -1f;
            restoreAfterRamp = false;
            decoyYaw = 0f;
            lastStandoffLane = int.MinValue;
            obstacleTimer = 0f;
            voluntaryHopTimer = 0f;
            coinTimer = 0f;
            coinDropHold = 0f;
            lastDodgeTime = -10f;

            // Idempotent safety - LevelManager already wound spawning down
            LevelGenerator.Instance?.SetRunnerSpawningSuppressed(true);

            // Bank the running-section score and flip the mini-level context so
            // a failure here retries the face attack, not the whole run
            GameManager.Instance?.NotifyInRunMiniLevelStarted();

            if (retryEntryPending && preChaseScore > 0)
            {
                LevelManager.Instance?.AddScore(preChaseScore);
                GameLog.Info($"[MiniLevelFaceAttack] Retry entry - re-seeded pre-chase score: {preChaseScore}");
            }
            else
            {
                preChaseScore = ScoreManager.Instance?.CurrentScore ?? 0;
            }
            retryEntryPending = false;

            SpawnDecoy();
            ShowBanner("FACE ATTACK!", Color.white, 1.6f, 96f);

            phase = FacePhase.Intro;
            phaseTimer = 0f;

            GameLog.Info($"[MiniLevelFaceAttack] Chase started (difficulty {difficulty}, from encounter {currentEncounter + 1}/{EncounterCount}, window {TapWindowSeconds[difficulty]}s)");
        }

        public override void NotifyLevelEndReached()
        {
            if (!chaseActive)
                return;

            GameLog.Info($"[MiniLevelFaceAttack] Level end reached (caught: {caught})");
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

            // A failure freeze-frames the standoff (the camera creep stops
            // here too); the retry/restart paths clean up. The targets must
            // not float over the death screen.
            if (state == GameState.GameOver)
            {
                HideQteTargets();
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
            UpdateScrollRamp(dt);

            switch (phase)
            {
                case FacePhase.Intro:
                    UpdateIntro(dt);
                    break;
                case FacePhase.Segment:
                    UpdateSegment(dt);
                    break;
                case FacePhase.Align:
                    UpdateAlign(dt);
                    break;
                case FacePhase.Charge:
                    UpdateCharge(dt);
                    break;
                case FacePhase.Pounce:
                    UpdatePounce(dt);
                    break;
                case FacePhase.Reveal:
                    UpdateReveal(dt);
                    break;
                case FacePhase.Window:
                    UpdateWindow(dt);
                    break;
                case FacePhase.DodgeResolve:
                    UpdateDodgeResolve(dt);
                    break;
                case FacePhase.CatchLunge:
                    UpdateCatchLunge(dt);
                    break;
                case FacePhase.Carry:
                    EasePounceSteeringOut(dt);
                    break;
                case FacePhase.FailEscape:
                    UpdateFailEscape(dt);
                    break;
            }

            if (phase != FacePhase.Carry)
                UpdateDecoyTransform(dt);

            if (drivingCamera)
                UpdateCameraDrive(dt);

            UpdateQteTargets();
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
                EnterSegment(FarGap);
        }

        private void EnterSegment(float startGap)
        {
            phase = FacePhase.Segment;
            phaseTimer = 0f;
            segmentStartGap = startGap;
            obstacleTimer = 0f;
            voluntaryHopTimer = 0f;
        }

        private void UpdateSegment(float dt)
        {
            float segment = SegmentSeconds[difficulty][currentEncounter];
            float remaining = segment - phaseTimer;

            // After a dodge the decoy sprints back out to the chase gap; on
            // the first segment this is a no-op (the intro already opened it)
            gap = Mathf.SmoothStep(segmentStartGap, FarGap, Mathf.Clamp01(phaseTimer / EscapeGapRecoverSeconds));

            // Obstacle rows: one barrel, or two with a single open lane
            obstacleTimer += dt;
            if (remaining > ObstacleStopTailSeconds && obstacleTimer >= ObstacleIntervalSeconds[difficulty])
            {
                obstacleTimer = 0f;
                TrySpawnChaseObstacle();
            }

            UpdateDecoyDodging();

            // Voluntary lane hops for liveliness - suppressed near the end so
            // the decoy settles into the lane the player must match
            voluntaryHopTimer += dt;
            if (remaining > CalmTailSeconds &&
                voluntaryHopTimer >= VoluntaryHopIntervalSeconds[difficulty] &&
                Time.time - lastDodgeTime > 0.8f)
            {
                voluntaryHopTimer = Random.Range(-0.6f, 0.4f); // jitter the cadence
                TryVoluntaryHop();
            }

            DropCoins(dt);

            if (phaseTimer >= segment)
                EnterAlign();
        }

        private void EnterAlign()
        {
            phase = FacePhase.Align;
            phaseTimer = 0f;

            // The decoy commits to a fresh standoff lane - never the same one
            // twice in a row, so the squaring-up spot stays unpredictable.
            // The track ahead is clean, so following him is a pure steer.
            decoyLane = PickStandoffLane();
            lastStandoffLane = decoyLane;

            ShowBanner("GET READY!", Color.white, AlignSeconds[difficulty] - 0.3f, 84f);

            GameLog.Info($"[MiniLevelFaceAttack] Encounter {currentEncounter + 1}/{EncounterCount}: align beat (standoff lane {decoyLane})");
        }

        /// <summary>Uniform over the three lanes, excluding the previous standoff's lane.</summary>
        private int PickStandoffLane()
        {
            var options = new List<int> { -1, 0, 1 };
            options.Remove(lastStandoffLane); // no-op on the first standoff (sentinel)
            return options[Random.Range(0, options.Count)];
        }

        private void UpdateAlign(float dt)
        {
            // The decoy brakes: the gap closes from the chase distance toward
            // charge range while the player steers into its lane. The track
            // ahead is clean by construction (rows stopped 3s before the
            // segment ended), so the lane change is a pure steering ask.
            float t = Mathf.Clamp01(phaseTimer / AlignSeconds[difficulty]);
            gap = Mathf.SmoothStep(FarGap, ChargeGap, t);

            if (phaseTimer >= AlignSeconds[difficulty])
            {
                if (PlayerLane() == decoyLane)
                {
                    EnterCharge();
                }
                else
                {
                    // The pounce needs the dog on the decoy's line - being
                    // out of position IS the dodge: the decoy just plants
                    // and lets the dog blow past
                    EnterFailEscape();
                }
            }
        }

        /// <summary>
        /// Wrong-lane failure staging: "ESQUIVÉD!" - the decoy stops dead in
        /// its lane and rides the world scroll back past the (still running)
        /// dog, out of frame behind it, before the failure actually lands.
        /// </summary>
        private void EnterFailEscape()
        {
            phase = FacePhase.FailEscape;
            phaseTimer = 0f;

            // Planted and flexing while the dog blows past - pure matador
            decoyHuman?.TriggerPowerUp();
            ShowBanner("ESQUIVÉD!", xColor, 1.4f, 110f);
            GameLog.Info($"[MiniLevelFaceAttack] Failed encounter {currentEncounter + 1}: wrong lane - decoy plants and drops out of frame");
        }

        private void UpdateFailEscape(float dt)
        {
            // World-fixed now: the stopped decoy sweeps toward and past the dog
            gap -= CurrentScrollSpeed() * dt;

            bool wellBehind = gap <= FailEscapeBehindGap && phaseTimer >= FailEscapeMinSeconds;
            if (wellBehind || phaseTimer >= FailEscapeMaxSeconds)
            {
                phase = FacePhase.Failed;
                GameManager.Instance?.TriggerMiniLevelGameOver();
            }
        }

        private void EnterCharge()
        {
            phase = FacePhase.Charge;
            phaseTimer = 0f;

            // The scripted pounce owns the dog from here - manual jumping and
            // lane switching would only break the staging
            playerController?.SetJumpEnabled(false);
            playerController?.SetLaneChangeEnabled(false);
            inputLocked = true;

            // The decoy squares up flexing: arms-up power-up taunt, timed by
            // DecoySetup so the peak lands right at the freeze - it spreads
            // the limb targets and reads aggressive
            decoyHuman?.TriggerPowerUp();

            GameLog.Info("[MiniLevelFaceAttack] Charge: decoy wheeling around, gap collapsing");
        }

        private void UpdateCharge(float dt)
        {
            // The decoy wheels to face the incoming dog while the last meters
            // collapse at full run speed - no slow-mo yet
            float t = Mathf.Clamp01(phaseTimer / ChargeSeconds);
            gap = Mathf.SmoothStep(ChargeGap, PounceGap, t);

            if (phaseTimer >= ChargeSeconds)
                EnterPounce();
        }

        private void EnterPounce()
        {
            phase = FacePhase.Pounce;
            phaseTimer = 0f;
            pounceRelaunches = 0;

            // The leap. (If a last-second manual jump left the dog already
            // airborne, that reads as the pounce too - the freeze catches it
            // mid-air either way.)
            playerController?.ForceJump();
            pounceMouth = FindChildByName(playerTransform, "Jaw")
                          ?? FindChildByName(playerTransform, "Head");
            pounceModelOffset = Vector3.zero;
        }

        private void UpdatePounce(float dt)
        {
            // Airborne at full speed for a beat - the freeze lands at the
            // apex of the jump, right in the decoy's face. Gated on the dog's
            // REAL jump state, not just the wall clock: a hitchy frame here
            // (floor spawn, GC) used to lap the physics and freeze the dog
            // still on the ground.
            float t = Mathf.Clamp01(phaseTimer / PounceFreezeDelaySeconds);
            gap = Mathf.Lerp(PounceGap, FreezeGap, t);

            bool airborne = playerController != null && !playerController.IsGrounded;

            if (airborne && phaseTimer >= PounceMinAirSeconds &&
                playerController.VerticalVelocity <= PounceApexFreezeVelocity)
            {
                gap = FreezeGap;
                EnterReveal();
                return;
            }

            if (phaseTimer < PounceRelaunchDeadlineSeconds)
                return;

            // Still grounded this far in: a single oversized gravity step can
            // wipe the whole takeoff velocity before the first Move applies
            // it. Fire the leap again and wait for the apex once more.
            if (!airborne && pounceRelaunches < MaxPounceRelaunches)
            {
                pounceRelaunches++;
                phaseTimer = 0f;
                playerController?.ForceJump();
                GameLog.Info("[MiniLevelFaceAttack] Pounce leap was lost to a frame hitch - relaunching");
                return;
            }

            // Last resort so the beat can never stall: freeze wherever the dog is
            gap = FreezeGap;
            EnterReveal();
        }

        /// <summary>
        /// The freeze: the world scroll snaps to a halt, movement pauses with
        /// the dog mid-air (gravity stops with it) and the animator holds the
        /// leap pose, while the camera dives into the tight two-shot and the
        /// four limb targets come up. Time stands still until the tap - or
        /// stays frozen under the game over screen on a failure.
        /// </summary>
        private void EnterReveal()
        {
            phase = FacePhase.Reveal;
            phaseTimer = 0f;
            correctTargetIndex = Random.Range(0, QteLimbs.Length);

            // Freeze the world...
            qteEntrySpeed = CurrentScrollSpeed();
            scrollOverride = qteEntrySpeed;
            scrollTarget = 0f;
            scrollRampRate = Mathf.Max(qteEntrySpeed, 1f) / FreezeRampSeconds;
            restoreAfterRamp = false;
            LevelScroller.Instance?.SetSpeedOverride(scrollOverride);

            // ...and the dog with it, mid-leap
            playerController?.PauseMovement();
            movementPausedByQte = true;
            playerController?.Animations?.SetAnimatorPaused(true);
            animatorFrozenByQte = true;

            BeginPounceShot();
            ShowQteTargets();
            ShowBanner("ATTACK THE RIGHT LIMB!", Color.white,
                RevealSeconds + TapWindowSeconds[difficulty] - 0.2f, 76f);

            // Bullet time gets quiet: the music drops to a whisper until the
            // window resolves (the urgency tick lives in UpdateWindow), with
            // the riser hitting on the freeze and then bleeding away across
            // the whole frozen beat
            GameManager.Instance?.SetMusicDuck(true);
            PlayFreezeRiser();

            GameLog.Info($"[MiniLevelFaceAttack] Time frozen mid-pounce - correct limb: {QteLimbs[correctTargetIndex]}");
        }

        /// <summary>
        /// Starts the tight over-the-shoulder two-shot on the frozen leap:
        /// camera dives in over the dog's right shoulder aimed between the
        /// airborne dog (LEFT of frame) and the decoy (RIGHT). Driven
        /// directly (the state machine's overload has no lateral offset)
        /// after telling the state machine the pose is external, so the
        /// restoring SetState still transitions cleanly back to the run.
        /// </summary>
        private void BeginPounceShot()
        {
            var stateMachine = CameraStateMachine.Instance;
            if (stateMachine == null || decoyRoot == null || playerTransform == null)
                return;

            stateMachine.NotifyExternalPose();
            cameraTransform = stateMachine.transform;

            // The dog is airborne here - frame its actual mid-leap body center
            Vector3 dogBody = playerTransform.position + Vector3.up * 0.45f;
            Vector3 decoyChest = decoyRoot.transform.position + Vector3.up * DecoyChestHeight;

            Vector3 axis = decoyChest - dogBody;
            axis.y = 0f;
            axis = axis.sqrMagnitude > 0.001f ? axis.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, axis); // the dog's right

            camTargetPos = dogBody
                           - axis * pounceCamBehindDog
                           + side * pounceCamSideOffset
                           + Vector3.up * pounceCamHeight;
            Vector3 aim = Vector3.Lerp(dogBody, decoyChest, pounceAimBias);
            camTargetRot = Quaternion.LookRotation(aim - camTargetPos, Vector3.up);

            camStartPos = cameraTransform.position;
            camStartRot = cameraTransform.rotation;
            camMoveTimer = 0f;
            drivingCamera = true;
            cameraZoomed = true;
        }

        /// <summary>
        /// The dive-in (running inside the frozen beat - pure bullet time),
        /// then a slow relentless dolly toward the decoy that only stops when
        /// the beat resolves - or freezes where it is on a failure (the
        /// GameOver state gates this update).
        /// </summary>
        private void UpdateCameraDrive(float dt)
        {
            if (cameraTransform == null)
                return;

            if (camMoveTimer < PounceCamMoveSeconds)
            {
                camMoveTimer += dt;
                float k = Mathf.Clamp01(camMoveTimer / PounceCamMoveSeconds);
                k = k * k * (3f - 2f * k);
                cameraTransform.position = Vector3.Lerp(camStartPos, camTargetPos, k);
                cameraTransform.rotation = Quaternion.Slerp(camStartRot, camTargetRot, k);
                return;
            }

            if (decoyRoot == null)
                return;

            Vector3 decoyChest = decoyRoot.transform.position + Vector3.up * DecoyChestHeight;
            Vector3 toDecoy = decoyChest - cameraTransform.position;
            if (toDecoy.magnitude > pounceCamMinDecoyDistance)
                cameraTransform.position += toDecoy.normalized * (pounceCamCreepSpeed * dt);
        }

        private void UpdateReveal(float dt)
        {
            if (phaseTimer >= RevealSeconds)
                EnterWindow();
        }

        private void EnterWindow()
        {
            phase = FacePhase.Window;
            phaseTimer = 0f;
            nextWindowTickIn = 0f;
            StampWrongTargets();
        }

        private void UpdateWindow(float dt)
        {
            // Urgency tick accelerates as the frozen window drains
            nextWindowTickIn -= dt;
            if (nextWindowTickIn <= 0f && windowTickSound != null)
            {
                float progress = Mathf.Clamp01(phaseTimer / TapWindowSeconds[difficulty]);
                LevelManager.Instance?.PlayPitchedSound(windowTickSound, Mathf.Lerp(0.85f, 1.25f, progress), 0.35f);
                nextWindowTickIn = Mathf.Lerp(0.45f, 0.16f, progress);
            }

            if (phaseTimer >= TapWindowSeconds[difficulty])
                FailQte("TOO SLOW!");
        }

        private void OnTargetTapped(int index)
        {
            if (!chaseActive || phase != FacePhase.Window)
                return; // taps before the X's are stamped are free

            // Press pop on the tapped widget (these runtime buttons have no
            // Unity transition - ColorTint fought their own color states)
            if (targets != null && index < targets.Length && targets[index].root != null)
                Juice.PunchScale(targets[index].root, 0.18f, 0.12f);

            if (index == correctTargetIndex)
                ResolveCorrectTap();
            else
                FailQte("WRONG LIMB!");
        }

        private void ResolveCorrectTap()
        {
            LevelManager.Instance?.AddScore(TapBonusPoints);
            if (tapSound != null)
                LevelManager.Instance?.PlayCollectSound(tapSound);

            // Bite impact: white burst on the picked limb + a camera hit,
            // and the music comes back up out of the duck
            Vector3 limbPos = decoyHuman != null
                ? decoyHuman.GetLimbPosition(QteLimbs[correctTargetIndex])
                : FallbackLimbPosition(correctTargetIndex);
            CollectBurstVFX.PlayLife(limbPos);
            CameraStateMachine.Instance?.AddShake(0.25f);
            GameManager.Instance?.SetMusicDuck(false);
            CutFreezeRiser();

            HideQteTargets();

            // Time snaps back: the leap resumes, the world speed ramps up and
            // the camera returns to the overhead run
            UnfreezeDog();
            RestoreWorldPace();
            RestoreCamera();

            if (currentEncounter < EncounterCount - 1)
                EnterDodgeResolve();
            else
                EnterCatchLunge();
        }

        private void EnterDodgeResolve()
        {
            phase = FacePhase.DodgeResolve;
            phaseTimer = 0f;

            // The leap completes... and the decoy isn't there anymore: it
            // slips into another lane and bolts. The dog lands empty.
            int[] escapes = OtherLanes(decoyLane);
            decoyLane = escapes[Random.Range(0, escapes.Length)];
            dodgeStartGap = gap;
            decoyHuman?.ResumeLocomotion(); // drop the power-up pose and sprint

            playerController?.SetJumpEnabled(true);
            playerController?.SetLaneChangeEnabled(true);
            inputLocked = false;

            ShowBanner("HE DODGED!", Color.white, 1.1f, 96f);
            GameLog.Info($"[MiniLevelFaceAttack] Encounter {currentEncounter + 1} read correctly - decoy dodged to lane {decoyLane}");
        }

        private void UpdateDodgeResolve(float dt)
        {
            // The escape burst: the gap tears open fastest in the first
            // moments (ease-out), so the landing dog can never overlap the
            // decoy even if the player snaps into its new lane immediately
            float t = Mathf.Clamp01(phaseTimer / DodgeEscapeSeconds);
            float ease = 1f - (1f - t) * (1f - t);
            gap = Mathf.Lerp(dodgeStartGap, DodgeEscapeGap, ease);

            if (phaseTimer >= DodgeResolveSeconds)
            {
                currentEncounter++;
                resumeEncounter = currentEncounter; // a later death retries from here
                EnterSegment(gap);
            }
        }

        private void EnterCatchLunge()
        {
            phase = FacePhase.CatchLunge;
            phaseTimer = 0f;

            // Inputs stay locked - the leap finishes as a scripted catch: the
            // decoy topples and the dog-model steers its mouth onto the limb
            // the player picked
            decoyHuman?.TriggerFall(QteLimbs[correctTargetIndex]);

            GameLog.Info("[MiniLevelFaceAttack] Final encounter read correctly - the bite goes in");
        }

        private void UpdateCatchLunge(float dt)
        {
            float t = Mathf.Clamp01(phaseTimer / CatchCloseSeconds);
            gap = Mathf.SmoothStep(FreezeGap, CatchGap, t);

            if (!caught)
                UpdatePounceSteering();

            if (gap <= CatchGap + 0.15f)
                DoCatch();
        }

        /// <summary>
        /// Blends the dog model onto the picked limb across the rest of the
        /// leap (weight 0 at the freeze gap, 1 at the catch gap) so the mouth
        /// lands on the limb the moment the bite connects. Purely visual -
        /// the CharacterController is untouched. (Same steering as the flee
        /// attack.)
        /// </summary>
        private void UpdatePounceSteering()
        {
            var animations = playerController != null ? playerController.Animations : null;
            if (decoyHuman == null || pounceMouth == null || animations == null)
                return;

            Vector3 mouthBase = pounceMouth.position - pounceModelOffset;

            float p = Mathf.InverseLerp(FreezeGap, CatchGap, gap);
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
            phase = FacePhase.Carry;
            phaseTimer = 0f;

            LevelManager.Instance?.AddScore(CatchBonusPoints);
            if (catchSound != null)
                LevelManager.Instance?.PlayCollectSound(catchSound);
            if (catchScreamSound != null)
                LevelManager.Instance?.PlayCollectSound(catchScreamSound);
            if (decoyRoot != null)
                ImpactVFX.PlayDust(decoyRoot.transform.position + Vector3.up * 1.1f, 12);
            CameraStateMachine.Instance?.AddShake(0.3f);
            ShowBanner("GOT HIM!", Color.white, 1.4f, 110f);

            AttachDecoyToMouth();

            GameLog.Info($"[MiniLevelFaceAttack] Bite landed on {QteLimbs[correctTargetIndex]} - carrying to the finish line");
        }

        /// <summary>
        /// Attaches the decoy to the dog's Jaw bone by the exact limb the
        /// player picked in the QTE: the limb pins kinematically to the mouth
        /// while the rest of the body ragdolls for the carry (see
        /// DecoyController.AttachToMouth). The fallback sphere just parents on.
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
                decoyHuman.AttachToMouth(mouth, forward, playerTransform, QteLimbs[correctTargetIndex]);
                return;
            }

            // Fallback sphere: shrink to "held in mouth" size
            if (decoySphere != null)
            {
                decoySphere.localScale = Vector3.one * 0.63f;
                decoySphere.localPosition = Vector3.zero;
            }

            decoyRoot.transform.position = mouth.position + forward * 0.45f + Vector3.up * 0.03f;
            decoyRoot.transform.SetParent(mouth, true);
        }

        /// <summary>
        /// Any QTE failure (wrong lane, wrong limb, too slow): freeze-frame
        /// the standoff and hand it to the standard mini-level game over,
        /// which retries from this encounter (resumeEncounter tracks it).
        /// </summary>
        private void FailQte(string message)
        {
            phase = FacePhase.Failed;
            HideQteTargets();
            GameManager.Instance?.SetMusicDuck(false);
            CutFreezeRiser();
            ShowBanner(message, xColor, 1.6f, 110f);

            GameLog.Info($"[MiniLevelFaceAttack] Failed encounter {currentEncounter + 1}: {message}");
            GameManager.Instance?.TriggerMiniLevelGameOver();
        }

        // ------------------------------------------------------------------
        // World pace + camera (the "time slows" beat)
        // ------------------------------------------------------------------

        private void UpdateScrollRamp(float dt)
        {
            if (scrollOverride < 0f)
                return;

            scrollOverride = Mathf.MoveTowards(scrollOverride, scrollTarget, scrollRampRate * dt);
            LevelScroller.Instance?.SetSpeedOverride(scrollOverride);

            // A restore ramp hands control back to the live (player/level)
            // speed once it arrives back at the entry pace
            if (restoreAfterRamp && Mathf.Approximately(scrollOverride, scrollTarget))
            {
                LevelScroller.Instance?.ClearSpeedOverride();
                scrollOverride = -1f;
                restoreAfterRamp = false;
            }
        }

        private void RestoreWorldPace()
        {
            if (movementPausedByQte)
            {
                playerController?.ResumeMovement();
                movementPausedByQte = false;
            }

            if (scrollOverride >= 0f)
            {
                scrollTarget = Mathf.Max(qteEntrySpeed, 1f);
                scrollRampRate = scrollTarget / SpeedRestoreRampSeconds;
                restoreAfterRamp = true;
            }
        }

        /// <summary>Lets the frozen mid-pounce animation play on (gravity resumes via RestoreWorldPace's movement unpause).</summary>
        private void UnfreezeDog()
        {
            if (!animatorFrozenByQte)
                return;
            animatorFrozenByQte = false;
            playerController?.Animations?.SetAnimatorPaused(false);
        }

        private void RestoreCamera()
        {
            drivingCamera = false;
            if (!cameraZoomed)
                return;
            cameraZoomed = false;
            // NotifyExternalPose forgot the applied scale, so this always
            // transitions back from wherever the standoff shot crept to
            CameraStateMachine.Instance?.SetState(CameraStateType.Gameplay);
        }

        // ------------------------------------------------------------------
        // Freeze riser audio
        //
        // The riser clip outlives the beat it scores (~4.3s against a 2.5s
        // worst-case frozen window), so it gets its own source rather than a
        // fire-and-forget PlayOneShot: it hits full-volume on the freeze,
        // bleeds down to a bed across the reveal + tap window (leaving room
        // for the accelerating urgency tick), then cuts fast the moment the
        // beat resolves - a tap, a dodge or a failure. Everything runs on
        // unscaled time, like the banners.
        // ------------------------------------------------------------------

        private void PlayFreezeRiser()
        {
            if (freezeRiserSound == null)
                return;

            EnsureRiserSource();

            if (riserRoutine != null)
                StopCoroutine(riserRoutine);

            riserSource.clip = freezeRiserSound;
            riserSource.volume = freezeRiserVolume;
            riserSource.Play();

            riserRoutine = StartCoroutine(RiserDecayRoutine(RevealSeconds + TapWindowSeconds[difficulty]));
        }

        private IEnumerator RiserDecayRoutine(float duration)
        {
            float tail = freezeRiserVolume * freezeRiserTailVolume;
            float t = 0f;
            while (t < duration && riserSource != null)
            {
                t += Time.unscaledDeltaTime;
                riserSource.volume = Mathf.Lerp(freezeRiserVolume, tail, Mathf.Clamp01(t / duration));
                yield return null;
            }
            if (riserSource != null)
                riserSource.volume = tail;
            riserRoutine = null;
        }

        /// <summary>
        /// The quick fade as the QTE resolves. Runs on through the game over
        /// state on a failure (Update is gated there, coroutines are not).
        /// </summary>
        private void CutFreezeRiser()
        {
            if (riserSource == null || !riserSource.isPlaying)
                return;

            if (riserRoutine != null)
                StopCoroutine(riserRoutine);
            riserRoutine = StartCoroutine(RiserCutRoutine());
        }

        private IEnumerator RiserCutRoutine()
        {
            float from = riserSource.volume;
            float t = 0f;
            while (t < freezeRiserCutSeconds && riserSource != null)
            {
                t += Time.unscaledDeltaTime;
                riserSource.volume = Mathf.Lerp(from, 0f, Mathf.Clamp01(t / freezeRiserCutSeconds));
                yield return null;
            }
            riserRoutine = null; // cleared first: StopFreezeRiser would stop this very coroutine
            if (riserSource != null)
            {
                riserSource.Stop();
                riserSource.volume = freezeRiserVolume;
            }
        }

        /// <summary>Hard stop, for teardown (a retry re-triggers the whole beat).</summary>
        private void StopFreezeRiser()
        {
            if (riserRoutine != null)
            {
                StopCoroutine(riserRoutine);
                riserRoutine = null;
            }
            if (riserSource == null)
                return;
            riserSource.Stop();
            riserSource.volume = freezeRiserVolume;
        }

        private void EnsureRiserSource()
        {
            if (riserSource != null)
                return;
            riserSource = gameObject.AddComponent<AudioSource>();
            riserSource.playOnAwake = false;
            riserSource.loop = false;
            riserSource.spatialBlend = 0f;
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
            decoyYaw = 0f;

            // Human decoy prefab (Steve + DecoyController); the model has no
            // collider, so it can never block the CharacterController
            if (decoyPrefab != null)
            {
                decoyRoot = Object.Instantiate(decoyPrefab);
                decoyRoot.name = "FaceAttackDecoy";
                decoyHuman = decoyRoot.GetComponent<DecoyController>();
            }

            if (decoyHuman == null)
            {
                // Fallback: the placeholder sphere
                if (decoyRoot == null)
                    decoyRoot = new GameObject("FaceAttackDecoy");

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

            bool fleeing = phase == FacePhase.Intro || phase == FacePhase.Segment || phase == FacePhase.Align;
            bool escaping = phase == FacePhase.DodgeResolve;

            decoyX = Mathf.Lerp(decoyX, decoyLane * LaneDistance, DecoyLaneLerpSpeed * dt);
            decoyRoot.transform.position = new Vector3(decoyX, 0f, PlayerZ() + gap);

            // The turn-around: 180 to face the dog for the standoff, back to 0
            // when it bolts again after a dodge
            float targetYaw = (fleeing || escaping) ? 0f : 180f;
            decoyYaw = Mathf.MoveTowards(decoyYaw, targetYaw, (180f / DecoyTurnSeconds) * dt);
            decoyRoot.transform.rotation = Quaternion.Euler(0f, decoyYaw, 0f);

            if (decoyHuman != null)
            {
                float speedMultiplier = LevelGenerator.Instance?.GetCurrentConfig()?.SpeedMultiplier ?? 1f;
                bool running = fleeing || (escaping && decoyYaw < 90f);
                if (running)
                {
                    // Full-tilt sprint, leaning into lane changes
                    float strafe = Mathf.Clamp((decoyLane * LaneDistance - decoyX) / LaneDistance, -1f, 1f);
                    decoyHuman.UpdateLocomotion(2f, strafe, speedMultiplier, Time.unscaledDeltaTime);
                }
                else
                {
                    // Damped down to a defiant stand facing the dog; during
                    // the frozen QTE its idle sway slows to a crawl so the
                    // whole tableau reads as stopped time
                    bool frozen = phase == FacePhase.Reveal || phase == FacePhase.Window || phase == FacePhase.Failed;
                    decoyHuman.UpdateLocomotion(0f, 0f, frozen ? 0.12f : 1f, Time.unscaledDeltaTime);
                }
            }
            else if (decoySphere != null && (fleeing || escaping))
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
            // Both other lanes are candidates (a side lane can dash across to
            // the far side, not just retreat to center) - prefer a clear one
            int[] candidates = OtherLanes(decoyLane);

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
            // Either other lane, tried in random order so the decoy weaves
            // across the whole track instead of ping-ponging around center
            int[] candidates = OtherLanes(decoyLane);
            int first = Random.Range(0, candidates.Length);
            for (int i = 0; i < candidates.Length; i++)
            {
                int lane = candidates[(first + i) % candidates.Length];
                if (IsLaneClearAheadOfDecoy(lane))
                {
                    decoyLane = lane;
                    coinDropHold = CoinLaneChangeHoldSeconds;
                    return;
                }
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
        // QTE targets (screen-space widgets tracking the decoy's limbs)
        // ------------------------------------------------------------------

        private void ShowQteTargets()
        {
            EnsureQteCanvas();
            targetsVisible = true;
            qteGroup.alpha = 1f;
            qteGroup.blocksRaycasts = true;
            qteGroup.interactable = true;

            for (int i = 0; i < targets.Length; i++)
            {
                var widget = targets[i];
                widget.group.alpha = 1f;
                widget.root.localScale = Vector3.one;
                widget.ring.gameObject.SetActive(true);
                widget.ring.color = targetRingColor;
                widget.disc.gameObject.SetActive(false);
                widget.tap.gameObject.SetActive(false);
                widget.pulse.gameObject.SetActive(false);
                widget.x.gameObject.SetActive(false);
            }

            UpdateQteTargets();
        }

        private void HideQteTargets()
        {
            if (qteGroup == null)
                return;
            targetsVisible = false;
            qteGroup.alpha = 0f;
            qteGroup.blocksRaycasts = false;
            qteGroup.interactable = false;
        }

        /// <summary>
        /// Strikes out the three wrong limbs with marker X's; the clean one
        /// arms - solid white TAP disc with the radiating pulse ring.
        /// </summary>
        private void StampWrongTargets()
        {
            if (targets == null)
                return;

            for (int i = 0; i < targets.Length; i++)
            {
                if (i == correctTargetIndex)
                {
                    var widget = targets[i];
                    widget.ring.gameObject.SetActive(false);
                    widget.disc.gameObject.SetActive(true);
                    widget.tap.gameObject.SetActive(true);
                    widget.pulse.gameObject.SetActive(true);
                    widget.pulse.rectTransform.localScale = Vector3.one;
                    widget.pulse.color = new Color(1f, 1f, 1f, 0f);
                    continue;
                }

                var x = targets[i].x;
                x.gameObject.SetActive(true);
                x.rectTransform.localEulerAngles = new Vector3(0f, 0f, Random.Range(-14f, 14f));
                StartCoroutine(StampX(x.rectTransform));

                // The struck-out ring recedes
                Color dim = targetRingColor;
                dim.a *= 0.35f;
                targets[i].ring.color = dim;
            }
        }

        private IEnumerator StampX(RectTransform x)
        {
            float t = 0f;
            const float duration = 0.12f;
            while (t < duration && x != null)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / duration);
                x.localScale = Vector3.one * Mathf.Lerp(2.2f, 1f, k * k);
                yield return null;
            }
            if (x != null)
                x.localScale = Vector3.one;
        }

        /// <summary>Keeps the four widgets glued to the limb bones in screen space.</summary>
        private void UpdateQteTargets()
        {
            if (!targetsVisible || targets == null)
                return;

            Camera cam = Camera.main;
            if (cam == null || qteCanvasRect == null)
                return;

            for (int i = 0; i < targets.Length; i++)
            {
                Vector3 world = decoyHuman != null
                    ? decoyHuman.GetLimbPosition(QteLimbs[i])
                    : FallbackLimbPosition(i);

                Vector3 screen = cam.WorldToScreenPoint(world);
                var widget = targets[i];

                if (screen.z <= 0.1f)
                {
                    widget.group.alpha = 0f;
                    continue;
                }

                widget.group.alpha = 1f;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    qteCanvasRect, screen, null, out Vector2 local);
                widget.root.anchoredPosition = local;

                // The armed target's ring radiates: scales up from the disc
                // and fades out fast, on a loop, until the window resolves
                if (phase == FacePhase.Window && i == correctTargetIndex)
                {
                    float p = (phaseTimer % PulseCycleSeconds) / PulseCycleSeconds;
                    widget.pulse.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, PulseMaxScale, p);
                    widget.pulse.color = new Color(1f, 1f, 1f, (1f - p) * 0.9f);
                }
            }
        }

        /// <summary>Rough limb offsets around the fallback sphere (no humanoid to track).</summary>
        private Vector3 FallbackLimbPosition(int index)
        {
            if (decoyRoot == null)
                return Vector3.zero;
            Vector3 center = decoyRoot.transform.position + Vector3.up * 0.9f;
            float dx = (index == 0 || index == 2) ? -0.45f : 0.45f;
            float dy = index < 2 ? 0.35f : -0.5f;
            return center + new Vector3(dx, dy, 0f);
        }

        private void EnsureQteCanvas()
        {
            if (qteCanvas != null)
                return;

            var canvasGO = new GameObject("FaceAttackTargetCanvas");
            canvasGO.transform.SetParent(transform, false);
            qteCanvas = canvasGO.AddComponent<Canvas>();
            qteCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            qteCanvas.sortingOrder = 402;
            qteCanvasRect = qteCanvas.transform as RectTransform;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGO.AddComponent<GraphicRaycaster>();

            qteGroup = canvasGO.AddComponent<CanvasGroup>();
            qteGroup.alpha = 0f;
            qteGroup.blocksRaycasts = false;
            qteGroup.interactable = false;

            targets = new TargetWidget[QteLimbs.Length];
            for (int i = 0; i < targets.Length; i++)
            {
                var widget = new TargetWidget();

                var rootGO = new GameObject($"Target{i}");
                rootGO.transform.SetParent(canvasGO.transform, false);
                widget.root = rootGO.AddComponent<RectTransform>();
                widget.root.anchorMin = widget.root.anchorMax = new Vector2(0.5f, 0.5f);
                widget.root.sizeDelta = new Vector2(150f, 150f);
                widget.group = rootGO.AddComponent<CanvasGroup>();

                // Invisible full-rect hit area on the button itself, so the
                // tap zone never changes as ring/disc visuals swap
                var hitArea = rootGO.AddComponent<Image>();
                hitArea.color = Color.clear;

                var button = rootGO.AddComponent<Button>();
                button.targetGraphic = hitArea;
                // ColorTint would fight the armed/dimmed visuals
                button.transition = Selectable.Transition.None;
                int index = i;
                button.onClick.AddListener(() => OnTargetTapped(index));

                // Radiating pulse ring (behind the disc; only its expansion
                // beyond the disc edge reads)
                widget.pulse = BuildTargetImage(rootGO.transform, "Pulse", GetRingSprite(),
                    new Color(1f, 1f, 1f, 0f), new Vector2(150f, 150f));

                // Neutral candidate ring
                widget.ring = BuildTargetImage(rootGO.transform, "Ring", GetRingSprite(),
                    targetRingColor, new Vector2(150f, 150f));

                // Solid white armed disc + "TAP"
                widget.disc = BuildTargetImage(rootGO.transform, "Disc", GetCircleSprite(),
                    Color.white, new Vector2(150f, 150f));

                var tapGO = new GameObject("TapLabel");
                tapGO.transform.SetParent(rootGO.transform, false);
                widget.tap = tapGO.AddComponent<TextMeshProUGUI>();
                widget.tap.text = "TAP";
                widget.tap.alignment = TextAlignmentOptions.Center;
                widget.tap.fontSize = 44f;
                widget.tap.color = new Color(0.1f, 0.11f, 0.13f);
                widget.tap.raycastTarget = false;
                var tapFont = ResolveBannerFont();
                if (tapFont != null)
                    widget.tap.font = tapFont;
                else
                    widget.tap.fontStyle = FontStyles.Bold;
                var tapRect = widget.tap.rectTransform;
                tapRect.anchorMin = tapRect.anchorMax = new Vector2(0.5f, 0.5f);
                tapRect.sizeDelta = new Vector2(150f, 150f);
                tapRect.anchoredPosition = Vector2.zero;

                var xGO = new GameObject("X");
                xGO.transform.SetParent(rootGO.transform, false);
                widget.x = xGO.AddComponent<TextMeshProUGUI>();
                widget.x.text = "X";
                widget.x.alignment = TextAlignmentOptions.Center;
                widget.x.fontSize = 132f;
                widget.x.color = xColor;
                widget.x.raycastTarget = false;
                if (markerFont != null)
                    widget.x.font = markerFont;
                var xRect = widget.x.rectTransform;
                xRect.anchorMin = xRect.anchorMax = new Vector2(0.5f, 0.5f);
                xRect.sizeDelta = new Vector2(170f, 170f);
                xRect.anchoredPosition = Vector2.zero;
                xGO.SetActive(false);

                targets[i] = widget;
            }
        }

        private static Image BuildTargetImage(Transform parent, string name, Sprite sprite, Color color, Vector2 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            var rt = image.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = Vector2.zero;
            return image;
        }

        /// <summary>Procedural anti-aliased ring, so no sprite asset wiring is needed.</summary>
        private static Sprite GetRingSprite()
        {
            if (ringSprite != null)
                return ringSprite;

            const int size = 128;
            const float outerRadius = size * 0.5f - 2f;
            const float innerRadius = outerRadius - 13f;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            float center = (size - 1) * 0.5f;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float alpha = Mathf.Clamp01(Mathf.Min(outerRadius - dist + 0.5f, dist - innerRadius + 0.5f));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            ringSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
            return ringSprite;
        }

        /// <summary>Procedural anti-aliased solid disc for the armed TAP target.</summary>
        private static Sprite GetCircleSprite()
        {
            if (circleSprite != null)
                return circleSprite;

            const int size = 128;
            const float radius = size * 0.5f - 2f;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            float center = (size - 1) * 0.5f;
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
            phase = FacePhase.Inactive;

            if (inputLocked)
            {
                playerController?.SetJumpEnabled(true);
                playerController?.SetLaneChangeEnabled(true);
                inputLocked = false;
            }

            if (movementPausedByQte)
            {
                playerController?.ResumeMovement();
                movementPausedByQte = false;
            }

            UnfreezeDog();

            if (scrollOverride >= 0f)
            {
                LevelScroller.Instance?.ClearSpeedOverride();
                scrollOverride = -1f;
                restoreAfterRamp = false;
            }

            // Only put the run camera back if we're still in the run - the
            // home/level-complete flows own their own camera moves (and the
            // game over screen keeps the zoomed freeze-frame; the retry's
            // Playing entry resets it)
            drivingCamera = false;
            cameraTransform = null;
            if (cameraZoomed)
            {
                cameraZoomed = false;
                if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.Playing)
                    CameraStateMachine.Instance?.SetState(CameraStateType.Gameplay);
            }

            // Recenter the dog model if the chase ended mid-steer
            if (pounceModelOffset != Vector3.zero)
            {
                playerController?.Animations?.SetModelOffset(Vector3.zero);
                pounceModelOffset = Vector3.zero;
            }
            pounceMouth = null;

            DestroyDecoy();
            HideQteTargets();
            StopFreezeRiser();

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

            LevelGenerator.Instance?.SetRunnerSpawningSuppressed(false);

            if (wasActive)
                GameLog.Info("[MiniLevelFaceAttack] Cleaned up");
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

            var canvasGO = new GameObject("FaceAttackBannerCanvas");
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
            TMP_FontAsset font = ResolveBannerFont();
            if (font != null)
            {
                bannerText.font = font;
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
        // Helpers
        // ------------------------------------------------------------------

        private static int ClampDifficulty(int index)
        {
            return Mathf.Clamp(index, 0, TapWindowSeconds.Length - 1);
        }

        /// <summary>
        /// The Barlow banner font, borrowing the flee attack's wire (same
        /// GameObject, same font) if our own is missing - e.g. a play session
        /// that raced the setup script. Never silently the TMP default.
        /// </summary>
        private TMP_FontAsset ResolveBannerFont()
        {
            return bannerFont != null
                ? bannerFont
                : GetComponent<MiniLevelFleeAttack>()?.BannerFontAsset;
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

        private Material GetDecoyMaterial()
        {
            if (decoyMaterial == null)
                decoyMaterial = CreateGlowMaterial(decoyColor);
            return decoyMaterial;
        }

        private static Material CreateGlowMaterial(Color color)
        {
            // Unlit reads as self-lit, matching the old URP/Lit + emission
            // look. URP/Lit is stripped from builds; Unlit ships via Always
            // Included Shaders.
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                return CreateLitMaterial(color);
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            return mat;
        }

        private static Material CreateLitMaterial(Color color)
        {
            Shader shader = Shader.Find("Custom/Mobile/ArcEffect");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit"); // editor safety net
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            return mat;
        }
    }
}
