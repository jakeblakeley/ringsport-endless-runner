using UnityEngine;
using System.Runtime.InteropServices;

namespace RingSport.Input
{
    /// <summary>
    /// Handles WebGL/iOS Safari specific touch event optimizations.
    /// Prevents default browser touch behaviors like scrolling and zooming.
    /// </summary>
    public class WebGLTouchHandler : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void PreventDefaultTouchEvents();
#endif

        private void Start()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Call JavaScript plugin to prevent default touch events
            try
            {
                PreventDefaultTouchEvents();
                Debug.Log("WebGL touch event prevention initialized for iOS Safari");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to initialize WebGL touch event prevention: {e.Message}");
            }
#else
            Debug.Log("WebGLTouchHandler is only active in WebGL builds");
#endif
        }
    }
}
