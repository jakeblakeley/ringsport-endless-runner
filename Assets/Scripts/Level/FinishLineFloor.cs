using UnityEngine;
using RingSport.Core;

namespace RingSport.Level
{
    /// <summary>
    /// Finish line floor that triggers level completion when player reaches it
    /// </summary>
    public class FinishLineFloor : MonoBehaviour
    {
        private bool hasTriggered = false;

        /// <summary>
        /// The finish line floor is pooled, and the pool hands the most
        /// recently returned instance back out first - so without this reset
        /// the tile that completed the last level comes back already latched
        /// and silently swallows the next level's finish (the level then runs
        /// on forever). Re-arm on every spawn; ObjectPooler toggles active.
        /// </summary>
        private void OnEnable()
        {
            hasTriggered = false;

            // A finish line with no trigger ends nothing: the level runs past
            // the line forever with no reward screen. Arizona's prefab shipped
            // that way, so say it out loud rather than hang silently
            // (Tools/RingSport/Fix Finish Line Colliders repairs the prefab).
            if (GetComponent<Collider>() == null)
                GameLog.Error($"[FinishLineFloor] '{name}' has no collider - this level can never complete.");
        }

        private void OnTriggerEnter(Collider other)
        {
            // Check if player entered the finish line
            if (!hasTriggered && other.CompareTag("Player"))
            {
                hasTriggered = true;
                GameLog.Info("Player reached finish line!");

                // Trigger level completion
                if (LevelManager.Instance != null)
                {
                    LevelManager.Instance.OnFinishLineReached();
                }
            }
        }
    }
}
