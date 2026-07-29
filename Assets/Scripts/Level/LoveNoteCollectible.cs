using UnityEngine;
using RingSport.Core;
using RingSport.UI;

namespace RingSport.Level
{
    /// <summary>
    /// Rare pickup that spawns in place of a large coin. Worth the same points,
    /// and permanently unlocks one still-locked love note to view on the start
    /// screen. Pooled under PoolTags.LoveNote.
    /// </summary>
    public class LoveNoteCollectible : MonoBehaviour
    {
        [SerializeField] private int pointValue = 50;
        [SerializeField] private AudioClip collectSound;

        private bool isCollected = false;
        private SpriteRenderer spriteRenderer;

        private void Awake()
        {
            spriteRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        private void OnEnable()
        {
            isCollected = false;

            if (spriteRenderer != null)
                spriteRenderer.enabled = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!enabled)
                return;

            if (other.CompareTag("Player"))
            {
                Collect();
            }
        }

        public void Collect()
        {
            if (isCollected)
                return;

            isCollected = true;

            // Hide visual immediately
            if (spriteRenderer != null)
                spriteRenderer.enabled = false;

            // Same points as the large coin it replaced
            LevelManager.Instance?.AddScore(pointValue);
            LevelManager.Instance?.PlayCollectSound(collectSound);

            if (LoveNoteManager.TryCollectRandomLockedNote(out int noteIndex))
            {
                Debug.Log($"[LoveNoteCollectible] Collected love note {noteIndex}!");
            }

            UIManager.Instance?.UpdateLoveNoteCounter(LoveNoteManager.CollectedThisRun);

            ObjectPooler.Instance?.ReturnToPool(gameObject);
        }

        /// <summary>
        /// Set the point value (spawner passes the large coin's value so the
        /// note is worth exactly what the coin it replaced would have been)
        /// </summary>
        public void SetPointValue(int value)
        {
            pointValue = value;
        }
    }
}
