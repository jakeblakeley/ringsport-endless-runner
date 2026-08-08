using UnityEngine;
#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace RingSport.Core
{
    /// <summary>
    /// Global audio mute, persisted across sessions. Mutes through
    /// AudioListener.volume rather than AudioListener.pause - the pause
    /// screen owns pause, and the two must not fight over it.
    ///
    /// On web the mute additionally suspends the WebAudio context
    /// (Plugins/WebGL/AudioMute.jslib): on iOS Safari a running context
    /// claims the audio session and silences whatever the player was
    /// listening to, so a muted game must actually let go of it.
    /// </summary>
    public static class AudioMuteManager
    {
        private const string PrefKey = "AudioMuted";

        public static bool Muted { get; private set; }

        /// <summary>Fired after the mute state changes (and once at startup when the saved state applies).</summary>
        public static event System.Action<bool> MutedChanged;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void RingSportSetWebAudioMuted(int muted);
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Muted = false;
            MutedChanged = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ApplySaved()
        {
            SetMuted(PlayerPrefs.GetInt(PrefKey, 0) == 1, save: false);
        }

        public static void Toggle() => SetMuted(!Muted);

        public static void SetMuted(bool muted, bool save = true)
        {
            Muted = muted;
            AudioListener.volume = muted ? 0f : 1f;
#if UNITY_WEBGL && !UNITY_EDITOR
            RingSportSetWebAudioMuted(muted ? 1 : 0);
#endif
            if (save)
            {
                PlayerPrefs.SetInt(PrefKey, muted ? 1 : 0);
                PlayerPrefs.Save();
            }
            MutedChanged?.Invoke(muted);
        }
    }
}
