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
            Debug.Log($"[{GetType().Name}] Game complete");
            MiniLevelManager.Instance?.OnMiniLevelGameComplete();
        }
    }
}
