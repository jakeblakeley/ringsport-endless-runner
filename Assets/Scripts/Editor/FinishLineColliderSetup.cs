using RingSport.Level;
using UnityEditor;
using UnityEngine;

namespace RingSport.Editor
{
    /// <summary>
    /// The finish line floor's trigger is the only thing that ends a running
    /// level, so a finish line prefab without a collider makes its levels run
    /// past the line forever - no reward screen, no next level. Arizona's
    /// prefab shipped without one, which is why the level 5 face attack could
    /// never complete: the dog carried the decoy over a finish line that had
    /// nothing to collide with.
    ///
    /// This repairs any finish line prefab missing its collider, matching the
    /// box France, Seattle and Oregon all use: a 10x10x10 trigger centred at
    /// y 5, i.e. a volume tall enough that a jumping dog can't clear it.
    ///
    /// Runs automatically once after compilation (version-gated); re-run from
    /// Tools/RingSport/Fix Finish Line Colliders.
    /// </summary>
    public static class FinishLineColliderSetup
    {
        // Bump to force the auto-run to re-apply the setup
        private const int SetupVersion = 1;
        private const string VersionPrefKey = "RingSport.FinishLineColliderSetup.Version";

        private static readonly Vector3 TriggerSize = new Vector3(10f, 10f, 10f);
        private static readonly Vector3 TriggerCenter = new Vector3(0f, 5f, 0f);

        [InitializeOnLoadMethod]
        private static void AutoRunOnLoad()
        {
            EditorApplication.delayCall += TryAutoRun;
        }

        private static void TryAutoRun()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryAutoRun;
                return;
            }

            if (EditorPrefs.GetInt(VersionPrefKey, 0) >= SetupVersion)
                return;

            try
            {
                Run();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[FinishLineColliderSetup] Auto-setup failed (will retry when the editor is idle): {e}");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Fix Finish Line Colliders")]
        public static void Run()
        {
            int inspected = 0;
            int repaired = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.GetComponentInChildren<FinishLineFloor>(true) == null)
                    continue;

                inspected++;
                if (prefab.GetComponentInChildren<Collider>(true) != null)
                    continue;

                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    // The collider must live on the same GameObject as the
                    // script - Unity delivers OnTriggerEnter to the collider's
                    // object, not up the hierarchy
                    var floor = root.GetComponentInChildren<FinishLineFloor>(true);
                    var box = floor.gameObject.AddComponent<BoxCollider>();
                    box.isTrigger = true;
                    box.size = TriggerSize;
                    box.center = TriggerCenter;
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                repaired++;
                Debug.Log($"[FinishLineColliderSetup] Added the missing finish line trigger to {path}");
            }

            AssetDatabase.SaveAssets();
            EditorPrefs.SetInt(VersionPrefKey, SetupVersion);
            Debug.Log($"[FinishLineColliderSetup] Done (v{SetupVersion}). Inspected {inspected} finish line prefab(s), repaired {repaired}.");
        }
    }
}
