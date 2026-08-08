using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RingSport.Editor
{
    /// <summary>
    /// The .glb models in Assets/Models import through glTFast, which generates
    /// its own materials (glTF/... shader graphs) as read-only sub-assets of the
    /// imported file. Those shaders have no vertex displacement, so the props
    /// stayed flat while the rest of the world curved away on the arc.
    ///
    /// glTFast's importer has no material remap slots, so this tool mirrors each
    /// glTF material into a standalone Custom/Mobile/ArcEffect material under
    /// Assets/Materials/Models (same base map, colour, metallic/roughness and
    /// cull settings) and rewrites the prefab renderers that used the original.
    ///
    /// Re-runnable: existing arc materials are updated in place, so re-importing
    /// a .glb (or dropping in new ones) only needs another run of this tool.
    /// Runs automatically once after compilation (version-gated); re-run from
    /// Tools/RingSport/Rebuild Model Arc Materials.
    /// </summary>
    public static class ModelArcMaterialBuilder
    {
        // Bump to force the auto-run to re-apply the conversion
        private const int SetupVersion = 1;
        private const string VersionPrefKey = "RingSport.ModelArcMaterialBuilder.Version";

        private const string ArcShaderName = "Custom/Mobile/ArcEffect";
        private const string ModelFolder = "Assets/Models";
        private const string MatFolder = "Assets/Materials/Models";
        private const string PrefabFolder = "Assets/Prefabs";

        // glTFast material property names (glTF/pbrMetallicRoughness)
        private const string GltfBaseColorTex = "baseColorTexture";
        private const string GltfBaseColorTexST = "baseColorTexture_ST";
        private const string GltfBaseColor = "baseColorFactor";
        private const string GltfMetallic = "metallicFactor";
        private const string GltfRoughness = "roughnessFactor";
        private const string GltfMetallicRoughnessTex = "metallicRoughnessTexture";
        private const string GltfAlphaCutoff = "alphaCutoff";

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

            try
            {
                Run();
            }
            catch (Exception e)
            {
                Debug.LogError($"[ModelArcMaterialBuilder] Auto-run failed (will retry when the editor is idle): {e}");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Rebuild Model Arc Materials")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[ModelArcMaterialBuilder] Cannot run during play mode - exit play mode first.");
                return;
            }

            Shader arcShader = Shader.Find(ArcShaderName);
            if (arcShader == null)
            {
                Debug.LogError($"[ModelArcMaterialBuilder] Shader '{ArcShaderName}' not found.");
                return;
            }

            if (!Directory.Exists(MatFolder))
            {
                Directory.CreateDirectory(MatFolder);
                AssetDatabase.Refresh();
            }

            // glTF material instance -> the arc material that replaces it
            var replacements = new Dictionary<Material, Material>();
            int builtCount = 0;

            foreach (string glbPath in ModelPaths())
            {
                List<Material> gltfMats = LoadGltfMaterials(glbPath);
                if (gltfMats.Count == 0)
                {
                    Debug.LogWarning($"[ModelArcMaterialBuilder] No materials found in {glbPath}.");
                    continue;
                }

                string modelName = Path.GetFileNameWithoutExtension(glbPath);
                foreach (Material gltfMat in gltfMats)
                {
                    string matName = gltfMats.Count == 1
                        ? $"{modelName}_Arc"
                        : $"{modelName}_{Sanitize(gltfMat.name)}_Arc";

                    replacements[gltfMat] = BuildArcMaterial(gltfMat, arcShader, $"{MatFolder}/{matName}.mat");
                    builtCount++;
                }
            }

            AssetDatabase.SaveAssets();

            int rewrittenPrefabs = RewritePrefabs(replacements);

            AssetDatabase.SaveAssets();
            EditorPrefs.SetInt(VersionPrefKey, SetupVersion);

            Debug.Log($"[ModelArcMaterialBuilder] {builtCount} arc material(s) in {MatFolder}, " +
                      $"{rewrittenPrefabs} prefab(s) rewritten.");
        }

        private static IEnumerable<string> ModelPaths()
        {
            if (!Directory.Exists(ModelFolder))
                yield break;

            foreach (string path in Directory.GetFiles(ModelFolder, "*.glb", SearchOption.AllDirectories))
                yield return path.Replace('\\', '/');

            foreach (string path in Directory.GetFiles(ModelFolder, "*.gltf", SearchOption.AllDirectories))
                yield return path.Replace('\\', '/');
        }

        private static List<Material> LoadGltfMaterials(string glbPath)
        {
            var result = new List<Material>();
            foreach (UnityEngine.Object obj in AssetDatabase.LoadAllAssetsAtPath(glbPath))
            {
                if (obj is Material mat)
                    result.Add(mat);
            }
            result.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return result;
        }

        /// <summary>
        /// Copies the glTF surface settings onto an arc material. glTF stores
        /// roughness where URP wants smoothness, and packs metallic/roughness
        /// into one texture (G = roughness, B = metallic) - the arc shader reads
        /// that packing directly behind the _METALLICROUGHNESSMAP keyword.
        /// </summary>
        private static Material BuildArcMaterial(Material gltfMat, Shader arcShader, string path)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            bool created = mat == null;
            if (created)
            {
                mat = new Material(arcShader);
                AssetDatabase.CreateAsset(mat, path);
            }

            mat.shader = arcShader;

            // Route through the compressed standalone twins when the payload
            // bake has produced them - pointing at the glb's embedded texture
            // would drag the uncompressed original back into the build.
            Texture baseMap = TexturePayloadBake.ToExtractedTwin(GetTexture(gltfMat, GltfBaseColorTex));
            mat.SetTexture("_BaseMap", baseMap);
            if (gltfMat.HasProperty(GltfBaseColorTexST))
            {
                Vector4 st = gltfMat.GetVector(GltfBaseColorTexST);
                mat.SetTextureScale("_BaseMap", new Vector2(st.x, st.y));
                mat.SetTextureOffset("_BaseMap", new Vector2(st.z, st.w));
            }

            mat.SetColor("_BaseColor", GetColor(gltfMat, GltfBaseColor, Color.white));
            mat.SetFloat("_Metallic", GetFloat(gltfMat, GltfMetallic, 0f));
            mat.SetFloat("_Smoothness", 1f - GetFloat(gltfMat, GltfRoughness, 0.5f));

            Texture metallicRoughness = TexturePayloadBake.ToExtractedTwin(GetTexture(gltfMat, GltfMetallicRoughnessTex));
            mat.SetTexture("_MetallicRoughnessMap", metallicRoughness);
            mat.SetFloat("_UseMetallicRoughnessMap", metallicRoughness != null ? 1f : 0f);
            if (metallicRoughness != null)
                mat.EnableKeyword("_METALLICROUGHNESSMAP");
            else
                mat.DisableKeyword("_METALLICROUGHNESSMAP");

            // glTF ALPHA_CUTOFF surfaces carry a cutoff; opaque ones report 0
            // and skip the clip() variant entirely (keeps early-Z)
            float cutoff = GetFloat(gltfMat, GltfAlphaCutoff, 0f);
            mat.SetFloat("_Cutoff", cutoff);
            if (cutoff > 0f)
                mat.EnableKeyword("_ALPHATEST_ON");
            else
                mat.DisableKeyword("_ALPHATEST_ON");

            // glTF doubleSided -> Cull Off; glTFast writes that into _Cull on
            // its own material. Fall back to Back culling when it is absent.
            mat.SetFloat("_Cull", GetFloat(gltfMat, "_Cull", 2f));

            mat.renderQueue = -1;
            EditorUtility.SetDirty(mat);

            if (created)
                Debug.Log($"[ModelArcMaterialBuilder] Arc material: {path} (from {gltfMat.name})");

            return mat;
        }

        /// <summary>
        /// Swaps every prefab renderer that still points at a glTF sub-asset
        /// material over to its arc twin. The models sit inside the prefabs as
        /// nested prefab instances, so this lands as a prefab override on the
        /// renderer - re-importing the .glb keeps it.
        /// </summary>
        private static int RewritePrefabs(Dictionary<Material, Material> replacements)
        {
            if (replacements.Count == 0)
                return 0;

            int changed = 0;
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabFolder });

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject contents = PrefabUtility.LoadPrefabContents(path);
                if (contents == null)
                    continue;

                try
                {
                    bool dirty = false;
                    foreach (Renderer renderer in contents.GetComponentsInChildren<Renderer>(true))
                    {
                        Material[] mats = renderer.sharedMaterials;
                        bool rendererDirty = false;

                        for (int i = 0; i < mats.Length; i++)
                        {
                            if (mats[i] != null &&
                                replacements.TryGetValue(mats[i], out Material arcMat) &&
                                arcMat != null)
                            {
                                mats[i] = arcMat;
                                rendererDirty = true;
                            }
                        }

                        if (rendererDirty)
                        {
                            renderer.sharedMaterials = mats;
                            dirty = true;
                        }
                    }

                    if (dirty)
                    {
                        PrefabUtility.SaveAsPrefabAsset(contents, path);
                        changed++;
                        Debug.Log($"[ModelArcMaterialBuilder] Rewrote materials on {path}");
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(contents);
                }
            }

            return changed;
        }

        private static Texture GetTexture(Material mat, string prop)
        {
            return mat.HasProperty(prop) ? mat.GetTexture(prop) : null;
        }

        private static Color GetColor(Material mat, string prop, Color fallback)
        {
            return mat.HasProperty(prop) ? mat.GetColor(prop) : fallback;
        }

        private static float GetFloat(Material mat, string prop, float fallback)
        {
            return mat.HasProperty(prop) ? mat.GetFloat(prop) : fallback;
        }

        private static string Sanitize(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Replace(' ', '_');
        }
    }
}
