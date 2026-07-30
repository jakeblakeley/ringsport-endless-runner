using System.IO;
using RingSport.Core;
using RingSport.Effects;
using RingSport.Player;
using RingSport.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace RingSport.Editor
{
    /// <summary>
    /// Wires the Tier-1 juice pass into the scene and Player prefab:
    /// - Assigns the previously-silent mini-level AudioClip fields (flee/face
    ///   catch, stop-attack whistle, QTE tap, food-refusal collect) plus the
    ///   new death-impact, countdown tick/GO and landing clips. All picks are
    ///   TEMPORARY stand-ins from clips already in the repo - the wishlist for
    ///   the real audio pass lives in SOUND_EFFECTS.md at the repo root.
    ///   Already-assigned fields are never overwritten.
    /// - Builds the shared "ImpactVFX" scene object (landing dust puffs +
    ///   finish-line confetti burst), following the CollectVFX pattern.
    /// - Wires LevelManager's FINISH! banner font (BarlowCondensed).
    ///
    /// Runs automatically once after compilation (version-gated); re-run from
    /// Tools/RingSport/Setup Juice Polish.
    /// </summary>
    public static class JuicePolishSetup
    {
        // Bump to force the auto-run to re-apply the setup
        private const int SetupVersion = 3;
        private const string VersionPrefKey = "RingSport.JuicePolishSetup.Version";

        private const string TextureFolder = "Assets/Textures/VFX";
        private const string GlowTexturePath = TextureFolder + "/SoftGlow.png"; // shared with ParticlePolishSetup
        private const string ConfettiTexturePath = TextureFolder + "/ConfettiQuad.png";
        private const string MaterialFolder = "Assets/Materials/Effects";
        private const string DustMaterialPath = MaterialFolder + "/ImpactDust.mat";
        private const string ConfettiMaterialPath = MaterialFolder + "/ConfettiQuad.mat";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string VFXRootName = "ImpactVFX";
        private const string BannerFontPath = "Assets/Fonts/BarlowCondensed-SemiBold SDF.asset";

        // Temporary clip picks (see SOUND_EFFECTS.md for the real wishlist)
        private const string ClipBiteTackle = "Assets/Sounds/Decoy bite/bite-tackle.wav";
        private const string ClipBiteImpact = "Assets/Sounds/Decoy bite/bite-impact1.wav";
        private const string ClipWhistle = "Assets/Malbers Animations/Common/Audio/Whistle/Whistle Stop.wav";
        private const string ClipRewardCollect = "Assets/Sounds/Reward/reward-collect.wav";
        private const string ClipUiImpact = "Assets/Sounds/UI/ui-impact.wav";
        private const string ClipRewardPop = "Assets/Sounds/Reward/reward-pop.wav";
        private const string ClipRewardBell = "Assets/Sounds/Reward/reward-bell.wav";
        private const string ClipLandThump = "Assets/Sounds/Dog/dog-footsteps3.wav";
        private const string ClipUiImpact2 = "Assets/Sounds/UI/ui-impact2.wav";
        private const string ClipThock = "Assets/Sounds/Dog/dog-footsteps5.wav";
        private const string ClipBark = "Assets/Sounds/Dog/dog-bark.wav";
        private const string ClipTaduh = "Assets/Sounds/Meme/meme-taduh.wav";
        private const string ClipScream1 = "Assets/Sounds/Decoy bite/decoy-scream1.wav";
        private const string ClipScream2 = "Assets/Sounds/Decoy bite/decoy-scream2.wav";
        private const string ClipSqueaker1 = "Assets/Sounds/Reward/reward-squeaker1.wav";
        private const string ClipRewardCoin = "Assets/Sounds/Reward/reward-coin.wav";
        private const string ClipBruh = "Assets/Sounds/Meme/meme-bruh.wav";
        private const string LoveNotePrefabPath = "Assets/Prefabs/Collectibles/LoveNote.prefab";

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

            // Only the game scene has the player
            if (Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include) == null)
                return;

            try
            {
                Run();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[JuicePolishSetup] Auto-setup failed (will retry when the editor is idle): {e}");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Setup Juice Polish")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[JuicePolishSetup] Cannot run during play mode - exit play mode first.");
                return;
            }

            var player = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
            if (player == null)
            {
                Debug.LogError("[JuicePolishSetup] No PlayerController in the open scene - open SampleScene first.");
                return;
            }

            WireSceneAudio();
            WireBannerFont();
            WirePlayerPrefab();
            WireLoveNotePrefab();
            AddJuicyButtons();
            BuildImpactVFX();

            AssetDatabase.SaveAssets();

            var scene = player.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!string.IsNullOrEmpty(scene.path))
                EditorSceneManager.SaveScene(scene);

            EditorPrefs.SetInt(VersionPrefKey, SetupVersion);
            Debug.Log($"[JuicePolishSetup] Juice polish applied (v{SetupVersion}): mini-level audio wired, ImpactVFX built, banner font set. Temp clips listed in SOUND_EFFECTS.md.");
        }

        // ------------------------------------------------------------------
        // Audio wiring: fill the silent serialized clip fields (never stomps
        // an already-assigned clip)
        // ------------------------------------------------------------------

        private static void WireSceneAudio()
        {
            var fleeAttack = Object.FindAnyObjectByType<MiniLevelFleeAttack>(FindObjectsInactive.Include);
            WireClip(fleeAttack, "catchSound", ClipBiteTackle);
            WireClip(fleeAttack, "catchScreamSound", ClipScream1);

            WireClip(Object.FindAnyObjectByType<MiniLevelStopAttack>(FindObjectsInactive.Include), "whistleSound", ClipWhistle);

            var faceAttack = Object.FindAnyObjectByType<MiniLevelFaceAttack>(FindObjectsInactive.Include);
            WireClip(faceAttack, "tapSound", ClipBiteImpact);
            WireClip(faceAttack, "catchSound", ClipBiteTackle);
            WireClip(faceAttack, "catchScreamSound", ClipScream2);
            WireClip(faceAttack, "windowTickSound", ClipRewardPop);

            WireClip(Object.FindAnyObjectByType<MiniLevelFoodRefusal>(FindObjectsInactive.Include), "collectSound", ClipRewardCollect);

            var palisade = Object.FindAnyObjectByType<PalisadeMinigame>(FindObjectsInactive.Include);
            WireClip(palisade, "wallHitSound", ClipUiImpact2);
            WireClip(palisade, "tapThockSound", ClipThock);
            WireClip(palisade, "timerTickSound", ClipRewardPop);
            WireClip(palisade, "successBarkSound", ClipBark);

            WireClip(Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include), "deathImpactSound", ClipUiImpact);

            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            WireClip(uiManager, "countdownTickSound", ClipRewardPop);
            WireClip(uiManager, "countdownGoSound", ClipRewardBell);
            WireClip(uiManager, "newHighScoreSound", ClipTaduh);

            WireClip(Object.FindAnyObjectByType<SecretNotePanel>(FindObjectsInactive.Include), "revealSound", ClipTaduh);

            var simonSays = Object.FindAnyObjectByType<MiniLevelPositionsSimonSays>(FindObjectsInactive.Include);
            WireClip(simonSays, "poseToneSound", ClipRewardPop);
            WireClip(simonSays, "correctSound", ClipRewardCoin);
            WireClip(simonSays, "wrongSound", ClipBruh);
        }

        /// <summary>
        /// JuicyButton (press scale + soft click) on every scene button. The
        /// primary actions (START, Retry) also get the idle attention pulse.
        /// The secret-note scrim is skipped - it's a full-screen dismiss
        /// button, and scaling the whole screen on press would look broken.
        /// </summary>
        private static void AddJuicyButtons()
        {
            Button startButton = null;
            Button retryButton = null;
            var uiManager = Object.FindAnyObjectByType<UIManager>(FindObjectsInactive.Include);
            if (uiManager != null)
            {
                var uiSerialized = new SerializedObject(uiManager);
                startButton = uiSerialized.FindProperty("startButton")?.objectReferenceValue as Button;
                retryButton = uiSerialized.FindProperty("retryButton")?.objectReferenceValue as Button;
            }

            var clickClip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipRewardPop);
            int added = 0;

            foreach (var button in Object.FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (button.gameObject.name == "Scrim")
                    continue;

                var juicy = button.GetComponent<JuicyButton>();
                if (juicy == null)
                {
                    juicy = button.gameObject.AddComponent<JuicyButton>();
                    added++;
                }

                var serialized = new SerializedObject(juicy);
                var clickProp = serialized.FindProperty("clickSound");
                if (clickProp != null && clickProp.objectReferenceValue == null)
                    clickProp.objectReferenceValue = clickClip;
                var pulseProp = serialized.FindProperty("idlePulse");
                if (pulseProp != null)
                    pulseProp.boolValue = button == startButton || button == retryButton;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(juicy);
            }

            Debug.Log($"[JuicePolishSetup] JuicyButton on scene buttons (added {added}; START/Retry get the idle pulse).");
        }

        /// <summary>
        /// The love note shipped sharing the mega coin's squeaker; give it its
        /// own clip. Deliberately swaps the known-shared clip (a null check
        /// alone would never fire here) but respects any other custom pick.
        /// </summary>
        private static void WireLoveNotePrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(LoveNotePrefabPath) == null)
            {
                Debug.LogWarning($"[JuicePolishSetup] No LoveNote prefab at {LoveNotePrefabPath} - chime not swapped.");
                return;
            }

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(ClipSqueaker1);
            if (clip == null)
            {
                Debug.LogWarning($"[JuicePolishSetup] Clip not found: {ClipSqueaker1} - love-note chime not swapped.");
                return;
            }

            var prefabRoot = PrefabUtility.LoadPrefabContents(LoveNotePrefabPath);
            try
            {
                var collectible = prefabRoot.GetComponent<RingSport.Level.LoveNoteCollectible>();
                if (collectible == null)
                    return;

                var serialized = new SerializedObject(collectible);
                var prop = serialized.FindProperty("collectSound");
                if (prop == null)
                    return;

                var current = prop.objectReferenceValue as AudioClip;
                if (current != null && current.name != "reward-squeaker2")
                    return; // someone picked a custom clip - leave it

                prop.objectReferenceValue = clip;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(prefabRoot, LoveNotePrefabPath);
                Debug.Log($"[JuicePolishSetup] LoveNote pickup chime -> {clip.name} (was sharing the mega coin's squeaker).");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static void WireBannerFont()
        {
            var levelManager = Object.FindAnyObjectByType<LevelManager>(FindObjectsInactive.Include);
            if (levelManager == null)
            {
                Debug.LogWarning("[JuicePolishSetup] No LevelManager in scene - banner font not wired.");
                return;
            }

            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(BannerFontPath);
            if (font == null)
            {
                Debug.LogWarning($"[JuicePolishSetup] Banner font not found at {BannerFontPath}.");
                return;
            }

            var serialized = new SerializedObject(levelManager);
            var prop = serialized.FindProperty("bannerFont");
            if (prop != null && prop.objectReferenceValue == null)
            {
                prop.objectReferenceValue = font;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(levelManager);
            }
        }

        private static void WirePlayerPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
            {
                Debug.LogWarning($"[JuicePolishSetup] No Player prefab at {PlayerPrefabPath} - landing thump not wired.");
                return;
            }

            var prefabRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var controller = prefabRoot.GetComponent<PlayerController>();
                if (controller != null && WireClip(controller, "landSound", ClipLandThump))
                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        private static bool WireClip(Component target, string property, string clipPath)
        {
            if (target == null)
            {
                Debug.LogWarning($"[JuicePolishSetup] Missing component for '{property}' - not wired.");
                return false;
            }

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            if (clip == null)
            {
                Debug.LogWarning($"[JuicePolishSetup] Clip not found: {clipPath} - {target.GetType().Name}.{property} left empty.");
                return false;
            }

            var serialized = new SerializedObject(target);
            var prop = serialized.FindProperty(property);
            if (prop == null)
            {
                Debug.LogWarning($"[JuicePolishSetup] {target.GetType().Name} has no serialized field '{property}'.");
                return false;
            }

            if (prop.objectReferenceValue != null)
                return false; // respect existing assignments

            prop.objectReferenceValue = clip;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
            Debug.Log($"[JuicePolishSetup] Wired {target.GetType().Name}.{property} = {clip.name} (temporary - see SOUND_EFFECTS.md)");
            return true;
        }

        // ------------------------------------------------------------------
        // ImpactVFX: shared Dust + Confetti systems (CollectVFX pattern)
        // ------------------------------------------------------------------

        private static void BuildImpactVFX()
        {
            EnsureFolder("Assets/Textures");
            EnsureFolder(TextureFolder);
            EnsureFolder("Assets/Materials");
            EnsureFolder(MaterialFolder);

            if (AssetDatabase.LoadAssetAtPath<Texture2D>(GlowTexturePath) == null)
                GenerateGlowTexture(GlowTexturePath);
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(ConfettiTexturePath) == null)
                GenerateConfettiTexture(ConfettiTexturePath);

            var dustMaterial = CreateAlphaParticleMaterial(DustMaterialPath, GlowTexturePath);
            var confettiMaterial = CreateAlphaParticleMaterial(ConfettiMaterialPath, ConfettiTexturePath);
            if (dustMaterial == null || confettiMaterial == null)
                return;

            var existing = GameObject.Find(VFXRootName);
            if (existing != null)
                Object.DestroyImmediate(existing);

            var root = new GameObject(VFXRootName);
            var vfx = root.AddComponent<ImpactVFX>();

            // Dust: soft alpha-blended puffs that expand and thin out
            var dust = CreateBurstSystem(root.transform, "Dust", dustMaterial, 256);
            var dustMain = dust.main;
            dustMain.gravityModifier = 0.02f; // hangs, barely settles

            var dustColor = dust.colorOverLifetime;
            dustColor.enabled = true;
            dustColor.color = new ParticleSystem.MinMaxGradient(FadeGradient(0.9f, 0.15f));

            var dustSize = dust.sizeOverLifetime;
            dustSize.enabled = true;
            dustSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.6f),
                new Keyframe(0.3f, 1f),
                new Keyframe(1f, 1.5f))); // puffs grow as they dissipate

            // Confetti: opaque colored squares raining under gravity
            var confetti = CreateBurstSystem(root.transform, "Confetti", confettiMaterial, 512);
            var confettiMain = confetti.main;
            confettiMain.gravityModifier = 0.85f;

            var confettiColor = confetti.colorOverLifetime;
            confettiColor.enabled = true;
            confettiColor.color = new ParticleSystem.MinMaxGradient(FadeGradient(1f, 0.75f));

            var serialized = new SerializedObject(vfx);
            serialized.FindProperty("dust").objectReferenceValue = dust;
            serialized.FindProperty("confetti").objectReferenceValue = confetti;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[JuicePolishSetup] Built ImpactVFX (shared Dust + Confetti systems).");
        }

        private static ParticleSystem CreateBurstSystem(Transform parent, string name, Material material, int maxParticles)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.playOnAwake = true; // must be playing for Emit() to simulate
            main.startSpeed = 0f;
            main.startLifetime = 0.5f;
            main.startSize = 0.2f;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true; // finish the pop even when a state freezes time
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f; // manual Emit() only

            var shape = ps.shape;
            shape.enabled = false;

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortMode = ParticleSystemSortMode.None;
            return ps;
        }

        // ------------------------------------------------------------------
        // Textures & materials
        // ------------------------------------------------------------------

        /// <summary>Radial glow (same look ParticlePolishSetup generates).</summary>
        private static void GenerateGlowTexture(string path)
        {
            WriteTexture(path, 64, (u, v) =>
            {
                float radius = Mathf.Sqrt(u * u + v * v);
                return Mathf.Pow(Mathf.Clamp01(1f - radius), 2.2f);
            });
        }

        /// <summary>Solid square with a soft edge - a confetti card.</summary>
        private static void GenerateConfettiTexture(string path)
        {
            WriteTexture(path, 32, (u, v) =>
            {
                float edge = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));
                return Mathf.Clamp01((0.92f - edge) / 0.12f);
            });
        }

        private static void WriteTexture(string path, int size, System.Func<float, float, float> alphaAt)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size * 2f - 1f;
                    float v = (y + 0.5f) / size * 2f - 1f;
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(alphaAt(u, v)) * 255f);
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        /// <summary>URP Particles/Unlit, standard alpha blend (dust and confetti want body, not additive glow).</summary>
        private static Material CreateAlphaParticleMaterial(string path, string texturePath)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogError("[JuicePolishSetup] URP Particles/Unlit shader not found.");
                return null;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath));
            material.SetColor("_BaseColor", Color.white);

            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f); // BaseShaderGUI.BlendMode.Alpha
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;

            EditorUtility.SetDirty(material);
            return material;
        }

        /// <summary>White gradient holding full alpha until holdUntil, then fading to 0.</summary>
        private static Gradient FadeGradient(float startAlpha, float holdUntil)
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(startAlpha, 0f),
                    new GradientAlphaKey(startAlpha, holdUntil),
                    new GradientAlphaKey(0f, 1f)
                });
            return gradient;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            var name = Path.GetFileName(path);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
