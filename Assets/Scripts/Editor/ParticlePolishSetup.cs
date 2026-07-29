using System.IO;
using RingSport.Effects;
using RingSport.Level;
using RingSport.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace RingSport.Editor
{
    /// <summary>
    /// Builds the pickup-burst and sprint-trail particle polish:
    /// - Procedural sparkle + glow textures and two additive URP particle materials
    /// - "CollectVFX" scene object: shared Sparks/Flash systems all pickups Emit()
    ///   into (see CollectBurstVFX) - two draw calls total, low-end safe
    /// - "SprintTrail" child inside the Player prefab: stretched-billboard white
    ///   speed lines toggled by PlayerController while sprinting
    /// - Marks the MegaCollectible prefab as a large coin so its burst is bigger
    ///
    /// Runs automatically once after compilation (version-gated); re-run from
    /// Tools/RingSport/Setup Particle Polish.
    /// </summary>
    public static class ParticlePolishSetup
    {
        // Bump to force the auto-run to re-apply the setup
        private const int SetupVersion = 1;
        private const string VersionPrefKey = "RingSport.ParticlePolishSetup.Version";

        private const string TextureFolder = "Assets/Textures/VFX";
        private const string SparkTexturePath = TextureFolder + "/SparkStar.png";
        private const string GlowTexturePath = TextureFolder + "/SoftGlow.png";
        private const string MaterialFolder = "Assets/Materials/Effects";
        private const string SparkMaterialPath = MaterialFolder + "/CollectSpark.mat";
        private const string GlowMaterialPath = MaterialFolder + "/CollectGlow.mat";
        private const string MegaPrefabPath = "Assets/Prefabs/Collectibles/MegaCollectible.prefab";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string VFXRootName = "CollectVFX";
        private const string TrailName = "SprintTrail";

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
                Debug.LogError($"[ParticlePolishSetup] Auto-setup failed (will retry when the editor is idle): {e}");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Setup Particle Polish")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[ParticlePolishSetup] Cannot run during play mode - exit play mode first.");
                return;
            }

            var player = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
            if (player == null)
            {
                Debug.LogError("[ParticlePolishSetup] No PlayerController in the open scene - open SampleScene first.");
                return;
            }

            EnsureFolder("Assets/Textures");
            EnsureFolder(TextureFolder);
            EnsureFolder("Assets/Materials");
            EnsureFolder(MaterialFolder);

            GenerateSparkleTexture(SparkTexturePath);
            GenerateGlowTexture(GlowTexturePath);

            var sparkMaterial = CreateAdditiveParticleMaterial(SparkMaterialPath, SparkTexturePath);
            var glowMaterial = CreateAdditiveParticleMaterial(GlowMaterialPath, GlowTexturePath);
            if (sparkMaterial == null || glowMaterial == null)
                return;

            BuildCollectVFX(sparkMaterial, glowMaterial);
            BuildSprintTrail(glowMaterial);
            MarkMegaCoinLarge();

            AssetDatabase.SaveAssets();

            var scene = player.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!string.IsNullOrEmpty(scene.path))
                EditorSceneManager.SaveScene(scene);

            EditorPrefs.SetInt(VersionPrefKey, SetupVersion);
            Debug.Log($"[ParticlePolishSetup] Particle polish applied (v{SetupVersion}): CollectVFX in scene, SprintTrail in Player prefab.");
        }

        // ------------------------------------------------------------------
        // Textures: tiny procedural sprites so the effect ships without art
        // ------------------------------------------------------------------

        /// <summary>Soft 4-point sparkle: diamond falloff with a hot core.</summary>
        private static void GenerateSparkleTexture(string path)
        {
            WriteTexture(path, 64, (u, v) =>
            {
                float diamond = Mathf.Clamp01(1f - (Mathf.Abs(u) + Mathf.Abs(v)));
                float alpha = Mathf.Pow(diamond, 1.8f);
                float radius = Mathf.Sqrt(u * u + v * v);
                float core = Mathf.Pow(Mathf.Max(0f, 1f - radius * 3f), 2f) * 0.6f;
                return Mathf.Clamp01(alpha + core);
            });
        }

        /// <summary>Radial glow for the pickup flash and the sprint speed lines.</summary>
        private static void GenerateGlowTexture(string path)
        {
            WriteTexture(path, 64, (u, v) =>
            {
                float radius = Mathf.Sqrt(u * u + v * v);
                return Mathf.Pow(Mathf.Clamp01(1f - radius), 2.2f);
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

        // ------------------------------------------------------------------
        // Materials: URP Particles/Unlit, additive - cheap and juicy
        // ------------------------------------------------------------------

        private static Material CreateAdditiveParticleMaterial(string path, string texturePath)
        {
            var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogError("[ParticlePolishSetup] URP Particles/Unlit shader not found.");
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

            // Transparent surface, additive blend (the URP shader reads these floats)
            material.SetOverrideTag("RenderType", "Transparent");
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 2f); // BaseShaderGUI.BlendMode.Additive
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.One);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;

            EditorUtility.SetDirty(material);
            return material;
        }

        // ------------------------------------------------------------------
        // CollectVFX: one shared Sparks + Flash pair for every pickup burst
        // ------------------------------------------------------------------

        private static void BuildCollectVFX(Material sparkMaterial, Material glowMaterial)
        {
            var existing = GameObject.Find(VFXRootName);
            if (existing != null)
                Object.DestroyImmediate(existing);

            var root = new GameObject(VFXRootName);
            var vfx = root.AddComponent<CollectBurstVFX>();

            // Sparks: radial sparkles. All per-burst variation (count, speed,
            // size, color, lifetime) comes from CollectBurstVFX's EmitParams;
            // the modules here only shape the shared look.
            var sparks = CreateBurstSystem(root.transform, "Sparks", sparkMaterial, 512);
            var sparksMain = sparks.main;
            sparksMain.gravityModifier = 0.6f; // pop up, then a slight confetti fall

            var sparkColor = sparks.colorOverLifetime;
            sparkColor.enabled = true;
            sparkColor.color = new ParticleSystem.MinMaxGradient(FadeGradient(1f, 0.55f));

            var sparkSize = sparks.sizeOverLifetime;
            sparkSize.enabled = true;
            sparkSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.7f),
                new Keyframe(0.12f, 1.15f),  // overshoot pop-in
                new Keyframe(0.6f, 0.9f),
                new Keyframe(1f, 0f)));      // shrink to nothing - no lingering dots

            // Flash: one hot glow that expands as it fades, selling the grab
            var flash = CreateBurstSystem(root.transform, "Flash", glowMaterial, 64);

            var flashColor = flash.colorOverLifetime;
            flashColor.enabled = true;
            flashColor.color = new ParticleSystem.MinMaxGradient(FadeGradient(1f, 0.1f));

            var flashSize = flash.sizeOverLifetime;
            flashSize.enabled = true;
            flashSize.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.5f),
                new Keyframe(0.35f, 1f),
                new Keyframe(1f, 1.25f)));

            var serialized = new SerializedObject(vfx);
            serialized.FindProperty("sparks").objectReferenceValue = sparks;
            serialized.FindProperty("flash").objectReferenceValue = flash;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            Debug.Log("[ParticlePolishSetup] Built CollectVFX (shared Sparks + Flash systems).");
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
            main.startLifetime = 0.4f;
            main.startSize = 0.15f;
            main.maxParticles = maxParticles;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.useUnscaledTime = true; // finish the pop even if a mini-level freezes time
            main.gravityModifier = 0f;

            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f; // manual Emit() only

            var shape = ps.shape;
            shape.enabled = false;

            ConfigureRenderer(ps, material);
            return ps;
        }

        // ------------------------------------------------------------------
        // Sprint trail: white speed lines inside the Player prefab
        // ------------------------------------------------------------------

        private static void BuildSprintTrail(Material glowMaterial)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
            {
                Debug.LogWarning($"[ParticlePolishSetup] No Player prefab at {PlayerPrefabPath} - run Tools > RingSport > Setup Dog Player first; sprint trail skipped.");
                return;
            }

            var prefabRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var existing = prefabRoot.transform.Find(TrailName);
                if (existing != null)
                    Object.DestroyImmediate(existing.gameObject);

                var go = new GameObject(TrailName);
                go.transform.SetParent(prefabRoot.transform, false);
                // Player pivot sits at y=1 (capsule centre); the dog's body is
                // ~0.65m above the ground, so the emitter drops to match. The
                // 180 turn points the box shape's +Z emission backward (world
                // -Z), blowing the lines past the body with the scroll.
                go.transform.localPosition = new Vector3(0f, -0.35f, 0f);
                go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

                var ps = go.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.loop = true;
                main.playOnAwake = true;
                main.startSpeed = 16f; // SprintTrail rescales live from scroll speed
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.14f, 0.24f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
                main.startColor = new ParticleSystem.MinMaxGradient(
                    new Color(1f, 1f, 1f, 0.35f), Color.white);
                main.maxParticles = 64;
                main.simulationSpace = ParticleSystemSimulationSpace.World;

                var emission = ps.emission;
                emission.enabled = true;
                emission.rateOverTime = 26f; // zeroed by SprintTrail until sprinting

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Box;
                shape.scale = new Vector3(0.9f, 0.7f, 1.6f);
                shape.position = new Vector3(0f, 0f, 0.3f); // biased slightly behind the dog

                var color = ps.colorOverLifetime;
                color.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[]
                    {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(1f, 0.12f), // quick fade-in, no popping
                        new GradientAlphaKey(0.55f, 0.8f),
                        new GradientAlphaKey(0f, 1f)
                    });
                color.color = new ParticleSystem.MinMaxGradient(gradient);

                var renderer = ConfigureRenderer(ps, glowMaterial);
                renderer.renderMode = ParticleSystemRenderMode.Stretch;
                renderer.lengthScale = 4f;      // base streak from the soft glow sprite
                renderer.velocityScale = 0.12f; // faster lines stretch longer

                var trail = go.AddComponent<SprintTrail>();
                var serialized = new SerializedObject(trail);
                serialized.FindProperty("lines").objectReferenceValue = ps;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(prefabRoot, PlayerPrefabPath);
                Debug.Log("[ParticlePolishSetup] Built SprintTrail inside Player prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static ParticleSystemRenderer ConfigureRenderer(ParticleSystem ps, Material material)
        {
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            renderer.sortMode = ParticleSystemSortMode.None;
            return renderer;
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

        /// <summary>The mega coin bursts bigger: set the prefab's isLargeCoin flag.</summary>
        private static void MarkMegaCoinLarge()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MegaPrefabPath);
            var collectible = prefab != null ? prefab.GetComponent<Collectible>() : null;
            if (collectible == null)
            {
                Debug.LogWarning($"[ParticlePolishSetup] No Collectible on {MegaPrefabPath} - large-coin burst flag not set.");
                return;
            }

            var serialized = new SerializedObject(collectible);
            serialized.FindProperty("isLargeCoin").boolValue = true;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(collectible);
            Debug.Log("[ParticlePolishSetup] Marked MegaCollectible as a large coin.");
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
