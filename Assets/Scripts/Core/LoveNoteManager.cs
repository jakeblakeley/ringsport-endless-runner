using System.Collections.Generic;
using UnityEngine;

namespace RingSport.Core
{
    /// <summary>
    /// Tracks the love note collection: which notes are unlocked, in what order,
    /// and whether the player has seen the latest unlocks (for the NEW badge).
    /// Unlocks persist via PlayerPrefs, which works cross-platform
    /// (WebGL: IndexedDB, iOS: NSUserDefaults).
    /// </summary>
    public static class LoveNoteManager
    {
        /// <summary>
        /// Chance that a large (mega) coin spawns as a love note instead, while
        /// locked notes remain. 1% - bumped from 0.5%, which never surfaced in
        /// playtests.
        /// </summary>
        public const float MegaCoinReplaceChance = 0.01f;

        /// <summary>
        /// Debug: every large coin spawns as a love note, ignoring the unlock
        /// state. Toggled from the debug menu; never persisted, so it resets on
        /// every app start (and release builds have no way to turn it on).
        /// </summary>
        public static bool DebugForceSpawnAll;

        /// <summary>
        /// Roll whether a large coin should spawn as a love note instead.
        /// </summary>
        public static bool RollMegaCoinReplace()
        {
            if (DebugForceSpawnAll)
                return true;
            return HasLockedNotes && Random.value < MegaCoinReplaceChance;
        }

        // One entry per note. The index is the note's identity in saved data,
        // so edit text freely but only ADD new notes at the end of the list.
        private static readonly string[] Notes =
        {
            "You're my favorite person to do nothing with.",
            "Every day with you is my new high score.",
            "I love you more than the dog loves dinner time.",
            "You make ordinary days feel like adventures.",
            "Still my favorite hello and my hardest goodbye.",
            "You + me + the dog = the whole world.",
            "I'd pick you first, every time, in every lifetime.",
            "Your laugh is my favorite sound.",
            "Home isn't a place, it's wherever you are.",
            "You make me want to be the person the dog thinks I am.",
            "I love the way you get excited about little things.",
            "Thanks for loving me even before coffee.",
            "You're the best thing I never saw coming.",
            "I'd run every level of this game for you.",
            "You're proof that soulmates are real.",
            "My favorite place is next to you.",
            "I love you a latte. Sorry. But it's true.",
            "You're my sunshine on the rainy days.",
            "Life with you is my favorite story.",
            "I still get butterflies when you walk in the room.",
            "You're the reason I believe in lucky days.",
            "Forever isn't long enough with you.",
            "I love you to the finish line and back.",
            "You caught me better than the dog catches a decoy.",
        };

        private const string UnlockedPrefKey = "LoveNotes.Unlocked";   // CSV of note indices, oldest unlock first
        private const string SeenCountPrefKey = "LoveNotes.SeenCount"; // unlock count when the grid was last opened

        private static List<int> unlockedOrder; // oldest unlock first, lazily loaded
        private static int collectedThisRun;

        // Statics survive play sessions when domain reload is disabled - reset explicitly
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            unlockedOrder = null;
            collectedThisRun = 0;
        }

        public static int TotalCount => Notes.Length;
        public static int UnlockedCount => GetUnlockedOrder().Count;
        public static bool HasLockedNotes => UnlockedCount < TotalCount;

        /// <summary>Notes collected during the current run (drives the HUD "[icon] xN" counter).</summary>
        public static int CollectedThisRun => collectedThisRun;

        /// <summary>True when notes were unlocked since the player last opened the grid.</summary>
        public static bool HasUnseenNotes => UnlockedCount > PlayerPrefs.GetInt(SeenCountPrefKey, 0);

        /// <summary>
        /// True when this unlocked note hasn't been seen in the grid yet (it
        /// was unlocked after the last MarkAllSeen). The grid must evaluate
        /// this BEFORE calling MarkAllSeen - LoveNotesPanel.Rebuild runs
        /// before Open() marks everything seen, which is what makes the
        /// per-cell NEW stamps show exactly once.
        /// </summary>
        public static bool IsNoteUnseen(int noteIndex)
        {
            int unlockPosition = GetUnlockedOrder().IndexOf(noteIndex);
            if (unlockPosition < 0)
                return false;
            return unlockPosition >= PlayerPrefs.GetInt(SeenCountPrefKey, 0);
        }

        public static string GetNoteText(int noteIndex)
        {
            if (noteIndex < 0 || noteIndex >= Notes.Length)
                return "";
            return Notes[noteIndex];
        }

        /// <summary>Unlocked note indices, most recently unlocked first.</summary>
        public static List<int> GetUnlockedNewestFirst()
        {
            var order = GetUnlockedOrder();
            var newestFirst = new List<int>(order);
            newestFirst.Reverse();
            return newestFirst;
        }

        public static bool IsUnlocked(int noteIndex)
        {
            return GetUnlockedOrder().Contains(noteIndex);
        }

        /// <summary>
        /// Unlocks a random still-locked note and persists it.
        /// Returns false when every note is already unlocked.
        /// </summary>
        public static bool TryCollectRandomLockedNote(out int noteIndex)
        {
            var order = GetUnlockedOrder();
            var locked = new List<int>();
            for (int i = 0; i < Notes.Length; i++)
            {
                if (!order.Contains(i))
                    locked.Add(i);
            }

            if (locked.Count == 0)
            {
                noteIndex = -1;
                return false;
            }

            noteIndex = locked[Random.Range(0, locked.Count)];
            order.Add(noteIndex);
            collectedThisRun++;
            SaveUnlocked();
            GameLog.Info($"[LoveNoteManager] Unlocked note {noteIndex} ({order.Count}/{Notes.Length}). Collected this run: {collectedThisRun}");
            return true;
        }

        /// <summary>Clears the NEW badge - call when the player opens the notes grid.</summary>
        public static void MarkAllSeen()
        {
            PlayerPrefs.SetInt(SeenCountPrefKey, UnlockedCount);
            PlayerPrefs.Save();
        }

        /// <summary>Call at the start of a new run so the HUD counter starts at 0.</summary>
        public static void ResetRunCounter()
        {
            collectedThisRun = 0;
        }

        /// <summary>Clears all unlocked notes. Useful for testing.</summary>
        public static void ClearAllProgress()
        {
            unlockedOrder = new List<int>();
            collectedThisRun = 0;
            PlayerPrefs.DeleteKey(UnlockedPrefKey);
            PlayerPrefs.DeleteKey(SeenCountPrefKey);
            PlayerPrefs.Save();
            GameLog.Info("[LoveNoteManager] All love note progress cleared.");
        }

        private static List<int> GetUnlockedOrder()
        {
            if (unlockedOrder != null)
                return unlockedOrder;

            unlockedOrder = new List<int>();
            string csv = PlayerPrefs.GetString(UnlockedPrefKey, "");
            if (string.IsNullOrEmpty(csv))
                return unlockedOrder;

            foreach (string entry in csv.Split(','))
            {
                // Ignore corrupt entries and indices beyond the current note list
                if (int.TryParse(entry, out int index) &&
                    index >= 0 && index < Notes.Length &&
                    !unlockedOrder.Contains(index))
                {
                    unlockedOrder.Add(index);
                }
            }

            return unlockedOrder;
        }

        private static void SaveUnlocked()
        {
            PlayerPrefs.SetString(UnlockedPrefKey, string.Join(",", GetUnlockedOrder()));
            PlayerPrefs.Save();
        }
    }
}
