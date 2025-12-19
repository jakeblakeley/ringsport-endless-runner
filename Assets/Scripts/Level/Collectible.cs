using UnityEngine;
using RingSport.Core;

namespace RingSport.Level
{
    public class Collectible : MonoBehaviour
    {
        [SerializeField] private int pointValue = 10;
        [SerializeField] private ParticleSystem collectVFX;
        [SerializeField] private AudioClip collectSound;
        [SerializeField] private float sfxVolume = 1.0f;

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

            // Add points to score
            LevelManager.Instance?.AddScore(pointValue);

            // Play VFX if assigned
            if (collectVFX != null)
            {
                collectVFX.Play();
            }

            // Play collect sound (uses PlayClipAtPoint so sound continues after object is pooled)
            if (collectSound != null)
                AudioSource.PlayClipAtPoint(collectSound, transform.position, sfxVolume);

            // Return to pool immediately
            ReturnToPool();
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
