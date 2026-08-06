using UnityEngine;
using UnityEngine.InputSystem;
using RingSport.Core;
using RingSport.Effects;
using RingSport.Level;
using RingSport.UI;
using RingSport.Input;

namespace RingSport.Player
{
    [RequireComponent(typeof(CharacterController))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Movement Settings")]
        [SerializeField] private float forwardSpeed = 10f;
        [SerializeField] private float sprintMultiplier = 1.5f;
        [SerializeField] private float laneDistance = 3f;
        [SerializeField] private float laneChangeSpeed = 10f;

        [Header("Jump Settings")]
        // 1.8 clears the 1.5-tall hurdle colliders VISUALLY (feet above the
        // bar at apex), not just the pivot>=1.5 gameplay check.
        //
        // Height and gravity are a PAIR, and neither one alone is a dial:
        // v0 = sqrt(2*g*h) below, so the apex is h while the air time is
        // 2*v0/g = 2*sqrt(2h/g) - the RATIO. Pick the pair from the two things
        // that are actually tuned, via g = 8h/T^2: h 1.8 puts the apex at 2.95
        // (the root rests at y=1.15) and g 68 gives T = 0.4602s.
        //
        // Air time is the half that levels care about. It sets the jump's
        // horizontal reach (6.90u at L1 up to 9.20u at L8) and how soon the dog
        // is back on the ground for the next gesture; the spacing budgets in
        // ObstacleSpawner are priced in seconds and never read these fields, so
        // a SHORTER air time only ever buys more landing room, never less.
        [SerializeField] private float jumpHeight = 1.8f;
        [SerializeField] private float gravity = -68f;
        [Tooltip("A jump swipe made this long before landing is queued and fires on touchdown, so mid-air swipes aren't silently dropped.")]
        [SerializeField] private float jumpBufferDuration = 0.2f;

        // Root Y with the capsule standing on the floor. The whole game runs on
        // one ground plane, so this is a constant - but it's a MEASURED one:
        // MeasureGroundRestY probes the floor at Awake and HandleGroundCheck
        // overwrites it from every grounded frame after that, so a resized
        // capsule or a re-authored floor can't leave the snap behind. The
        // serialized value is only the fallback for a probe that finds nothing;
        // don't trust it on sight, it is one stray prefab apply from being some
        // height the dog was falling through (the scene authors the player at
        // y=1; the capsule settles at 1.15 once physics has had a frame).
        [Tooltip("Fallback root Y for the ground snap. Measured from the real floor at Awake and from every grounded frame after that.")]
        [SerializeField] private float groundRestY = 1.15f;

        [Header("Sprint Stamina Settings")]
        [SerializeField] private float maxSprintDuration = 5f;
        [SerializeField] private float sprintDrainRate = 1f;
        [SerializeField] private float sprintRefillRate = 1f;

        private CharacterController characterController;
        private PlayerInput playerInput;
        private InputAction moveAction;
        private InputAction jumpAction;
        private InputAction sprintAction;
        private MobileInputHandler mobileInputHandler;
        private PlayerAnimator playerAnimator;
        private PlayerRagdoll playerRagdoll;
        private SprintTrail sprintTrail;

        private Vector3 velocity;
        private bool isGrounded;
        private bool isJumpEnabled = true;
        private bool isLaneChangeEnabled = true;
        private float targetLaneX = 0f;
        private int currentLane = 0; // -1 = left, 0 = center, 1 = right
        private bool isMovementPaused = false;
        private float lastInputTime = -1f;
        private float inputCooldown = 0.2f;
        private float pendingJumpRequestTime = float.NegativeInfinity;
        private bool wasAirborne;
        private float airborneTime;

        // Stamina system for sprint management
        private PlayerStaminaSystem staminaSystem;

        [Header("Audio Settings")]
        [SerializeField] private AudioClip jumpSound;
        [SerializeField] private AudioClip footstepLoop;
        [SerializeField] [Range(0f, 1f)] private float footstepVolume = 0.5f;
        [SerializeField] private float baseFootstepPitch = 1.0f;
        [SerializeField] private float sprintPitchMultiplier = 1.3f;
        [Tooltip("One-shot thump on touchdown after real air time (temporary clip - see SOUND_EFFECTS.md).")]
        [SerializeField] private AudioClip landSound;
        [SerializeField] [Range(0f, 1f)] private float landVolume = 0.5f;
        [Tooltip("Soft swipe whoosh on each lane change - restarts rather than layers, so a fast weave doesn't pile up.")]
        [SerializeField] private AudioClip laneChangeSound;
        [SerializeField] [Range(0f, 1f)] private float laneChangeVolume = 0.35f;
        [Tooltip("One-shot the moment a sprint begins.")]
        [SerializeField] private AudioClip sprintStartSound;
        [SerializeField] [Range(0f, 1f)] private float sprintStartVolume = 0.7f;
        [Tooltip("Looping wind layer under a sprint; fades in with the sprint and rides the world speed.")]
        [SerializeField] private AudioClip sprintWindLoop;
        [SerializeField] [Range(0f, 1f)] private float sprintWindVolume = 0.55f;
        [Tooltip("Looping pant while sprint is locked out - stops as soon as the bar refills and sprint is usable again.")]
        [SerializeField] private AudioClip sprintExhaustedPant;
        [SerializeField] [Range(0f, 1f)] private float sprintPantVolume = 0.8f;

        private AudioSource sfxAudioSource;
        private AudioSource footstepAudioSource;
        private AudioSource whooshAudioSource;  // lane-change swipes, restarted per change
        private AudioSource windAudioSource;    // sprint wind loop
        private AudioSource pantAudioSource;    // exhaustion pant loop

        private const float SprintAudioFadeSeconds = 0.22f;

        // Ceilings on the palisade clamber alignment (see BeginClamber). Z covers
        // the worst realistic scroll step (~0.6m at 30fps on a fast level) with
        // headroom; X only ever tidies up a part-finished lane change.
        private const float MaxClamberAlignZ = 0.9f;
        private const float MaxClamberAlignX = 0.5f;

        // Cached manager references for performance
        private GameManager gameManager;
        private UIManager uiManager;

        public float ForwardSpeed => staminaSystem.IsSprinting ? forwardSpeed * sprintMultiplier : forwardSpeed;
        public bool IsGrounded => isGrounded;
        /// <summary>World position at the bottom of the capsule (dust bursts land here).</summary>
        public Vector3 FeetPosition => transform.position + characterController.center
            - Vector3.up * (characterController.height * 0.5f - 0.05f);
        public PlayerAnimator Animations => playerAnimator;
        /// <summary>Current target lane: -1 left, 0 center, 1 right.</summary>
        public int CurrentLane => currentLane;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();

            // The prefab's 0.001 min move distance made the dog report AIRBORNE
            // while standing still: the pre-run countdown holds timeScale at 0,
            // so the per-frame Move is exactly zero, and a move under this
            // threshold is discarded WITH its collision flags - isGrounded went
            // false for the whole countdown (visible in Editor.log as ten
            // seconds of "isGrounded: False, position.y: 1.15"). Everything that
            // reads IsGrounded believed it: taps banked as buffered jumps
            // instead of firing, footsteps stayed silent, and the finish beat's
            // wait-until-grounded had to be talked out of freezing the dog.
            // Zero keeps the sweep running so a still frame still finds the floor.
            characterController.minMoveDistance = 0f;

            // Take the rest height off the floor that's actually under the dog
            // rather than off the serialized seed. That number lives on the
            // prefab, and a stray "apply overrides" from a live scene wrote a
            // mid-fall height into it once already - the home screen then buried
            // the dog to the shoulders before the run had even started.
            MeasureGroundRestY();

            playerInput = GetComponent<PlayerInput>();
            playerAnimator = GetComponentInChildren<PlayerAnimator>(true);
            playerRagdoll = GetComponentInChildren<PlayerRagdoll>(true);
            sprintTrail = GetComponentInChildren<SprintTrail>(true);

            // Get or add MobileInputHandler
            mobileInputHandler = GetComponent<MobileInputHandler>();
            if (mobileInputHandler == null)
            {
                mobileInputHandler = gameObject.AddComponent<MobileInputHandler>();
                GameLog.Info("MobileInputHandler added to player");
            }

            // Initialize stamina system
            staminaSystem = new PlayerStaminaSystem(maxSprintDuration, sprintDrainRate, sprintRefillRate);

            // Setup audio sources
            sfxAudioSource = gameObject.AddComponent<AudioSource>();
            sfxAudioSource.playOnAwake = false;

            footstepAudioSource = gameObject.AddComponent<AudioSource>();
            footstepAudioSource.playOnAwake = false;
            footstepAudioSource.loop = true;
            footstepAudioSource.clip = footstepLoop;
            footstepAudioSource.volume = footstepVolume;

            // Lane whooshes get their own source so a new swipe cuts the last
            // one off instead of stacking a second whoosh on top of it
            whooshAudioSource = gameObject.AddComponent<AudioSource>();
            whooshAudioSource.playOnAwake = false;

            windAudioSource = CreateSprintLoopSource(sprintWindLoop);
            pantAudioSource = CreateSprintLoopSource(sprintExhaustedPant);

            if (playerInput == null)
            {
                GameLog.Warn("PlayerInput component not found, adding one. Please add PlayerInput manually and assign the InputSystem_Actions asset!");
                playerInput = gameObject.AddComponent<PlayerInput>();
            }

            // Make sure we have the actions asset
            if (playerInput.actions == null)
            {
                GameLog.Error("PlayerInput.actions is null! Please assign InputSystem_Actions asset to PlayerInput component.");
                return;
            }

            // All input flows through the direct action subscriptions in
            // SetupInputActions; the prefab's SendMessages mode also reflected
            // into 'OnJump' and threw MissingMethodException on every jump
            // press (the handler takes a CallbackContext, not an InputValue).
            playerInput.notificationBehavior = PlayerNotifications.InvokeCSharpEvents;

            SetupInputActions();
        }

