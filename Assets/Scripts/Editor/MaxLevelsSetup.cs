using RingSport.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RingSport.Editor
{
    /// <summary>
    /// Scene migration: the run is 8 levels, one per LevelConfig asset. The
    /// scene's LevelManager was serialized with maxLevels 9 from before the
    /// count was trimmed - "level 9" only replayed the Level8 finale config
    /// (GetLevelConfig clamps to the last entry). Sets the serialized value
    /// to 8 so the game ends at the Ring 3 Finale.
    /// Runs automatically once after compilation (version-gated); re-run from
    /// Tools/RingSport/Set Max Levels (8).
    /// </summary>
    public static class MaxLevelsSetup
    {
        // Bump to force the auto-run to re-apply the setup
        private const int SetupVersion = 1;
        private const string VersionPrefKey = "RingSport.MaxLevelsSetup.Version";

        private const int MaxLevels = 8;

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

            if (Object.FindFirstObjectByType<LevelManager>(FindObjectsInactive.Include) == null)
                return; // not the game scene

            try
            {
                Run();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[MaxLevelsSetup] Auto-setup failed (will retry when the editor is idle): {e}");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Set Max Levels (8)")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[MaxLevelsSetup] Cannot run during play mode - exit play mode first.");
                return;
            }

            var manager = Object.FindFirstObjectByType<LevelManager>(FindObjectsInactive.Include);
            if (manager == null)
            {
                Debug.LogError("[MaxLevelsSetup] No LevelManager in the open scene - open SampleScene first.");
                return;
            }

            var serialized = new SerializedObject(manager);
            var property = serialized.FindProperty("maxLevels");
            if (property == null)
            {
                Debug.LogError("[MaxLevelsSetup] LevelManager has no 'maxLevels' field - nothing changed.");
                return;
            }

            int previous = property.intValue;
            property.intValue = MaxLevels;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);

            // Play mode can begin mid-run: the edit above is safe, but saving
            // the scene now would throw - retry when idle instead
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[MaxLevelsSetup] Play mode started mid-setup - will re-apply when the editor is idle.");
                EditorApplication.delayCall += TryAutoRun;
                return;
            }

            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            if (!string.IsNullOrEmpty(manager.gameObject.scene.path))
                EditorSceneManager.SaveScene(manager.gameObject.scene);

            EditorPrefs.SetInt(VersionPrefKey, SetupVersion);
            Debug.Log($"[MaxLevelsSetup] LevelManager.maxLevels {previous} -> {MaxLevels} (v{SetupVersion}).");
        }
    }
}
