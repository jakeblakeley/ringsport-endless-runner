using RingSport.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace RingSport.Editor
{
    /// <summary>
    /// Scene wiring for the finale secret note (shown when the last level's
    /// finish line is crossed):
    /// - "SecretNoteOverlay" canvas sorted above every other screen, holding a
    ///   dark scrim (tap to dismiss), a big love note with the secret word, a
    ///   confetti container and a "tap to continue" hint
    /// - SecretNotePanel component driving the pop-in and confetti
    /// - Wires UIManager.secretNotePanel
    ///
    /// Authored in the same 1080x1920 phone design space as PhoneUILayoutSetup.
    /// Runs automatically once after compilation (version-gated); re-run from
    /// Tools/RingSport/Setup Secret Note.
    /// </summary>
    public static class SecretNoteSetup
    {
        // Bump to force the auto-run to re-apply the setup
        private const int SetupVersion = 1;
        private const string VersionPrefKey = "RingSport.SecretNoteSetup.Version";

        private const string NoteBackgroundPath = "Assets/Textures/Love Note Background.png";
        private const string MarkerFontPath = "Assets/Fonts/PermanentMarker-Regular SDF.asset";

        private const string NoteMessage = "The secret word is\n<size=160%>Agave</size>";

        // Above every gameplay/screen canvas (they all sit at sorting order 0)
        private const int OverlaySortingOrder = 10;

        private static readonly Color NoteTextColor = new Color(0.13f, 0.08f, 0.03f, 1f);
        private static readonly Color ScrimColor = new Color(0.06f, 0.04f, 0.03f, 0.94f);

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
                Debug.LogError($"[SecretNoteSetup] Auto-setup failed (will retry when the editor is idle): {e}");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Setup Secret Note")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[SecretNoteSetup] Cannot run during play mode - exit play mode first.");
                return;
            }

            var uiRootObject = GameObject.Find("UI");
            if (uiRootObject == null)
            {
                Debug.LogError("[SecretNoteSetup] No 'UI' GameObject in the open scene - open SampleScene first.");
                return;
            }

            var manager = uiRootObject.GetComponent<UIManager>();
            if (manager == null)
            {
                Debug.LogError("[SecretNoteSetup] No UIManager on the 'UI' object.");
                return;
            }

            SecretNotePanel panel = BuildOverlay(uiRootObject.transform);
            WireUIManager(manager, panel);

            // Play mode can begin mid-run: asset/scene edits above are safe,
            // but saving the scene now would throw - retry when idle instead
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[SecretNoteSetup] Play mode started mid-setup - will re-apply when the editor is idle.");
                EditorApplication.delayCall += TryAutoRun;
                return;
            }

            EditorSceneManager.MarkSceneDirty(uiRootObject.scene);
            if (!string.IsNullOrEmpty(uiRootObject.scene.path))
                EditorSceneManager.SaveScene(uiRootObject.scene);

            EditorPrefs.SetInt(VersionPrefKey, SetupVersion);
            Debug.Log($"[SecretNoteSetup] Secret note overlay setup applied (v{SetupVersion}).");
        }

        // ------------------------------------------------------------------
        // Overlay canvas
        // ------------------------------------------------------------------

        private static SecretNotePanel BuildOverlay(Transform uiRoot)
        {
            RemoveExisting(uiRoot, "SecretNoteOverlay");

            // Own canvas so the note sorts above whichever screen is showing
            // (reward screen in the real flow, home screen from the debug menu)
            var overlay = CreateRect("SecretNoteOverlay", uiRoot);
            var canvas = overlay.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = OverlaySortingOrder;

            var scaler = overlay.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            scaler.referencePixelsPerUnit = 100f;

            overlay.AddComponent<GraphicRaycaster>();

            var markerFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MarkerFontPath);
            if (markerFont == null)
                Debug.LogWarning($"[SecretNoteSetup] Missing font at {MarkerFontPath} - using default.");

            // Dark scrim doubling as the full-screen dismiss button
            var scrimObject = CreateRect("Scrim", overlay.transform);
            SetRect(scrimObject, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            var scrimImage = scrimObject.AddComponent<Image>();
            scrimImage.color = ScrimColor;
            var dismissButton = scrimObject.AddComponent<Button>();
            dismissButton.targetGraphic = scrimImage;
            dismissButton.transition = Selectable.Transition.None;

            // The note itself: a big version of the love note, hand-tilted.
            // Runtime pop-in overrides the scale/rotation set here.
            var note = CreateRect("Note", overlay.transform);
            SetRect(note, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(900f, 900f), new Vector2(0f, 60f));
            note.transform.localRotation = Quaternion.Euler(0f, 0f, -4f);
            var noteImage = AddSpriteImage(note, NoteBackgroundPath);
            noteImage.preserveAspect = true;
            noteImage.raycastTarget = false;

            var textObject = CreateRect("Text", note.transform);
            SetRect(textObject, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(-224f, -224f), Vector2.zero);
            var noteLabel = AddText(textObject, NoteMessage, markerFont, 96f, NoteTextColor,
                TextAlignmentOptions.Center);
            noteLabel.enableAutoSizing = true;
            noteLabel.fontSizeMin = 48f;
            noteLabel.fontSizeMax = 120f;

            // Confetti container AFTER the note so pieces flutter in front of it.
            // Pieces are created at runtime by SecretNotePanel.
            var confetti = CreateRect("Confetti", overlay.transform);
            SetRect(confetti, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);

            var hint = CreateRect("Hint", overlay.transform);
            SetRect(hint, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-96f, 72f), new Vector2(0f, 128f));
            var hintLabel = AddText(hint, "tap to continue", markerFont, 52f, Color.white,
                TextAlignmentOptions.Center);

            var panel = overlay.AddComponent<SecretNotePanel>();
            var serialized = new SerializedObject(panel);
            serialized.FindProperty("noteRoot").objectReferenceValue = note.GetComponent<RectTransform>();
            serialized.FindProperty("scrim").objectReferenceValue = scrimImage;
            serialized.FindProperty("confettiRoot").objectReferenceValue = confetti.GetComponent<RectTransform>();
            serialized.FindProperty("hintText").objectReferenceValue = hintLabel;
            serialized.FindProperty("dismissButton").objectReferenceValue = dismissButton;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            overlay.SetActive(false);
            return panel;
        }

        private static void WireUIManager(UIManager manager, SecretNotePanel panel)
        {
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("secretNotePanel").objectReferenceValue = panel;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
            Debug.Log("[SecretNoteSetup] Wired UIManager.secretNotePanel.");
        }

        // ------------------------------------------------------------------
        // Helpers (same conventions as LoveNoteSetup)
        // ------------------------------------------------------------------

        private static void RemoveExisting(Transform parent, string name)
        {
            var existing = parent.Find(name);
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);
        }

        private static GameObject CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.layer = LayerMask.NameToLayer("UI");
            return go;
        }

        private static void SetRect(GameObject go, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 size, Vector2 position)
        {
            var rt = (RectTransform)go.transform;
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = position;
        }

        private static Image AddSpriteImage(GameObject go, string spritePath)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(spritePath);
            if (sprite == null)
                Debug.LogWarning($"[SecretNoteSetup] Missing sprite at {spritePath}");

            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            return image;
        }

        private static TextMeshProUGUI AddText(GameObject go, string text, TMP_FontAsset font,
            float fontSize, Color color, TextAlignmentOptions alignment)
        {
            var tmp = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
                tmp.font = font;
            tmp.text = text;
            tmp.enableAutoSizing = false;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }
    }
}
