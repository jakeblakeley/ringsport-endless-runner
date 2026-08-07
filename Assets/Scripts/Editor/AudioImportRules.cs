using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace RingSport.EditorTools
{
    /// <summary>
    /// Applies the project's audio import rules, established in the 2026-08-04
    /// perf pass, to every clip in Assets/Sounds. New clips land with Unity's
    /// defaults (stereo, quality 1.0, DecompressOnLoad) and quietly undo that
    /// work, so this is re-runnable rather than one-shot.
    ///
    /// Rules:
    ///   Ambient/ and Music/  -> CompressedInMemory, stereo kept
    ///   everything else      -> forceToMono + DecompressOnLoad, whatever the length
    ///   all                  -> Vorbis, quality 0.60, 44.1 kHz, preloadAudioData
    ///
    /// SFX must NEVER be CompressedInMemory on web: Unity's web runtime plays
    /// compressed clips through cached HTML Audio elements
    /// (Audio.js: audioCache.pop() : new Audio() -> createMediaElementSource),
    /// and iOS Safari rejects an Audio element created outside a user gesture -
    /// the cache refills only on touchstart, so mid-run one-shots go SILENT on
    /// iPhone while desktop plays fine (mini-game stingers, 2026-08-06).
    /// DecompressOnLoad clips are WebAudio buffers and immune. Music/ambience
    /// stay compressed because their decoded PCM is tens of MB; they start
    /// under the START tap, where the element cache is freshly filled.
    ///
    /// Decompressed PCM is what costs memory: a 5s stereo clip on
    /// DecompressOnLoad is ~2.6 MB resident, the same clip mono and compressed
    /// is a rounding error.
    /// </summary>
    [InitializeOnLoad]
    public static class AudioImportRules
    {
        private const float Quality = 0.60f;
        private const int RulesVersion = 3;
        private const string VersionKey = "RingSport.AudioImportRules.Version";

        static AudioImportRules()
        {
            EditorApplication.delayCall += TryAutoRun;
        }

        private static void TryAutoRun()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += TryAutoRun;
                return;
            }
            if (EditorPrefs.GetInt(VersionKey, 0) >= RulesVersion)
                return;

            Apply();
            EditorPrefs.SetInt(VersionKey, RulesVersion);
        }

        [MenuItem("Tools/RingSport/Apply Audio Import Rules")]
        public static void Apply()
        {
            string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Sounds" });
            var changed = new List<string>();

            try
            {
                AssetDatabase.StartAssetEditing();
                foreach (string guid in guids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    var importer = AssetImporter.GetAtPath(path) as AudioImporter;
                    if (importer == null)
                        continue;

                    var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
                    if (clip == null)
                        continue;

                    string rel = path.Substring("Assets/Sounds/".Length).Replace('\\', '/');
                    bool longTrack = rel.StartsWith("Ambient/") || rel.StartsWith("Music/");

                    AudioImporterSampleSettings s = importer.defaultSampleSettings;
                    AudioImporterSampleSettings before = s;
                    bool beforeMono = importer.forceToMono;

                    s.compressionFormat = AudioCompressionFormat.Vorbis;
                    s.quality = Quality;
                    s.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
                    s.sampleRateOverride = 44100;

                    if (longTrack)
                    {
                        // NOT Streaming. On web there is no filesystem to stream
                        // from - the clip lives in the .data bundle - so Streaming
                        // just defers the fetch/decode to first play, which is a
                        // multi-second silence before the home music starts.
                        // CompressedInMemory keeps only the *compressed* bytes
                        // resident (single-digit MB), so it costs little and
                        // starts immediately. Stereo image kept: it is the point.
                        s.loadType = AudioClipLoadType.CompressedInMemory;
                    }
                    else
                    {
                        importer.forceToMono = true;
                        s.loadType = AudioClipLoadType.DecompressOnLoad;
                    }

                    // Without this the clip is loaded on FIRST PLAY, which on web
                    // means a decode hitch mid-run the first time each sound fires
                    // - exactly the "first run stutters, later runs are fine"
                    // symptom. Preloading moves that work under the loading bar.
                    s.preloadAudioData = true;

                    bool dirty = beforeMono != importer.forceToMono
                                 || before.preloadAudioData != s.preloadAudioData
                                 || before.loadType != s.loadType
                                 || before.compressionFormat != s.compressionFormat
                                 || !Mathf.Approximately(before.quality, s.quality)
                                 || before.sampleRateSetting != s.sampleRateSetting
                                 || before.sampleRateOverride != s.sampleRateOverride;

                    if (!dirty)
                        continue;

                    importer.defaultSampleSettings = s;
                    importer.SaveAndReimport();
                    changed.Add($"{rel} ({clip.length:F1}s -> {s.loadType}{(longTrack ? "" : ", mono")})");
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            if (changed.Count == 0)
            {
                Debug.Log($"[AudioImportRules] All {guids.Length} clips already conform.");
                return;
            }

            Debug.Log($"[AudioImportRules] Re-imported {changed.Count} of {guids.Length} clips:\n  " +
                      string.Join("\n  ", changed));
        }
    }
}
