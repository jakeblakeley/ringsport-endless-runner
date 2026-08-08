using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RingSport.Editor
{
    /// <summary>
    /// Perf audit fix #3: every scrolled prefab with a collider gets one
    /// kinematic Rigidbody at its root. The whole world moves past the player
    /// by transform writes, and a moved collider WITHOUT a rigidbody is
    /// PhysX's slow path - the engine re-syncs it as a modified static
    /// collider every physics step, for 60-120 live objects at once.
    /// A kinematic body is the fast path for exactly this pattern.
    ///
    /// ContinuousSpeculative keeps thin-collider trigger overlaps reliable at
    /// the 30Hz fixed step this project now runs.
    ///
    /// Sweep rule: prefabs under Assets/Prefabs with at least one Collider
    /// and NO existing Rigidbody/CharacterController anywhere (skips Player,
    /// ragdolls, Steak, anything already physical). Re-runnable: touch
    /// PerfReports/kinematic_request.txt or bump SetupVersion.
    /// </summary>
    public static class ScrolledColliderBodies
    {
        private const int SetupVersion = 2;
        private const string VersionPrefKey = "RingSport.ScrolledColliderBodies.Version";
        private const string MarkerPath = "PerfReports/kinematic_request.txt";
        private static double nextPoll;

        [InitializeOnLoadMethod]
        private static void AutoRunOnLoad()
        {
            // update-delegate poll, not delayCall - delayCall starves when
            // this machine's editor wedges (same lesson as TexturePayloadBake)
            EditorApplication.update += PollAutoRun;
        }

        private static void PollAutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll)
                return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;

            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                return;
            }

            bool marker = File.Exists(MarkerPath);
            if (!marker && EditorPrefs.GetInt(VersionPrefKey, 0) >= SetupVersion)
                return;
            if (marker)
                File.Delete(MarkerPath);

            try
            {
                Run();
                EditorPrefs.SetInt(VersionPrefKey, SetupVersion);
            }
            catch (Exception e)
            {
                Debug.LogError($"[ScrolledColliderBodies] Failed (retrying in 30s): {e}");
                nextPoll = EditorApplication.timeSinceStartup + 30.0;
            }
        }

        [MenuItem("Tools/RingSport/Add Kinematic Bodies To Scrolled Colliders")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[ScrolledColliderBodies] Exit play mode first.");
                return;
            }

            int changed = 0;
            int skippedPhysical = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    // v2 rule: what matters is whether a collider ATTACHES to a
                    // body - a collider with no Rigidbody/CharacterController
                    // anywhere up its chain rides the prefab root as a moving
                    // static collider. Checking for "any RB in children" (v1)
                    // wrongly skipped Palisade, whose nested LifePickup had
                    // just received its own body earlier in the same sweep.
                    bool needsBody = false;
                    foreach (Collider col in contents.GetComponentsInChildren<Collider>(true))
                    {
                        bool attached = false;
                        for (Transform t = col.transform; t != null; t = t.parent)
                        {
                            if (t.GetComponent<Rigidbody>() != null ||
                                t.GetComponent<CharacterController>() != null)
                            {
                                attached = true;
                                break;
                            }
                        }

                        if (!attached)
                        {
                            needsBody = true;
                            break;
                        }
                    }

                    if (!needsBody)
                    {
                        if (contents.GetComponentsInChildren<Collider>(true).Length > 0)
                            skippedPhysical++;
                        continue;
                    }

                    if (contents.GetComponent<Rigidbody>() != null)
                        continue; // root already has one; colliders above were deeper strays

                    var body = contents.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    body.useGravity = false;
                    body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    body.interpolation = RigidbodyInterpolation.None;

                    PrefabUtility.SaveAsPrefabAsset(contents, path);
                    changed++;
                    Debug.Log($"[ScrolledColliderBodies] Kinematic body added to {path}");
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            Debug.Log($"[ScrolledColliderBodies] Done: {changed} prefab(s) got kinematic bodies, " +
                      $"{skippedPhysical} already physical, rest had no colliders.");
        }
    }
}
