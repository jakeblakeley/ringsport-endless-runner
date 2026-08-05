using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace RingSport.Editor
{
    /// <summary>
    /// Turns every styled scene button into a pill: fully round left/right ends
    /// instead of the softly-rounded rectangle they used before.
    ///
    /// The old "rounded 9" sprite can't do this. Its 9-slice border is 80px but
    /// the corner arc only has a ~72px radius, and Unity shrinks slices that
    /// don't fit the rect - so the corner can never reach half the button
    /// height. "pill.png" is a full-bleed circle sliced straight down the
    /// middle (border = 128 = half the 256px texture), so each corner slice IS
    /// a quarter circle whose radius equals the slice size.
    ///
    /// A pill then just needs the rendered slice to be exactly half the button
    /// height, which is what pixelsPerUnitMultiplier is set to below - so
    /// buttons of different heights (320x128 menu buttons, 112x64 Simon Says
    /// buttons) all come out correctly capped.
    ///
    /// Runs automatically once after compilation (version-gated); re-run from
    /// Tools/RingSport/Make Buttons Pills.
    /// </summary>
    public static class PillButtonSetup
    {
        // Bump to force the auto-run to re-apply the setup
        private const int SetupVersion = 1;
        private const string VersionPrefKey = "RingSport.PillButtonSetup.Version";

        private const string PillSpritePath = "Assets/Textures/UI/pill.png";
        private const string RoundedSpritePath = "Assets/Textures/UI/rounded 9.png";

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

            // Only the game scene has the buttons
            if (Object.FindAnyObjectByType<Button>(FindObjectsInactive.Include) == null)
                return;

            try
            {
                Run();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PillButtonSetup] Auto-setup failed (will retry when the editor is idle): {e}");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Make Buttons Pills")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[PillButtonSetup] Cannot run during play mode - exit play mode first.");
                return;
            }

            var pill = AssetDatabase.LoadAssetAtPath<Sprite>(PillSpritePath);
            if (pill == null)
            {
                Debug.LogError($"[PillButtonSetup] Missing {PillSpritePath} - the pill sprite must be imported first.");
                return;
            }

            var rounded = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedSpritePath);
            var buttons = Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (buttons.Length == 0)
            {
                Debug.LogError("[PillButtonSetup] No buttons in the open scene - open SampleScene first.");
                return;
            }

            int converted = 0;
            UnityEngine.SceneManagement.Scene scene = default;

            foreach (var button in buttons)
            {
                var image = button.targetGraphic as Image;
                if (image == null)
                    image = button.GetComponent<Image>();
                if (image == null)
                    continue;

                // Invisible tap areas (LoveNotes, Close, the pause Scrim) have no
                // background of their own - leave them alone
                if (image.sprite == null || image.color.a <= 0f)
                    continue;

                // Only the buttons wearing the shared rounded-rect background
                if (image.sprite != rounded && image.sprite != pill)
                    continue;

                if (ApplyPill(image, pill))
                {
                    converted++;
                    scene = button.gameObject.scene;
                }
            }

            if (converted > 0 && scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(scene);
                if (!string.IsNullOrEmpty(scene.path))
                    EditorSceneManager.SaveScene(scene);
            }

            EditorPrefs.SetInt(VersionPrefKey, SetupVersion);
            Debug.Log($"[PillButtonSetup] Pill buttons applied (v{SetupVersion}): {converted} button background(s) re-capped.");
        }

        /// <summary>
        /// Assigns the pill sprite and picks the pixelsPerUnitMultiplier that
        /// renders the 9-slice corner at exactly half the button's height.
        /// </summary>
        private static bool ApplyPill(Image image, Sprite pill)
        {
            float height = image.rectTransform.rect.height;
            if (height <= 0f)
            {
                Debug.LogWarning($"[PillButtonSetup] {image.name} has no height yet - skipped.");
                return false;
            }

            var canvas = image.canvas;
            float referencePixelsPerUnit = canvas != null ? canvas.referencePixelsPerUnit : 100f;

            // Image draws a border of borderPx * (referencePPU / spritePPU) / multiplier
            float borderPixels = pill.border.x; // square sprite: all four borders match
            float unscaledBorder = borderPixels * referencePixelsPerUnit / pill.pixelsPerUnit;

            Undo.RecordObject(image, "Make Button Pill");
            image.sprite = pill;
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.pixelsPerUnitMultiplier = unscaledBorder / (height * 0.5f);
            EditorUtility.SetDirty(image);
            return true;
        }
    }
}
