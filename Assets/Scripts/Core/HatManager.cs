using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RingSport.Core
{
    /// <summary>
    /// One hat in the catalog. The id is the hat's identity in saved data AND
    /// its prefab name under Resources/Hats/ AND (for the editor baker) its
    /// source model name in Assets/Models/hats - append new hats at the end of
    /// the catalog, never reorder or rename shipped ids.
    ///
    /// Seasonal hats carry a holiday and a yearly calendar window: they can
    /// ONLY drop while the window is open (at a big spawn-rate bonus), and the
    /// home screen advertises the limited run until they're unlocked.
    /// </summary>
    public sealed class HatDef
    {
        public readonly string Id;
        public readonly string DisplayName;

        /// <summary>Banner-grammar holiday name ("Caicos's Birthday", "the 4th of July"); null = regular hat.</summary>
        public readonly string HolidayName;

        /// <summary>Short holiday tag shown on the hat's selector box ("Birthday", "July 4th").</summary>
        public readonly string HolidayShort;

        // Inclusive month/day window the hat drops in each year (unused when EasterWindow)
        public readonly int StartMonth, StartDay, EndMonth, EndDay;

        /// <summary>Easter moves every year - windows flagged with this are computed from Easter Sunday +/-3 days.</summary>
        public readonly bool EasterWindow;

        /// <summary>
        /// Enclosing hats (helmets, deep caps, wraps) scale the dog's ear
        /// bones to zero while worn, instead of the ears clipping through.
        /// </summary>
        public readonly bool HideEars;

        public bool IsSeasonal => HolidayName != null;

        public HatDef(string id, string displayName, bool hideEars = false)
        {
            Id = id;
            DisplayName = displayName;
            HideEars = hideEars;
        }

        public HatDef(string id, string displayName, string holidayName, string holidayShort,
            int startMonth, int startDay, int endMonth, int endDay, bool hideEars = false)
        {
            Id = id;
            DisplayName = displayName;
            HolidayName = holidayName;
            HolidayShort = holidayShort;
            StartMonth = startMonth;
            StartDay = startDay;
            EndMonth = endMonth;
            EndDay = endDay;
            HideEars = hideEars;
        }

        public HatDef(string id, string displayName, string holidayName, string holidayShort,
            bool easterWindow, bool hideEars = false)
        {
            Id = id;
            DisplayName = displayName;
            HolidayName = holidayName;
            HolidayShort = holidayShort;
            EasterWindow = easterWindow;
            HideEars = hideEars;
        }
    }

    /// <summary>
    /// Tracks the hat cosmetic collection: which hats are unlocked and which
    /// one the dog is wearing. Mirrors LoveNoteManager (static, raw
    /// PlayerPrefs, domain-reload-safe). Hat prefabs live in
    /// Resources/Hats/&lt;id&gt;.prefab and are loaded on demand, so only the
    /// worn hat's model ever occupies memory.
    /// </summary>
    public static class HatManager
    {
        /// <summary>
        /// Chance that a large (mega) coin spawns as a hat pickup instead,
        /// while locked hats remain. Rides the love-note rate so the two rare
        /// collectibles stay one family; rolled after the love-note roll, so
        /// notes keep priority when both would claim the same coin.
        /// </summary>
        public const float MegaCoinReplaceChance = LoveNoteManager.MegaCoinReplaceChance;

        /// <summary>
        /// Seasonal hats while their holiday window is open (they never drop
        /// outside it) - boosted enough to realistically land inside one week
        /// of casual play.
        /// </summary>
        public const float SeasonalChance = 0.06f;

        /// <summary>
        /// Debug: overrides the spawn chance (0..1) from the debug menu.
        /// Never persisted, so it resets on every app start.
        /// </summary>
        public static float? DebugSpawnChanceOverride;

        /// <summary>Debug: treats every seasonal window as open (test off-season hats). Never persisted.</summary>
        public static bool DebugForceAllSeasonalActive;

        /// <summary>Hats every save starts with - unlocked quietly (no NEW badge), restored after a debug reset.</summary>
        private static readonly string[] DefaultUnlockedIds = { "HiphopCap" };

        // The catalog, in carousel/unlock order: regular hats first, then the
        // seasonal hats in calendar order. Ids match the .glb names in
        // Assets/Models/hats (seasonal files carry a seasonal_<Id>_<tag>
        // wrapper name). Append-only once shipped.
        public static readonly HatDef[] Defs =
        {
            new HatDef("KidCap", "Kid Cap", hideEars: true),
            new HatDef("FrogHat", "Frog Hat", hideEars: true),
            new HatDef("ChefHat", "Chef Hat", hideEars: true),
            new HatDef("BlackCowboyHat", "Cowboy Hat", hideEars: true),
            new HatDef("CatHeadband", "Cat Ears"),
            new HatDef("Crown", "Crown"),
            new HatDef("HiphopCap", "Hip Hop Cap", hideEars: true),
            new HatDef("WizardHat", "Wizard Hat", hideEars: true),
            new HatDef("UnicornHeadband", "Unicorn Horn"),
            new HatDef("FedoraHat", "Fedora"),
            new HatDef("BaseballHelmet", "Baseball Helmet", hideEars: true),
            new HatDef("DeerStalker", "Deerstalker", hideEars: true),
            new HatDef("AlienHeadband", "Alien Antennae"),
            new HatDef("MusketeerHat", "Musketeer Hat", hideEars: true),
            new HatDef("SafetyHelmet", "Safety Helmet", hideEars: true),
            new HatDef("WoolBeretHat", "Wool Beret"),
            new HatDef("BandanaHat", "Bandana"),
            new HatDef("VikingHelmet", "Viking Helmet", hideEars: true),
            new HatDef("LadyHeadband", "Lady Headband"),
            new HatDef("FBICap", "FBI Cap", hideEars: true),
            new HatDef("KungfuHeadband", "Kung Fu Headband"),
            new HatDef("RamHorn", "Ram Horns"),
            new HatDef("FireFighterHelmet", "Firefighter Helmet", hideEars: true),
            new HatDef("GoldLaurelCrown", "Gold Laurels"),
            new HatDef("DeerHorn", "Antlers"),
            new HatDef("MotorcycleHelmet", "Motorcycle Helmet", hideEars: true),
            new HatDef("BandannaHeaddress", "Bandanna Wrap", hideEars: true),
            new HatDef("SergeantHat", "Sergeant Hat", hideEars: true),
            new HatDef("AntelopeHorn", "Antelope Horns", hideEars: true),
            new HatDef("RomanHelmet", "Roman Helmet", hideEars: true),
            new HatDef("VintageMotorcycleHelmet", "Vintage Moto Helmet", hideEars: true),
            new HatDef("SoldierHelmet", "Soldier Helmet", hideEars: true),

            // Seasonal, in calendar order
            new HatDef("HeartHeadband", "Heart Headband", "Valentine's Day", "Valentine's", 2, 11, 2, 17),
            new HatDef("JesterHat", "Jester Hat", "April Fools", "April Fools", 3, 29, 4, 4, hideEars: true),
            new HatDef("BunnyHeadband", "Bunny Ears", "Easter", "Easter", easterWindow: true),
            new HatDef("PinkBonnetHat", "Easter Bonnet", "Easter", "Easter", easterWindow: true, hideEars: true),
            new HatDef("FlowerHat", "Flower Hat", "Earth Day", "Earth Day", 4, 19, 4, 25),
            new HatDef("MexicanMusicianHat", "Mariachi Hat", "Cinco de Mayo", "Cinco de Mayo", 5, 2, 5, 8, hideEars: true),
            new HatDef("WatermelonHelmet", "Watermelon Helmet", "the Summer Solstice", "Solstice", 6, 17, 6, 23, hideEars: true),
            new HatDef("Cake", "Pride Cake", "Pride", "Pride", 6, 24, 6, 30),
            new HatDef("UncleSamHat", "Uncle Sam Hat", "the 4th of July", "July 4th", 7, 1, 7, 7, hideEars: true),
            new HatDef("PartyHat", "Party Hat", "Caicos's Birthday", "Birthday", 8, 1, 8, 10),
            new HatDef("PirateHat", "Pirate Hat", "Pirate Day", "Pirate Day", 9, 16, 9, 22, hideEars: true),
            new HatDef("WitchHat", "Witch Hat", "Halloween", "Halloween", 10, 26, 11, 1, hideEars: true),
            new HatDef("SatanHorn2", "Devil Horns", "Halloween", "Halloween", 10, 26, 11, 1, hideEars: true),
            new HatDef("ElfHat", "Elf Hat", "Christmas", "Christmas", 12, 22, 12, 28, hideEars: true),
        };

        private const string UnlockedPrefKey = "Hats.Unlocked"; // CSV of hat ids, oldest unlock first
        private const string SelectedPrefKey = "Hats.Selected"; // worn hat id, "" = no hat
        private const string SeenCountPrefKey = "Hats.SeenCount"; // unlock count when the selector last showed them

        private static string[] hatIds; // lazily derived from Defs
        private static List<string> unlockedOrder; // lazily loaded
        private static int stateVersion;
        private static bool spawnedThisRun;

        // Statics survive play sessions when domain reload is disabled - reset explicitly
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            hatIds = null;
            unlockedOrder = null;
            stateVersion = 0;
            spawnedThisRun = false;
            DebugSpawnChanceOverride = null;
            DebugForceAllSeasonalActive = false;
        }

        /// <summary>Catalog ids in carousel/unlock order (derived from Defs).</summary>
        public static string[] HatIds
        {
            get
            {
                if (hatIds == null)
                {
                    hatIds = new string[Defs.Length];
                    for (int i = 0; i < Defs.Length; i++)
                        hatIds[i] = Defs[i].Id;
                }
                return hatIds;
            }
        }

        public static int TotalCount => Defs.Length;
        public static int UnlockedCount => GetUnlockedOrder().Count;

        /// <summary>Bumped on every unlock/selection change - cheap dirty-check for the selector UI.</summary>
        public static int StateVersion => stateVersion;

        public static HatDef GetDef(string hatId)
        {
            if (string.IsNullOrEmpty(hatId))
                return null;
            foreach (HatDef def in Defs)
            {
                if (def.Id == hatId)
                    return def;
            }
            return null;
        }

        /// <summary>
        /// Whether wearing this hat should collapse the ear bones. Prefer this
        /// over reading HatDef.HideEars directly - in the editor, the Hat Fit
        /// Tuner overlays saved-but-not-yet-compiled choices so re-equips honor
        /// them for the rest of the play session.
        /// </summary>
        public static bool HideEarsFor(string hatId)
        {
            HatDef def = GetDef(hatId);
            bool hide = def != null && def.HideEars;
#if UNITY_EDITOR
            if (hatId != null && hideEarsOverrides.TryGetValue(hatId, out bool overridden))
                hide = overridden;
#endif
            return hide;
        }

#if UNITY_EDITOR
        // Session-only overlay for the Hat Fit Tuner: its catalog source edit
        // is deferred to play-mode exit (an immediate edit would recompile and
        // wipe the session), so saved choices live here until then. Cleared
        // naturally by the domain reload that applies the real catalog values.
        private static readonly Dictionary<string, bool> hideEarsOverrides = new Dictionary<string, bool>();

        public static void SetHideEarsOverride(string hatId, bool hide)
        {
            if (!string.IsNullOrEmpty(hatId))
                hideEarsOverrides[hatId] = hide;
        }
#endif

        // ------------------------------------------------------------------
        // Seasonal calendar
        // ------------------------------------------------------------------

        /// <summary>True while this hat's holiday window is open (regular hats are always "in season").</summary>
        public static bool IsInSeason(HatDef def)
        {
            if (def == null)
                return false;
            if (!def.IsSeasonal)
                return true;
            if (DebugForceAllSeasonalActive)
                return true;

            DateTime today = DateTime.Now.Date;
            if (def.EasterWindow)
            {
                DateTime easter = EasterSunday(today.Year);
                return today >= easter.AddDays(-3) && today <= easter.AddDays(3);
            }

            // Month/day tuple compare; start > end means the window wraps New Year
            int day = today.Month * 100 + today.Day;
            int start = def.StartMonth * 100 + def.StartDay;
            int end = def.EndMonth * 100 + def.EndDay;
            return start <= end ? day >= start && day <= end : day >= start || day <= end;
        }

        /// <summary>Last calendar day of the hat's current/next window ("Until August 10" on the banner).</summary>
        public static DateTime SeasonEndDate(HatDef def)
        {
            DateTime today = DateTime.Now.Date;
            if (def.EasterWindow)
                return EasterSunday(today.Year).AddDays(3);

            int year = today.Year;
            // A wrap window's January tail belongs to the year after its start
            if (today.Month * 100 + today.Day > def.EndMonth * 100 + def.EndDay)
                year++;
            return new DateTime(year, def.EndMonth, def.EndDay);
        }

        /// <summary>"August 10" - locale-stable banner date.</summary>
        public static string FormatSeasonEnd(DateTime date)
        {
            return date.ToString("MMMM d", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// The seasonal hat the home screen should advertise: the first locked
        /// hat whose window is open right now. False once it's unlocked (the
        /// banner's job is done) or when no window is open.
        /// </summary>
        public static bool TryGetActiveSeasonal(out HatDef def, out DateTime windowEnd)
        {
            var order = GetUnlockedOrder();
            foreach (HatDef candidate in Defs)
            {
                if (candidate.IsSeasonal && IsInSeason(candidate) && !order.Contains(candidate.Id))
                {
                    def = candidate;
                    windowEnd = SeasonEndDate(candidate);
                    return true;
                }
            }

            def = null;
            windowEnd = default;
            return false;
        }

        /// <summary>Anonymous Gregorian computus - Easter Sunday of the given year.</summary>
        private static DateTime EasterSunday(int year)
        {
            int a = year % 19;
            int b = year / 100, c = year % 100;
            int d = b / 4, e = b % 4;
            int f = (b + 8) / 25;
            int g = (b - f + 1) / 3;
            int h = (19 * a + b - d - g + 15) % 30;
            int i = c / 4, k = c % 4;
            int l = (32 + 2 * e + 2 * i - h - k) % 7;
            int m = (a + 11 * h + 22 * l) / 451;
            int month = (h + l - 7 * m + 114) / 31;
            int day = ((h + l - 7 * m + 114) % 31) + 1;
            return new DateTime(year, month, day);
        }

        // ------------------------------------------------------------------
        // Spawn rolls
        // ------------------------------------------------------------------

        /// <summary>
        /// The hat the next pickup unlocks: an open-window locked seasonal hat
        /// takes the slot (its run is limited), otherwise the next locked
        /// regular hat in catalog order. Null when nothing droppable remains -
        /// off-season seasonal hats never drop.
        /// </summary>
        public static string NextDropId
        {
            get
            {
                var order = GetUnlockedOrder();

                foreach (HatDef def in Defs)
                {
                    if (def.IsSeasonal && IsInSeason(def) && !order.Contains(def.Id))
                        return def.Id;
                }

                foreach (HatDef def in Defs)
                {
                    if (!def.IsSeasonal && !order.Contains(def.Id))
                        return def.Id;
                }

                return null;
            }
        }

        /// <summary>Roll whether a large coin should spawn as a hat pickup instead.</summary>
        public static bool RollMegaCoinReplace()
        {
            string target = NextDropId;
            if (target == null)
                return false;

            // At most one hat pickup per run: a second would preview the same
            // unlock (or go stale once the first is collected). The debug
            // override bypasses the cap so pickups stay easy to test.
            if (spawnedThisRun && DebugSpawnChanceOverride == null)
                return false;

            float chance = DebugSpawnChanceOverride ?? ChanceFor(target);
            return UnityEngine.Random.value < chance;
        }

        /// <summary>The live spawn chance for a hat id (seasonal windows get the boosted rate).</summary>
        public static float ChanceFor(string hatId)
        {
            HatDef def = GetDef(hatId);
            bool seasonal = def != null && def.IsSeasonal;
            return seasonal ? SeasonalChance : MegaCoinReplaceChance;
        }

        /// <summary>Spawner calls this when a hat pickup actually spawns.</summary>
        public static void MarkSpawnedThisRun()
        {
            spawnedThisRun = true;
        }

        /// <summary>Call at the start of a new run (retries keep the flag - same run).</summary>
        public static void ResetRunSpawn()
        {
            spawnedThisRun = false;
        }

        // ------------------------------------------------------------------
        // Unlocks & selection
        // ------------------------------------------------------------------

        public static bool IsUnlocked(string hatId)
        {
            return GetUnlockedOrder().Contains(hatId);
        }

        /// <summary>
        /// True when this hat was unlocked after the player last browsed the
        /// selector (drives the NEW badge, like the love notes grid).
        /// </summary>
        public static bool IsHatUnseen(string hatId)
        {
            int unlockPosition = GetUnlockedOrder().IndexOf(hatId);
            if (unlockPosition < 0)
                return false;
            return unlockPosition >= PlayerPrefs.GetInt(SeenCountPrefKey, 0);
        }

        /// <summary>Most recently unlocked hat that hasn't been seen in the selector, or null.</summary>
        public static string NewestUnseenId
        {
            get
            {
                var order = GetUnlockedOrder();
                if (order.Count == 0)
                    return null;
                string newest = order[order.Count - 1];
                return IsHatUnseen(newest) ? newest : null;
            }
        }

        /// <summary>Clears the NEW badges - the selector calls this when the player browses the carousel.</summary>
        public static void MarkAllSeen()
        {
            PlayerPrefs.SetInt(SeenCountPrefKey, UnlockedCount);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// Unlocks NextDropId and persists it, so the pickup's floating
        /// preview model always matches what you get. Returns false when
        /// nothing droppable remains.
        /// </summary>
        public static bool TryUnlockNext(out string hatId)
        {
            hatId = NextDropId;
            if (hatId == null)
                return false;

            GetUnlockedOrder().Add(hatId);
            SaveUnlocked();
            stateVersion++;
            GameLog.Info($"[HatManager] Unlocked hat '{hatId}' ({UnlockedCount}/{TotalCount}).");
            return true;
        }

        /// <summary>
        /// The hat the dog wears, "" for none. Persisted on set; a locked or
        /// unknown id clears to none.
        /// </summary>
        public static string SelectedId
        {
            get
            {
                string id = PlayerPrefs.GetString(SelectedPrefKey, "");
                return id.Length > 0 && IsUnlocked(id) ? id : "";
            }
            set
            {
                string id = !string.IsNullOrEmpty(value) && IsUnlocked(value) ? value : "";
                PlayerPrefs.SetString(SelectedPrefKey, id);
                PlayerPrefs.Save();
                stateVersion++;
            }
        }

        /// <summary>The hat's prefab, loaded on demand from Resources. Null for "" or a missing asset.</summary>
        public static GameObject LoadHatPrefab(string hatId)
        {
            if (string.IsNullOrEmpty(hatId))
                return null;
            return Resources.Load<GameObject>("Hats/" + hatId);
        }

        /// <summary>Debug: unlocks everything (seasonal included).</summary>
        public static void UnlockAll()
        {
            var order = GetUnlockedOrder();
            foreach (HatDef def in Defs)
            {
                if (!order.Contains(def.Id))
                    order.Add(def.Id);
            }
            SaveUnlocked();
            stateVersion++;
            GameLog.Info("[HatManager] All hats unlocked.");
        }

        /// <summary>Debug: clears every unlock and the worn hat (the default hats come straight back).</summary>
        public static void ClearAllProgress()
        {
            unlockedOrder = null; // force a fresh load, which re-seeds the defaults
            PlayerPrefs.DeleteKey(UnlockedPrefKey);
            PlayerPrefs.DeleteKey(SelectedPrefKey);
            PlayerPrefs.DeleteKey(SeenCountPrefKey);
            PlayerPrefs.Save();
            GetUnlockedOrder();
            stateVersion++;
            GameLog.Info("[HatManager] Hat progress reset to the starter hats.");
        }

        private static List<string> GetUnlockedOrder()
        {
            if (unlockedOrder != null)
                return unlockedOrder;

            unlockedOrder = new List<string>();
            string csv = PlayerPrefs.GetString(UnlockedPrefKey, "");
            if (!string.IsNullOrEmpty(csv))
            {
                foreach (string entry in csv.Split(','))
                {
                    // Ignore corrupt entries and ids no longer in the catalog
                    string id = entry.Trim();
                    if (id.Length > 0 &&
                        Array.IndexOf(HatIds, id) >= 0 &&
                        !unlockedOrder.Contains(id))
                    {
                        unlockedOrder.Add(id);
                    }
                }
            }

            EnsureDefaultUnlocks();
            return unlockedOrder;
        }

        /// <summary>
        /// Seeds the starter hats into any save missing them. They join the
        /// FRONT of the unlock order (they came with the game) and count as
        /// already seen, so they never trigger the NEW-badge/auto-wear flow.
        /// </summary>
        private static void EnsureDefaultUnlocks()
        {
            int injected = 0;
            for (int i = DefaultUnlockedIds.Length - 1; i >= 0; i--)
            {
                string id = DefaultUnlockedIds[i];
                if (Array.IndexOf(HatIds, id) >= 0 && !unlockedOrder.Contains(id))
                {
                    unlockedOrder.Insert(0, id);
                    injected++;
                }
            }

            if (injected == 0)
                return;

            // Front-insertion shifts every unlock position up - move the seen
            // watermark with them so old badges stay seen and the defaults
            // arrive pre-seen.
            PlayerPrefs.SetInt(SeenCountPrefKey, PlayerPrefs.GetInt(SeenCountPrefKey, 0) + injected);
            SaveUnlocked();
        }

        private static void SaveUnlocked()
        {
            PlayerPrefs.SetString(UnlockedPrefKey, string.Join(",", unlockedOrder));
            PlayerPrefs.Save();
        }
    }
}
