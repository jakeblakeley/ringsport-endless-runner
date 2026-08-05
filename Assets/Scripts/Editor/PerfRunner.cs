using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RingSport.EditorTools
{
    /// <summary>
    /// Editor side of the perf harness. Watches for
    /// PerfReports/pending_request.json (written externally or via the menu
    /// item) and enters play mode so PerfProbe can run the sampled session.
    /// The probe deletes the marker and exits play mode when done.
    /// </summary>
    [InitializeOnLoad]
    public static class PerfRunner
    {
        private const string MarkerPath = "PerfReports/pending_request.json";
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private static double nextCheck;

        // Survives the domain reloads that play-mode cycling causes
        private static int EnterAttempts
        {
            get => SessionState.GetInt("PerfRunner.enterAttempts", 0);
            set => SessionState.SetInt("PerfRunner.enterAttempts", value);
        }

        static PerfRunner()
        {
            EditorApplication.update += Tick;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            // Fallback spawn path: RuntimeInitializeOnLoadMethod has proven
            // flaky when the script was imported mid-refresh, so the editor
            // spawns the probe directly once play mode is up.
            if (change == PlayModeStateChange.EnteredPlayMode && File.Exists(MarkerPath))
                RingSport.DebugTools.PerfProbe.EnsureSpawned();
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextCheck)
                return;
            nextCheck = EditorApplication.timeSinceStartup + 1.0;

            if (EditorApplication.isPlaying)
            {
                // A request is pending but this session has no probe (session
                // predates the request, or bootstrap was missed after an
                // in-play recompile): restart into a clean session.
                if (File.Exists(MarkerPath) && !RingSport.DebugTools.PerfProbe.IsActive)
                {
                    Debug.Log("[PerfRunner] Pending perf request with no probe in session - exiting play mode to restart cleanly");
                    EditorApplication.ExitPlaymode();
                }
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (!File.Exists(MarkerPath))
            {
                if (EnterAttempts != 0)
                    EnterAttempts = 0; // last request completed - fresh budget for the next one
                return;
            }

            if (EnterAttempts >= 4)
            {
                Debug.LogError("[PerfRunner] Perf run failed to start after 4 attempts - marking request as failed");
                try { File.Move(MarkerPath, MarkerPath.Replace("pending_request", "failed_request")); }
                catch (System.Exception) { File.Delete(MarkerPath); }
                EnterAttempts = 0;
                return;
            }
            EnterAttempts++;

            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (scene.isDirty)
                {
                    Debug.LogWarning("[PerfRunner] Perf run pending, but the active scene has unsaved changes. Save or discard them; retrying in 10s.");
                    nextCheck = EditorApplication.timeSinceStartup + 10.0;
                    return;
                }
                EditorSceneManager.OpenScene(ScenePath);
            }

            Debug.Log("[PerfRunner] Perf request found - entering play mode");
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("Tools/Perf/Run 60s Sample")]
        private static void RunManual()
        {
            Directory.CreateDirectory("PerfReports");
            File.WriteAllText(MarkerPath, "{\"label\":\"manual\",\"durationSeconds\":60}");
            Debug.Log("[PerfRunner] Perf request queued - play mode will start shortly");
        }
    }
}
