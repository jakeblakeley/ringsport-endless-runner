using UnityEditor;
using UnityEngine;

namespace RingSport.Editor
{
    /// <summary>
    /// One-shot diagnostic for the floating-feet-at-home bug: poses the Player
    /// prefab in the idle and run cycles and logs where the paws (renderer
    /// bounds bottom) sit relative to the player root plane, plus the capsule
    /// bottom and Dog Model offset for context. Read the numbers out of
    /// Logs/Editor.log; re-run from Tools/RingSport/Probe Dog Ground.
    /// </summary>
    public static class DogGroundProbe
    {
        private const int ProbeVersion = 1;
        private const string VersionPrefKey = "RingSport.DogGroundProbe.Version";

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

            if (EditorPrefs.GetInt(VersionPrefKey, 0) >= ProbeVersion)
                return;

            EditorPrefs.SetInt(VersionPrefKey, ProbeVersion);
            Run();
        }

        [MenuItem("Tools/RingSport/Probe Dog Ground")]
        public static void Run()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            if (prefab == null)
            {
                Debug.LogError("[DogGroundProbe] No Player prefab.");
                return;
            }

            GameObject dog = Object.Instantiate(prefab);
            try
            {
                dog.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                foreach (var behaviour in dog.GetComponentsInChildren<Behaviour>(true))
                {
                    if (!(behaviour is Animator))
                        behaviour.enabled = false;
                }

                var capsule = dog.GetComponent<CapsuleCollider>();
                string capsuleInfo = capsule != null
                    ? $"capsule bottom {capsule.center.y - capsule.height * 0.5f:0.###}"
                    : "no capsule on root";

                Transform model = FindChild(dog.transform, "Dog Model");
                string modelInfo = model != null
                    ? $"Dog Model localPos {model.localPosition.y:0.###}, scale {model.localScale.y:0.###}"
                    : "no Dog Model child";

                var animator = dog.GetComponentInChildren<Animator>(true);
                if (animator == null)
                {
                    Debug.LogError("[DogGroundProbe] No animator.");
                    return;
                }

                animator.Update(0f);
                animator.SetFloat("MoveSpeed", 0f);
                float idleMin = float.MaxValue, idleMax = float.MinValue;
                for (int i = 0; i < 12; i++)
                {
                    animator.Update(0.15f);
                    float y = BoundsMinY(dog);
                    idleMin = Mathf.Min(idleMin, y);
                    idleMax = Mathf.Max(idleMax, y);
                }

                animator.SetFloat("MoveSpeed", 1f);
                for (int i = 0; i < 10; i++)
                    animator.Update(0.1f); // let the damped blend settle into the run
                float runMin = float.MaxValue, runMax = float.MinValue;
                for (int i = 0; i < 16; i++)
                {
                    animator.Update(0.06f);
                    float y = BoundsMinY(dog);
                    runMin = Mathf.Min(runMin, y);
                    runMax = Mathf.Max(runMax, y);
                }

                Debug.Log($"[DogGroundProbe] paw bottom vs root plane - idle: {idleMin:0.###}..{idleMax:0.###}, " +
                          $"run cycle: {runMin:0.###}..{runMax:0.###}; {capsuleInfo}; {modelInfo}");
            }
            finally
            {
                Object.DestroyImmediate(dog);
            }
        }

        private static float BoundsMinY(GameObject root)
        {
            float min = float.MaxValue;
            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>())
                min = Mathf.Min(min, renderer.bounds.min.y);
            return min;
        }

        private static Transform FindChild(Transform root, string name)
        {
            if (root.name == name)
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChild(root.GetChild(i), name);
                if (found != null)
                    return found;
            }
            return null;
        }
    }
}
