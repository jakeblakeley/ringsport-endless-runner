using RingSport.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace RingSport.Editor
{
    /// <summary>
    /// Wires the home screen's menu music loop:
    /// - Sets the clip's import settings for the WebGL-on-iPhone target
    ///   (see the constants below for the reasoning).
    /// - Assigns it to GameManager.homeMusic in the open scene, without
    ///   stomping an already-assigned clip.
    ///
    /// Runs automatically once after compilation (version-gated); re-run from
    /// Tools/RingSport/Setup Home Music.
    /// </summary>
    public static class HomeMusicSetup
    {
        // Bump to force the auto-run to re-apply the setup
        private const int SetupVersion = 2;
        private const string VersionPrefKey = "RingSport.HomeMusicSetup.Version";

        private const string HomeMusicPath =
            "Assets/Sounds/Music/ES_Croissants Et Baguettes - Trabant 33.mp3";

        // Quality for both the editor (Vorbis) and the shipped WebGL build
        // (AAC). The source is a 284kbps 48kHz mp3, so this is a second lossy
        // pass - 0.7 keeps it clean at roughly 170kbps rather than the ~0.5-0.6
        // we use for short SFX, where artefacts hide under the transient.
        private const float MusicQuality = 0.7f;

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

            // Only the game scene has the GameManager
            if (Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include) == null)
                return;

            try
            {
                Run();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[HomeMusicSetup] Auto-setup failed (will retry when the editor is idle): {e}");
                EditorApplication.delayCall += TryAutoRun;
            }
        }

        [MenuItem("Tools/RingSport/Setup Home Music")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[HomeMusicSetup] Cannot run during play mode - exit play mode first.");
                return;
            }

            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(HomeMusicPath);
            if (clip == null)
            {
                Debug.LogError($"[HomeMusicSetup] No clip at {HomeMusicPath} - home music not wired.");
                return;
            }

            ApplyImportSettings();
            if (!WireGameManager(clip))
                return;

            EditorPrefs.SetInt(VersionPrefKey, SetupVersion);
        }

        /// <summary>
        /// Import settings tuned for the actual target: a WebGL build played in
        /// mobile Safari.
        /// - CompressedInMemory, NOT DecompressOnLoad: a 113s stereo track
        ///   decompresses to ~40MB of PCM, which iOS Safari will not thank us
        ///   for, and Unity's own WebGL docs warn that DecompressOnLoad clips
        ///   can go silent on an iOS device in Silent Mode.
        /// - NOT Streaming either: browsers can't stream, so WebGL ignores it.
        /// - Sample rate preserved at the source's 48kHz. Downsampling a music
        ///   bed to 22kHz is exactly the "too much compression" we're avoiding;
        ///   the saving is small next to the bitrate.
        ///
        /// No per-platform override: WebGL re-encodes every clip to AAC at
        /// build time regardless of the format picked here, and 6000.5 rejects
        /// SetOverrideSampleSettings("WebGL", ...) outright (returns false, and
        /// nothing lands in platformSettingOverrides). The default settings
        /// above are what the build actually reads, so the quality below is the
        /// one that ships.
        /// </summary>
        private static void ApplyImportSettings()
        {
            var importer = AssetImporter.GetAtPath(HomeMusicPath) as AudioImporter;
            if (importer == null)
            {
                Debug.LogWarning($"[HomeMusicSetup] No AudioImporter at {HomeMusicPath} - import settings unchanged.");
                return;
            }

            var current = importer.defaultSampleSettings;
            bool alreadyApplied =
                current.loadType == AudioClipLoadType.CompressedInMemory &&
                current.sampleRateSetting == AudioSampleRateSetting.PreserveSampleRate &&
                Mathf.Approximately(current.quality, MusicQuality) &&
                !current.preloadAudioData;
            if (alreadyApplied)
                return; // a 4MB re-encode isn't free - don't redo it every menu run

            var defaults = importer.defaultSampleSettings;
            defaults.loadType = AudioClipLoadType.CompressedInMemory;
            defaults.compressionFormat = AudioCompressionFormat.Vorbis;
            defaults.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            defaults.quality = MusicQuality;
            // Loads on first Play instead of at startup - the home screen
            // enters behind a fade, so there's cover for it. Per-platform
            // sample setting in 6000.x, not an importer-level property.
            defaults.preloadAudioData = false;
            importer.defaultSampleSettings = defaults;

            importer.loadInBackground = true;
            importer.forceToMono = false;

            EditorUtility.SetDirty(importer);
            importer.SaveAndReimport();

            Debug.Log("[HomeMusicSetup] Home music import: CompressedInMemory, 48kHz preserved, " +
                      $"quality {MusicQuality:0.00} (WebGL re-encodes this to AAC at build time).");
        }

        private static bool WireGameManager(AudioClip clip)
        {
            var gameManager = Object.FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
            if (gameManager == null)
            {
                Debug.LogWarning("[HomeMusicSetup] No GameManager in the open scene - open SampleScene first.");
                return false;
            }

            var serialized = new SerializedObject(gameManager);
            var prop = serialized.FindProperty("homeMusic");
            if (prop == null)
            {
                Debug.LogWarning("[HomeMusicSetup] GameManager has no serialized field 'homeMusic'.");
                return false;
            }

            if (prop.objectReferenceValue == null)
            {
                prop.objectReferenceValue = clip;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(gameManager);
                Debug.Log($"[HomeMusicSetup] Wired GameManager.homeMusic = {clip.name}");
            }

            // Play mode can begin mid-Run(); scene ops throw there
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return false;

            var scene = gameManager.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);
            if (!string.IsNullOrEmpty(scene.path))
                EditorSceneManager.SaveScene(scene);

            return true;
        }
    }
}
