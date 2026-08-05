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
    /// Applies the Web player settings this project wants for an itch.io upload,
    /// and builds a release (non-development) player into Builds/Web-itch.
    ///
    /// Idempotent and version-gated: bump SettingsVersion to make it re-apply.
    /// Everything here is also reachable from Tools/RingSport/.
    /// </summary>
    [InitializeOnLoad]
    public static class WebBuildSettings
    {
        private const int SettingsVersion = 1;
        private const string VersionKey = "RingSport.WebBuildSettings.Version";
        private const string TemplateName = "PROJECT:RingSportItch";
        private const string ReleaseOutputDir = "Builds/Web-itch";

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

        [MenuItem("Tools/RingSport/Build Web (Release for itch)")]
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
                      $"Build/ payload = {bytes / (1024f * 1024f):F1} MB (this is the itch download). " +
                      $"Zip the CONTENTS of {ReleaseOutputDir} (index.html at the zip root) and upload.");
        }

        /// <summary>Unity writes debug-symbol folders next to the player that must never ship.</summary>
        private static void StripDoNotShipFolders(string root)
        {
            if (!Directory.Exists(root))
                return;

            foreach (string dir in Directory.GetDirectories(root))
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
