using RingSport.Core;
using RingSport.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace RingSport.Editor
{
    /// <summary>
    /// Scene wiring for the love notes collectible system:
    /// - Adds the LoveNote pool to the ObjectPooler
    /// - GameHud: "[icon] xN" counter below the score (top left)
    /// - HomeScreen: "[icon] collected/total" button below the high score
    ///   (top centre) with a NEW badge, opening a 2-column notes grid
    /// - Wires all new UIManager serialized fields
    ///
    /// Authored in the same 1080x1920 phone design space as PhoneUILayoutSetup.
    /// Runs automatically once after compilation (version-gated); re-run from
    /// Tools/RingSport/Setup Love Notes.
    /// </summary>
    public static class LoveNoteSetup
    {
        // Bump to force the auto-run to re-apply the setup
        private const int SetupVersion = 5;
        private const string VersionPrefKey = "RingSport.LoveNoteSetup.Version";

        // Shared design-space constants (match PhoneUILayoutSetup)
        private const float Margin = 48f;
        private const float Row1 = -96f;
        private const float Row2 = -176f;
        private const float Row3 = -248f;
        private const float Row4 = -320f;
        private const float FontSmall = 48f;
        private const float FontBody = 72f;
        private const float FontTitle = 96f;

        private const string PrefabPath = "Assets/Prefabs/Collectibles/LoveNote.prefab";
        private const string NoteIconPath = "Assets/Textures/Love Note.png";
        private const string NoteBackgroundPath = "Assets/Textures/Love Note Background.png";
        private const string RoundedSpritePath = "Assets/Textures/UI/rounded 9.png";
        private const string MarkerFontPath = "Assets/Fonts/PermanentMarker-Regular SDF.asset";

        private static readonly Color NoteTextColor = new Color(0.13f, 0.08f, 0.03f, 1f);
        private static readonly Color BadgeColor = new Color(0.91f, 0.30f, 0.24f, 1f);

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
                Debug.LogError($"[LoveNoteSetup] Auto-setup failed (will retry when the editor is idle): {e}");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Setup Love Notes")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[LoveNoteSetup] Cannot run during play mode - exit play mode first.");
                return;
            }

            var uiRootObject = GameObject.Find("UI");
            if (uiRootObject == null)
            {
                Debug.LogError("[LoveNoteSetup] No 'UI' GameObject in the open scene - open SampleScene first.");
                return;
            }

            Transform uiRoot = uiRootObject.transform;
            var manager = uiRootObject.GetComponent<UIManager>();
            if (manager == null)
            {
                Debug.LogError("[LoveNoteSetup] No UIManager on the 'UI' object.");
                return;
            }

            AddLoveNotePool();

            // Same counter on the HUD and on the game over screen (one row below
            // its New High Score indicator) so notes stay visible on retry
            GameObject hudCounter = BuildCounter(uiRoot, "GameHud", Row3, out TextMeshProUGUI hudCountText);
            GameObject gameOverCounter = BuildCounter(uiRoot, "GameOver", Row4, out TextMeshProUGUI gameOverCountText);
            RestyleHighScore(uiRoot);
            GameObject homeButton = BuildHomeButton(uiRoot,
                out TextMeshProUGUI homeCountText, out GameObject newBadge);
            LoveNotesPanel panel = BuildNotesPanel(uiRoot);

            WireUIManager(manager, hudCounter, hudCountText, gameOverCounter, gameOverCountText,
                homeButton, homeCountText, newBadge, panel);

            EditorSceneManager.MarkSceneDirty(uiRootObject.scene);
            if (!string.IsNullOrEmpty(uiRootObject.scene.path))
                EditorSceneManager.SaveScene(uiRootObject.scene);

            EditorPrefs.SetInt(VersionPrefKey, SetupVersion);
            Debug.Log($"[LoveNoteSetup] Love notes scene setup applied (v{SetupVersion}).");
        }

        // ------------------------------------------------------------------
        // Object pool
        // ------------------------------------------------------------------

        private static void AddLoveNotePool()
        {
            var pooler = Object.FindFirstObjectByType<ObjectPooler>(FindObjectsInactive.Include);
            if (pooler == null)
            {
                Debug.LogError("[LoveNoteSetup] No ObjectPooler in the scene - pool not added.");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[LoveNoteSetup] Missing prefab at {PrefabPath} - pool not added.");
                return;
            }

            var serialized = new SerializedObject(pooler);
            var pools = serialized.FindProperty("pools");

            // Update the existing entry if a re-run already added it
            for (int i = 0; i < pools.arraySize; i++)
            {
                var entry = pools.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("tag").stringValue == PoolTags.LoveNote)
                {
                    entry.FindPropertyRelative("prefab").objectReferenceValue = prefab;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    return;
                }
            }

            pools.arraySize++;
            var newEntry = pools.GetArrayElementAtIndex(pools.arraySize - 1);
            newEntry.FindPropertyRelative("tag").stringValue = PoolTags.LoveNote;
            newEntry.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            // TESTING head-room: with every large coin a note, many can be live at once
            newEntry.FindPropertyRelative("size").intValue = 15;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pooler);
            Debug.Log("[LoveNoteSetup] Added 'LoveNote' pool (size 15) to ObjectPooler.");
        }

        // ------------------------------------------------------------------
        // In-game counter: [icon] xN pinned to the top-left score column
        // ------------------------------------------------------------------

        private static GameObject BuildCounter(Transform uiRoot, string canvasName, float y,
            out TextMeshProUGUI countText)
        {
            countText = null;

            Transform canvas = uiRoot.Find(canvasName);
            if (canvas == null)
            {
                Debug.LogError($"[LoveNoteSetup] Missing 'UI/{canvasName}' canvas.");
                return null;
            }

            RemoveExisting(canvas, "LoveNoteCounter");

            var container = CreateRect("LoveNoteCounter", canvas);
            SetRect(container, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(400f, 64f), new Vector2(Margin, y));

            var icon = CreateRect("Icon", container.transform);
            SetRect(icon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(84f, 84f), Vector2.zero);
            AddSpriteImage(icon, NoteIconPath).preserveAspect = true;

            var count = CreateRect("Count", container.transform);
            SetRect(count, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(300f, 64f), new Vector2(100f, 0f));
            countText = AddText(count, "x1", HudFont(uiRoot), FontSmall, Color.white,
                TextAlignmentOptions.Left);

            // Hidden until a note is collected this run
            container.SetActive(false);
            return container;
        }

        // ------------------------------------------------------------------
        // Home screen: high score column top left, sticky note button top right
        // ------------------------------------------------------------------

        /// <summary>
        /// Restyles the home screen high score to match the in-game score
        /// column: number on top (Row1), "High Score" label below (Row2).
        /// UIManager writes only the number into HighScoreText.
        /// </summary>
        private static void RestyleHighScore(Transform uiRoot)
        {
            Transform home = uiRoot.Find("HomeScreen");
            if (home == null)
                return;

            Color labelColor = Color.white;

            var highScore = home.Find("HighScoreText");
            if (highScore == null)
            {
                Debug.LogWarning("[LoveNoteSetup] Missing 'HomeScreen/HighScoreText' - high score not restyled.");
            }
            else
            {
                SetRect(highScore.gameObject, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                    new Vector2(400f, 100f), new Vector2(Margin, Row1));
                var tmp = highScore.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.enableAutoSizing = false;
                    tmp.fontSize = FontBody;
                    tmp.alignment = TextAlignmentOptions.Left;
                    labelColor = tmp.color;
                }
            }

            RemoveExisting(home, "HighScoreLabel");
            var label = CreateRect("HighScoreLabel", home);
            SetRect(label, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0.5f),
                new Vector2(400f, 64f), new Vector2(Margin, Row2));
            AddText(label, "High Score", HudFont(uiRoot), FontSmall, labelColor,
                TextAlignmentOptions.Left);
        }

        private static GameObject BuildHomeButton(Transform uiRoot,
            out TextMeshProUGUI countText, out GameObject newBadge)
        {
            countText = null;
            newBadge = null;

            Transform home = uiRoot.Find("HomeScreen");
            if (home == null)
            {
                Debug.LogError("[LoveNoteSetup] Missing 'UI/HomeScreen' canvas.");
                return null;
            }

            RemoveExisting(home, "LoveNotesButton");

            var markerFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MarkerFontPath);

            // Sticky note pinned top right, opposite the high score column
            var buttonObject = CreateRect("LoveNotesButton", home);
            SetRect(buttonObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(154f, 154f), new Vector2(-Margin - 77f, -136f));

            // Invisible image keeps the whole area tappable without a visible background
            var tapArea = buttonObject.AddComponent<Image>();
            tapArea.color = new Color(0f, 0f, 0f, 0f);

            var button = buttonObject.AddComponent<Button>();
            button.targetGraphic = tapArea;

            // The note itself, tilted like the Love Note icon. The count text is
            // a child of the note so it inherits the same 15 degree tilt.
            var note = CreateRect("Note", buttonObject.transform);
            SetRect(note, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            note.transform.localRotation = Quaternion.Euler(0f, 0f, -15f);
            AddSpriteImage(note, NoteBackgroundPath).preserveAspect = true;

            var count = CreateRect("Count", note.transform);
            SetRect(count, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(-60f, -60f), Vector2.zero);
            countText = AddText(count, "0/0", markerFont, 39f, NoteTextColor,
                TextAlignmentOptions.Center);
            countText.enableAutoSizing = true;
            countText.fontSizeMin = 20f;
            countText.fontSizeMax = 45f;

            // NEW indicator: a red dot with a black stroke matching the note's
            // outline, sitting just below the note's top-right corner. The
            // stroke is a slightly larger black circle behind the red fill.
            // (9-slice corners rendered at half the rect size turn the rounded
            // square sprite into a circle.)
            var badge = CreateRect("NewBadge", buttonObject.transform);
            SetRect(badge, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(36f, 36f), new Vector2(-2f, -16f));
            var badgeOutline = AddSpriteImage(badge, RoundedSpritePath);
            badgeOutline.type = Image.Type.Sliced;
            badgeOutline.pixelsPerUnitMultiplier = 80f / 18f;
            badgeOutline.color = Color.black;

            var badgeFill = CreateRect("Fill", badge.transform);
            SetRect(badgeFill, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(26f, 26f), Vector2.zero);
            var badgeFillImage = AddSpriteImage(badgeFill, RoundedSpritePath);
            badgeFillImage.type = Image.Type.Sliced;
            badgeFillImage.pixelsPerUnitMultiplier = 80f / 13f;
            badgeFillImage.color = BadgeColor;

            badge.SetActive(false);
            newBadge = badge;
            return buttonObject;
        }

        // ------------------------------------------------------------------
        // Notes grid panel: full-screen overlay on the home screen
        // ------------------------------------------------------------------

        private static LoveNotesPanel BuildNotesPanel(Transform uiRoot)
        {
            Transform home = uiRoot.Find("HomeScreen");
            if (home == null)
                return null;

            RemoveExisting(home, "LoveNotesPanel");

            var panelObject = CreateRect("LoveNotesPanel", home);
            SetRect(panelObject, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);

            // Dark scrim that also blocks clicks on the screen behind it
            var scrim = panelObject.AddComponent<Image>();
            scrim.color = new Color(0.06f, 0.04f, 0.03f, 0.95f);

            var markerFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MarkerFontPath);
            if (markerFont == null)
                Debug.LogWarning($"[LoveNoteSetup] Missing font at {MarkerFontPath} - using default.");

            var title = CreateRect("Title", panelObject.transform);
            SetRect(title, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(-2f * Margin, 96f), new Vector2(0f, -96f));
            AddText(title, "Love Notes", markerFont, FontTitle, Color.white,
                TextAlignmentOptions.Center);

            // Close button, top right over the title row: a bare marker-drawn X
            var closeObject = CreateRect("CloseButton", panelObject.transform);
            SetRect(closeObject, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0.5f),
                new Vector2(96f, 96f), new Vector2(-Margin, -96f));
            var closeTapArea = closeObject.AddComponent<Image>();
            closeTapArea.color = new Color(0f, 0f, 0f, 0f);
            var closeButton = closeObject.AddComponent<Button>();
            closeButton.targetGraphic = closeTapArea;

            var closeLabel = CreateRect("Text", closeObject.transform);
            SetRect(closeLabel, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            AddText(closeLabel, "X", markerFont, FontBody, Color.white,
                TextAlignmentOptions.Center);

            // Scrollable grid below the title row
            var scroll = CreateRect("Scroll", panelObject.transform);
            SetRect(scroll, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0f, -176f), new Vector2(0f, -88f));

            // Invisible graphic so drags on empty areas still reach the ScrollRect
            var dragCatcher = scroll.AddComponent<Image>();
            dragCatcher.color = new Color(0f, 0f, 0f, 0f);

            var scrollRect = scroll.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Elastic;
            scrollRect.scrollSensitivity = 30f;

            var viewport = CreateRect("Viewport", scroll.transform);
            SetRect(viewport, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            viewport.AddComponent<RectMask2D>();

            var content = CreateRect("Content", viewport.transform);
            SetRect(content, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                Vector2.zero, Vector2.zero);

            var grid = content.AddComponent<GridLayoutGroup>();
            grid.padding = new RectOffset(0, 0, 24, 48);
            grid.cellSize = new Vector2(452f, 452f);
            grid.spacing = new Vector2(32f, 32f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.childAlignment = TextAnchor.UpperCenter;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = viewport.GetComponent<RectTransform>();
            scrollRect.content = content.GetComponent<RectTransform>();

            // Square note cell template: note background + Permanent Marker text
            var template = CreateRect("NoteCellTemplate", content.transform);
            var templateImage = AddSpriteImage(template, NoteBackgroundPath);
            templateImage.preserveAspect = true;

            var noteText = CreateRect("Text", template.transform);
            SetRect(noteText, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(-112f, -112f), Vector2.zero);
            var noteLabel = AddText(noteText, "", markerFont, 44f, NoteTextColor,
                TextAlignmentOptions.Center);
            noteLabel.enableAutoSizing = true;
            noteLabel.fontSizeMin = 28f;
            noteLabel.fontSizeMax = 52f;

            template.SetActive(false);

            var panel = panelObject.AddComponent<LoveNotesPanel>();
            var serialized = new SerializedObject(panel);
            serialized.FindProperty("contentRoot").objectReferenceValue = content.GetComponent<RectTransform>();
            serialized.FindProperty("noteCellTemplate").objectReferenceValue = template;
            serialized.FindProperty("closeButton").objectReferenceValue = closeButton;
            serialized.FindProperty("scrollRect").objectReferenceValue = scrollRect;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            panelObject.SetActive(false);
            return panel;
        }

        // ------------------------------------------------------------------
        // UIManager wiring
        // ------------------------------------------------------------------

        private static void WireUIManager(UIManager manager, GameObject hudCounter,
            TextMeshProUGUI hudCountText, GameObject gameOverCounter, TextMeshProUGUI gameOverCountText,
            GameObject homeButton, TextMeshProUGUI homeCountText,
            GameObject newBadge, LoveNotesPanel panel)
        {
            var serialized = new SerializedObject(manager);
            serialized.FindProperty("loveNotesButton").objectReferenceValue =
                homeButton != null ? homeButton.GetComponent<Button>() : null;
            serialized.FindProperty("loveNotesCountText").objectReferenceValue = homeCountText;
            serialized.FindProperty("loveNotesNewBadge").objectReferenceValue = newBadge;
            serialized.FindProperty("loveNotesPanel").objectReferenceValue = panel;
            serialized.FindProperty("loveNoteHudCounter").objectReferenceValue = hudCounter;
            serialized.FindProperty("loveNoteHudCountText").objectReferenceValue = hudCountText;
            serialized.FindProperty("gameOverLoveNoteCounter").objectReferenceValue = gameOverCounter;
            serialized.FindProperty("gameOverLoveNoteCountText").objectReferenceValue = gameOverCountText;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
            Debug.Log("[LoveNoteSetup] Wired UIManager love note references.");
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>The font the rest of the HUD uses, taken from the high score text.</summary>
        private static TMP_FontAsset HudFont(Transform uiRoot)
        {
            var highScore = uiRoot.Find("HomeScreen/HighScoreText");
            if (highScore != null)
            {
                var tmp = highScore.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                    return tmp.font;
            }
            return null; // TMP falls back to its default font
        }

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
                Debug.LogWarning($"[LoveNoteSetup] Missing sprite at {spritePath}");

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
