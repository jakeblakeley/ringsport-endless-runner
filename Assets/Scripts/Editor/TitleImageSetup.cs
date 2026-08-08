using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace RingSport.Editor
{
    /// <summary>
    /// Drops the game's title art onto the home screen, two thirds up the
    /// viewport so it sits above the dog.
    ///
    /// Authored in the same 1080x1920 phone design space as PhoneUILayoutSetup:
    /// full width minus the side gutters, aspect preserved. Sits as the first
    /// child of the canvas so the love notes button and its panel keep drawing
    /// over it, and takes no raycasts so nothing under it stops working.
    ///
    /// Uses title_cutout.png - the delivered title.png is rendered on a solid
    /// black plate, so the cutout is that art with the black keyed out.
    ///
    /// Runs automatically once after compilation (version-gated so it never
    /// stomps later hand tweaks); re-run from Tools/RingSport/Setup Title Image.
    /// </summary>
    public static class TitleImageSetup
    {
        // Bump to force the auto-run to re-apply the setup
        private const int SetupVersion = 7;
        private const string VersionPrefKey = "RingSport.TitleImageSetup.Version";

        private const string TitleSpritePath = "Assets/Textures/title_cutout.png";
        private const string ObjectName = "TitleImage";

        // Shared design-space constants (match PhoneUILayoutSetup)
        private const float DesignWidth = 1080f;
        private const float Margin = 48f;

        // Well under full width: the hat selector joined the home screen above
        // START, so the title cedes room to keep the title / dog / selector
        // stack breathing. 0.68 = 80% of the 0.85 it shipped the selector with.
        private const float TitleWidthScale = 0.68f;

        // Height up the viewport the title is centred on (0 = bottom, 1 = top).
        // 0.74: the 50mm portrait camera fills the middle of the frame with
        // the dog, so the title rides high to stay clear of her ears - nudged
        // down from 0.78 so it sits off the very top of the screen.
        private const float TitleViewportY = 0.74f;

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

            if (GameObject.Find("UI") == null)
                return; // not the game scene

            try
            {
                Run();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[TitleImageSetup] Auto-setup failed (will retry when the editor is idle): {e}");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Setup Title Image")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[TitleImageSetup] Cannot run during play mode - exit play mode first.");
                return;
            }

            var uiRootObject = GameObject.Find("UI");
            if (uiRootObject == null)
            {
                Debug.LogError("[TitleImageSetup] No 'UI' GameObject in the open scene - open SampleScene first.");
                return;
            }

            Transform home = uiRootObject.transform.Find("HomeScreen");
            if (home == null)
            {
                Debug.LogError("[TitleImageSetup] Missing 'UI/HomeScreen' canvas.");
                return;
            }

            Sprite sprite = LoadTitleSprite();
            if (sprite == null)
                return;

            var existing = home.Find(ObjectName);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            var titleObject = new GameObject(ObjectName, typeof(RectTransform));
            titleObject.transform.SetParent(home, false);
            titleObject.layer = LayerMask.NameToLayer("UI");
            // Behind the buttons, the NEW badge and the notes panel
            titleObject.transform.SetSiblingIndex(0);

            float width = (DesignWidth - 2f * Margin) * TitleWidthScale;
            float height = width * sprite.rect.height / sprite.rect.width;

            // Anchored to the fraction of the viewport rather than a pixel
            // offset, so the height holds on any aspect the scaler expands to
            var rt = (RectTransform)titleObject.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, TitleViewportY);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(width, height);
            rt.anchoredPosition = Vector2.zero;

            var image = titleObject.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;

            EditorSceneManager.MarkSceneDirty(uiRootObject.scene);
            if (!string.IsNullOrEmpty(uiRootObject.scene.path))
                EditorSceneManager.SaveScene(uiRootObject.scene);

            EditorPrefs.SetInt(VersionPrefKey, SetupVersion);
            Debug.Log($"[TitleImageSetup] Placed the title art on the home screen at {width}x{height}, {TitleViewportY:P0} up the viewport (v{SetupVersion}).");
        }

        /// <summary>
        /// The art comes in as a Multiple-mode sprite sheet, which yields no
        /// loadable Sprite - flip it to a single sprite before loading it.
        /// </summary>
        private static Sprite LoadTitleSprite()
        {
            var importer = AssetImporter.GetAtPath(TitleSpritePath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"[TitleImageSetup] Missing texture at {TitleSpritePath}.");
                return null;
            }

            if (importer.textureType != TextureImporterType.Sprite ||
                importer.spriteImportMode != SpriteImportMode.Single)
            {
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.alphaIsTransparency = true;
                importer.SaveAndReimport();
            }

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(TitleSpritePath);
            if (sprite == null)
                Debug.LogError($"[TitleImageSetup] Could not load a Sprite from {TitleSpritePath}.");

            return sprite;
        }
    }
}
