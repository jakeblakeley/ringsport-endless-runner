using UnityEngine;

namespace RingSport.Level
{
    /// <summary>
    /// Controls the arc effect shader by updating global shader parameters.
    /// This creates an Animal Crossing-style world curvature effect where objects
    /// further from the player are warped downward, creating a horizon dip effect.
    /// </summary>
    public class ArcEffectController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField, Tooltip("The player transform (usually assigned automatically via tag)")]
        private Transform playerTransform;

        [Header("Effect Settings")]
        [SerializeField, Tooltip("Enable/disable the arc effect at runtime")]
        private bool enableEffect = true;

        [SerializeField, Tooltip("Override player Y position (useful for keeping effect consistent)")]
        private bool useFixedYPosition = true;

        [SerializeField, Tooltip("Fixed Y position to use when useFixedYPosition is true")]
        private float fixedYPosition = 0f;

        [Header("Arc Parameters (Global Control)")]
        [SerializeField, Tooltip("Controls how strong the arc displacement is (0 = no effect, 50 = maximum)")]
        [Range(0f, 50f)]
        private float arcStrength = 2f;

        [SerializeField, Tooltip("Distance at which the arc effect reaches maximum strength")]
        [Range(10f, 100f)]
        private float arcDistance = 20f;

        [Header("Atmospheric Tint")]
        [SerializeField, Tooltip("Color tint applied to distant objects")]
        private Color tintColor = new Color(0.8f, 0.9f, 1.0f, 1.0f);

        [SerializeField, Tooltip("Strength of the atmospheric tint effect")]
        [Range(0f, 1f)]
        private float tintStrength = 0.5f;

        // Shader property IDs (cached for performance)
        private static readonly int PlayerPositionID = Shader.PropertyToID("_PlayerPosition");
        private static readonly int ArcStrengthID = Shader.PropertyToID("_ArcStrength");
        private static readonly int ArcDistanceID = Shader.PropertyToID("_ArcDistance");
        private static readonly int TintColorID = Shader.PropertyToID("_TintColor");
        private static readonly int TintStrengthID = Shader.PropertyToID("_TintStrength");

        // Track previous values to detect changes
        private float previousArcStrength;
        private float previousArcDistance;
        private Color previousTintColor;
        private float previousTintStrength;

        private void Start()
        {
            // Auto-find player if not assigned
            if (playerTransform == null)
            {
                GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
                if (playerObject != null)
                {
                    playerTransform = playerObject.transform;
                }
                else
                {
                    Debug.LogWarning("ArcEffectController: No player found! Please assign player transform or tag player as 'Player'");
                }
            }

            // Initialize global shader parameters
            UpdateAllShaderParameters();
        }

        private void Update()
        {
            if (!enableEffect || playerTransform == null)
            {
                return;
            }

            // Always update player position
            UpdateShaderPlayerPosition();

            // Check if arc parameters changed and update if needed
            if (HasParametersChanged())
            {
                UpdateArcParameters();
            }
        }

        /// <summary>
        /// Updates all global shader parameters.
        /// </summary>
        private void UpdateAllShaderParameters()
        {
            UpdateShaderPlayerPosition();
            UpdateArcParameters();
        }

        /// <summary>
        /// Updates the global shader property for player position.
        /// This is used by all materials using the ArcEffect shader.
        /// </summary>
        private void UpdateShaderPlayerPosition()
        {
            Vector3 position = playerTransform.position;

            // Optionally use fixed Y to prevent vertical player movement from affecting the arc
            if (useFixedYPosition)
            {
                position.y = fixedYPosition;
            }

            // Set global shader property (affects all materials using this property)
            Shader.SetGlobalVector(PlayerPositionID, position);
        }

        /// <summary>
        /// Updates the arc effect parameters globally for all materials.
        /// </summary>
        private void UpdateArcParameters()
        {
            Shader.SetGlobalFloat(ArcStrengthID, arcStrength);
            Shader.SetGlobalFloat(ArcDistanceID, arcDistance);
            Shader.SetGlobalColor(TintColorID, tintColor);
            Shader.SetGlobalFloat(TintStrengthID, tintStrength);

            // Update tracked values
            previousArcStrength = arcStrength;
            previousArcDistance = arcDistance;
            previousTintColor = tintColor;
            previousTintStrength = tintStrength;
        }

        /// <summary>
        /// Checks if any arc parameters have changed since last update.
        /// </summary>
        private bool HasParametersChanged()
        {
            return !Mathf.Approximately(previousArcStrength, arcStrength) ||
                   !Mathf.Approximately(previousArcDistance, arcDistance) ||
                   previousTintColor != tintColor ||
                   !Mathf.Approximately(previousTintStrength, tintStrength);
        }

        /// <summary>
        /// Enable or disable the arc effect at runtime.
        /// </summary>
        public void SetEffectEnabled(bool enabled)
        {
            enableEffect = enabled;

            // If disabling, reset all parameters to neutralize effect
            if (!enabled)
            {
                Shader.SetGlobalVector(PlayerPositionID, Vector3.zero);
                Shader.SetGlobalFloat(ArcStrengthID, 0f);
            }
            else
            {
                UpdateAllShaderParameters();
            }
        }

        /// <summary>
        /// Toggle the effect on/off.
        /// </summary>
        public void ToggleEffect()
        {
            SetEffectEnabled(!enableEffect);
        }

        /// <summary>
        /// Set the arc strength globally.
        /// </summary>
        public void SetArcStrength(float strength)
        {
            arcStrength = Mathf.Clamp(strength, 0f, 50f);
            UpdateArcParameters();
        }

        /// <summary>
        /// Set the arc distance globally.
        /// </summary>
        public void SetArcDistance(float distance)
        {
            arcDistance = Mathf.Clamp(distance, 10f, 100f);
            UpdateArcParameters();
        }

        /// <summary>
        /// Set the tint color for distant objects.
        /// </summary>
        public void SetTintColor(Color color)
        {
            tintColor = color;
            UpdateArcParameters();
        }

        /// <summary>
        /// Set the tint strength.
        /// </summary>
        public void SetTintStrength(float strength)
        {
            tintStrength = Mathf.Clamp01(strength);
            UpdateArcParameters();
        }

        /// <summary>
        /// Set the player transform reference manually.
        /// </summary>
        public void SetPlayerTransform(Transform player)
        {
            playerTransform = player;
        }

        private void OnDisable()
        {
            // Clean up shader properties when disabled
            Shader.SetGlobalVector(PlayerPositionID, Vector3.zero);
            Shader.SetGlobalFloat(ArcStrengthID, 0f);
            Shader.SetGlobalFloat(ArcDistanceID, 50f);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Update immediately in editor when values change
            if (Application.isPlaying && enableEffect)
            {
                UpdateAllShaderParameters();
            }
        }
#endif
    }
}
