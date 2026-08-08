using System.Collections;
using UnityEngine;
using RingSport.Core;
using RingSport.Effects;

namespace RingSport.Level
{
    /// <summary>
    /// Rare pickup that spawns in place of a large coin (rolled after the
    /// love note, so notes keep priority). Worth the same points, and
    /// permanently unlocks the next droppable hat - an open-window seasonal
    /// hat first, else the next regular hat in catalog order. The floating
    /// model IS that hat, so the pickup always shows what you're getting.
    /// Pooled under PoolTags.Hat.
    /// </summary>
    public class HatCollectible : MonoBehaviour
    {
        [SerializeField] private int pointValue = 50;
        [SerializeField] private AudioClip collectSound;
        [Tooltip("Container the next-locked hat's model is instantiated under (its position/scale author the pickup framing).")]
        [SerializeField] private Transform visualRoot;

        private static readonly Color ToastColor = new Color(1f, 0.84f, 0.25f);

        private bool isCollected;
        private string shownHatId;

        private void OnEnable()
        {
            isCollected = false;

            if (visualRoot != null)
                visualRoot.gameObject.SetActive(true);

            RefreshVisual();
        }

        /// <summary>
        /// Keep the floating model in sync with the next hat this pickup would
        /// unlock. Pooled instances are reused across spawns, so the model is
        /// only rebuilt when the target hat actually changed.
        /// </summary>
        private void RefreshVisual()
        {
            if (visualRoot == null)
                return;

            string nextId = HatManager.NextDropId;
            if (nextId == shownHatId && visualRoot.childCount > 0)
                return;

            for (int i = visualRoot.childCount - 1; i >= 0; i--)
                Destroy(visualRoot.GetChild(i).gameObject);
            shownHatId = nextId;

            GameObject prefab = HatManager.LoadHatPrefab(nextId);
            if (prefab == null)
                return;

            // The prefab root's position is a head-fitting offset that doesn't
            // apply here, but its rotation and scale are part of the hat's
            // identity (real models carry arbitrary source units) - keep them
            // and centre the rendered bounds in the pickup frame instead
            GameObject model = Instantiate(prefab, visualRoot, false);
            model.transform.localPosition = Vector3.zero;

            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);
                model.transform.position += visualRoot.position - bounds.center;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!enabled)
                return;

            if (other.CompareTag("Player"))
                Collect();
        }

        public void Collect()
        {
            if (isCollected)
                return;

            isCollected = true;

            // Hide visual immediately
            if (visualRoot != null)
                visualRoot.gameObject.SetActive(false);

            // Same points as the large coin it replaced
            LevelManager.Instance?.AddScore(pointValue);
            LevelManager.Instance?.PlayCollectSound(collectSound, comboPitch: true);

            CollectBurstVFX.PlayHat(transform.position);

            // Rare-pickup beat, same shape as the love note's: brief soft
            // slow-mo on the shared juice host, guarded against the chase
            // overrides, the finish moment and a death freeze.
            Juice.Run(HatSlowMoMoment());

            if (HatManager.TryUnlockNext(out string hatId))
            {
                GameLog.Info($"[HatCollectible] Unlocked hat '{hatId}'!");
                ScreenBanner.Show("New Hat Unlocked!", ToastColor, 1.1f, 72f,
                    LevelManager.Instance != null ? LevelManager.Instance.BannerFont : null);
            }

            ObjectPooler.Instance?.ReturnToPool(gameObject);
        }

        private static IEnumerator HatSlowMoMoment()
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
        /// hat is worth exactly what the coin it replaced would have been)
        /// </summary>
        public void SetPointValue(int value)
        {
            pointValue = value;
        }
    }
}
