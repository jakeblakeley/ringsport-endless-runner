using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;

namespace RingSport.Editor
{
    /// <summary>
    /// Perf audit 2026-08-08, fix #1: the .glb models import through glTFast,
    /// which embeds their textures as UNCOMPRESSED RGBA32/RGB24 sub-assets —
    /// ~41 MB of the web build's 81 MB texture payload. glTFast's importer has
    /// no compression control, and because the gameplay prefabs NEST the
    /// imported glb prefab, the glTF materials (and every texture they
    /// reference) ride into the build even where an Arc material override
    /// hides them.
    ///
    /// What this does, in order:
    ///  1. Blits every texture referenced by a glb's glTF materials out to
    ///     Assets/Textures/Models/*.png and imports them block-compressed with
    ///     mips (same pixels, same size — only the GPU format changes).
    ///  2. Repoints the Arc materials (Assets/Materials/Models/*_Arc.mat) at
    ///     those twins.
    ///  3. Clones each glTF material to Assets/Materials/Models/<model>_<mat>_Gltf.mat
    ///     — same glTFast shader, all properties and keywords copied, textures
    ///     repointed — for the renderers that use the glTF look directly
    ///     (Player, Decoy, both ragdolls).
    ///  4. UNPACKS the nested glb prefab instances inside the consuming
    ///     prefabs and swaps any remaining glTF-material reference to a twin.
    ///     The prefabs keep referencing the glb MESHES and AVATARS directly,
    ///     so rigs, bones and animation are untouched; the glTF materials and
    ///     embedded textures simply fall out of the build's reference closure.
    ///  5. Separately: swaps PC_Renderer's PostProcessData for a clone with
    ///     the 10 film-grain textures removed (film grain is unused; 2.6 MB).
    ///
    /// COST of the unpack: prefabs no longer auto-track glb re-imports. If a
    /// model's node structure ever changes, re-nest the instance by hand and
    /// re-run this tool (Tools/RingSport/Compact Texture Payload). Re-runs are
    /// safe: extraction overwrites deterministically, twins update in place,
    /// unpacking no-ops once nothing is nested.
    /// </summary>
    public static class TexturePayloadBake
    {
        private const int BakeVersion = 3;
        private const string VersionPrefKey = "RingSport.TexturePayloadBake.Version";

        private const string ModelFolder = "Assets/Models";
        private const string TexFolder = "Assets/Textures/Models";
        private const string MatFolder = "Assets/Materials/Models";
        private const string StrippedPostDataPath = "Assets/Settings/RingSportPostProcessData.asset";
        private const string PcRendererPath = "Assets/Settings/PC_Renderer.asset";

        // delayCall starves on this machine's editor-wedge state while update
        // delegates keep firing (same lesson as HatSetup) - so the auto-run
        // rides EditorApplication.update. Touching PerfReports/bake_texture_request.txt
        // forces a re-run without a version bump, mirroring WebBuildSettings'
        // build markers.
        private const string MarkerPath = "PerfReports/bake_texture_request.txt";
        private static double nextPoll;

        [InitializeOnLoadMethod]
        private static void AutoRunOnLoad()
        {
            EditorApplication.update += PollAutoRun;
        }

        private static void PollAutoRun()
        {
            if (EditorApplication.timeSinceStartup < nextPoll)
                return;
            nextPoll = EditorApplication.timeSinceStartup + 1.0;

            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                return;
            }

            bool marker = File.Exists(MarkerPath);
            if (!marker && EditorPrefs.GetInt(VersionPrefKey, 0) >= BakeVersion)
                return;
            if (marker)
                File.Delete(MarkerPath);

            try
            {
                Run();
                EditorPrefs.SetInt(VersionPrefKey, BakeVersion);
            }
            catch (Exception e)
            {
                Debug.LogError($"[TexturePayloadBake] Auto-run failed (retrying in 30s): {e}");
                nextPoll = EditorApplication.timeSinceStartup + 30.0;
            }
        }

