using UnityEngine;
using RingSport.Core;

namespace RingSport.Level
{
    /// <summary>
    /// Pickup that grants partial lives (0.5) during the palisade minigame.
    /// Lives only count when you collect enough to form a full retry.
    /// </summary>
    public class LifePickup : MonoBehaviour
    {
        [SerializeField] private float lifeValue = 0.5f;
        [SerializeField] private AudioClip collectSound;
        [SerializeField] private ParticleSystem collectVFX;

        private bool isCollected = false;
        private MeshRenderer meshRenderer;

        private void Awake()
        {
            meshRenderer = GetComponent<MeshRenderer>();
        }

        private void OnEnable()
        {
            isCollected = false;
            if (meshRenderer != null)
                meshRenderer.enabled = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!isCollected && other.CompareTag("Player"))
            {
                Collect();
            }
        }

        public void Collect()
        {
            if (isCollected)
                return;

            isCollected = true;

            // Hide mesh immediately
            if (meshRenderer != null)
                meshRenderer.enabled = false;

            // Add partial life
            LevelManager.Instance?.AddPartialRetry(lifeValue);
            LevelManager.Instance?.PlayCollectSound(collectSound);

            // Play VFX
            if (collectVFX != null)
                collectVFX.Play();

            Debug.Log($"[LifePickup] Collected! Added {lifeValue} life.");

            // Destroy after short delay for VFX
            Destroy(gameObject, collectVFX != null ? 0.5f : 0f);
        }

        /// <summary>
        /// Check if this pickup has been collected
        /// </summary>
        public bool IsCollected => isCollected;
    }
}
