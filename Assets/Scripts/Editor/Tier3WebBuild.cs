using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace RingSport.EditorTools
{
    /// <summary>
    /// Tier-3 one-shot: point the Web target at WebGPU with a WebGL2 fallback,
    /// then produce a development build (PerfProbe included, triggered via
    /// ?perf=1) and write a size/result summary the harness workflow can read.
    /// Marker-triggered so the whole thing runs unattended; also available via
    /// the menu. Safe to delete once the pipeline settles.
    /// </summary>
    [InitializeOnLoad]
    public static class Tier3WebBuild
    {
        private const string MarkerPath = "PerfReports/build_web_request.txt";
        private const string ResultPath = "PerfReports/build_result.json";
        private const string OutputDir = "Builds/Web-dev";
        private static double nextCheck;

        static Tier3WebBuild()
        {
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextCheck)
                return;
            nextCheck = EditorApplication.timeSinceStartup + 1.0;

            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (!File.Exists(MarkerPath))
                return;

            File.Delete(MarkerPath);
            Build();
        }

        [MenuItem("Tools/Perf/Build Web (Development)")]
        private static void Build()
        {
            // WebGL2 only. WebGPU was tried first (2026-08-04): the device
            // initialized and claimed the canvas, then the main loop hung on
            // first present with no errors - not shippable on 6000.5. Revisit
            // WebGPU as a progressive enhancement on a later Unity 6 patch.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL,
                new[] { GraphicsDeviceType.OpenGLES3 });
            Debug.Log("[Tier3WebBuild] Graphics APIs set: WebGL2 only");

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                target = BuildTarget.WebGL,
                locationPathName = OutputDir,
                options = BuildOptions.Development,
            };

            Debug.Log("[Tier3WebBuild] Starting WebGL development build...");
            BuildReport report = BuildPipeline.BuildPlayer(options);

            var lines = new List<string>
            {
                "{",
                $"  \"result\": \"{report.summary.result}\",",
                $"  \"errors\": {report.summary.totalErrors},",
                $"  \"warnings\": {report.summary.totalWarnings},",
                $"  \"totalSeconds\": {report.summary.totalTime.TotalSeconds:F0},",
                $"  \"totalSizeMB\": {report.summary.totalSize / (1024f * 1024f):F1},",
                "  \"files\": {",
            };

            string buildDataDir = Path.Combine(OutputDir, "Build");
            if (Directory.Exists(buildDataDir))
            {
                var entries = new List<string>();
                foreach (string f in Directory.GetFiles(buildDataDir))
                {
                    var info = new FileInfo(f);
                    entries.Add($"    \"{info.Name}\": {info.Length / (1024f * 1024f):F2}");
                }
                lines.Add(string.Join(",\n", entries));
            }

            lines.Add("  }");
            lines.Add("}");

            Directory.CreateDirectory("PerfReports");
            File.WriteAllText(ResultPath, string.Join("\n", lines));
            Debug.Log($"[Tier3WebBuild] Build finished: {report.summary.result}, {report.summary.totalSize / (1024f * 1024f):F1} MB total - summary at {ResultPath}");
        }
    }
}
