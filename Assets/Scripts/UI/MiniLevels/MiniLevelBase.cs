using UnityEngine;
using RingSport.Level;
using RingSport.Core;

namespace RingSport.UI
{
    /// <summary>
    /// Base class for mini level gameplay logic.
    /// Each mini level type implements its own game mechanics.
    /// Called by MiniLevelManager after countdown completes.
    /// </summary>
    public abstract class MiniLevelBase : MonoBehaviour
    {
        /// <summary>
        /// The mini level type this script handles
        /// </summary>
        public abstract MiniLevelType MiniLevelType { get; }

        /// <summary>
        /// Called as soon as the mini level is entered, before the start panel
        /// (or, on a retry, the countdown) appears. isRetry is true when the
        /// entry follows a failure - state meant to survive a retry (like a
        /// resume round) should only be reset when it is false.
        /// </summary>
        public virtual void OnMiniLevelEntry(bool isRetry)
        {
            // Default: do nothing
        }

        /// <summary>
        /// Called when user clicks start button, before countdown begins.
        /// Override to set up camera, UI, etc.
        /// </summary>
        public virtual void OnPrepareGame()
        {
            // Default: do nothing
        }

        /// <summary>
        /// Called when this mini level should start (after countdown)
        /// </summary>
        public abstract void StartGame();

        /// <summary>
        /// Called to stop/cleanup this mini level
        /// </summary>
        public abstract void StopGame();

        /// <summary>
        /// Call this when the mini level gameplay is complete
        /// </summary>
        protected void CompleteGame()
        {
            GameLog.Info($"[{GetType().Name}] Game complete");
            MiniLevelManager.Instance?.OnMiniLevelGameComplete();
        }
    }
}
