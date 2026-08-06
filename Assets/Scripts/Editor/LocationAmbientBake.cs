using System.Collections.Generic;
using RingSport.Level;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RingSport.EditorTools
{
    /// <summary>
    /// Bakes each LocationConfig's skybox down to a 27-float ambient SH probe.
    ///
    /// At runtime the game used to call DynamicGI.UpdateEnvironment() on every
    /// skybox swap. That renders the skybox to a cubemap and reads it back to the
    /// CPU, which WebGPU refuses ("Texture Readback is not supported by WebGPU"),
    /// leaving the ambient probe garbage - every lit surface renders wrong while
    /// the skybox and UI, being unlit, stay correct. Doing it here means the
    /// player just assigns a pre-computed probe, on every platform.
    ///
    /// Re-run whenever a location's skybox material changes.
    /// </summary>
    [InitializeOnLoad]
    public static class LocationAmbientBake
    {
        private const int BakeVersion = 3;
        private const string VersionKey = "RingSport.LocationAmbientBake.Version";

        static LocationAmbientBake()
        {
            EditorApplication.delayCall += TryAutoRun;
        }

        private static void TryAutoRun()
        {
            // Domain reloads happen on compile, not on play-exit, so a bare return
            // would mean the bake never runs for the rest of the session.
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryAutoRun;
                return;
            }

            if (EditorPrefs.GetInt(VersionKey, 0) >= BakeVersion)
                return;

            Bake();
            EditorPrefs.SetInt(VersionKey, BakeVersion);
        }

        [MenuItem("Tools/RingSport/Bake Location Ambient")]
        public static void Bake()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[LocationAmbientBake] Not while in play mode - the bake mutates RenderSettings.");
                return;
            }

            string[] guids = AssetDatabase.FindAssets("t:LocationConfig");
            if (guids.Length == 0)
            {
                Debug.LogWarning("[LocationAmbientBake] No LocationConfig assets found.");
                return;
            }

            // The bake drives the live RenderSettings, so put them back afterwards
            // or the open scene silently keeps the last location's sky.
            Material savedSkybox = RenderSettings.skybox;
            AmbientMode savedMode = RenderSettings.ambientMode;
            SphericalHarmonicsL2 savedProbe = RenderSettings.ambientProbe;

            var baked = new List<string>();
            var skipped = new List<string>();

            try
            {
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var config = AssetDatabase.LoadAssetAtPath<LocationConfig>(path);
                    if (config == null)
                        continue;

                    if (!config.OverrideAtmosphere || config.SkyboxMaterial == null)
                    {
                        skipped.Add($"{config.name} (no skybox override)");
                        continue;
                    }

                    RenderSettings.ambientMode = AmbientMode.Skybox;
                    RenderSettings.skybox = config.SkyboxMaterial;
                    DynamicGI.UpdateEnvironment();

                    SphericalHarmonicsL2 probe = RenderSettings.ambientProbe;
                    if (IsDegenerate(probe))
                    {
                        skipped.Add($"{config.name} (probe came back empty - is the skybox material valid?)");
                        continue;
                    }

                    probe = Grade(probe, config.AmbientSaturation, config.AmbientIntensity);

                    Undo.RecordObject(config, "Bake Location Ambient");
                    config.SetBakedAmbientProbe(probe);
                    EditorUtility.SetDirty(config);
                    baked.Add($"{config.name} (L0 rgb {probe[0, 0]:F3}/{probe[1, 0]:F3}/{probe[2, 0]:F3})");
                }

                AssetDatabase.SaveAssets();
            }
            finally
            {
                RenderSettings.skybox = savedSkybox;
                RenderSettings.ambientMode = savedMode;
                RenderSettings.ambientProbe = savedProbe;
                DynamicGI.UpdateEnvironment();
            }

            Debug.Log($"[LocationAmbientBake] Baked {baked.Count}: {string.Join(", ", baked)}" +
                      (skipped.Count > 0 ? $" | Skipped {skipped.Count}: {string.Join(", ", skipped)}" : ""));
        }

        /// <summary>
        /// Art-directs the sky's ambient without touching the sky itself. A saturated
        /// gradient sky bakes to a heavily tinted probe, and because that probe is the
        /// only fill light in the scene it re-tints every surface it lands on - the
        /// player included. Pulling the probe towards its own luma decouples "what the
        /// sky looks like" from "what colour the world is lit by".
        ///
        /// Saturation is a lerp towards luma and intensity is a scale; both are linear
        /// and per-channel-independent of direction, so applying them to the SH
        /// coefficients is identical to applying them to the result for every normal.
        /// </summary>
        private static SphericalHarmonicsL2 Grade(SphericalHarmonicsL2 sh, float saturation, float intensity)
        {
            if (Mathf.Approximately(saturation, 1f) && Mathf.Approximately(intensity, 1f))
                return sh;

            var graded = new SphericalHarmonicsL2();
            for (int coeff = 0; coeff < 9; coeff++)
            {
                float r = sh[0, coeff];
                float g = sh[1, coeff];
                float b = sh[2, coeff];
                float luma = 0.2126f * r + 0.7152f * g + 0.0722f * b;

                graded[0, coeff] = Mathf.Lerp(luma, r, saturation) * intensity;
                graded[1, coeff] = Mathf.Lerp(luma, g, saturation) * intensity;
                graded[2, coeff] = Mathf.Lerp(luma, b, saturation) * intensity;
            }
            return graded;
        }

        /// <summary>An all-zero probe means the bake did not actually take.</summary>
        private static bool IsDegenerate(SphericalHarmonicsL2 sh)
        {
            for (int rgb = 0; rgb < 3; rgb++)
            {
                for (int coeff = 0; coeff < 9; coeff++)
                {
                    if (Mathf.Abs(sh[rgb, coeff]) > 1e-6f)
                        return false;
                }
            }
            return true;
        }
    }
}
