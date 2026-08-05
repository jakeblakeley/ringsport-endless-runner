using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace RingSport.EditorTools
{
    /// <summary>
    /// One-shot fixes from the 2026-08 WebGL perf audit that need editor APIs
    /// (scene surgery, nested-prefab overrides, PlayerSettings). Triggered by a
    /// marker file so the whole batch can run unattended; queues the "after"
    /// perf measurement when done. Safe to delete once applied.
    /// </summary>
    [InitializeOnLoad]
    public static class PerfAuditOneShots
    {
        private const string MarkerPath = "PerfReports/apply_oneshots_request.txt";
        private const string PerfMarkerPath = "PerfReports/pending_request.json";
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private static double nextCheck;

        static PerfAuditOneShots()
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
            Apply();
        }

        [MenuItem("Tools/Perf/Apply Audit One-Shots")]
        private static void Apply()
        {
            bool ok = true;

            // 1. Remove duplicate ArcEffectController components (the Level GO
            //    carries two with conflicting useFixedYPosition - two global
            //    uniform writes per frame and execution-order-dependent Y).
            try
            {
                var scene = EditorSceneManager.GetActiveScene();
                if (scene.path != ScenePath)
                    scene = EditorSceneManager.OpenScene(ScenePath);

                int removed = 0;
                var controllers = Object.FindObjectsByType<RingSport.Level.ArcEffectController>(
                    FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
                for (int i = 0; i < controllers.Length; i++)
                {
                    if (controllers[i] == null)
                        continue;
                    var others = controllers[i].GetComponents<RingSport.Level.ArcEffectController>();
                    for (int j = 1; j < others.Length; j++)
                    {
                        Object.DestroyImmediate(others[j], true);
                        removed++;
                    }
                }

                if (removed > 0)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                Debug.Log($"[PerfAuditOneShots] Duplicate ArcEffectControllers removed: {removed}");
            }
            catch (System.Exception e)
            {
                ok = false;
                Debug.LogError($"[PerfAuditOneShots] Scene fix failed: {e.Message}");
            }

            // 2. Collectibles never cast shadows (their renderers live on nested
            //    .glb instances, so the flag needs a real prefab override).
            foreach (string path in new[]
                     {
                         "Assets/Prefabs/Collectibles/Coin.prefab",
                         "Assets/Prefabs/Collectibles/MegaCollectible.prefab",
                     })
            {
                try
                {
                    var root = PrefabUtility.LoadPrefabContents(path);
                    int changed = 0;
                    foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    {
                        if (r.shadowCastingMode != ShadowCastingMode.Off)
                        {
                            r.shadowCastingMode = ShadowCastingMode.Off;
                            changed++;
                        }
                    }
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    PrefabUtility.UnloadPrefabContents(root);
                    Debug.Log($"[PerfAuditOneShots] {path}: {changed} renderer(s) set to no shadows");
                }
                catch (System.Exception e)
                {
                    ok = false;
                    Debug.LogError($"[PerfAuditOneShots] Prefab fix failed for {path}: {e.Message}");
                }
            }

            // 3. Brotli needs correct Content-Encoding headers from the host;
            //    the fallback decompressor makes the build load anywhere.
            try
            {
                PlayerSettings.WebGL.decompressionFallback = true;
                AssetDatabase.SaveAssets();
                Debug.Log("[PerfAuditOneShots] WebGL decompression fallback enabled");
            }
            catch (System.Exception e)
            {
                ok = false;
                Debug.LogError($"[PerfAuditOneShots] PlayerSettings fix failed: {e.Message}");
            }

            Debug.Log($"[PerfAuditOneShots] Done (ok={ok}) - queueing 'after' perf run");
            Directory.CreateDirectory("PerfReports");
            File.WriteAllText(PerfMarkerPath, "{\"label\":\"after\",\"durationSeconds\":60}");
        }
    }
}
