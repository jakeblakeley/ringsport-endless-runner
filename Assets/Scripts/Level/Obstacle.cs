using UnityEngine;
using RingSport.Core;
using RingSport.Effects;
using RingSport.Player;
using RingSport.UI;

namespace RingSport.Level
{
    public enum ObstacleType
    {
        Avoid,      // Hit = Game Over
        JumpOver,   // Can jump over safely
        Palisade    // Requires rapid tapping to clear
    }

    public class Obstacle : MonoBehaviour
    {
        [SerializeField] private ObstacleType obstacleType = ObstacleType.Avoid;
        [SerializeField] private float jumpHeightThreshold = 1.5f; // Min height to clear JumpOver obstacles

        private bool hasBeenTriggered = false; // Prevent multiple triggers

        // Cached component references for performance
        private Collider obstacleCollider;
        private GameManager gameManager;
        private UIManager uiManager;

        public ObstacleType Type => obstacleType;

        /// <summary>
        /// Runtime initializer for procedurally-built obstacles (e.g. the
        /// flee attack finale walls), which can't set the serialized type.
        /// </summary>
        public void Configure(ObstacleType type)
        {
            obstacleType = type;
            hasBeenTriggered = false;
        }

        private void Awake()
        {
            // Cache component reference (collider doesn't change)
            obstacleCollider = GetComponent<Collider>();
        }

        private void OnEnable()
        {
            // Reset trigger state when object is reused from pool
            hasBeenTriggered = false;

            // Cache singleton references on enable (they may not exist during Awake for pooled objects)
            gameManager = GameManager.Instance;
            uiManager = UIManager.Instance;
        }

        private void OnTriggerEnter(Collider other)
        {
            // Skip if component is disabled (e.g., during mini-levels that reuse obstacle prefabs)
            if (!enabled)
                return;

            // Prevent multiple triggers
            if (hasBeenTriggered)
                return;

            // Perf-harness automation: sail through hits (no death, no minigame)
            if (PerfFlags.Invincible)
                return;

            GameLog.Info($"Obstacle ({obstacleType}) triggered by: {other.name}, tag: {other.tag}");

            // Check if player collided with obstacle
            if (other.CompareTag("Player"))
            {
                // Get the root player object (in case trigger is on child)
                Transform playerRoot = other.transform.root;
                PlayerController player = playerRoot.GetComponent<PlayerController>();

                if (player == null)
                {
                    player = other.GetComponent<PlayerController>();
                }

                if (player != null)
                {
                    hasBeenTriggered = true; // Mark as triggered before processing
                    OnPlayerCollision(player);
                }
                else
                {
                    GameLog.Warn("Player tag found but no PlayerController component!");
                }
            }
        }

        public void OnPlayerCollision(PlayerController player)
        {
            float playerHeight = player.transform.position.y;

            switch (obstacleType)
            {
                case ObstacleType.Avoid:
                    // Instant game over
                    GameLog.Info($"Hit AVOID obstacle! Game Over!");
                    gameManager?.TriggerGameOver();
                    break;

                case ObstacleType.JumpOver:
                    // If player is on the ground or didn't jump high enough, game over
                    if (player.IsGrounded)
                    {
                        GameLog.Info($"Hit JUMP obstacle while grounded! Game Over!");
                        gameManager?.TriggerGameOver();
                    }
                    else if (playerHeight < jumpHeightThreshold)
                    {
                        GameLog.Info($"Hit JUMP obstacle while too low (height: {playerHeight}, required: {jumpHeightThreshold})! Game Over!");
                        gameManager?.TriggerGameOver();
                    }
                    else
                    {
                        GameLog.Info($"Successfully jumped over obstacle! (height: {playerHeight})");

                        // Near-miss reward: a small white glint on the bar the
                        // dog just cleared, plus a whoosh that sharpens the
                        // tighter the clearance was
                        Vector3 barTop = obstacleCollider != null
                            ? new Vector3(transform.position.x, obstacleCollider.bounds.max.y, transform.position.z)
                            : transform.position;
                        CollectBurstVFX.PlayNearMiss(barTop);

                        // 0 = shaved the bar, 1 = sailed a body height over it
                        float clearance = Mathf.InverseLerp(jumpHeightThreshold, jumpHeightThreshold + 0.8f, playerHeight);
                        LevelManager.Instance?.PlayNearMissWhoosh(clearance);
                    }
                    break;

                case ObstacleType.Palisade:
                    HandlePalisadeCollision(player);
                    break;
            }
        }

        private void HandlePalisadeCollision(PlayerController player)
        {
            GameLog.Info("=== HandlePalisadeCollision started ===");

            // Use cached collider reference
            if (obstacleCollider == null)
            {
                GameLog.Error("Palisade obstacle has no collider!");
                gameManager?.TriggerGameOver();
                return;
            }

            // Calculate collision height relative to obstacle
            float obstacleBottom = obstacleCollider.bounds.min.y;
            float obstacleTop = obstacleCollider.bounds.max.y;
            float obstacleHeight = obstacleTop - obstacleBottom;
            float playerY = player.transform.position.y;

            // Calculate hit height percentage (0 = bottom, 1 = top)
            float hitHeightPercent = Mathf.Clamp01((playerY - obstacleBottom) / obstacleHeight);

            GameLog.Info($"Palisade collision - Hit height: {hitHeightPercent * 100f:F1}% (Player Y: {playerY}, Obstacle: {obstacleBottom} to {obstacleTop})");

            // Below 50% height = instant game over
            if (hitHeightPercent < 0.5f)
            {
                GameLog.Info($"Hit Palisade too low ({hitHeightPercent * 100f:F1}%)! Game Over!");
                gameManager?.TriggerGameOver();
                return;
            }

            // Calculate required taps: 50% = 10 taps, 100% = 1 tap (linear interpolation)
            // Map 50%-100% hit height to 10-1 taps
            float tapPercent = (hitHeightPercent - 0.5f) / 0.5f; // Remap 0.5-1.0 to 0-1
            int requiredTaps = Mathf.RoundToInt(Mathf.Lerp(10f, 1f, tapPercent));
            requiredTaps = Mathf.Max(1, requiredTaps); // Ensure at least 1 tap

            GameLog.Info($"Palisade requires {requiredTaps} taps (hit at {hitHeightPercent * 100f:F1}%)");
            GameLog.Info($"About to call UIManager.ShowPalisadeMinigame, UIManager.Instance: {(uiManager != null ? "EXISTS" : "NULL")}");

            // Where the dog should end up gripping the wall:
            //   x = the wall's lane, y = its base (the vault arc), z = the FACE
            //   the dog ran into. The world scrolls in whole frames, so this
            //   face is wherever the last scroll step happened to leave it -
            //   the player aligns its clamber pose against it.
            Vector3 contactPoint = new Vector3(
                transform.position.x,
                obstacleBottom,
                obstacleCollider.bounds.min.z
            );

            // Trigger the minigame
            uiManager?.ShowPalisadeMinigame(
                requiredTaps,
                contactPoint,
                obstacleHeight,
                player
            );

            GameLog.Info("=== HandlePalisadeCollision finished ===");
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Set tag based on obstacle type for pooling
            // Use delayCall to avoid SendMessage errors during validation
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && gameObject != null)
                {
                    gameObject.tag = "Obstacle";
                }
            };
        }
#endif
    }
}
