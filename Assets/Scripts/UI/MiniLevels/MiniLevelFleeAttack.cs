using UnityEngine;
using RingSport.Level;

namespace RingSport.UI
{
    /// <summary>
    /// Flee Attack mini level gameplay.
    /// TODO: Implement actual game mechanics.
    /// </summary>
    public class MiniLevelFleeAttack : MiniLevelBase
    {
        public override MiniLevelType MiniLevelType => MiniLevelType.FleeAttack;

        public override void StartGame()
        {
            Debug.Log("[MiniLevelFleeAttack] Starting game...");

            // TODO: Implement actual gameplay
            // For now, immediately complete
            CompleteGame();
        }

        public override void StopGame()
        {
            Debug.Log("[MiniLevelFleeAttack] Stopping game...");
        }
    }
}