        [MenuItem("Tools/RingSport/Compact Texture Payload")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[TexturePayloadBake] Exit play mode first.");
                return;
            }

            if (!Directory.Exists(TexFolder))
            {
                Directory.CreateDirectory(TexFolder);
                AssetDatabase.Refresh();
            }

            ClaimedPaths.Clear();

            // source glTF texture -> compressed standalone twin
            var twinTextures = new Dictionary<Texture, Texture2D>();
            // source glTF material -> standalone clone with twin textures
            var twinMaterials = new Dictionary<Material, Material>();
            long extractedBytes = 0;

            // TopDirectoryOnly: Assets/Models/hats/*.glb are consumed through
            // HatPrefabBaker's own bake and never referenced by the game.
            foreach (string glbPath in Directory.GetFiles(ModelFolder, "*.glb", SearchOption.TopDirectoryOnly))
            {
                string modelName = Path.GetFileNameWithoutExtension(glbPath);
                foreach (UnityEngine.Object obj in AssetDatabase.LoadAllAssetsAtPath(glbPath.Replace('\\', '/')))
                {
                    if (obj is not Material gltfMat)
                        continue;

                    ExtractMaterialTextures(gltfMat, modelName, twinTextures, ref extractedBytes);
                    twinMaterials[gltfMat] = BuildGltfTwin(gltfMat, modelName, twinTextures);
                }
            }

            AssetDatabase.SaveAssets();

            int arcRepointed = RepointArcMaterials(twinTextures);
            int prefabsRewritten = UnpackAndRewriteConsumers(twinMaterials);
            StripFilmGrain();

            AssetDatabase.SaveAssets();

