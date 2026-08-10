using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RingSport.Effects;
using RingSport.Level;
using RingSport.Core;
using RingSport.Player;
using System.Collections;
using System.Collections.Generic;

namespace RingSport.UI
{
    /// <summary>
    /// Food Refusal mini level gameplay.
    /// Player dodges 20 falling steaks while optionally collecting 3 mega collectibles.
    /// </summary>
    public class MiniLevelFoodRefusal : MiniLevelBase
    {
        public override MiniLevelType MiniLevelType => MiniLevelType.FoodRefusal;

        [Header("Game Settings")]
        [Tooltip("Drops in a run - beats, not steaks, since a beat can drop two. Trimmed from 20 when the gap between them opened up - 16 keeps the mini level around 25 seconds.")]
        [SerializeField] private int totalSteaks = 16;
        [Tooltip("Seconds between drops. At the fall speed this is also their spacing in the air - 1.35s leaves about 8 units between one steak and the next.")]
        [SerializeField] private float steakSpawnInterval = 1.35f;
        [Tooltip("Chance a beat drops two steaks at once, leaving a single safe lane to be standing in. 0 = only ever one steak.")]
        [Range(0f, 1f)]
        [SerializeField] private float doubleDropChance = 1f / 3f;
        [SerializeField] private float fallSpeed = 6f;
        [SerializeField] private float spawnHeight = 15f;

        [Header("Pool Settings")]
        [SerializeField] private string steakPoolTag = "FoodRefusalSteak";
        [SerializeField] private string collectiblePoolTag = "FoodRefusalCollectible";

        [Header("Lane Settings")]
        [SerializeField] private float laneDistance = 3f; // -3, 0, +3

        // Camera framing. Code-owned on purpose (not serialized): the first
        // pass at these was baked into the scene within a session, silently
        // pinning the shot against further code tuning - same story as
        // CameraStateMachine's home lens constants.
        //
        // The shot is dead level, aligned with the lane axis: no pitch, no
        // yaw, no lens shift. Steaks therefore fall in perfectly vertical
        // screen lines, and the lanes are symmetric about frame centre.
        //
        // Scale on the mini-level camera's distance from its rig - ~6.3m in
        // front of the dog.
        private const float CameraDistanceScale = 1.05f;
        // Lowers the camera after scaling, to just above the dog's eye line
        // (~1.75m). With the shot level, this height is also what places the
        // dog in the frame: the horizon sits at centre and the dog hangs
        // below it, around the bottom third, with the drop above in view.
        private const float CameraHeightOffset = -3.5f;

        [Header("Collectible Settings")]
        [SerializeField] private int megaCollectibleCount = 3;
        [SerializeField] private int megaCollectiblePoints = 100;

        [Header("UI References")]
        [SerializeField] private GameObject gamePanel;
        [SerializeField] private TextMeshProUGUI miniLevelScoreText;

        [Header("Audio")]
        [SerializeField] private AudioClip collectSound;
        [Tooltip("Wet splat when a steak lands on the dog.")]
        [SerializeField] private AudioClip splatSound;

        // Runtime state
        private Coroutine gameCoroutine;
        private List<GameObject> activeObjects = new List<GameObject>();
        private int steaksSpawned = 0;
        private bool isGameRunning = false;
        private PlayerController playerController;
        private float playerZPosition;

        // Collectible spawn indices (at steaks 5, 10, 15)
        private readonly int[] collectibleSpawnIndices = { 4, 9, 14 };

        // Lane picking. NoLane stands for "nothing to avoid" - it has to be a
        // value no real lane can take.
        private const int NoLane = int.MinValue;
        private static readonly int[] Lanes = { -1, 0, 1 };
        private static readonly int[] OuterLanes = { -1, 1 };
        private readonly List<int> laneOptions = new List<int>(3);

        // Lanes as a bitmask, used to carry where the player can possibly be
        // from one beat to the next
        private const int AllLanesMask = 0b111;
        private static int LaneBit(int lane) => 1 << (lane + 1);

