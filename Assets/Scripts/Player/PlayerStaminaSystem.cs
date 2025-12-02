using UnityEngine;
using RingSport.UI;

namespace RingSport.Player
{
    /// <summary>
    /// Handles player sprint stamina management including draining, refilling, and exhaustion states
    /// </summary>
    public class PlayerStaminaSystem
    {
        private readonly float maxSprintDuration;
        private readonly float sprintDrainRate;
        private readonly float sprintRefillRate;

        private float currentSprintStamina;
        private bool isSprintExhausted = false;

        // UI update throttling
        private float lastStaminaUIValue = 1f;
        private const float StaminaUIUpdateThreshold = 0.01f;

        private UIManager uiManager;

        public bool IsSprinting { get; set; }
        public bool IsSprintExhausted => isSprintExhausted;
        public float CurrentStamina => currentSprintStamina;
        public float StaminaPercent => currentSprintStamina / maxSprintDuration;

        public PlayerStaminaSystem(float maxDuration, float drainRate, float refillRate)
        {
            maxSprintDuration = maxDuration;
            sprintDrainRate = drainRate;
            sprintRefillRate = refillRate;
            currentSprintStamina = maxDuration; // Start with full stamina
        }

        public void Initialize(UIManager uiManager)
        {
            this.uiManager = uiManager;
        }

        public void Update(float deltaTime)
        {
            if (IsSprinting && !isSprintExhausted)
            {
                // Drain stamina while sprinting
                currentSprintStamina -= sprintDrainRate * deltaTime;

                if (currentSprintStamina <= 0f)
                {
                    currentSprintStamina = 0f;
                    isSprintExhausted = true;
                    IsSprinting = false; // Force stop sprinting
                }
            }
            else if (!IsSprinting)
            {
                // Refill stamina when not sprinting
                currentSprintStamina += sprintRefillRate * deltaTime;

                if (currentSprintStamina >= maxSprintDuration)
                {
                    currentSprintStamina = maxSprintDuration;

                    // Unlock sprint once fully refilled
                    if (isSprintExhausted)
                    {
                        isSprintExhausted = false;
                    }
                }
            }

            // Update UI only if stamina changed significantly (throttling)
            float fillAmount = StaminaPercent;
            if (uiManager != null && Mathf.Abs(fillAmount - lastStaminaUIValue) > StaminaUIUpdateThreshold)
            {
                uiManager.UpdateSprintBar(fillAmount, isSprintExhausted);
                lastStaminaUIValue = fillAmount;
            }
        }

        public void Reset()
        {
            currentSprintStamina = maxSprintDuration;
            isSprintExhausted = false;
            IsSprinting = false;
            lastStaminaUIValue = 1f;
        }

        public bool CanSprint()
        {
            return !isSprintExhausted;
        }
    }
}
