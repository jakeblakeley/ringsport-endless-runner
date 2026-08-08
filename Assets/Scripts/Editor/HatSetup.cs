using System.IO;
using RingSport.Core;
using RingSport.Level;
using RingSport.Player;
using RingSport.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace RingSport.Editor
{
    /// <summary>
    /// Everything the hat system needs in the scene and shared prefabs:
    /// - The pooled HatPickup prefab (the next droppable hat floating over the
    ///   lane with the love note's attention beacon) + its ObjectPooler entry
    /// - HatEquipper on the Player prefab
    /// - The home-screen selector carousel above START: thumbnail boxes with
    ///   generated rounded-stroke sprites, Permanent Marker arrows,
    ///   love-note-style NEW badges, holiday tags on seasonal hats
    /// - The pulsing "Limited Time" seasonal banner above the selector
    /// - The countdown "how to play" line + hiding the home screen
    ///   Instructions text it replaces
    ///
    /// The hat prefabs themselves (real .glb models fitted to the head) are
    /// baked separately by HatPrefabBaker.
    ///
    /// Authored in the same 1080x1920 phone design space as
    /// PhoneUILayoutSetup. Runs automatically once after compilation
    /// (version-gated); re-run from Tools/RingSport/Setup Hats.
    /// </summary>
    public static class HatSetup
    {
        // Bump to force the auto-run to re-apply the setup
        private const int SetupVersion = 8;
        private const string VersionPrefKey = "RingSport.HatSetup.Version";

        private const string PickupPrefabPath = "Assets/Prefabs/Collectibles/HatPickup.prefab";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string ParticleShaderPath = "Assets/Shaders/ShaderGraphs/Generic_ParticlesUnlit_Arc.shadergraph";
        private const string SparkTexPath = "Assets/Textures/VFX/SparkStar.png";
        private const string GlowTexPath = "Assets/Textures/VFX/SoftGlow.png";
        private const string MarkerFontPath = "Assets/Fonts/PermanentMarker-Regular SDF.asset";
        private const string RoundedSpritePath = "Assets/Textures/UI/rounded 9.png";
        private const string BoxSpritePath = "Assets/Textures/UI/hat_box.png";
        private const string EdgeFadeSpritePath = "Assets/Textures/UI/hat_edge_fade.png";
        private const string PickupSoundPath = "Assets/Sounds/Reward/reward-bell.wav";

        // Selector layout (1080x1920 design space, above the enlarged START
        // button whose bottom edge sits at y=156). Everything here is the
        // original layout scaled up 20%.
        private const float BoxSize = 216f;
        private const float BoxGap = 7f;
        private const float SideVisible = 76f; // ~35% of a side box peeks into the viewport
        private const float ViewportWidth = BoxSize + 2f * (SideVisible + BoxGap);
        private const float SelectorY = 368f; // leaves room for the count label under the boxes
        private const float ArrowWidth = 115f;
        private const float ArrowGap = 29f;
        private const float SelectorHeight = 288f; // tall enough for the NEW badge peeking above a box

        private static readonly Color BadgeColor = new Color(0.91f, 0.30f, 0.24f, 1f);
        // Same gold as the "New Hat Unlocked!" toast - the seasonal voice
        private static readonly Color SeasonalGold = new Color(1f, 0.84f, 0.25f, 1f);

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
                Debug.LogError($"[HatSetup] Auto-setup failed (will retry when the editor is idle): {e}");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Setup Hats")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[HatSetup] Cannot run during play mode - exit play mode first.");
                return;
            }

            var uiRootObject = GameObject.Find("UI");
            if (uiRootObject == null)
            {
                Debug.LogError("[HatSetup] No 'UI' GameObject in the open scene - open SampleScene first.");
                return;
            }

            var manager = uiRootObject.GetComponent<UIManager>();
            if (manager == null)
            {
                Debug.LogError("[HatSetup] No UIManager on the 'UI' object.");
                return;
            }

            GenerateSprites();
            BuildPickupPrefab();
            AddHatEquipperToPlayer();

            AddHatPool();
            BuildSelector(uiRootObject.transform, manager);
            BuildSeasonalBanner(uiRootObject.transform);
            BuildCountdownInstructions(manager);
            HideHomeInstructions(uiRootObject.transform);

            // The version stamp is only earned by a REAL scene save - a run
            // that interleaves with play mode must retry, not silently burn
            // the stamp while its changes evaporate on play-exit
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[HatSetup] Play mode started mid-setup - not saving; will re-run when the editor is idle.");
                EditorApplication.delayCall += TryAutoRun;
                return;
            }

            EditorSceneManager.MarkSceneDirty(uiRootObject.scene);
            if (!string.IsNullOrEmpty(uiRootObject.scene.path))
                EditorSceneManager.SaveScene(uiRootObject.scene);

            EditorPrefs.SetInt(VersionPrefKey, SetupVersion);
            Debug.Log($"[HatSetup] Hat system setup applied (v{SetupVersion}) and saved to {uiRootObject.scene.path}.");
        }

        // ------------------------------------------------------------------
        // Generated UI sprites
        // ------------------------------------------------------------------

        private static void GenerateSprites()
        {
            EnsureFolder("Assets/Textures/UI");

            // Rounded selector box: translucent black fill, thin white stroke.
            // 9-sliced so the 128px art keeps its stroke weight at any size.
            const int size = 128;
            const float radius = 28f;
            const float stroke = 3.5f;
            const float margin = 2f;
            var box = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color[size * size];
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float px = x + 0.5f - half;
                    float py = y + 0.5f - half;
                    float dist = RoundedRectDistance(px, py, half - margin, half - margin, radius);

                    const float aa = 1.2f;
                    float inside = Mathf.Clamp01(0.5f - dist / aa);                    // whole shape
                    float insideCore = Mathf.Clamp01(0.5f - (dist + stroke) / aa);     // beyond the stroke band
                    float strokeAlpha = Mathf.Clamp01(inside - insideCore);
                    float fillAlpha = insideCore * 0.45f;

                    float outAlpha = strokeAlpha + fillAlpha * (1f - strokeAlpha);
                    float white = outAlpha > 0.001f ? strokeAlpha / outAlpha : 0f;
                    pixels[y * size + x] = new Color(white, white, white, outAlpha);
                }
            }
            box.SetPixels(pixels);
            WriteSprite(box, BoxSpritePath, new Vector4(44f, 44f, 44f, 44f));

            // The side-box fades are EdgeFadeGraphic vertex gradients now - a
            // textured sprite was one import setting away from rendering as a
            // solid block. Drop the old generated asset if it's still around.
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(EdgeFadeSpritePath) != null)
                AssetDatabase.DeleteAsset(EdgeFadeSpritePath);
        }

        /// <summary>Signed distance to a rounded rect centred at the origin (negative inside).</summary>
        private static float RoundedRectDistance(float x, float y, float halfW, float halfH, float radius)
        {
            float qx = Mathf.Abs(x) - (halfW - radius);
            float qy = Mathf.Abs(y) - (halfH - radius);
            float ax = Mathf.Max(qx, 0f);
            float ay = Mathf.Max(qy, 0f);
            return Mathf.Sqrt(ax * ax + ay * ay) + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius;
        }

        private static void WriteSprite(Texture2D texture, string path, Vector4 border)
        {
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);

            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"[HatSetup] Could not import generated sprite at {path}");
                return;
            }

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.spriteBorder = border;
            importer.SaveAndReimport();
        }

        // ------------------------------------------------------------------
        // Pickup prefab (pooled) - next-locked hat + the love note's beacon
        // ------------------------------------------------------------------

        private static void BuildPickupPrefab()
        {
            var root = new GameObject("HatPickup");
            try
            {
                root.tag = "Collectible";

                var capsule = root.AddComponent<CapsuleCollider>();
                capsule.isTrigger = true;
                capsule.radius = 0.625f;
                capsule.height = 2.5f;

                var collectible = root.AddComponent<HatCollectible>();
                root.AddComponent<ScrollableObject>();
                var animation = root.AddComponent<CollectibleAnimation>();

                // 3D hats read best with a slow turntable spin (the love note
                // disables spin because it's a flat sprite)
                var animSo = new SerializedObject(animation);
                var rotProp = animSo.FindProperty("rotationSpeed");
                if (rotProp != null)
                {
                    rotProp.floatValue = 80f;
                    animSo.ApplyModifiedPropertiesWithoutUndo();
                }

                // The hat model is instantiated under Visual at runtime
                // (HatCollectible.RefreshVisual) - lifted and enlarged so it
                // reads from spawn distance like the love note does
                var visual = new GameObject("Visual");
                visual.transform.SetParent(root.transform, false);
                visual.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                visual.transform.localScale = Vector3.one * 1.7f;

                BuildBeacon(root);

                var collectibleSo = new SerializedObject(collectible);
                collectibleSo.FindProperty("collectSound").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<AudioClip>(PickupSoundPath);
                collectibleSo.FindProperty("visualRoot").objectReferenceValue = visual.transform;
                collectibleSo.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(root, PickupPrefabPath);
                Debug.Log("[HatSetup] Built HatPickup prefab (beacon + turntable spin).");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Same radial beacon as the love note, sharing its materials.</summary>
        private static void BuildBeacon(GameObject root)
        {
            var particleShader = AssetDatabase.LoadAssetAtPath<Shader>(ParticleShaderPath);
            var sparkTex = AssetDatabase.LoadAssetAtPath<Texture2D>(SparkTexPath);
            var glowTex = AssetDatabase.LoadAssetAtPath<Texture2D>(GlowTexPath);
            if (particleShader == null || sparkTex == null || glowTex == null)
            {
                Debug.LogWarning("[HatSetup] Missing particle shader graph or VFX textures - pickup built without beacon.");
                return;
            }

            Material sparkMat = WorldSceneryBuilder.CreateParticleMat("Arc_VFX_NoteBurst", particleShader, sparkTex, Color.white);
            Material glowMat = WorldSceneryBuilder.CreateParticleMat("Arc_VFX_NoteGlow", particleShader, glowTex, new Color(1f, 1f, 1f, 0.6f));

            var beacon = new GameObject("AttentionBeacon");
            beacon.transform.SetParent(root.transform, false);
            // +Z is away from the camera - the hat occludes the burst centre
            beacon.transform.localPosition = new Vector3(0f, 0.55f, 0.2f);

            WorldSceneryBuilder.ConfigureBurst(WorldSceneryBuilder.CreateChildPs(beacon, "SparkBurst"), sparkMat);
            WorldSceneryBuilder.ConfigurePulse(WorldSceneryBuilder.CreateChildPs(beacon, "GlowPulse"), glowMat);
        }

        // ------------------------------------------------------------------
        // Player prefab
        // ------------------------------------------------------------------

        private static void AddHatEquipperToPlayer()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
            {
                Debug.LogWarning($"[HatSetup] Missing player prefab at {PlayerPrefabPath} - HatEquipper not added.");
                return;
            }

            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                if (root.GetComponent<HatEquipper>() == null)
                {
                    root.AddComponent<HatEquipper>();
                    PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                    Debug.Log("[HatSetup] Added HatEquipper to the Player prefab.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------
        // Object pool
        // ------------------------------------------------------------------

        private static void AddHatPool()
        {
            var pooler = Object.FindFirstObjectByType<ObjectPooler>(FindObjectsInactive.Include);
            if (pooler == null)
            {
                Debug.LogError("[HatSetup] No ObjectPooler in the scene - pool not added.");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PickupPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[HatSetup] Missing prefab at {PickupPrefabPath} - pool not added.");
                return;
            }

            var serialized = new SerializedObject(pooler);
            var pools = serialized.FindProperty("pools");

            // Update the existing entry if a re-run already added it
            for (int i = 0; i < pools.arraySize; i++)
            {
                var entry = pools.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("tag").stringValue == PoolTags.Hat)
                {
                    entry.FindPropertyRelative("prefab").objectReferenceValue = prefab;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    return;
                }
            }

            pools.arraySize++;
            var newEntry = pools.GetArrayElementAtIndex(pools.arraySize - 1);
            newEntry.FindPropertyRelative("tag").stringValue = PoolTags.Hat;
            newEntry.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            // Head-room for the debug 100% spawn chance
            newEntry.FindPropertyRelative("size").intValue = 8;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pooler);
            Debug.Log("[HatSetup] Added 'Hat' pool (size 8) to ObjectPooler.");
        }

        // ------------------------------------------------------------------
        // Home-screen selector carousel
        // ------------------------------------------------------------------

        private static void BuildSelector(Transform uiRoot, UIManager manager)
        {
            Transform home = uiRoot.Find("HomeScreen");
            if (home == null)
            {
                Debug.LogError("[HatSetup] Missing 'UI/HomeScreen' canvas - selector not built.");
                return;
            }

            RemoveExisting(home, "HatSelector");

            var markerFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MarkerFontPath);
            var boxSprite = AssetDatabase.LoadAssetAtPath<Sprite>(BoxSpritePath);
            var badgeSprite = AssetDatabase.LoadAssetAtPath<Sprite>(RoundedSpritePath);

            float containerWidth = ViewportWidth + 2f * (ArrowWidth + ArrowGap);
            var container = CreateRect("HatSelector", home);
            SetRect(container, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(containerWidth, SelectorHeight), new Vector2(0f, SelectorY));

            // The notes panel is a full-screen overlay - keep drawing over the selector
            var notesPanel = home.Find("LoveNotesPanel");
            if (notesPanel != null)
                container.transform.SetSiblingIndex(notesPanel.GetSiblingIndex());

            // Masked viewport so the side boxes are cropped to their peek
            // Tall enough that the NEW badge peeking above a box survives the
            // mask. Horizontal softness feathers the clip edges, so the side
            // boxes dissolve to transparent toward the viewport edges - an
            // opacity mask, not a painted-on black overlay. The centre box
            // ends 5px shy of the feather zone and stays fully opaque.
            var viewport = CreateRect("Boxes", container.transform);
            SetRect(viewport, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(ViewportWidth, SelectorHeight), Vector2.zero);
            var mask = viewport.AddComponent<RectMask2D>();
            mask.softness = new Vector2Int(77, 0);

            float slotSpacing = BoxSize + BoxGap;
            GameObject leftBox = BuildBox(viewport.transform, "BoxLeft", -slotSpacing, boxSprite, badgeSprite, markerFont,
                out RawImage leftThumb, out TextMeshProUGUI leftLock, out TextMeshProUGUI leftNone,
                out TextMeshProUGUI leftHoliday, out GameObject leftBadge);
            GameObject centerBox = BuildBox(viewport.transform, "BoxCenter", 0f, boxSprite, badgeSprite, markerFont,
                out RawImage centerThumb, out TextMeshProUGUI centerLock, out TextMeshProUGUI centerNone,
                out TextMeshProUGUI centerHoliday, out GameObject centerBadge);
            GameObject rightBox = BuildBox(viewport.transform, "BoxRight", slotSpacing, boxSprite, badgeSprite, markerFont,
                out RawImage rightThumb, out TextMeshProUGUI rightLock, out TextMeshProUGUI rightNone,
                out TextMeshProUGUI rightHoliday, out GameObject rightBadge);

            // Unlock tally under the boxes, marker-voiced like the love notes count
            var count = CreateRect("Count", container.transform);
            SetRect(count, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(360f, 53f), new Vector2(0f, -149f));
            TextMeshProUGUI countText = AddText(count, "0/0", markerFont, 48f, Color.white, TextAlignmentOptions.Center);

            Button leftArrow = BuildArrow(container.transform, "ArrowLeft", "<",
                -(ViewportWidth / 2f + ArrowGap + ArrowWidth / 2f), markerFont, manager);
            Button rightArrow = BuildArrow(container.transform, "ArrowRight", ">",
                ViewportWidth / 2f + ArrowGap + ArrowWidth / 2f, markerFont, manager);

            var selector = container.AddComponent<HatSelectorUI>();
            var serialized = new SerializedObject(selector);
            serialized.FindProperty("leftArrow").objectReferenceValue = leftArrow;
            serialized.FindProperty("rightArrow").objectReferenceValue = rightArrow;
            serialized.FindProperty("leftThumb").objectReferenceValue = leftThumb;
            serialized.FindProperty("centerThumb").objectReferenceValue = centerThumb;
            serialized.FindProperty("rightThumb").objectReferenceValue = rightThumb;
            serialized.FindProperty("leftLock").objectReferenceValue = leftLock;
            serialized.FindProperty("centerLock").objectReferenceValue = centerLock;
            serialized.FindProperty("rightLock").objectReferenceValue = rightLock;
            serialized.FindProperty("leftNone").objectReferenceValue = leftNone;
            serialized.FindProperty("centerNone").objectReferenceValue = centerNone;
            serialized.FindProperty("rightNone").objectReferenceValue = rightNone;
            serialized.FindProperty("leftHoliday").objectReferenceValue = leftHoliday;
            serialized.FindProperty("centerHoliday").objectReferenceValue = centerHoliday;
            serialized.FindProperty("rightHoliday").objectReferenceValue = rightHoliday;
            serialized.FindProperty("countText").objectReferenceValue = countText;
            serialized.FindProperty("leftBadge").objectReferenceValue = leftBadge;
            serialized.FindProperty("centerBadge").objectReferenceValue = centerBadge;
            serialized.FindProperty("rightBadge").objectReferenceValue = rightBadge;
            serialized.FindProperty("centerBox").objectReferenceValue = (RectTransform)centerBox.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[HatSetup] Built the home-screen hat selector.");
        }

        private static GameObject BuildBox(Transform parent, string name, float x, Sprite boxSprite,
            Sprite badgeSprite, TMP_FontAsset markerFont,
            out RawImage thumb, out TextMeshProUGUI lockMark, out TextMeshProUGUI noneMark,
            out TextMeshProUGUI holidayMark, out GameObject badge)
        {
            var box = CreateRect(name, parent);
            SetRect(box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(BoxSize, BoxSize), new Vector2(x, 0f));
            var boxImage = box.AddComponent<Image>();
            boxImage.sprite = boxSprite;
            boxImage.type = Image.Type.Sliced;
            boxImage.raycastTarget = false;

            var thumbObject = CreateRect("Thumb", box.transform);
            SetRect(thumbObject, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(-38f, -38f), Vector2.zero);
            thumb = thumbObject.AddComponent<RawImage>();
            thumb.raycastTarget = false;
            thumb.enabled = false; // empty "no hat" slot shows the bare box

            var lockObject = CreateRect("Lock", box.transform);
            SetRect(lockObject, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            lockMark = AddText(lockObject, "?", markerFont, 110f, Color.white, TextAlignmentOptions.Center);
            lockObject.SetActive(false);

            // The "no hat" slot reads as a word, not an empty box
            var noneObject = CreateRect("None", box.transform);
            SetRect(noneObject, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                Vector2.zero, Vector2.zero);
            noneMark = AddText(noneObject, "None", markerFont, 53f, Color.white, TextAlignmentOptions.Center);
            noneObject.SetActive(false);

            // Locked seasonal hats wear their holiday as a diagonal sash,
            // corner to corner, in place of the "?" - the silhouette plus the
            // holiday name says exactly what to come back for. Flush
            // alignment letter-spaces the word across the whole sash.
            var holidayObject = CreateRect("Holiday", box.transform);
            SetRect(holidayObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(BoxSize * 1.25f, 56f), Vector2.zero);
            holidayObject.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            holidayMark = AddText(holidayObject, "", markerFont, 40f, SeasonalGold, TextAlignmentOptions.Flush);
            holidayMark.enableAutoSizing = true;
            holidayMark.fontSizeMin = 22f;
            holidayMark.fontSizeMax = 46f;
            holidayObject.SetActive(false);

            // NEW badge above the box - the love notes button's red dot with a
            // black stroke (rounded-9 corners at half size render as a circle)
            badge = CreateRect("NewBadge", box.transform);
            SetRect(badge, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
                new Vector2(43f, 43f), new Vector2(0f, 12f));
            var badgeOutline = badge.AddComponent<Image>();
            badgeOutline.sprite = badgeSprite;
            badgeOutline.type = Image.Type.Sliced;
            badgeOutline.pixelsPerUnitMultiplier = 80f / 21.6f;
            badgeOutline.color = Color.black;
            badgeOutline.raycastTarget = false;

            var badgeFill = CreateRect("Fill", badge.transform);
            SetRect(badgeFill, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(31f, 31f), Vector2.zero);
            var badgeFillImage = badgeFill.AddComponent<Image>();
            badgeFillImage.sprite = badgeSprite;
            badgeFillImage.type = Image.Type.Sliced;
            badgeFillImage.pixelsPerUnitMultiplier = 80f / 15.6f;
            badgeFillImage.color = BadgeColor;
            badgeFillImage.raycastTarget = false;

            badge.SetActive(false);
            return box;
        }

        private static Button BuildArrow(Transform parent, string name, string glyph, float x,
            TMP_FontAsset markerFont, UIManager manager)
        {
            var arrowObject = CreateRect(name, parent);
            SetRect(arrowObject, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(ArrowWidth, 180f), new Vector2(x, 0f));

            // Invisible image keeps the whole area tappable
            var tapArea = arrowObject.AddComponent<Image>();
            tapArea.color = new Color(0f, 0f, 0f, 0f);

            var button = arrowObject.AddComponent<Button>();
            button.targetGraphic = tapArea;
            button.transition = Selectable.Transition.None; // JuicyButton owns the press feel

            var juicy = arrowObject.AddComponent<Effects.JuicyButton>();
            CopyJuicyClick(manager, juicy);

            var label = CreateRect("Text", arrowObject.transform);
            SetRect(label, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            AddText(label, glyph, markerFont, 101f, Color.white, TextAlignmentOptions.Center);

            return button;
        }

        /// <summary>Give the arrows the same soft click every other button has.</summary>
        private static void CopyJuicyClick(UIManager manager, Effects.JuicyButton target)
        {
            var managerSo = new SerializedObject(manager);
            var startButton = managerSo.FindProperty("startButton")?.objectReferenceValue as Button;
            var template = startButton != null ? startButton.GetComponent<Effects.JuicyButton>() : null;
            if (template == null)
                return;

            var templateSo = new SerializedObject(template);
            var targetSo = new SerializedObject(target);
            targetSo.FindProperty("clickSound").objectReferenceValue =
                templateSo.FindProperty("clickSound").objectReferenceValue;
            targetSo.FindProperty("clickVolume").floatValue =
                templateSo.FindProperty("clickVolume").floatValue;
            targetSo.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------
        // Seasonal "Limited Time" banner
        // ------------------------------------------------------------------

        /// <summary>
        /// The pulsing limited-run callout above the hat selector. The host
        /// stays active whenever the home screen is up; SeasonalHatBanner
        /// drives the label's text, visibility and breathing scale.
        /// </summary>
        private static void BuildSeasonalBanner(Transform uiRoot)
        {
            Transform home = uiRoot.Find("HomeScreen");
            if (home == null)
            {
                Debug.LogError("[HatSetup] Missing 'UI/HomeScreen' canvas - seasonal banner not built.");
                return;
            }

            RemoveExisting(home, "SeasonalBanner");

            var markerFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(MarkerFontPath);

            // Sits in the gap between the enlarged selector's badge row
            // (~y 670) and the dog; wide enough that the two-line callout
            // never wraps ugly
            var host = CreateRect("SeasonalBanner", home);
            SetRect(host, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(960f, 150f), new Vector2(0f, 684f));

            // The notes panel is a full-screen overlay - keep drawing over this
            var notesPanel = home.Find("LoveNotesPanel");
            if (notesPanel != null)
                host.transform.SetSiblingIndex(notesPanel.GetSiblingIndex());

            var labelObject = CreateRect("Label", host.transform);
            SetRect(labelObject, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            TextMeshProUGUI label = AddText(labelObject, "", markerFont, 40f, SeasonalGold, TextAlignmentOptions.Center);
            label.enableAutoSizing = true;
            label.fontSizeMin = 26f;
            label.fontSizeMax = 42f;
            labelObject.SetActive(false); // SeasonalHatBanner shows it while a window is open

            var banner = host.AddComponent<SeasonalHatBanner>();
            var serialized = new SerializedObject(banner);
            serialized.FindProperty("label").objectReferenceValue = label;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[HatSetup] Seasonal banner built and wired.");
        }

        // ------------------------------------------------------------------
        // Countdown instructions (replaces the home screen Instructions text)
        // ------------------------------------------------------------------

        private static void BuildCountdownInstructions(UIManager manager)
        {
            var serialized = new SerializedObject(manager);
            var panel = serialized.FindProperty("countdownPanel")?.objectReferenceValue as GameObject;
            if (panel == null)
            {
                Debug.LogWarning("[HatSetup] UIManager.countdownPanel not wired - countdown instructions skipped.");
                return;
            }

            // Match the countdown number's font (Barlow Bold after the font pass)
            var countdownText = serialized.FindProperty("countdownText")?.objectReferenceValue as TextMeshProUGUI;
            TMP_FontAsset font = countdownText != null ? countdownText.font : null;

            RemoveExisting(panel.transform, "Instructions");
            var line = CreateRect("Instructions", panel.transform);
            SetRect(line, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(800f, 160f), new Vector2(0f, -64f));
            AddText(line, "Tap to sprint\nSwipe to move", font, 48f, Color.white, TextAlignmentOptions.Center);
            line.SetActive(false); // CountdownRoutine turns it on for a run's first countdown

            serialized.FindProperty("countdownInstructions").objectReferenceValue = line;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(manager);
            Debug.Log("[HatSetup] Countdown instructions line built and wired.");
        }

        private static void HideHomeInstructions(Transform uiRoot)
        {
            var instructions = uiRoot.Find("HomeScreen/Instructions");
            if (instructions != null && instructions.gameObject.activeSelf)
            {
                instructions.gameObject.SetActive(false);
                Debug.Log("[HatSetup] Home screen Instructions hidden (now shown under the first countdown).");
            }
        }

        // ------------------------------------------------------------------
        // Helpers (same idioms as LoveNoteSetup)
        // ------------------------------------------------------------------

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            string leaf = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
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