        /// <summary>
        /// Called when user clicks start, before countdown begins.
        /// Sets up camera for the mini-level.
        /// </summary>
        public override void OnPrepareGame()
        {
            GameLog.Info("[MiniLevelFoodRefusal] Preparing game - setting camera to MiniLevel state (near eye line, slightly angled down)");
            playerController = Object.FindAnyObjectByType<PlayerController>();

            // Dead level, aligned with the lane axis. The look-at point sits
            // at the camera's own settled height on the LANE MIDLINE (world
            // x = 0, the centre of the steak lanes): same height = zero
            // pitch, midline = zero yaw. Aiming at the dog instead used to
            // tilt the shot down at it and let any lateral hair on its
            // position yaw the frame sideways, clipping the right lane.
            var cameraState = CameraStateMachine.Instance;
            if (cameraState != null)
            {
                Vector3 settled = cameraState.GetStateWorldPosition(
                    CameraStateType.MiniLevel, CameraDistanceScale, CameraHeightOffset);
                Vector3? focus = playerController != null
                    ? new Vector3(0f, settled.y, playerController.transform.position.z)
                    : (Vector3?)null;
                cameraState.SetState(CameraStateType.MiniLevel, CameraDistanceScale, CameraHeightOffset, focus);
            }

            // Dog turns around to face the straight-on mini-level camera
            playerController?.Animations?.SetFacing(true);

            // Get the steaks' blob shadows onto the GPU during the camera move
            // and countdown, so the first one to fall is already textured
            // instead of popping in a frame after it spawns.
            if (playerController != null)
            {
                Vector3 feet = playerController.transform.position;
                BlobShadow.Warmup(new Vector3(feet.x, 0.02f, feet.z));
            }
        }

        public override void StartGame()
        {
            GameLog.Info("[MiniLevelFoodRefusal] Starting game...");

            // Reset state
            steaksSpawned = 0;
            isGameRunning = true;
            activeObjects.Clear();

            // Enable physics by setting timeScale to 1 (required for FixedUpdate and trigger detection)
            Time.timeScale = 1f;

            // Start mini-level score tracking
            ScoreManager.Instance?.StartMiniLevelScoring();

            // Get player reference and position
            playerController = Object.FindAnyObjectByType<PlayerController>();
            if (playerController != null)
            {
                playerZPosition = playerController.transform.position.z;
                playerController.ResumeMovement();
                // Dodge-only mini game - no jumping over the steaks
                playerController.SetJumpEnabled(false);
                GameLog.Info($"[MiniLevelFoodRefusal] Player found at Z={playerZPosition}");
            }
            else
            {
                playerZPosition = 0f;
                GameLog.Warn("[MiniLevelFoodRefusal] PlayerController not found!");
            }

            // Show UI
            ShowPanel();
            UpdateUI();

            LogCameraDiagnostic();

            // Start spawning
            gameCoroutine = StartCoroutine(RunGame());
        }

        /// <summary>
        /// TEMPORARY framing probe while the mini-level shot is being tuned:
        /// the settled camera's world pose and where the three lanes land on
        /// screen. Runs at StartGame - the 3s countdown outlasts the 0.5s
        /// camera transition, so the shot has settled by now. If the numbers
        /// here are symmetric while the image is not, the skew is in the
        /// world, not the camera.
        /// </summary>
        private void LogCameraDiagnostic()
        {
            var cameraState = CameraStateMachine.Instance;
            Camera cam = cameraState != null ? cameraState.GetComponent<Camera>() : Camera.main;
            if (cam == null || playerController == null)
                return;

            Transform rig = cam.transform.parent;
            float z = playerController.transform.position.z;
            Vector3 left = cam.WorldToScreenPoint(new Vector3(-laneDistance, 0.5f, z));
            Vector3 mid = cam.WorldToScreenPoint(new Vector3(0f, 0.5f, z));
            Vector3 right = cam.WorldToScreenPoint(new Vector3(laneDistance, 0.5f, z));
            Vector3 far = cam.WorldToScreenPoint(new Vector3(0f, cam.transform.position.y, z + 60f));

            GameLog.Info(
                $"[FoodRefusalCam] cam pos {cam.transform.position:F3} euler {cam.transform.eulerAngles:F2} fov {cam.fieldOfView:F1} view {cam.pixelWidth}x{cam.pixelHeight} | " +
                $"rig '{(rig != null ? rig.name : "none")}' pos {(rig != null ? rig.position : Vector3.zero):F3} euler {(rig != null ? rig.eulerAngles : Vector3.zero):F2} | " +
                $"player {playerController.transform.position:F3} | " +
                $"screenX left={left.x:F0} mid={mid.x:F0} right={right.x:F0} far={far.x:F0} centre={cam.pixelWidth * 0.5f:F0}");
        }

