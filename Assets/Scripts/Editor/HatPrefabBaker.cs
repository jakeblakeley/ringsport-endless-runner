using System.Collections.Generic;
using System.IO;
using RingSport.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace RingSport.Editor
{
    /// <summary>
    /// Turns the .glb hat models in Assets/Models/hats into the wearable
    /// prefabs under Resources/Hats/&lt;id&gt;.prefab - one per HatManager
    /// catalog entry (seasonal files are named seasonal_&lt;Id&gt;_&lt;tag&gt;.glb).
    ///
    /// The prefabs are fully self-contained so the source .glb imports stay
    /// OUT of the WebGL build:
    /// - meshes are copied into Assets/Models/hats/Baked (Medium compression)
    /// - each glTF material becomes a Custom/Mobile/ArcEffect twin (same
    ///   mapping as ModelArcMaterialBuilder) whose base map is a downsized
    ///   copy in Assets/Textures/Hats, capped at 256px - some sources carry
    ///   2-4K textures that would sink the itch.io download
    ///
    /// Each prefab ROOT's TRS is its head fit under HatEquipper's anchor
    /// (+Y up off the skull, +Z toward the nose): computed from the model's
    /// bounds via category defaults (helmet / headband / hat), then per-hat
    /// FitOverrides. BUMPING BakeVersion REBUILDS EVERY PREFAB and resets any
    /// hand-tuned root TRS to the table - fold manual tweaks back into
    /// FitOverrides if they should survive a re-bake.
    ///
    /// Afterwards it renders a contact sheet of every hat worn on the actual
    /// dog's head to Temp/HatContactSheets for eyeballing the fits.
    /// Runs automatically once after compilation (version-gated); re-run from
    /// Tools/RingSport/Rebuild Hat Prefabs.
    /// </summary>
    public static class HatPrefabBaker
    {
        // Bump to force the auto-run to re-bake every hat prefab
        private const int BakeVersion = 2;
        private const string VersionPrefKey = "RingSport.HatPrefabBaker.Version";

        private const string ModelFolder = "Assets/Models/hats";
        private const string BakedMeshFolder = "Assets/Models/hats/Baked";
        private const string ResourcesHatFolder = "Assets/Resources/Hats";
        private const string TextureFolder = "Assets/Textures/Hats";
        private const string MaterialFolder = "Assets/Materials/Hats";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string ArcShaderName = "Custom/Mobile/ArcEffect";

        private const int MaxTextureSize = 256;

        // glTFast material property names (glTF/pbrMetallicRoughness)
        private const string GltfBaseColorTex = "baseColorTexture";
        private const string GltfBaseColor = "baseColorFactor";
        private const string GltfMetallic = "metallicFactor";
        private const string GltfRoughness = "roughnessFactor";
        private const string GltfAlphaCutoff = "alphaCutoff";

        /// <summary>
        /// How a hat sits on the head anchor. Contact-sheet ground truth: the
        /// dog's skull is ~0.5 anchor units ear-to-ear and the Head bone (the
        /// anchor origin) sits toward the FOREHEAD, so hats seat with negative
        /// BaseY/ForwardZ - back and down from the anchor onto the crown.
        /// </summary>
        private struct Fit
        {
            public float Width;    // target bounds width (local X after rotation)
            public float BaseY;    // anchor-space height of the hat's lowest point
            public float ForwardZ; // bounds-centre nudge toward the nose (negative = toward the ears)
            public float Yaw;      // extra spin in degrees for models not authored facing +Z
            public float Pitch;    // X tilt - several sources are authored "facing the camera" and need laying down
            public float Roll;

            public Fit(float width, float baseY, float forwardZ = 0f, float yaw = 0f, float pitch = 0f, float roll = 0f)
            {
                Width = width;
                BaseY = baseY;
                ForwardZ = forwardZ;
                Yaw = yaw;
                Pitch = pitch;
                Roll = roll;
            }
        }

        /// <summary>
        /// Per-hat fits that beat the category defaults - the tuning knob for
        /// the contact-sheet loop. Wide-brim hats want more width than the
        /// skull; billboard-authored pieces get a pitch to lay them onto it.
        /// </summary>
        private static readonly Dictionary<string, Fit> FitOverrides = new Dictionary<string, Fit>
        {
            { "MexicanMusicianHat", new Fit(0.85f, -0.02f, -0.06f) }, // sombrero brim dwarfs the head
            { "BlackCowboyHat", new Fit(0.72f, -0.04f, -0.09f) },
            { "MusketeerHat", new Fit(0.75f, -0.04f, -0.09f) },
            { "PirateHat", new Fit(0.75f, -0.03f, -0.10f) },
            { "FedoraHat", new Fit(0.62f, -0.04f, -0.09f) },
            { "WizardHat", new Fit(0.60f, -0.03f, -0.09f) },
            { "WitchHat", new Fit(0.60f, -0.03f, -0.09f) },
            { "ChefHat", new Fit(0.50f, -0.04f, -0.09f) },
            { "JesterHat", new Fit(0.55f, -0.04f, -0.08f) },
            { "FlowerHat", new Fit(0.55f, -0.06f, -0.08f) },
            { "SafetyHelmet", new Fit(0.55f, -0.10f, -0.08f) },
            { "DeerHorn", new Fit(0.60f, -0.06f, -0.12f) },
            { "AntelopeHorn", new Fit(0.60f, -0.06f, -0.12f) },
            { "Cake", new Fit(0.34f, -0.02f, -0.10f) },
            { "PartyHat", new Fit(0.30f, -0.02f, -0.10f) },
            { "UncleSamHat", new Fit(0.45f, -0.03f, -0.09f) },
            // Authored flat "facing the camera" - pitch lays them onto the skull
            { "GoldLaurelCrown", new Fit(0.55f, -0.08f, -0.10f, 0f, -90f) },
            { "BandannaHeaddress", new Fit(0.58f, -0.08f, -0.10f, 0f, -90f) },
        };

        // The first hat pass shipped primitive placeholders under different
        // ids - clear them (and their flat-colour materials) out of the way
        private static readonly string[] PlaceholderPrefabIds =
            { "tophat", "cap", "crown", "beanie", "propeller", "bow" };
        private static readonly string[] PlaceholderMaterials =
        {
            "Hat_Black", "Hat_Red", "Hat_Gold", "Hat_Blue", "Hat_LightBlue",
            "Hat_White", "Hat_Yellow", "Hat_Pink", "Hat_DeepPink", "Hat_Gray",
        };

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

            if (EditorPrefs.GetInt(VersionPrefKey, 0) >= BakeVersion)
                return;

            try
            {
                Run();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[HatPrefabBaker] Auto-bake failed (will retry when the editor is idle): {e}");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Rebuild Hat Prefabs")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[HatPrefabBaker] Cannot run during play mode - exit play mode first.");
                return;
            }

            Shader arcShader = Shader.Find(ArcShaderName);
            if (arcShader == null)
            {
                Debug.LogError($"[HatPrefabBaker] Shader '{ArcShaderName}' not found - hats not baked.");
                return;
            }

            Dictionary<string, string> modelPaths = MapModelFiles();
            EnsureFolder(BakedMeshFolder);
            EnsureFolder(ResourcesHatFolder);
            EnsureFolder(TextureFolder);
            EnsureFolder(MaterialFolder);
            DeletePlaceholderAssets();

            int built = 0;
            var manifest = new List<string>();
            try
            {
                for (int i = 0; i < HatManager.Defs.Length; i++)
                {
                    HatDef def = HatManager.Defs[i];
                    EditorUtility.DisplayProgressBar("Baking hat prefabs",
                        $"{def.Id} ({i + 1}/{HatManager.Defs.Length})", (float)i / HatManager.Defs.Length);

                    if (!modelPaths.TryGetValue(def.Id, out string glbPath))
                    {
                        Debug.LogError($"[HatPrefabBaker] No model in {ModelFolder} for catalog hat '{def.Id}' - skipped.");
                        continue;
                    }

                    string line = BakeHat(def, glbPath, arcShader);
                    if (line != null)
                    {
                        manifest.Add(line);
                        built++;
                    }
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            EditorPrefs.SetInt(VersionPrefKey, BakeVersion);
            Debug.Log($"[HatPrefabBaker] Baked {built}/{HatManager.Defs.Length} hat prefabs (v{BakeVersion}).\n" +
                      string.Join("\n", manifest));

            BakeContactSheets();
        }

        /// <summary>Catalog id -> .glb path. Seasonal files unwrap seasonal_&lt;Id&gt;_&lt;tag&gt;; strays are reported once.</summary>
        private static Dictionary<string, string> MapModelFiles()
        {
            var map = new Dictionary<string, string>();
            if (!Directory.Exists(ModelFolder))
            {
                Debug.LogError($"[HatPrefabBaker] Missing model folder {ModelFolder}.");
                return map;
            }

            foreach (string rawPath in Directory.GetFiles(ModelFolder, "*.glb", SearchOption.TopDirectoryOnly))
            {
                string path = rawPath.Replace('\\', '/');
                string baseName = Path.GetFileNameWithoutExtension(path);

                string id = baseName;
                if (baseName.StartsWith("seasonal_"))
                {
                    string[] parts = baseName.Split('_');
                    if (parts.Length >= 3)
                        id = parts[1];
                }

                if (HatManager.GetDef(id) != null)
                    map[id] = path;
                else
                    Debug.LogWarning($"[HatPrefabBaker] {path} matches no catalog hat - ignored.");
            }

            return map;
        }

        private static void DeletePlaceholderAssets()
        {
            foreach (string id in PlaceholderPrefabIds)
            {
                string path = $"{ResourcesHatFolder}/{id}.prefab";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                    AssetDatabase.DeleteAsset(path);
            }

            foreach (string name in PlaceholderMaterials)
            {
                string path = $"{MaterialFolder}/{name}.mat";
                if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
                    AssetDatabase.DeleteAsset(path);
            }
        }

        // ------------------------------------------------------------------
        // One hat: materials -> meshes -> fitted prefab
        // ------------------------------------------------------------------

        private static string BakeHat(HatDef def, string glbPath, Shader arcShader)
        {
            GameObject imported = AssetDatabase.LoadAssetAtPath<GameObject>(glbPath);
            if (imported == null)
            {
                Debug.LogError($"[HatPrefabBaker] {glbPath} did not import as a GameObject - '{def.Id}' skipped.");
                return null;
            }

            // Arc twins for every glTF material in the file
            var materialMap = new Dictionary<Material, Material>();
            var textureCache = new Dictionary<Texture, Texture>();
            foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(glbPath))
            {
                if (sub is Material gltfMat && !materialMap.ContainsKey(gltfMat))
                    materialMap[gltfMat] = BuildArcMaterial(def.Id, gltfMat, arcShader, textureCache);
            }

            GameObject clone = Object.Instantiate(imported);
            clone.name = def.Id;
            try
            {
                StripNonVisual(clone);

                int vertexCount = 0;
                int meshIndex = 0;
                var meshCopies = new Dictionary<Mesh, Mesh>();

                foreach (var filter in clone.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (filter.sharedMesh == null)
                        continue;
                    filter.sharedMesh = CopyMesh(filter.sharedMesh, def.Id, meshCopies, ref meshIndex);
                    vertexCount += filter.sharedMesh.vertexCount;
                }

                foreach (var skinned in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (skinned.sharedMesh == null)
                        continue;
                    skinned.sharedMesh = CopyMesh(skinned.sharedMesh, def.Id, meshCopies, ref meshIndex);
                    vertexCount += skinned.sharedMesh.vertexCount;
                }

                foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
                {
                    Material[] mats = renderer.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        if (mats[i] != null && materialMap.TryGetValue(mats[i], out Material arc))
                            mats[i] = arc;
                    }
                    renderer.sharedMaterials = mats;

                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    renderer.receiveShadows = false;
                }

                if (vertexCount > 40000)
                    Debug.LogWarning($"[HatPrefabBaker] '{def.Id}' is heavy: {vertexCount} verts. Consider decimating the source.");

                Fit fit = ApplyFit(def.Id, clone);

                string prefabPath = $"{ResourcesHatFolder}/{def.Id}.prefab";
                PrefabUtility.SaveAsPrefabAsset(clone, prefabPath);

                return $"  {def.Id}: {vertexCount} verts, scale {clone.transform.localScale.x:0.###}, " +
                       $"width {fit.Width:0.##}, baseY {fit.BaseY:0.##}";
            }
            finally
            {
                Object.DestroyImmediate(clone);
            }
        }

        /// <summary>Some source scenes ship cameras, lights or animators along with the meshes.</summary>
        private static void StripNonVisual(GameObject root)
        {
            foreach (var animator in root.GetComponentsInChildren<Animator>(true))
                Object.DestroyImmediate(animator);
            foreach (var animation in root.GetComponentsInChildren<Animation>(true))
                Object.DestroyImmediate(animation);
            foreach (var camera in root.GetComponentsInChildren<Camera>(true))
                Object.DestroyImmediate(camera.gameObject);
            foreach (var light in root.GetComponentsInChildren<Light>(true))
                Object.DestroyImmediate(light.gameObject);
        }

        /// <summary>
        /// Copies a glb sub-asset mesh into our own compressed asset so the
        /// prefab (and the build) never references the source import. Existing
        /// copies are updated in place to keep their GUIDs stable.
        /// </summary>
        private static Mesh CopyMesh(Mesh source, string hatId, Dictionary<Mesh, Mesh> cache, ref int index)
        {
            if (cache.TryGetValue(source, out Mesh cached))
                return cached;

            string path = $"{BakedMeshFolder}/{hatId}_{index++}_{Sanitize(source.name)}.asset";
            Mesh copy = Object.Instantiate(source);
            copy.name = source.name;

            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(copy, existing);
                Object.DestroyImmediate(copy);
                copy = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(copy, path);
            }

            MeshUtility.SetMeshCompression(copy, ModelImporterMeshCompression.Medium);
            EditorUtility.SetDirty(copy);
            cache[source] = copy;
            return copy;
        }

        /// <summary>
        /// Sizes and seats the hat on the anchor: rotate (facing fix), scale
        /// to the target width, then land the bounds' bottom-centre at
        /// (0, BaseY, ForwardZ). The resulting root TRS IS the head fit that
        /// HatEquipper preserves on equip.
        /// </summary>
        private static Fit ApplyFit(string id, GameObject clone)
        {
            Fit fit = FitOverrides.TryGetValue(id, out Fit over) ? over : DefaultFitFor(id);

            Transform t = clone.transform;
            t.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(fit.Pitch, fit.Yaw, fit.Roll));
            t.localScale = Vector3.one;

            Bounds bounds = RenderedBounds(clone);
            float width = Mathf.Max(0.001f, bounds.size.x);
            float scale = fit.Width / width;

            t.localScale = Vector3.one * scale;
            t.position = new Vector3(
                -bounds.center.x * scale,
                fit.BaseY - bounds.min.y * scale,
                -bounds.center.z * scale + fit.ForwardZ);
            return fit;
        }

        private static Fit DefaultFitFor(string id)
        {
            // Helmets wrap the skull, headbands/horns sit into the fur,
            // brimmed hats perch on top - all seated back off the forehead
            // anchor onto the crown
            if (id.Contains("Helmet"))
                return new Fit(0.62f, -0.14f, -0.12f);
            if (id.Contains("Headband") || id.Contains("Horn") || id.Contains("Crown") || id.Contains("Laurel"))
                return new Fit(0.52f, -0.10f, -0.10f);
            return new Fit(0.55f, -0.05f, -0.09f);
        }

        private static Bounds RenderedBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(root.transform.position, Vector3.one * 0.3f);

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);
            return bounds;
        }

        // ------------------------------------------------------------------
        // Materials & textures
        // ------------------------------------------------------------------

        /// <summary>
        /// Same glTF -> ArcEffect mapping as ModelArcMaterialBuilder, except
        /// the base map is a downsized standalone copy so the full-size glb
        /// texture never enters the build. Metallic/roughness maps are dropped
        /// - scalar factors are plenty for something hat-sized on screen.
        /// </summary>
        private static Material BuildArcMaterial(string hatId, Material gltfMat, Shader arcShader,
            Dictionary<Texture, Texture> textureCache)
        {
            string path = $"{MaterialFolder}/{hatId}_{Sanitize(gltfMat.name)}_Arc.mat";
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(arcShader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = arcShader;

            Texture source = gltfMat.HasProperty(GltfBaseColorTex) ? gltfMat.GetTexture(GltfBaseColorTex) : null;
            Texture baked = source != null ? BakeTexture(source, hatId, textureCache) : null;
            mat.SetTexture("_BaseMap", baked);

            mat.SetColor("_BaseColor", gltfMat.HasProperty(GltfBaseColor) ? gltfMat.GetColor(GltfBaseColor) : Color.white);
            mat.SetFloat("_Metallic", gltfMat.HasProperty(GltfMetallic) ? gltfMat.GetFloat(GltfMetallic) : 0f);
            float roughness = gltfMat.HasProperty(GltfRoughness) ? gltfMat.GetFloat(GltfRoughness) : 0.5f;
            mat.SetFloat("_Smoothness", Mathf.Clamp01(1f - roughness));
            mat.SetFloat("_UseMetallicRoughnessMap", 0f);
            mat.DisableKeyword("_METALLICROUGHNESSMAP");

            float cutoff = gltfMat.HasProperty(GltfAlphaCutoff) ? gltfMat.GetFloat(GltfAlphaCutoff) : 0f;
            mat.SetFloat("_Cutoff", cutoff);
            if (cutoff > 0f)
                mat.EnableKeyword("_ALPHATEST_ON");
            else
                mat.DisableKeyword("_ALPHATEST_ON");

            mat.SetFloat("_Cull", gltfMat.HasProperty("_Cull") ? gltfMat.GetFloat("_Cull") : 2f);
            mat.enableInstancing = true;
            mat.renderQueue = -1;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>GPU-downsizes a source texture to a capped standalone PNG asset (works on non-readable sources).</summary>
        private static Texture BakeTexture(Texture source, string hatId, Dictionary<Texture, Texture> cache)
        {
            if (cache.TryGetValue(source, out Texture cachedTex))
                return cachedTex;

            int size = Mathf.Min(MaxTextureSize, Mathf.NextPowerOfTwo(Mathf.Max(source.width, source.height)));
            string path = $"{TextureFolder}/{hatId}_{Sanitize(source.name)}.png";

            RenderTexture rt = RenderTexture.GetTemporary(size, size, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var readback = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
                readback.ReadPixels(new Rect(0, 0, size, size), 0, 0);
                readback.Apply();
                File.WriteAllBytes(path, readback.EncodeToPNG());
                Object.DestroyImmediate(readback);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }

            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.textureType = TextureImporterType.Default;
                importer.sRGBTexture = true;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.maxTextureSize = MaxTextureSize;
                importer.textureCompression = TextureImporterCompression.Compressed;
                importer.SaveAndReimport();
            }

            var asset = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            cache[source] = asset;
            return asset;
        }

        // ------------------------------------------------------------------
        // Contact sheets - every hat on the real dog's head
        // ------------------------------------------------------------------

        private const int CellSize = 320;
        private const int SheetColumns = 6;
        private const int RowsPerSheet = 4;

        [MenuItem("Tools/RingSport/Bake Hat Contact Sheet")]
        public static void BakeContactSheets()
        {
            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null)
            {
                Debug.LogWarning("[HatPrefabBaker] No player prefab - contact sheets skipped.");
                return;
            }

            string outDir = Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? ".", "Temp/HatContactSheets");
            Directory.CreateDirectory(outDir);

            GameObject dog = Object.Instantiate(playerPrefab);
            GameObject cameraObject = null;
            try
            {
                dog.name = "HatContactSheetDog";
                foreach (var behaviour in dog.GetComponentsInChildren<Behaviour>(true))
                    behaviour.enabled = false;

                // Same trick as the selector thumbnails: stage far below the
                // track at the world origin's XZ so ArcEffect displacement ~0
                dog.transform.SetPositionAndRotation(new Vector3(0f, -60f, 0f), Quaternion.identity);

                Transform head = FindChildByName(dog.transform, "Head");
                Transform modelRoot = FindChildByName(dog.transform, "Dog Model");
                if (head == null)
                {
                    Debug.LogWarning("[HatPrefabBaker] No 'Head' bone on the player - contact sheets skipped.");
                    return;
                }

                // Replicate HatEquipper.EnsureAnchor: anchor aligned to the
                // model root's frame, riding the head bone
                var anchor = new GameObject("SheetAnchor").transform;
                anchor.SetParent(head, false);
                anchor.rotation = modelRoot != null ? modelRoot.rotation : dog.transform.rotation;
                anchor.localPosition = Vector3.zero;
                anchor.localScale = Vector3.one;

                cameraObject = new GameObject("HatSheetCamera");
                var cam = cameraObject.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = 0.55f;
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 12f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.32f, 0.34f, 0.40f, 1f);

                // Two poses per hat: 3/4 front-right of the muzzle, and a flat
                // side profile (nose points LEFT) that disambiguates pitch/yaw
                Quaternion frame = anchor.rotation;
                Vector3 focus = anchor.position + frame * new Vector3(0f, 0.14f, -0.05f);
                Vector3 threeQuarterPos = focus + frame * (new Vector3(0.8f, 0.35f, 1.15f).normalized * 2.2f);
                Vector3 sidePos = focus + frame * (new Vector3(1f, 0.12f, 0f).normalized * 2.2f);
                Vector3 up = frame * Vector3.up;

                int cellCount = HatManager.Defs.Length + 1; // leading bare-head reference
                int totalRows = Mathf.CeilToInt((float)cellCount / SheetColumns);
                int sheetCount = Mathf.CeilToInt((float)totalRows / RowsPerSheet);
                var sheets = new Texture2D[sheetCount];
                var sideSheets = new Texture2D[sheetCount];
                for (int i = 0; i < sheetCount; i++)
                {
                    int rows = Mathf.Min(RowsPerSheet, totalRows - i * RowsPerSheet);
                    sheets[i] = new Texture2D(SheetColumns * CellSize, rows * CellSize, TextureFormat.RGB24, false);
                    sideSheets[i] = new Texture2D(SheetColumns * CellSize, rows * CellSize, TextureFormat.RGB24, false);
                }

                var manifest = new List<string> { "cell layout: catalog order, row-major. sheet/row/col id" };
                for (int cell = 0; cell < cellCount; cell++)
                {
                    string id = cell == 0 ? null : HatManager.Defs[cell - 1].Id;
                    GameObject hat = null;
                    if (id != null)
                    {
                        var hatPrefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{ResourcesHatFolder}/{id}.prefab");
                        if (hatPrefab != null)
                            hat = Object.Instantiate(hatPrefab, anchor, false);
                    }

                    try
                    {
                        cam.transform.SetPositionAndRotation(threeQuarterPos,
                            Quaternion.LookRotation(focus - threeQuarterPos, up));
                        RenderCell(cam, sheets, cell, manifest, id ?? "(bare head)");

                        cam.transform.SetPositionAndRotation(sidePos,
                            Quaternion.LookRotation(focus - sidePos, up));
                        RenderCell(cam, sideSheets, cell, null, null);
                    }
                    finally
                    {
                        if (hat != null)
                            Object.DestroyImmediate(hat);
                    }
                }

                for (int i = 0; i < sheetCount; i++)
                {
                    File.WriteAllBytes(Path.Combine(outDir, $"sheet{i}.png"), sheets[i].EncodeToPNG());
                    File.WriteAllBytes(Path.Combine(outDir, $"side{i}.png"), sideSheets[i].EncodeToPNG());
                    Object.DestroyImmediate(sheets[i]);
                    Object.DestroyImmediate(sideSheets[i]);
                }
                File.WriteAllLines(Path.Combine(outDir, "manifest.txt"), manifest);
                Debug.Log($"[HatPrefabBaker] Contact sheets written to {outDir}");
            }
            finally
            {
                if (cameraObject != null)
                    Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(dog);
            }
        }

        private static void RenderCell(Camera cam, Texture2D[] sheets, int cell, List<string> manifest, string label)
        {
            int row = cell / SheetColumns;
            int col = cell % SheetColumns;
            int sheetIndex = row / RowsPerSheet;
            int sheetRow = row % RowsPerSheet;
            if (manifest != null)
                manifest.Add($"sheet{sheetIndex} r{sheetRow} c{col}: {label}");

            RenderTexture rt = RenderTexture.GetTemporary(CellSize, CellSize, 24);
            RenderTexture previous = RenderTexture.active;
            try
            {
                var request = new RenderPipeline.StandardRequest();
                if (RenderPipeline.SupportsRenderRequest(cam, request))
                {
                    request.destination = rt;
                    RenderPipeline.SubmitRenderRequest(cam, request);
                }
                else
                {
                    cam.targetTexture = rt;
                    cam.Render();
                    cam.targetTexture = null;
                }

                RenderTexture.active = rt;
                Texture2D sheet = sheets[sheetIndex];
                // Texture Y runs bottom-up; row 0 lands at the top of the sheet
                int destY = sheet.height - (sheetRow + 1) * CellSize;
                sheet.ReadPixels(new Rect(0, 0, CellSize, CellSize), col * CellSize, destY);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static Transform FindChildByName(Transform root, string name)
        {
            if (root.name == name)
                return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildByName(root.GetChild(i), name);
                if (found != null)
                    return found;
            }
            return null;
        }

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

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace(' ', '_').Replace('.', '_');
        }
    }
}
