using System.Collections;
using UnityEngine;
using RingSport.Core;
using RingSport.Effects;
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
            LevelManager.Instance?.PlayCollectSound(collectSound, comboPitch: true);

            CollectBurstVFX.PlayLoveNote(transform.position);

            // Rare-pickup beat: a brief soft slow-mo. Runs on the shared juice
            // host (this object returns to the pool immediately) and is
            // guarded so it never fights the chase overrides, the finish
            // moment or a death freeze.
            Juice.Run(LoveNoteSlowMoMoment());

            if (LoveNoteManager.TryCollectRandomLockedNote(out int noteIndex))
            {
                Debug.Log($"[LoveNoteCollectible] Collected love note {noteIndex}!");
            }

            UIManager.Instance?.UpdateLoveNoteCounter(LoveNoteManager.CollectedThisRun);

            ObjectPooler.Instance?.ReturnToPool(gameObject);
        }

        private static IEnumerator LoveNoteSlowMoMoment()
        {
            var scroller = LevelScroller.Instance;
            var gameManager = GameManager.Instance;
            if (scroller == null || gameManager == null || gameManager.CurrentState != GameState.Playing)
                yield break;
            if (scroller.HasSpeedOverride || gameManager.DeathSequenceRunning)
                yield break;
            if (LevelManager.Instance != null && LevelManager.Instance.FinishMomentActive)
                yield break;

            float speed = scroller.GetScrollSpeed();
            const float duration = 0.45f;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;

                // If a new owner took the world speed (death freeze, finish
                // beat), back off and leave their override alone
                gameManager = GameManager.Instance;
                if (gameManager == null || gameManager.CurrentState != GameState.Playing || gameManager.DeathSequenceRunning)
                    yield break;
                if (LevelManager.Instance != null && LevelManager.Instance.FinishMomentActive)
                    yield break;

                float dip = Mathf.Lerp(0.55f, 1f, Juice.OutQuad(Mathf.Clamp01(elapsed / duration)));
                scroller.SetSpeedOverride(speed * dip);
                yield return null;
            }

            scroller.ClearSpeedOverride();
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
