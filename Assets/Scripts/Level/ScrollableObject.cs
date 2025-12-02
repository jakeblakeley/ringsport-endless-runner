using UnityEngine;

namespace RingSport.Level
{
    public class ScrollableObject : MonoBehaviour
    {
        private LevelScroller scroller;
        private Transform cachedTransform;

        private void Start()
        {
            scroller = LevelScroller.Instance;
            cachedTransform = transform;
        }

        private void Update()
        {
            if (scroller != null)
            {
                scroller.ScrollObject(cachedTransform);
            }
        }
    }
}
