using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace RingSport.EditorTools
{
    /// <summary>
    /// Records the shader variants the game actually renders and wires them into
    /// Graphics Settings' preloaded-shader list.
    ///
    /// Why: a variant is compiled the first time it is drawn. On web that lands
    /// as a hitch the first time each material appears - the "first run through a
    /// world stutters, later runs are smooth" symptom, because the variant is
    /// cached from then on. Preloading moves every compile to load time, under
    /// the loading bar.
    ///
    /// Recording beats hand-listing variants: URP's keyword set at runtime
    /// (lighting, shadows, fog, instancing) is not something you can reliably
    /// reconstruct from a material's inspector state.
    ///
    /// Flow (the perf harness drives the playing):
    ///   1. Clear      - starts a fresh recording
    ///   2. play every level, which is what the harness already does
    ///   3. Save       - writes the collection and assigns it as preloaded
    ///
    /// The editor accumulates variants across play sessions within one editor
    /// session, so the levels can be visited one run at a time.
    /// </summary>
    [InitializeOnLoad]
    public static class ShaderVariantBake
    {
        private const string CollectionPath = "Assets/Settings/RingSportShaderVariants.shadervariants";
        private const string ClearMarker = "PerfReports/svc_clear.txt";
        private const string SaveMarker = "PerfReports/svc_save.txt";
        private const string CountPath = "PerfReports/svc_count.txt";
        private static double nextCheck;

        static ShaderVariantBake()
        {
            EditorApplication.update += Poll;
        }

        private static void Poll()
        {
            if (EditorApplication.timeSinceStartup < nextCheck)
                return;
            nextCheck = EditorApplication.timeSinceStartup + 1.0;

            // Recording happens DURING play mode, so unlike the build markers this
            // must not bail while playing - it only avoids acting mid-compile.
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            if (File.Exists(ClearMarker))
            {
                File.Delete(ClearMarker);
                Clear();
            }

            if (File.Exists(SaveMarker) && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                File.Delete(SaveMarker);
                SaveAndPreload();
            }

            WriteCount();
        }

        // Unity keeps the variant-recording API internal on UnityEditor.ShaderUtil,
        // even though it is what the Graphics Settings inspector drives. Reflection
        // with loud failures beats hand-authoring variant lists we cannot get right.
        private static readonly System.Type ShaderUtilType = typeof(UnityEditor.ShaderUtil);

        private static object CallStatic(string name, params object[] args)
        {
            var m = ShaderUtilType.GetMethod(name,
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);
            if (m != null)
                return m.Invoke(null, args);

            var p = ShaderUtilType.GetProperty(name,
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);
            if (p != null)
                return p.GetValue(null);

            Debug.LogError($"[ShaderVariantBake] UnityEditor.ShaderUtil.{name} not found on this Unity version.");
            return null;
        }

        private static int ShaderCount =>
            CallStatic("GetCurrentShaderVariantCollectionShaderCount") as int? ?? 0;

        private static int VariantCount =>
            CallStatic("GetCurrentShaderVariantCollectionVariantCount") as int? ?? 0;

        private static void WriteCount()
        {
            Directory.CreateDirectory("PerfReports");
            File.WriteAllText(CountPath, $"shaders={ShaderCount} variants={VariantCount}\n");
        }

        [MenuItem("Tools/RingSport/Shader Variants: Start Recording")]
        public static void Clear()
        {
            CallStatic("ClearCurrentShaderVariantCollection");
            Debug.Log("[ShaderVariantBake] Recording cleared - play every level now, then Save.");
        }

        [MenuItem("Tools/RingSport/Shader Variants: Save + Preload")]
        public static void SaveAndPreload()
        {
            int shaders = ShaderCount;
            int variants = VariantCount;
            if (variants == 0)
            {
                Debug.LogWarning("[ShaderVariantBake] Nothing recorded - run the game first.");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(CollectionPath));
            CallStatic("SaveCurrentShaderVariantCollection", CollectionPath);
            AssetDatabase.ImportAsset(CollectionPath, ImportAssetOptions.ForceUpdate);

            var collection = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(CollectionPath);
            if (collection == null)
            {
                Debug.LogError($"[ShaderVariantBake] Saved but could not load {CollectionPath}");
                return;
            }

            AssignAsPreloaded(collection);
            Debug.Log($"[ShaderVariantBake] Saved {shaders} shader(s) / {variants} variant(s) to {CollectionPath} " +
                      "and set it as a preloaded shader collection.");
        }

        /// <summary>
        /// Graphics Settings' preloaded-shader list has no public setter, so this
        /// goes through the serialized object - the same thing the inspector edits.
        /// </summary>
        private static void AssignAsPreloaded(ShaderVariantCollection collection)
        {
            var settings = new SerializedObject(
                Unsupported.GetSerializedAssetInterfaceSingleton("GraphicsSettings"));
            SerializedProperty list = settings.FindProperty("m_PreloadedShaders");
            if (list == null)
            {
                Debug.LogError("[ShaderVariantBake] m_PreloadedShaders not found on GraphicsSettings.");
                return;
            }

            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == collection)
                    return; // already wired
            }

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = collection;
            settings.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }
    }
}
