using UnityEngine;

namespace RingSport.Level
{
    /// <summary>
    /// Marks a spawned object as world-scrolled. Registration only - the
    /// movement itself runs as one batched loop in LevelScroller.Update, so a
    /// frame costs one managed call instead of one per live object (100-250
    /// floors/scenery/obstacles/coins are alive at steady state).
    /// </summary>
    public class ScrollableObject : MonoBehaviour
    {
        // Slot in LevelScroller's registry (swap-remove bookkeeping)
        internal int RegIndex = -1;
        internal Transform CachedTransform;

        private void Awake()
        {
            CachedTransform = transform;
        }

        private void OnEnable()
        {
            LevelScroller.Register(this);
        }

        private void OnDisable()
        {
            LevelScroller.Unregister(this);
        }
    }
}
