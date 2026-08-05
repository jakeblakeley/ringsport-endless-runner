using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;

namespace RingSport.EditorTools
{
    /// <summary>
    /// Tier-2 font one-shot: the three gameplay TMP fonts ship a baked 1024
    /// atlas AND their source TTF because population mode is Dynamic - but the
    /// atlases only contain glyphs that happened to render during development
    /// (Barlow-Bold was missing the digits 5 and 7!). This bakes the full
    /// printable-ASCII set, then switches each font to Static and clears the
    /// runtime TTF reference so it drops from the build. Fonts whose atlas
    /// cannot fit the full set stay Dynamic (loud warning instead of tofu).
    /// Safe to delete once applied.
    /// </summary>
    [InitializeOnLoad]
    public static class Tier2FontBake
    {
        private const string MarkerPath = "PerfReports/apply_tier2_fonts_request.txt";
        private const string PerfMarkerPath = "PerfReports/pending_request.json";
        private static double nextCheck;

        private static readonly string[] FontPaths =
        {
            "Assets/Fonts/Barlow-Bold SDF.asset",
            "Assets/Fonts/BarlowCondensed-SemiBold SDF.asset",
            "Assets/Fonts/PermanentMarker-Regular SDF.asset",
        };

        static Tier2FontBake()
        {
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (EditorApplication.timeSinceStartup < nextCheck)
                return;
            nextCheck = EditorApplication.timeSinceStartup + 1.0;

            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (!File.Exists(MarkerPath))
                return;

            File.Delete(MarkerPath);
            Apply();
        }

        [MenuItem("Tools/Perf/Bake Fonts Static (Tier 2)")]
        private static void Apply()
        {
            var ascii = new StringBuilder(96);
            for (char c = ' '; c <= '~'; c++)
                ascii.Append(c);
            string charset = ascii.ToString();

            foreach (string path in FontPaths)
            {
                var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font == null)
                {
                    Debug.LogError($"[Tier2FontBake] Font not found: {path}");
                    continue;
                }

                if (font.atlasPopulationMode != AtlasPopulationMode.Dynamic)
                {
                    Debug.Log($"[Tier2FontBake] {font.name} already static - skipped");
                    continue;
                }

                // Incrementally-grown atlases fragment; a clean repack fits the
                // full set where TryAddCharacters on the grown atlas fell a few
                // characters short.
                font.ClearFontAssetData(true);

                bool allAdded = font.TryAddCharacters(charset, out string missing);
                if (!allAdded || !string.IsNullOrEmpty(missing))
                {
                    Debug.LogWarning($"[Tier2FontBake] {font.name}: atlas could not fit '{missing}' even after repack - KEEPING Dynamic to avoid tofu");
                    EditorUtility.SetDirty(font);
                    continue;
                }

                var so = new SerializedObject(font);
                so.FindProperty("m_AtlasPopulationMode").intValue = (int)AtlasPopulationMode.Static;
                so.FindProperty("m_SourceFontFile").objectReferenceValue = null;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(font);
                Debug.Log($"[Tier2FontBake] {font.name}: full ASCII baked ({font.characterTable.Count} glyphs), now Static, TTF reference cleared");
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[Tier2FontBake] Done - queueing tier2b perf run");
            Directory.CreateDirectory("PerfReports");
            File.WriteAllText(PerfMarkerPath, "{\"label\":\"tier2b\",\"durationSeconds\":60}");
        }
    }
}
