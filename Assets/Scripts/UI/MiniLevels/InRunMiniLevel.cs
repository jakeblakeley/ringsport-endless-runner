using System.Collections.Generic;
using RingSport.Level;

namespace RingSport.UI
{
    /// <summary>
    /// Base class for mini levels that play IN-RUN, during the Playing state,
    /// over the last stretch of their level (the flee attack chase, the stop
    /// attack) instead of in the arena flow. LevelManager schedules entry via
    /// GetLeadSeconds/BeginChase, and GameManager reroutes any MiniLevel-state
    /// entry (retry after a failure, debug menu jump) back into a short run
    /// that fast-forwards to the chase.
    /// </summary>
    public abstract class InRunMiniLevel : MiniLevelBase
    {
        // Every live in-run controller, so LevelManager can reset them all on
        // level start and look one up by type without hard-coding subclasses
        private static readonly List<InRunMiniLevel> controllers = new List<InRunMiniLevel>();

        public static IReadOnlyList<InRunMiniLevel> Controllers => controllers;

        /// <summary>The controller handling the given type, or null if the type is not in-run (arena flow).</summary>
        public static InRunMiniLevel GetController(MiniLevelType type)
        {
            foreach (var controller in controllers)
            {
                if (controller != null && controller.MiniLevelType == type)
                    return controller;
            }
            return null;
        }

        /// <summary>Subclasses call this from Awake once they've claimed their singleton.</summary>
        protected void Register()
        {
            if (!controllers.Contains(this))
                controllers.Add(this);
        }

        protected void Unregister()
        {
            controllers.Remove(this);
        }

        /// <summary>
        /// Seconds before the end of the level timer at which the chase must
        /// begin so it resolves before the finish line spawns.
        /// </summary>
        public abstract float GetLeadSeconds(int difficultyIndex);

        /// <summary>
        /// Called by LevelManager whenever a running level starts. Resets any
        /// leftover chase state; on a chase retry entry keeps the banked
        /// pre-chase score so BeginChase can re-seed it.
        /// </summary>
        public abstract void OnRunLevelStarted(bool isThisMiniLevelsLevel, bool isRetryEntry);

        /// <summary>
        /// Starts the chase. Called by LevelManager during the Playing state
        /// when the level timer enters the mini level's window.
        /// </summary>
        public abstract void BeginChase(int difficultyIndex);

        /// <summary>Called when the level ends (finish line or in-run completion): tear everything down.</summary>
        public abstract void NotifyLevelEndReached();

        public abstract bool IsChaseActive { get; }
    }
}
