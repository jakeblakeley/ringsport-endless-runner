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

        private void OnTriggerEnter(Collider other)
        {
            // Check if player entered the finish line
            if (!hasTriggered && other.CompareTag("Player"))
            {
                hasTriggered = true;
                Debug.Log("Player reached finish line!");

                // Trigger level completion
                if (LevelManager.Instance != null)
                {
                    LevelManager.Instance.OnFinishLineReached();
                }
            }
        }
    }
}
