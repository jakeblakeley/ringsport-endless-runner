using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace RingSport.EditorTools
{
    /// <summary>
    /// Applies the Web player settings this project wants for the Netlify deploy,
    /// and builds a release (non-development) player into Builds/Web-release.
    ///
    /// Idempotent and version-gated: bump SettingsVersion to make it re-apply.
    /// Everything here is also reachable from Tools/RingSport/.
    /// </summary>
    [InitializeOnLoad]
    public static class WebBuildSettings
    {
        private const int SettingsVersion = 3;
        private const string VersionKey = "RingSport.WebBuildSettings.Version";
        private const string TemplateName = "PROJECT:RingSportWeb";
        private const string ReleaseOutputDir = "Builds/Web-release";
        private const string WebGpuOutputDir = "Builds/Web-webgpu";
        private const string WebGpuMarkerPath = "PerfReports/build_webgpu_request.txt";
        private const string ReleaseMarkerPath = "PerfReports/build_release_request.txt";
        private static double nextMarkerCheck;

        /// <summary>
        /// Desktop WebGL2 exposes S3TC/DXT; phone GPUs expose ASTC/ETC2 instead.
        /// A build can only ship one, and anything the GPU cannot sample is
        /// decompressed to RGBA32 at load (4-8x the VRAM). DXT = desktop-first,
        /// which is what the itch page is aimed at. Flip to ASTC only if phones
        /// become the primary target, and re-measure memory if you do.
        /// </summary>
        private const WebGLTextureSubtarget TextureSubtarget = WebGLTextureSubtarget.DXT;

        static WebBuildSettings()
        {
            EditorApplication.delayCall += TryAutoRun;
            EditorApplication.update += PollMarker;
        }

        /// <summary>Lets the WebGPU test build be kicked off without clicking a menu.</summary>
        private static void PollMarker()
        {
            if (EditorApplication.timeSinceStartup < nextMarkerCheck)
                return;
            nextMarkerCheck = EditorApplication.timeSinceStartup + 1.0;

            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (File.Exists(WebGpuMarkerPath))
            {
                File.Delete(WebGpuMarkerPath);
                BuildWebGpuDebug();
                return;
            }

            if (File.Exists(ReleaseMarkerPath))
            {
                File.Delete(ReleaseMarkerPath);
                BuildRelease();
            }
        }

        private static void TryAutoRun()
        {
            // Domain reloads happen on compile, not on play-exit - a bare return
            // here means the apply silently never happens for the rest of the
            // session. Re-queue instead.
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryAutoRun;
                return;
            }

            if (EditorPrefs.GetInt(VersionKey, 0) >= SettingsVersion)
                return;

            try
            {
                Apply();
                EditorPrefs.SetInt(VersionKey, SettingsVersion);
            }
            catch (Exception e)
            {
                Debug.LogError($"[WebBuildSettings] Apply failed, will retry: {e}");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Apply Web Build Settings")]
        public static void Apply()
        {
            // --- Graphics API -------------------------------------------------
            // WebGL2 only, deliberately. WebGPU was tried on 2026-08-04 (see
            // Tier3WebBuild): the device initialised, claimed the canvas, then
            // hung on first present with no error. Re-test on a later 6000.x
            // patch before adding GraphicsDeviceType.WebGPU ahead of OpenGLES3.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.WebGL, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, new[] { GraphicsDeviceType.OpenGLES3 });

            // --- Page / template ----------------------------------------------
            // Full-bleed template: the stock one hard-codes a 960x600 canvas,
            // which letterboxes inside the itch embed iframe.
            PlayerSettings.WebGL.template = TemplateName;

            // --- Download size -------------------------------------------------
            // itch serves .br with the right Content-Encoding, so the JS
            // decompression fallback is dead weight. If an upload ever fails with
            // "Unable to parse Build/...", set decompressionFallback back to true.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Brotli;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.WebGL.dataCaching = true;
            PlayerSettings.WebGL.nameFilesAsHashes = true;

            PlayerSettings.stripEngineCode = true;
            // Medium, not High: Malbers drives a lot through UnityEvents and
            // ScriptableObject references that High is happy to strip. Link.xml
            // covers the known reflection users; raise to High only with a full
            // playtest behind it.
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);

            // --- Runtime -------------------------------------------------------
            PlayerSettings.WebGL.linkerTarget = WebGLLinkerTarget.Wasm;
            PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
            PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.Off;
            // Pinned here because the debug build toggles it under a try/finally:
            // an editor crash mid-build (seen 2026-08-06) skips the finally, and
            // the next debug build then "restores" the poisoned value forever.
            PlayerSettings.WebGL.showDiagnostics = false;
            PlayerSettings.WebGL.threadsSupport = false; // no SharedArrayBuffer on itch
            PlayerSettings.WebGL.powerPreference = WebGLPowerPreference.HighPerformance;

            // 2 GB is past what mobile Safari will ever hand out - it just turns a
            // fast, clear OOM into a two-minute stall and then a crash.
            PlayerSettings.WebGL.initialMemorySize = 256;
            PlayerSettings.WebGL.maximumMemorySize = 1024;
            PlayerSettings.WebGL.memoryGrowthMode = WebGLMemoryGrowthMode.Geometric;

            EditorUserBuildSettings.webGLBuildSubtarget = TextureSubtarget;

            SetStaticBatching();
            WriteLinkXml();

            AssetDatabase.SaveAssets();
            Debug.Log($"[WebBuildSettings] Applied (v{SettingsVersion}): template={TemplateName}, Brotli/no-fallback, " +
                      $"stripping=Medium, memory 256-1024MB geometric, textures={TextureSubtarget}, static batching on.");
        }

        /// <summary>
        /// PlayerSettings.SetBatchingForPlatform is internal; there is no public
        /// equivalent. Reflection with a loud failure is better than leaving
        /// static batching off on the one platform where draw calls hurt most.
        /// </summary>
        private static void SetStaticBatching()
        {
            MethodInfo setter = typeof(PlayerSettings).GetMethod(
                "SetBatchingForPlatform",
                BindingFlags.Static | BindingFlags.NonPublic,
                null,
                new[] { typeof(BuildTarget), typeof(int), typeof(int) },
                null);

            if (setter == null)
            {
                Debug.LogWarning("[WebBuildSettings] Could not reach SetBatchingForPlatform - " +
                                 "turn Static Batching on by hand in Player Settings > Web > Other Settings.");
                return;
            }

            setter.Invoke(null, new object[] { BuildTarget.WebGL, 1, 0 });
        }

        /// <summary>
        /// Managed stripping walks static references only. Anything resolved at
        /// runtime (UnityEvent targets, SerializeReference types, Malbers' state
        /// machines) has to be pinned by hand or it vanishes from the player.
        /// </summary>
        private static void WriteLinkXml()
        {
            const string path = "Assets/link.xml";
            string contents =
                "<linker>\n" +
                "  <!-- Generated by Tools/RingSport/Apply Web Build Settings. -->\n" +
                "  <!-- Managed stripping is Medium for Web; these assemblies resolve\n" +
                "       types at runtime and cannot survive stripping on their own. -->\n" +
                "  <assembly fullname=\"MalbersAnimations\" preserve=\"all\"/>\n" +
                "  <assembly fullname=\"MalbersAnimations.Cinemachine\" preserve=\"all\"/>\n" +
                "  <assembly fullname=\"MalbersAnimations.InputSystem\" preserve=\"all\"/>\n" +
                "  <assembly fullname=\"Unity.InputSystem\" preserve=\"all\"/>\n" +
                "  <assembly fullname=\"Assembly-CSharp\" preserve=\"all\"/>\n" +
                "</linker>\n";

            if (File.Exists(path) && File.ReadAllText(path) == contents)
                return;

            File.WriteAllText(path, contents);
            AssetDatabase.ImportAsset(path);
            Debug.Log("[WebBuildSettings] Wrote Assets/link.xml");
        }

        [MenuItem("Tools/RingSport/Build Web (Release)")]
        public static void BuildRelease()
        {
            Apply();

            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                target = BuildTarget.WebGL,
                locationPathName = ReleaseOutputDir,
                options = BuildOptions.None, // release: no profiler, no debug symbols
            };

            Debug.Log("[WebBuildSettings] Starting WebGL RELEASE build...");
            BuildReport report = BuildPipeline.BuildPlayer(options);

            if (report.summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[WebBuildSettings] Build {report.summary.result} with {report.summary.totalErrors} error(s).");
                return;
            }

            StripDoNotShipFolders(ReleaseOutputDir);

            long bytes = 0;
            string buildDir = Path.Combine(ReleaseOutputDir, "Build");
            if (Directory.Exists(buildDir))
            {
                foreach (string f in Directory.GetFiles(buildDir))
                    bytes += new FileInfo(f).Length;
            }

            Debug.Log($"[WebBuildSettings] Release build OK in {report.summary.totalTime.TotalSeconds:F0}s. " +
                      $"Build/ payload = {bytes / (1024f * 1024f):F1} MB. " +
                      $"Deploy the CONTENTS of {ReleaseOutputDir} (index.html at the root) to Netlify.");
        }

        /// <summary>
        /// Development build with WebGPU first and WebGL2 behind it, into its own
        /// output dir so the shipping build is never at risk. Every setting this
        /// touches is restored afterwards - leaving the project on WebGPU would
        /// silently poison the next release build.
        /// </summary>
        [MenuItem("Tools/RingSport/Build Web (WebGPU debug test)")]
        public static void BuildWebGpuDebug()
        {
            Apply();

            GraphicsDeviceType[] savedApis = PlayerSettings.GetGraphicsAPIs(BuildTarget.WebGL);
            WebGLExceptionSupport savedExceptions = PlayerSettings.WebGL.exceptionSupport;
            WebGLDebugSymbolMode savedSymbols = PlayerSettings.WebGL.debugSymbolMode;
            bool savedFallback = PlayerSettings.WebGL.decompressionFallback;
            bool savedDiagnostics = PlayerSettings.WebGL.showDiagnostics;

            try
            {
                // WebGPU first, WebGL2 behind it. Note the fallback only covers a
                // failed device *creation* - the 2026-08-04 failure got past that
                // and hung on present, which no fallback can rescue. That is what
                // the template's 45s watchdog is there to name.
                PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL,
                    new[] { GraphicsDeviceType.WebGPU, GraphicsDeviceType.OpenGLES3 });

                // Explicit, not FullWithStacktrace: managed exceptions still carry
                // stack traces in a development build, and Full roughly doubles the
                // wasm - which matters a lot when this has to be uploaded to itch.
                PlayerSettings.WebGL.exceptionSupport = WebGLExceptionSupport.ExplicitlyThrownExceptionsOnly;
                // External, not Embedded: symbols move into a side-car file the
                // loader fetches on demand, so stack traces still resolve but the
                // wasm stays small enough to actually upload to itch.
                PlayerSettings.WebGL.debugSymbolMode = WebGLDebugSymbolMode.External;
                PlayerSettings.WebGL.showDiagnostics = true;
                // Off, matching the shipping build: Brotli then actually applies,
                // which is the difference between a ~35 MB and a ~157 MB download.
                // The template ships a _headers file, so Netlify serves the .br
                // files with the right Content-Encoding. Turn this back on only
                // for a host that cannot set headers.
                PlayerSettings.WebGL.decompressionFallback = false;

                var options = new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Scenes/SampleScene.unity" },
                    target = BuildTarget.WebGL,
                    locationPathName = WebGpuOutputDir,
                    // ConnectWithProfiler lets the editor's Profiler window attach
                    // when the build is served locally; the in-page Profile button
                    // (template, DEVELOPMENT_PLAYER) is the one that works from a
                    // phone - it captures a .raw you open in the Profiler later.
                    options = BuildOptions.Development | BuildOptions.AllowDebugging
                              | BuildOptions.ConnectWithProfiler,
                };

                Debug.Log("[WebBuildSettings] Starting WebGPU DEBUG build (WebGPU -> WebGL2 fallback)...");
                BuildReport report = BuildPipeline.BuildPlayer(options);

                if (report.summary.result != BuildResult.Succeeded)
                {
                    Debug.LogError($"[WebBuildSettings] WebGPU build {report.summary.result} " +
                                   $"with {report.summary.totalErrors} error(s).");
                    return;
                }

                StripDoNotShipFolders(WebGpuOutputDir);
                Debug.Log($"[WebBuildSettings] WebGPU debug build OK in {report.summary.totalTime.TotalSeconds:F0}s -> {WebGpuOutputDir}");
            }
            finally
            {
                PlayerSettings.SetGraphicsAPIs(BuildTarget.WebGL, savedApis);
                PlayerSettings.WebGL.exceptionSupport = savedExceptions;
                PlayerSettings.WebGL.debugSymbolMode = savedSymbols;
                PlayerSettings.WebGL.decompressionFallback = savedFallback;
                PlayerSettings.WebGL.showDiagnostics = savedDiagnostics;
                AssetDatabase.SaveAssets();
                Debug.Log("[WebBuildSettings] Restored shipping Web settings (WebGL2 only, Brotli/no-fallback).");
            }
        }

        /// <summary>
        /// Unity writes debug-symbol folders that must never ship - some inside the
        /// output dir, and the Burst one as a SIBLING of it (Builds/&lt;product&gt;_Burst...),
        /// so both levels have to be swept.
        /// </summary>
        private static void StripDoNotShipFolders(string root)
        {
            if (!Directory.Exists(root))
                return;

            SweepDoNotShip(root);

            string parent = Path.GetDirectoryName(Path.GetFullPath(root));
            if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                SweepDoNotShip(parent);
        }

        private static void SweepDoNotShip(string dirToScan)
        {
            foreach (string dir in Directory.GetDirectories(dirToScan))
            {
                string name = Path.GetFileName(dir);
                if (name.EndsWith("_DoNotShip", StringComparison.Ordinal) ||
                    name.EndsWith("_ButDontShipItWithYourGame", StringComparison.Ordinal))
                {
                    Directory.Delete(dir, true);
                    Debug.Log($"[WebBuildSettings] Removed {name} from the build output.");
                }
            }
        }
    }
}
