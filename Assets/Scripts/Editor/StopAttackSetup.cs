using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using RingSport.UI;

namespace RingSport.Editor
{
    /// <summary>
    /// Scene wiring for the stop attack mini level: adds a MiniLevelStopAttack
    /// component alongside the scene's MiniLevelFleeAttack (same GameObject -
    /// they share the in-run mini level plumbing) and wires the shared Decoy
    /// prefab (built by Tools > RingSport > Setup Decoy) and the Barlow-Bold
    /// banner font into it. Runs automatically after script compilation when
    /// the component or a wire is missing; idempotent.
    /// </summary>
    public static class StopAttackSetup
    {
        private const string DecoyPrefabPath = "Assets/Prefabs/Decoy.prefab";
        private const string BarlowBoldFontGuid = "099dce98fb9fd47cb8ff1abc60bfba4c"; // Barlow-Bold SDF.asset

        [InitializeOnLoadMethod]
        private static void AutoRun()
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

            try
            {
                Run();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[StopAttackSetup] Auto-run failed ({e.Message}) - re-queueing");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Setup Stop Attack")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[StopAttackSetup] Cannot run during play mode - exit play mode first (the auto-run will then apply it).");
                return;
            }

            // Host the stop attack on the same GameObject as the flee attack -
            // MiniLevelManager and LevelManager discover both via their base
            // classes, and the two share the decoy assets
            var fleeAttack = Object.FindAnyObjectByType<MiniLevelFleeAttack>(FindObjectsInactive.Include);
            if (fleeAttack == null)
                return; // scene with the mini levels isn't open

            bool changed = false;

            var stopAttack = Object.FindAnyObjectByType<MiniLevelStopAttack>(FindObjectsInactive.Include);
            if (stopAttack == null)
            {
                stopAttack = fleeAttack.gameObject.AddComponent<MiniLevelStopAttack>();
                changed = true;
                Debug.Log($"[StopAttackSetup] Added MiniLevelStopAttack to '{fleeAttack.gameObject.name}'.");
            }

            var decoyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DecoyPrefabPath);
            if (decoyPrefab == null)
                Debug.LogWarning("[StopAttackSetup] Decoy prefab not found - run Tools > RingSport > Setup Decoy first; the stop attack will use the placeholder sphere until wired.");

            var fontPath = AssetDatabase.GUIDToAssetPath(BarlowBoldFontGuid);
            var bannerFont = string.IsNullOrEmpty(fontPath) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath);
            if (bannerFont == null)
                Debug.LogWarning("[StopAttackSetup] Barlow-Bold SDF font asset not found - the stop attack banners will use the TMP default font.");

            var so = new SerializedObject(stopAttack);
            var prefabProp = so.FindProperty("decoyPrefab");
            var fontProp = so.FindProperty("bannerFont");

            if (prefabProp != null && decoyPrefab != null && prefabProp.objectReferenceValue != decoyPrefab)
            {
                prefabProp.objectReferenceValue = decoyPrefab;
                changed = true;
            }
            if (fontProp != null && bannerFont != null && fontProp.objectReferenceValue != bannerFont)
            {
                fontProp.objectReferenceValue = bannerFont;
                changed = true;
            }

            if (!changed)
                return;

            bool sceneWasDirty = stopAttack.gameObject.scene.isDirty;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(stopAttack.gameObject.scene);
            if (!sceneWasDirty)
                EditorSceneManager.SaveScene(stopAttack.gameObject.scene);
            else
                Debug.Log("[StopAttackSetup] Scene had unsaved changes - stop attack wired but scene NOT auto-saved. Save it when ready.");

            Debug.Log("[StopAttackSetup] MiniLevelStopAttack wired (decoy prefab + banner font).");
        }
    }
}
