using UnityEngine;
using RingSport.Level;

namespace RingSport.UI
{
    /// <summary>
    /// Positions Simon Says mini level gameplay.
    /// TODO: Implement actual game mechanics.
    /// </summary>
    public class MiniLevelPositionsSimonSays : MiniLevelBase
    {
        public override MiniLevelType MiniLevelType => MiniLevelType.PositionsSimonSays;

        public override void StartGame()
        {
            Debug.Log("[MiniLevelPositionsSimonSays] Starting game...");

            // TODO: Implement actual gameplay
            // For now, immediately complete
            CompleteGame();
        }

        public override void StopGame()
        {
            Debug.Log("[MiniLevelPositionsSimonSays] Stopping game...");
        }
    }
}
