using UnityEngine;
using RingSport.Level;

namespace RingSport.UI
{
    /// <summary>
    /// Food Refusal mini level gameplay.
    /// TODO: Implement actual game mechanics.
    /// </summary>
    public class MiniLevelFoodRefusal : MiniLevelBase
    {
        public override MiniLevelType MiniLevelType => MiniLevelType.FoodRefusal;

        public override void StartGame()
        {
            Debug.Log("[MiniLevelFoodRefusal] Starting game...");

            // TODO: Implement actual gameplay
            // For now, immediately complete
            CompleteGame();
        }

        public override void StopGame()
        {
            Debug.Log("[MiniLevelFoodRefusal] Stopping game...");
        }
    }
}
