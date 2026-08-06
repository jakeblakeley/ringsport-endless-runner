using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using TMPro;
using RingSport.Core;
using RingSport.Level;
using RingSport.UI;

namespace RingSport.Editor
{
    /// <summary>
    /// Scene + asset wiring for the face attack mini level, and the one-time
    /// removal of the scrapped decoy battle:
    /// - wires the shared Decoy prefab, the Barlow-Bold banner font and the
    ///   Permanent Marker font (the QTE X stamps) into MiniLevelFaceAttack
    ///   on the scene's MiniGames object
    /// - strips the missing-script component left behind by the deleted
    ///   MiniLevelDecoyBattle, and its entry in MiniLevelManager's serialized
    ///   config list
    /// - migrates the level assets: Level 5 "Ring 2 - 2" FleeAttack ->
    ///   FaceAttack (easy) and Level 8 "Ring 3 Finale" DecoyBattle ->
    ///   FaceAttack (hard). Only values still holding the OLD type are
    ///   touched, so later hand-edits stick.
    /// Runs automatically after script compilation; idempotent.
    /// </summary>
    public static class FaceAttackSetup
    {
        private const string DecoyPrefabPath = "Assets/Prefabs/Decoy.prefab";
        private const string BarlowBoldFontGuid = "099dce98fb9fd47cb8ff1abc60bfba4c";     // Barlow-Bold SDF.asset
        private const string PermanentMarkerFontGuid = "57d07bf02cfe044d68e15a878f8d1b5a"; // PermanentMarker-Regular SDF.asset

        private const string Level5Path = "Assets/LevelsData/Level Data/Level5 - Ring 2 Leg 2.asset";
        private const string Level8Path = "Assets/LevelsData/Level Data/Level8 Ring3 Finale.asset";

