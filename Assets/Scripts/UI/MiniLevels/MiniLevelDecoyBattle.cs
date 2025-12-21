using UnityEngine;
using RingSport.Level;

namespace RingSport.UI
{
    /// <summary>
    /// Decoy Battle mini level gameplay.
    /// TODO: Implement actual game mechanics.
    /// </summary>
    public class MiniLevelDecoyBattle : MiniLevelBase
    {
        public override MiniLevelType MiniLevelType => MiniLevelType.DecoyBattle;

        public override void StartGame()
        {
            Debug.Log("[MiniLevelDecoyBattle] Starting game...");

            // TODO: Implement actual gameplay
            // For now, immediately complete
            CompleteGame();
        }

        public override void StopGame()
        {
            Debug.Log("[MiniLevelDecoyBattle] Stopping game...");
        }
    }
}
