using UnityEngine;
using UnityEngine.UI;
using TMPro;
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
        [SerializeField] private int totalSteaks = 20;
        [SerializeField] private float steakSpawnInterval = 1f;
        [SerializeField] private float fallSpeed = 8f;
        [SerializeField] private float spawnHeight = 15f;

        [Header("Pool Settings")]
        [SerializeField] private string steakPoolTag = "FoodRefusalSteak";
        [SerializeField] private string collectiblePoolTag = "MegaCollectible";

        [Header("Lane Settings")]
        [SerializeField] private float laneDistance = 3f; // -3, 0, +3

        [Header("Collectible Settings")]
        [SerializeField] private int megaCollectibleCount = 3;
        [SerializeField] private int megaCollectiblePoints = 100;

        [Header("UI References")]
        [SerializeField] private GameObject gamePanel;
        [SerializeField] private TextMeshProUGUI miniLevelScoreText;

        [Header("Audio")]
        [SerializeField] private AudioClip collectSound;

        // Runtime state
        private Coroutine gameCoroutine;
        private List<GameObject> activeObjects = new List<GameObject>();
        private int steaksSpawned = 0;
        private bool isGameRunning = false;
        private PlayerController playerController;
        private int previousLane = 0;
        private float playerZPosition;

        // Collectible spawn indices (at steaks 5, 10, 15)
        private readonly int[] collectibleSpawnIndices = { 4, 9, 14 };

        /// <summary>
        /// Called when user clicks start, before countdown begins.
        /// Sets up camera for the mini-level.
        /// </summary>
        public override void OnPrepareGame()
        {
            Debug.Log("[MiniLevelFoodRefusal] Preparing game - setting camera to MiniLevel state");
            CameraStateMachine.Instance?.SetState(CameraStateType.MiniLevel);
        }

        public override void StartGame()
        {
            Debug.Log("[MiniLevelFoodRefusal] Starting game...");

            // Reset state
            steaksSpawned = 0;
            isGameRunning = true;
            activeObjects.Clear();
            previousLane = 0;

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
                Debug.Log($"[MiniLevelFoodRefusal] Player found at Z={playerZPosition}");
            }
            else
            {
                playerZPosition = 0f;
                Debug.LogWarning("[MiniLevelFoodRefusal] PlayerController not found!");
            }

            // Show UI
            ShowPanel();
            UpdateUI();

            // Start spawning
            gameCoroutine = StartCoroutine(RunGame());
        }

        public override void StopGame()
        {
            Debug.Log("[MiniLevelFoodRefusal] Stopping game...");

            isGameRunning = false;

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

            for (int i = 0; i < totalSteaks && isGameRunning; i++)
            {
                steaksSpawned = i + 1;

                // Determine lane for this steak
                int steakLane = GetRandomLaneAvoidingRepeat(previousLane);
                previousLane = steakLane;

                // Spawn steak
                SpawnSteak(steakLane);

                // Check if we should also spawn a collectible
                if (collectiblesSpawned < megaCollectibleCount &&
                    System.Array.IndexOf(collectibleSpawnIndices, i) >= 0)
                {
                    int collectibleLane = GetSafeLane(steakLane);
                    SpawnCollectible(collectibleLane);
                    collectiblesSpawned++;
                    Debug.Log($"[MiniLevelFoodRefusal] Spawned collectible {collectiblesSpawned}/{megaCollectibleCount} in lane {collectibleLane}");
                }

                UpdateUI();

                // Wait for next spawn (use realtime since TimeScale may be 0)
                yield return new WaitForSecondsRealtime(steakSpawnInterval);
            }

            // Wait for all objects to fall past
            yield return new WaitForSecondsRealtime(spawnHeight / fallSpeed + 0.5f);

            // If we got here without game over, player wins!
            if (isGameRunning)
            {
                Debug.Log("[MiniLevelFoodRefusal] Player survived all steaks!");
                isGameRunning = false;
                HidePanel();
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
                    Debug.Log($"[MiniLevelFoodRefusal] Added FoodRefusalFallingObject component to steak");
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
                Debug.Log($"[MiniLevelFoodRefusal] Steak spawned at {spawnPos} - Collider: {(collider != null ? $"exists, isTrigger={collider.isTrigger}" : "MISSING")}, Rigidbody: {(rb != null ? $"exists, isKinematic={rb.isKinematic}" : "MISSING")}");

                activeObjects.Add(steak);
            }
            else
            {
                Debug.LogWarning($"[MiniLevelFoodRefusal] Failed to spawn steak from pool '{steakPoolTag}'! Make sure to add this pool to ObjectPooler.");
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
                    Debug.Log($"[MiniLevelFoodRefusal] Added FoodRefusalFallingObject component to collectible");
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
                Debug.Log($"[MiniLevelFoodRefusal] Collectible spawned at {spawnPos} - Collider: {(collider != null ? $"exists, isTrigger={collider.isTrigger}" : "MISSING")}, Rigidbody: {(rb != null ? $"exists, isKinematic={rb.isKinematic}" : "MISSING")}");

                activeObjects.Add(collectible);
            }
            else
            {
                Debug.LogWarning($"[MiniLevelFoodRefusal] Failed to spawn collectible from pool '{collectiblePoolTag}'!");
            }
        }

        private void OnSteakHit()
        {
            if (!isGameRunning) return;

            Debug.Log("[MiniLevelFoodRefusal] Player hit a steak! Game over.");
            isGameRunning = false;

            // Reset mini-level score (removes points earned in this mini-level only)
            ScoreManager.Instance?.ResetMiniLevelScore();

            // Stop the game
            StopGame();

            // Trigger mini-level game over
            GameManager.Instance?.TriggerMiniLevelGameOver();
        }

        private void OnCollectibleCollected(int points)
        {
            Debug.Log($"[MiniLevelFoodRefusal] Collectible collected! +{points} points");

            // Add to mini-level score (also adds to level score)
            ScoreManager.Instance?.AddMiniLevelScore(points);

            UpdateUI();

            // Play collect sound
            if (collectSound != null)
            {
                LevelManager.Instance?.PlayCollectSound(collectSound);
            }
        }

        private int GetRandomLaneAvoidingRepeat(int previousLane)
        {
            int[] lanes = { -1, 0, 1 };
            int newLane;

            // 70% chance to pick a different lane
            if (Random.value < 0.7f)
            {
                // Pick a different lane
                List<int> otherLanes = new List<int>();
                foreach (int lane in lanes)
                {
                    if (lane != previousLane)
                        otherLanes.Add(lane);
                }
                newLane = otherLanes[Random.Range(0, otherLanes.Count)];
            }
            else
            {
                // 30% chance to repeat the same lane
                newLane = lanes[Random.Range(0, lanes.Length)];
            }

            return newLane;
        }

        private int GetSafeLane(int steakLane)
        {
            // Return a lane that's not the steak lane
            int[] options;
            if (steakLane == 0)
            {
                options = new[] { -1, 1 };
            }
            else if (steakLane == -1)
            {
                options = new[] { 0, 1 };
            }
            else // steakLane == 1
            {
                options = new[] { -1, 0 };
            }

            return options[Random.Range(0, options.Length)];
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
