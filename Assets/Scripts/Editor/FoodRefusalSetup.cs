using RingSport.Core;
using RingSport.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RingSport.Editor
{
    /// <summary>
    /// Points the Food Refusal mini level at its own collectible prefab and
    /// slows the drop.
    ///
    /// The mini level used to borrow the run's MegaCollectible, whose capsule
    /// is twice as tall and stands upright - too generous a catch box for a
    /// straight-on dodging game. FoodRefusalCollectible is the same coin with a
    /// shorter collider and its own angle, so it needs its own pool entry on
    /// the scene's ObjectPooler.
    ///
    /// v3 also opens up the gap between drops and trims the run to suit - see
    /// SpawnInterval and TotalSteaks.
    ///
    /// Runs automatically once after compilation (version-gated so it never
    /// stomps later hand tweaks); re-run from Tools/RingSport/Setup Food Refusal.
    /// </summary>
    public static class FoodRefusalSetup
    {
        private const int SetupVersion = 3;
        private const string VersionPrefKey = "RingSport.FoodRefusalSetup.Version";

        private const string CollectiblePrefabPath = "Assets/Prefabs/Collectibles/FoodRefusalCollectible.prefab";
        private const int CollectiblePoolSize = 5;   // only 3 spawn per run

        // 25% slower than the original 8 u/s
        private const float FallSpeed = 6f;

        // A beat every second put the steak either side of a collectible close
        // enough to pin the player out of the lane it was falling in; 1.35 opens
        // the drops to ~8 units apart
        private const float SpawnInterval = 1.35f;

        // 20 at the wider spacing ran half a minute - 16 puts it back to ~25s
        private const int TotalSteaks = 16;

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

            if (Object.FindAnyObjectByType<MiniLevelFoodRefusal>(FindObjectsInactive.Include) == null)
                return; // not the game scene

            try
            {
                Run();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[FoodRefusalSetup] Auto-setup failed (will retry when the editor is idle): {e}");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Setup Food Refusal")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[FoodRefusalSetup] Cannot run during play mode - exit play mode first.");
                return;
            }

            var miniLevel = Object.FindAnyObjectByType<MiniLevelFoodRefusal>(FindObjectsInactive.Include);
            if (miniLevel == null)
            {
                Debug.LogError("[FoodRefusalSetup] No MiniLevelFoodRefusal in the open scene - open SampleScene first.");
                return;
            }

            bool changed = RegisterCollectiblePool();
            changed |= ConfigureMiniLevel(miniLevel);

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(miniLevel.gameObject.scene);
                if (!string.IsNullOrEmpty(miniLevel.gameObject.scene.path))
                    EditorSceneManager.SaveScene(miniLevel.gameObject.scene);
            }

            EditorPrefs.SetInt(VersionPrefKey, SetupVersion);
            Debug.Log($"[FoodRefusalSetup] Done (v{SetupVersion}), scene {(changed ? "updated" : "already correct")}.");
        }

        /// <summary>Adds the FoodRefusalCollectible pool if it isn't there yet.</summary>
        private static bool RegisterCollectiblePool()
        {
            var pooler = Object.FindAnyObjectByType<ObjectPooler>(FindObjectsInactive.Include);
            if (pooler == null)
            {
                Debug.LogError("[FoodRefusalSetup] No ObjectPooler in the scene - pool not registered.");
                return false;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CollectiblePrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[FoodRefusalSetup] No prefab at '{CollectiblePrefabPath}' - pool not registered.");
                return false;
            }

            var serialized = new SerializedObject(pooler);
            var pools = serialized.FindProperty("pools");
            if (pools == null)
            {
                Debug.LogError("[FoodRefusalSetup] ObjectPooler has no 'pools' list.");
                return false;
            }

            for (int i = 0; i < pools.arraySize; i++)
            {
                var existing = pools.GetArrayElementAtIndex(i);
                if (existing.FindPropertyRelative("tag").stringValue != PoolTags.FoodRefusalCollectible)
                    continue;

                // Already registered - make sure it points at the right prefab
                var prefabProperty = existing.FindPropertyRelative("prefab");
                if (prefabProperty.objectReferenceValue == prefab)
                    return false;

                prefabProperty.objectReferenceValue = prefab;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(pooler);
                Debug.Log($"[FoodRefusalSetup] Repointed pool '{PoolTags.FoodRefusalCollectible}' -> {prefab.name}");
                return true;
            }

            pools.InsertArrayElementAtIndex(pools.arraySize);
            var added = pools.GetArrayElementAtIndex(pools.arraySize - 1);
            added.FindPropertyRelative("tag").stringValue = PoolTags.FoodRefusalCollectible;
            added.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            added.FindPropertyRelative("size").intValue = CollectiblePoolSize;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pooler);

            Debug.Log($"[FoodRefusalSetup] Registered pool '{PoolTags.FoodRefusalCollectible}' ({CollectiblePoolSize}) -> {prefab.name}");
            return true;
        }

        private static bool ConfigureMiniLevel(MiniLevelFoodRefusal miniLevel)
        {
            var serialized = new SerializedObject(miniLevel);
            var tag = serialized.FindProperty("collectiblePoolTag");
            var speed = serialized.FindProperty("fallSpeed");
            var interval = serialized.FindProperty("steakSpawnInterval");
            var count = serialized.FindProperty("totalSteaks");
            bool changed = false;

            if (tag != null && tag.stringValue != PoolTags.FoodRefusalCollectible)
            {
                Debug.Log($"[FoodRefusalSetup] collectiblePoolTag '{tag.stringValue}' -> '{PoolTags.FoodRefusalCollectible}'");
                tag.stringValue = PoolTags.FoodRefusalCollectible;
                changed = true;
            }

            if (speed != null && !Mathf.Approximately(speed.floatValue, FallSpeed))
            {
                Debug.Log($"[FoodRefusalSetup] fallSpeed {speed.floatValue} -> {FallSpeed}");
                speed.floatValue = FallSpeed;
                changed = true;
            }

            if (interval != null && !Mathf.Approximately(interval.floatValue, SpawnInterval))
            {
                Debug.Log($"[FoodRefusalSetup] steakSpawnInterval {interval.floatValue} -> {SpawnInterval}");
                interval.floatValue = SpawnInterval;
                changed = true;
            }

            if (count != null && count.intValue != TotalSteaks)
            {
                Debug.Log($"[FoodRefusalSetup] totalSteaks {count.intValue} -> {TotalSteaks}");
                count.intValue = TotalSteaks;
                changed = true;
            }

            if (changed)
            {
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(miniLevel);
            }

            return changed;
        }
    }
}