        public override void StopGame()
        {
            GameLog.Info("[MiniLevelFoodRefusal] Stopping game...");

            isGameRunning = false;

            // Reinstate jumping now that the dodge-only game is over. The dog's
            // camera-facing is NOT reset here - on failure it stays toward the
            // camera for the retry; success turns it back explicitly.
            playerController?.SetJumpEnabled(true);

            // Reset timeScale back to 0 for mini-level state
            Time.timeScale = 0f;

            if (gameCoroutine != null)
            {
                StopCoroutine(gameCoroutine);
                gameCoroutine = null;
            }

            // Clean up all active falling objects
            foreach (var obj in activeObjects)
            {
                if (obj != null)
                {
                    // Re-enable the original components before returning to pool
                    var obstacle = obj.GetComponent<Obstacle>();
                    if (obstacle != null) obstacle.enabled = true;

                    var collectible = obj.GetComponent<Collectible>();
                    if (collectible != null) collectible.enabled = true;

                    ObjectPooler.Instance?.ReturnToPool(obj);
                }
            }
            activeObjects.Clear();

            HidePanel();
        }

        private IEnumerator RunGame()
        {
            int collectiblesSpawned = 0;

            // Reused across iterations (WaitForSecondsRealtime re-arms on
            // yield) instead of allocating one per steak
            var spawnWait = new WaitForSecondsRealtime(steakSpawnInterval);

            // Lane of the steak one beat back, and a lane the next steak has to
            // leave alone because a collectible is coming down in it
            int lastSteakLane = NoLane;
            int reservedLane = NoLane;

            // Lanes the player can be standing in when the next beat drops. The
            // countdown leaves the dog in the middle; after that it is whatever
            // the last beat didn't fill with steak.
            int playerLanes = LaneBit(0);

            for (int i = 0; i < totalSteaks && isGameRunning; i++)
            {
                steaksSpawned = i + 1;

                bool collectibleDue = collectiblesSpawned < megaCollectibleCount &&
                                      System.Array.IndexOf(collectibleSpawnIndices, i) >= 0;

                // A double fills two of the three lanes, so it can't share a beat
                // with a collectible, and it can't run while one is still falling
                // (reservedLane) - either way there'd be no lane left to catch it
                // in. Never on the opening beat: the player has had no drop yet to
                // read the timing off.
                bool doubleAllowed = i > 0 && !collectibleDue && reservedLane == NoLane;
                int safeLane = doubleAllowed && Random.value < doubleDropChance
                    ? GetDoubleSafeLane(playerLanes)
                    : NoLane;

                int steakLanes;
                int steakLane = NoLane;

                if (safeLane != NoLane)
                {
                    foreach (int lane in Lanes)
                    {
                        if (lane != safeLane)
                            SpawnSteak(lane);
                    }
                    steakLanes = AllLanesMask & ~LaneBit(safeLane);

                    // The next single is picked with no memory of a double: it has
                    // a lane each way to land in, and landing in the safe lane the
                    // player is pinned in is a fair one-lane dodge.
                    lastSteakLane = NoLane;
                    GameLog.Info($"[MiniLevelFoodRefusal] Double drop - safe lane {safeLane}");
                }
                else
                {
                    steakLane = GetSteakLane(lastSteakLane, reservedLane, collectibleDue);
                    SpawnSteak(steakLane);
                    steakLanes = LaneBit(steakLane);
                }

                reservedLane = NoLane;

                if (collectibleDue)
                {
                    int collectibleLane = GetCollectibleLane(steakLane, playerLanes);
                    SpawnCollectible(collectibleLane);
                    reservedLane = collectibleLane;
                    collectiblesSpawned++;
                    GameLog.Info($"[MiniLevelFoodRefusal] Spawned collectible {collectiblesSpawned}/{megaCollectibleCount} in lane {collectibleLane} (steak in {steakLane})");
                }

                if (steakLane != NoLane)
                    lastSteakLane = steakLane;

                playerLanes = AllLanesMask & ~steakLanes;
                UpdateUI();

                // Wait for next spawn (use realtime since TimeScale may be 0)
                spawnWait.waitTime = steakSpawnInterval;
                yield return spawnWait;
            }

            // Wait for all objects to fall past
            yield return new WaitForSecondsRealtime(spawnHeight / fallSpeed + 0.5f);

            // If we got here without game over, player wins!
            if (isGameRunning)
            {
                GameLog.Info("[MiniLevelFoodRefusal] Player survived all steaks!");
                isGameRunning = false;
                HidePanel();

                // Success - turn back away from the camera before the next level
                playerController?.Animations?.SetFacing(false);

                CompleteGame();
            }
        }

