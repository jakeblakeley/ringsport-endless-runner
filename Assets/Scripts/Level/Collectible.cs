using UnityEngine;
using RingSport.Core;

namespace RingSport.Level
{
    public class Collectible : MonoBehaviour
    {
        [SerializeField] private int pointValue = 10;
        [SerializeField] private ParticleSystem collectVFX;
        [SerializeField] private AudioClip collectSound;

        private bool isCollected = false;
        private MeshRenderer meshRenderer;

        private void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }

        private void OnEnable()
        {
            isCollected = false;

            // Make sure visual is enabled
            if (meshRenderer != null)
                meshRenderer.enabled = true;
        }

        private void OnDisable()
        {
            // Cancel any pending invoke when disabled
            CancelInvoke();
        }

        private void OnTriggerEnter(Collider other)
        {
            // Skip if component is disabled (e.g., during mini-levels that handle collection differently)
            if (!enabled)
                return;

            Debug.Log($"Collectible triggered by: {other.name}, tag: {other.tag}");

            // Check if player collided with collectible
            if (other.CompareTag("Player"))
            {
                Debug.Log("Player collected item!");
                Collect();
            }
        }

        public void Collect()
        {
            if (isCollected)
            {
                Debug.Log("Already collected, ignoring");
                return;
            }

            isCollected = true;

            Debug.Log($"Collecting! Adding {pointValue} points");

            // Hide visual immediately
            if (meshRenderer != null)
                meshRenderer.enabled = false;

            // Add points to score and play sound
            LevelManager.Instance?.AddScore(pointValue);
            LevelManager.Instance?.PlayCollectSound(collectSound);

            // Play VFX if assigned
            if (collectVFX != null)
            {
                collectVFX.Play();
            }

            // Return to pool immediately (or after short delay for VFX)
            float delay = collectVFX != null ? 0.5f : 0f;
            Invoke(nameof(ReturnToPool), delay);
        }

        private void ReturnToPool()
        {
            Debug.Log("Returning collectible to pool");
            ObjectPooler.Instance?.ReturnToPool(gameObject);
        }

        /// <summary>
        /// Set the point value for this collectible (used for mega collectibles)
        /// </summary>
        public void SetPointValue(int value)
        {
            pointValue = value;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Set tag for pooling
            // Use delayCall to avoid SendMessage errors during validation
            UnityEditor.EditorApplication.delayCall += () =>
            {
                if (this != null && gameObject != null)
                {
                    gameObject.tag = "Collectible";
                }
            };
        }
#endif
    }
}
