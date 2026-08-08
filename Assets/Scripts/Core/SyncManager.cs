using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace RingSport.Core
{
    /// <summary>
    /// Cloud backup of the progress PlayerPrefs, keyed by a short human-friendly
    /// sync code (e.g. "WOOF-4821") so progress survives a new phone or a
    /// deleted home-screen icon.
    ///
    /// How it works: every few seconds the manager snapshots the six progress
    /// keys; when the snapshot differs from the last uploaded one it POSTs it
    /// to the site's own Netlify function (/.netlify/functions/sync), which
    /// stores it in Netlify Blobs under the code. Polling instead of hooks
    /// keeps the three save-owning managers untouched and also catches debug
    /// resets and future keys' owners calling PlayerPrefs directly.
    ///
    /// Restore is strictly explicit: nothing is ever downloaded onto a device
    /// unless a code is typed into the hidden sync panel (tap the title five times)
    /// and confirmed - so the cloud can never clobber local progress on its
    /// own. After a restore the page reloads so every manager re-reads its
    /// prefs from scratch.
    ///
    /// Outside the deployed site (editor, local file) there is no backend, so
    /// sync silently disables itself; the panel still opens for UI checks.
    /// </summary>
    public class SyncManager : MonoBehaviour
    {
        public static SyncManager Instance { get; private set; }

        private const string CodePrefKey = "Sync.Code";
        private const string UploadedPrefKey = "Sync.LastUploaded";
        private const string FunctionPath = "/.netlify/functions/sync";
        private const float PollSeconds = 4f;

        private static readonly string[] CodeWords =
            { "WOOF", "BARK", "FETCH", "TREAT", "ZOOMY", "BISCUIT", "WIGGLE", "SPROCKET" };

        /// <summary>
        /// The synced snapshot. Field values mirror the private PlayerPrefs
        /// keys owned by ScoreManager, HatManager and LoveNoteManager - keep
        /// Capture/Apply in step with those managers if their keys ever change.
        /// </summary>
        [Serializable]
        public class SaveData
        {
            public int v = 1;
            public int highScore;
            public string hatsUnlocked;
            public string hatsSelected;
            public int hatsSeen;
            public string notesUnlocked;
            public int notesSeen;
        }

        [Serializable]
        private class CloudEnvelope
        {
            public SaveData data;
        }

        private string lastUploaded;
        private bool uploading;
        private bool restoring;
        private bool loggedDisabled;

        public string Code { get; private set; }
        public bool Restoring => restoring;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (Instance != null)
                return;
            var go = new GameObject("SyncManager");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<SyncManager>();
        }

        private void Awake()
        {
            Code = PlayerPrefs.GetString(CodePrefKey, "");
            if (string.IsNullOrEmpty(Code))
            {
                Code = CodeWords[UnityEngine.Random.Range(0, CodeWords.Length)] + "-" +
                       UnityEngine.Random.Range(0, 10000).ToString("D4");
                PlayerPrefs.SetString(CodePrefKey, Code);
                PlayerPrefs.Save();
                GameLog.Info($"[SyncManager] Generated sync code {Code}");
            }

            // Persisted so a snapshot changed just before the page was killed
            // still uploads on the next launch.
            lastUploaded = PlayerPrefs.GetString(UploadedPrefKey, "");
            StartCoroutine(WatchLoop());
        }

        private IEnumerator WatchLoop()
        {
            var wait = new WaitForSecondsRealtime(PollSeconds);
            while (true)
            {
                yield return wait;
                if (uploading || restoring)
                    continue;
                string json = JsonUtility.ToJson(Capture());
                if (json != lastUploaded)
                    yield return Upload(json);
            }
        }

        private IEnumerator Upload(string dataJson)
        {
            string baseUrl = BaseUrl;
            if (baseUrl == null)
            {
                if (!loggedDisabled)
                {
                    loggedDisabled = true;
                    GameLog.Info("[SyncManager] No backend outside the deployed site - cloud backup off.");
                }
                lastUploaded = dataJson; // stop rechecking an unchanging snapshot
                yield break;
            }

            uploading = true;
            string body = "{\"code\":\"" + Code + "\",\"data\":" + dataJson + "}";
            using (var req = new UnityWebRequest(baseUrl + FunctionPath, "POST"))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.Success)
                {
                    lastUploaded = dataJson;
                    PlayerPrefs.SetString(UploadedPrefKey, dataJson);
                    PlayerPrefs.Save();
                    GameLog.Info($"[SyncManager] Progress backed up under {Code}");
                }
                else
                {
                    // Transient failures self-heal: the snapshot still differs
                    // from lastUploaded, so the next poll retries.
                    GameLog.Warn($"[SyncManager] Backup failed ({req.responseCode}): {req.error}");
                }
            }
            uploading = false;
        }

        /// <summary>
        /// Pulls the cloud save for <paramref name="code"/> onto this device,
        /// then reloads the page so all managers re-read their prefs. Local
        /// progress is only overwritten after the panel's explicit confirm.
        /// </summary>
        public void BeginRestore(string code, Action<string> progress, Action<string> fail)
        {
            if (!restoring)
                StartCoroutine(RestoreRoutine(code, progress, fail));
        }

        private IEnumerator RestoreRoutine(string code, Action<string> progress, Action<string> fail)
        {
            string baseUrl = BaseUrl;
            if (baseUrl == null)
            {
                fail("Only works in the deployed game.");
                yield break;
            }

            restoring = true;
            progress("Looking up " + code + "...");
            using (var req = UnityWebRequest.Get(baseUrl + FunctionPath + "?code=" + UnityWebRequest.EscapeURL(code)))
            {
                yield return req.SendWebRequest();

                if (req.responseCode == 404)
                {
                    restoring = false;
                    fail("No save found for " + code);
                    yield break;
                }
                if (req.result != UnityWebRequest.Result.Success)
                {
                    restoring = false;
                    fail("Network error (" + req.responseCode + ")");
                    yield break;
                }

                CloudEnvelope env = null;
                try { env = JsonUtility.FromJson<CloudEnvelope>(req.downloadHandler.text); }
                catch (Exception) { }
                if (env == null || env.data == null)
                {
                    restoring = false;
                    fail("Save data unreadable.");
                    yield break;
                }

                Apply(env.data);
                Code = code;
                PlayerPrefs.SetString(CodePrefKey, code);
                // Mark the restored state as already-uploaded so the watcher
                // doesn't immediately push it back over the cloud copy.
                lastUploaded = JsonUtility.ToJson(Capture());
                PlayerPrefs.SetString(UploadedPrefKey, lastUploaded);
                PlayerPrefs.Save();
            }

            progress("Restored! Reloading...");
            // WebGL flushes PlayerPrefs to IndexedDB asynchronously - give it a
            // beat before tearing the page down.
            yield return new WaitForSecondsRealtime(1.5f);
            ReloadPage();
        }

        private static SaveData Capture()
        {
            return new SaveData
            {
                highScore = PlayerPrefs.GetInt("HighScore", 0),
                hatsUnlocked = PlayerPrefs.GetString("Hats.Unlocked", ""),
                hatsSelected = PlayerPrefs.GetString("Hats.Selected", ""),
                hatsSeen = PlayerPrefs.GetInt("Hats.SeenCount", 0),
                notesUnlocked = PlayerPrefs.GetString("LoveNotes.Unlocked", ""),
                notesSeen = PlayerPrefs.GetInt("LoveNotes.SeenCount", 0),
            };
        }

        private static void Apply(SaveData d)
        {
            PlayerPrefs.SetInt("HighScore", d.highScore);
            PlayerPrefs.SetString("Hats.Unlocked", d.hatsUnlocked ?? "");
            PlayerPrefs.SetString("Hats.Selected", d.hatsSelected ?? "");
            PlayerPrefs.SetInt("Hats.SeenCount", d.hatsSeen);
            PlayerPrefs.SetString("LoveNotes.Unlocked", d.notesUnlocked ?? "");
            PlayerPrefs.SetInt("LoveNotes.SeenCount", d.notesSeen);
        }

        private static string BaseUrl
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                string abs = Application.absoluteURL;
                if (string.IsNullOrEmpty(abs))
                    return null;
                try { return new Uri(abs).GetLeftPart(UriPartial.Authority); }
                catch (Exception) { return null; }
#else
                return null;
#endif
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void SyncReloadPage();
#endif

        private static void ReloadPage()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            SyncReloadPage();
#else
            GameLog.Info("[SyncManager] Restore complete - page reload happens on WebGL only.");
#endif
        }
    }
}
