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
        /// locked notes remain. 2% - raised from 1% (and 0.5% before that);
        /// notes were still turning up too rarely in playtests.
        /// </summary>
        public const float MegaCoinReplaceChance = 0.02f;

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
            "I love you more than caffeine",
            "Otters at deception pass. That's it, that's the note",
            "I love you even if you make your dogs bite me",
            "Loving you is the only honest work I do",
            "You > providing shareholder value",
            "I never learned a release cue when it comes to loving you",
            "I will always recall to you",
            "10/10 general allure",
            "If y’all’d’ve seen her that first day, y’all’d’ve fallen just as hard",
            "I loved you before Elon went crazy",
            "Are you an astrophage? Because your body is out of this world",
            "<3",
            "Born Red, married up",
            "I’d cross the parapet for you",
            "You're a book I never want to put down",
            "My signet is loving you",
            "I'm sorry for not saying \"arms\" before picking you up",
            "I’d rather worship right here beside you",
            "You are my Eden",
            "Our history is my favorite thing at the Burke",
            "Married you in a building full of extinct things. We’re the exception",
            "I love you despite your stinky feet",
            "There’s fur on everything I own and I’d still marry you again",
            "I love you enough to own a cat with you... Eventually",
            "You’re the Quito my heart.",
            "*Qatar screaming loudly* I love you",
            "Losing you would be Qatar-strophic",
            "Absence of handler is the cruelest. Three minutes without you is too long",
            "You can't esquive me, you're stuck with me",
            "Tu es le seul objet que je garde",
            "If today goes sideways remember: even Ring III dogs blow a retrieve sometimes",
            "You never apologized for the scallops. That confidence is why I married you",
            "Agave you my heart on the first date",
            "Our story started with salsa and has only gotten spicier",
            "Loving you started as an uphill drive (to the highlands)",
            "Only you make me tear up in the kitchen (no onions allowed)",
            "You run True North. I just follow the heading",
            "You put me on a variable reward schedule and I have never worked harder",
            "You're raising Quito to be ring ready. You already were",
            "A whole museum of natural history and you’re my favorite specimen",
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

        /// <summary>
        /// Drops the cached unlock list so the next read reloads it from
        /// PlayerPrefs. SyncManager calls this right after a cloud restore
        /// rewrites the pref keys (mirrors HatManager.ReloadFromPrefs).
        /// </summary>
        public static void ReloadFromPrefs()
        {
            unlockedOrder = null;
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
            // Note pickups fire this on the slow-mo frame - defer the
            // IndexedDB flush to the next state transition (perf audit fix #2).
            SaveFlush.MarkDirty();
        }
    }
}