        private void SpawnSteak(int lane)
        {
            Vector3 spawnPos = new Vector3(
                lane * laneDistance,
                spawnHeight,
                playerZPosition
            );

            GameObject steak = ObjectPooler.Instance?.SpawnFromPool(
                steakPoolTag,
                spawnPos,
                Quaternion.identity
            );

            if (steak != null)
            {
                // Disable the normal Obstacle component if present (to prevent global game over)
                var obstacle = steak.GetComponent<Obstacle>();
                if (obstacle != null)
                {
                    obstacle.enabled = false;
                }

                // Add or configure falling behavior
                var falling = steak.GetComponent<FoodRefusalFallingObject>();
                if (falling == null)
                {
                    falling = steak.AddComponent<FoodRefusalFallingObject>();
                    GameLog.Info($"[MiniLevelFoodRefusal] Added FoodRefusalFallingObject component to steak");
                }

                falling.Initialize(
                    FoodRefusalFallingObject.FallingObjectType.Steak,
                    fallSpeed,
                    onHitSteak: OnSteakHit,
                    despawnHeight: -5f
                );

                // Verify collider setup
                var collider = steak.GetComponent<Collider>();
                var rb = steak.GetComponent<Rigidbody>();
                GameLog.Info($"[MiniLevelFoodRefusal] Steak spawned at {spawnPos} - Collider: {(collider != null ? $"exists, isTrigger={collider.isTrigger}" : "MISSING")}, Rigidbody: {(rb != null ? $"exists, isKinematic={rb.isKinematic}" : "MISSING")}");

                activeObjects.Add(steak);
            }
            else
            {
                GameLog.Warn($"[MiniLevelFoodRefusal] Failed to spawn steak from pool '{steakPoolTag}'! Make sure to add this pool to ObjectPooler.");
            }
        }

        private void SpawnCollectible(int lane)
        {
            Vector3 spawnPos = new Vector3(
                lane * laneDistance,
                spawnHeight,
                playerZPosition
            );

            GameObject collectible = ObjectPooler.Instance?.SpawnFromPool(
                collectiblePoolTag,
                spawnPos,
                Quaternion.identity
            );

            if (collectible != null)
            {
                // Disable normal Collectible behavior if present
                var originalCollectible = collectible.GetComponent<Collectible>();
                if (originalCollectible != null)
                {
                    originalCollectible.enabled = false;
                }

                // Add or configure falling behavior
                var falling = collectible.GetComponent<FoodRefusalFallingObject>();
                if (falling == null)
                {
                    falling = collectible.AddComponent<FoodRefusalFallingObject>();
                    GameLog.Info($"[MiniLevelFoodRefusal] Added FoodRefusalFallingObject component to collectible");
                }

                falling.Initialize(
                    FoodRefusalFallingObject.FallingObjectType.Collectible,
                    fallSpeed,
                    onCollectCollectible: OnCollectibleCollected,
                    collectiblePoints: megaCollectiblePoints,
                    despawnHeight: -5f
                );

                // Verify collider setup
                var collider = collectible.GetComponent<Collider>();
                var rb = collectible.GetComponent<Rigidbody>();
                GameLog.Info($"[MiniLevelFoodRefusal] Collectible spawned at {spawnPos} - Collider: {(collider != null ? $"exists, isTrigger={collider.isTrigger}" : "MISSING")}, Rigidbody: {(rb != null ? $"exists, isKinematic={rb.isKinematic}" : "MISSING")}");

                activeObjects.Add(collectible);
            }
            else
            {
                GameLog.Warn($"[MiniLevelFoodRefusal] Failed to spawn collectible from pool '{collectiblePoolTag}'!");
            }
        }

        private void OnSteakHit()
        {
            if (!isGameRunning) return;

            GameLog.Info("[MiniLevelFoodRefusal] Player hit a steak! Game over.");
            isGameRunning = false;

            // Steak splat: wet hit + dust burst + camera shake
            LevelManager.Instance?.PlayCollectSound(splatSound);
            if (playerController != null)
                ImpactVFX.PlayDust(playerController.transform.position + Vector3.up * 0.8f, 12);
            CameraStateMachine.Instance?.AddShake(0.3f);

            // Reset mini-level score (removes points earned in this mini-level only)
            ScoreManager.Instance?.ResetMiniLevelScore();

            // Stop the game
            StopGame();

            // Trigger mini-level game over
            GameManager.Instance?.TriggerMiniLevelGameOver();
        }

