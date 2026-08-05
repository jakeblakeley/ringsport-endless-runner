using System.Diagnostics;

namespace RingSport.Core
{
    /// <summary>
    /// Debug.Log wrapper that release builds strip completely: [Conditional]
    /// removes the call AND its argument construction (string interpolation,
    /// name/tag marshaling) at compile time. Logging is disproportionately
    /// expensive on WebGL, and several call sites sit on per-pickup and
    /// per-spawn paths. Errors always log - they are rare and load-bearing.
    /// </summary>
    public static class GameLog
    {
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Info(string message) => UnityEngine.Debug.Log(message);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Info(string message, UnityEngine.Object context) => UnityEngine.Debug.Log(message, context);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Warn(string message) => UnityEngine.Debug.LogWarning(message);

        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Warn(string message, UnityEngine.Object context) => UnityEngine.Debug.LogWarning(message, context);

        public static void Error(string message) => UnityEngine.Debug.LogError(message);

        public static void Error(string message, UnityEngine.Object context) => UnityEngine.Debug.LogError(message, context);
    }
}
