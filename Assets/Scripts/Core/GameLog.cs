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
        /// <summary>
        /// Gates the chatty per-event logs (jumps, coin arcs, sprint presses,
        /// the once-a-second ground check). In a web development build every log
        /// is marshaled out to the JS console, which accumulates entries and
        /// degrades as a session goes on - jump-heavy play read as creeping lag
        /// (2026-08-06). Off by default; the debug menu can flip it on.
        /// </summary>
        public static bool VerboseEnabled;

#if !UNITY_EDITOR
        /// <summary>
        /// Players don't need Log/Warning stack traces, and on web capturing one
        /// per log is a real per-event cost. The editor keeps them - that is
        /// what makes console click-through work. Errors keep traces everywhere.
        /// </summary>
        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void SilencePlayerStackTraces()
        {
            UnityEngine.Application.SetStackTraceLogType(UnityEngine.LogType.Log, UnityEngine.StackTraceLogType.None);
            UnityEngine.Application.SetStackTraceLogType(UnityEngine.LogType.Warning, UnityEngine.StackTraceLogType.None);
        }
#endif

        /// <summary>Per-event chatter; compiled out of release, silent unless VerboseEnabled.</summary>
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD")]
        public static void Verbose(string message)
        {
            if (VerboseEnabled)
                UnityEngine.Debug.Log(message);
        }

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