        // Raw serialized values (DecoyBattle no longer exists in the enum)
        private const int FaceAttackValue = 1;
        private const int FleeAttackValue = 2;
        private const int DecoyBattleValue = 3;

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
                Debug.LogWarning($"[FaceAttackSetup] Auto-run failed ({e.Message}) - re-queueing");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Setup Face Attack")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[FaceAttackSetup] Cannot run during play mode - exit play mode first (the auto-run will then apply it).");
                return;
            }

            // Level asset migration is scene-independent
            bool assetsChanged = MigrateLevelAsset(Level5Path, FleeAttackValue);
            assetsChanged |= MigrateLevelAsset(Level8Path, DecoyBattleValue);
            if (assetsChanged)
                AssetDatabase.SaveAssets();

            // Scene wiring needs the mini-games scene open
            var faceAttack = Object.FindAnyObjectByType<MiniLevelFaceAttack>(FindObjectsInactive.Include);
            if (faceAttack == null)
            {
                // The component ships in the scene; recreate it if it was lost
                var fleeAttack = Object.FindAnyObjectByType<MiniLevelFleeAttack>(FindObjectsInactive.Include);
                if (fleeAttack == null)
                    return; // scene with the mini levels isn't open
                faceAttack = fleeAttack.gameObject.AddComponent<MiniLevelFaceAttack>();
                Debug.Log($"[FaceAttackSetup] Added MiniLevelFaceAttack to '{fleeAttack.gameObject.name}'.");
                EditorSceneManager.MarkSceneDirty(faceAttack.gameObject.scene);
            }

            bool changed = false;

            // The deleted MiniLevelDecoyBattle leaves a missing-script slot on
            // the MiniGames object - strip it
            int removed = GameObjectUtility.RemoveMonoBehavioursWithMissingScript(faceAttack.gameObject);
            if (removed > 0)
            {
                changed = true;
                Debug.Log($"[FaceAttackSetup] Removed {removed} missing script(s) (the deleted decoy battle) from '{faceAttack.gameObject.name}'.");
            }

            changed |= WireFaceAttack(faceAttack);
            changed |= CleanMiniLevelManagerConfigs();

            if (!changed)
                return;

            bool sceneWasDirty = faceAttack.gameObject.scene.isDirty;
            EditorSceneManager.MarkSceneDirty(faceAttack.gameObject.scene);
            if (!sceneWasDirty)
                EditorSceneManager.SaveScene(faceAttack.gameObject.scene);
            else
                Debug.Log("[FaceAttackSetup] Scene had unsaved changes - face attack wired but scene NOT auto-saved. Save it when ready.");

            Debug.Log("[FaceAttackSetup] Face attack wired (decoy prefab + banner font + marker font).");
        }

        private static bool WireFaceAttack(MiniLevelFaceAttack faceAttack)
        {
            var decoyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DecoyPrefabPath);
            if (decoyPrefab == null)
                Debug.LogWarning("[FaceAttackSetup] Decoy prefab not found - run Tools > RingSport > Setup Decoy first; the face attack will use the placeholder sphere until wired.");

            var bannerFont = LoadFont(BarlowBoldFontGuid, "Barlow-Bold SDF");
            var markerFont = LoadFont(PermanentMarkerFontGuid, "PermanentMarker-Regular SDF");

            var so = new SerializedObject(faceAttack);
            bool changed = false;
            changed |= SetReference(so, "decoyPrefab", decoyPrefab);
            changed |= SetReference(so, "bannerFont", bannerFont);
            changed |= SetReference(so, "markerFont", markerFont);

            // Camera tuning migrations ("even closer", 2026-07-29): a C#
            // default edit never reaches an already-open scene instance
            // (domain reload restores the live value), so stale-default
            // values are migrated here. Hand-tuned values are left alone.
            changed |= MigrateFloat(so, "pounceCamBehindDog", 1.35f, 1f);
            changed |= MigrateFloat(so, "pounceCamSideOffset", 1.1f, 0.85f);
            changed |= MigrateFloat(so, "pounceCamHeight", 0.2f, 0.15f);

            // Push the standoff shot in harder (2026-08-06). The QTE beat is
            // only 1.4-2s, so the creep speed is most of what decides how far
            // the shot travels; the stop distance comes in with it so the
            // extra travel isn't clamped away on a close bite. Both old
            // values are migrated so a scene at either generation lands here.
            changed |= MigrateFloat(so, "pounceCamCreepSpeed", 0.25f, 0.45f);
            changed |= MigrateFloat(so, "pounceCamMinDecoyDistance", 1.7f, 1.2f);
            changed |= MigrateFloat(so, "pounceCamMinDecoyDistance", 1.35f, 1.2f);

            if (changed)
                so.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        }

        private static bool MigrateFloat(SerializedObject so, string propertyName, float oldDefault, float newValue)
        {
            var prop = so.FindProperty(propertyName);
            if (prop == null || !Mathf.Approximately(prop.floatValue, oldDefault))
                return false;
            prop.floatValue = newValue;
            return true;
        }

        private static TMP_FontAsset LoadFont(string guid, string label)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var font = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (font == null)
                Debug.LogWarning($"[FaceAttackSetup] {label} font asset not found - the face attack will fall back to the TMP default font.");
            return font;
        }

        private static bool SetReference(SerializedObject so, string propertyName, Object value)
        {
            var prop = so.FindProperty(propertyName);
            if (prop == null || value == null || prop.objectReferenceValue == value)
                return false;
            prop.objectReferenceValue = value;
            return true;
        }

        /// <summary>
        /// Drops the decoy battle entry from MiniLevelManager's serialized
        /// config list and refreshes the face attack instructions if they
        /// still carry the old placeholder text.
        /// </summary>
        private static bool CleanMiniLevelManagerConfigs()
        {
            var manager = Object.FindAnyObjectByType<MiniLevelManager>(FindObjectsInactive.Include);
            if (manager == null)
                return false;

            var so = new SerializedObject(manager);
            var configs = so.FindProperty("miniLevelConfigs");
            if (configs == null || !configs.isArray)
                return false;

            bool changed = false;
            for (int i = configs.arraySize - 1; i >= 0; i--)
            {
                var element = configs.GetArrayElementAtIndex(i);
                var typeProp = element.FindPropertyRelative("type");
                if (typeProp == null)
                    continue;

                if (typeProp.intValue == DecoyBattleValue)
                {
                    configs.DeleteArrayElementAtIndex(i);
                    changed = true;
                    Debug.Log("[FaceAttackSetup] Removed the Decoy Battle entry from MiniLevelManager's configs.");
                }
                else if (typeProp.intValue == FaceAttackValue)
                {
                    var instructions = element.FindPropertyRelative("instructions");
                    if (instructions != null && instructions.stringValue == "Attack the man in the correct lane")
                    {
                        instructions.stringValue = "Get in his lane, then tap the right limb!";
                        changed = true;
                    }
                }
            }

            if (changed)
                so.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        }

        /// <summary>
        /// One-time migration of a level asset onto the face attack: only
        /// applied while the asset still holds the expected OLD mini level
        /// type, so deliberate later edits are never overwritten.
        /// </summary>
        private static bool MigrateLevelAsset(string path, int expectedOldValue)
        {
            var config = AssetDatabase.LoadAssetAtPath<LevelConfig>(path);
            if (config == null)
            {
                Debug.LogWarning($"[FaceAttackSetup] Level asset not found at {path} - face attack not assigned.");
                return false;
            }

            var so = new SerializedObject(config);
            var typeProp = so.FindProperty("miniLevelType");
            if (typeProp == null || typeProp.intValue != expectedOldValue)
                return false;

            typeProp.intValue = FaceAttackValue;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            Debug.Log($"[FaceAttackSetup] {System.IO.Path.GetFileNameWithoutExtension(path)}: miniLevelType {expectedOldValue} -> FaceAttack.");
            return true;
        }
    }
}
