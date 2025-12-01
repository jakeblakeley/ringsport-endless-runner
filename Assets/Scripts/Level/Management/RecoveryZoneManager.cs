using UnityEngine;

namespace RingSport.Level
{
    /// <summary>
    /// Manages recovery zones after palisade minigames
    /// FAIRNESS: Prevents spawning obstacles immediately after challenging minigames
    /// </summary>
    public class RecoveryZoneManager
    {
        private const float RECOVERY_ZONE_DURATION = 15f; // 15 units = ~1-1.5 seconds at normal speed

        private bool inRecoveryZone = false;
        private float recoveryZoneEndVirtualZ = 0f;

        /// <summary>
        /// Start a new recovery zone at the current virtual distance
        /// </summary>
        public void StartRecoveryZone(float currentVirtualDistance)
        {
            if (!inRecoveryZone)
            {
                inRecoveryZone = true;
                recoveryZoneEndVirtualZ = currentVirtualDistance + RECOVERY_ZONE_DURATION;
                Debug.Log($"Palisade completed! Recovery zone active until virtual Z: {recoveryZoneEndVirtualZ:F2}");
            }
        }

        /// <summary>
        /// Check if currently in a recovery zone
        /// Updates state if recovery zone has ended
        /// </summary>
        public bool IsInRecoveryZone(float currentVirtualDistance)
        {
            if (inRecoveryZone)
            {
                if (currentVirtualDistance >= recoveryZoneEndVirtualZ)
                {
                    // Recovery zone ended
                    inRecoveryZone = false;
                    Debug.Log("Recovery zone ended, resuming obstacle spawning");
                    return false;
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Reset recovery zone state (called on level reset)
        /// </summary>
        public void Reset()
        {
            inRecoveryZone = false;
            recoveryZoneEndVirtualZ = 0f;
        }
    }
}