            Debug.Log($"[TexturePayloadBake] Done: {twinTextures.Count} texture(s) extracted " +
                      $"({extractedBytes / (1024f * 1024f):F1} MB of PNG), {twinMaterials.Count} glTF material twin(s), " +
                      $"{arcRepointed} arc material(s) repointed, {prefabsRewritten} prefab(s) unpacked/rewritten.");
        }

        // ---------------------------------------------------------------- textures

        // extraction path -> the source texture that claimed it this run.
        // Blender's Ucupaint bakes name every texture in a set identically
        // ("Ucupaint Material" is caicos's basecolor AND normal AND roughness),
        // so paths must be claimed per source texture or the extractions
        // overwrite each other (round 1 shipped the dog with her normal map
        // as albedo - a lavender dog).
        private static readonly Dictionary<string, Texture> ClaimedPaths = new();

        private static void ExtractMaterialTextures(
            Material gltfMat, string modelName, Dictionary<Texture, Texture2D> twins, ref long extractedBytes)
        {
            // baseColorTexture goes first so it always gets the clean
            // "<model>_<name>.png" path - ModelArcMaterialBuilder resolves
            // _BaseMap twins by that convention.
            string[] props = gltfMat.GetTexturePropertyNames();
            Array.Sort(props, (a, b) =>
                (a == "baseColorTexture" ? 0 : 1).CompareTo(b == "baseColorTexture" ? 0 : 1));

            foreach (string prop in props)
            {
                if (gltfMat.GetTexture(prop) is not Texture2D source || twins.ContainsKey(source))
                    continue;

                // Only extract textures embedded in the glb itself.
                string srcPath = AssetDatabase.GetAssetPath(source);
                if (!srcPath.StartsWith(ModelFolder))
                    continue;

                bool isNormal = prop.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0;
                // baseColor/emissive are sRGB; metallicRoughness/occlusion/normal are linear data
                bool sRGB = !isNormal &&
                            prop.IndexOf("metallicRoughness", StringComparison.OrdinalIgnoreCase) < 0 &&
                            prop.IndexOf("occlusion", StringComparison.OrdinalIgnoreCase) < 0;

                string path = $"{TexFolder}/{modelName}_{Sanitize(source.name)}.png";
                if (ClaimedPaths.TryGetValue(path, out Texture claimant) && claimant != source)
                    path = $"{TexFolder}/{modelName}_{Sanitize(source.name)}_{Sanitize(prop)}.png";

                Texture2D twin = ExtractTexture(source, path, sRGB, isNormal);
                if (twin != null)
                {
                    ClaimedPaths[path] = source;
                    twins[source] = twin;
                    extractedBytes += new FileInfo(path).Length;
                }
            }
        }

        /// <summary>
        /// GPU-blits a (possibly non-readable) texture to a standalone PNG and
        /// imports it compressed. Sources without an alpha channel are written
        /// as RGB so the importer picks the half-size opaque format (DXT1).
        /// </summary>
        private static Texture2D ExtractTexture(Texture2D source, string path, bool sRGB, bool isNormal)
        {
            int w = source.width, h = source.height;
            bool hasAlpha = GraphicsFormatUtility.HasAlphaChannel(source.graphicsFormat);

            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32,
                sRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear);
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                var readback = new Texture2D(w, h,
                    hasAlpha ? TextureFormat.RGBA32 : TextureFormat.RGB24, false, linear: !sRGB);
                readback.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                readback.Apply();
                File.WriteAllBytes(path, readback.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(readback);
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }

            AssetDatabase.ImportAsset(path);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                Debug.LogError($"[TexturePayloadBake] No importer for {path}");
                return null;
            }

            importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = sRGB;
            importer.mipmapEnabled = true; // glb import had generateMipMaps: 1
            importer.wrapMode = source.wrapMode;
            importer.filterMode = source.filterMode;
            importer.anisoLevel = 1;
            importer.maxTextureSize = Mathf.Max(32, Mathf.NextPowerOfTwo(Mathf.Max(w, h)));
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>
        /// Resolves the compressed twin for a glb-embedded texture if the bake
        /// has produced one. Used by ModelArcMaterialBuilder so an arc-material
        /// rebuild never points back at the uncompressed embedded texture.
        /// </summary>
        public static Texture ToExtractedTwin(Texture source)
        {
            if (source == null)
                return null;
            string srcPath = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(srcPath) || !srcPath.StartsWith(ModelFolder))
                return source;
            string twinPath = $"{TexFolder}/{Path.GetFileNameWithoutExtension(srcPath)}_{Sanitize(source.name)}.png";
            Texture2D twin = AssetDatabase.LoadAssetAtPath<Texture2D>(twinPath);
            return twin != null ? twin : source;
        }

        // ---------------------------------------------------------------- materials

        /// <summary>
        /// Standalone copy of a glTF sub-asset material: same shader, same
        /// properties and keywords, but sampling the compressed twin textures.
        /// The dog/decoy/ragdoll renderers keep their exact look through this.
        /// </summary>
        private static Material BuildGltfTwin(
            Material gltfMat, string modelName, Dictionary<Texture, Texture2D> twinTextures)
        {
            string path = $"{MatFolder}/{modelName}_{Sanitize(gltfMat.name)}_Gltf.mat";
            Material twin = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (twin == null)
            {
                twin = new Material(gltfMat);
                AssetDatabase.CreateAsset(twin, path);
            }
            else
            {
                twin.shader = gltfMat.shader;
                twin.CopyPropertiesFromMaterial(gltfMat);
                twin.shaderKeywords = gltfMat.shaderKeywords;
                twin.renderQueue = gltfMat.renderQueue;
            }

            foreach (string prop in twin.GetTexturePropertyNames())
            {
                if (twin.GetTexture(prop) is Texture tex &&
                    twinTextures.TryGetValue(tex, out Texture2D twinTex))
                {
                    twin.SetTexture(prop, twinTex);
                }
            }

            EditorUtility.SetDirty(twin);
            return twin;
        }

        private static int RepointArcMaterials(Dictionary<Texture, Texture2D> twinTextures)
        {
            int repointed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { MatFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("_Arc.mat"))
                    continue;

                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                bool dirty = false;
                foreach (string prop in mat.GetTexturePropertyNames())
                {
                    if (mat.GetTexture(prop) is Texture tex &&
                        twinTextures.TryGetValue(tex, out Texture2D twinTex))
                    {
                        mat.SetTexture(prop, twinTex);
                        dirty = true;
                    }
                }

                if (dirty)
                {
                    EditorUtility.SetDirty(mat);
                    repointed++;
                }
            }

            return repointed;
        }

        // ---------------------------------------------------------------- prefabs

        private static int UnpackAndRewriteConsumers(Dictionary<Material, Material> twinMaterials)
        {
            int changed = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    int unpacked = UnpackNestedGlbInstances(contents);
                    int swapped = SwapGltfMaterials(contents, twinMaterials);

                    if (unpacked > 0 || swapped > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        changed++;
                        Debug.Log($"[TexturePayloadBake] {path}: unpacked {unpacked} glb instance(s), " +
                                  $"swapped {swapped} material slot(s).");

                        // Round 1 saw four rigged prefabs revert to nested on
                        // disk despite this code path reporting success - so
                        // trust nothing: re-read the saved file and say loudly
                        // whether the unpack actually persisted.
                        string yaml = File.ReadAllText(path);
                        foreach (string glbMeta in Directory.GetFiles(ModelFolder, "*.glb.meta", SearchOption.TopDirectoryOnly))
                        {
                            string glbGuid = null;
                            foreach (string line in File.ReadLines(glbMeta))
                            {
                                if (line.StartsWith("guid: "))
                                {
                                    glbGuid = line.Substring(6).Trim();
                                    break;
                                }
                            }

                            if (glbGuid != null &&
                                System.Text.RegularExpressions.Regex.IsMatch(
                                    yaml, $@"m_SourcePrefab: \{{fileID: -?\d+, guid: {glbGuid}"))
                            {
                                Debug.LogError($"[TexturePayloadBake] PERSIST FAILURE: {path} still nests " +
                                               $"{Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(glbMeta))} after save.");
                            }
                        }
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            return changed;
        }

        private static int UnpackNestedGlbInstances(GameObject contents)
        {
            int unpacked = 0;
            bool again = true;
            while (again)
            {
                again = false;
                foreach (Transform t in contents.GetComponentsInChildren<Transform>(true))
                {
                    GameObject go = t.gameObject;
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(go))
                        continue;

                    string srcPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);
                    if (string.IsNullOrEmpty(srcPath) ||
                        !srcPath.StartsWith(ModelFolder) ||
                        (!srcPath.EndsWith(".glb") && !srcPath.EndsWith(".gltf")))
                    {
                        continue;
                    }

                    PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                    unpacked++;
                    again = true; // hierarchy changed; re-scan from scratch
                    break;
                }
            }

            return unpacked;
        }

        private static int SwapGltfMaterials(GameObject contents, Dictionary<Material, Material> twinMaterials)
        {
            int swapped = 0;
            foreach (Renderer renderer in contents.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = renderer.sharedMaterials;
                bool dirty = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != null && twinMaterials.TryGetValue(mats[i], out Material twin))
                    {
                        mats[i] = twin;
                        dirty = true;
                        swapped++;
                    }
                }

                if (dirty)
                    renderer.sharedMaterials = mats;
            }

            return swapped;
        }

        /// <summary>
        /// Unpacks any nested glb prefab instances under <paramref name="root"/>
        /// and swaps glTF sub-asset materials for their compressed standalone
        /// twins (resolved by the naming convention this bake writes). The
        /// codegen setups that rebuild the dog/decoy/ragdoll prefabs call this
        /// right before saving, so their output never drags the uncompressed
        /// embedded textures back into the build. No-op on already-compacted
        /// hierarchies.
        /// </summary>
        public static int CompactGlbSubtree(GameObject root, bool preferArcTwins = false)
        {
            int unpacked = UnpackNestedGlbInstances(root);
            int swapped = 0;
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = renderer.sharedMaterials;
                bool dirty = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material twin = ToTwinMaterial(mats[i], preferArcTwins);
                    if (twin != null)
                    {
                        mats[i] = twin;
                        dirty = true;
                        swapped++;
                    }
                }

                if (dirty)
                    renderer.sharedMaterials = mats;
            }

            if (unpacked > 0 || swapped > 0)
            {
                Debug.Log($"[TexturePayloadBake] Compacted '{root.name}': {unpacked} instance(s) unpacked, " +
                          $"{swapped} material slot(s) twinned.");
            }

            return unpacked + swapped;
        }

        /// <summary>Twin for a glb sub-asset material, or null when no swap is needed.</summary>
        private static Material ToTwinMaterial(Material mat, bool preferArcTwin = false)
        {
            if (mat == null)
                return null;
            string path = AssetDatabase.GetAssetPath(mat);
            if (string.IsNullOrEmpty(path) || !path.StartsWith(ModelFolder) ||
                (!path.EndsWith(".glb") && !path.EndsWith(".gltf")))
            {
                return null;
            }

            string model = Path.GetFileNameWithoutExtension(path);

            // Props seen far from the player want ModelArcMaterialBuilder's
            // arc twin (world-curvature vertex warp) over the exact-look glTF
            // twin: on a non-arc shader the decoy hovers above the warped
            // ground 25m+ out. Both twin flavors sample the same compressed
            // textures, so the payload win is identical either way.
            if (preferArcTwin)
            {
                Material arc =
                    AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/{model}_{Sanitize(mat.name)}_Arc.mat") ??
                    AssetDatabase.LoadAssetAtPath<Material>($"{MatFolder}/{model}_Arc.mat");
                if (arc != null)
                    return arc;
                Debug.LogWarning($"[TexturePayloadBake] No arc twin for '{mat.name}' ({path}) - " +
                                 "run Tools/RingSport/Rebuild Model Arc Materials; falling back to the glTF twin.");
            }

            string twinPath = $"{MatFolder}/{model}_{Sanitize(mat.name)}_Gltf.mat";
            Material twin = AssetDatabase.LoadAssetAtPath<Material>(twinPath);
            if (twin == null)
            {
                Debug.LogWarning($"[TexturePayloadBake] No twin material for '{mat.name}' ({path}) - " +
                                 "run Tools/RingSport/Compact Texture Payload first.");
            }

            return twin;
        }

        // ---------------------------------------------------------------- post fx

        /// <summary>
        /// PC_Renderer ships URP's stock PostProcessData, which drags 10 film
        /// grain textures (2.6 MB) into every build. Film grain is not in any
        /// volume profile. Clone the asset, drop the grain, keep everything
        /// else (incl. SMAA textures for future AA experiments).
        /// </summary>
        private static void StripFilmGrain()
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(PcRendererPath);
            if (rendererData == null)
            {
                Debug.LogWarning($"[TexturePayloadBake] {PcRendererPath} not found; film grain strip skipped.");
                return;
            }

            PostProcessData stripped = AssetDatabase.LoadAssetAtPath<PostProcessData>(StrippedPostDataPath);
            if (stripped == null)
            {
                if (rendererData.postProcessData == null)
                {
                    Debug.LogWarning("[TexturePayloadBake] PC_Renderer has no PostProcessData; nothing to strip.");
                    return;
                }

                stripped = UnityEngine.Object.Instantiate(rendererData.postProcessData);
                AssetDatabase.CreateAsset(stripped, StrippedPostDataPath);
            }

            var so = new SerializedObject(stripped);
            SerializedProperty grain = so.FindProperty("textures.filmGrainTex");
            if (grain != null && grain.isArray && grain.arraySize > 0)
            {
                grain.arraySize = 0;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (rendererData.postProcessData != stripped)
            {
                rendererData.postProcessData = stripped;
                EditorUtility.SetDirty(rendererData);
                Debug.Log("[TexturePayloadBake] PC_Renderer now uses RingSportPostProcessData (film grain stripped).");
            }
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }
    }
}
