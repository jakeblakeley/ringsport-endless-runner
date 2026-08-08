using RingSport.Core;
using RingSport.Player;
using UnityEditor;
using UnityEngine;

namespace RingSport.Editor
{
    /// <summary>
    /// In-situ hat fitting: enter play mode, wear a hat via the selector, open
    /// Tools > RingSport > Hat Fit Tuner, and drag the worn hat's transform
    /// while it rides the animated dog. "Save to Prefab" writes the live
    /// values onto the hat's prefab ROOT in Resources/Hats - prefab assets
    /// aren't rolled back when play mode ends, so the fit survives.
    ///
    /// Saving also logs the equivalent FitOverrides row: paste that into
    /// HatPrefabBaker so a future BakeVersion bump (which resets every prefab
    /// root to the table) keeps the hand fit.
    /// </summary>
    public class HatFitTunerWindow : EditorWindow
    {
        private const string ResourcesHatFolder = "Assets/Resources/Hats";

        [MenuItem("Tools/RingSport/Hat Fit Tuner")]
        public static void Open()
        {
            GetWindow<HatFitTunerWindow>("Hat Fit Tuner");
        }

        private void OnEnable()
        {
            // The worn hat moves every frame - keep the fields current
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        private void OnGUI()
        {
            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Enter play mode, then wear the hat you want to fit (hat selector on the home screen, " +
                    "or Debug > Unlock All Hats first).", MessageType.Info);
                return;
            }

            var equipper = FindAnyObjectByType<HatEquipper>();
            Transform worn = equipper != null ? equipper.WornHat : null;
            string id = HatManager.SelectedId;

            if (worn == null || string.IsNullOrEmpty(id))
            {
                EditorGUILayout.HelpBox("No hat worn right now - pick one in the selector.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Fitting", id, EditorStyles.boldLabel);
            EditorGUILayout.Space(4f);

            // Live-edit the instance: +Y is up off the skull, +Z is toward the
            // nose, in the head anchor's frame
            EditorGUI.BeginChangeCheck();
            Vector3 position = EditorGUILayout.Vector3Field("Local Position (Y up, Z nose)", worn.localPosition);
            Vector3 euler = EditorGUILayout.Vector3Field("Local Rotation", NormalizeEuler(worn.localEulerAngles));
            float scale = EditorGUILayout.FloatField("Uniform Scale", worn.localScale.x);
            if (EditorGUI.EndChangeCheck())
            {
                worn.localPosition = position;
                worn.localRotation = Quaternion.Euler(euler);
                worn.localScale = Vector3.one * Mathf.Max(0.0001f, scale);
            }

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button($"Save to Prefab ({id})", GUILayout.Height(28f)))
                    SaveToPrefab(id, worn);
                if (GUILayout.Button("Log FitOverrides Row", GUILayout.Height(28f)))
                    Debug.Log($"[HatFitTuner] {FitRowFor(id, worn)}");
            }

            EditorGUILayout.HelpBox(
                "Save to Prefab persists through play-mode exit. Then paste the logged FitOverrides row into " +
                "HatPrefabBaker - a BakeVersion bump resets prefab roots to that table.", MessageType.None);
        }

        /// <summary>Writes the live TRS onto the prefab asset's root - the part of play mode that sticks.</summary>
        private static void SaveToPrefab(string id, Transform worn)
        {
            string path = $"{ResourcesHatFolder}/{id}.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
            {
                Debug.LogError($"[HatFitTuner] No prefab at {path}.");
                return;
            }

            try
            {
                root.transform.localPosition = worn.localPosition;
                root.transform.localRotation = worn.localRotation;
                root.transform.localScale = worn.localScale;
                PrefabUtility.SaveAsPrefabAsset(root, path);
                Debug.Log($"[HatFitTuner] Saved '{id}' fit to {path}.\n[HatFitTuner] {FitRowFor(id, worn)}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>
        /// Inverts the baker's ApplyFit math so the hand fit can live in the
        /// FitOverrides table: measure the model's bounds at this rotation,
        /// then express the TRS as width / baseY / forwardZ.
        /// </summary>
        private static string FitRowFor(string id, Transform worn)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ResourcesHatFolder}/{id}.prefab");
            if (prefab == null)
                return $"(no prefab found for '{id}')";

            var temp = Object.Instantiate(prefab);
            try
            {
                temp.transform.SetPositionAndRotation(Vector3.zero, worn.localRotation);
                temp.transform.localScale = Vector3.one;

                var renderers = temp.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                    return $"(no renderers found for '{id}')";
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    bounds.Encapsulate(renderers[i].bounds);

                float s = worn.localScale.x;
                float width = bounds.size.x * s;
                float baseY = worn.localPosition.y + bounds.min.y * s;
                float forwardZ = worn.localPosition.z + bounds.center.z * s;
                Vector3 euler = NormalizeEuler(worn.localRotation.eulerAngles);

                string row = $"{{ \"{id}\", new Fit({width:0.###}f, {baseY:0.###}f, {forwardZ:0.###}f, " +
                             $"{euler.y:0.#}f, {euler.x:0.#}f, {euler.z:0.#}f) }},";

                float expectedX = -bounds.center.x * s;
                if (Mathf.Abs(worn.localPosition.x - expectedX) > 0.005f)
                    row += "  // NOTE: X offset isn't representable in the Fit table - it lives only on the prefab";
                return row;
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }
        }

        private static Vector3 NormalizeEuler(Vector3 euler)
        {
            euler.x = Mathf.Repeat(euler.x + 180f, 360f) - 180f;
            euler.y = Mathf.Repeat(euler.y + 180f, 360f) - 180f;
            euler.z = Mathf.Repeat(euler.z + 180f, 360f) - 180f;
            return euler;
        }
    }
}