        private void OnCollectibleCollected(int points)
        {
            GameLog.Info($"[MiniLevelFoodRefusal] Collectible collected! +{points} points");

            // The falling collectible's own Collectible component is disabled,
            // so its burst never fires - play it manually at the catch point
            if (playerController != null)
                CollectBurstVFX.PlayCoin(playerController.transform.position + Vector3.up * 0.8f, true);

            // Add to mini-level score (also adds to level score)
            ScoreManager.Instance?.AddMiniLevelScore(points);

            UpdateUI();

            // Play collect sound
            if (collectSound != null)
            {
                LevelManager.Instance?.PlayCollectSound(collectSound);
            }
        }

        /// <summary>
        /// Lane for the next steak: usually not a repeat of the last one, never
        /// <paramref name="reservedLane"/> (a collectible is falling there and the
        /// player has to stand in it), and held to an outer lane when a
        /// collectible drops alongside it.
        ///
        /// That last rule is what makes the coins catchable. Lane changes slide
        /// the dog through everything in between, so a steak in the middle lane
        /// walls the board in half - a player on the wrong side has to cross
        /// underneath it to reach a coin on the far side. Kept outside, whatever
        /// two lanes are left are always next to each other.
        /// </summary>
        private int GetSteakLane(int lastLane, int reservedLane, bool outerOnly)
        {
            int[] source = outerOnly ? OuterLanes : Lanes;

            // Moves off the last lane 70% of the time, and always if it has to
            bool avoidRepeat = Random.value < 0.7f;

            laneOptions.Clear();
            for (int pass = 0; pass < 2 && laneOptions.Count == 0; pass++)
            {
                foreach (int lane in source)
                {
                    if (lane == reservedLane) continue;
                    if (avoidRepeat && lane == lastLane) continue;
                    laneOptions.Add(lane);
                }

                // Two outer lanes minus a reserved one leaves nothing to move to
                avoidRepeat = false;
            }

            return laneOptions.Count > 0 ? laneOptions[Random.Range(0, laneOptions.Count)] : lastLane;
        }

        /// <summary>
        /// The one lane a double drop leaves open, or <see cref="NoLane"/> when no
        /// fair one exists and the beat should stay a single steak.
        ///
        /// A double names the lane the player has to be in, so it is only fair if
        /// they can get there from wherever they currently are. One swipe moves one
        /// lane and PlayerController gates swipes behind a cooldown, so a two-lane
        /// crossing costs two of them - more than the 1.35s between beats buys.
        /// Hence: the safe lane has to be within one lane of every lane the player
        /// could be standing in.
        /// </summary>
        private int GetDoubleSafeLane(int playerLanes)
        {
            laneOptions.Clear();
            foreach (int lane in Lanes)
            {
                if (IsOneLaneFromAll(lane, playerLanes))
                    laneOptions.Add(lane);
            }

            return laneOptions.Count > 0 ? laneOptions[Random.Range(0, laneOptions.Count)] : NoLane;
        }

        /// <summary>True when <paramref name="lane"/> is at most one lane change from every lane in the mask.</summary>
        private static bool IsOneLaneFromAll(int lane, int laneMask)
        {
            foreach (int from in Lanes)
            {
                if ((laneMask & LaneBit(from)) == 0) continue;
                if (Mathf.Abs(lane - from) > 1) return false;
            }
            return true;
        }

        /// <summary>
        /// Lane for a collectible: clear of the steak falling beside it, and one
        /// the player could already be standing in rather than diving into at the
        /// last moment.
        /// </summary>
        private int GetCollectibleLane(int steakLane, int playerLanes)
        {
            laneOptions.Clear();
            foreach (int lane in Lanes)
            {
                if (lane == steakLane) continue;
                if ((playerLanes & LaneBit(lane)) == 0) continue;
                laneOptions.Add(lane);
            }

            // Only when a double pinned the player in the very lane this beat's
            // steak fell in - they have to move regardless, so any lane will do
            if (laneOptions.Count == 0)
            {
                foreach (int lane in Lanes)
                {
                    if (lane != steakLane)
                        laneOptions.Add(lane);
                }
            }

            return laneOptions[Random.Range(0, laneOptions.Count)];
        }

        private void UpdateUI()
        {
            if (miniLevelScoreText != null)
            {
                int score = ScoreManager.Instance?.MiniLevelScore ?? 0;
                miniLevelScoreText.text = score > 0 ? $"+{score}" : "";
            }
        }

        private void ShowPanel()
        {
            if (gamePanel != null)
                gamePanel.SetActive(true);
        }

        private void HidePanel()
        {
            if (gamePanel != null)
                gamePanel.SetActive(false);
        }
    }
}
