using RingSport.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace RingSport.Editor
{
    /// <summary>
    /// Re-authors every screen canvas against a single 9:16 phone design space
    /// (1080x1920) and switches the canvas scalers from Constant Pixel Size to
    /// Scale With Screen Size / Expand, so the whole design is guaranteed to be
    /// on screen on any aspect ratio (Expand picks min(w/1080, h/1920)).
    ///
    /// Every value below is authored in that 1080x1920 space. Anything that can
    /// grow with its content (button rows, the reward screen's bottom block)
    /// stretches to the screen edges minus a margin instead of using a fixed
    /// width, so it can no longer extend past its container.
    ///
    /// Runs automatically once after compilation (version-gated so it never
    /// stomps later hand tweaks); re-run from Tools/RingSport/Setup Phone UI Layout.
    /// </summary>
    public static class PhoneUILayoutSetup
    {
        // Bump to force the auto-run to re-apply the layout
        private const int SetupVersion = 6;
        private const string VersionPrefKey = "RingSport.PhoneUILayoutSetup.Version";

        // Every screen speaks in Barlow Bold. Permanent Marker is the one
        // deliberate exception - the handwritten voice of the love-note and
        // secret-note panels - so anything already on it is left alone.
        private const string PrimaryFontPath = "Assets/Fonts/Barlow-Bold SDF.asset";
        private const string HandwrittenFontPath = "Assets/Fonts/PermanentMarker-Regular SDF.asset";

        // 9:16 design space
        private const float DesignWidth = 1080f;
        private const float DesignHeight = 1920f;

        private const float Margin = 48f;        // side gutter
        private const float Row1 = -96f;         // first top row, centre y
        private const float Row2 = -176f;        // second top row
        private const float Row3 = -248f;        // third top row
        private const float ButtonW = 320f;
        private const float ButtonH = 128f;
        private const float ButtonGap = 16f;
        private const float ButtonRowW = ButtonW * 2f + ButtonGap;
        private const float ButtonCaptionInset = 20f;

        // Font sizes (2x the old constant-pixel design, which was authored ~540x960)
        private const float FontSmall = 48f;
        private const float FontBody = 72f;
        private const float FontTitle = 96f;
        private const float FontHuge = 128f;

        private static readonly Vector2 TopLeft = new Vector2(0f, 1f);
        private static readonly Vector2 TopRight = new Vector2(1f, 1f);
        private static readonly Vector2 BottomCentre = new Vector2(0.5f, 0f);
        private static readonly Vector2 Centre = new Vector2(0.5f, 0.5f);

        private static Transform uiRoot;

        [InitializeOnLoadMethod]
        private static void AutoRunOnLoad()
        {
            EditorApplication.delayCall += TryAutoRun;
        }

        private static void TryAutoRun()
        {
            // Never touch the scene mid-play, but keep waiting - a domain reload
            // only happens on compile, so giving up here means the setup would
            // silently never run once the play session ends.
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
                Debug.LogError($"[PhoneUILayoutSetup] Auto-setup failed (will retry when the editor is idle): {e}");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Setup Phone UI Layout")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[PhoneUILayoutSetup] Cannot run during play mode - exit play mode first (the auto-run will then apply it).");
                return;
            }

            var root = GameObject.Find("UI");
            if (root == null)
            {
                Debug.LogError("[PhoneUILayoutSetup] No 'UI' GameObject in the open scene - open SampleScene first.");
                return;
            }

            uiRoot = root.transform;

            ConfigureScalers();

            LayoutHomeScreen();
            LayoutCountdown();
            LayoutGameHud();
            LayoutGameOver();
            LayoutPalisade();
            LayoutRewardScreen();
            LayoutMiniLevelStart();
            LayoutSimonSays();
            LayoutFoodRefusal();

            ApplyFonts();
            WireRewardBanner();
            Validate();

            // Only a saved scene earns the version stamp - if play mode crept
            // in, the in-memory changes evaporate on play-exit, so retry later
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[PhoneUILayoutSetup] Play mode started mid-setup - not saving; will re-run when the editor is idle.");
                EditorApplication.delayCall += TryAutoRun;
                return;
            }

            EditorSceneManager.MarkSceneDirty(root.scene);
            if (!string.IsNullOrEmpty(root.scene.path))
                EditorSceneManager.SaveScene(root.scene);

            EditorPrefs.SetInt(VersionPrefKey, SetupVersion);
            Debug.Log($"[PhoneUILayoutSetup] Applied {DesignWidth}x{DesignHeight} phone layout to all screens (v{SetupVersion}), saved to {root.scene.path}.");
        }

        // ------------------------------------------------------------------
        // Canvas scalers
        // ------------------------------------------------------------------

        private static void ConfigureScalers()
        {
            foreach (var scaler in uiRoot.GetComponentsInChildren<CanvasScaler>(true))
            {
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(DesignWidth, DesignHeight);
                // Expand => scaleFactor = min(w/1080, h/1920), so nothing authored
                // inside the 9:16 box can ever be cropped, whatever the aspect.
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
                scaler.referencePixelsPerUnit = 100f;
                EditorUtility.SetDirty(scaler);
            }
        }

        // ------------------------------------------------------------------
        // Screens
        // ------------------------------------------------------------------

        private static void LayoutHomeScreen()
        {
            const string c = "HomeScreen";

            // Left column - number on top, "High Score" label under it - so the
            // home screen reads the same way as the in-game score column.
            Place(Find(c, "HighScoreText"), TopLeft, TopLeft, new Vector2(0f, 0.5f), new Vector2(400f, 100f), new Vector2(Margin, Row1));
            Text(Find(c, "HighScoreText"), FontBody, TextAlignmentOptions.Left);

            Place(Find(c, "HighScoreLabel"), TopLeft, TopLeft, new Vector2(0f, 0.5f), new Vector2(400f, 64f), new Vector2(Margin, Row2));
            Text(Find(c, "HighScoreLabel"), FontSmall, TextAlignmentOptions.Left);

            StretchBottom(Find(c, "Instructions"), 384f, 96f);
            Text(Find(c, "Instructions"), FontSmall, TextAlignmentOptions.Center);

            // Home START sits lower than the other screens' 192 rows (the hat
            // selector stack above it needs the breathing room) and runs 20%
            // larger than the shared button size - it's the screen's one CTA
            Place(Find(c, "Button"), BottomCentre, BottomCentre, BottomCentre, new Vector2(ButtonW * 1.2f, ButtonH * 1.2f), new Vector2(0f, 156f));
            Text(Find(c, "Button/Text (TMP)"), FontSmall * 1.2f, TextAlignmentOptions.Center);
        }

        private static void LayoutCountdown()
        {
            const string c = "Countdown UI";

            Place(Find(c, "Countdown Text"), Centre, Centre, Centre, new Vector2(960f, 160f), new Vector2(0f, 192f));
            Text(Find(c, "Countdown Text"), FontHuge, TextAlignmentOptions.Center);
        }

        private static void LayoutGameHud()
        {
            const string c = "GameHud";

            LivesCluster(c);

            Place(Find(c, "Score"), TopLeft, TopLeft, new Vector2(0f, 0.5f), new Vector2(400f, 100f), new Vector2(Margin, Row1));
            Text(Find(c, "Score"), FontBody, TextAlignmentOptions.Left);

            Place(Find(c, "Level"), TopLeft, TopLeft, new Vector2(0f, 0.5f), new Vector2(400f, 64f), new Vector2(Margin, Row2));
            Text(Find(c, "Level"), FontSmall, TextAlignmentOptions.Left);

            // The fill child is stretched and driven at runtime (UIManager sets
            // anchorMax.x) - only the background is positioned here.
            Place(Find(c, "SprintBarBackground"), BottomCentre, BottomCentre, BottomCentre, new Vector2(400f, 40f), new Vector2(0f, 160f));
        }

        private static void LayoutGameOver()
        {
            const string c = "GameOver";

            LivesCluster(c);

            Place(Find(c, "TotalScoreText"), TopLeft, TopLeft, new Vector2(0f, 0.5f), new Vector2(600f, 96f), new Vector2(Margin, Row1));
            Text(Find(c, "TotalScoreText"), FontBody, TextAlignmentOptions.Left);

            Place(Find(c, "HighScoreText"), TopLeft, TopLeft, new Vector2(0f, 0.5f), new Vector2(600f, 64f), new Vector2(Margin, Row2));
            Text(Find(c, "HighScoreText"), FontSmall, TextAlignmentOptions.Left);

            Place(Find(c, "New High Score Indicator"), TopLeft, TopLeft, new Vector2(0f, 0.5f), new Vector2(600f, 64f), new Vector2(Margin, Row3));
            Fill(Find(c, "New High Score Indicator/New High Score"));
            Text(Find(c, "New High Score Indicator/New High Score"), FontSmall, TextAlignmentOptions.Left);

            // Centre banner, stretched to the gutters so a long word can't run off
            Place(Find(c, "Game Over"), new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), Centre, new Vector2(-2f * Margin, 160f), new Vector2(0f, 64f));
            Text(Find(c, "Game Over"), FontHuge, TextAlignmentOptions.Center);

            ButtonRow(Find(c, "Layout"), BottomCentre, BottomCentre, BottomCentre, new Vector2(ButtonRowW, ButtonH), new Vector2(0f, 192f));
            ActionButton(Find(c, "Layout/Quit"), "Text (TMP)");
            ActionButton(Find(c, "Layout/Retry"), "Retry Text");
        }

        private static void LayoutPalisade()
        {
            const string c = "Palisade UI";

            // Timer and instructions used to sit on the same spot and overlap
            Place(Find(c, "Timer"), Centre, Centre, Centre, new Vector2(800f, 100f), new Vector2(0f, 380f));
            Text(Find(c, "Timer"), FontBody, TextAlignmentOptions.Center);

            Place(Find(c, "Tap Instructions"), Centre, Centre, Centre, new Vector2(800f, 100f), new Vector2(0f, 270f));
            Text(Find(c, "Tap Instructions"), FontBody, TextAlignmentOptions.Center);

            Place(Find(c, "Taps Counter Bar"), Centre, Centre, Centre, new Vector2(400f, 40f), new Vector2(0f, 180f));
        }

        private static void LayoutRewardScreen()
        {
            const string c = "Reward Screen";

            Place(Find(c, "TotalScore"), TopLeft, TopLeft, new Vector2(0f, 0.5f), new Vector2(600f, 96f), new Vector2(Margin, Row1));
            Text(Find(c, "TotalScore"), FontBody, TextAlignmentOptions.Left);

            Place(Find(c, "HighScoreText"), TopLeft, TopLeft, new Vector2(0f, 0.5f), new Vector2(600f, 64f), new Vector2(Margin, Row2));
            Text(Find(c, "HighScoreText"), FontSmall, TextAlignmentOptions.Left);

            Place(Find(c, "New High Score Indicator"), TopLeft, TopLeft, new Vector2(0f, 0.5f), new Vector2(600f, 64f), new Vector2(Margin, Row3));
            Fill(Find(c, "New High Score Indicator/New High Score"));
            Text(Find(c, "New High Score Indicator/New High Score"), FontSmall, TextAlignmentOptions.Left);

            StretchTop(Find(c, "You Did it"), -560f, 128f);
            Text(Find(c, "You Did it"), FontTitle, TextAlignmentOptions.Center);

            // Bottom block: location / level name / buttons, full width minus gutters
            var bottom = Find(c, "Bottom Text");
            StretchBottom(bottom, 128f, 56f + 96f + ButtonH + 2f * ButtonGap);
            var vertical = Require<VerticalLayoutGroup>(bottom);
            if (vertical != null)
            {
                vertical.padding = new RectOffset(0, 0, 0, 0);
                vertical.spacing = ButtonGap;
                vertical.childAlignment = TextAnchor.LowerCenter;
                // Control width so children always span the block and never
                // overhang it; keep their authored heights.
                vertical.childControlWidth = true;
                vertical.childForceExpandWidth = true;
                vertical.childControlHeight = false;
                vertical.childForceExpandHeight = false;
                EditorUtility.SetDirty(vertical);
            }

            float blockWidth = DesignWidth - 2f * Margin;
            Size(Find(c, "Bottom Text/Location"), new Vector2(blockWidth, 56f));
            Text(Find(c, "Bottom Text/Location"), FontSmall, TextAlignmentOptions.Center);

            Size(Find(c, "Bottom Text/Level Name"), new Vector2(blockWidth, 96f));
            Text(Find(c, "Bottom Text/Level Name"), FontBody, TextAlignmentOptions.Center);

            var row = Find(c, "Bottom Text/Level Buttons");
            Size(row, new Vector2(blockWidth, ButtonH));
            ConfigureButtonRow(row);
            ActionButton(Find(c, "Bottom Text/Level Buttons/Quit"), "Text (TMP)");
            ActionButton(Find(c, "Bottom Text/Level Buttons/Next Level"), "Text (TMP)");
        }

        private static void LayoutMiniLevelStart()
        {
            const string c = "MiniLevelStart";

            StretchTop(Find(c, "LevelName"), Row1, 96f);
            Text(Find(c, "LevelName"), FontBody, TextAlignmentOptions.Center);

            StretchBottom(Find(c, "Instructions"), 368f, 96f);
            Text(Find(c, "Instructions"), FontSmall, TextAlignmentOptions.Center);

            Place(Find(c, "Button"), BottomCentre, BottomCentre, BottomCentre, new Vector2(ButtonW, ButtonH), new Vector2(0f, 192f));
            Text(Find(c, "Button/Text (TMP)"), FontSmall, TextAlignmentOptions.Center);
        }

        private static void LayoutSimonSays()
        {
            const string c = "MiniLevelPositionsSimonsSays";

            Place(Find(c, "SimonSays Text"), Centre, Centre, Centre, new Vector2(800f, 128f), new Vector2(0f, 256f));
            Text(Find(c, "SimonSays Text"), FontTitle, TextAlignmentOptions.Center);

            // Three equal-width buttons that always fit between the gutters
            var row = Find(c, "Layout");
            StretchBottom(row, 192f, ButtonH);
            var horizontal = Require<HorizontalLayoutGroup>(row);
            if (horizontal != null)
            {
                horizontal.padding = new RectOffset(0, 0, 0, 0);
                horizontal.spacing = ButtonGap;
                horizontal.childAlignment = TextAnchor.MiddleCenter;
                horizontal.childControlWidth = true;
                horizontal.childForceExpandWidth = true;
                horizontal.childControlHeight = true;
                horizontal.childForceExpandHeight = true;
                EditorUtility.SetDirty(horizontal);
            }

            Text(Find(c, "Layout/Sit/Sit Text"), FontSmall, TextAlignmentOptions.Center, wrap: false);
            Text(Find(c, "Layout/Down/Down Text"), FontSmall, TextAlignmentOptions.Center, wrap: false);
            Text(Find(c, "Layout/Stand/Stand Text"), FontSmall, TextAlignmentOptions.Center, wrap: false);
        }

        private static void LayoutFoodRefusal()
        {
            const string c = "MiniLevelFoodRefusal";

            Place(Find(c, "Score"), TopLeft, TopLeft, new Vector2(0f, 0.5f), new Vector2(400f, 100f), new Vector2(Margin, Row1));
            Text(Find(c, "Score"), FontBody, TextAlignmentOptions.Left);
        }

        // ------------------------------------------------------------------
        // Shared pieces
        // ------------------------------------------------------------------

        /// <summary>Lives counter + its icon, pinned to the top-right gutter.</summary>
        private static void LivesCluster(string canvasName)
        {
            Place(Find(canvasName, "caicoslife"), TopRight, TopRight, new Vector2(1f, 0.5f), new Vector2(80f, 80f), new Vector2(-Margin, Row1));
            Place(Find(canvasName, "Lives"), TopRight, TopRight, new Vector2(1f, 0.5f), new Vector2(256f, 100f), new Vector2(-(Margin + 80f + ButtonGap), Row1));
            Text(Find(canvasName, "Lives"), FontBody, TextAlignmentOptions.Right);
        }

        private static void ButtonRow(RectTransform row, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size, Vector2 pos)
        {
            Place(row, aMin, aMax, pivot, size, pos);
            ConfigureButtonRow(row);
        }

        /// <summary>
        /// Fixed-size buttons centred in the row. Sizes are explicit rather than
        /// layout-driven so hiding one button (Retry when out of lives) leaves
        /// the other at its normal size instead of stretching it across the row.
        /// </summary>
        private static void ConfigureButtonRow(RectTransform row)
        {
            var horizontal = Require<HorizontalLayoutGroup>(row);
            if (horizontal == null)
                return;

            horizontal.padding = new RectOffset(0, 0, 0, 0);
            horizontal.spacing = ButtonGap;
            horizontal.childAlignment = TextAnchor.MiddleCenter;
            horizontal.childControlWidth = false;
            horizontal.childForceExpandWidth = false;
            horizontal.childControlHeight = false;
            horizontal.childForceExpandHeight = false;
            EditorUtility.SetDirty(horizontal);
        }

        private static void ActionButton(RectTransform button, string labelName)
        {
            if (button == null)
                return;

            Size(button, new Vector2(ButtonW, ButtonH));

            var label = button.Find(labelName) as RectTransform;
            if (label == null)
            {
                Debug.LogWarning($"[PhoneUILayoutSetup] Missing label '{labelName}' under {button.name}");
                return;
            }

            // Inset from the pill's rounded ends, and let a long caption
            // ("NEXT LEVEL") shrink rather than run over the edge - Barlow Bold
            // is a good deal wider than the condensed face these were sized for.
            Place(label, Vector2.zero, Vector2.one, Centre, new Vector2(-ButtonCaptionInset * 2f, 0f), Vector2.zero);
            Text(label, FontSmall, TextAlignmentOptions.Center, wrap: false, autoShrink: true);
        }

        /// <summary>
        /// The reward screen doubles as a level intro, which needs to hide the
        /// "YOU DID IT!" banner - point UIManager at it if it isn't wired yet.
        /// </summary>
        private static void WireRewardBanner()
        {
            var manager = uiRoot.GetComponent<UIManager>();
            if (manager == null)
            {
                Debug.LogWarning("[PhoneUILayoutSetup] No UIManager on the 'UI' object - reward banner not wired.");
                return;
            }

            var banner = Find("Reward Screen", "You Did it");
            if (banner == null)
                return;

            var serialized = new SerializedObject(manager);
            var property = serialized.FindProperty("rewardCompleteBanner");
            if (property == null || property.objectReferenceValue != null)
                return;

            property.objectReferenceValue = banner.gameObject;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
            Debug.Log("[PhoneUILayoutSetup] Wired UIManager.rewardCompleteBanner -> Reward Screen/You Did it");
        }

        // ------------------------------------------------------------------
        // RectTransform helpers
        // ------------------------------------------------------------------

        private static void Place(RectTransform rt, Vector2 aMin, Vector2 aMax, Vector2 pivot, Vector2 size, Vector2 pos)
        {
            if (rt == null)
                return;

            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.pivot = pivot;
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            EditorUtility.SetDirty(rt);
        }

        /// <summary>Full width minus the side gutters, pinned to the top edge.</summary>
        private static void StretchTop(RectTransform rt, float y, float height)
        {
            Place(rt, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(-2f * Margin, height), new Vector2(0f, y));
        }

        /// <summary>Full width minus the side gutters, pinned to the bottom edge.</summary>
        private static void StretchBottom(RectTransform rt, float y, float height)
        {
            Place(rt, new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-2f * Margin, height), new Vector2(0f, y));
        }

        /// <summary>Stretch to fill the parent exactly.</summary>
        private static void Fill(RectTransform rt)
        {
            Place(rt, Vector2.zero, Vector2.one, Centre, Vector2.zero, Vector2.zero);
        }

        /// <summary>Resize without touching anchors (for layout-driven children).</summary>
        private static void Size(RectTransform rt, Vector2 size)
        {
            if (rt == null)
                return;

            rt.sizeDelta = size;
            EditorUtility.SetDirty(rt);
        }

        /// <param name="autoShrink">
        /// Let the text scale itself down (never up) to fit its box. Used for
        /// button captions, where the copy is fixed and the pill is not.
        /// </param>
        private static void Text(RectTransform rt, float fontSize, TextAlignmentOptions alignment,
            bool wrap = true, bool autoShrink = false)
        {
            if (rt == null)
                return;

            var tmp = rt.GetComponent<TextMeshProUGUI>();
            if (tmp == null)
            {
                Debug.LogWarning($"[PhoneUILayoutSetup] No TextMeshProUGUI on {rt.name}");
                return;
            }

            tmp.fontSize = fontSize;
            tmp.fontSizeMin = autoShrink ? fontSize * 0.7f : fontSize * 0.5f;
            tmp.fontSizeMax = fontSize;
            tmp.enableAutoSizing = autoShrink;
            tmp.alignment = alignment;
            // Button captions stay on one line; body copy wraps inside its box
            tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            EditorUtility.SetDirty(tmp);
        }

        // ------------------------------------------------------------------
        // Fonts
        // ------------------------------------------------------------------

        /// <summary>
        /// Pulls every label back onto the primary face. Screens had drifted
        /// apart - the reward screen's buttons and score column were on the
        /// condensed face while its banner was on Bold, and Game Over's
        /// "NEW HIGH SCORE!" was still on TMP's default LiberationSans.
        /// </summary>
        private static void ApplyFonts()
        {
            var primary = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(PrimaryFontPath);
            if (primary == null)
            {
                Debug.LogError($"[PhoneUILayoutSetup] No font asset at '{PrimaryFontPath}' - fonts left untouched.");
                return;
            }

            var handwritten = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(HandwrittenFontPath);
            int changed = 0;

            foreach (var tmp in uiRoot.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (tmp.font == primary || (handwritten != null && tmp.font == handwritten))
                    continue;

                Debug.Log($"[PhoneUILayoutSetup] '{Path(tmp.transform)}': {(tmp.font != null ? tmp.font.name : "no font")} -> {primary.name}");
                tmp.font = primary;
                tmp.fontSharedMaterial = primary.material;
                EditorUtility.SetDirty(tmp);
                changed++;
            }

            Debug.Log($"[PhoneUILayoutSetup] Font pass: {changed} label(s) moved onto {primary.name}.");
        }

        private static RectTransform Find(string canvasName, string childPath = null)
        {
            var canvas = uiRoot.Find(canvasName);
            if (canvas == null)
            {
                Debug.LogWarning($"[PhoneUILayoutSetup] Missing canvas 'UI/{canvasName}'");
                return null;
            }

            if (string.IsNullOrEmpty(childPath))
                return canvas as RectTransform;

            var child = canvas.Find(childPath);
            if (child == null)
            {
                Debug.LogWarning($"[PhoneUILayoutSetup] Missing 'UI/{canvasName}/{childPath}'");
                return null;
            }

            return child as RectTransform;
        }

        private static T Require<T>(RectTransform rt) where T : Component
        {
            if (rt == null)
                return null;

            var component = rt.GetComponent<T>();
            if (component == null)
                Debug.LogWarning($"[PhoneUILayoutSetup] No {typeof(T).Name} on {rt.name}");

            return component;
        }

        // ------------------------------------------------------------------
        // Validation - reports anything sticking out of the 9:16 design box
        // ------------------------------------------------------------------

        private static void Validate()
        {
            int problems = 0;

            foreach (Transform canvas in uiRoot)
            {
                var canvasRect = canvas as RectTransform;
                if (canvasRect == null)
                    continue;

                foreach (var rt in canvas.GetComponentsInChildren<RectTransform>(true))
                {
                    if (rt == canvasRect)
                        continue;

                    // Layout-group children (and everything under them) are
                    // positioned at runtime; their serialized anchors say nothing
                    // useful until the group has rebuilt.
                    if (IsLayoutDriven(rt, canvasRect))
                        continue;

                    Rect box = ResolveRect(rt, canvasRect, new Vector2(DesignWidth, DesignHeight));
                    if (box.xMin < -0.5f || box.yMin < -0.5f || box.xMax > DesignWidth + 0.5f || box.yMax > DesignHeight + 0.5f)
                    {
                        Debug.LogWarning($"[PhoneUILayoutSetup] '{Path(rt)}' extends outside the {DesignWidth}x{DesignHeight} frame: {box}");
                        problems++;
                    }
                }
            }

            if (problems == 0)
                Debug.Log($"[PhoneUILayoutSetup] Validation passed - every element fits inside {DesignWidth}x{DesignHeight}.");
        }

        private static bool IsLayoutDriven(RectTransform rt, RectTransform canvasRect)
        {
            for (Transform t = rt; t != null && t != canvasRect; t = t.parent)
            {
                if (t.parent != null && t.parent.GetComponent<LayoutGroup>() != null)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Resolves a rect's position in canvas pixels for the given canvas size,
        /// walking down from the canvas so the check does not depend on whatever
        /// resolution the Game view currently happens to be at.
        /// </summary>
        private static Rect ResolveRect(RectTransform rt, RectTransform canvasRect, Vector2 canvasSize)
        {
            if (rt == canvasRect)
                return new Rect(Vector2.zero, canvasSize);

            Rect parent = ResolveRect((RectTransform)rt.parent, canvasRect, canvasSize);

            float anchorMinX = parent.xMin + rt.anchorMin.x * parent.width;
            float anchorMaxX = parent.xMin + rt.anchorMax.x * parent.width;
            float anchorMinY = parent.yMin + rt.anchorMin.y * parent.height;
            float anchorMaxY = parent.yMin + rt.anchorMax.y * parent.height;

            float width = (anchorMaxX - anchorMinX) + rt.sizeDelta.x;
            float height = (anchorMaxY - anchorMinY) + rt.sizeDelta.y;

            // anchoredPosition is the pivot's offset from the anchor reference point
            float pivotX = Mathf.Lerp(anchorMinX, anchorMaxX, rt.pivot.x) + rt.anchoredPosition.x;
            float pivotY = Mathf.Lerp(anchorMinY, anchorMaxY, rt.pivot.y) + rt.anchoredPosition.y;

            return new Rect(pivotX - rt.pivot.x * width, pivotY - rt.pivot.y * height, width, height);
        }

        private static string Path(Transform t)
        {
            string path = t.name;
            while (t.parent != null && t.parent != uiRoot)
            {
                t = t.parent;
                path = t.name + "/" + path;
            }
            return path;
        }
    }
}
