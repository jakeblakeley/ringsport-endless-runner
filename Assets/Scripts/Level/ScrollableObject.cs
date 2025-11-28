using UnityEngine;

namespace RingSport.Level
{
    public class ScrollableObject : MonoBehaviour
    {
        private void Update()
        {
            if (LevelScroller.Instance != null)
            {
                LevelScroller.Instance.ScrollObject(transform);
            }
        }
    }
}
