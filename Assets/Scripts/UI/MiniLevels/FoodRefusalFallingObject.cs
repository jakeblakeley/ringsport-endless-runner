using UnityEngine;
using RingSport.Core;

namespace RingSport.UI
{
    /// <summary>
    /// Handles falling behavior for Food Refusal mini-level objects.
    /// Attaches to obstacles and collectibles spawned during the mini-level.
    /// </summary>
    public class FoodRefusalFallingObject : MonoBehaviour
    {
        public enum FallingObjectType { Steak, Collectible }

        private float fallSpeed = 8f;
        private float despawnY = -5f;

        private FallingObjectType objectType;
        private System.Action onSteakHit;
        private System.Action<int> onCollectibleCollected;
        private int pointValue = 100;
        private bool hasBeenTriggered = false;
        private bool isInitialized = false;
        private Rigidbody rb;

        /// <summary>
        /// Initialize the falling object with callbacks and settings.
        /// </summary>
        public void Initialize(
            FallingObjectType type,
            float speed,
            System.Action onHitSteak = null,
            System.Action<int> onCollectCollectible = null,
            int collectiblePoints = 100,
            float despawnHeight = -5f)
        {
            objectType = type;
            fallSpeed = speed;
            onSteakHit = onHitSteak;
            onCollectibleCollected = onCollectCollectible;
            pointValue = collectiblePoints;
            despawnY = despawnHeight;
            hasBeenTriggered = false;
            isInitialized = true;

            // Cache rigidbody reference
            rb = GetComponent<Rigidbody>();

            Debug.Log($"[FoodRefusalFallingObject] Initialized as {type} at {transform.position}, Rigidbody: {(rb != null ? "found" : "MISSING")}");
        }

        private void FixedUpdate()
        {
            if (!isInitialized) return;

            // Use Rigidbody.MovePosition for proper physics/trigger detection
            if (rb != null)
            {
                Vector3 newPosition = rb.position + Vector3.down * fallSpeed * Time.fixedDeltaTime;
                rb.MovePosition(newPosition);
            }
            else
            {
                // Fallback if no rigidbody
                transform.position += Vector3.down * fallSpeed * Time.unscaledDeltaTime;
            }

            // Cleanup when below play area
            if (transform.position.y < despawnY)
            {
                Debug.Log($"[FoodRefusalFallingObject] {objectType} fell below despawn height, cleaning up");
                Cleanup();
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            Debug.Log($"[FoodRefusalFallingObject] OnTriggerEnter called by {other.name}, tag: {other.tag}, isInitialized: {isInitialized}, hasBeenTriggered: {hasBeenTriggered}");

            if (!isInitialized || hasBeenTriggered) return;

            if (other.CompareTag("Player"))
            {
                hasBeenTriggered = true;

                Debug.Log($"[FoodRefusalFallingObject] Player hit {objectType}! Invoking callback...");

                if (objectType == FallingObjectType.Steak)
                {
                    Debug.Log($"[FoodRefusalFallingObject] onSteakHit is {(onSteakHit != null ? "set" : "NULL")}");
                    onSteakHit?.Invoke();
                    // Don't cleanup immediately - let the mini-level handle it
                }
                else if (objectType == FallingObjectType.Collectible)
                {
                    Debug.Log($"[FoodRefusalFallingObject] onCollectibleCollected is {(onCollectibleCollected != null ? "set" : "NULL")}");
                    onCollectibleCollected?.Invoke(pointValue);
                    Cleanup();
                }
            }
        }

        private void Cleanup()
        {
            isInitialized = false;
            hasBeenTriggered = false;
            rb = null;

            // Return to pool
            ObjectPooler.Instance?.ReturnToPool(gameObject);
        }

        private void OnDisable()
        {
            // Reset state when returned to pool
            isInitialized = false;
            hasBeenTriggered = false;
            onSteakHit = null;
            onCollectibleCollected = null;
            rb = null;
        }
    }
}
