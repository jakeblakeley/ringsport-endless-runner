using System.IO;
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
            EditorApplication.update += RepaintWhilePlaying;
        }

        private void OnDisable()
        {
            EditorApplication.update -= RepaintWhilePlaying;
        }

        // The worn hat moves every frame in play mode - keep the fields
        // current. Out of play mode the window is a static help box, and an
        // unconditional every-tick Repaint left the editor repainting at
        // full tilt whenever the window stayed open.
        private void RepaintWhilePlaying()
        {
            if (EditorApplication.isPlaying)
                Repaint();
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

            // Live preview of the HideEars flag - the saved value lives on the
            // hat's catalog row in HatManager.cs
            EditorGUI.BeginChangeCheck();
            bool hideEars = EditorGUILayout.Toggle("Hide Ears", equipper.EarsHidden);
            if (EditorGUI.EndChangeCheck())
                equipper.SetEarsHiddenLive(hideEars);

            EditorGUILayout.Space(8f);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button($"Save to Prefab ({id})", GUILayout.Height(28f)))
                {
                    SaveToPrefab(id, worn);
                    // Overlay makes re-equips honor the choice for the rest of
                    // this session; the queue lands it in HatManager.cs source
                    // once play mode ends.
                    HatManager.SetHideEarsOverride(id, hideEars);
                    HatHideEarsPersistence.Queue(id, hideEars);
                }
                if (GUILayout.Button("Log FitOverrides Row", GUILayout.Height(28f)))
                    Debug.Log($"[HatFitTuner] {FitRowFor(id, worn)}");
            }

            EditorGUILayout.HelpBox(
                "Save to Prefab persists the fit AND the Hide Ears choice. The transform writes to the prefab " +
                "immediately; the ear flag edits the hat's catalog row in HatManager.cs when you exit play mode " +
                "(writing source mid-play would recompile and wipe the session). Paste the logged FitOverrides " +
                "row into HatPrefabBaker so BakeVersion bumps keep the fit.", MessageType.None);
        }

        /// <summary>
        /// Persists the Hide Ears choice by editing the hat's catalog row in
        /// HatManager.cs (adds/removes the trailing "hideEars: true" arg).
        /// Called by HatHideEarsPersistence outside play mode; the flag goes
        /// live on the recompile the edit triggers.
        /// </summary>
        internal static void SaveHideEarsToCatalog(string id, bool hideEars)
        {
            const string catalogPath = "Assets/Scripts/Core/HatManager.cs";
            string fullPath = Path.GetFullPath(catalogPath);
            if (!File.Exists(fullPath))
            {
                Debug.LogError($"[HatFitTuner] Missing {catalogPath} - Hide Ears not saved.");
                return;
            }

            string[] lines = File.ReadAllLines(fullPath);
            string marker = $"new HatDef(\"{id}\",";
            int lineIndex = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(marker))
                {
                    lineIndex = i;
                    break;
                }
            }

            if (lineIndex < 0)
            {
                Debug.LogError($"[HatFitTuner] No catalog row found for '{id}' in {catalogPath}.");
                return;
            }

            string line = lines[lineIndex];
            bool hasFlag = line.Contains("hideEars: true");
            if (hideEars == hasFlag)
                return;

            if (hideEars)
            {
                int close = line.LastIndexOf(')');
                if (close < 0)
                {
                    Debug.LogError($"[HatFitTuner] Couldn't parse the catalog row for '{id}' - edit HatManager.cs by hand.");
                    return;
                }
                line = line.Substring(0, close) + ", hideEars: true" + line.Substring(close);
            }
            else
            {
                line = line.Replace(", hideEars: true", "");
            }

            lines[lineIndex] = line;
            File.WriteAllLines(fullPath, lines);
            Debug.Log($"[HatFitTuner] Catalog row updated: '{id}' hideEars -> {(hideEars ? "true" : "false")}. " +
                      "Takes effect after the next script recompile.");
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

    /// <summary>
    /// Carries the tuner's Hide Ears choices from play mode to a moment when
    /// editing HatManager.cs is safe. Writing source DURING play triggers a
    /// recompile-and-continue domain reload that wipes the session (and was
    /// why the old separate save button was a trap - fits saved but ears
    /// never did). Choices are queued in EditorPrefs by Save to Prefab and
    /// flushed on play-mode exit; the editor-boot flush covers quitting Unity
    /// straight from play mode.
    /// </summary>
    [InitializeOnLoad]
    internal static class HatHideEarsPersistence
    {
        private const string PendingIdsKey = "RingSport.HatFitTuner.PendingHideEarsIds";
        private const string PendingPrefix = "RingSport.HatFitTuner.PendingHideEars.";

        static HatHideEarsPersistence()
        {
            EditorApplication.playModeStateChanged += change =>
            {
                if (change == PlayModeStateChange.EnteredEditMode)
                    Flush();
            };
            EditorApplication.delayCall += () =>
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode)
                    Flush();
            };
        }

        /// <summary>
        /// Records the desired Hide Ears state for a hat. Always queue the
        /// current state - a later save overwrites an earlier one, and the
        /// flush no-ops when the catalog already matches.
        /// </summary>
        public static void Queue(string id, bool hideEars)
        {
            EditorPrefs.SetBool(PendingPrefix + id, hideEars);
            string joined = EditorPrefs.GetString(PendingIdsKey, "");
            var ids = new System.Collections.Generic.List<string>(
                joined.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries));
            if (!ids.Contains(id))
                ids.Add(id);
            EditorPrefs.SetString(PendingIdsKey, string.Join(";", ids));
            Debug.Log($"[HatFitTuner] Hide Ears for '{id}' -> {hideEars}; live for this session, " +
                      "catalog row in HatManager.cs updates when play mode ends.");
        }

        private static void Flush()
        {
            string joined = EditorPrefs.GetString(PendingIdsKey, "");
            if (string.IsNullOrEmpty(joined))
                return;

            foreach (string id in joined.Split(new[] { ';' }, System.StringSplitOptions.RemoveEmptyEntries))
            {
                HatFitTunerWindow.SaveHideEarsToCatalog(id, EditorPrefs.GetBool(PendingPrefix + id, false));
                EditorPrefs.DeleteKey(PendingPrefix + id);
            }
            EditorPrefs.DeleteKey(PendingIdsKey);
        }
    }
}
