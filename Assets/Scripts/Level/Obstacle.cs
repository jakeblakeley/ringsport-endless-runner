using UnityEngine;
using RingSport.Core;
using RingSport.Player;

namespace RingSport.Level
{
    public enum ObstacleType
    {
        Avoid,      // Hit = Game Over
        JumpOver    // Can jump over safely
    }

    public class Obstacle : MonoBehaviour
    {
        [SerializeField] private ObstacleType obstacleType = ObstacleType.Avoid;
        [SerializeField] private float jumpHeightThreshold = 1.5f; // Min height to clear JumpOver obstacles

        public ObstacleType Type => obstacleType;

        private void OnTriggerEnter(Collider other)
        {
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
                    GameManager.Instance?.TriggerGameOver();
                    break;

                case ObstacleType.JumpOver:
                    // If player didn't jump high enough, game over
                    if (playerHeight < jumpHeightThreshold)
                    {
                        Debug.Log($"Hit JUMP obstacle while too low (height: {playerHeight}, required: {jumpHeightThreshold})! Game Over!");
                        GameManager.Instance?.TriggerGameOver();
                    }
                    else
                    {
                        Debug.Log($"Successfully jumped over obstacle! (height: {playerHeight})");
                    }
                    break;
            }
        }

        private void OnValidate()
        {
            // Set tag based on obstacle type for pooling
            gameObject.tag = "Obstacle";
        }
    }
}
