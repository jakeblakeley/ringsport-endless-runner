using UnityEngine;

namespace RingSport.Level
{
    public class CollectibleAnimation : MonoBehaviour
    {
        [Header("Rotation")]
        [SerializeField] private float rotationSpeed = 50f;
        [SerializeField] private Vector3 rotationAxis = Vector3.up;

        [Header("Hover")]
        [SerializeField] private float hoverHeight = 0.3f;
        [SerializeField] private float hoverSpeed = 2f;

        private float previousHoverOffset = 0f;
        private Transform cachedTransform;

        private void Awake()
        {
            cachedTransform = transform;
        }

        private void LateUpdate()
        {
            // Rotate the collectible
            cachedTransform.Rotate(rotationAxis.normalized, rotationSpeed * Time.deltaTime, Space.World);

            // Use global time for synchronized animation across all collectibles
            float hoverTime = Time.time * hoverSpeed;

            // Calculate ease-in-out-circ value (0 to 1)
            float t = (Mathf.Sin(hoverTime) + 1f) / 2f; // Convert sin wave (-1 to 1) to (0 to 1)
            float easedValue = EaseInOutCirc(t);

            // Calculate current hover offset
            float currentHoverOffset = (easedValue - 0.5f) * 2f * hoverHeight; // Center around 0

            // Apply the delta hover offset to work with ScrollableObject
            float deltaHover = currentHoverOffset - previousHoverOffset;
            cachedTransform.position += new Vector3(0f, deltaHover, 0f);

            // Store for next frame
            previousHoverOffset = currentHoverOffset;
        }

        /// <summary>
        /// Ease-in-out-circ interpolation function
        /// </summary>
        private float EaseInOutCirc(float t)
        {
            if (t < 0.5f)
            {
                // Ease in
                return (1f - Mathf.Sqrt(1f - 4f * t * t)) / 2f;
            }
            else
            {
                // Ease out
                float x = 2f * t - 2f;
                return (Mathf.Sqrt(1f - x * x) + 1f) / 2f;
            }
        }
    }
}
