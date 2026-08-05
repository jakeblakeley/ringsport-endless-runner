using UnityEngine;
using System.Runtime.InteropServices;
using RingSport.Core;

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
                GameLog.Info("WebGL touch event prevention initialized for iOS Safari");
            }
            catch (System.Exception e)
            {
                GameLog.Error($"Failed to initialize WebGL touch event prevention: {e.Message}");
            }
#else
            GameLog.Info("WebGLTouchHandler is only active in WebGL builds");
#endif
        }
    }
}
