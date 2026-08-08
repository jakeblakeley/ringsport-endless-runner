using UnityEngine;

namespace RingSport.Core
{
    /// <summary>
    /// Deferred PlayerPrefs flushing (perf audit fix #2). On WebGL,
    /// PlayerPrefs.Save() is a synchronous IndexedDB sync that used to land
    /// exactly on the hat/love-note pickup slow-mo frames. Progress writers
    /// call SetString/SetInt as usual, then MarkDirty() instead of Save();
    /// GameManager flushes on every state transition (behind the fade), and
    /// the hook below flushes when the tab is hidden so a closed tab can't
    /// lose an unlock. PlayerPrefs reads always see unsaved writes, so
    /// nothing else changes.
    /// </summary>
    public static class SaveFlush
    {
        private static bool dirty;

        public static void MarkDirty()
        {
            dirty = true;
        }

        public static void FlushIfDirty()
        {
            if (!dirty)
                return;
            dirty = false;
            PlayerPrefs.Save();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallHook()
        {
            var host = new GameObject("SaveFlushHook");
            Object.DontDestroyOnLoad(host);
            host.AddComponent<SaveFlushHook>();
        }
    }

    /// <summary>Flushes pending saves when the page is hidden/backgrounded.</summary>
    internal sealed class SaveFlushHook : MonoBehaviour
    {
        private void OnApplicationPause(bool paused)
        {
            if (paused)
                SaveFlush.FlushIfDirty();
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
                SaveFlush.FlushIfDirty();
        }

        private void OnDestroy()
        {
            SaveFlush.FlushIfDirty();
        }
    }
}
