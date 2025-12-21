using UnityEngine;
using RingSport.Level;

namespace RingSport.UI
{
    /// <summary>
    /// Face Attack mini level gameplay.
    /// TODO: Implement actual game mechanics.
    /// </summary>
    public class MiniLevelFaceAttack : MiniLevelBase
    {
        public override MiniLevelType MiniLevelType => MiniLevelType.FaceAttack;

        public override void StartGame()
        {
            Debug.Log("[MiniLevelFaceAttack] Starting game...");

            // TODO: Implement actual gameplay
            // For now, immediately complete
            CompleteGame();
        }

        public override void StopGame()
        {
            Debug.Log("[MiniLevelFaceAttack] Stopping game...");
        }
    }
}