        /// <summary>Silent looping source - HandleSprintAudio fades it in and out.</summary>
        private AudioSource CreateSprintLoopSource(AudioClip clip)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.clip = clip;
            source.volume = 0f;
            return source;
        }

        private void Start()
        {
            // Cache manager references for performance
            gameManager = GameManager.Instance;
            uiManager = UIManager.Instance;

            // Initialize stamina system with UI manager
            staminaSystem.Initialize(uiManager);
        }

        private void SetupInputActions()
        {
            var actionMap = playerInput.actions.FindActionMap("Player");

            if (actionMap == null)
            {
                GameLog.Error("Player action map not found!");
                return;
            }

            moveAction = actionMap.FindAction("Move");
            jumpAction = actionMap.FindAction("Jump");
            sprintAction = actionMap.FindAction("Sprint");

            if (jumpAction != null)
            {
                jumpAction.performed += OnJump;
                GameLog.Info("Jump action registered successfully");
            }
            else
            {
                GameLog.Error("Jump action not found in Player action map!");
            }

            if (sprintAction != null)
            {
                sprintAction.performed += OnSprintStarted;
                sprintAction.canceled += OnSprintCanceled;
            }
        }

        private void OnEnable()
        {
            playerInput?.ActivateInput();

            // Subscribe to mobile input events
            if (mobileInputHandler != null)
            {
                mobileInputHandler.OnJumpTriggered += OnMobileJump;
                mobileInputHandler.OnSprintStarted += OnMobileSprint;
                mobileInputHandler.OnSprintEnded += OnMobileSprintEnded;
            }
        }

        private void OnDisable()
        {
            playerInput?.DeactivateInput();

            if (jumpAction != null)
            {
                jumpAction.performed -= OnJump;
            }

            if (sprintAction != null)
            {
                sprintAction.performed -= OnSprintStarted;
                sprintAction.canceled -= OnSprintCanceled;
            }

            // Unsubscribe from mobile input events
            if (mobileInputHandler != null)
            {
                mobileInputHandler.OnJumpTriggered -= OnMobileJump;
                mobileInputHandler.OnSprintStarted -= OnMobileSprint;
                mobileInputHandler.OnSprintEnded -= OnMobileSprintEnded;
            }
        }

        private void Update()
        {
            // The tab-away pause freezes the run mid-pose: no gait or footstep
            // updates, and a swipe aimed at CONTINUE must not bank a lane change
            if (PauseScreen.IsPaused)
                return;

            // Handle footsteps regardless of game state (so it can stop when not playing)
            HandleFootsteps();
            HandleSprintAudio();

            // Drive animation in every state (idle at home, death pose on game over, etc.)
            UpdateAnimation();

            // Allow movement during Playing and MiniLevel states
            bool isValidState = gameManager?.CurrentState == GameState.Playing ||
                               gameManager?.CurrentState == GameState.MiniLevel;

            // Every other state (Home, GameOver, LevelComplete) runs no movement
            // step at all, so the dog has to be put back on the floor explicitly
            // - see SettleToGround. Deliberately ahead of the isMovementPaused
            // check: a run death pauses movement and never unpauses it, which is
            // exactly the case that used to strand the dog in the air.
            if (!isValidState)
            {
                SettleToGround();
                return;
            }

            // Movement paused DURING gameplay is a deliberate hold (the face
            // attack parks the dog mid-pounce for its bullet-time limb pick) -
            // nothing to settle, the pose is the point
            if (isMovementPaused)
                return;

            // Use unscaled delta time during mini-levels (TimeScale may be 0)
            float deltaTime = gameManager?.CurrentState == GameState.MiniLevel
                ? Time.unscaledDeltaTime
                : Time.deltaTime;

            HandleGroundCheck();

            // Landing feedback (dust, squash, thump, camera dip) after real air
            // time - purely audiovisual, never gates input (fairness model)
            if (isGrounded)
            {
                if (wasAirborne && airborneTime >= 0.15f)
                    OnLanded();
                wasAirborne = false;
                airborneTime = 0f;
            }
            else
            {
                wasAirborne = true;
                airborneTime += deltaTime;
            }

            // Fire a buffered jump the moment we're grounded again - a swipe in
            // the last part of a jump would otherwise be silently dropped
            if (isGrounded && Time.unscaledTime - pendingJumpRequestTime <= jumpBufferDuration)
                DoJump();

            HandleLaneMovement(deltaTime);
            HandleGravity(deltaTime);
            staminaSystem.Update(deltaTime); // Delegate to stamina system

            // Only move in X (lanes) and Y (jump/gravity), not Z
            Vector3 movement = new Vector3(velocity.x, velocity.y, 0f);
            characterController.Move(movement * deltaTime);

            // Reset X velocity after moving
            velocity.x = 0f;
        }

        private void HandleGroundCheck()
        {
            isGrounded = characterController.isGrounded;

            if (isGrounded)
            {
                // Re-measure the rest height from wherever the capsule actually
                // came to rest, so SnapToGround stays honest without anyone
                // having to keep a hand-tuned number in sync with the collider
                groundRestY = transform.position.y;

                if (velocity.y < 0)
                    velocity.y = -2f;
            }

            // Debug ground state occasionally
            if (Time.frameCount % 60 == 0)
            {
                GameLog.Info($"Ground check - isGrounded: {isGrounded}, position.y: {transform.position.y}, velocity.y: {velocity.y}");
            }
        }

        private void HandleLaneMovement(float deltaTime)
        {
            // Read input from keyboard/gamepad
            Vector2 moveInput = moveAction.ReadValue<Vector2>();

            // Touch takes priority when it has input
            if (mobileInputHandler != null)
            {
                Vector2 mobileMove = mobileInputHandler.MoveInput;
                if (mobileMove != Vector2.zero)
                {
                    moveInput = mobileMove;
                }
            }

            // Discrete lane switching with cooldown (scripted sequences like
            // the flee attack catch can lock switching; the lerp toward the
            // current target lane keeps running so the player still settles)
            if (isLaneChangeEnabled && Time.unscaledTime - lastInputTime > inputCooldown)
            {
                if (moveInput.x > 0.5f && currentLane < 1)
                {
                    currentLane++;
                    targetLaneX = currentLane * laneDistance;
                    lastInputTime = Time.unscaledTime;
                    NotifyLaneChange(toRight: true);
                }
                else if (moveInput.x < -0.5f && currentLane > -1)
                {
                    currentLane--;
                    targetLaneX = currentLane * laneDistance;
                    lastInputTime = Time.unscaledTime;
                    NotifyLaneChange(toRight: false);
                }
            }

            // Smooth lane transition
            float currentX = transform.position.x;
            float newX = Mathf.Lerp(currentX, targetLaneX, laneChangeSpeed * deltaTime);

            // Prevent division by zero/very small deltaTime which can cause NaN
            if (deltaTime > 0.0001f)
            {
                velocity.x = (newX - currentX) / deltaTime;
            }
            else
            {
                velocity.x = 0f;
            }
        }

        private void HandleGravity(float deltaTime)
        {
            velocity.y += gravity * deltaTime;
        }

        /// <summary>
        /// Puts the dog back on the floor in the states that run no movement
        /// step: Home, GameOver and LevelComplete.
        ///
        /// Nothing else touches the capsule there. A death or a finish taken
        /// mid-jump therefore parked the dog at whatever Y it was passing
        /// through - it held that height behind the game over panel, ResetPosition
        /// preserved it (it only ever rewrote X), and the dog then hung in the
        /// air on the home screen, or through the entire pre-run countdown on a
        /// retry, before dropping the moment timeScale went back to 1.
        ///
        /// Unscaled, because LevelComplete and the countdown both sit at
        /// timeScale 0. The controller's own collision is what stops the drop;
        /// the remembered rest height is only the backstop under it, because the
        /// home screen clears the floor pool before it respawns the tiles and a
        /// frame of gravity landing in that gap would drop the dog through the
        /// world instead.
        /// </summary>
        private void SettleToGround()
        {
            // Clamped: the first frame back from a tab-away or a long load
            // carries a huge unscaled delta, and an unclamped gravity step there
            // would fling the capsule through the floor in one move
            float deltaTime = Mathf.Min(Time.unscaledDeltaTime, 0.05f);

            velocity.y += gravity * deltaTime;
            characterController.Move(Vector3.up * (velocity.y * deltaTime));

            // Landed on real geometry - take the measurement. Letting the
            // controller's own collision call it keeps this honest on whatever
            // floor is actually there, instead of driving the dog to a
            // remembered height that may not match this level (or, when the seed
            // has been clobbered, may be underground).
            if (characterController.isGrounded)
            {
                groundRestY = transform.position.y;
                SnapToGround();
                return;
            }

            // Nothing underneath. Above the last known rest height the fall
            // reads as a real drop, so keep the arc; at it, park. The home
            // screen clears the floor pool before it respawns the tiles, and a
            // frame of gravity landing in that gap would drop the dog through
            // the world.
            if (transform.position.y <= groundRestY)
                SnapToGround();
        }

        /// <summary>
        /// Seeds <see cref="groundRestY"/> from the floor beneath the dog, so
        /// the first snap is right before any run has had a chance to measure
        /// one. Leaves the serialized value alone if the probe finds nothing.
        /// </summary>
        private void MeasureGroundRestY()
        {
            // Start above the capsule so the ray can't begin inside the floor,
            // and ignore the dog's own colliders on the way down
            Vector3 origin = transform.position + Vector3.up * (characterController.height * 0.5f + 0.5f);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 60f, ~0, QueryTriggerInteraction.Ignore);

            float floorY = float.NegativeInfinity;
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(transform))
                    continue;

                // Highest surface at or below the root: an obstacle the dog is
                // standing next to must not pass for the floor
                if (hit.point.y <= transform.position.y && hit.point.y > floorY)
                    floorY = hit.point.y;
            }

            if (float.IsNegativeInfinity(floorY))
                return;

            groundRestY = floorY + characterController.height * 0.5f
                - characterController.center.y + characterController.skinWidth;
        }

        /// <summary>Parks the capsule exactly on the floor plane and kills any fall.</summary>
        private void SnapToGround()
        {
            velocity.y = 0f;

            // Nothing calls Move in these states, so the controller's own flag
            // would stay stuck at whatever the last gameplay frame left behind;
            // say plainly that the dog is down. HandleGroundCheck takes the
            // reading back over on the first gameplay frame.
            isGrounded = true;

            if (Mathf.Abs(transform.position.y - groundRestY) < 0.001f)
                return;

            Teleport(new Vector3(transform.position.x, groundRestY, transform.position.z));
        }

        /// <summary>
        /// Moves the capsule without sweeping. The CharacterController keeps its
        /// own copy of the position, so the component has to be off for a direct
        /// transform write to survive the next Move.
        /// </summary>
        private void Teleport(Vector3 position)
        {
            characterController.enabled = false;
            transform.position = position;
            characterController.enabled = true;
        }

        private void NotifyLaneChange(bool toRight)
        {
            // Mini-levels (dodging steaks) play a discrete sideways dodge; the
            // normal run gets a quick roll-bank pulse on top of the strafe lean.
            if (gameManager?.CurrentState == GameState.MiniLevel)
                playerAnimator?.TriggerDodge(toRight);
            else
                playerAnimator?.PulseLaneBank(toRight);

            // Whoosh pans with the swipe direction and alternates pitch, so a
            // left-right weave doesn't sound like the same sample twice
            if (laneChangeSound != null && whooshAudioSource != null)
            {
                whooshAudioSource.clip = laneChangeSound;
                whooshAudioSource.volume = laneChangeVolume;
                whooshAudioSource.panStereo = toRight ? 0.35f : -0.35f;
                whooshAudioSource.pitch = toRight ? 1.05f : 0.95f;
                whooshAudioSource.Play();
            }
        }

        private void UpdateAnimation()
        {
            if (playerAnimator == null)
                return;

            // The animated model is hidden while the death ragdoll owns the body
            if (playerRagdoll != null && playerRagdoll.IsActive)
                return;

            // Time.timeScale is 0 during the pre-run countdown; don't run in place
            // while the world is frozen.
            bool isRunning = gameManager?.CurrentState == GameState.Playing &&
                             !isMovementPaused &&
                             Time.timeScale > 0f;

            // Signed -1..1 lean while chasing the target lane, matching the lerp
            // in HandleLaneMovement: full deflection on a lane switch, easing out
            // as the player converges on the lane.
            float strafe = laneDistance > 0f
                ? Mathf.Clamp((targetLaneX - transform.position.x) / laneDistance, -1f, 1f)
                : 0f;

            // Sprint is a distinct gait tier in the blend tree, not a faster run.
            // Mini-levels stay at idle; lane changes there play the dodge states
            // (see NotifyLaneChange) instead of the locomotion blend.
            float moveSpeed = isRunning ? (staminaSystem.IsSprinting ? 2f : 1f) : 0f;

            // Speed-line trail follows the sprint gait tier exactly
            sprintTrail?.SetSprinting(moveSpeed > 1.5f);

            // Scale the cycle with level speed so feet stay in sync with the ground
            float levelSpeedMultiplier = LevelGenerator.Instance?.GetCurrentConfig()?.SpeedMultiplier ?? 1f;

            playerAnimator.UpdateLocomotion(
                moveSpeed,
                isRunning ? strafe : 0f,
                isRunning ? levelSpeedMultiplier : 1f,
                Time.unscaledDeltaTime);
            playerAnimator.SetGrounded(isGrounded);
            playerAnimator.SetVerticalVelocity(velocity.y);
        }

        private void HandleFootsteps()
        {
            if (footstepAudioSource == null || footstepLoop == null)
                return;

            // Determine if footsteps should play
            bool shouldPlay = gameManager?.CurrentState == GameState.Playing &&
                              !isMovementPaused &&
                              isGrounded;

            if (shouldPlay)
            {
                // Calculate pitch based on level speed multiplier and sprint state
                float levelSpeedMultiplier = LevelGenerator.Instance?.GetCurrentConfig()?.SpeedMultiplier ?? 1f;
                float sprintMultiplier = staminaSystem.IsSprinting ? sprintPitchMultiplier : 1f;
                float targetPitch = baseFootstepPitch * levelSpeedMultiplier * sprintMultiplier;

                footstepAudioSource.pitch = targetPitch;

                // Start playing if not already
                if (!footstepAudioSource.isPlaying)
                    footstepAudioSource.Play();
            }
            else
            {
                // Stop playing if currently playing
                if (footstepAudioSource.isPlaying)
                    footstepAudioSource.Stop();
            }
        }

        /// <summary>
        /// The two sprint loops, both crossfaded rather than hard-cut:
        /// - wind rides the sprint, its level scaled by how fast the world is
        ///   actually moving (so a fast level's sprint is windier than a slow
        ///   one's);
        /// - the pant runs from the moment stamina empties until the bar has
        ///   refilled enough that sprint is usable again - it's the audible
        ///   half of the lockout, so it must not outlive it.
        /// Runs in every state, so a death or pause silences both.
        /// </summary>
        private void HandleSprintAudio()
        {
            float dt = Time.unscaledDeltaTime;
            bool active = gameManager?.CurrentState == GameState.Playing && !isMovementPaused;

            if (windAudioSource != null && sprintWindLoop != null)
            {
                float target = 0f;
                if (active && staminaSystem.IsSprinting)
                {
                    float scrollSpeed = LevelScroller.Instance != null ? LevelScroller.Instance.GetScrollSpeed() : ForwardSpeed;
                    float speed01 = Mathf.Clamp01(Mathf.InverseLerp(forwardSpeed, forwardSpeed * sprintMultiplier * 1.5f, scrollSpeed));
                    target = sprintWindVolume * Mathf.Lerp(0.6f, 1f, speed01);
                    windAudioSource.pitch = Mathf.Lerp(0.95f, 1.1f, speed01);
                }
                FadeLoop(windAudioSource, target, dt);
            }

            if (pantAudioSource != null && sprintExhaustedPant != null)
            {
                bool panting = active && staminaSystem.IsSprintExhausted;
                FadeLoop(pantAudioSource, panting ? sprintPantVolume : 0f, dt);
            }
        }

        /// <summary>Ramps a looping source toward a volume, starting/stopping it at the ends.</summary>
        private static void FadeLoop(AudioSource source, float targetVolume, float dt)
        {
            source.volume = Mathf.MoveTowards(source.volume, targetVolume, dt / SprintAudioFadeSeconds);

            if (source.volume > 0.001f)
            {
                if (!source.isPlaying)
                    source.Play();
            }
            else if (source.isPlaying)
            {
                source.Stop();
            }
        }

        private void OnJump(InputAction.CallbackContext context)
        {
            TryJump();
        }

        private void OnMobileJump()
        {
            TryJump();
        }

        private void TryJump()
        {
            if (!isJumpEnabled || PauseScreen.IsPaused)
                return;

            if (!isGrounded)
            {
                // Buffer the request; Update fires it on landing if recent enough
                pendingJumpRequestTime = Time.unscaledTime;
                return;
            }

            DoJump();
        }

        private void DoJump()
        {
            pendingJumpRequestTime = float.NegativeInfinity;
            isGrounded = false;

            velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
            GameLog.Info($"Jumping! New velocity.y: {velocity.y}");

            playerAnimator?.TriggerJump();

            // Play jump sound
            if (jumpSound != null && sfxAudioSource != null)
                sfxAudioSource.PlayOneShot(jumpSound);

            // Takeoff garnish: slight vertical stretch + a small dust kick
            // behind the paws (negative squash = stretch). Visual only.
            playerAnimator?.PulseSquash(-0.09f, 0.22f);
            ImpactVFX.PlayDust(FeetPosition, 5, 0.7f);
        }

        private void OnLanded()
        {
            playerAnimator?.PulseSquash(0.12f, 0.16f);
            ImpactVFX.PlayDust(FeetPosition, 9);

            if (landSound != null && sfxAudioSource != null)
                sfxAudioSource.PlayOneShot(landSound, landVolume);

            CameraStateMachine.Instance?.AddKick(Vector3.down * 0.07f, 0.22f);
        }

        private void OnMobileSprint()
        {
            GameLog.Info("Mobile sprint started!");
            TryStartSprint();
        }

        /// <summary>
        /// Starts a sprint if stamina allows, with the kick-off one-shot. The
        /// already-sprinting check keeps a held input (or a touch re-press)
        /// from re-triggering the sound mid-sprint.
        /// </summary>
        private void TryStartSprint()
        {
            if (staminaSystem.IsSprinting || !staminaSystem.CanSprint())
                return;

            staminaSystem.IsSprinting = true;

            bool audible = gameManager?.CurrentState == GameState.Playing && !isMovementPaused;
            if (audible && sprintStartSound != null && sfxAudioSource != null)
                sfxAudioSource.PlayOneShot(sprintStartSound, sprintStartVolume);
        }

        private void OnMobileSprintEnded()
        {
            GameLog.Info("Mobile sprint ended!");

            // Delegate to stamina system
            staminaSystem.IsSprinting = false;
        }

        private void OnSprintStarted(InputAction.CallbackContext context)
        {
            TryStartSprint();
        }

        private void OnSprintCanceled(InputAction.CallbackContext context)
        {
            // Delegate to stamina system
            staminaSystem.IsSprinting = false;
        }

        public void Stop()
        {
        }

        public void Resume()
        {
        }

        /// <summary>Mini-levels can disable jumping (e.g. Food Refusal is dodge-only).</summary>
        public void SetJumpEnabled(bool enabled)
        {
            isJumpEnabled = enabled;

            // A swipe buffered just before disabling would otherwise still
            // fire on landing (the buffer check bypasses isJumpEnabled)
            if (!enabled)
                pendingJumpRequestTime = float.NegativeInfinity;
        }

        /// <summary>
        /// Scripted jump used by the flee attack's catch pounce - fires even
        /// while manual jumping is disabled. No-op when airborne.
        /// </summary>
        public void ForceJump()
        {
            if (isGrounded)
                DoJump();
        }

        /// <summary>
        /// Locks/unlocks discrete lane switching (the flee attack catch
        /// sequence owns the dog's lane once the walls are passed).
        /// </summary>
        public void SetLaneChangeEnabled(bool enabled)
        {
            isLaneChangeEnabled = enabled;
        }

        public void ResetPosition()
        {
            currentLane = 0;
            targetLaneX = 0f;
            velocity = Vector3.zero;
            isJumpEnabled = true;
            isLaneChangeEnabled = true;
            pendingJumpRequestTime = float.NegativeInfinity;
            wasAirborne = false;
            airborneTime = 0f;

            // Centre lane, standing on the floor. Y matters as much as X here:
            // this used to carry transform.position.y through untouched, which
            // meant a reset was only ever as grounded as the moment that
            // preceded it - a mid-jump death or finish handed its airborne Y
            // straight on to the home screen and to the next run's countdown.
            Teleport(new Vector3(0f, groundRestY, transform.position.z));

            // Reset stamina system
            staminaSystem.Reset();

            // Clear death/clamber pose from the previous attempt
            playerRagdoll?.Clear();
            playerAnimator?.ResetToLocomotion();
        }

        /// <summary>Called by GameManager when the player fails a level.</summary>
        public void PlayDeathAnimation()
        {
            if (playerRagdoll != null && playerRagdoll.HasRagdoll)
            {
                playerRagdoll.ActivateRagdoll();
            }
            else
            {
                playerAnimator?.TriggerDeath();
            }
        }

        public void PauseMovement()
        {
            isMovementPaused = true;

            // Force stop sprinting and unsubscribe from sprint events
            staminaSystem.IsSprinting = false;
            if (sprintAction != null)
            {
                sprintAction.performed -= OnSprintStarted;
                sprintAction.canceled -= OnSprintCanceled;
            }

            // Stop footsteps immediately
            if (footstepAudioSource != null && footstepAudioSource.isPlaying)
                footstepAudioSource.Stop();
        }

        public void ResumeMovement()
        {
            isMovementPaused = false;

            // Resubscribe to sprint events
            if (sprintAction != null)
            {
                sprintAction.performed -= OnSprintStarted; // Remove first to avoid duplicates
                sprintAction.canceled -= OnSprintCanceled;
                sprintAction.performed += OnSprintStarted;
                sprintAction.canceled += OnSprintCanceled;
            }
        }

        /// <summary>
        /// Starts the palisade clamber, aligning the grab pose to where the wall
        /// actually came to rest.
        ///
        /// The dog never moves in Z - the world scrolls past it in whole frames,
        /// so OnTriggerEnter fires anywhere inside the last step of travel
        /// (~0.17m at 60fps, up to ~0.6m at 30fps) and the minigame then freezes
        /// the scroll right there. The clamber pose offset alone assumes the wall
        /// stopped at the ideal contact point, so any overshoot showed up as the
        /// dog hanging in front of the palisade or buried inside it, differently
        /// every run. Feeding the overshoot back into the pose offset cancels it.
        ///
        /// contactPoint: x = the wall's lane, z = the face the dog ran into.
        /// </summary>
        public void BeginClamber(Vector3 contactPoint)
        {
            // Where the face would sit had the trigger fired the instant it
            // touched the capsule - the tight frame the pose was tuned against
            float idealFaceZ = transform.position.z + characterController.radius;

            // Clamped so a frame hitch (or a palisade reworked to a different
            // collider) can only ever nudge the dog, never teleport it
            Vector3 alignment = new Vector3(
                Mathf.Clamp(contactPoint.x - transform.position.x, -MaxClamberAlignX, MaxClamberAlignX),
                0f,
                Mathf.Clamp(contactPoint.z - idealFaceZ, -MaxClamberAlignZ, MaxClamberAlignZ));

            GameLog.Info($"Clamber alignment: wall face {contactPoint.z:F2}, ideal {idealFaceZ:F2}, correction {alignment}");

            playerAnimator?.SetClamberAlignment(alignment);
            playerAnimator?.SetClambering(true);
        }

        /// <summary>
        /// The vault out of a finished clamber. Only the contact point's Y (the
        /// wall's base) matters here - the arc stays in one lane, and the world
        /// scroll is what carries the palisade out from under the dog.
        /// </summary>
        public System.Collections.IEnumerator AnimateOverObstacle(Vector3 obstacleContactPoint, float obstacleHeight)
        {
            float duration = 0.2f;
            float elapsed = 0f;

            Vector3 startPosition = transform.position;

            // Clamber is done - leap over the top of the palisade
            playerAnimator?.SetClambering(false);
            playerAnimator?.TriggerVault();

            // How far above the wall the PIVOT peaks. Note the start height
            // cancels out of the arc below - the apex is always obstacleTop +
            // clearanceHeight, wherever up the wall the dog finished clambering.
            //
            // Well under a body height (the pivot rides 1m above the paws)
            // because the root arc isn't doing this alone - the vault clip
            // supplies its own lift on top. Clearing the top by a full body
            // height read ~50% too high.
            //
            // The palisade collider used to overshoot the visual by 0.225m and
            // this was 0.35 to absorb it; the collider now ends at the visual
            // top, so the same apex is spelled out honestly.
            float clearanceHeight = 0.575f;
            float obstacleTop = obstacleContactPoint.y + obstacleHeight;
            float arcHeight = (obstacleTop - startPosition.y) + clearanceHeight;

            // Ensure arc height is positive (in case player is already above obstacle)
            arcHeight = Mathf.Max(arcHeight, 0.5f);

            // Target is slightly past the obstacle
            Vector3 targetPosition = new Vector3(
                startPosition.x, // Keep current lane
                startPosition.y, // Return to original height
                startPosition.z  // Stay at same Z (world scrolls, not player)
            );

            GameLog.Info($"Animating over obstacle - Start Y: {startPosition.y}, Obstacle Top: {obstacleTop}, Arc Height: {arcHeight}");

            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime; // Use unscaled time for consistency
                float t = elapsed / duration;

                // Parabolic arc: y = -4 * height * t * (t - 1)
                float arcProgress = -4f * arcHeight * t * (t - 1f);

                // Interpolate position with arc
                Vector3 newPosition = Vector3.Lerp(startPosition, targetPosition, t);
                newPosition.y += arcProgress;

                transform.position = newPosition;

                yield return null;
            }

            // Ensure we end at exact target position
            transform.position = targetPosition;
        }
    }
}
