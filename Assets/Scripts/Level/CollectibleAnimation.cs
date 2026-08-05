using System.Collections.Generic;
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
        private int regIndex = -1;

        // Registry: one driver LateUpdate ticks every live collectible instead
        // of up to ~95 pooled instances each paying their own managed dispatch
        private static readonly List<CollectibleAnimation> live = new List<CollectibleAnimation>(128);
        private static CollectibleAnimationDriver driver;

        private void Awake()
        {
            cachedTransform = transform;
        }

        private void OnEnable()
        {
            if (regIndex >= 0)
                return;
            regIndex = live.Count;
            live.Add(this);

            if (driver == null && Application.isPlaying)
            {
                var go = new GameObject("CollectibleAnimationDriver");
                DontDestroyOnLoad(go);
                driver = go.AddComponent<CollectibleAnimationDriver>();
            }
        }

        private void OnDisable()
        {
            int index = regIndex;
            if (index < 0)
                return;
            int last = live.Count - 1;
            CollectibleAnimation moved = live[last];
            live[index] = moved;
            moved.regIndex = index;
            live.RemoveAt(last);
            regIndex = -1;
        }

        internal static void TickAll(float deltaTime)
        {
            for (int i = 0; i < live.Count; i++)
                live[i].Tick(deltaTime);
        }

        private void Tick(float deltaTime)
        {
            // Rotate the collectible
            cachedTransform.Rotate(rotationAxis.normalized, rotationSpeed * deltaTime, Space.World);

            // Use global time for synchronized animation across all collectibles
            float hoverTime = Time.time * hoverSpeed;

            // Calculate ease-in-out-circ value (0 to 1)
            float t = (Mathf.Sin(hoverTime) + 1f) / 2f; // Convert sin wave (-1 to 1) to (0 to 1)
            float easedValue = EaseInOutCirc(t);

            // Calculate current hover offset
            float currentHoverOffset = (easedValue - 0.5f) * 2f * hoverHeight; // Center around 0

            // Apply the delta hover offset to work with the scroll loop
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

    /// <summary>Single LateUpdate driving every live CollectibleAnimation.</summary>
    internal class CollectibleAnimationDriver : MonoBehaviour
    {
        private void LateUpdate()
        {
            CollectibleAnimation.TickAll(Time.deltaTime);
        }
    }
}
