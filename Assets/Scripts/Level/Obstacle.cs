using UnityEngine;
using RingSport.Core;
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

        private void Awake()
        {
            // Cache component references
            obstacleCollider = GetComponent<Collider>();
            gameManager = GameManager.Instance;
            uiManager = UIManager.Instance;
        }

        private void OnEnable()
        {
            // Reset trigger state when object is reused from pool
            hasBeenTriggered = false;
        }

        private void OnTriggerEnter(Collider other)
        {
            // Prevent multiple triggers
            if (hasBeenTriggered)
                return;

            Debug.Log($"Obstacle ({obstacleType}) triggered by: {other.name}, tag: {other.tag}");

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
                    Debug.LogWarning("Player tag found but no PlayerController component!");
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
                    Debug.Log($"Hit AVOID obstacle! Game Over!");
                    gameManager?.TriggerGameOver();
                    break;

                case ObstacleType.JumpOver:
                    // If player didn't jump high enough, game over
                    if (playerHeight < jumpHeightThreshold)
                    {
                        Debug.Log($"Hit JUMP obstacle while too low (height: {playerHeight}, required: {jumpHeightThreshold})! Game Over!");
                        gameManager?.TriggerGameOver();
                    }
                    else
                    {
                        Debug.Log($"Successfully jumped over obstacle! (height: {playerHeight})");
                    }
                    break;

                case ObstacleType.Palisade:
                    HandlePalisadeCollision(player);
                    break;
            }
        }

        private void HandlePalisadeCollision(PlayerController player)
        {
            Debug.Log("=== HandlePalisadeCollision started ===");

            // Use cached collider reference
            if (obstacleCollider == null)
            {
                Debug.LogError("Palisade obstacle has no collider!");
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

            Debug.Log($"Palisade collision - Hit height: {hitHeightPercent * 100f:F1}% (Player Y: {playerY}, Obstacle: {obstacleBottom} to {obstacleTop})");

            // Below 50% height = instant game over
            if (hitHeightPercent < 0.5f)
            {
                Debug.Log($"Hit Palisade too low ({hitHeightPercent * 100f:F1}%)! Game Over!");
                gameManager?.TriggerGameOver();
                return;
            }

            // Calculate required taps: 50% = 10 taps, 100% = 1 tap (linear interpolation)
            // Map 50%-100% hit height to 10-1 taps
            float tapPercent = (hitHeightPercent - 0.5f) / 0.5f; // Remap 0.5-1.0 to 0-1
            int requiredTaps = Mathf.RoundToInt(Mathf.Lerp(10f, 1f, tapPercent));
            requiredTaps = Mathf.Max(1, requiredTaps); // Ensure at least 1 tap

            Debug.Log($"Palisade requires {requiredTaps} taps (hit at {hitHeightPercent * 100f:F1}%)");
            Debug.Log($"About to call UIManager.ShowPalisadeMinigame, UIManager.Instance: {(uiManager != null ? "EXISTS" : "NULL")}");

            // Pass obstacle bottom position for accurate arc calculation
            Vector3 obstacleBottomPosition = new Vector3(
                transform.position.x,
                obstacleBottom,
                transform.position.z
            );

            // Trigger the minigame
            uiManager?.ShowPalisadeMinigame(
                requiredTaps,
                obstacleBottomPosition,
                obstacleHeight,
                player
            );

            Debug.Log("=== HandlePalisadeCollision finished ===");
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
